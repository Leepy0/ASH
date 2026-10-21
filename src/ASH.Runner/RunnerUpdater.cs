using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using ASH.Core.Settings;
using ASH.Core.Update;

namespace ASH.Runner;

/// <param name="Handler">Tests only: the in-memory server. Null = a plain handler without proxy (the server is on the LAN).</param>
public sealed record RunnerUpdateOptions(Uri Server, ManifestSignature Key, string CurrentVersion, string ExePath, TimeSpan Timeout, HttpMessageHandler? Handler = null);

public sealed record PreparedUpdate(string Version, string NewExePath);

/// <summary>
/// ash-run updates itself from ASH.Server before it starts a job, so a job never runs half on one version:
/// ask /api/ash/runner, check the signature with the embedded key, download the zip, check size and SHA-256, take ash-run.exe out of it,
/// swap it in (the old file stays as ash-run.exe.old for a manual rollback), prove the new one starts, and run the new one with the same arguments.
/// Updating is quiet and optional: no key, no server address, a server that does not answer or a folder that is not writable just run this version.
/// </summary>
public static class RunnerUpdater
{
    public const string GuardEnv = "ASH_RUNNER_UPDATED"; // set for the relaunched process so it cannot update again (no loops)
    public const string ServerEnv = "ASH_SERVER";
    public const string ExeName = "ash-run.exe";
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SmokeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Version of this build (Directory.Build.props &lt;Version&gt;), without the "+commit" suffix.</summary>
    public static string CurrentVersion { get; } =
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    // ---------- download and verify ----------

    /// <summary>Null when the server has no newer runner. Throws on a bad signature, size or hash, or any network error.</summary>
    public static async Task<PreparedUpdate?> PrepareAsync(RunnerUpdateOptions o, CancellationToken ct)
    {
        using var http = new HttpClient(o.Handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
        var baseUrl = o.Server.AbsoluteUri.TrimEnd('/');

        var json = await GetBytes(http, baseUrl + "/api/ash/runner", o.Timeout, ct);
        if (json == null) return null; // the server has no runner published
        var sig = await GetBytes(http, baseUrl + "/api/ash/runner.sig", o.Timeout, ct);
        if (sig == null || !o.Key.Verify(json, sig)) throw new InvalidDataException("서명 확인 실패");
        var m = PackageManifest.ParseRunner(json);
        if (!m.IsNewerThan(o.CurrentVersion)) return null;

        var dir = Path.GetDirectoryName(o.ExePath)!;
        var work = Path.Combine(dir, "update");
        Directory.CreateDirectory(work);
        var part = Path.Combine(work, m.File + ".part");
        try
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(TimeSpan.FromMinutes(15)); // whole download
                using var r = await http.GetAsync(baseUrl + "/download/" + Uri.EscapeDataString(m.File), HttpCompletionOption.ResponseHeadersRead, cts.Token);
                r.EnsureSuccessStatusCode();
                await using var src = await r.Content.ReadAsStreamAsync(cts.Token);
                long size; string hash;
                await using (var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
                    (size, hash) = await HashedCopy.RunAsync(src, dst, m.Size, cts.Token);
                if (size != m.Size) throw new InvalidDataException($"Runner zip 크기가 다릅니다(받음 {size}, 배포 정보 {m.Size}).");
                if (hash != m.Sha256) throw new InvalidDataException("Runner zip의 SHA-256이 배포 정보와 다릅니다.");
            }
            var newExe = o.ExePath + ".new";
            using (var zip = ZipFile.OpenRead(part))
            {
                var entry = zip.Entries.FirstOrDefault(e => e.FullName == ExeName) ?? throw new InvalidDataException($"Runner zip에 {ExeName}이(가) 없습니다.");
                if (entry.Length is <= 0 or > 1L << 30) throw new InvalidDataException($"Runner zip의 {ExeName} 크기가 이상합니다({entry.Length}).");
                await using var from = entry.Open();
                await using var to = new FileStream(newExe, FileMode.Create, FileAccess.Write, FileShare.None);
                await from.CopyToAsync(to, ct);
            }
            return new PreparedUpdate(m.Version, newExe);
        }
        finally { TryDelete(part); }
    }

    private static async Task<byte[]?> GetBytes(HttpClient http, string url, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        using var r = await http.GetAsync(url, HttpCompletionOption.ResponseContentRead, cts.Token);
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        r.EnsureSuccessStatusCode();
        var b = await r.Content.ReadAsByteArrayAsync(cts.Token);
        if (b.Length > 1 << 16) throw new InvalidDataException("서버 응답이 너무 큽니다.");
        return b;
    }

    // ---------- swap ----------

    /// <summary>exe becomes exe.old and the new file takes its name. A failure puts everything back and rethrows.</summary>
    public static void Apply(string exePath, string newPath)
    {
        var old = exePath + ".old";
        if (File.Exists(old)) { try { File.Delete(old); } catch (IOException) { old = exePath + ".old." + DateTime.UtcNow.Ticks; } catch (UnauthorizedAccessException) { old = exePath + ".old." + DateTime.UtcNow.Ticks; } }
        File.Move(exePath, old); // a running exe can be renamed, not overwritten
        try { File.Move(newPath, exePath); }
        catch { File.Move(old, exePath); throw; }
    }

    /// <summary>Puts ash-run.exe.old back; the exe that failed is kept as ash-run.exe.bad.</summary>
    public static void Revert(string exePath)
    {
        var old = exePath + ".old";
        if (!File.Exists(old)) return;
        var bad = exePath + ".bad";
        TryDelete(bad);
        if (File.Exists(exePath)) File.Move(exePath, bad);
        File.Move(old, exePath);
    }

    // ---------- the entry points ----------

    /// <summary>
    /// Before a job. Returns the exit code of the relaunched (updated) ash-run, or null to go on as this process:
    /// no update wanted or possible, or nothing newer. Never throws.
    /// </summary>
    public static async Task<int?> TryAsync(string[] args, string? serverArg, TextWriter o, CancellationToken ct)
    {
        try
        {
            if (Environment.GetEnvironmentVariable(GuardEnv) != null) return null;
            var ctx = Context(serverArg, o, verbose: false);
            if (ctx == null) return null;
            return await UpdateAsync(ctx, args, relaunch: true, o, verbose: false, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) { o.WriteLine("update skipped: " + e.Message); return null; }
    }

    /// <summary><c>ash-run --update [--server url]</c>: check and apply now, then stop. Exit codes: 0 up to date or updated, 1 could not update.</summary>
    public static async Task<int> UpdateCommandAsync(string[] args, TextWriter o, CancellationToken ct)
    {
        string? server = null;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--server" && i + 1 < args.Length) server = args[++i];
            else { o.WriteLine("usage: ash-run --update [--server <http://host:port>]"); return 1; }
        }
        try
        {
            var ctx = Context(server, o, verbose: true);
            if (ctx == null) return 1;
            var code = await UpdateAsync(ctx, args, relaunch: false, o, verbose: true, ct);
            return code ?? 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return 1; }
        catch (Exception e) { o.WriteLine("update failed: " + e.Message); return 1; }
    }

    private sealed record Ctx(Uri Server, ManifestSignature Key, string ExePath);

    // null (with a line when verbose) when this process cannot or should not update
    private static Ctx? Context(string? serverArg, TextWriter o, bool verbose)
    {
        var exe = Environment.ProcessPath;
        if (exe == null || !string.Equals(Path.GetFileName(exe), ExeName, StringComparison.OrdinalIgnoreCase))
        {
            if (verbose) o.WriteLine($"update: this is not the installed {ExeName} (development run), nothing to update.");
            return null;
        }
        var (key, keyErr) = UpdateFlow.LoadKey(Assembly.GetExecutingAssembly().GetManifestResourceStream("update-key.pub"));
        if (key == null)
        {
            if (verbose || keyErr != null) o.WriteLine(keyErr ?? "update: this build has no signing key, it never updates itself.");
            return null;
        }
        var url = serverArg ?? Environment.GetEnvironmentVariable(ServerEnv);
        if (string.IsNullOrWhiteSpace(url)) { try { url = UserSettings.Load().ServerUrl; } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        if (string.IsNullOrWhiteSpace(url))
        {
            if (verbose) o.WriteLine("update: no server address. Use --server <url>, the ASH_SERVER variable, or the ServerUrl in the ASH settings file.");
            return null;
        }
        if (!UserSettings.TryNormalizeUrl(url, out var norm))
        {
            o.WriteLine("update skipped: bad server address " + url);
            return null;
        }
        return new Ctx(new Uri(norm), key, exe);
    }

    private static async Task<int?> UpdateAsync(Ctx c, string[] args, bool relaunch, TextWriter o, bool verbose, CancellationToken ct)
    {
        CleanLeftovers(c.ExePath);
        // one updater at a time: a second ash-run started together waits, then finds the new exe already in place.
        // A file lock, not a Mutex: this code awaits, and a Mutex must be released by the thread that took it.
        using var gate = await TakeLockAsync(c.ExePath + ".lock", TimeSpan.FromSeconds(120), ct);
        if (gate == null) { o.WriteLine("update skipped: another update is taking too long"); return null; }
        {
            // another process may have swapped the file after this one started
            var onDisk = DiskVersion(c.ExePath);
            if (onDisk != null && SemVersion.TryParse(onDisk, out var disk) && SemVersion.TryParse(CurrentVersion, out var mine) && disk!.CompareTo(mine) > 0)
                return Finish(c, args, onDisk, relaunch, o);

            PreparedUpdate? p;
            try
            {
                p = await PrepareAsync(new RunnerUpdateOptions(c.Server, c.Key, CurrentVersion, c.ExePath, verbose ? TimeSpan.FromSeconds(15) : CheckTimeout), ct);
            }
            catch (Exception e) when (e is HttpRequestException || (e is OperationCanceledException && !ct.IsCancellationRequested))
            {
                o.WriteLine($"update skipped: server {c.Server} does not answer" + (e is HttpRequestException ? " (" + e.Message + ")" : ""));
                return verbose ? 1 : null;
            }
            if (p == null) { if (verbose) o.WriteLine($"ash-run {CurrentVersion} is up to date."); return null; }

            o.WriteLine($"update: ash-run {CurrentVersion} -> {p.Version}");
            Apply(c.ExePath, p.NewExePath);
            var said = SmokeTest(c.ExePath);
            if (said != p.Version)
            {
                Revert(c.ExePath);
                throw new InvalidOperationException($"the new ash-run did not start correctly (said '{said}'), kept {CurrentVersion}");
            }
            return Finish(c, args, p.Version, relaunch, o);
        }
    }

    private static async Task<FileStream?> TakeLockAsync(string path, TimeSpan wait, CancellationToken ct)
    {
        var until = DateTime.UtcNow + wait;
        while (true)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
            catch (IOException) when (DateTime.UtcNow < until) { await Task.Delay(300, ct); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; } // folder not writable: the swap would fail too
        }
    }

    private static int? Finish(Ctx c, string[] args, string version, bool relaunch, TextWriter o)
    {
        if (!relaunch) { o.WriteLine($"ash-run is now {version}. The previous file is kept as {Path.GetFileName(c.ExePath)}.old."); return 0; }
        return Relaunch(c.ExePath, args);
    }

    /// <summary>Runs the exe in this console with the same arguments and returns its exit code.</summary>
    private static int Relaunch(string exe, string[] args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment[GuardEnv] = "1";
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("could not start the updated ash-run");
        p.WaitForExit();
        return p.ExitCode;
    }

    /// <summary>What the new exe prints for --version, or "" when it does not start in time.</summary>
    private static string SmokeTest(string exe)
    {
        var psi = new ProcessStartInfo(exe, "--version") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
        psi.Environment[GuardEnv] = "1";
        try
        {
            using var p = Process.Start(psi);
            if (p == null) return "";
            var read = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(SmokeTimeout)) { try { p.Kill(); } catch (InvalidOperationException) { } return ""; }
            return read.Result.Trim();
        }
        catch (System.ComponentModel.Win32Exception) { return ""; }
    }

    private static string? DiskVersion(string exe)
    {
        try { return FileVersionInfo.GetVersionInfo(exe).ProductVersion?.Split('+')[0]; }
        catch (IOException) { return null; }
    }

    private static void CleanLeftovers(string exe)
    {
        TryDelete(exe + ".new");
        TryDelete(exe + ".bad");
        var dir = Path.GetDirectoryName(exe)!;
        try { foreach (var f in Directory.GetFiles(Path.Combine(dir, "update"), "*.part")) TryDelete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { foreach (var f in Directory.GetFiles(dir, ExeName + ".old.*")) TryDelete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
