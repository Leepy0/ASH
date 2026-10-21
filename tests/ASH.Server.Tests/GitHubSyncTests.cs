using System.Net;
using System.Security.Cryptography;
using ASH.Core.Update;
using Microsoft.Extensions.Logging.Abstractions;

namespace ASH.Server.Tests;

/// <summary>GitHub pull against a fake github.com: only the fixed release URLs, same checks as an upload.</summary>
public class GitHubSyncTests
{
    private const string Base = "https://github.com/o/r";

    private sealed class Fake : HttpMessageHandler
    {
        public readonly Dictionary<string, byte[]> Files = new();
        public readonly List<string> Requests = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var u = req.RequestUri!.AbsoluteUri;
            Requests.Add(u);
            return Task.FromResult(Files.TryGetValue(u, out var b)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(b) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static void Serve(Fake f, Env.Release r, byte[]? sig = null, byte[]? setup = null)
    {
        f.Files[$"{Base}/releases/latest/download/ASH.json"] = r.Json;
        f.Files[$"{Base}/releases/download/v{r.Version}/ASH.json.sig"] = sig ?? r.Sig;
        f.Files[$"{Base}/releases/download/v{r.Version}/{r.File}"] = setup ?? r.Setup;
    }

    private static GitHubSync Sync(Env e, Fake f) => new(e.Store, Base, TimeSpan.Zero, new HttpClient(f), NullLogger.Instance);

    [Fact]
    public async Task Newer_signed_release_is_pulled_and_served()
    {
        using var e = new Env();
        var f = new Fake();
        var r = e.MakeRelease("0.2.0");
        Serve(f, r);
        using var s = Sync(e, f);

        await s.CheckOnceAsync(default);

        Assert.StartsWith("가져옴: 0.2.0", s.LastResult);
        Assert.Equal(r.Json, await e.Get("/api/ash/version"));
        Assert.Equal(r.Sig, await e.Get("/api/ash/version.sig"));
        Assert.Equal(SHA256.HashData(r.Setup), SHA256.HashData(await e.Get("/download/" + r.File)));
    }

    [Fact]
    public async Task Same_version_does_not_download_the_setup()
    {
        using var e = new Env();
        var r = e.MakeRelease("0.2.0");
        await e.Published(r);
        var f = new Fake();
        Serve(f, r);
        using var s = Sync(e, f);

        await s.CheckOnceAsync(default);

        Assert.Equal("최신(0.2.0)", s.LastResult);
        Assert.Equal(new[] { $"{Base}/releases/latest/download/ASH.json" }, f.Requests);
    }

    [Fact]
    public async Task Older_GitHub_release_leaves_the_server_alone()
    {
        using var e = new Env();
        var cur = e.MakeRelease("0.3.0");
        await e.Published(cur);
        var f = new Fake();
        Serve(f, e.MakeRelease("0.2.0"));
        using var s = Sync(e, f);

        await s.CheckOnceAsync(default);

        Assert.StartsWith("서버 버전(0.3.0)", s.LastResult);
        Assert.Equal(cur.Json, await e.Get("/api/ash/version"));
    }

    [Fact]
    public async Task Bad_signature_is_rejected_and_nothing_is_published()
    {
        using var e = new Env();
        var f = new Fake();
        var r = e.MakeRelease("0.2.0");
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Serve(f, r, sig: other.SignData(r.Json, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        using var s = Sync(e, f);

        await s.CheckOnceAsync(default);

        Assert.StartsWith("거절: 서명이 맞지 않습니다", s.LastResult);
        Assert.Null(e.Store.GetCurrent());
        Assert.Empty(Directory.GetFiles(e.Store.FilesDir));
    }

    [Fact]
    public async Task Setup_with_wrong_bytes_is_rejected()
    {
        using var e = new Env();
        var f = new Fake();
        var r = e.MakeRelease("0.2.0");
        Serve(f, r, setup: r.Setup.Concat(new byte[1_000_000]).ToArray());
        using var s = Sync(e, f);

        await s.CheckOnceAsync(default);

        Assert.StartsWith("거절: 설치 파일 크기가 다릅니다", s.LastResult);
        Assert.Null(e.Store.GetCurrent());
    }

    [Fact]
    public async Task No_release_on_GitHub_is_reported_not_thrown()
    {
        using var e = new Env();
        using var s = Sync(e, new Fake());

        await s.CheckOnceAsync(default);

        Assert.Contains("릴리스가 없습니다", s.LastResult);
        Assert.NotNull(s.LastCheckUtc);
    }

    [Theory]
    [InlineData(null, GitHubSync.DefaultBaseUrl)]
    [InlineData(" https://github.com/a/b/ ", "https://github.com/a/b")]
    public void Base_url_is_normalized(string? given, string expected) => Assert.Equal(expected, GitHubSync.Url(given));

    [Theory]
    [InlineData("http://github.com/a/b")]
    [InlineData("file:///c:/x")]
    [InlineData("https://user:pw@github.com/a/b")]
    [InlineData("github.com/a/b")]
    public void Base_url_must_be_plain_https(string given) => Assert.Throws<ArgumentException>(() => GitHubSync.Url(given));
}
