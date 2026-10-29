using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ASH.Core.Input;
using ASH.Core.Model;

namespace ASH.Core.Runner;

/// <param name="Slots">Work slots &lt;ProjectDir&gt;\work\w1..wN (one run in flight per slot); a cycle group's count model gets w&lt;k&gt;-&lt;model&gt; beside them.</param>
/// <param name="Timeout">Per run; default 30 min.</param>
/// <param name="RunsDir">Where runs\&lt;RunId&gt; land; default &lt;ProjectDir&gt;\runs.</param>
/// <param name="SummaryPath">Default &lt;RunsDir&gt;\summary.tsv.</param>
/// <param name="Progress">Reported once per finished run (success or not), from the run's thread.</param>
/// <param name="Reuse">Resume: a run whose seed and scenario already have rows in summary.tsv (and whose result.tsv is still there) is taken from that
/// run folder instead of running the model again; its rows are not written twice. Only for a job continued on the same model (a changed model must not reuse).</param>
public sealed record ExecutorOptions(string ProjectDir, string Model, int Slots = 2, TimeSpan? Timeout = null, string? DialogButton = null,
    string? AmHome = null, bool Debug = false, string? RunsDir = null, string? SummaryPath = null, IProgress<RunOutcome>? Progress = null, bool Reuse = false)
{
    public string RunsDirOrDefault => RunsDir ?? Path.Combine(ProjectDir, "runs");
    public string SummaryPathOrDefault => SummaryPath ?? Path.Combine(RunsDirOrDefault, "summary.tsv");
}

/// <summary>One model run in a work slot: run, parse, KPI, append summary rows. The only summary.tsv writer.</summary>
public sealed class ModelExecutor
{
    readonly ExecutorOptions a; readonly ProjectInput input;
    readonly Func<RunRequest, RunOutcome> runner;
    readonly Func<RunScenario, string> modelFor;
    /// <summary>(slot, model) -> its work folder, prepared when first used.</summary>
    readonly Dictionary<(int Slot, string Model), string> slots = new();
    readonly HashSet<string> failedIds = new();
    /// <summary>(seed, scenario) -> earlier runs to take again, in the order they were made (Reuse only).</summary>
    readonly Dictionary<(int Seed, string Scenario), Queue<(string RunId, double Sec)>> earlier = new();
    public int Failures;
    /// <summary>Runs taken from earlier results instead of running the model (Reuse).</summary>
    public int Reused;
    public List<string> FailureMessages { get; } = new();

    public ModelExecutor(ExecutorOptions a, ProjectInput input, Func<RunRequest, RunOutcome>? runner = null, Func<RunScenario, string>? modelFor = null)
    {
        this.a = a; this.input = input; this.runner = runner ?? ModelRunner.Run;
        // the default: a cycle group's count runs in its own model (ModelVariants), made and built when first needed; the run's time limit bounds the build
        this.modelFor = modelFor ?? (s =>
        {
            var m = ModelVariants.NameFor(input, a.Model, s);
            if (m != a.Model) ModelVariants.Ensure(a.ProjectDir, a.Model, m, input, s, a.AmHome, a.Timeout ?? TimeSpan.FromMinutes(30));
            return m;
        });
        if (a.Reuse && File.Exists(a.SummaryPathOrDefault))
            foreach (var g in SummaryTsv.ReadAll(a.SummaryPathOrDefault).GroupBy(r => r.RunId))
            {
                var r = g.First();
                var key = (r.Seed, r.Scenario);
                if (!earlier.TryGetValue(key, out var q)) earlier[key] = q = new Queue<(string, double)>();
                q.Enqueue((r.RunId, r.DurationSec));
            }
    }

    /// <summary>An earlier run of this scenario with a valid result.tsv (and the report of <paramref name="model"/>, the one that ran it), as the outcome the
    /// model run would have given; null = run the model.</summary>
    RunOutcome? Earlier(int seed, string scenario, string model)
    {
        lock (earlier)
            while (earlier.TryGetValue((seed, scenario), out var q) && q.Count > 0)
            {
                var (id, sec) = q.Dequeue();
                var dir = Path.Combine(a.RunsDirOrDefault, id);
                string tsv = Path.Combine(dir, "result.tsv"), rep = Path.Combine(dir, model + ".report");
                if (File.Exists(tsv) && File.Exists(rep) && ResultParser.Validate(tsv, out _)) return new RunOutcome(id, true, dir, TimeSpan.FromSeconds(sec), null, 0, null, tsv, rep);
            }
        return null;
    }

    /// <summary>The slot's folder for this model, prepared when first used: work\w&lt;slot&gt; for the base model as before, work\w&lt;slot&gt;-&lt;model&gt; for a count model.
    /// A slot has one run at a time, so none is in flight there while it is prepared.</summary>
    string WorkDir(int slot, string model)
    {
        lock (slots)
        {
            if (!slots.TryGetValue((slot, model), out var dir))
                slots[(slot, model)] = dir = ModelRunner.PrepareWorkSlot(a.ProjectDir, model, slot, model == a.Model ? null : "w" + slot + "-" + model);
            return dir;
        }
    }

    /// <summary>True when the run with this RunId failed (run error or unparsable result).</summary>
    public bool Failed(string runId) { lock (FailureMessages) return failedIds.Contains(runId); }

    public IReadOnlyList<RunKpi> Run(int slot, RunScenario s)
    {
        if (a.Debug) s = s with { Debug = true };
        var scen = ModelRunner.ScenarioText(input, s);
        string? model = null, work = null, why = null;
        try { model = modelFor(s); work = WorkDir(slot, model); }
        catch (Exception e) { why = e.Message; } // the count's model could not be made: this run fails and the search goes on (a run must not throw)
        var reused = why == null && a.Reuse ? Earlier(s.Seed, scen, model!) : null;
        if (reused != null)
            try { ResultParser.Parse(reused.ResultTsvPath!); }
            catch (Exception) { reused = null; } // unreadable after all: run it again
        var id = ModelRunner.RunId(input, s);
        var o = why != null ? new RunOutcome(id, false, Path.Combine(a.RunsDirOrDefault, id), TimeSpan.Zero, why)
            : reused ?? runner(new RunRequest(a.ProjectDir, model!, input, s, a.Timeout ?? TimeSpan.FromMinutes(30), a.DialogButton,
            WorkDir: work, RunsDir: a.RunsDirOrDefault, AmHome: a.AmHome));
        if (reused != null) Interlocked.Increment(ref Reused);
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
        foreach (var k in kpis.Where(_ => reused == null)) // a reused run's rows are in summary.tsv already
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
