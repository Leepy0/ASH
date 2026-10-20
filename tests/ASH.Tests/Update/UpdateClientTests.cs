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

public sealed class UpdateClientTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ash-upd-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private sealed class Fake : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Fn = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Fn(r, ct);
    }

    private static HttpResponseMessage Ok(byte[] b) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(b) };
    private static UpdateClient Client(Fake f, int ms = 3000) => new(new Uri("http://srv:5080/"), TimeSpan.FromMilliseconds(ms), f);
    private static string Hex(byte[] b) { using var s = SHA256.Create(); return string.Concat(s.ComputeHash(b).Select(x => x.ToString("x2"))); }

    // odd spacing on purpose: the client must hand back the bytes untouched, not a re-serialization
    private static readonly byte[] Json = Encoding.UTF8.GetBytes("{ \"version\" : \"0.2.0\", \"file\":\"ASH-Setup-0.2.0.exe\", \"sha256\":\"ab\", \"size\": 3 }\n");

    private static ReleaseManifest Mf(byte[] setup, string file = "ASH-Setup-0.2.0.exe") => new() { Version = "0.2.0", File = file, Sha256 = Hex(setup), Size = setup.Length };

    [Fact] public async Task GetManifest_Returns_Verbatim_Bytes_And_Sig()
    {
        var sig = new byte[] { 1, 2, 3 };
        var f = new Fake { Fn = (r, _) => Task.FromResult(r.RequestUri!.AbsolutePath switch
        {
            "/api/ash/version" => Ok(Json),
            "/api/ash/version.sig" => Ok(sig),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        }) };
        using var c = Client(f);
        var got = await c.GetManifestAsync();
        Assert.Equal(Json, got.Json);
        Assert.Equal(sig, got.Sig);
        Assert.Equal("0.2.0", got.Manifest.Version);
    }

    [Fact] public async Task GetManifest_Sig_404_Gives_Null_Sig()
    {
        var f = new Fake { Fn = (r, _) => Task.FromResult(r.RequestUri!.AbsolutePath == "/api/ash/version" ? Ok(Json) : new HttpResponseMessage(HttpStatusCode.NotFound)) };
        using var c = Client(f);
        Assert.Null((await c.GetManifestAsync()).Sig);
    }

    [Fact] public async Task GetManifest_Server_Error_Propagates()
    {
        var f = new Fake { Fn = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)) };
        using var c = Client(f);
        await Assert.ThrowsAsync<HttpRequestException>(() => c.GetManifestAsync());
    }

    [Fact] public async Task GetManifest_Connection_Failure_Propagates()
    {
        var f = new Fake { Fn = (_, _) => throw new HttpRequestException("refused") };
        using var c = Client(f);
        await Assert.ThrowsAsync<HttpRequestException>(() => c.GetManifestAsync());
    }

    [Fact] public async Task GetManifest_Timeout_Is_Cancellation()
    {
        var f = new Fake { Fn = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return null!; } };
        using var c = Client(f, 100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => c.GetManifestAsync());
    }

    [Fact] public async Task Download_Ok_Renames_And_Leaves_No_Part()
    {
        var setup = Enumerable.Range(0, 300000).Select(i => (byte)(i * 7)).ToArray();
        string? path = null;
        var f = new Fake { Fn = (r, _) => { path = r.RequestUri!.AbsolutePath; return Task.FromResult(Ok(setup)); } };
        using var c = Client(f);
        string got = await c.DownloadVerifiedAsync(Mf(setup), _dir);
        Assert.Equal("/download/ASH-Setup-0.2.0.exe", path);
        Assert.Equal(Path.Combine(_dir, "ASH-Setup-0.2.0.exe"), got);
        Assert.Equal(setup, File.ReadAllBytes(got));
        Assert.Equal(new[] { got }, Directory.GetFiles(_dir));
    }

    [Fact] public async Task Download_Overwrites_Existing_File()
    {
        var setup = new byte[] { 9, 9, 9 };
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(Path.Combine(_dir, "ASH-Setup-0.2.0.exe"), new byte[] { 1 });
        using var c = Client(new Fake { Fn = (_, _) => Task.FromResult(Ok(setup)) });
        Assert.Equal(setup, File.ReadAllBytes(await c.DownloadVerifiedAsync(Mf(setup), _dir)));
    }

    [Fact] public async Task Download_Hash_Mismatch_Deletes_File()
    {
        var setup = new byte[] { 1, 2, 3 };
        var m = Mf(setup);
        using var c = Client(new Fake { Fn = (_, _) => Task.FromResult(Ok(new byte[] { 1, 2, 4 })) }); // same size, different content
        await Assert.ThrowsAsync<InvalidDataException>(() => c.DownloadVerifiedAsync(m, _dir));
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact] public async Task Download_Size_Mismatch_Deletes_File()
    {
        var m = Mf(new byte[] { 1, 2, 3 });
        foreach (var body in new[] { new byte[] { 1, 2 }, new byte[] { 1, 2, 3, 4 } })
        {
            using var c = Client(new Fake { Fn = (_, _) => Task.FromResult(Ok(body)) });
            await Assert.ThrowsAsync<InvalidDataException>(() => c.DownloadVerifiedAsync(m, _dir));
            Assert.Empty(Directory.GetFiles(_dir));
        }
    }

    [Fact] public async Task Download_404_Propagates_And_Leaves_Nothing()
    {
        using var c = Client(new Fake());
        await Assert.ThrowsAsync<HttpRequestException>(() => c.DownloadVerifiedAsync(Mf(new byte[] { 1 }), _dir));
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Theory]
    [InlineData("..\\evil.exe")] [InlineData("../evil.exe")] [InlineData("sub/a.exe")] [InlineData("sub\\a.exe")] [InlineData("..")] [InlineData("")] [InlineData("C:\\a.exe")] [InlineData("a..b.exe")]
    public async Task Download_Rejects_Unsafe_File_Name(string file)
    {
        bool called = false;
        using var c = Client(new Fake { Fn = (_, _) => { called = true; return Task.FromResult(Ok(new byte[] { 1 })); } });
        await Assert.ThrowsAsync<InvalidDataException>(() => c.DownloadVerifiedAsync(Mf(new byte[] { 1 }, file), _dir));
        Assert.False(called);
    }
}
