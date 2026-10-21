using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using ASH.Core.Input;
using ASH.Core.Model;

namespace ASH.Core.Runner;

/// <param name="RunTimeout">Per model run.</param>
/// <param name="Append">Keep an existing runs\ folder instead of rotating it to runs.&lt;yyyyMMdd_HHmmss&gt;\.</param>
public sealed record SearchJobOptions(string ProjectDir, string Model, string? AmHome = null, int Parallel = 2, string? DialogButton = null,
    TimeSpan? RunTimeout = null, int InitialReps = 1, bool Impact = false, bool Debug = false, bool Append = false);

/// <summary>ExitCode: 0 ok, 1 error (Error says why), 2 ok with warnings. Search, Input and Clusters are null when the job stopped before the search.</summary>
public sealed record SearchJobResult(int ExitCode, string? Error, ProjectInput? Input, GroupClusters? Clusters, SearchResult? Search, int Runs, int Failures,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> FailureMessages, string RunsDir, TimeSpan Elapsed);

/// <summary>
/// The vehicle-count search over an already built model in a project folder: read the input (input.xlsx, or the plugin's cell snapshot when the workbook
/// cannot be read or is not there), check it matches the model, split groups into clusters from the model's own layout, run the search with parallel model
/// runs and write runs\summary.tsv (and runs\impact.tsv). Shared by ash-run and the server's worker, so both search the same way.
/// Every line it writes to <c>o</c> is the same as ash-run printed before.
/// </summary>
public static class SearchJob
{
    /// <summary>Moves an existing runs\ to runs.&lt;yyyyMMdd_HHmmss&gt;\ (suffix _2, _3... on a clash) so each job starts a fresh summary.</summary>
    public static string? RotateRuns(string runsDir)
    {
        if (!Directory.Exists(runsDir)) return null;
        var stamp = runsDir.TrimEnd('\\', '/') + "." + SeoulTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var dst = stamp;
        for (int k = 2; Directory.Exists(dst); k++) dst = stamp + "_" + k;
        Directory.Move(runsDir, dst);
        return dst;
    }

    /// <summary>The project's input: input.xlsx, else the cell snapshot the plugin writes (company document encryption leaves the workbook readable by Excel
    /// only). Null with the reason in <paramref name="error"/> when neither can be read. Findings and the snapshot notice go to <paramref name="warnings"/>.</summary>
    public static ProjectInput? ReadInput(string projectDir, TextWriter o, List<string> warnings, out string? error)
    {
        error = null;
        var xlsx = Path.Combine(projectDir, "input.xlsx");
        var snap = Path.Combine(projectDir, CellDump.FileName);
        if (!File.Exists(xlsx) && !File.Exists(snap)) { error = "input.xlsx not found: " + xlsx; return null; }
        List<ASH.Core.Layout.Finding> findings;
        ProjectInput input;
        try
        {
            if (!File.Exists(xlsx))
            {
                // the server only gets the snapshot: the workbook may be encrypted and is not sent
                o.WriteLine("input: " + CellDump.FileName + " (모델 생성 때 읽은 셀)");
                using var only = CellDump.Load(snap);
                input = ExcelReader.Read(only, out findings);
            }
            else input = ExcelReader.Read(xlsx, out findings);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException || ex is ExcelDataException) && File.Exists(xlsx) && File.Exists(snap))
        {
            var msg = $"input.xlsx를 읽지 못해({ex.GetType().Name}) 마지막 모델 생성 때의 셀 스냅샷 {CellDump.FileName}을 씁니다. 그 뒤의 input.xlsx 수정은 반영되지 않습니다.";
            o.WriteLine("warning: " + msg); warnings.Add(msg);
            using var snapBook = CellDump.Load(snap);
            input = ExcelReader.Read(snapBook, out findings);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            error = "input.xlsx를 읽지 못함: " + ex.Message;
            if (!(ex is ExcelDataException) && File.Exists(xlsx)) foreach (var line in ExcelDiagnosis.Explain(xlsx, ex)) o.WriteLine("  " + line);
            return null;
        }
        foreach (var f in findings) { o.WriteLine("input: " + f.Message); warnings.Add(f.Message); }
        return input;
    }

    /// <summary>Runs the search and, once it got that far, writes runs\search.tsv (what was decided), which a local result upload carries to the server.</summary>
    public static SearchJobResult Run(SearchJobOptions a, ProjectInput? input, TextWriter o, IProgress<RunOutcome>? progress = null, CancellationToken ct = default)
    {
        var r = RunSearch(a, input, o, progress, ct);
        if (r.Search != null)
        {
            try { SearchSummaryFile.Write(Path.Combine(r.RunsDir, SearchSummaryFile.FileName), SearchSummaryFile.From(r, a.Debug)); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { o.WriteLine("warning: " + SearchSummaryFile.FileName + " not written: " + e.Message); }
        }
        return r;
    }

    static SearchJobResult RunSearch(SearchJobOptions a, ProjectInput? input, TextWriter o, IProgress<RunOutcome>? progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var warnings = new List<string>();
        var runsDir = Path.Combine(a.ProjectDir, "runs");
        SearchJobResult Fail(string error, ProjectInput? inp = null, GroupClusters? cl = null, SearchResult? sr = null, int runs = 0, int failures = 0, IReadOnlyList<string>? fm = null)
        {
            o.WriteLine("error: " + error);
            return new SearchJobResult(1, error, inp, cl, sr, runs, failures, warnings, fm ?? new string[0], runsDir, sw.Elapsed);
        }
        if (!Directory.Exists(a.ProjectDir)) return Fail("project dir not found: " + a.ProjectDir);
        var exe = Path.Combine(a.ProjectDir, a.Model + ".exe");
        if (!File.Exists(exe)) return Fail($"{exe} not found. Build the model in the ZWCAD plugin first (ash-run only runs an existing model).");
        if (input == null)
        {
            input = ReadInput(a.ProjectDir, o, warnings, out var readError);
            if (input == null) return Fail(readError!);
        }
        if (input.Groups.Count == 0) return Fail("input has no AGV groups", input);
        var arc = Path.Combine(a.ProjectDir, a.Model + ".arc");
        var sig = InputSignature.Read(arc);
        if (sig == null) { var w = $"{arc}\\{InputSignature.FileName} 없음: 모델과 Excel 일치 여부를 확인하지 못함"; o.WriteLine("warning: " + w); warnings.Add(w); }
        else if (sig != InputSignature.Compute(input))
            return Fail("모델 재생성 필요 — input.xlsx의 그룹·타입·자재·Routes·대기 CP·SimLength가 모델을 만들 때와 다릅니다. 플러그인에서 모델을 다시 만드세요.", input);

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

        if (!a.Append && RotateRuns(runsDir) is { } old) o.WriteLine("previous runs moved to " + old);
        var mx = new ModelExecutor(new ExecutorOptions(a.ProjectDir, a.Model, a.Parallel, a.RunTimeout ?? TimeSpan.FromMinutes(30), a.DialogButton, a.AmHome,
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
            var msg = Directory.Exists(runsDir)
                ? new DirectoryInfo(runsDir).GetFiles(a.Model + ".message", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault() : null;
            var r = Fail("every baseline run failed — the model does not run.", input, clusters, res, exec.Runs, mx.Failures, mx.FailureMessages);
            if (msg != null) { o.WriteLine($"{msg.FullName}:"); foreach (var l in File.ReadLines(msg.FullName).Take(5)) o.WriteLine("  " + l); }
            return r;
        }

        foreach (var w in res.Warnings) o.WriteLine("warning: " + w);
        foreach (var m in mx.FailureMessages) o.WriteLine("failed run: " + m);

        o.WriteLine($"{"group",-12}{"decided",8}  {"min-max",-8}{"judged",7}");
        foreach (var g in input.Groups)
            o.WriteLine($"{g.Group,-12}{res.Decided[g.Group],8}  {g.MinCount + "-" + g.MaxCount,-8}{res.Trace.Count(t => t.Phase != "impact" && t.VerdictsByGroup.ContainsKey(g.Group)),7}");
        o.WriteLine($"total {exec.Runs} runs ({mx.Failures} failed), elapsed {sw.Elapsed:hh\\:mm\\:ss}");
        int code = warnings.Count + res.Warnings.Count + clusters.Warnings.Count + mx.Failures > 0 ? 2 : 0;
        return new SearchJobResult(code, null, input, clusters, res, exec.Runs, mx.Failures, warnings, mx.FailureMessages, runsDir, sw.Elapsed);
    }
}
