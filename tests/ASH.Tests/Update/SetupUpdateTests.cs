using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using ASH.Core.Update;
using ASH.Tests.Release;

namespace ASH.Tests.Update;

/// <summary>The Setup exe checking github.com itself: UpdateClient.GitHub + UpdateFlow + SetupUpdate texts, against a fake github.com.</summary>
public sealed class SetupUpdateTests : IDisposable
{
    private const string Repo = "https://github.com/o/r";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ash-setup-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private sealed class Fake : HttpMessageHandler
    {
        public readonly Dictionary<string, byte[]> Files = new();
        public Exception? Throw;
        public readonly List<string> Requests = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Requests.Add(r.RequestUri!.AbsoluteUri);
            if (Throw != null) throw Throw;
            return Task.FromResult(Files.TryGetValue(r.RequestUri.AbsoluteUri, out var b)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(b) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static string Hex(byte[] b) { using var s = SHA256.Create(); return string.Concat(s.ComputeHash(b).Select(x => x.ToString("x2"))); }
    private static readonly byte[] Setup = Encoding.UTF8.GetBytes("new setup bytes");

    private static Fake Release(string version = "0.20.0", byte[]? manifestSetup = null, long? size = null, byte[]? json = null, byte[]? sig = null)
    {
        var hashed = manifestSetup ?? Setup;
        var m = new ReleaseManifest { Version = version, File = $"ASH-Setup-{version}.exe", Sha256 = Hex(hashed), Size = size ?? hashed.Length };
        var f = new Fake();
        f.Files[$"{Repo}/releases/latest/download/ASH.json"] = json ?? m.ToJson();
        f.Files[$"{Repo}/releases/download/v{version}/ASH-Setup-{version}.exe"] = Setup;
        if (sig != null) f.Files[$"{Repo}/releases/download/v{version}/ASH.json.sig"] = sig;
        return f;
    }

    private static async Task<UpdateResult> Check(Fake f, string current, ManifestSignature? key = null)
    {
        using var c = UpdateClient.GitHub(TimeSpan.FromSeconds(5), f, Repo);
        return await UpdateFlow.CheckAsync(c, key, current);
    }

    private async Task<string> Download(Fake f, UpdateResult r, IProgress<long>? p = null)
    {
        using var c = UpdateClient.GitHub(TimeSpan.FromSeconds(5), f, Repo);
        return await c.DownloadVerifiedAsync(r.Fetched, null, _dir, default, null, p);
    }

    [Fact]
    public async Task Newer_release_is_offered_and_only_fixed_github_com_urls_are_used()
    {
        var f = Release();
        var r = await Check(f, "0.19.1+abc");
        Assert.True(SetupUpdate.IsNewer(r.Status));
        Assert.Equal("설치 파일 버전 0.19.1 / GitHub 최신 0.20.0 — 새 버전이 있습니다", SetupUpdate.Result("0.19.1+abc", r));
        Assert.Equal(new[] { $"{Repo}/releases/latest/download/ASH.json", $"{Repo}/releases/download/v0.20.0/ASH.json.sig" }, f.Requests);
        Assert.DoesNotContain(f.Requests, u => u.Contains("api.github.com"));
    }

    [Theory]
    [InlineData("0.20.0", "— 최신입니다")]
    [InlineData("0.21.0", "— 이 설치 파일이 더 새 버전입니다")]
    public async Task Same_or_older_release_is_not_offered(string current, string tail)
    {
        var r = await Check(Release(), current);
        Assert.False(SetupUpdate.IsNewer(r.Status));
        Assert.EndsWith(tail, SetupUpdate.Result(current, r));
    }

    [Fact]
    public async Task Prerelease_is_older_than_the_release()
    {
        var r = await Check(Release(), "0.20.0-rc.1");
        Assert.True(SetupUpdate.IsNewer(r.Status));
    }

    [Fact]
    public async Task Garbage_manifest_fails_the_check()
    {
        var e = await Assert.ThrowsAsync<InvalidDataException>(() => Check(Release(json: Encoding.UTF8.GetBytes("<html>")), "0.19.1"));
        Assert.StartsWith("설치 파일 버전 0.19.1 / GitHub 최신 확인 실패(배포 정보(ASH.json)를 읽을 수 없습니다", SetupUpdate.Failed("0.19.1", e));
    }

    [Fact]
    public async Task Offline_gives_a_short_note_and_keeps_this_version()
    {
        var f = new Fake { Throw = new HttpRequestException("No such host is known") };
        var e = await Assert.ThrowsAsync<HttpRequestException>(() => Check(f, "0.19.1"));
        Assert.Equal("설치 파일 버전 0.19.1 / GitHub 최신 확인 실패(서버 응답 없음) — 이 버전으로 설치할 수 있습니다", SetupUpdate.Failed("0.19.1", e));
        Assert.Contains("서버 응답 없음", SetupUpdate.Failed("0.19.1", new TimeoutException())); // the UI's hard cap
    }

    [Fact]
    public void Company_certificate_gets_the_tls_text()
    {
        var tls = new HttpRequestException("ssl", new System.Security.Authentication.AuthenticationException("The remote certificate is invalid"));
        Assert.Contains("서버 인증서 오류 — 사내 인증서 신뢰 여부 확인", SetupUpdate.Failed("0.19.1", tls));
    }

    [Fact]
    public async Task Signed_build_rejects_a_release_without_signature()
    {
        var f = Release(json: Convert.FromBase64String(SigVector.ManifestB64)); // version 0.1.0, no .sig published
        await Assert.ThrowsAsync<InvalidDataException>(() => Check(f, "0.0.1", new ManifestSignature(SigVector.PubKey2)));
    }

    [Fact]
    public async Task Signed_build_accepts_the_tag_signature()
    {
        var f = Release(version: "0.1.0", json: Convert.FromBase64String(SigVector.ManifestB64), sig: Convert.FromBase64String(SigVector.SigB64));
        var r = await Check(f, "0.0.1", new ManifestSignature(SigVector.PubKey2));
        Assert.True(SetupUpdate.IsNewer(r.Status));
    }

    [Fact]
    public async Task Download_lands_in_the_folder_with_progress()
    {
        var f = Release();
        var seen = new List<long>();
        var path = await Download(f, await Check(f, "0.19.1"), new SyncProgress(seen));
        Assert.Equal(Path.Combine(_dir, "ASH-Setup-0.20.0.exe"), path);
        Assert.Equal(Setup, File.ReadAllBytes(path));
        Assert.Equal(Setup.Length, seen.Last());
        Assert.Equal($"{Repo}/releases/download/v0.20.0/ASH-Setup-0.20.0.exe", f.Requests.Last());
    }

    [Fact]
    public async Task Sha_mismatch_is_rejected_and_nothing_is_left()
    {
        var f = Release(manifestSetup: Encoding.UTF8.GetBytes("other bytes, same len"), size: Setup.Length);
        var e = await Assert.ThrowsAsync<InvalidDataException>(async () => await Download(f, await Check(f, "0.19.1")));
        Assert.Contains("SHA-256", e.Message);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task Size_mismatch_is_rejected_and_nothing_is_left()
    {
        var f = Release(size: Setup.Length + 1);
        var e = await Assert.ThrowsAsync<InvalidDataException>(async () => await Download(f, await Check(f, "0.19.1")));
        Assert.Contains("크기", e.Message);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Relaunch_passes_this_runs_arguments_and_suppresses_the_check()
    {
        Assert.Equal("/noupdatecheck", SetupUpdate.RelaunchArguments(new string[0]));
        Assert.Equal("/uninstall \"/dir:C:\\Program Files\\ZWCAD 2026\" /server:http://s:8082 /noupdatecheck",
            SetupUpdate.RelaunchArguments(new[] { "/uninstall", @"/dir:C:\Program Files\ZWCAD 2026", "/server:http://s:8082", "/capture:x.png", "/NoUpdateCheck" }));
        // a trailing backslash must not swallow the closing quote
        Assert.Equal("\"/dir:C:\\A B\\\\\"", SetupUpdate.Quote(@"/dir:C:\A B\"));
        Assert.Equal("\"a\\\\\\\"b\"", SetupUpdate.Quote("a\\\"b"));
    }

    // Progress<T> posts to the thread pool; this one records synchronously
    private sealed class SyncProgress : IProgress<long>
    {
        private readonly List<long> _seen;
        public SyncProgress(List<long> seen) => _seen = seen;
        public void Report(long v) => _seen.Add(v);
    }
}
