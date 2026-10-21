using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ASH.Core.Update;
using ASH.Runner;
using Microsoft.Extensions.DependencyInjection;

namespace ASH.Server.Tests;

/// <summary>ash-run's self-update against the real server (in memory): what it asks for, what it accepts, how it swaps the exe.</summary>
public class RunnerUpdaterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ash-runner-test-" + Guid.NewGuid().ToString("N"));
    private string Exe => Path.Combine(_dir, "ash-run.exe");

    public RunnerUpdaterTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static byte[] ZipOf(string entryName, byte[] content)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
        using (var s = z.CreateEntry(entryName).Open()) s.Write(content);
        return ms.ToArray();
    }

    private static async Task Publish(Env e, string version, byte[] zip)
    {
        var p = RunnerStoreTests.MakeRunner(e, version, bytes: zip);
        await e.Factory.Services.GetRequiredService<RunnerStore>().PublishAsync(p.Json, p.Sig, p.File, new MemoryStream(p.Zip), default);
    }

    private RunnerUpdateOptions Opts(Env e, string current = "0.5.0", HttpMessageHandler? h = null, ECDsa? signer = null) =>
        new(new Uri("http://localhost/"), new ManifestSignature((signer ?? e.Key).ExportSubjectPublicKeyInfoPem()), current, Exe, TimeSpan.FromSeconds(10), h ?? e.Factory.Server.CreateHandler());

    [Fact]
    public async Task Newer_runner_is_downloaded_verified_and_unpacked()
    {
        using var e = new Env();
        var exeBytes = Encoding.UTF8.GetBytes("pretend ash-run.exe " + new string('x', 100_000));
        await Publish(e, "0.6.0", ZipOf("ash-run.exe", exeBytes));

        var p = await RunnerUpdater.PrepareAsync(Opts(e), default);

        Assert.NotNull(p);
        Assert.Equal("0.6.0", p!.Version);
        Assert.Equal(Exe + ".new", p.NewExePath);
        Assert.Equal(exeBytes, File.ReadAllBytes(p.NewExePath));
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "update"))); // no .part or zip left behind
    }

    [Theory]
    [InlineData("0.6.0")]
    [InlineData("0.7.0")]
    public async Task Same_or_older_runner_on_the_server_means_no_update(string current)
    {
        using var e = new Env();
        await Publish(e, "0.6.0", ZipOf("ash-run.exe", new byte[] { 1, 2, 3 }));
        Assert.Null(await RunnerUpdater.PrepareAsync(Opts(e, current), default));
        Assert.False(File.Exists(Exe + ".new"));
    }

    [Fact]
    public async Task Server_without_a_runner_means_no_update()
    {
        using var e = new Env();
        Assert.Null(await RunnerUpdater.PrepareAsync(Opts(e), default));
    }

    [Fact]
    public async Task A_manifest_signed_with_another_key_is_refused()
    {
        using var e = new Env();
        await Publish(e, "0.6.0", ZipOf("ash-run.exe", new byte[] { 1 }));
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => RunnerUpdater.PrepareAsync(Opts(e, signer: other), default));
        Assert.Contains("서명", ex.Message);
        Assert.False(File.Exists(Exe + ".new"));
    }

    private sealed class TamperZip(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var r = await base.SendAsync(req, ct);
            if (req.RequestUri!.AbsolutePath.StartsWith("/download/"))
            {
                var b = await r.Content.ReadAsByteArrayAsync(ct);
                b[b.Length / 2] ^= 0xFF; // same length, one byte changed
                r.Content = new ByteArrayContent(b);
            }
            return r;
        }
    }

    [Fact]
    public async Task A_zip_changed_on_the_way_is_refused_by_the_hash()
    {
        using var e = new Env();
        await Publish(e, "0.6.0", ZipOf("ash-run.exe", new byte[50_000]));
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => RunnerUpdater.PrepareAsync(Opts(e, h: new TamperZip(e.Factory.Server.CreateHandler())), default));
        Assert.Contains("SHA-256", ex.Message);
        Assert.False(File.Exists(Exe + ".new"));
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "update")));
    }

    [Fact]
    public async Task A_zip_without_ash_run_exe_is_refused()
    {
        using var e = new Env();
        await Publish(e, "0.6.0", ZipOf("other.exe", new byte[] { 1, 2, 3 }));
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => RunnerUpdater.PrepareAsync(Opts(e), default));
        Assert.Contains("ash-run.exe", ex.Message);
        Assert.False(File.Exists(Exe + ".new"));
    }

    [Fact]
    public void Apply_keeps_the_old_exe_and_Revert_puts_it_back()
    {
        File.WriteAllText(Exe, "old");
        File.WriteAllText(Exe + ".new", "new");

        RunnerUpdater.Apply(Exe, Exe + ".new");
        Assert.Equal("new", File.ReadAllText(Exe));
        Assert.Equal("old", File.ReadAllText(Exe + ".old"));
        Assert.False(File.Exists(Exe + ".new"));

        RunnerUpdater.Revert(Exe);
        Assert.Equal("old", File.ReadAllText(Exe));
        Assert.Equal("new", File.ReadAllText(Exe + ".bad"));
        Assert.False(File.Exists(Exe + ".old"));
    }

    [Fact]
    public void Apply_replaces_an_older_old_file()
    {
        File.WriteAllText(Exe, "v2");
        File.WriteAllText(Exe + ".old", "v1");
        File.WriteAllText(Exe + ".new", "v3");
        RunnerUpdater.Apply(Exe, Exe + ".new");
        Assert.Equal("v3", File.ReadAllText(Exe));
        Assert.Equal("v2", File.ReadAllText(Exe + ".old"));
    }

    [Fact]
    public void Apply_with_a_missing_new_file_leaves_the_exe_alone()
    {
        File.WriteAllText(Exe, "keep");
        Assert.ThrowsAny<IOException>(() => RunnerUpdater.Apply(Exe, Exe + ".new"));
        Assert.Equal("keep", File.ReadAllText(Exe));
    }

    [Fact]
    public async Task Without_a_signing_key_in_the_build_nothing_updates_in_a_development_run()
    {
        // under the test host Environment.ProcessPath is dotnet/testhost, not ash-run.exe: the entry point must stay out of the way
        var w = new StringWriter();
        Assert.Null(await RunnerUpdater.TryAsync(new[] { "proj", "--model", "m" }, "http://localhost:1", w, default));
        Assert.Equal("", w.ToString());
    }

    [Fact]
    public void Version_is_the_build_version() => Assert.True(SemVersion.TryParse(RunnerUpdater.CurrentVersion, out _), RunnerUpdater.CurrentVersion);
}
