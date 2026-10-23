using System.Net;
using System.Text.Json.Nodes;
using ASH.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ASH.Server.Tests;

/// <summary>Server whose settings page edits a temp appsettings.json (never the real one next to the exe).</summary>
public sealed class SettingsEnv : IDisposable
{
    public const string Token = Env.Token;
    public const string Original = """
        {
          // a comment people may have written
          "Urls": "http://0.0.0.0:8082",
          "Admin": { "Token": "secret-in-file-1234567890" },
          "Jobs": { "Parallel": 2, "AmHome": "C:\\AutoMod\\12_6", },
          "Custom": { "Keep": [1, 2, 3], "Name": "그대로" }
        }
        """;
    public readonly string Dir = Path.Combine(Path.GetTempPath(), "ash-settings-test-" + Guid.NewGuid().ToString("N"));
    public string File => Path.Combine(Dir, "appsettings.json");
    public readonly WebApplicationFactory<Program> Factory;
    public readonly HttpClient Http;

    public SettingsEnv(string token = Token)
    {
        Directory.CreateDirectory(Dir);
        System.IO.File.WriteAllText(File, Original);
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Releases:Dir", Path.Combine(Dir, "releases"));
            b.UseSetting("Jobs:Dir", Path.Combine(Dir, "jobs"));
            b.UseSetting("Jobs:WorkerEnabled", "false");
            b.UseSetting("Admin:Token", token);
            b.UseSetting("Update:PublicKeyPem", "");
            b.UseSetting("GitHub:Enabled", "false");
            b.ConfigureTestServices(s => s.AddSingleton(new AppSettingsFile(File)));
        });
        Http = Factory.CreateClient();
    }

    public static FormUrlEncodedContent Form(params (string, string)[] kv) => new(kv.Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2)));

    public Task<HttpResponseMessage> Post(string path, HttpContent? body = null, string? origin = "http://localhost", string? token = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = body ?? Form() };
        if (origin != null) req.Headers.Add("Origin", origin);
        if (token != null) req.Headers.Add(ReleaseStore.HeaderName, token);
        return Http.SendAsync(req);
    }

    public async Task Login() => Assert.Equal(HttpStatusCode.OK, (await Post("/admin/login", Form(("token", Token)))).StatusCode);

    public void Dispose()
    {
        Http.Dispose();
        Factory.Dispose();
        try { Directory.Delete(Dir, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

public class SettingsTests
{
    [Fact] public async Task Admin_Jobs_Is_Gated_And_Old_Jobs_Url_Redirects()
    {
        using var e = new SettingsEnv();
        var noRedirect = e.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var r = await noRedirect.GetAsync("/jobs");
        Assert.Equal(HttpStatusCode.MovedPermanently, r.StatusCode); Assert.Equal("/admin/jobs", r.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await e.Http.GetAsync("/admin/jobs")).StatusCode);
        var req = new HttpRequestMessage(HttpMethod.Get, "/admin/jobs"); req.Headers.Add("X-ASH-Admin-Token", SettingsEnv.Token);
        Assert.Contains("작업 대기열", await (await e.Http.SendAsync(req)).Content.ReadAsStringAsync());
        var settings = new HttpRequestMessage(HttpMethod.Get, "/admin/settings"); settings.Headers.Add("X-ASH-Admin-Token", SettingsEnv.Token);
        var page = await (await e.Http.SendAsync(settings)).Content.ReadAsStringAsync();
        foreach (var s in new[] { "서버 현황", "배포", "서버 상태", "href=\"/admin/jobs\"" }) Assert.Contains(s, page);
    }

    [Fact]
    public async Task Settings_page_asks_for_login_and_never_shows_the_token()
    {
        using var e = new SettingsEnv();
        var r = await e.Http.GetAsync("/admin/settings");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        var page = await r.Content.ReadAsStringAsync();
        Assert.Contains("관리자 토큰", page);
        Assert.DoesNotContain("Jobs:Parallel", page);

        await e.Login();
        page = await e.Http.GetStringAsync("/admin/settings");
        Assert.Contains("Jobs:Parallel", page);
        Assert.Contains("동시 실행 수", page);
        Assert.DoesNotContain(SettingsEnv.Token, page);
        Assert.DoesNotContain("secret-in-file", page); // Admin:Token from the file is not shown either
    }

    [Fact]
    public async Task Login_cookie_is_HttpOnly_and_SameSite_Strict_and_wrong_token_is_401()
    {
        using var e = new SettingsEnv();
        Assert.Equal(HttpStatusCode.Unauthorized, (await e.Post("/admin/login", SettingsEnv.Form(("token", "wrong-token-123456789")))).StatusCode);
        using var raw = e.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var req = new HttpRequestMessage(HttpMethod.Post, "/admin/login") { Content = SettingsEnv.Form(("token", SettingsEnv.Token)) };
        req.Headers.Add("Origin", "http://localhost");
        var r = await raw.SendAsync(req);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var cookie = Assert.Single(r.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith(AdminSessions.CookieName + "=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SettingsEnv.Token, cookie);
    }

    [Fact]
    public async Task Save_round_trip_keeps_unknown_keys_and_makes_a_backup()
    {
        using var e = new SettingsEnv();
        await e.Login();
        var r = await e.Post("/admin/settings", SettingsEnv.Form(("Jobs:Parallel", "4"), ("Jobs:WorkerEnabled", "false"), ("Jobs:RunTimeoutMin", "30"), ("Admin:Token", "attacker-token-123456")));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var page = await r.Content.ReadAsStringAsync();
        Assert.Contains("저장했습니다(2개)", page);       // RunTimeoutMin = default and missing: not written
        Assert.Contains("다시 시작해야 적용: 작업 실행", page); // WorkerEnabled is read once at start
        Assert.Contains("바로 적용: 동시 실행 수", page);

        var json = JsonNode.Parse(File.ReadAllText(e.File))!.AsObject();
        Assert.Equal(4, json["Jobs"]!["Parallel"]!.GetValue<int>());
        Assert.False(json["Jobs"]!["WorkerEnabled"]!.GetValue<bool>());
        Assert.Null(json["Jobs"]!["RunTimeoutMin"]);
        Assert.Equal(@"C:\AutoMod\12_6", json["Jobs"]!["AmHome"]!.GetValue<string>());
        Assert.Equal("secret-in-file-1234567890", json["Admin"]!["Token"]!.GetValue<string>()); // never editable from the web
        Assert.Equal("그대로", json["Custom"]!["Name"]!.GetValue<string>());
        Assert.Equal(3, json["Custom"]!["Keep"]!.AsArray().Count);
        Assert.Equal("http://0.0.0.0:8082", json["Urls"]!.GetValue<string>());

        var backup = Assert.Single(Directory.GetFiles(e.Dir, "appsettings.json.*.bak"));
        Assert.Equal(SettingsEnv.Original, File.ReadAllText(backup));
        Assert.False(File.Exists(e.File + ".tmp"));
    }

    [Theory]
    [InlineData("Jobs:Parallel", "0")]
    [InlineData("Jobs:Parallel", "abc")]
    [InlineData("Jobs:KeepRunsDays", "-1")]
    [InlineData("Jobs:WorkerEnabled", "maybe")]
    [InlineData("Urls", "ftp://0.0.0.0:21")]
    [InlineData("GitHub:BaseUrl", "http://example.com/x")]
    [InlineData("GitHub:Dns", "not-an-ip")]
    [InlineData("GitHub:Proxy", "proxy:8080")]
    public async Task Invalid_value_is_400_and_nothing_written(string key, string value)
    {
        using var e = new SettingsEnv();
        await e.Login();
        var r = await e.Post("/admin/settings", SettingsEnv.Form(("Jobs:Parallel", "3"), (key, value)));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("저장하지 않았습니다", await r.Content.ReadAsStringAsync());
        Assert.Equal(SettingsEnv.Original, File.ReadAllText(e.File));
        Assert.Empty(Directory.GetFiles(e.Dir, "*.bak"));
    }

    [Fact]
    public async Task Save_needs_auth_and_same_origin()
    {
        using var e = new SettingsEnv();
        var body = SettingsEnv.Form(("Jobs:Parallel", "5"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await e.Post("/admin/settings", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await e.Post("/admin/settings", body, token: "wrong-token-123456789")).StatusCode);
        await e.Login();
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Post("/admin/settings", body, origin: "http://evil.example")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Post("/admin/settings", body, origin: null)).StatusCode); // no Origin, no Referer
        Assert.Equal(SettingsEnv.Original, File.ReadAllText(e.File));
        // the header token works without a session (scripts)
        Assert.Equal(HttpStatusCode.OK, (await e.Post("/admin/settings", body, origin: null, token: SettingsEnv.Token)).StatusCode);
        Assert.Equal(5, JsonNode.Parse(File.ReadAllText(e.File))!["Jobs"]!["Parallel"]!.GetValue<int>());
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        using var e = new SettingsEnv();
        await e.Login();
        Assert.Equal(HttpStatusCode.OK, (await e.Http.GetAsync("/admin/settings")).StatusCode);
        await e.Post("/admin/logout");
        Assert.Equal(HttpStatusCode.Unauthorized, (await e.Http.GetAsync("/admin/settings")).StatusCode);
    }

    [Fact]
    public async Task Restart_is_refused_without_auth_and_outside_a_service()
    {
        using var e = new SettingsEnv();
        Assert.Equal(HttpStatusCode.Unauthorized, (await e.Post("/admin/restart")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await e.Post("/admin/restart", token: "wrong-token-123456789")).StatusCode);
        await e.Login();
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Post("/admin/restart", origin: "http://evil.example")).StatusCode);
        var r = await e.Post("/admin/restart", SettingsEnv.Form(("confirm", "1")));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); // the test host is not the Windows service: nothing is restarted
        Assert.Contains("Windows 서비스로 실행 중이 아님", await r.Content.ReadAsStringAsync());
        Assert.Contains("Windows 서비스로 실행 중이 아님", await e.Http.GetStringAsync("/admin/settings"));
    }

    [Fact]
    public async Task Without_admin_token_settings_and_restart_are_off()
    {
        using var e = new SettingsEnv(token: "");
        var r = await e.Http.GetAsync("/admin/settings");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Contains("Admin:Token", await r.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Post("/admin/login", SettingsEnv.Form(("token", "")))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Post("/admin/settings", SettingsEnv.Form(("Jobs:Parallel", "5")), token: "")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Post("/admin/restart", token: "")).StatusCode);
        Assert.Equal(SettingsEnv.Original, File.ReadAllText(e.File));
    }

    [Fact]
    public async Task Missing_AutoMod_folder_is_a_warning_not_an_error()
    {
        using var e = new SettingsEnv();
        await e.Login();
        var missing = Path.Combine(e.Dir, "no-automod");
        var r = await e.Post("/admin/settings", SettingsEnv.Form(("Jobs:AmHome", missing)));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("AutoMod 빌드 exe가 없습니다", await r.Content.ReadAsStringAsync());
        Assert.Equal(missing, JsonNode.Parse(File.ReadAllText(e.File))!["Jobs"]!["AmHome"]!.GetValue<string>());
    }

    [Fact]
    public async Task Settings_page_links_nav_and_shows_status_sections()
    {
        using var e = new SettingsEnv();
        var req = new HttpRequestMessage(HttpMethod.Get, "/admin/settings"); req.Headers.Add("X-ASH-Admin-Token", SettingsEnv.Token);
        var page = await (await e.Http.SendAsync(req)).Content.ReadAsStringAsync();
        foreach (var s in new[] { "href=\"/analyses\"", "href=\"/admin/settings\"", "지금 실행", "name=\"viewport\"" })
            Assert.Contains(s, page);
    }

    [Fact]
    public void Every_setting_default_passes_its_own_validation()
    {
        foreach (var d in ServerSettings.All)
            Assert.True(ServerSettings.Validate(d, d.Default, out _) == null, d.Key);
    }
}
