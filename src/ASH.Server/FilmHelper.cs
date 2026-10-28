using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ASH.Core.Film;

namespace ASH.Server;

/// <summary>
/// AutoView needs a desktop to draw on, and the Windows service runs in session 0, which has none (diagnostics: "서버 세션 0", no window, no frames).
/// So the service keeps everything else and hands only the AutoView runs to "ASH.Server.exe --film-helper", started by the task scheduler in the
/// logged-on user's session (<see cref="Install"/>). They talk through files in Jobs:Dir\film-helper: the helper writes heartbeat.txt every 2 s,
/// the service writes req-&lt;id&gt;.json (work folder, model, camera script, time limit) and waits for res-&lt;id&gt;.json; cancel-&lt;id&gt; asks the helper
/// to kill that run. The helper resolves AutoView, AutoMod and ffmpeg from appsettings.json itself and only works on folders under Jobs:Dir,
/// so a request file can never name a program to run.
/// </summary>
public static class FilmHelper
{
    public const string TaskName = "ASH.FilmHelper", Arg = "--film-helper", InstallArg = "--install-film-helper", UninstallArg = "--uninstall-film-helper";
    const string BeatFile = "heartbeat.txt";
    /// <summary>A heartbeat older than this means no helper.</summary>
    internal static readonly TimeSpan Fresh = TimeSpan.FromSeconds(15);

    public sealed record Request(string WorkDir, string Model, string Script, double TimeoutSec);

    public static string DirOf(string jobsDir) => Path.Combine(jobsDir, "film-helper");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>The helper's heartbeat is fresh.</summary>
    public static bool Alive(string dir, DateTime nowUtc)
    {
        try
        {
            var t = File.ReadAllText(Path.Combine(dir, BeatFile)).Split('|')[0];
            return long.TryParse(t, out var ticks) && nowUtc - new DateTime(ticks, DateTimeKind.Utc) < Fresh;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    static void WriteAtomic(string path, string text)
    {
        File.WriteAllText(path + ".tmp", text, new UTF8Encoding(false));
        File.Move(path + ".tmp", path, true);
    }

    static void TryDelete(string path) { try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }

    /// <summary>"schtasks /Run" of the helper task (the service runs it as SYSTEM; it starts only when its user is logged on).</summary>
    public static void Wake()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "/Run", "/TN", TaskName }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            p.WaitForExit(10_000);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    public const string NotRunning = "로그온 세션의 AutoView 대리 실행기가 돌고 있지 않습니다. 서버는 서비스(세션 0)라 화면이 없어 AutoView가 그리지 못합니다. " +
        "서버 PC에 영상용 계정이 로그온해 있어야 하고, 처음 한 번 그 계정의 관리자 명령 프롬프트에서 \"ASH.Server.exe " + InstallArg + "\"로 등록합니다(docs\\server-install.md \"영상 촬영\").";

    /// <summary>The service side: one render handed to the helper and waited for. Never throws for a failed run, like <see cref="IFilmRenderer.Render"/>.</summary>
    public sealed class Client(string dir, Action? wake, TimeSpan startWait)
    {
        DateTime _missingUtc = DateTime.MinValue;

        public RenderResult Render(string workDir, string model, string script, TimeSpan timeout, CancellationToken ct)
        {
            static RenderResult Fail(string e, bool timedOut = false) => new(false, null, e, Array.Empty<string>(), timedOut);
            // no helper a moment ago: fail at once instead of waiting again for every cut of the film
            if (!Alive(dir, DateTime.UtcNow))
            {
                if (DateTime.UtcNow - _missingUtc < TimeSpan.FromMinutes(2)) return Fail(NotRunning);
                wake?.Invoke();
                var w = Stopwatch.StartNew();
                while (!Alive(dir, DateTime.UtcNow))
                {
                    if (w.Elapsed > startWait) { _missingUtc = DateTime.UtcNow; return Fail(NotRunning); }
                    if (ct.WaitHandle.WaitOne(500)) return Fail("취소되었습니다.");
                }
            }
            _missingUtc = DateTime.MinValue;
            var id = Guid.NewGuid().ToString("N");
            string req = Path.Combine(dir, $"req-{id}.json"), res = Path.Combine(dir, $"res-{id}.json"), cancel = Path.Combine(dir, $"cancel-{id}");
            try { WriteAtomic(req, JsonSerializer.Serialize(new Request(workDir, model, script, timeout.TotalSeconds), Json)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Fail("대리 실행기에 요청을 쓰지 못했습니다: " + e.Message); }
            var sw = Stopwatch.StartNew();
            TimeSpan? cancelAt = null;
            try
            {
                while (true)
                {
                    if (File.Exists(res))
                    {
                        try { return JsonSerializer.Deserialize<RenderResult>(File.ReadAllText(res), Json) ?? Fail("대리 실행기의 결과가 비어 있습니다."); }
                        catch (Exception e) when (e is IOException or JsonException) { if (sw.Elapsed > timeout + TimeSpan.FromMinutes(2)) return Fail("대리 실행기의 결과를 읽지 못했습니다: " + e.Message); }
                    }
                    if (ct.IsCancellationRequested && cancelAt == null) { cancelAt = sw.Elapsed; try { File.WriteAllText(cancel, ""); } catch (IOException) { } }
                    if (cancelAt is { } c && sw.Elapsed - c > TimeSpan.FromSeconds(20)) return Fail("취소되었습니다.");
                    if (!Alive(dir, DateTime.UtcNow)) return Fail("AutoView 대리 실행기가 응답하지 않습니다(로그오프했거나 꺼졌습니다).", true);
                    // the helper keeps its own limit; this is only for a helper that stopped answering yet still beats
                    if (sw.Elapsed > timeout + TimeSpan.FromMinutes(2)) return Fail($"시간 제한({timeout.TotalMinutes:0.#}분)을 넘어도 대리 실행기가 결과를 주지 않았습니다.", true);
                    Thread.Sleep(300);
                }
            }
            finally { TryDelete(req); TryDelete(res); TryDelete(cancel); }
        }
    }

    /// <summary>The helper's loop: heartbeat, then each request in turn (oldest first) through <paramref name="render"/>. Returns when <paramref name="stop"/>
    /// is cancelled or, between requests, when <paramref name="exeChanged"/> says a server update replaced the program (the service wakes the new one).</summary>
    public static void RunHost(string jobsDir, Func<Request, CancellationToken, RenderResult> render, CancellationToken stop, Func<bool>? exeChanged = null, Action<string>? log = null)
    {
        var dir = DirOf(jobsDir);
        Directory.CreateDirectory(dir);
        var root = Path.GetFullPath(jobsDir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        int session; try { session = Process.GetCurrentProcess().SessionId; } catch (InvalidOperationException) { session = -1; }
        string? current = null;
        CancellationTokenSource? runCts = null;
        var gate = new object();
        void Beat()
        {
            try { WriteAtomic(Path.Combine(dir, BeatFile), $"{DateTime.UtcNow.Ticks}|{Environment.ProcessId}|{session}"); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            lock (gate) if (current != null && File.Exists(Path.Combine(dir, $"cancel-{current}"))) runCts?.Cancel();
        }
        using var timer = new Timer(_ => Beat(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        log?.Invoke($"시작: 세션 {session}, 폴더 {dir}");
        while (!stop.IsCancellationRequested)
        {
            var next = Directory.GetFiles(dir, "req-*.json").Select(f => new FileInfo(f)).OrderBy(f => f.CreationTimeUtc).FirstOrDefault();
            if (next == null)
            {
                if (exeChanged?.Invoke() == true) { log?.Invoke("프로그램이 업데이트되어 끝냅니다."); break; }
                stop.WaitHandle.WaitOne(500);
                continue;
            }
            var id = next.Name["req-".Length..^".json".Length];
            RenderResult result;
            try
            {
                var r = JsonSerializer.Deserialize<Request>(File.ReadAllText(next.FullName), Json) ?? throw new JsonException("빈 요청");
                var work = Path.GetFullPath(r.WorkDir);
                if (!work.StartsWith(root, StringComparison.OrdinalIgnoreCase)) result = new(false, null, "작업 폴더가 Jobs:Dir 밖이라 거절했습니다: " + work, Array.Empty<string>());
                else if (File.Exists(Path.Combine(dir, $"cancel-{id}"))) result = new(false, null, "취소되었습니다.", Array.Empty<string>()); // cancelled while it waited: not started at all
                else
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(stop);
                    lock (gate) { current = id; runCts = cts; }
                    log?.Invoke($"렌더 {id}: {work}");
                    try { result = render(r with { WorkDir = work }, cts.Token); }
                    finally { lock (gate) { current = null; runCts = null; } }
                    log?.Invoke($"렌더 {id}: {(result.Ok ? "완료" : "실패 " + result.Error)}");
                }
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
            {
                result = new(false, null, "대리 실행기가 요청을 처리하지 못했습니다: " + e.Message, Array.Empty<string>());
            }
            TryDelete(next.FullName); // the service waits for the result file, not for the request to go
            try { WriteAtomic(Path.Combine(dir, $"res-{id}.json"), JsonSerializer.Serialize(result, Json)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { log?.Invoke("결과를 쓰지 못함: " + e.Message); }
            TryDelete(Path.Combine(dir, $"cancel-{id}"));
        }
        TryDelete(Path.Combine(dir, BeatFile));
    }

    [DllImport("kernel32.dll")] static extern bool FreeConsole();

    /// <summary>"ASH.Server.exe --film-helper": one per user session, no console window, log in film-helper\helper.log.</summary>
    public static int RunHelper()
    {
        using var single = new Mutex(true, @"Local\" + TaskName, out var first);
        if (!first) return 0;
        FreeConsole(); // started by the task scheduler at logon: a console window would only be closed by hand
        var cfg = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: true, reloadOnChange: true).AddEnvironmentVariables().Build();
        var jobsDir = JobStore.DirFrom(cfg);
        var dir = DirOf(jobsDir);
        Directory.CreateDirectory(dir);
        var logPath = Path.Combine(dir, "helper.log");
        void Log(string s)
        {
            try
            {
                if (File.Exists(logPath) && new FileInfo(logPath).Length > 1_000_000) File.Move(logPath, logPath + ".old", true);
                File.AppendAllText(logPath, $"[{StampedWriter.Stamp()}] {s}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        var exe = Environment.ProcessPath ?? "";
        var stamp = File.Exists(exe) ? File.GetLastWriteTimeUtc(exe) : DateTime.MinValue;
        var renderer = new LiveFilmRenderer(cfg);
        RunHost(jobsDir, (r, ct) => renderer.RenderHere(r.WorkDir, r.Model, r.Script, TimeSpan.FromSeconds(r.TimeoutSec), ct), CancellationToken.None,
            () => !File.Exists(exe) || File.GetLastWriteTimeUtc(exe) != stamp, Log);
        return 0;
    }

    /// <summary>--install-film-helper [user]: Modify on Jobs:Dir for the user (AutoView writes into the film work folders the service made), the logon task
    /// (only while that user is logged on, normal rights), and starts it now. --uninstall-film-helper removes the task. Needs an elevated prompt.</summary>
    public static int Install(bool install, string? user)
    {
        var exe = Environment.ProcessPath ?? "";
        if (install && !exe.EndsWith("ASH.Server.exe", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("ASH.Server.exe 로 실행했을 때만 등록할 수 있습니다(설치 폴더의 exe).");
            return 1;
        }
        if (!install) return Tool("schtasks.exe", "/Delete", "/TN", TaskName, "/F");
        user = string.IsNullOrWhiteSpace(user) ? WindowsIdentity.GetCurrent().Name : user.Trim();
        var cfg = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: true).AddEnvironmentVariables().Build();
        var jobsDir = JobStore.DirFrom(cfg);
        Directory.CreateDirectory(DirOf(jobsDir));
        Console.WriteLine($"영상용 계정: {user}\n작업 폴더: {jobsDir}");
        var code = Tool("icacls.exe", jobsDir, "/grant", $"{user}:(OI)(CI)M", "/T", "/C", "/Q");
        if (code != 0) { Console.Error.WriteLine("작업 폴더 권한을 주지 못했습니다. 관리자 권한 명령 프롬프트에서 다시 실행하세요."); return code; }
        // the install folder is locked to Administrators and SYSTEM (docs\server-install.md): the helper runs with normal rights and must start the exe.
        // That account can then read appsettings.json (the admin password too): use a dedicated account.
        code = Tool("icacls.exe", Path.GetDirectoryName(exe)!, "/grant", $"{user}:(OI)(CI)RX", "/C", "/Q");
        if (code != 0) { Console.Error.WriteLine("설치 폴더 읽기 권한을 주지 못했습니다. 관리자 권한 명령 프롬프트에서 다시 실행하세요."); return code; }
        code = Tool("schtasks.exe", "/Create", "/TN", TaskName, "/TR", $"\"{exe}\" {Arg}", "/SC", "ONLOGON", "/RU", user, "/IT", "/RL", "LIMITED", "/F");
        if (code != 0) { Console.Error.WriteLine("작업 스케줄러에 등록하지 못했습니다. 관리자 권한 명령 프롬프트에서 다시 실행하세요."); return code; }
        Tool("schtasks.exe", "/Run", "/TN", TaskName);
        Console.WriteLine($"등록 완료. {user} 계정이 로그온해 있는 동안 AutoView는 그 화면에서 돕니다. 로그온을 유지하고 화면 잠금·원격 데스크톱 연결 끊기는 피하세요.");
        return 0;
    }

    static int Tool(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode;
    }
}
