using System.Diagnostics;
using System.Globalization;
using ASH.Core.Input;
using ASH.Core.Model;
using ASH.Core.Runner;

namespace ASH.Runner;

public static class Program
{
    public static int Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); }; // Ctrl+C: finish runs in flight, start no new ones
        return RunAsync(args, ct: cts.Token).GetAwaiter().GetResult();
    }

    /// <summary>Exit codes: 0 ok, 1 error, 2 ok with warnings. <paramref name="input"/> replaces reading input.xlsx (tests).</summary>
    public static async Task<int> RunAsync(string[] args, ProjectInput? input = null, TextWriter? output = null, CancellationToken ct = default)
    {
        var o = output ?? Console.Out;
        var a = RunnerArgs.Parse(args, out var err);
        if (a == null) { o.WriteLine("error: " + err); o.WriteLine(RunnerArgs.Usage); return 1; }
        try { return await Task.Run(() => Execute(a, input, o, ct)); }
        catch (Exception ex)
        {
            var e = ex is AggregateException ag && ag.InnerException != null ? ag.InnerException : ex;
            if (e is OperationCanceledException) { o.WriteLine("cancelled"); return 1; }
            o.WriteLine("error: " + e.Message); if (a.Debug) o.WriteLine(e); return 1;
        }
    }

    /// <summary>Moves an existing runs\ to runs.&lt;yyyyMMdd_HHmmss&gt;\ (suffix _2, _3... on a clash) so each job starts a fresh summary.</summary>
    internal static string? RotateRuns(string runsDir)
    {
        if (!Directory.Exists(runsDir)) return null;
        var stamp = runsDir.TrimEnd('\\', '/') + "." + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var dst = stamp;
        for (int k = 2; Directory.Exists(dst); k++) dst = stamp + "_" + k;
        Directory.Move(runsDir, dst);
        return dst;
    }

    static int Execute(RunnerArgs a, ProjectInput? input, TextWriter o, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var warnings = new List<string>();
        if (!Directory.Exists(a.ProjectDir)) { o.WriteLine("error: project dir not found: " + a.ProjectDir); return 1; }
        var exe = Path.Combine(a.ProjectDir, a.Model + ".exe");
        if (!File.Exists(exe)) { o.WriteLine($"error: {exe} not found. Build the model in the ZWCAD plugin first (ash-run only runs an existing model)."); return 1; }
        if (input == null)
        {
            var xlsx = Path.Combine(a.ProjectDir, "input.xlsx");
            if (!File.Exists(xlsx)) { o.WriteLine("error: input.xlsx not found: " + xlsx); return 1; }
            input = ExcelReader.Read(xlsx, out var findings);
            foreach (var f in findings) { o.WriteLine("input: " + f.Message); warnings.Add(f.Message); }
        }
        if (input.Groups.Count == 0) { o.WriteLine("error: input has no AGV groups"); return 1; }
        var arc = Path.Combine(a.ProjectDir, a.Model + ".arc");
        var sig = InputSignature.Read(arc);
        if (sig == null) { var w = $"{arc}\\{InputSignature.FileName} 없음: 모델과 Excel 일치 여부를 확인하지 못함"; o.WriteLine("warning: " + w); warnings.Add(w); }
        else if (sig != InputSignature.Compute(input))
        {
            o.WriteLine("error: 모델 재생성 필요 — input.xlsx의 그룹·타입·자재·Routes·대기 CP·SimLength가 모델을 만들 때와 다릅니다. 플러그인에서 모델을 다시 만드세요.");
            return 1;
        }

        GroupClusters clusters;
        try
        {
            var (layout, traffic) = ArcLayout.Load(arc);
            clusters = GroupClusters.Build(layout, input, traffic);
        }
        catch (Exception e) when (e is IOException || e is FormatException) // FileNotFoundException is an IOException
        {
            clusters = GroupClusters.Singletons(input);
            var w = $"{e.Message}: layout unavailable, every group is searched on its own";
            o.WriteLine("warning: " + w); warnings.Add(w);
        }
        foreach (var c in clusters.Clusters) o.WriteLine("cluster: " + string.Join(", ", c));
        foreach (var w in clusters.Warnings) o.WriteLine("warning: " + w);
        foreach (var f in InputValidator.SupplyCapacity(input, clusters.SupplyLength)) { o.WriteLine("input: " + f.Message); warnings.Add(f.Message); }

        var runsDir = Path.Combine(a.ProjectDir, "runs");
        if (!a.Append && RotateRuns(runsDir) is { } old) o.WriteLine("previous runs moved to " + old);
        var progress = new SyncProgress<RunOutcome>(r => { lock (o) o.WriteLine($"run {r.RunId} {(r.Success ? "ok" : "FAILED")} {r.Duration.TotalSeconds:0.0} s"); });
        var mx = new ModelExecutor(new ExecutorOptions(a.ProjectDir, a.Model, a.Parallel, TimeSpan.FromMinutes(a.TimeoutMin), a.DialogButton, a.AmHome,
            a.Debug, runsDir, Progress: progress), input);
        var exec = new ParallelExecutor(a.Parallel, mx.Run);
        var res = SearchStrategy.Search(input, exec, clusters, new SearchOptions(a.InitialReps, a.Impact), ct);
        if (a.Impact) { Directory.CreateDirectory(runsDir); ImpactTsv.Write(Path.Combine(runsDir, "impact.tsv"), res.Impact); }

        // every baseline run failed: the model itself does not run, so no search result means anything
        var baseline = res.Trace.Where(t => t.Phase == "baseline")
            .SelectMany(t => t.Seeds.Select(seed => ModelRunner.RunId(input, t.Scenario with { Seed = seed, Debug = a.Debug }))).ToList();
        if (baseline.Count > 0 && baseline.All(mx.Failed))
        {
            foreach (var m in mx.FailureMessages.Take(5)) o.WriteLine("failed run: " + m);
            o.WriteLine("error: every baseline run failed — the model does not run.");
            var msg = Directory.Exists(runsDir)
                ? new DirectoryInfo(runsDir).GetFiles(a.Model + ".message", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault() : null;
            if (msg != null) { o.WriteLine($"{msg.FullName}:"); foreach (var l in File.ReadLines(msg.FullName).Take(5)) o.WriteLine("  " + l); }
            return 1;
        }

        foreach (var w in res.Warnings) o.WriteLine("warning: " + w);
        foreach (var m in mx.FailureMessages) o.WriteLine("failed run: " + m);

        o.WriteLine($"{"group",-12}{"decided",8}  {"min-max",-8}{"judged",7}");
        foreach (var g in input.Groups)
            o.WriteLine($"{g.Group,-12}{res.Decided[g.Group],8}  {g.MinCount + "-" + g.MaxCount,-8}{res.Trace.Count(t => t.Phase != "impact" && t.VerdictsByGroup.ContainsKey(g.Group)),7}");
        o.WriteLine($"total {exec.Runs} runs ({mx.Failures} failed), elapsed {sw.Elapsed:hh\\:mm\\:ss}");
        return warnings.Count + res.Warnings.Count + clusters.Warnings.Count + mx.Failures > 0 ? 2 : 0;
    }

    /// <summary>IProgress that calls back on the reporting thread (Progress&lt;T&gt; would post to the thread pool and lose ordering).</summary>
    sealed class SyncProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
}
