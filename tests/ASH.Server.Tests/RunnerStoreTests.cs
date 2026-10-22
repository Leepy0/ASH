using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ASH.Server.Tests;

/// <summary>The runner package on the server: signed manifest + zip, served to ash-run, filled from GitHub.</summary>
public class RunnerStoreTests
{
    public sealed record Pkg(byte[] Json, byte[] Sig, byte[] Zip, string File, string Version);

    public static Pkg MakeRunner(Env e, string version, string? payload = null, bool sign = true, long? size = null, string? sha = null, byte[]? bytes = null)
    {
        var zip = bytes ?? Encoding.UTF8.GetBytes(payload ?? "runner-zip-bytes-" + version);
        var file = $"ASH-Runner-{version}-win-x64.zip";
        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            product = "ASH.Runner", version, file,
            sha256 = sha ?? Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant(), size = size ?? zip.Length,
        }));
        var sig = sign ? e.Key.SignData(json, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence) : Array.Empty<byte>();
        return new Pkg(json, sig, zip, file, version);
    }

    private static RunnerStore Store(Env e) => e.Factory.Services.GetRequiredService<RunnerStore>();

    private static Task<ASH.Core.Update.PackageManifest> Publish(RunnerStore s, Pkg p) =>
        s.PublishAsync(p.Json, p.Sig.Length == 0 ? null : p.Sig, p.File, new MemoryStream(p.Zip), default);

    [Fact]
    public async Task Published_runner_is_served_byte_for_byte()
    {
        using var e = new Env();
        var p = MakeRunner(e, "0.5.1");
        await Publish(Store(e), p);

        Assert.Equal(p.Json, await e.Get("/api/ash/runner"));
        Assert.Equal(p.Sig, await e.Get("/api/ash/runner.sig"));
        Assert.Equal(p.Zip, await e.Get("/download/" + p.File));
    }

    [Fact]
    public async Task Nothing_published_answers_404()
    {
        using var e = new Env();
        Assert.Equal(HttpStatusCode.NotFound, (await e.Http.GetAsync("/api/ash/runner")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await e.Http.GetAsync("/api/ash/runner.sig")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await e.Http.GetAsync("/download/ASH-Runner-9.9.9-win-x64.zip")).StatusCode);
    }

    [Fact]
    public async Task Download_only_serves_plain_runner_names()
    {
        using var e = new Env();
        await Publish(Store(e), MakeRunner(e, "0.5.1"));
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Http.GetAsync("/download/..%5Cappsettings.json")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Http.GetAsync("/download/runner.zip")).StatusCode);
    }

    [Fact]
    public async Task Wrong_signature_is_rejected_and_nothing_is_kept()
    {
        using var e = new Env();
        var p = MakeRunner(e, "0.5.1");
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var bad = p with { Sig = other.SignData(p.Json, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence) };

        var ex = await Assert.ThrowsAsync<UploadException>(() => Publish(Store(e), bad));
        Assert.Equal(400, ex.Status);
        Assert.Null(Store(e).Current);
        Assert.Empty(Directory.GetFiles(Store(e).FilesDir));
    }

    [Fact]
    public async Task Missing_signature_is_rejected()
    {
        using var e = new Env();
        var ex = await Assert.ThrowsAsync<UploadException>(() => Publish(Store(e), MakeRunner(e, "0.5.1", sign: false)));
        Assert.Equal(400, ex.Status);
    }

    [Fact]
    public async Task Server_without_a_key_accepts_unsigned_runner()
    {
        // 키 없는 빌드(기본): 서명 없이 받고, ash-run이 github.com과 대조한다
        using var e = new Env(key: false);
        var p = MakeRunner(e, "0.5.1", sign: false);
        await Publish(Store(e), p);
        Assert.Equal(p.Json, await e.Get("/api/ash/runner"));
        Assert.Equal(HttpStatusCode.NotFound, (await e.Http.GetAsync("/api/ash/runner.sig")).StatusCode);
        Assert.Equal(p.Zip, await e.Get("/download/" + p.File));
    }

    [Fact]
    public async Task Zip_with_other_bytes_than_the_manifest_is_rejected()
    {
        using var e = new Env();
        var p = MakeRunner(e, "0.5.1");
        var tampered = p with { Zip = Encoding.UTF8.GetBytes("runner-zip-bytes-0.5.X") }; // same length, other content
        var ex = await Assert.ThrowsAsync<UploadException>(() => Publish(Store(e), tampered));
        Assert.Contains("SHA-256", ex.Message);
        Assert.Null(Store(e).Current);
        Assert.Empty(Directory.GetFiles(Store(e).FilesDir));
    }

    [Fact]
    public async Task Zip_longer_than_declared_is_cut_off_and_rejected()
    {
        using var e = new Env();
        var p = MakeRunner(e, "0.5.1");
        var big = p with { Zip = new byte[5_000_000] };
        var ex = await Assert.ThrowsAsync<UploadException>(() => Publish(Store(e), big));
        Assert.Contains("크기", ex.Message);
        Assert.Empty(Directory.GetFiles(Store(e).FilesDir));
    }

    [Fact]
    public async Task Same_or_older_version_is_rejected_with_409()
    {
        using var e = new Env();
        await Publish(Store(e), MakeRunner(e, "0.5.2"));
        foreach (var v in new[] { "0.5.2", "0.5.1" })
        {
            var ex = await Assert.ThrowsAsync<UploadException>(() => Publish(Store(e), MakeRunner(e, v)));
            Assert.Equal(409, ex.Status);
        }
        Assert.Equal("0.5.2", Store(e).Current!.Version);
    }

    [Fact]
    public async Task Only_the_two_newest_zips_are_kept()
    {
        using var e = new Env();
        foreach (var v in new[] { "0.5.1", "0.5.2", "0.5.10" }) await Publish(Store(e), MakeRunner(e, v));
        var files = Directory.GetFiles(Store(e).FilesDir).Select(Path.GetFileName).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "ASH-Runner-0.5.10-win-x64.zip", "ASH-Runner-0.5.2-win-x64.zip" }, files); // 0.5.10 is newer than 0.5.2
        Assert.Equal("0.5.10", Store(e).Current!.Version);
    }

    [Fact]
    public async Task File_name_must_match_the_manifest()
    {
        using var e = new Env();
        var p = MakeRunner(e, "0.5.1");
        var ex = await Assert.ThrowsAsync<UploadException>(() => Store(e).PublishAsync(p.Json, p.Sig, "ASH-Runner-0.5.9-win-x64.zip", new MemoryStream(p.Zip), default));
        Assert.Equal(400, ex.Status);
    }

    // ---- the GitHub pull ----

    private const string Base = "https://github.com/o/r";

    private sealed class Fake : HttpMessageHandler
    {
        public readonly Dictionary<string, byte[]> Files = new();
        public readonly List<string> Requests = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var u = req.RequestUri!.AbsoluteUri;
            Requests.Add(u);
            return Task.FromResult(Files.TryGetValue(u, out var b) ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(b) } : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static void ServeRunner(Fake f, Pkg p, byte[]? sig = null)
    {
        f.Files[$"{Base}/releases/latest/download/ASH.Runner.json"] = p.Json;
        f.Files[$"{Base}/releases/download/v{p.Version}/ASH.Runner.json.sig"] = sig ?? p.Sig;
        f.Files[$"{Base}/releases/download/v{p.Version}/{p.File}"] = p.Zip;
    }

    private static GitHubSync Sync(Env e, Fake f) => new(e.Store, Base, TimeSpan.Zero, new HttpClient(f), NullLogger.Instance, null, Store(e));

    [Fact]
    public async Task Github_pull_stores_a_newer_signed_runner()
    {
        using var e = new Env();
        var f = new Fake(); var p = MakeRunner(e, "0.5.1");
        ServeRunner(f, p);
        using var s = Sync(e, f);

        await s.CheckOnceAsync(default);

        Assert.Equal("가져옴: 0.5.1", s.RunnerResult);
        Assert.Equal(p.Zip, await e.Get("/download/" + p.File));
    }

    [Fact]
    public async Task Github_pull_skips_the_zip_when_up_to_date()
    {
        using var e = new Env();
        var p = MakeRunner(e, "0.5.1");
        await Publish(Store(e), p);
        var f = new Fake(); ServeRunner(f, p);
        using var s = Sync(e, f);

        await s.CheckOnceAsync(default);

        Assert.Equal("최신(0.5.1)", s.RunnerResult);
        Assert.DoesNotContain(f.Requests, r => r.EndsWith(".zip"));
    }

    [Fact]
    public async Task Github_pull_with_a_bad_signature_stores_nothing()
    {
        using var e = new Env();
        var f = new Fake(); var p = MakeRunner(e, "0.5.1");
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ServeRunner(f, p, sig: other.SignData(p.Json, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        using var s = Sync(e, f);

        await s.CheckOnceAsync(default);

        Assert.StartsWith("거절:", s.RunnerResult);
        Assert.Null(Store(e).Current);
    }

    [Fact]
    public async Task Github_release_without_a_runner_is_not_an_error()
    {
        using var e = new Env();
        using var s = Sync(e, new Fake());
        await s.CheckOnceAsync(default);
        Assert.Contains("ASH.Runner.json 없음", s.RunnerResult);
    }
}
