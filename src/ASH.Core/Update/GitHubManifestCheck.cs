using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ASH.Core.Update;

/// <summary>github.com 대조 실패(접속 불가 포함). 메시지가 그대로 명령줄에 나간다.</summary>
public sealed class GitHubCheckException : Exception
{
    public GitHubCheckException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// 서명 대신 쓰는 무결성 확인: 사내 서버가 준 매니페스트 바이트가 github.com 릴리스 v{version}의 같은 이름 파일과 정확히 같아야 한다.
/// github.com은 HTTPS라 사내망에서 바꿔치기할 수 없고, 매니페스트의 SHA-256이 설치 파일·zip을 묶는다.
/// api.github.com은 쓰지 않는다(사내망 차단). 고정 주소 {Repo}/releases/download/v{version}/{name}만 받는다.
/// </summary>
public sealed class GitHubManifestCheck
{
    public const string DefaultRepo = "https://github.com/Leepy0/ASH";
    private const int MaxBytes = 1 << 20;

    private readonly TimeSpan _timeout;
    private readonly HttpMessageHandler? _handler;

    public string Repo { get; }

    /// <param name="handler">테스트용. null이면 PC의 프록시 설정(로그인 계정 자격 증명)을 쓰고 리디렉션을 따라간다.</param>
    public GitHubManifestCheck(TimeSpan timeout, HttpMessageHandler? handler = null, string repo = DefaultRepo)
    {
        _timeout = timeout;
        _handler = handler;
        Repo = repo.TrimEnd('/');
    }

    public string ManifestUrl(string version, string name) => $"{Repo}/releases/download/v{Uri.EscapeDataString(version)}/{name}";

    /// <exception cref="GitHubCheckException">접속 불가, 릴리스·파일 없음, 내용이 다름.</exception>
    public async Task VerifyAsync(byte[] json, string version, string name, CancellationToken ct = default)
    {
        var url = ManifestUrl(version, name);
        byte[]? remote;
        try { remote = await GetAsync(url, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException || e is WebException || e is IOException)
        {
            throw new GitHubCheckException("github.com에 접속할 수 없어 배포 정보를 대조하지 못했습니다", e);
        }
        if (remote == null) throw new GitHubCheckException($"github.com에 v{version}의 {name}이(가) 없습니다");
        if (!remote.SequenceEqual(json)) throw new GitHubCheckException($"서버의 {name}이(가) github.com v{version}과 다릅니다");
    }

    // 404면 null
    private async Task<byte[]?> GetAsync(string url, CancellationToken ct)
    {
        using var http = new HttpClient(_handler ?? DefaultHandler(), disposeHandler: _handler == null) { Timeout = Timeout.InfiniteTimeSpan };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);
        using var r = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        r.EnsureSuccessStatusCode();
        if (r.Content.Headers.ContentLength > MaxBytes) throw new IOException("배포 정보가 너무 큽니다: " + url);
        // net48은 본문 읽기 중 토큰을 무시하므로 응답을 닫아 중단
        using var reg = cts.Token.Register(() => r.Dispose());
        using var s = await r.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buf = new byte[8192];
        int n;
        while ((n = await s.ReadAsync(buf, 0, buf.Length, cts.Token).ConfigureAwait(false)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ms.Length > MaxBytes) throw new IOException("배포 정보가 너무 큽니다: " + url);
        }
        return ms.ToArray();
    }

    // github.com은 사내 프록시를 거칠 수 있으니 PC 설정을 따른다(LAN 서버용 UpdateClient와 다름)
    internal static HttpClientHandler DefaultHandler() => new HttpClientHandler
    {
        AllowAutoRedirect = true,
        UseProxy = true,
        DefaultProxyCredentials = CredentialCache.DefaultCredentials,
    };
}
