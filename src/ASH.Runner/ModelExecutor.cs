using ASH.Core.Input;
using ASH.Core.Model;
using ASH.Core.Runner;

namespace ASH.Runner;

/// <summary>One model run in a work slot: run, parse, KPI, append summary rows. The only summary.tsv writer.</summary>
public sealed class ModelExecutor
{
    readonly RunnerArgs a; readonly ProjectInput input; readonly string summaryPath; readonly string?[] workDirs;
    readonly Func<RunRequest, RunOutcome> runner;
    public int Failures;
    public List<string> FailureMessages { get; } = new();

    public ModelExecutor(RunnerArgs a, ProjectInput input, Func<RunRequest, RunOutcome>? runner = null)
    {
        this.a = a; this.input = input; this.runner = runner ?? ModelRunner.Run;
        summaryPath = Path.Combine(a.ProjectDir, "runs", "summary.tsv");
        workDirs = new string?[a.Parallel + 1];
        for (int k = 1; k <= a.Parallel; k++) workDirs[k] = ModelRunner.PrepareWorkSlot(a.ProjectDir, a.Model, k); // once per slot, before any run
    }

    public IReadOnlyList<RunKpi> Run(int slot, RunScenario s)
    {
        if (a.Debug) s = s with { Debug = true };
        var o = runner(new RunRequest(a.ProjectDir, a.Model, input, s, TimeSpan.FromMinutes(a.TimeoutMin), a.DialogButton, WorkDir: workDirs[slot], AmHome: a.AmHome));
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
        if (failed) lock (FailureMessages) { Failures++; FailureMessages.Add($"{o.RunId}: {error}"); }
        var scen = ModelRunner.ScenarioText(input, s);
        foreach (var k in kpis)
        {
            // shortcut: this row judges one replication of one group; the search verdict averages replications and so can differ
            var v = !failed ? Judge.Evaluate(new[] { k }, input.Settings) : new Verdict(false, error ?? "run failed");
            SummaryTsv.Append(summaryPath, new SummaryRow(o.RunId, k.Group, k.Active, s.Seed, v.Ok, v.Reason,
                k.Utilization, k.MinStockRatio, k.Supplies, k.Expected, k.StopTimeSec, k.Deadlocks, o.Duration.TotalSeconds, scen));
        }
        return kpis;
    }
}
