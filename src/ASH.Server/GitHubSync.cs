using System.Net;
using System.Reflection;
using ASH.Core.Update;

namespace ASH.Server;

/// <summary>
/// Pulls new releases from GitHub, the same way AGV_FOTA_GATEWAY updates itself: the intranet blocks api.github.com but not github.com,
/// so only the fixed release URLs are used: {BaseUrl}/releases/latest/download/ASH.json(.sig), then {BaseUrl}/releases/download/v{version}/{file}.
/// Everything goes through <see cref="ReleaseStore.PublishAsync(byte[], byte[], string, Stream, bool, CancellationToken)"/>,
/// so a pulled release passes exactly the upload checks (signature, size, SHA-256, no downgrade). No admin token is needed.
/// Settings (section "GitHub"): Enabled (true), BaseUrl, CheckIntervalMin (60, 0 = at start only), Proxy ("" = system, "none" = direct, or a URL).
/// </summary>
public sealed class GitHubSync : BackgroundService
{
    public const string DefaultBaseUrl = "https://github.com/Leepy0/ASH";
    private const int MaxManifestBytes = 1 << 20, MaxSignatureBytes = 4096;
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(10);

    private readonly ReleaseStore _store;
    private readonly HttpClient _http;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ServerUpdater? _updater;

    /// <summary>Null when the pull is off (GitHub:Enabled=false).</summary>
    public string? BaseUrl { get; }
    public TimeSpan Interval { get; }
    public string LastResult { get; private set; } = "아직 확인하지 않음";
    public DateTime? LastCheckUtc { get; private set; }

    public GitHubSync(ReleaseStore store, ServerUpdater updater, IConfiguration cfg, ILogger<GitHubSync> log)
        : this(store, cfg.GetValue("GitHub:Enabled", true) ? Url(cfg["GitHub:BaseUrl"]) : null,
               TimeSpan.FromMinutes(Math.Max(0, cfg.GetValue("GitHub:CheckIntervalMin", 60))), new HttpClient(Handler(cfg["GitHub:Proxy"])), log, updater) { }

    internal GitHubSync(ReleaseStore store, string? baseUrl, TimeSpan interval, HttpClient http, ILogger log, ServerUpdater? updater = null)
    {
        _store = store;
        _updater = updater;
        BaseUrl = baseUrl;
        Interval = interval;
        _http = http;
        _http.Timeout = TimeSpan.FromMinutes(10);
        var ver = typeof(GitHubSync).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0";
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ASH.Server/" + ver.Split('+')[0]);
        _log = log;
        if (BaseUrl == null) LastResult = "꺼짐(GitHub:Enabled=false)";
    }

    /// <summary>https only (a signature protects the content, TLS keeps a proxy from swapping the version we see); plain http only to this PC (loopback, for tests).</summary>
    internal static string Url(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return DefaultBaseUrl;
        if (!Uri.TryCreate(s.Trim(), UriKind.Absolute, out var u) || !(u.Scheme == Uri.UriSchemeHttps || (u.Scheme == Uri.UriSchemeHttp && u.IsLoopback)) || !string.IsNullOrEmpty(u.UserInfo))
            throw new ArgumentException($"GitHub:BaseUrl은 https 저장소 주소여야 합니다(예: {DefaultBaseUrl}): {s}");
        return u.AbsoluteUri.TrimEnd('/');
    }

    // a service runs as LocalSystem: it has no user proxy settings, so the machine's (or an explicit) proxy is used with the computer account
    private static HttpMessageHandler Handler(string? proxy)
    {
        var h = new SocketsHttpHandler { AllowAutoRedirect = true, DefaultProxyCredentials = CredentialCache.DefaultCredentials };
        if (string.Equals(proxy?.Trim(), "none", StringComparison.OrdinalIgnoreCase)) h.UseProxy = false;
        else if (!string.IsNullOrWhiteSpace(proxy)) h.Proxy = new WebProxy(proxy.Trim()) { UseDefaultCredentials = true };
        return h;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (BaseUrl == null) return;
        _log.LogInformation("GitHub 릴리스 확인: {Url} ({Min}분마다)", BaseUrl, Interval.TotalMinutes);
        try
        {
            await Task.Delay(FirstDelay, stop);
            while (true)
            {
                await CheckOnceAsync(stop);
                if (Interval <= TimeSpan.Zero) return;
                await Task.Delay(Interval, stop);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    /// <summary>One check; never throws except on cancellation. The outcome is in <see cref="LastResult"/>.</summary>
    public async Task CheckOnceAsync(CancellationToken ct)
    {
        if (BaseUrl == null) return;
        await _gate.WaitAsync(ct);
        try { LastResult = await Check(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (UploadException e) { LastResult = "거절: " + e.Message; }
        catch (Exception e) // the service must keep running; the next check retries
        {
            LastResult = "실패: " + (e is OperationCanceledException ? "응답 시간 초과" : e.Message);
        }
        finally
        {
            LastCheckUtc = DateTime.UtcNow;
            _gate.Release();
        }
        if (LastResult.StartsWith("거절") || LastResult.StartsWith("실패")) _log.LogWarning("GitHub 릴리스 확인 {Result}", LastResult);
        else _log.LogInformation("GitHub 릴리스 확인 {Result}", LastResult);
        // the server's own update last: it may stop this process
        if (_updater != null) await _updater.CheckAsync(_http, BaseUrl, ct);
    }

    private async Task<string> Check(CancellationToken ct)
    {
        var json = await GetSmall(_http, $"{BaseUrl}/releases/latest/download/{ReleaseManifest.FileName}", MaxManifestBytes, ct);
        if (json == null) return $"GitHub에 {ReleaseManifest.FileName}이(가) 있는 릴리스가 없습니다.";
        var m = ReleaseManifest.Parse(json);
        if (!SemVersion.TryParse(m.Version, out var latest)) throw new InvalidDataException("GitHub 배포 정보의 version이 올바르지 않습니다: " + m.Version);
        var cur = _store.GetCurrent()?.Manifest;
        if (cur != null && SemVersion.TryParse(cur.Version, out var have) && latest!.CompareTo(have) <= 0)
            return latest.CompareTo(have) == 0 ? $"최신({cur.Version})" : $"서버 버전({cur.Version})이 GitHub({m.Version})보다 높음, 그대로 둠";

        // signature and setup from the tag, not "latest": a release published between two requests cannot mix files
        var tag = $"{BaseUrl}/releases/download/v{Uri.EscapeDataString(m.Version)}";
        var sig = await GetSmall(_http, $"{tag}/{ReleaseManifest.SignatureFileName}", MaxSignatureBytes, ct);
        var url = $"{tag}/{Uri.EscapeDataString(m.File)}";
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"설치 파일을 받을 수 없습니다({(int)resp.StatusCode}): {url}");
        await using var setup = await resp.Content.ReadAsStreamAsync(ct);
        var pub = await _store.PublishAsync(json, sig, m.File, setup, force: false, ct);
        return $"가져옴: {pub.Version} ({pub.File})";
    }

    /// <summary>Body of a small file, or null on 404.</summary>
    internal static async Task<byte[]?> GetSmall(HttpClient http, string url, int max, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"GitHub 응답 {(int)resp.StatusCode}: {url}");
        if (resp.Content.Headers.ContentLength > max) throw new InvalidDataException($"너무 큽니다: {url}");
        await using var s = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buf = new byte[8192];
        int n;
        while ((n = await s.ReadAsync(buf, ct)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ms.Length > max) throw new InvalidDataException($"너무 큽니다: {url}");
        }
        return ms.ToArray();
    }

    public override void Dispose()
    {
        base.Dispose();
        _http.Dispose();
    }
}
