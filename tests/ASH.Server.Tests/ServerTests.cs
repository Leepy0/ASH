using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using ASH.Core.Update;
using ASH.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ASH.Server.Tests;

/// <summary>Server over a temp release folder. key=true: a fresh P-256 key is injected via Update:PublicKeyPem, otherwise "no key" (empty override).</summary>
public sealed class Env : IDisposable
{
    public const string Token = "test-token-1234567890";
    public readonly string Dir = Path.Combine(Path.GetTempPath(), "ash-server-test-" + Guid.NewGuid().ToString("N"));
    public readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public readonly WebApplicationFactory<Program> Factory;
    public readonly HttpClient Http;
    public ReleaseStore Store => Factory.Services.GetRequiredService<ReleaseStore>();

    public Env(bool key = true, string token = Token, bool allowUnsigned = false)
    {
        var pem = key ? Key.ExportSubjectPublicKeyInfoPem() : "";
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Releases:Dir", Dir);
            b.UseSetting("Admin:Token", token);
            b.UseSetting("Update:PublicKeyPem", pem);
            b.UseSetting("Update:AllowUnsigned", allowUnsigned ? "true" : "false");
            b.UseSetting("GitHub:Enabled", "false");
        });
        Http = Factory.CreateClient();
    }

    public sealed record Release(byte[] Json, byte[] Sig, byte[] Setup, string File, string Version);

    public Release MakeRelease(string version, string? setupText = null, bool sign = true, string? published = null, string min = "0.1.0")
    {
        var setup = Encoding.UTF8.GetBytes(setupText ?? "setup-bytes-" + version);
        var m = new ReleaseManifest
        {
            Version = version, MinSupported = min, File = $"ASH-Setup-{version}.exe",
            Sha256 = Convert.ToHexString(SHA256.HashData(setup)).ToLowerInvariant(), Size = setup.Length,
            Published = published ?? ReleaseManifest.FormatPublished(DateTime.UtcNow),
        };
        var json = m.ToJson();
        var sig = sign ? Key.SignData(json, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence) : Array.Empty<byte>();
        return new Release(json, sig, setup, m.File, version);
    }

    public Task<HttpResponseMessage> Upload(Release r, string? token = Token, string query = "", bool withSig = true, string? setupName = null, byte[]? setup = null, byte[]? sig = null, bool withSetup = true)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(r.Json), "manifest", "ASH.json");
        if (withSig && (sig ?? r.Sig).Length > 0) form.Add(new ByteArrayContent(sig ?? r.Sig), "signature", "ASH.json.sig");
        if (withSetup) form.Add(new ByteArrayContent(setup ?? r.Setup), "setup", setupName ?? r.File);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/releases" + query) { Content = form };
        if (token != null) req.Headers.Add("X-ASH-Admin-Token", token);
        return Http.SendAsync(req);
    }

    public async Task Published(Release r) => Assert.Equal(HttpStatusCode.OK, (await Upload(r)).StatusCode);
    public Task<byte[]> Get(string path) => Http.GetByteArrayAsync(path);

    public void Dispose()
    {
        Http.Dispose();
        Factory.Dispose();
        Key.Dispose();
        try { Directory.Delete(Dir, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

public class ServerTests
{
    [Fact]
    public async Task Upload_serves_exact_bytes()
    {
        using var e = new Env();
        var r = e.MakeRelease("0.2.0");
        var resp = await e.Upload(r);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var v = await e.Http.GetAsync("/api/ash/version");
        Assert.Equal("application/json", v.Content.Headers.ContentType?.MediaType);
        Assert.Equal(r.Json, await v.Content.ReadAsByteArrayAsync());
        var s = await e.Http.GetAsync("/api/ash/version.sig");
        Assert.Equal("application/octet-stream", s.Content.Headers.ContentType?.MediaType);
        Assert.Equal(r.Sig, await s.Content.ReadAsByteArrayAsync());
        Assert.Equal(r.Setup, await e.Get("/download/" + r.File));
        Assert.Empty(Directory.GetDirectories(Path.Combine(e.Dir, "incoming")));
    }

    [Fact]
    public async Task Empty_server_answers_404()
    {
        using var e = new Env();
        Assert.Equal(HttpStatusCode.NotFound, (await e.Http.GetAsync("/api/ash/version")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await e.Http.GetAsync("/api/ash/version.sig")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await e.Http.GetAsync("/download/ASH-Setup-9.9.9.exe")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    public async Task Bad_token_is_401_and_nothing_published(string? token)
    {
        using var e = new Env();
        Assert.Equal(HttpStatusCode.Unauthorized, (await e.Upload(e.MakeRelease("0.2.0"), token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await e.Http.GetAsync("/api/ash/version")).StatusCode);
    }

    [Fact]
    public async Task Empty_admin_token_disables_uploads_with_403()
    {
        using var e = new Env(token: "");
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Upload(e.MakeRelease("0.2.0"), "")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Upload(e.MakeRelease("0.2.0"), null)).StatusCode);
        Assert.Contains("꺼짐", await e.Http.GetStringAsync("/"));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("0123456789abcde")] // 15 chars
    public async Task Short_admin_token_disables_uploads_with_403(string token)
    {
        using var e = new Env(token: token);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Upload(e.MakeRelease("0.2.0"), token)).StatusCode);
        Assert.Contains("꺼짐", await e.Http.GetStringAsync("/"));
    }

    [Fact]
    public async Task Bad_signature_is_400_and_current_unchanged()
    {
        using var e = new Env();
        var v1 = e.MakeRelease("0.2.0");
        await e.Published(v1);
        var v2 = e.MakeRelease("0.3.0");
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var forged = other.SignData(v2.Json, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Upload(v2, sig: forged)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Upload(v2, withSig: false)).StatusCode); // key set: signature is mandatory
        Assert.Equal(v1.Json, await e.Get("/api/ash/version"));
        Assert.Equal(v1.Sig, await e.Get("/api/ash/version.sig"));
        Assert.Equal(HttpStatusCode.NotFound, (await e.Http.GetAsync("/download/" + v2.File)).StatusCode);
    }

    [Fact]
    public async Task Hash_mismatch_is_400_and_current_unchanged()
    {
        using var e = new Env();
        var v1 = e.MakeRelease("0.2.0");
        await e.Published(v1);
        var v2 = e.MakeRelease("0.3.0");
        var resp = await e.Upload(v2, setup: Encoding.UTF8.GetBytes("setup-bytes-0.3.X")); // same length, other content
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("SHA-256", await resp.Content.ReadAsStringAsync());
        Assert.Equal(v1.Json, await e.Get("/api/ash/version"));
        Assert.Equal(HttpStatusCode.NotFound, (await e.Http.GetAsync("/download/" + v2.File)).StatusCode);
    }

    [Fact]
    public async Task Size_mismatch_and_file_name_mismatch_are_400()
    {
        using var e = new Env();
        var r = e.MakeRelease("0.2.0");
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Upload(r, setup: r.Setup.Append((byte)0).ToArray())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Upload(r, setupName: "ASH-Setup-0.2.1.exe")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await e.Http.GetAsync("/api/ash/version")).StatusCode);
    }

    [Fact]
    public async Task Missing_setup_part_is_400()
    {
        using var e = new Env();
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Upload(e.MakeRelease("0.2.0"), withSetup: false)).StatusCode);
    }

    [Fact]
    public async Task Lower_version_is_409_unless_forced()
    {
        using var e = new Env();
        var v2 = e.MakeRelease("0.3.0");
        var v1 = e.MakeRelease("0.2.0");
        await e.Published(v2);
        Assert.Equal(HttpStatusCode.Conflict, (await e.Upload(v1)).StatusCode);
        Assert.Equal(v2.Json, await e.Get("/api/ash/version"));

        Assert.Equal(HttpStatusCode.OK, (await e.Upload(v1, query: "?force=1")).StatusCode);
        Assert.Equal(v1.Json, await e.Get("/api/ash/version"));
        Assert.True(File.Exists(Path.Combine(e.Dir, "history", "0.3.0", "ASH.json"))); // previous current kept
        Assert.Contains("0.3.0", await e.Http.GetStringAsync("/"));
    }

    [Fact]
    public async Task Same_file_name_with_other_content_is_409()
    {
        using var e = new Env();
        var a = e.MakeRelease("0.2.0");
        await e.Published(a);
        var b = e.MakeRelease("0.2.0", setupText: "different content!"); // same file name, other hash
        Assert.Equal(HttpStatusCode.Conflict, (await e.Upload(b)).StatusCode);
        Assert.Equal(a.Setup, await e.Get("/download/" + a.File));
        Assert.Equal(HttpStatusCode.OK, (await e.Upload(a)).StatusCode); // identical re-upload is fine
    }

    [Theory]
    [InlineData("/download/..%2Fcurrent%2FASH.json")]
    [InlineData("/download/%2e%2e%2f%2e%2e%2fwindows%2fwin.ini")]
    [InlineData("/download/..%5Ccurrent%5CASH.json")]
    [InlineData("/download/ASH.json")]
    [InlineData("/download/ASH-Setup-1.0.0.txt")]
    [InlineData("/download/C:%5Cwindows%5Cwin.ini")]
    public async Task Download_rejects_traversal_and_foreign_names(string path)
    {
        using var e = new Env();
        await e.Published(e.MakeRelease("0.2.0"));
        var code = (await e.Http.GetAsync(path)).StatusCode;
        Assert.True(code is HttpStatusCode.BadRequest or HttpStatusCode.NotFound, code.ToString());
    }

    [Theory]
    [InlineData("validated")]
    [InlineData("moved")]
    public async Task Failure_mid_upload_leaves_current_and_files_unchanged(string stage)
    {
        using var e = new Env();
        var v1 = e.MakeRelease("0.2.0");
        await e.Published(v1);
        var v2 = e.MakeRelease("0.3.0");
        e.Store.Stage = s => { if (s == stage) throw new IOException("boom"); };
        var code = (await e.Upload(v2)).StatusCode;
        e.Store.Stage = null;
        Assert.Equal(HttpStatusCode.InternalServerError, code);
        Assert.Equal(v1.Json, await e.Get("/api/ash/version"));
        Assert.Equal(v1.Sig, await e.Get("/api/ash/version.sig"));
        Assert.Equal(new[] { v1.File }, Directory.GetFiles(Path.Combine(e.Dir, "files")).Select(Path.GetFileName));
        Assert.Empty(Directory.GetDirectories(Path.Combine(e.Dir, "incoming")));
        Assert.Equal(HttpStatusCode.OK, (await e.Upload(v2)).StatusCode); // and the retry works
    }

    [Fact]
    public async Task Failure_after_sig_swap_restores_previous_signature()
    {
        using var e = new Env();
        var v1 = e.MakeRelease("0.2.0");
        await e.Published(v1);
        e.Store.Stage = s => { if (s == "sig-swapped") throw new IOException("boom"); };
        var code = (await e.Upload(e.MakeRelease("0.3.0"))).StatusCode;
        e.Store.Stage = null;
        Assert.Equal(HttpStatusCode.InternalServerError, code);
        Assert.Equal(v1.Sig, await e.Get("/api/ash/version.sig")); // old manifest must not sit next to the new signature
        Assert.Equal(v1.Json, await e.Get("/api/ash/version"));
        var cur = ReleaseManifest.Parse(await e.Get("/api/ash/version"));
        Assert.True(e.Store.Key!.Verify(await e.Get("/api/ash/version"), await e.Get("/api/ash/version.sig")));
        Assert.Equal("0.2.0", cur.Version);
    }

    [Fact]
    public async Task Version_and_sig_are_no_store()
    {
        using var e = new Env();
        await e.Published(e.MakeRelease("0.2.0"));
        foreach (var path in new[] { "/api/ash/version", "/api/ash/version.sig" })
            Assert.Contains("no-store", (await e.Http.GetAsync(path)).Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Status_page_html_encodes_manifest_text()
    {
        using var e = new Env();
        await e.Published(e.MakeRelease("0.2.0", published: "<script>alert(1)</script>"));
        await e.Published(e.MakeRelease("0.3.0"));
        var page = await e.Http.GetStringAsync("/"); // 0.2.0 is in the history list
        Assert.DoesNotContain("<script>", page);
        Assert.Contains("&lt;script&gt;", page);
    }

    [Fact]
    public async Task Same_hash_upload_keeps_existing_file()
    {
        using var e = new Env();
        var a = e.MakeRelease("0.2.0");
        await e.Published(a);
        var path = Path.Combine(e.Dir, "files", a.File);
        File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await e.Published(a);
        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(path)); // not replaced
        Assert.Empty(Directory.GetDirectories(Path.Combine(e.Dir, "incoming")));
    }

    [Fact]
    public async Task Without_key_and_AllowUnsigned_unsigned_upload_is_accepted_and_status_says_github()
    {
        using var e = new Env(key: false, allowUnsigned: true);
        var r = e.MakeRelease("0.2.0", sign: false);
        var resp = await e.Upload(r);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("github.com", await resp.Content.ReadAsStringAsync());
        Assert.Equal(r.Json, await e.Get("/api/ash/version"));
        Assert.Equal(HttpStatusCode.NotFound, (await e.Http.GetAsync("/api/ash/version.sig")).StatusCode);
        var page = await e.Http.GetStringAsync("/");
        Assert.Contains("github.com 릴리스와 대조", page);
        Assert.Contains("0.2.0", page);
    }

    [Fact]
    public async Task Without_key_and_AllowUnsigned_false_uploads_are_rejected()
    {
        using var e = new Env(key: false);
        var resp = await e.Upload(e.MakeRelease("0.2.0", sign: false));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("Update:AllowUnsigned=true", await resp.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await e.Http.GetAsync("/api/ash/version")).StatusCode);
        var page = await e.Http.GetStringAsync("/");
        Assert.Contains("업로드 거절", page);
        Assert.DoesNotContain("github.com 릴리스와 대조", page);
    }

    [Fact]
    public async Task Upload_with_minSupported_above_version_is_rejected()
    {
        using var e = new Env();
        var resp = await e.Upload(e.MakeRelease("0.2.0", min: "0.3.0"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("minSupported", await resp.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await e.Http.GetAsync("/api/ash/version")).StatusCode);
        await e.Published(e.MakeRelease("0.2.0", min: "0.2.0")); // equal is fine
    }

    [Fact]
    public async Task Status_page_shows_key_id_when_key_is_set()
    {
        using var e = new Env();
        await e.Published(e.MakeRelease("0.2.0"));
        var page = await e.Http.GetStringAsync("/");
        Assert.Contains(e.Store.Key!.KeyId, page);
        Assert.DoesNotContain("서명 키 없음", page);
        Assert.Contains("가능", page);
    }

    [Fact]
    public async Task Concurrent_uploads_both_finish_consistently()
    {
        using var e = new Env();
        var a = e.MakeRelease("0.2.0");
        var b = e.MakeRelease("0.3.0");
        var codes = await Task.WhenAll(e.Upload(a), e.Upload(b));
        Assert.All(codes, c => Assert.True(c.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict));
        var cur = ReleaseManifest.Parse(await e.Get("/api/ash/version"));
        Assert.Equal("0.3.0", cur.Version); // b can never be overwritten by the lower a
    }

    [Fact]
    public async Task Healthz_is_200()
    {
        using var e = new Env();
        Assert.Equal(HttpStatusCode.OK, (await e.Http.GetAsync("/healthz")).StatusCode);
    }
}

public class UnitTests
{
    [Fact]
    public void Create_args_quote_the_binary_path()
    {
        const string exe = @"C:\Program Files\ASH Server\ASH.Server.exe";
        var a = ServiceCommand.BuildCreateArgs(exe);
        Assert.Equal(new[] { "create", "ASH.Server", "binPath=", "\"" + exe + "\"", "start=", "auto", "DisplayName=", "ASH Server" }, a);
    }

    [Theory]
    [InlineData(null, "EMB", false, "EMB")]
    [InlineData("", "EMB", false, "EMB")]  // empty override ignored outside Development
    [InlineData("   ", "EMB", false, "EMB")]
    [InlineData("", "EMB", true, "")]      // ... honoured in Development
    [InlineData("OVR", "EMB", false, "OVR")]
    [InlineData("OVR", null, false, "OVR")]
    [InlineData(null, null, false, null)]
    public void ResolveKeyPem_rules(string? over, string? embedded, bool dev, string? expected)
    {
        Assert.Equal(expected, ReleaseStore.ResolveKeyPem(over, embedded, dev, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));
    }

    [Fact]
    public async Task Status_page_shows_seoul_time()
    {
        using var e = new Env();
        var r = e.MakeRelease("0.2.0", published: "2026-10-21T00:57:15Z");
        await e.Published(r);

        var html = Encoding.UTF8.GetString(await e.Get("/"));

        Assert.Contains("2026-10-21 09:57:15", html);           // published, converted from UTC to Seoul
        Assert.DoesNotContain("2026-10-21T00:57:15Z", html);
        Assert.Contains("서버 시각(서울)", html);                 // the clock behind the page, to spot a wrong PC clock
        Assert.Matches(@"서버 시각\(서울\): \d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}", html);
    }
}
