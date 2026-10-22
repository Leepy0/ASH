using System.Net;
using System.Net.Http;
using System.Text;
using ASH.Core.Update;
using ASH.Tests.Release;

namespace ASH.Tests.Update;

public class UpdateFlowTests
{
    private sealed class Fake : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Fn = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Fn(r, ct);
    }

    private static readonly Uri Srv = new("http://srv:5080/");
    private static readonly byte[] VecJson = Convert.FromBase64String(SigVector.ManifestB64);   // version 0.1.0, minSupported 0.1.0
    private static readonly byte[] VecSig = Convert.FromBase64String(SigVector.SigB64);

    private static Fake Serving(byte[] json, byte[]? sig) => new()
    {
        Fn = (r, _) => Task.FromResult(r.RequestUri!.AbsolutePath switch
        {
            "/api/ash/version" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(json) },
            "/api/ash/version.sig" when sig != null => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(sig) },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        }),
    };

    private static byte[] Json(string version, string min) =>
        new ReleaseManifest { Version = version, MinSupported = min, File = "ASH-Setup-" + version + ".exe", Sha256 = new string('a', 64), Size = 3 }.ToJson();

    private static Task<string?> Run(Fake f, string current, ManifestSignature? key = null, int ms = 3000) =>
        UpdateFlow.StartupMessageAsync(Srv, TimeSpan.FromMilliseconds(ms), key, current, f);

    [Fact] public async Task Available() =>
        Assert.Equal("ASH 0.2.0 사용 가능 — AGVUPDATE로 받기", await Run(Serving(Json("0.2.0", "0.1.0"), null), "0.1.0+abc123"));

    [Fact] public async Task UpToDate_Says_Nothing() => Assert.Null(await Run(Serving(Json("0.1.0", ""), null), "0.1.0"));

    [Fact] public async Task BelowMinimum_Names_The_Minimum() =>
        Assert.Equal("현재 버전이 최소 지원(0.1.0)보다 낮습니다 — AGVUPDATE", await Run(Serving(VecJson, VecSig), "0.0.9", new ManifestSignature(SigVector.PubKey2)));

    [Fact] public async Task ServerOlder_Is_One_Line()
    {
        var m = await Run(Serving(Json("0.1.0", ""), null), "0.3.0");
        Assert.NotNull(m);
        Assert.DoesNotContain('\n', m!);
    }

    [Fact] public async Task Signed_Ok_Is_Silent_When_Current() =>
        Assert.Null(await Run(Serving(VecJson, VecSig), "0.1.0", new ManifestSignature(SigVector.PubKey2)));

    [Fact] public async Task Wrong_Key_Is_Rejected() =>
        Assert.Equal("업데이트 확인 실패(서명 확인 실패)", await Run(Serving(VecJson, VecSig), "0.0.1", new ManifestSignature(SigVector.PubKey1)));

    [Fact] public async Task Key_But_No_Sig_Is_Rejected() =>
        Assert.Equal("업데이트 확인 실패(서명 확인 실패)", await Run(Serving(VecJson, null), "0.0.1", new ManifestSignature(SigVector.PubKey2)));

    [Fact] public async Task Startup_Check_Timeout_Does_Not_Throw()
    {
        var f = new Fake { Fn = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); } };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var m = await Run(f, "0.1.0", ms: 300);
        Assert.Equal("업데이트 확인 실패(서버 응답 없음)", m);
        // generous for busy CI runners (both target frameworks test at once); a hang would never return at all
        Assert.True(sw.ElapsedMilliseconds < 10000, "gave up in time: " + sw.ElapsedMilliseconds);
    }

    [Fact] public async Task Connection_Refused_Does_Not_Throw()
    {
        var f = new Fake { Fn = (_, _) => throw new HttpRequestException("refused") };
        Assert.Equal("업데이트 확인 실패(서버 응답 없음)", await Run(f, "0.1.0"));
    }

    [Fact] public async Task Garbage_Manifest_Does_Not_Throw()
    {
        var m = await Run(Serving(Encoding.UTF8.GetBytes("<html>"), null), "0.1.0");
        Assert.StartsWith("업데이트 확인 실패(", m);
        Assert.DoesNotContain('\n', m!);
    }

    [Fact] public async Task Bad_Current_Version_Does_Not_Throw() =>
        Assert.StartsWith("업데이트 확인 실패(", await Run(Serving(Json("0.2.0", ""), null), "not-a-version"));

    [Fact] public void LoadKey_Missing_Resource_Is_Unsigned_Mode() => Assert.Equal((null, null), UpdateFlow.LoadKey(null));

    [Fact] public void LoadKey_Good_Resource_Gives_Key()
    {
        var (k, e) = UpdateFlow.LoadKey(new MemoryStream(Encoding.UTF8.GetBytes(SigVector.PubKey1)));
        Assert.NotNull(k);
        Assert.Null(e);
    }

    [Fact] public void LoadKey_Bad_Resource_Is_Error_Not_Unsigned()
    {
        var (k, e) = UpdateFlow.LoadKey(new MemoryStream(Encoding.UTF8.GetBytes("not a key")));
        Assert.Null(k);
        Assert.StartsWith("서명 키 오류", e);
    }

    [Fact] public void DownloadFailed_Locked_File_Says_Close_Installer()
    {
        Assert.Contains("실행 중이면 닫고", UpdateMessages.DownloadFailed(new IOException("x")));
        Assert.Contains("실행 중이면 닫고", UpdateMessages.DownloadFailed(new UnauthorizedAccessException("x")));
        Assert.Equal("다운로드 실패(SHA-256이 다릅니다)", UpdateMessages.DownloadFailed(new InvalidDataException("SHA-256이 다릅니다")));
    }

    [Fact] public void DownloadFailed_Total_Timeout_Differs_From_Idle_Cancel()
    {
        Assert.Equal("다운로드 시간 초과(5분)", UpdateMessages.DownloadFailed(new OperationCanceledException(), totalTimeout: true));
        Assert.Equal("다운로드 실패(서버 응답 없음)", UpdateMessages.DownloadFailed(new OperationCanceledException()));
        Assert.Equal("업데이트 확인 실패(서버 응답 없음)", UpdateMessages.CheckFailed(new TimeoutException()));
    }

    [Fact] public void Tls_Failure_Gets_Certificate_Message()
    {
        var tls = new HttpRequestException("ssl", new System.Security.Authentication.AuthenticationException("The remote certificate is invalid"));
        Assert.Equal("업데이트 확인 실패(서버 인증서 오류 — 사내 인증서 신뢰 여부 확인)", UpdateMessages.CheckFailed(tls));
        Assert.Contains("서버 인증서 오류", UpdateMessages.DownloadFailed(new AggregateException(tls)));
        Assert.Equal("서버 응답 없음", UpdateMessages.Reason(new HttpRequestException("refused"))); // other HTTP failures keep the old text
    }

    [Fact] public void Default_Handler_Skips_Proxy_And_Redirects()
    {
        var h = UpdateClient.DefaultHandler();
        Assert.False(h.UseProxy);
        Assert.False(h.AllowAutoRedirect);
    }
}
