using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ASH.Core.Update;
using ASH.Tests.Release;
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

    /// <summary>Delivers one byte, then blocks until disposed (ignores cancellation tokens like a stuck net48 socket read).</summary>
    private sealed class StallingStream : Stream
    {
        private readonly TaskCompletionSource<int> _tcs = new();
        private bool _first = true;
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_first) { _first = false; buffer[offset] = 7; return Task.FromResult(1); }
            return _tcs.Task;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _tcs.TrySetException(new ObjectDisposedException("stall")); base.Dispose(disposing); }
    }

    private static HttpResponseMessage Ok(byte[] b) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(b) };
    private static UpdateClient Client(Fake f, int ms = 3000) => new(new Uri("http://srv:5080/"), TimeSpan.FromMilliseconds(ms), f);
    private static string Hex(byte[] b) { using var s = SHA256.Create(); return string.Concat(s.ComputeHash(b).Select(x => x.ToString("x2"))); }

    // odd spacing on purpose: the client must hand back the bytes untouched, not a re-serialization
    private static readonly byte[] Json = Encoding.UTF8.GetBytes("{ \"version\" : \"0.2.0\", \"file\":\"ASH-Setup-0.2.0.exe\", \"sha256\":\"" + new string('a', 64) + "\", \"size\": 3 }\n");

    private static ReleaseManifest Mf(byte[] setup, string file = "ASH-Setup-0.2.0.exe") => new() { Version = "0.2.0", File = file, Sha256 = Hex(setup), Size = setup.Length };
    private static FetchedManifest Fm(ReleaseManifest m, byte[]? sig = null) => new(m.ToJson(), sig);

    // signed vector: valid manifest for the 10-byte "fake setup", signed by the key in SigVector.PubKey2
    private static readonly byte[] VecJson = Convert.FromBase64String(SigVector.ManifestB64);
    private static readonly byte[] VecSig = Convert.FromBase64String(SigVector.SigB64);
    private static readonly byte[] VecSetup = Encoding.ASCII.GetBytes(SigVector.SetupText);
    private static ManifestSignature VecKey => new(SigVector.PubKey2);

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

    [Fact] public async Task GetManifest_Redirect_Is_An_Error()
    {
        var f = new Fake { Fn = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)) };
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

    [Fact] public async Task GetManifest_Over_1MB_Is_Rejected()
    {
        var f = new Fake { Fn = (_, _) => Task.FromResult(Ok(new byte[2 << 20])) };
        using var c = Client(f);
        await Assert.ThrowsAnyAsync<Exception>(() => c.GetManifestAsync());
    }

    [Fact] public async Task GetManifest_Invalid_Manifest_Is_InvalidData()
    {
        var f = new Fake { Fn = (_, _) => Task.FromResult(Ok(Encoding.UTF8.GetBytes("{\"version\":\"1.0.0\",\"file\":\"../x.exe\",\"sha256\":\"" + new string('a', 64) + "\"}"))) };
        using var c = Client(f);
        await Assert.ThrowsAsync<InvalidDataException>(() => c.GetManifestAsync());
    }

    [Fact] public async Task Download_Ok_Renames_And_Leaves_No_Part()
    {
        var setup = Enumerable.Range(0, 300000).Select(i => (byte)(i * 7)).ToArray();
        string? path = null;
        var f = new Fake { Fn = (r, _) => { path = r.RequestUri!.AbsolutePath; return Task.FromResult(Ok(setup)); } };
        using var c = Client(f);
        string got = await c.DownloadVerifiedAsync(Fm(Mf(setup)), null, _dir);
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
        Assert.Equal(setup, File.ReadAllBytes(await c.DownloadVerifiedAsync(Fm(Mf(setup)), null, _dir)));
    }

    [Fact] public async Task Download_Hash_Mismatch_Deletes_File()
    {
        var m = Fm(Mf(new byte[] { 1, 2, 3 }));
        using var c = Client(new Fake { Fn = (_, _) => Task.FromResult(Ok(new byte[] { 1, 2, 4 })) }); // same size, different content
        await Assert.ThrowsAsync<InvalidDataException>(() => c.DownloadVerifiedAsync(m, null, _dir));
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact] public async Task Download_Size_Mismatch_Deletes_File()
    {
        var m = Fm(Mf(new byte[] { 1, 2, 3 }));
        foreach (var body in new[] { new byte[] { 1, 2 }, new byte[] { 1, 2, 3, 4 } })
        {
            using var c = Client(new Fake { Fn = (_, _) => Task.FromResult(Ok(body)) });
            await Assert.ThrowsAsync<InvalidDataException>(() => c.DownloadVerifiedAsync(m, null, _dir));
            Assert.Empty(Directory.GetFiles(_dir));
        }
    }

    [Fact] public async Task Download_404_Propagates_And_Leaves_Nothing()
    {
        using var c = Client(new Fake());
        await Assert.ThrowsAsync<HttpRequestException>(() => c.DownloadVerifiedAsync(Fm(Mf(new byte[] { 1 })), null, _dir));
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact] public async Task Download_Stalled_Body_Times_Out_Within_Idle_Timeout()
    {
        var m = Fm(Mf(new byte[] { 7, 8, 9 }));
        using var c = Client(new Fake { Fn = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) }) }, 300);
        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => c.DownloadVerifiedAsync(m, null, _dir));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), "took " + sw.Elapsed);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Theory]
    [InlineData("..\\evil.exe")] [InlineData("../evil.exe")] [InlineData("sub/a.exe")] [InlineData("sub\\a.exe")] [InlineData("..")] [InlineData("C:\\a.exe")]
    [InlineData("a.exe")] [InlineData("ASH-Setup-1.0.0.msi")] [InlineData("ASH-Setup-1.0.0.exe ")] [InlineData("ASH-Setup-.exe")] [InlineData("ASH-Setup-1/../2.exe")]
    public async Task Unsafe_File_Name_Is_Rejected_Before_Any_Request(string file)
    {
        bool called = false;
        using var c = Client(new Fake { Fn = (_, _) => { called = true; return Task.FromResult(Ok(new byte[] { 1 })); } });
        await Assert.ThrowsAsync<InvalidDataException>(async () => await c.DownloadVerifiedAsync(Fm(Mf(new byte[] { 1 }, file)), null, _dir));
        Assert.False(called);
    }

    // ---- signature policy ----

    private Fake VecServer(Action? onCall = null) => new() { Fn = (_, _) => { onCall?.Invoke(); return Task.FromResult(Ok(VecSetup)); } };

    [Fact] public async Task Signed_Download_With_Valid_Signature_Passes()
    {
        using var c = Client(VecServer());
        string p = await c.DownloadVerifiedAsync(new FetchedManifest(VecJson, VecSig), VecKey, _dir);
        Assert.Equal(VecSetup, File.ReadAllBytes(p));
    }

    [Fact] public async Task Key_Present_But_Sig_Missing_Is_Rejected_Without_Request()
    {
        bool called = false;
        using var c = Client(VecServer(() => called = true));
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => c.DownloadVerifiedAsync(new FetchedManifest(VecJson, null), VecKey, _dir));
        Assert.Contains("서명 확인 실패", ex.Message);
        Assert.False(called);
    }

    [Fact] public async Task Key_Present_And_Bad_Sig_Is_Rejected()
    {
        var bad = (byte[])VecSig.Clone(); bad[20] ^= 1;
        using var c = Client(VecServer());
        await Assert.ThrowsAsync<InvalidDataException>(() => c.DownloadVerifiedAsync(new FetchedManifest(VecJson, bad), VecKey, _dir));
    }

    [Fact] public async Task Key_Present_And_Tampered_Manifest_Is_Rejected()
    {
        var tampered = (byte[])VecJson.Clone();
        tampered[VecJson.Length - 5] ^= 0x01; // inside "notes"; still a parseable manifest
        Assert.Equal("0.1.0", new FetchedManifest(tampered, VecSig).Manifest.Version);
        using var c = Client(VecServer());
        await Assert.ThrowsAsync<InvalidDataException>(() => c.DownloadVerifiedAsync(new FetchedManifest(tampered, VecSig), VecKey, _dir));
    }

    [Fact] public async Task Key_Present_And_Other_Key_Is_Rejected()
    {
        using var c = Client(VecServer());
        await Assert.ThrowsAsync<InvalidDataException>(() => c.DownloadVerifiedAsync(new FetchedManifest(VecJson, VecSig), new ManifestSignature(SigVector.PubKey1), _dir));
    }

    [Fact] public async Task No_Key_Is_The_Unsigned_Path_And_Needs_No_Sig()
    {
        using var c = Client(VecServer());
        string p = await c.DownloadVerifiedAsync(new FetchedManifest(VecJson, null), null, _dir);
        Assert.Equal(VecSetup, File.ReadAllBytes(p));
    }

    [Fact] public void FetchedManifest_Manifest_Always_Comes_From_Json_And_Ctor_Is_Not_Public()
    {
        Assert.Empty(typeof(FetchedManifest).GetConstructors());
        var f = new FetchedManifest(VecJson, VecSig);
        Assert.Equal(ReleaseManifest.Parse(f.Json).Sha256, f.Manifest.Sha256);
        Assert.Null(typeof(FetchedManifest).GetProperty(nameof(FetchedManifest.Manifest))!.SetMethod);
    }
}
