using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ASH.Core.Input;
using ASH.Core.Model;

namespace ASH.Core.Runner;

/// <param name="Slots">Work slots &lt;ProjectDir&gt;\work\w1..wN (one run in flight per slot).</param>
/// <param name="Timeout">Per run; default 30 min.</param>
/// <param name="RunsDir">Where runs\&lt;RunId&gt; land; default &lt;ProjectDir&gt;\runs.</param>
/// <param name="SummaryPath">Default &lt;RunsDir&gt;\summary.tsv.</param>
/// <param name="Progress">Reported once per finished run (success or not), from the run's thread.</param>
public sealed record ExecutorOptions(string ProjectDir, string Model, int Slots = 2, TimeSpan? Timeout = null, string? DialogButton = null,
    string? AmHome = null, bool Debug = false, string? RunsDir = null, string? SummaryPath = null, IProgress<RunOutcome>? Progress = null)
{
    public string RunsDirOrDefault => RunsDir ?? Path.Combine(ProjectDir, "runs");
    public string SummaryPathOrDefault => SummaryPath ?? Path.Combine(RunsDirOrDefault, "summary.tsv");
}

/// <summary>One model run in a work slot: run, parse, KPI, append summary rows. The only summary.tsv writer.</summary>
public sealed class ModelExecutor
{
    readonly ExecutorOptions a; readonly ProjectInput input; readonly string?[] workDirs;
    readonly Func<RunRequest, RunOutcome> runner;
    readonly HashSet<string> failedIds = new();
    public int Failures;
    public List<string> FailureMessages { get; } = new();

    public ModelExecutor(ExecutorOptions a, ProjectInput input, Func<RunRequest, RunOutcome>? runner = null)
    {
        this.a = a; this.input = input; this.runner = runner ?? ModelRunner.Run;
        int slots = Math.Max(1, a.Slots);
        workDirs = new string?[slots + 1];
        for (int k = 1; k <= slots; k++) workDirs[k] = ModelRunner.PrepareWorkSlot(a.ProjectDir, a.Model, k); // once per slot, before any run
    }

    /// <summary>True when the run with this RunId failed (run error or unparsable result).</summary>
    public bool Failed(string runId) { lock (FailureMessages) return failedIds.Contains(runId); }

    public IReadOnlyList<RunKpi> Run(int slot, RunScenario s)
    {
        if (a.Debug) s = s with { Debug = true };
        var o = runner(new RunRequest(a.ProjectDir, a.Model, input, s, a.Timeout ?? TimeSpan.FromMinutes(30), a.DialogButton,
            WorkDir: workDirs[slot], RunsDir: a.RunsDirOrDefault, AmHome: a.AmHome));
        IReadOnlyList<RunKpi> kpis;
        string? error = o.Error;
        bool failed = !o.Success;
        string? reportNote = null;
        try
        {
            if (!o.Success) throw new InvalidOperationException(o.Error);
            ReportStats? rep = null;
            try { rep = o.ReportPath == null ? null : ResultParser.ParseReport(o.ReportPath); }
            catch (Exception re) { reportNote = "report unreadable (" + re.Message + "): utilization not computed"; }
            kpis = Kpi.FromRun(ResultParser.Parse(o.ResultTsvPath!), rep, input);
            if (reportNote != null) kpis = kpis.Select(k => k with { Note = string.Join("; ", new[] { k.Note, reportNote }.Where(n => !string.IsNullOrEmpty(n))) }).ToList();
        }
        catch (Exception e)
        {
            failed = true; error = o.Success ? e.Message : error ?? e.Message; // a parse failure after an OK exit still counts as a failed run
            kpis = input.Groups.Select(g => new RunKpi(g.Group, s.ActiveByGroup.TryGetValue(g.Group, out var n) ? n : g.MinCount,
                double.NaN, double.NaN, 0, 0, 0, 0, 0, "run failed: " + e.Message)).ToList();
        }
        if (failed) lock (FailureMessages) { Failures++; FailureMessages.Add($"{o.RunId}: {error}"); failedIds.Add(o.RunId); }
        var scen = ModelRunner.ScenarioText(input, s);
        foreach (var k in kpis)
        {
            // shortcut: this row judges one replication of one group; the search verdict averages replications and so can differ
            var v = !failed ? Judge.Evaluate(new[] { k }, input.Settings) : new Verdict(false, error ?? "run failed");
            SummaryTsv.Append(a.SummaryPathOrDefault, new SummaryRow(o.RunId, k.Group, k.Active, s.Seed, v.Ok, v.Reason,
                k.Utilization, k.MinStockRatio, k.Supplies, k.Expected, k.StopTimeSec, k.Deadlocks, o.Duration.TotalSeconds, scen));
        }
        a.Progress?.Report(failed && o.Success ? o with { Success = false, Error = error } : o);
        return kpis;
    }
}
