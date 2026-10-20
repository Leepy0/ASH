using System.Diagnostics;
using ASH.Core.Input;
using ASH.Core.Runner;

namespace ASH.Runner;

public static class Program
{
    public static int Main(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    /// <summary>Exit codes: 0 ok, 1 error, 2 ok with warnings. <paramref name="input"/> replaces reading input.xlsx (tests).</summary>
    public static async Task<int> RunAsync(string[] args, ProjectInput? input = null, TextWriter? output = null)
    {
        var o = output ?? Console.Out;
        var a = RunnerArgs.Parse(args, out var err);
        if (a == null) { o.WriteLine("error: " + err); o.WriteLine(RunnerArgs.Usage); return 1; }
        try { return await Task.Run(() => Execute(a, input, o)); }
        catch (Exception e) { o.WriteLine("error: " + e.Message); if (a.Debug) o.WriteLine(e); return 1; }
    }

    static int Execute(RunnerArgs a, ProjectInput? input, TextWriter o)
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

        GroupClusters clusters;
        try
        {
            var (layout, traffic) = ArcLayout.Load(Path.Combine(a.ProjectDir, a.Model + ".arc"));
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

        var mx = new ModelExecutor(a, input);
        var exec = new ParallelExecutor(a.Parallel, mx.Run);
        var res = SearchStrategy.Search(input, exec, clusters, new SearchOptions(a.InitialReps, a.Impact));
        var runsDir = Path.Combine(a.ProjectDir, "runs");
        if (a.Impact) { Directory.CreateDirectory(runsDir); ImpactTsv.Write(Path.Combine(runsDir, "impact.tsv"), res.Impact); }
        foreach (var w in res.Warnings) o.WriteLine("warning: " + w);
        foreach (var m in mx.FailureMessages) o.WriteLine("failed run: " + m);

        o.WriteLine($"{"group",-12}{"decided",8}  {"min-max",-8}{"judged",7}");
        foreach (var g in input.Groups)
            o.WriteLine($"{g.Group,-12}{res.Decided[g.Group],8}  {g.MinCount + "-" + g.MaxCount,-8}{res.Trace.Count(t => t.Phase != "impact" && t.VerdictsByGroup.ContainsKey(g.Group)),7}");
        o.WriteLine($"total {exec.Runs} runs ({mx.Failures} failed), elapsed {sw.Elapsed:hh\\:mm\\:ss}");
        return warnings.Count + res.Warnings.Count + clusters.Warnings.Count + mx.Failures > 0 ? 2 : 0;
    }
}
