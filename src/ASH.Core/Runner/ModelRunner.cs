using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ASH.Core.AutoMod;
using ASH.Core.Input;
using ASH.Core.Model;

namespace ASH.Core.Runner;

/// <param name="ExeOverride">Test hook: run this executable (with ExeArgs) instead of &lt;Project&gt;\&lt;Model&gt;.exe -w.</param>
public sealed record RunRequest(string ProjectDir, string ModelName, ProjectInput Input, RunScenario Scenario, TimeSpan Timeout,
    string? DialogButton, string? ExeOverride = null, string ExeArgs = "-w");

public sealed record RunOutcome(string RunId, bool Success, string RunDir, TimeSpan Duration, string? Error);

public static class ModelRunner
{
    /// <summary>g-less id: group active counts (by group name) joined by '-', then the seed, e.g. n1-2_s3.</summary>
    public static string RunId(RunScenario s) =>
        "n" + string.Join("-", s.ActiveByGroup.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value)) + "_s" + s.Seed;

    public static RunOutcome Run(RunRequest r)
    {
        var id = RunId(r.Scenario);
        var runDir = Path.Combine(r.ProjectDir, "runs", id);
        var sw = Stopwatch.StartNew();
        RunOutcome Done(bool ok, string? err) => new RunOutcome(id, ok, runDir, sw.Elapsed, err);

        var exe = r.ExeOverride ?? Path.Combine(r.ProjectDir, r.ModelName + ".exe");
        if (!File.Exists(exe) && r.ExeOverride == null) return Done(false, "exe not found: " + exe);
        Directory.CreateDirectory(runDir);
        // AM reads arc/params.txt; a stale result.tsv must not pass for this run's
        ParamsFile.Write(r.Input, r.Scenario, Path.Combine(r.ProjectDir, r.ModelName + ".arc", "params.txt"));
        var tsv = Path.Combine(r.ProjectDir, "result.tsv");
        if (File.Exists(tsv)) File.Delete(tsv);

        int? exit = null; string? error = null;
        try
        {
            using var proc = Process.Start(new ProcessStartInfo(exe, r.ExeArgs) { WorkingDirectory = r.ProjectDir, UseShellExecute = false })!;
            using var closer = r.DialogButton == null ? null
                : DialogCloser.Start(proc.Id, new[] { "Authorization" }, _ => { }, r.DialogButton);
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

        foreach (var name in new[] { "result.tsv", r.ModelName + ".report", r.ModelName + ".message", r.ModelName + ".print" })
        {
            var src = Path.Combine(r.ProjectDir, name);
            if (!File.Exists(src)) continue;
            var dst = Path.Combine(runDir, name);
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(src, dst);
        }
        if (error == null && exit != 0) error = "exit code " + exit;
        if (error == null && !File.Exists(Path.Combine(runDir, "result.tsv"))) error = "result.tsv missing";
        return Done(error == null, error);
    }
}
