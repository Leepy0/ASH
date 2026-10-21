using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ASH.Core.Update;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Win32;

namespace ASH.Server;

/// <summary>ASH.Server.json: signed description of the server zip (same key as ASH.json; the signature covers the file bytes).</summary>
public sealed partial record ServerManifest(string Version, string File, string Sha256, long Size)
{
    public const string FileName = "ASH.Server.json";
    public const string SignatureFileName = "ASH.Server.json.sig";

    [GeneratedRegex(@"^ASH-Server-[0-9A-Za-z.+-]+-win-x64\.zip\z")] private static partial Regex FileRx();
    [GeneratedRegex(@"^[0-9a-f]{64}\z")] private static partial Regex ShaRx();

    /// <exception cref="InvalidDataException">Not a server manifest.</exception>
    public static ServerManifest Parse(byte[] json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            var r = d.RootElement;
            string S(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            if (S("product") != "ASH.Server") throw new InvalidDataException("ASH.Server.json의 product가 ASH.Server가 아닙니다.");
            var m = new ServerManifest(S("version"), S("file"), S("sha256"),
                r.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number ? sz.GetInt64() : -1);
            if (!SemVersion.TryParse(m.Version, out _)) throw new InvalidDataException("ASH.Server.json의 version이 올바르지 않습니다: " + m.Version);
            if (!FileRx().IsMatch(m.File)) throw new InvalidDataException("ASH.Server.json의 file 이름이 올바르지 않습니다: " + m.File);
            if (!ShaRx().IsMatch(m.Sha256)) throw new InvalidDataException("ASH.Server.json의 sha256은 소문자 hex 64자여야 합니다.");
            if (m.Size <= 0) throw new InvalidDataException("ASH.Server.json의 size가 없습니다.");
            return m;
        }
        catch (JsonException e) { throw new InvalidDataException("ASH.Server.json을 읽을 수 없습니다: " + e.Message, e); }
    }
}

/// <summary>
/// Server self-update, the AGV_FOTA_GATEWAY way: the signed server zip from the GitHub release is checked (signature, size, SHA-256),
/// unpacked to &lt;exe folder&gt;\update\v&lt;ver&gt;, and apply-update.bat swaps the files with the service stopped:
/// current exe -&gt; .prev, copy the new files (appsettings.json is kept), .prev -&gt; .old, start, and if /healthz/version does not answer the new
/// version within 60 s the old exe is put back (failed one kept as .bad). The result is in update\pending.txt and reported at the next start;
/// a version that failed is not tried again automatically (update\skip-auto.txt) until a newer one is released.
/// Runs only as the registered Windows service from the published single-file exe, with an embedded signing key.
/// </summary>
public sealed class ServerUpdater
{
    public const string BatchName = "apply-update.bat";
    public const int HealthTimeoutSec = 60;
    public const string TicketFile = "run-ticket.txt", StartedFile = "run-started.txt";
    private const string ExeName = "ASH.Server.exe";
    private static readonly TimeSpan BatchStartTimeout = TimeSpan.FromSeconds(5);

    private readonly ReleaseStore _store;
    private readonly IServer _server;
    private readonly IHostApplicationLifetime _life;
    private readonly ILogger _log;
    private readonly string _dir;

    public string Current { get; }
    /// <summary>Why this process does not replace itself; null = it does.</summary>
    public string? DisabledReason { get; }
    public string LastResult { get; private set; } = "아직 확인하지 않음";
    /// <summary>What the previous update run reported (pending.txt), shown on the status page.</summary>
    public string? PreviousRun { get; }
    private string SkipPath => Path.Combine(_dir, "skip-auto.txt");

    public ServerUpdater(ReleaseStore store, IConfiguration cfg, IServer server, IHostApplicationLifetime life, ILogger<ServerUpdater> log)
    {
        _store = store;
        _server = server;
        _life = life;
        _log = log;
        _dir = Path.Combine(AppContext.BaseDirectory, "update");
        Current = (typeof(ServerUpdater).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];
        DisabledReason = !cfg.GetValue("GitHub:Enabled", true) ? "GitHub 가져오기가 꺼짐"
            : !cfg.GetValue("GitHub:UpdateServer", true) ? "GitHub:UpdateServer=false"
            : store.Key == null ? "서명 공개키가 없는 빌드"
            : !OperatingSystem.IsWindows() || !WindowsServiceHelpers.IsWindowsService() ? "Windows 서비스로 실행 중이 아님(콘솔 실행)"
            : File.Exists(Path.Combine(AppContext.BaseDirectory, "ASH.Server.dll")) ? "배포용 단일 exe가 아님(개발 빌드)" // single-file publish has no dll next to the exe
            : !ServiceUsesThisExe() ? $"서비스 {ServiceCommand.ServiceName}에 등록된 exe가 지금 실행 중인 exe가 아님"
            : null;
        if (DisabledReason != null) LastResult = "자동 교체 안 함: " + DisabledReason;
        PreviousRun = ReportPreviousRun();
    }

    [SupportedOSPlatform("windows")]
    private static bool ServiceUsesThisExe()
    {
        try
        {
            var image = Registry.GetValue($@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\{ServiceCommand.ServiceName}", "ImagePath", null) as string;
            return SameExe(image, Environment.ProcessPath ?? "");
        }
        catch (Exception) { return false; }
    }

    /// <summary>One check against the GitHub release; never throws except on cancellation. Starts the swap and stops the server when a newer signed server zip is found.</summary>
    public async Task CheckAsync(HttpClient http, string baseUrl, CancellationToken ct)
    {
        if (DisabledReason != null) return;
        try { LastResult = await Check(http, baseUrl, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) { LastResult = "실패: " + (e is OperationCanceledException ? "응답 시간 초과" : e.Message); }
        if (LastResult.StartsWith("실패") || LastResult.StartsWith("거절")) _log.LogWarning("서버 자동 업데이트 {Result}", LastResult);
        else _log.LogInformation("서버 자동 업데이트 {Result}", LastResult);
    }

    private async Task<string> Check(HttpClient http, string baseUrl, CancellationToken ct)
    {
        var json = await GitHubSync.GetSmall(http, $"{baseUrl}/releases/latest/download/{ServerManifest.FileName}", 1 << 16, ct);
        if (json == null) return $"GitHub 최신 릴리스에 {ServerManifest.FileName}이(가) 없음";
        var m = ServerManifest.Parse(json);
        SemVersion.TryParse(m.Version, out var latest);
        if (!SemVersion.TryParse(Current, out var cur) || latest!.CompareTo(cur) <= 0) return $"최신({Current})";
        if (File.Exists(SkipPath) && File.ReadAllText(SkipPath).Trim() == m.Version)
            return $"{m.Version}은(는) 지난 교체에 실패해 자동 적용하지 않음(update\\apply.log 확인, 더 높은 버전이 나오면 다시 시도)";

        var tag = $"{baseUrl}/releases/download/v{Uri.EscapeDataString(m.Version)}";
        var sig = await GitHubSync.GetSmall(http, $"{tag}/{ServerManifest.SignatureFileName}", 4096, ct);
        if (sig == null) return $"거절: {ServerManifest.SignatureFileName}이(가) 없음";
        if (!_store.Key!.Verify(json, sig)) return "거절: 서버 배포 서명이 맞지 않습니다.";

        Directory.CreateDirectory(_dir);
        foreach (var f in Directory.GetFiles(_dir, "*.partial")) TryDeleteFile(f);
        var part = Path.Combine(_dir, m.File + ".partial");
        using (var resp = await http.GetAsync($"{tag}/{Uri.EscapeDataString(m.File)}", HttpCompletionOption.ResponseHeadersRead, ct))
        {
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"서버 zip을 받을 수 없습니다({(int)resp.StatusCode}).");
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            var (size, hash) = await CopyAndHash(src, part, m.Size, ct);
            if (size != m.Size || hash != m.Sha256) { TryDeleteFile(part); return "거절: 받은 서버 zip의 크기 또는 SHA-256이 서명된 정보와 다릅니다."; }
        }
        var stage = Path.Combine(_dir, "v" + m.Version);
        if (Directory.Exists(stage)) Directory.Delete(stage, true);
        ZipFile.ExtractToDirectory(part, stage); // rejects entries outside the folder (zip slip)
        TryDeleteFile(part);
        if (!File.Exists(Path.Combine(stage, ExeName))) return $"거절: 서버 zip에 {ExeName}이(가) 없음";

        await RunBatchAsync(stage, m.Version);
        return $"{m.Version} 적용 중 — 서비스가 재시작됩니다(약 10초)";
    }

    private async Task RunBatchAsync(string stage, string version)
    {
        var pending = Path.Combine(_dir, "pending.txt");
        try
        {
            File.WriteAllText(Path.Combine(_dir, BatchName), BatchText.ReplaceLineEndings("\r\n"), Encoding.ASCII); // cmd breaks on non-ASCII without a BOM
            File.WriteAllText(Path.Combine(_dir, "exclude.txt"), "appsettings.json\r\n");
            File.WriteAllText(pending, "apply " + version);
            var health = HealthUrl(_server.Features.Get<IServerAddressesFeature>()?.Addresses ?? []);
            var args = Arguments(Environment.ProcessId, AppContext.BaseDirectory, stage, ServiceCommand.ServiceName, health, version);
            File.Delete(Path.Combine(_dir, StartedFile));
            File.WriteAllText(Path.Combine(_dir, TicketFile), "");
            var psi = new ProcessStartInfo("cmd.exe", LaunchArguments(Path.Combine(_dir, BatchName), args))
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = _dir };
            using (Process.Start(psi)) { }
            if (!await WaitStartedAsync(_dir, BatchStartTimeout))
                throw new InvalidOperationException($"교체 배치가 {BatchStartTimeout.TotalSeconds:0}초 안에 시작되지 않음 — 서버는 그대로 실행합니다(update\\apply.log 확인)");
            _log.LogWarning("서버 자동 업데이트: {Version} 교체 배치 시작 ({Args})", version, args);
            _ = Task.Delay(1500).ContinueWith(_ => _life.StopApplication());
        }
        catch
        {
            TryDeleteFile(pending); // not run: do not report it at the next start
            TryDeleteFile(Path.Combine(_dir, TicketFile)); // a batch that starts late must not claim it
            throw;
        }
    }

    private string? ReportPreviousRun()
    {
        var pending = Path.Combine(_dir, "pending.txt");
        try
        {
            if (!File.Exists(pending)) return null;
            var p = File.ReadAllText(pending).Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            File.Delete(pending);
            var ver = p.Length > 1 ? p[1].Trim() : "?";
            var msg = p.Length == 0 ? "알 수 없는 결과" : p[0] switch
            {
                "apply" => ver == Current ? $"서버 {ver}(으)로 자동 업데이트됨" : $"{ver} 적용 뒤에도 버전이 {Current} — update\\apply.log 확인",
                "apply-reverted" => $"{ver}이(가) {HealthTimeoutSec}초 안에 응답하지 않아 {Current}(으)로 자동 되돌림(실패한 exe는 {ExeName}.bad) — update\\apply.log 확인",
                "apply-unhealthy" => $"{ver}이(가) 응답하지 않았지만 되돌릴 이전 exe가 없음 — update\\apply.log 확인",
                "apply-failed" => $"{ver} 파일 교체 실패 — {Current} 유지, update\\apply.log 확인",
                _ => $"알 수 없는 업데이트 결과 '{p[0]}' — update\\apply.log 확인",
            };
            if (p.Length > 0 && p[0] is "apply-reverted" or "apply-unhealthy" or "apply-failed") File.WriteAllText(SkipPath, ver);
            else if (p.Length > 0 && p[0] == "apply" && ver == Current) TryDeleteFile(SkipPath);
            var stage = Path.Combine(_dir, "v" + ver);
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            if (p.Length > 0 && p[0] == "apply" && ver == Current) _log.LogInformation("{Msg}", msg); else _log.LogWarning("{Msg}", msg);
            return msg;
        }
        catch (Exception e) { _log.LogWarning("지난 서버 업데이트 결과를 읽지 못함: {Msg}", e.Message); return null; }
    }

    private static async Task<(long Size, string Hash)> CopyAndHash(Stream src, string path, long expected, CancellationToken ct)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long size = 0;
        var buf = new byte[81920];
        await using var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, buf.Length, useAsync: true);
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            sha.AppendData(buf, 0, n);
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            size += n;
            if (size > expected) break;
        }
        return (size, Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant());
    }

    private static void TryDeleteFile(string f) { try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    /// <summary>Batch args: %1 pid, %2 app folder, %3 new files folder, %4 service name, %5 health URL (- = none), %6 expected version.</summary>
    internal static string Arguments(int pid, string appDir, string stageDir, string serviceName, string? healthUrl, string version)
        => $"{pid} \"{appDir.TrimEnd('\\', '/')}\" \"{stageDir.TrimEnd('\\', '/')}\" \"{serviceName}\" \"{(string.IsNullOrEmpty(healthUrl) ? "-" : healthUrl)}\" {version}";

    /// <summary>
    /// START runs cmd.exe (not the batch) so cmd's quote stripping never sees a line that starts with a quote; the new console also lets
    /// "timeout" work when the server runs as a service. /d = ignore AutoRun.
    /// </summary>
    internal static string LaunchArguments(string batPath, string args) => $"/c start \"\" /min cmd.exe /d /c call \"{batPath}\" {args}";

    /// <summary>
    /// True once the batch renamed the ticket to <see cref="StartedFile"/>. On timeout the ticket is withdrawn (atomic rename, only one side wins)
    /// so a batch that starts late quits at its first line; the server is stopped only after this returns true.
    /// </summary>
    internal static async Task<bool> WaitStartedAsync(string dir, TimeSpan timeout)
    {
        string ticket = Path.Combine(dir, TicketFile), started = Path.Combine(dir, StartedFile);
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (File.Exists(started)) return true;
            await Task.Delay(100);
        }
        for (int i = 0; i < 10; i++)
        {
            try { File.Move(ticket, ticket + ".void", true); return false; }
            catch (FileNotFoundException) { return File.Exists(started); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { await Task.Delay(100); }
        }
        return File.Exists(started);
    }

    /// <summary>Whether a service ImagePath ("C:\a b\x.exe" -args, or C:\x.exe) names exePath (case-insensitive).</summary>
    internal static bool SameExe(string? imagePath, string exePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || string.IsNullOrWhiteSpace(exePath)) return false;
        var s = imagePath.Trim();
        if (s.StartsWith(@"\??\", StringComparison.Ordinal)) s = s[4..];
        string exe;
        if (s.StartsWith('"')) { int e = s.IndexOf('"', 1); exe = e < 0 ? s[1..] : s[1..e]; }
        else { int e = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); exe = e < 0 ? s : s[..(e + 4)]; }
        return string.Equals(exe.Replace('/', '\\').Trim(), exePath.Replace('/', '\\').Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Local http URL of /healthz/version from the Kestrel addresses (any-address and localhost become 127.0.0.1); null without an http address.</summary>
    internal static string? HealthUrl(IEnumerable<string> addresses)
    {
        foreach (var a in addresses)
        {
            var s = a.Replace("://*", "://0.0.0.0").Replace("://+", "://0.0.0.0");
            if (!Uri.TryCreate(s, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttp) continue;
            var host = u.Host;
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || (IPAddress.TryParse(host.Trim('[', ']'), out var ip) && (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))))
                host = "127.0.0.1";
            return $"http://{host}:{u.Port}/healthz/version";
        }
        return null;
    }

    /// <summary>ASCII only (cmd mangles UTF-8 without a BOM). Every set is set "X=value" (set X=1 &amp; ... keeps the space before &amp;).</summary>
    internal const string BatchText = """
        @echo off
        rem ASH.Server self-update (written by the server before each run - do not edit)
        rem args: pid appdir stagedir service healthurl version (- = none)
        rem claim the ticket first: the server stops only after it sees run-started.txt (no ticket = withdrawn or manual run, so quit)
        ren "%~dp0run-ticket.txt" run-started.txt >nul 2>&1 || exit /b 1
        setlocal
        set "PID=%~1"
        set "APP=%~2"
        set "STAGE=%~3"
        set "SVC=%~4"
        set "HEALTH=%~5"
        set "WANT=%~6"
        set "LOG=%~dp0apply.log"
        set "PENDING=%~dp0pending.txt"
        set "BODY=%~dp0health.txt"
        set "EXE=%APP%\ASH.Server.exe"
        cd /d "%~dp0"
        >>"%LOG%" echo [%date% %time%] apply %WANT%: "%STAGE%" -^> "%APP%"
        sc stop "%SVC%" >>"%LOG%" 2>&1
        set /a N=0
        :wait_old
        tasklist /FI "PID eq %PID%" /NH 2>nul | findstr /B /I /C:"ASH.Server" >nul || goto swap_files
        set /a N+=1
        if %N% GEQ 60 (>>"%LOG%" echo [%date% %time%] process %PID% still running, killing& taskkill /PID %PID% /F >>"%LOG%" 2>&1& goto swap_files)
        timeout /t 1 /nobreak >nul
        goto wait_old

        :swap_files
        timeout /t 1 /nobreak >nul
        rem the current exe waits as .prev; the existing .old (rollback point) is replaced only after the new exe is in place
        if exist "%EXE%.prev" del /f /q "%EXE%.prev"
        ren "%EXE%" ASH.Server.exe.prev >>"%LOG%" 2>&1
        if exist "%EXE%" (>>"%LOG%" echo [%date% %time%] cannot rename exe, update aborted& >"%PENDING%" echo apply-failed %WANT%& goto start_only)
        rem xcopy /EXCLUDE: cannot take a quoted path (fails with spaces), so use a path relative to this folder
        xcopy "%STAGE%\*" "%APP%\" /E /Y /I /EXCLUDE:exclude.txt >>"%LOG%" 2>&1
        if exist "%EXE%" goto keep_old
        >>"%LOG%" echo [%date% %time%] copy failed, restoring previous exe
        ren "%EXE%.prev" ASH.Server.exe >>"%LOG%" 2>&1
        >"%PENDING%" echo apply-failed %WANT%
        goto start_only

        :keep_old
        if exist "%EXE%.old" del /f /q "%EXE%.old"
        ren "%EXE%.prev" ASH.Server.exe.old >>"%LOG%" 2>&1 || >>"%LOG%" echo [%date% %time%] cannot rename .prev to .old
        sc start "%SVC%" >>"%LOG%" 2>&1
        if "%HEALTH%"=="-" (>>"%LOG%" echo [%date% %time%] no http address, health check skipped& goto done)
        call :waithealth
        if not errorlevel 1 (>>"%LOG%" echo [%date% %time%] healthy: %HEALTH%& goto done)
        >>"%LOG%" echo [%date% %time%] no answer %WANT% from %HEALTH% within 60 s, auto rollback
        call :stopapp
        call :revert
        if errorlevel 1 (>"%PENDING%" echo apply-unhealthy %WANT%) else (>"%PENDING%" echo apply-reverted %WANT%)

        :start_only
        sc start "%SVC%" >>"%LOG%" 2>&1

        :done
        >>"%LOG%" echo [%date% %time%] done
        endlocal
        exit /b 0

        rem ---- stop the new process, kill after 30 s ----
        :stopapp
        sc stop "%SVC%" >>"%LOG%" 2>&1
        set /a N=0
        :stop_wait
        tasklist /FI "IMAGENAME eq ASH.Server.exe" /NH 2>nul | findstr /B /I /C:"ASH.Server" >nul || exit /b 0
        set /a N+=1
        if %N% GEQ 30 (taskkill /IM ASH.Server.exe /F >>"%LOG%" 2>&1& timeout /t 2 /nobreak >nul& exit /b 0)
        timeout /t 1 /nobreak >nul
        goto stop_wait

        rem ---- put the previous exe back (.old, or .prev when it could not become .old); the failed one is kept as .bad ----
        :revert
        set "PREV=%EXE%.old"
        if exist "%EXE%.prev" set "PREV=%EXE%.prev"
        if not exist "%PREV%" (>>"%LOG%" echo [%date% %time%] no previous exe to restore& exit /b 1)
        if exist "%EXE%.bad" del /f /q "%EXE%.bad"
        ren "%EXE%" ASH.Server.exe.bad >>"%LOG%" 2>&1
        ren "%PREV%" ASH.Server.exe >>"%LOG%" 2>&1 && (>>"%LOG%" echo [%date% %time%] restored previous exe& exit /b 0)
        >>"%LOG%" echo [%date% %time%] restore failed, keeping the new exe
        if not exist "%EXE%" ren "%EXE%.bad" ASH.Server.exe >>"%LOG%" 2>&1
        exit /b 1

        rem ---- poll health up to 60 s: errorlevel 0 = answered the expected version ----
        :waithealth
        set /a T=0
        :health_loop
        timeout /t 2 /nobreak >nul
        set /a T+=2
        call :probe_get && exit /b 0
        if %T% GEQ 60 exit /b 1
        goto health_loop

        :probe_get
        if exist "%BODY%" del /f /q "%BODY%"
        where curl.exe >nul 2>&1 || goto probe_ps
        curl.exe -fsS -m 3 --noproxy "*" -o "%BODY%" "%HEALTH%" >nul 2>&1
        goto probe_check
        :probe_ps
        powershell -NoProfile -NonInteractive -Command "try { [System.Net.WebRequest]::DefaultWebProxy = $null; (Invoke-WebRequest -UseBasicParsing -TimeoutSec 3 -Uri '%HEALTH%').Content | Set-Content -Encoding ASCII -LiteralPath '%BODY%' } catch { exit 1 }" >nul 2>&1
        :probe_check
        if not exist "%BODY%" exit /b 1
        rem exact compare of the first line; findstr /X misses a last line without CRLF inside a batch. Delayed expansion: the body is never parsed as commands
        set "GOT="
        set /p GOT=<"%BODY%"
        setlocal EnableDelayedExpansion
        if "!GOT!"=="!WANT!" (endlocal& exit /b 0)
        endlocal
        exit /b 1
        """;
}
