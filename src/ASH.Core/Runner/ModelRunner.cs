using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using ASH.Core.AutoMod;
using ASH.Core.Input;
using ASH.Core.Model;

namespace ASH.Core.Runner;

/// <param name="WorkDir">Execution cwd holding &lt;Model&gt;.exe/.arc/.dir (default ProjectDir; see PrepareWorkSlot for parallel runs).</param>
/// <param name="AmHome">When set, &lt;AmHome&gt;\bin is put first on the child process PATH (as AmBuilder does).</param>
/// <param name="RunsDir">Where runs\&lt;RunId&gt; land (default &lt;ProjectDir&gt;\runs).</param>
/// <param name="DialogButton">Button to click on an AutoMod "Authorization" dialog ("Student Version" on a trial install); null = never click,
/// so a licensed server never falls into the student mode.</param>
public sealed record RunRequest(string ProjectDir, string ModelName, ProjectInput Input, RunScenario Scenario, TimeSpan Timeout,
    string? DialogButton = null, string? WorkDir = null, string? RunsDir = null, string? AmHome = null)
{
    /// <summary>Test hook: run this executable (with ExeArgs) instead of &lt;WorkDir&gt;\&lt;Model&gt;.exe -w.</summary>
    internal string? ExeOverride { get; init; }
    internal string ExeArgs { get; init; } = "-w";
}

public sealed record RunOutcome(string RunId, bool Success, string RunDir, TimeSpan Duration, string? Error,
    int? ExitCode = null, IReadOnlyList<string>? ClickedDialogTitles = null, string? ResultTsvPath = null, string? ReportPath = null);

public static class ModelRunner
{
    /// <summary>n&lt;active count per group in Input.Groups row order, joined by '-'&gt;_s&lt;seed&gt;[_dbg][_ev]; a group missing from the scenario counts as MinCount.</summary>
    public static string RunId(ProjectInput input, RunScenario s) =>
        "n" + string.Join("-", input.Groups.Select(g => Count(g, s))) + "_s" + s.Seed + (s.Debug ? "_dbg" : "") + (s.Events ? "_ev" : "");

    /// <summary>e.g. "G1=1;G2=2" (all groups, row order) for the summary's scenario column.</summary>
    public static string ScenarioText(ProjectInput input, RunScenario s) =>
        string.Join(";", input.Groups.Select(g => g.Group + "=" + Count(g, s)));

    internal static int Count(AgvGroup g, RunScenario s) => s.ActiveByGroup.TryGetValue(g.Group, out var n) ? n : g.MinCount;

    /// <summary>Creates &lt;projectDir&gt;\work\w&lt;slot&gt; and copies &lt;Model&gt;.exe, &lt;Model&gt;.arc\* and &lt;Model&gt;.dir\* when missing or older than the source.
    /// Call before the slot's runs, never while one is running there. <paramref name="folder"/> names the folder under work\ instead of w&lt;slot&gt;.</summary>
    public static string PrepareWorkSlot(string projectDir, string modelName, int slot, string? folder = null)
    {
        var dir = Path.Combine(projectDir, "work", folder ?? "w" + slot);
        Directory.CreateDirectory(dir);
        Sync(Path.Combine(projectDir, modelName + ".exe"), Path.Combine(dir, modelName + ".exe"));
        foreach (var sub in new[] { modelName + ".arc", modelName + ".dir" })
        {
            var src = Path.Combine(projectDir, sub);
            if (!Directory.Exists(src)) continue;
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                var rel = f.Substring(src.Length).TrimStart('\\', '/');
                var dst = Path.Combine(dir, sub, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                Sync(f, dst);
            }
        }
        return dir;
    }

    static void Sync(string src, string dst)
    {
        if (!File.Exists(src)) return;
        if (!File.Exists(dst) || File.GetLastWriteTimeUtc(src) > File.GetLastWriteTimeUtc(dst)) File.Copy(src, dst, true);
    }

    internal static string MutexName(string workDir)
    {
        using var sha = SHA1.Create();
        var h = sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(workDir).TrimEnd('\\', '/').ToLowerInvariant()));
        return "Global\\ASH-run-" + BitConverter.ToString(h).Replace("-", "");
    }

    /// <summary>How long a run waits for its work dir (a slot in use by another process, e.g. a second ash-run on the same project).</summary>
    internal static TimeSpan MutexWait = TimeSpan.FromSeconds(30);

    /// <summary>Never throws: every failure comes back as Success=false with Error.</summary>
    public static RunOutcome Run(RunRequest r)
    {
        var sw = Stopwatch.StartNew();
        string id = "?", runDir = r.RunsDir ?? r.ProjectDir;
        try
        {
            id = RunId(r.Input, r.Scenario);
            runDir = Path.Combine(r.RunsDir ?? Path.Combine(r.ProjectDir, "runs"), id);
            var workDir = r.WorkDir ?? r.ProjectDir;
            using var mtx = new Mutex(false, MutexName(workDir));
            bool got;
            try { got = mtx.WaitOne(MutexWait); } catch (AbandonedMutexException) { got = true; } // previous holder died
            if (!got) return new RunOutcome(id, false, runDir, sw.Elapsed, $"work dir busy (another run held it for {MutexWait.TotalSeconds:0} s): {workDir}");
            try { return RunLocked(r, id, runDir, workDir, sw); }
            finally { mtx.ReleaseMutex(); }
        }
        catch (Exception e) { return new RunOutcome(id, false, runDir, sw.Elapsed, e.GetType().Name + ": " + e.Message); }
    }

    static RunOutcome RunLocked(RunRequest r, string id, string runDir, string workDir, Stopwatch sw)
    {
        var exe = r.ExeOverride ?? Path.Combine(workDir, r.ModelName + ".exe");
        if (!File.Exists(exe) && r.ExeOverride == null) return new RunOutcome(id, false, runDir, sw.Elapsed, "exe not found: " + exe);
        if (Directory.Exists(runDir)) Directory.Delete(runDir, true); // a rerun must not inherit old files
        Directory.CreateDirectory(runDir);
        var names = new[] { "result.tsv", "events.tsv", r.ModelName + ".report", r.ModelName + ".message", r.ModelName + ".print" };
        foreach (var n in names) File.Delete(Path.Combine(workDir, n)); // stale output must not pass for this run's
        ParamsFile.Write(r.Input, r.Scenario, Path.Combine(workDir, r.ModelName + ".arc", "params.txt"));

        var clicked = new List<string>();
        int? exit = null; string? error = null;
        try
        {
            var psi = new ProcessStartInfo(exe, r.ExeArgs) { WorkingDirectory = workDir, UseShellExecute = false };
            if (r.AmHome != null) psi.EnvironmentVariables["PATH"] = Path.Combine(r.AmHome, "bin") + ";" + Environment.GetEnvironmentVariable("PATH");
            using var proc = Process.Start(psi)!;
            using var closer = r.DialogButton == null ? null
                : DialogCloser.Start(proc.Id, new[] { "Authorization" }, t => { lock (clicked) clicked.Add(t); }, r.DialogButton);
            if (proc.WaitForExit((int)r.Timeout.TotalMilliseconds)) exit = proc.ExitCode;
            else
            {
                error = "timeout";
                try { Process.Start(new ProcessStartInfo("taskkill", "/PID " + proc.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true })?.WaitForExit(5000); }
                catch (System.ComponentModel.Win32Exception) { } // whole tree: children keep the cwd locked
                try { proc.Kill(); }
                catch (InvalidOperationException) { } // already exited
                catch (System.ComponentModel.Win32Exception) { } // exiting or access denied
                proc.WaitForExit(5000);
            }
        }
        catch (System.ComponentModel.Win32Exception e) { error = "start failed: " + e.Message; }

        var errs = new List<string>();
        foreach (var name in names)
        {
            var src = Path.Combine(workDir, name);
            try { if (File.Exists(src)) File.Move(src, Path.Combine(runDir, name)); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { errs.Add("move " + name + ": " + e.Message); }
        }
        var tsv = Path.Combine(runDir, "result.tsv"); var rep = Path.Combine(runDir, r.ModelName + ".report");
        if (error == null && exit != 0) error = "exit code " + exit;
        if (error == null && !File.Exists(tsv)) error = "result.tsv missing";
        if (error == null && !File.Exists(rep)) error = r.ModelName + ".report missing";
        if (error == null && !ResultParser.Validate(tsv, out var verr)) error = verr;
        if (errs.Count > 0) error = string.Join("; ", new[] { error }.Where(e => e != null).Concat(errs)!);
        return new RunOutcome(id, error == null, runDir, sw.Elapsed, error, exit, clicked.ToArray(),
            File.Exists(tsv) ? tsv : null, File.Exists(rep) ? rep : null);
    }
}
