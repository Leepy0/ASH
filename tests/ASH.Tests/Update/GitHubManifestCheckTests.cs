using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ASH.Core.Update;
using Xunit;

namespace ASH.Tests.Update;

/// <summary>서명 대신 github.com 릴리스의 같은 매니페스트와 바이트 대조.</summary>
public sealed class GitHubManifestCheckTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ash-gh-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private sealed class Fake : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Fn = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        public int Calls;
        public string? LastUrl;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
            LastUrl = r.RequestUri!.AbsoluteUri;
            return Task.FromResult(Fn(r));
        }
    }

    private static HttpResponseMessage Ok(byte[] b) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(b) };
    private static string Hex(byte[] b) { using var s = SHA256.Create(); return string.Concat(s.ComputeHash(b).Select(x => x.ToString("x2"))); }
    private static GitHubManifestCheck Check(Fake f) => new(TimeSpan.FromSeconds(5), f);

    private static readonly byte[] Json = Encoding.UTF8.GetBytes("{\"version\":\"0.15.0\",\"file\":\"ASH-Setup-0.15.0.exe\"}");

    [Fact]
    public async Task Same_bytes_pass_and_the_tag_url_is_used()
    {
        var f = new Fake { Fn = _ => Ok(Json) };
        await Check(f).VerifyAsync(Json, "0.15.0", "ASH.json");
        Assert.Equal("https://github.com/Leepy0/ASH/releases/download/v0.15.0/ASH.json", f.LastUrl);
    }

    [Fact]
    public async Task Different_bytes_are_rejected()
    {
        var other = Json.Concat(new byte[] { (byte)'\n' }).ToArray(); // 공백 하나도 다르면 거절
        var f = new Fake { Fn = _ => Ok(other) };
        var e = await Assert.ThrowsAsync<GitHubCheckException>(() => Check(f).VerifyAsync(Json, "0.15.0", "ASH.json"));
        Assert.Contains("다릅니다", e.Message);
    }

    [Fact]
    public async Task Missing_release_file_is_rejected()
    {
        var e = await Assert.ThrowsAsync<GitHubCheckException>(() => Check(new Fake()).VerifyAsync(Json, "0.15.0", "ASH.json"));
        Assert.Contains("없습니다", e.Message);
    }

    [Fact]
    public async Task Unreachable_github_is_rejected_with_a_clear_reason()
    {
        var f = new Fake { Fn = _ => throw new HttpRequestException("no route") };
        var e = await Assert.ThrowsAsync<GitHubCheckException>(() => Check(f).VerifyAsync(Json, "0.15.0", "ASH.json"));
        Assert.Contains("접속할 수 없어", e.Message);
        Assert.StartsWith("업데이트 중단(", UpdateMessages.DownloadFailed(e));
    }

    // ---- UpdateClient: 키 없을 때 대조 후에만 설치 파일을 받는다 ----

    private static (FetchedManifest F, byte[] Setup) Release()
    {
        var setup = Encoding.UTF8.GetBytes("fake setup");
        var m = new ReleaseManifest { Version = "0.15.0", File = "ASH-Setup-0.15.0.exe", Sha256 = Hex(setup), Size = setup.Length };
        return (new FetchedManifest(m.ToJson(), null), setup);
    }

    [Fact]
    public async Task Client_downloads_only_after_github_matches()
    {
        var (fm, setup) = Release();
        var server = new Fake { Fn = _ => Ok(setup) };
        var gh = new Fake { Fn = _ => Ok(fm.Json) };
        using var c = new UpdateClient(new Uri("http://srv:5080/"), TimeSpan.FromSeconds(5), server);
        var path = await c.DownloadVerifiedAsync(fm, null, _dir, default, Check(gh));
        Assert.Equal(setup, File.ReadAllBytes(path));
        Assert.Equal(1, gh.Calls);
    }

    [Fact]
    public async Task Client_does_not_download_when_github_differs()
    {
        var (fm, setup) = Release();
        var server = new Fake { Fn = _ => Ok(setup) };
        var gh = new Fake { Fn = _ => Ok(Encoding.UTF8.GetBytes("{}")) };
        using var c = new UpdateClient(new Uri("http://srv:5080/"), TimeSpan.FromSeconds(5), server);
        await Assert.ThrowsAsync<GitHubCheckException>(() => c.DownloadVerifiedAsync(fm, null, _dir, default, Check(gh)));
        Assert.Equal(0, server.Calls);
        Assert.False(Directory.Exists(_dir) && Directory.GetFiles(_dir).Length > 0);
    }
}
