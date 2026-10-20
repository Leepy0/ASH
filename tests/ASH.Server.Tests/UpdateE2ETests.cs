using System.Security.Cryptography;
using ASH.Core.Update;

namespace ASH.Server.Tests;

/// <summary>Core UpdateClient against the in-memory server: signed manifest + fake installer, as the plugin sees it.</summary>
public class UpdateE2ETests
{
    private static UpdateClient Client(Env e) => new(new Uri("http://localhost/"), TimeSpan.FromSeconds(10), e.Factory.Server.CreateHandler());

    [Fact]
    public async Task Signed_release_is_checked_downloaded_and_hash_verified()
    {
        using var e = new Env();
        var r = e.MakeRelease("0.2.0", "fake installer bytes " + new string('x', 200_000));
        await e.Published(r);
        var dest = Path.Combine(e.Dir, "dl");
        using var c = Client(e);
        var key = new ManifestSignature(e.Key.ExportSubjectPublicKeyInfoPem());

        var res = await UpdateFlow.CheckAsync(c, key, "0.1.0");
        Assert.Equal(UpdateState.Available, res.Status.State);
        Assert.Equal(UpdateState.UpToDate, UpdateCheck.Evaluate("0.2.0", res.Fetched.Manifest).State);

        var path = await c.DownloadVerifiedAsync(res.Fetched, key, dest);
        Assert.Equal(r.File, Path.GetFileName(path));
        Assert.Equal(SHA256.HashData(r.Setup), SHA256.HashData(File.ReadAllBytes(path)));
    }

    [Fact]
    public async Task Client_with_a_different_key_rejects_the_release()
    {
        using var e = new Env();
        await e.Published(e.MakeRelease("0.2.0"));
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var wrong = new ManifestSignature(other.ExportSubjectPublicKeyInfoPem());
        using var c = Client(e);

        await Assert.ThrowsAsync<InvalidDataException>(() => UpdateFlow.CheckAsync(c, wrong, "0.1.0"));
        var f = await c.GetManifestAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => c.DownloadVerifiedAsync(f, wrong, Path.Combine(e.Dir, "dl")));
        Assert.False(Directory.Exists(Path.Combine(e.Dir, "dl")) && Directory.GetFiles(Path.Combine(e.Dir, "dl")).Length > 0);
    }
}
