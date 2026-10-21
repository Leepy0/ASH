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
        if (args.Length == 1 && args[0] == "--version") { Console.WriteLine(RunnerUpdater.CurrentVersion); return 0; }
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); }; // Ctrl+C: finish runs in flight, start no new ones
        if (args.Length > 0 && args[0] == "--update") return RunnerUpdater.UpdateCommandAsync(args, Console.Out, cts.Token).GetAwaiter().GetResult();
        // a job starts only after the update check, so one job never runs on two versions; --no-update skips it
        if (RunnerArgs.Parse(args, out _) is { NoUpdate: false } a && RunnerUpdater.TryAsync(args, a.Server, Console.Out, cts.Token).GetAwaiter().GetResult() is { } code)
            return code;
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
    internal static string? RotateRuns(string runsDir) => SearchJob.RotateRuns(runsDir);

    static int Execute(RunnerArgs a, ProjectInput? input, TextWriter o, CancellationToken ct)
    {
        var progress = new SyncProgress<RunOutcome>(r => { lock (o) o.WriteLine($"run {r.RunId} {(r.Success ? "ok" : "FAILED")} {r.Duration.TotalSeconds:0.0} s"); });
        var job = new SearchJobOptions(a.ProjectDir, a.Model, a.AmHome, a.Parallel, a.DialogButton, TimeSpan.FromMinutes(a.TimeoutMin), a.InitialReps, a.Impact, a.Debug, a.Append);
        return SearchJob.Run(job, input, o, progress, ct).ExitCode;
    }

    /// <summary>IProgress that calls back on the reporting thread (Progress&lt;T&gt; would post to the thread pool and lose ordering).</summary>
    sealed class SyncProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
}
