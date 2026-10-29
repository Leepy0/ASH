using System.Net;
using System.Net.Sockets;
using System.Reflection;
using ASH.Core.Update;

namespace ASH.Server;

/// <summary>
/// Pulls new releases from GitHub, the same way AGV_FOTA_GATEWAY updates itself: the intranet blocks api.github.com but not github.com,
/// so only the fixed release URLs are used: {BaseUrl}/releases/latest/download/ASH.json(.sig), then {BaseUrl}/releases/download/v{version}/{file}.
/// Everything goes through <see cref="ReleaseStore.PublishAsync(byte[], byte[], string, Stream, bool, CancellationToken)"/>,
/// so a pulled release passes exactly the upload checks (signature, size, SHA-256, no downgrade). No admin token is needed.
/// Settings (section "GitHub"): Enabled (true), BaseUrl, CheckIntervalMin (60, 0 = at start only), Proxy ("" = system, "none" = direct, or a URL),
/// Dns ("" = the PC's DNS, or DNS server IPs such as "8.8.8.8, 1.1.1.1": GitHub names are looked up there first, then with the PC's DNS).
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
    private readonly RunnerStore? _runner;
    private readonly NoticeStore? _notices;

    /// <summary>Null when the pull is off (GitHub:Enabled=false).</summary>
    public string? BaseUrl { get; }
    public TimeSpan Interval { get; }
    /// <summary>DNS servers used for GitHub names; empty = the PC's DNS.</summary>
    public IReadOnlyList<IPAddress> DnsServers { get; private init; } = [];
    public string LastResult { get; private set; } = "아직 확인하지 않음";
    public string RunnerResult { get; private set; } = "아직 확인하지 않음";
    public string NotesResult { get; private set; } = "아직 확인하지 않음";
    public DateTime? LastCheckUtc { get; private set; }

    public GitHubSync(ReleaseStore store, RunnerStore runner, ServerUpdater updater, NoticeStore notices, IConfiguration cfg, ILogger<GitHubSync> log)
        : this(store, cfg.GetValue("GitHub:Enabled", true) ? Url(cfg["GitHub:BaseUrl"]) : null,
               TimeSpan.FromMinutes(Math.Max(0, cfg.GetValue("GitHub:CheckIntervalMin", 60))), new HttpClient(Handler(cfg["GitHub:Proxy"], DnsLookup.ParseServers(cfg["GitHub:Dns"]))), log, updater, runner, notices)
        => DnsServers = DnsLookup.ParseServers(cfg["GitHub:Dns"]);

    internal GitHubSync(ReleaseStore store, string? baseUrl, TimeSpan interval, HttpClient http, ILogger log, ServerUpdater? updater = null, RunnerStore? runner = null, NoticeStore? notices = null)
    {
        _store = store;
        _updater = updater;
        _runner = runner;
        _notices = notices;
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
    private static HttpMessageHandler Handler(string? proxy, IPAddress[] dns)
    {
        var h = new SocketsHttpHandler { AllowAutoRedirect = true, DefaultProxyCredentials = CredentialCache.DefaultCredentials };
        if (string.Equals(proxy?.Trim(), "none", StringComparison.OrdinalIgnoreCase)) h.UseProxy = false;
        else if (!string.IsNullOrWhiteSpace(proxy)) h.Proxy = new WebProxy(proxy.Trim()) { UseDefaultCredentials = true };
        // every connection (github.com, the release-assets redirect host, or the proxy) is looked up with GitHub:Dns first, then the PC's DNS
        if (dns.Length > 0) h.ConnectCallback = async (ctx, ct) =>
        {
            var host = ctx.DnsEndPoint.Host;
            var addrs = IPAddress.TryParse(host, out var ip) ? [ip] : await DnsLookup.ResolveAsync(host, dns, ct);
            if (addrs.Length == 0) addrs = await Dns.GetHostAddressesAsync(host, ct);
            var s = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await s.ConnectAsync(addrs, ctx.DnsEndPoint.Port, ct);
                return new NetworkStream(s, ownsSocket: true);
            }
            catch { s.Dispose(); throw; }
        };
        return h;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (BaseUrl == null) return;
        _log.LogInformation("GitHub 릴리스 확인: {Url} ({Min}분마다, DNS {Dns})", BaseUrl, Interval.TotalMinutes, DnsServers.Count == 0 ? "PC 기본" : string.Join(", ", DnsServers));
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

    /// <summary>Step by step state of the running (or last) check, for the settings page.</summary>
    public CheckProgress Progress { get; } = new();

    /// <summary>The "지금 확인" button: runs one check in the background. False when the pull is off or a check is already running (none is started).</summary>
    public bool StartCheck(CancellationToken stop)
    {
        if (BaseUrl == null || !Progress.TryBegin()) return false;
        _ = Task.Run(async () =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stop);
            cts.CancelAfter(TimeSpan.FromMinutes(15)); // a new setup is downloaded here
            try { await RunAsync(cts.Token, begun: true); }
            catch (OperationCanceledException) { _log.LogWarning("최신 버전 확인 중단(종료 또는 15분 초과)"); }
            catch (Exception e) { _log.LogError(e, "최신 버전 확인 오류"); }
        }, CancellationToken.None);
        return true;
    }

    /// <summary>One check; never throws except on cancellation. The outcome is in <see cref="LastResult"/>.</summary>
    public Task CheckOnceAsync(CancellationToken ct) => RunAsync(ct, begun: false);

    // one check at a time (the schedule and the button share the gate); begun = the caller already claimed Progress
    private async Task RunAsync(CancellationToken ct, bool begun)
    {
        if (BaseUrl == null) return;
        var gated = false;
        var owns = begun;
        try
        {
            await _gate.WaitAsync(ct);
            gated = true;
            if (!begun && !(owns = Progress.TryBegin())) return; // a button check was claimed meanwhile
            await RunChecks(ct);
        }
        finally
        {
            if (owns) Progress.End();
            if (gated) _gate.Release();
        }
    }

    private async Task RunChecks(CancellationToken ct)
    {
        try { LastResult = await Check(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (UploadException e) { LastResult = "거절: " + e.Message; }
        catch (Exception e) // the service must keep running; the next check retries
        {
            LastResult = "실패: " + (e is OperationCanceledException ? "응답 시간 초과" : e.Message);
        }
        LastCheckUtc = DateTime.UtcNow;
        Progress.Settle(CheckProgress.PluginCheck, CheckProgress.PluginGet, LastResult);
        if (LastResult.StartsWith("거절") || LastResult.StartsWith("실패")) _log.LogWarning("GitHub 릴리스 확인 {Result}", LastResult);
        else _log.LogInformation("GitHub 릴리스 확인 {Result}", LastResult);
        await CheckRunnerAsync(ct);
        await CheckNotesAsync(ct);
        // the server's own update last: it may stop this process
        if (_updater != null) await _updater.CheckAsync(_http, BaseUrl!, ct, Progress);
    }

    // ASH.Notes.json of the latest release (every version's notes): into the store as drafts, an admin approves them. 404 = an older release without notes.
    private async Task CheckNotesAsync(CancellationToken ct)
    {
        if (_notices == null || BaseUrl == null) return;
        Progress.Start(CheckProgress.Notes);
        try
        {
            var json = await GetSmall(_http, $"{BaseUrl}/releases/latest/download/{NotesFile.FileName}", MaxManifestBytes, ct);
            NotesResult = json == null ? $"최신 릴리스에 {NotesFile.FileName} 없음" : $"받음: 새로 오거나 바뀐 버전 {_notices.Pull(NotesFile.Parse(json))}개";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) { NotesResult = "실패: " + (e is OperationCanceledException ? "응답 시간 초과" : e.Message); }
        Progress.Settle(CheckProgress.Notes, CheckProgress.Notes, NotesResult);
        if (NotesResult.StartsWith("실패")) _log.LogWarning("공지 가져오기 {Result}", NotesResult);
        else _log.LogInformation("공지 가져오기 {Result}", NotesResult);
    }

    private async Task CheckRunnerAsync(CancellationToken ct)
    {
        if (_runner == null || BaseUrl == null) return;
        try { RunnerResult = await PullRunner(_runner, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (UploadException e) { RunnerResult = "거절: " + e.Message; }
        catch (Exception e) { RunnerResult = "실패: " + (e is OperationCanceledException ? "응답 시간 초과" : e.Message); }
        Progress.Settle(CheckProgress.RunnerCheck, CheckProgress.RunnerGet, RunnerResult);
        if (RunnerResult.StartsWith("거절") || RunnerResult.StartsWith("실패")) _log.LogWarning("Runner 가져오기 {Result}", RunnerResult);
        else _log.LogInformation("Runner 가져오기 {Result}", RunnerResult);
    }

    // same rule as the setup: the manifest from "latest", the signature and zip from its tag; the store verifies everything before it keeps a byte
    private async Task<string> PullRunner(RunnerStore store, CancellationToken ct)
    {
        Progress.Start(CheckProgress.RunnerCheck);
        var json = await GetSmall(_http, $"{BaseUrl}/releases/latest/download/{PackageManifest.RunnerManifestName}", MaxManifestBytes, ct);
        if (json == null) return $"최신 릴리스에 {PackageManifest.RunnerManifestName} 없음";
        var m = PackageManifest.ParseRunner(json);
        var cur = store.Current;
        if (cur != null && !m.IsNewerThan(cur.Version)) return $"최신({cur.Version})";
        Progress.Finish(CheckProgress.RunnerCheck, $"새 버전 {m.Version}");
        var tag = $"{BaseUrl}/releases/download/v{Uri.EscapeDataString(m.Version)}";
        var sig = await GetSmall(_http, $"{tag}/{PackageManifest.RunnerSignatureName}", MaxSignatureBytes, ct);
        Progress.Start(CheckProgress.RunnerGet, m.Size);
        using var resp = await _http.GetAsync($"{tag}/{Uri.EscapeDataString(m.File)}", HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"Runner zip을 받을 수 없습니다({(int)resp.StatusCode}).");
        await using var zip = new CountingStream(await resp.Content.ReadAsStreamAsync(ct), n => Progress.Bytes(CheckProgress.RunnerGet, n));
        var pub = await store.PublishAsync(json, sig, m.File, zip, ct);
        return $"가져옴: {pub.Version}";
    }

    private async Task<string> Check(CancellationToken ct)
    {
        Progress.Start(CheckProgress.PluginCheck);
        var json = await GetSmall(_http, $"{BaseUrl}/releases/latest/download/{ReleaseManifest.FileName}", MaxManifestBytes, ct);
        if (json == null) return $"GitHub에 {ReleaseManifest.FileName}이(가) 있는 릴리스가 없습니다.";
        var m = ReleaseManifest.Parse(json);
        if (!SemVersion.TryParse(m.Version, out var latest)) throw new InvalidDataException("GitHub 배포 정보의 version이 올바르지 않습니다: " + m.Version);
        var cur = _store.GetCurrent()?.Manifest;
        if (cur != null && SemVersion.TryParse(cur.Version, out var have) && latest!.CompareTo(have) <= 0)
            return latest.CompareTo(have) == 0 ? $"최신({cur.Version})" : $"서버 버전({cur.Version})이 GitHub({m.Version})보다 높음, 그대로 둠";

        Progress.Finish(CheckProgress.PluginCheck, $"새 버전 {m.Version}");
        // signature and setup from the tag, not "latest": a release published between two requests cannot mix files
        var tag = $"{BaseUrl}/releases/download/v{Uri.EscapeDataString(m.Version)}";
        var sig = await GetSmall(_http, $"{tag}/{ReleaseManifest.SignatureFileName}", MaxSignatureBytes, ct);
        var url = $"{tag}/{Uri.EscapeDataString(m.File)}";
        Progress.Start(CheckProgress.PluginGet, m.Size);
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"설치 파일을 받을 수 없습니다({(int)resp.StatusCode}): {url}");
        await using var setup = new CountingStream(await resp.Content.ReadAsStreamAsync(ct), n => Progress.Bytes(CheckProgress.PluginGet, n));
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
