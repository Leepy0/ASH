using System.Text;
using ASH.Core.Update;

namespace ASH.Tests.Update;

public class PackageManifestTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private static byte[] J(string product, string version, string file, string sha = Sha, long size = 42) =>
        Encoding.UTF8.GetBytes($"{{\"product\":\"{product}\",\"version\":\"{version}\",\"file\":\"{file.Replace("\\", "\\\\")}\",\"sha256\":\"{sha}\",\"size\":{size},\"extra\":true}}");

    [Fact]
    public void Runner_manifest_parses_and_ignores_extra_fields()
    {
        var m = PackageManifest.ParseRunner(J("ASH.Runner", "0.6.0", "ASH-Runner-0.6.0-win-x64.zip"));
        Assert.Equal(("ASH.Runner", "0.6.0", "ASH-Runner-0.6.0-win-x64.zip", Sha, 42L), (m.Product, m.Version, m.File, m.Sha256, m.Size));
    }

    [Theory]
    [InlineData("ASH.Server", "0.6.0", "ASH-Runner-0.6.0-win-x64.zip", Sha, 42)]   // another product
    [InlineData("ASH.Runner", "six", "ASH-Runner-0.6.0-win-x64.zip", Sha, 42)]
    [InlineData("ASH.Runner", "0.6.0", "ASH-Server-0.6.0-win-x64.zip", Sha, 42)]   // another file kind
    [InlineData("ASH.Runner", "0.6.0", "..\\ASH-Runner-0.6.0-win-x64.zip", Sha, 42)]
    [InlineData("ASH.Runner", "0.6.0", "ASH-Runner-0.6.0-win-x64.zip/x", Sha, 42)]
    [InlineData("ASH.Runner", "0.6.0", "ASH-Runner-0.6.0-win-x64.zip", "ABC", 42)]
    [InlineData("ASH.Runner", "0.6.0", "ASH-Runner-0.6.0-win-x64.zip", Sha, 0)]
    public void Bad_runner_manifest_is_rejected(string product, string version, string file, string sha, long size)
        => Assert.Throws<InvalidDataException>(() => PackageManifest.ParseRunner(J(product, version, file, sha, size)));

    [Fact] public void Not_json_is_rejected() => Assert.Throws<InvalidDataException>(() => PackageManifest.ParseRunner(Encoding.UTF8.GetBytes("<html>")));

    [Theory]
    [InlineData("0.6.0", "0.5.9", true)]
    [InlineData("0.6.0", "0.6.0", false)]
    [InlineData("0.6.0", "0.7.0", false)]
    [InlineData("0.5.10", "0.5.9", true)]   // numeric, not text, order
    [InlineData("0.6.0", "not-a-version", true)]
    [InlineData("0.6.0", null, true)]
    public void Newer_than_compares_semver(string pkg, string? current, bool newer)
        => Assert.Equal(newer, PackageManifest.ParseRunner(J("ASH.Runner", pkg, $"ASH-Runner-{pkg}-win-x64.zip")).IsNewerThan(current));

    [Fact]
    public void Runner_file_names_are_checked()
    {
        Assert.True(PackageManifest.IsRunnerFileName("ASH-Runner-0.6.0-win-x64.zip"));
        Assert.False(PackageManifest.IsRunnerFileName("ASH-Setup-0.6.0.exe"));
        Assert.False(PackageManifest.IsRunnerFileName("..\\ASH-Runner-0.6.0-win-x64.zip"));
        Assert.False(PackageManifest.IsRunnerFileName(null));
    }

    [Fact]
    public async Task Hashed_copy_stops_one_chunk_past_the_expected_size()
    {
        var data = new byte[1_000_000];
        using var dst = new MemoryStream();
        var (size, _) = await HashedCopy.RunAsync(new MemoryStream(data), dst, 100, default);
        Assert.InRange(size, 101, 81920); // not the whole megabyte
    }

    [Fact]
    public async Task Hashed_copy_hashes_exactly_what_it_copied()
    {
        var data = Encoding.UTF8.GetBytes("abc");
        using var dst = new MemoryStream();
        var (size, sha) = await HashedCopy.RunAsync(new MemoryStream(data), dst, 3, default);
        Assert.Equal(3, size);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", sha);
        Assert.Equal(data, dst.ToArray());
    }
}
