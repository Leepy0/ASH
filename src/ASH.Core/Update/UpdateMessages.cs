using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;

namespace ASH.Core.Update;

/// <summary>Command-line texts for the startup check and AGVUPDATE, kept out of the plugin so they are testable.</summary>
public static class UpdateMessages
{
    /// <summary>One line for the startup check; null when nothing needs saying (up to date).</summary>
    public static string? Startup(UpdateStatus s, ReleaseManifest m)
    {
        switch (s.State)
        {
            case UpdateState.Available: return $"ASH {Show(m.Version)} 사용 가능 — AGVUPDATE로 받기";
            case UpdateState.BelowMinimum: return $"현재 버전이 최소 지원({Show(m.MinSupported)})보다 낮습니다 — AGVUPDATE";
            case UpdateState.UpToDate: return null;
            default: return s.Message;
        }
    }

    public static string CheckFailed(Exception e) => $"업데이트 확인 실패({Reason(e)})";

    /// <param name="totalTimeout">The caller's overall download limit fired (as opposed to an idle/connect failure).</param>
    public static string DownloadFailed(Exception e, bool totalTimeout = false) => totalTimeout ? "다운로드 시간 초과(5분)"
        : Unwrap(e) is IOException || Unwrap(e) is UnauthorizedAccessException
            ? "다운로드 실패(받을 위치의 파일을 쓸 수 없음 — 이전 설치 프로그램이 실행 중이면 닫고 다시 시도하세요)"
            : $"다운로드 실패({Reason(e)})";

    /// <summary>Short cause: connection, timeout and HTTP errors all read as "no answer from the server".</summary>
    public static string Reason(Exception e)
    {
        e = Unwrap(e);
        if (IsTlsFailure(e)) return "서버 인증서 오류 — 사내 인증서 신뢰 여부 확인";
        if (e is OperationCanceledException || e is TimeoutException || e is HttpRequestException || e is WebException || e is SocketException) return "서버 응답 없음";
        var msg = e.Message.Split('\r', '\n')[0].Trim();
        if (msg.Length == 0) msg = e.GetType().Name;
        return msg.Length > 60 ? msg.Substring(0, 60) + "…" : msg;
    }

    // https server whose certificate this PC does not trust: HttpRequestException/WebException wrapping AuthenticationException
    private static bool IsTlsFailure(Exception e)
    {
        for (var x = e; x != null; x = x.InnerException)
            if (x is AuthenticationException || (x is WebException w && (w.Status == WebExceptionStatus.TrustFailure || w.Status == WebExceptionStatus.SecureChannelFailure))) return true;
        return false;
    }

    private static Exception Unwrap(Exception e)
    {
        while (e is AggregateException a && a.InnerExceptions.Count == 1) e = a.InnerExceptions[0];
        return e;
    }

    private static string Show(string v) => SemVersion.TryParse(v, out var s) ? s!.ToString() : v;
}
