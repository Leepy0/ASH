using System.Net;
using System.Text.Json;
using ASH.Server;

namespace ASH.Server.Tests;

public class AdminFsTests
{
    static Task<HttpResponseMessage> Get(SettingsEnv e, string url, string? token = SettingsEnv.Token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (token != null) req.Headers.Add(ReleaseStore.HeaderName, token);
        return e.Http.SendAsync(req);
    }

    static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    [Fact] public async Task Needs_Admin_Login()
    {
        using var e = new SettingsEnv();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(e, "/admin/fs", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(e, "/admin/fs?path=" + Uri.EscapeDataString(e.Dir), "wrong")).StatusCode);
        await e.Login(); // the browser way: session cookie
        Assert.Equal(HttpStatusCode.OK, (await Get(e, "/admin/fs", null)).StatusCode);
    }

    [Fact] public async Task Disabled_Admin_Is_Forbidden()
    {
        using var e = new SettingsEnv("bad"); // Admin:Password breaks the rule: the admin pages are off
        Assert.Equal(HttpStatusCode.Forbidden, (await Get(e, "/admin/fs", "bad")).StatusCode);
    }

    [Fact] public async Task Empty_Path_Lists_Drives()
    {
        using var e = new SettingsEnv();
        var j = await Json(await Get(e, "/admin/fs"));
        Assert.Equal("", j.GetProperty("path").GetString());
        Assert.NotEmpty(j.GetProperty("dirs").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, j.GetProperty("parent").ValueKind);
    }

    [Fact] public async Task Lists_Folders_And_Only_With_files_Also_Files()
    {
        using var e = new SettingsEnv();
        var root = Path.Combine(e.Dir, "browse");
        Directory.CreateDirectory(Path.Combine(root, "b-sub"));
        Directory.CreateDirectory(Path.Combine(root, "a-sub"));
        File.WriteAllText(Path.Combine(root, "tool.exe"), "SECRET CONTENT");

        var raw = await (await Get(e, "/admin/fs?path=" + Uri.EscapeDataString(root))).Content.ReadAsStringAsync();
        var j = JsonDocument.Parse(raw).RootElement;
        Assert.Equal(root, j.GetProperty("path").GetString());
        Assert.Equal(e.Dir, j.GetProperty("parent").GetString());
        Assert.Equal(new[] { "a-sub", "b-sub" }, j.GetProperty("dirs").EnumerateArray().Select(x => x.GetProperty("name").GetString()));
        Assert.Equal(Path.Combine(root, "a-sub"), j.GetProperty("dirs")[0].GetProperty("path").GetString());
        Assert.Empty(j.GetProperty("files").EnumerateArray());
        Assert.DoesNotContain("SECRET", raw);

        var withFiles = await Json(await Get(e, "/admin/fs?files=1&path=" + Uri.EscapeDataString(root)));
        Assert.Equal("tool.exe", Assert.Single(withFiles.GetProperty("files").EnumerateArray()).GetProperty("name").GetString());
    }

    [Fact] public async Task A_File_Path_Opens_Its_Folder_And_Bad_Paths_Are_Rejected()
    {
        using var e = new SettingsEnv();
        var f = Path.Combine(e.Dir, "appsettings.json");
        var j = await Json(await Get(e, "/admin/fs?files=1&path=" + Uri.EscapeDataString(f)));
        Assert.Equal(e.Dir, j.GetProperty("path").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await Get(e, "/admin/fs?path=" + Uri.EscapeDataString(Path.Combine(e.Dir, "nope")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Get(e, "/admin/fs?path=relative%2Ffolder")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Get(e, "/admin/fs?path=C%3A%5Cbad%00name")).StatusCode);
    }

    [Fact] public async Task Settings_Page_Offers_Browse_Buttons_For_Path_Settings_Only()
    {
        using var e = new SettingsEnv();
        await e.Login();
        var html = await e.Http.GetStringAsync("/admin/settings?tab=server");
        foreach (var key in new[] { "Jobs-AmHome", "Jobs-Dir", "Film-FfmpegPath", "Film-AviewExe", "Releases-Dir" })
            Assert.Contains($"data-for=\"f-{key}\"", html);
        Assert.Equal(5, System.Text.RegularExpressions.Regex.Matches(html, "data-pick=").Count);
        Assert.Contains("data-pick=\"file\" data-for=\"f-Film-FfmpegPath\"", html);
        Assert.Contains("data-pick=\"dir\" data-for=\"f-Jobs-AmHome\"", html);
    }
}
