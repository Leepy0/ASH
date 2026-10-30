using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ASH.Core;
using ASH.Core.Input;
using ASH.Core.Model;
using ASH.Core.Runner;

namespace ASH.Server;

/// <summary>One step of the production limit: Rule is the short name of the rule that failed ("" when it passed), Reason the judge's text,
/// Worst the material, block or group that failed worst.</summary>
public sealed record LimitStep(int Pct, bool Ok, string Rule, string Reason, string Worst);

/// <summary>&lt;job&gt;\limit.json: the finished steps in order. Done once the steps ran out, failed <see cref="ProductionLimit.MaxFails"/> times or hit an error.</summary>
public sealed class LimitInfo
{
    public List<LimitStep> Steps { get; set; } = new();
    public bool Done { get; set; }
    public string? Error { get; set; }

    /// <summary>The highest step passed before the first failure; null when +0 % failed or nothing ran yet.</summary>
    [JsonIgnore] public int? Passed => Steps.TakeWhile(s => s.Ok).LastOrDefault()?.Pct;
    [JsonIgnore] public LimitStep? FirstFail => Steps.FirstOrDefault(s => !s.Ok);
}

/// <summary>
/// 생산량 한계, run by FilmWorker between jobs like the scene search, so AutoMod never runs twice at once. &lt;job&gt;\limit-request.txt queues a job
/// (its text is the progress the page shows); each turn runs one step (the decided counts at the step's takt, the settings' seeds, judged as the
/// search's confirm round), so a job sent meanwhile goes first between steps. A server restart picks the request up again at the next step.
/// </summary>
public sealed class LimitWorker(JobStore jobs, IFilmAutoMod am, IConfiguration cfg, ILogger<LimitWorker> logger)
{
    public const string RequestFile = "limit-request.txt", ResultFile = "limit.json", Queued = "대기", Busy = "생산량 한계 계산 중";
    /// <summary>A conveyor model built before its line clock read the takt from params.txt: its line would keep the built takt.</summary>
    public const string OldConveyor = "컨베이어 모델 재생성 필요";
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    static readonly Regex ParamsClock = new(@"P_cvsrc_\d+ arriving procedure\s+wait for V_takt\(", RegexOptions.Compiled);

    readonly object _gate = new();
    string? _id;
    CancellationTokenSource? _cts;

    static string Req(JobStore js, string id) => Path.Combine(js.JobDir(id), RequestFile);
    static string Res(JobStore js, string id) => Path.Combine(js.JobDir(id), ResultFile);

    public static LimitInfo? Read(JobStore js, string id)
    {
        try { return File.Exists(Res(js, id)) ? JsonSerializer.Deserialize<LimitInfo>(File.ReadAllText(Res(js, id)), Json) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    /// <summary>The request's text (<see cref="Queued"/> or the running step's progress); null when none waits or runs.</summary>
    public static string? Progress(JobStore js, string id)
    {
        try { return File.Exists(Req(js, id)) ? File.ReadAllText(Req(js, id)) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Queued; }
    }

    /// <summary>Queues a fresh run (the earlier result goes). False when one is queued or running already.</summary>
    public bool Request(string id)
    {
        if (File.Exists(Req(jobs, id))) return false;
        File.Delete(Res(jobs, id));
        File.WriteAllText(Req(jobs, id), Queued, new UTF8Encoding(false));
        jobs.Wake();
        return true;
    }

    /// <summary>Drops the request; a step running now stops starting runs (runs in flight finish) and is not kept.</summary>
    public void Cancel(string id)
    {
        TryDelete(Req(jobs, id));
        lock (_gate) if (_id == id) _cts?.Cancel();
    }

    /// <summary>A job whose request waits, null when none.</summary>
    public JobInfo? Next() => jobs.All().LastOrDefault(j => Waits(jobs, j.Id));

    /// <summary>Whether the job's limit run is queued or running.</summary>
    public static bool Waits(JobStore js, string id) => File.Exists(Req(js, id)) && Read(js, id)?.Done != true;

    public void RunStep(JobInfo j, CancellationToken stop)
    {
        var info = Read(jobs, j.Id) ?? new LimitInfo();
        if (!File.Exists(Req(jobs, j.Id))) return; // cancelled meanwhile
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stop);
        lock (_gate) { _id = j.Id; _cts = cts; }
        using var log = new StampedWriter(new StreamWriter(jobs.LogPath(j.Id), true, new UTF8Encoding(false)) { AutoFlush = true });
        try
        {
            var pct = ProductionLimit.Pcts[info.Steps.Count];
            log.WriteLine($"생산량 한계 +{pct}%");
            info.Steps.Add(Step(j, pct, log, cts.Token));
            info.Done = info.Steps.Count == ProductionLimit.Pcts.Count || info.Steps.Count(s => !s.Ok) >= ProductionLimit.MaxFails;
        }
        catch (OperationCanceledException)
        {
            log.WriteLine($"생산량 한계 중단");
            // a server stop keeps the request for the next start; a cancel deleted it (again here: a progress write may have raced it)
            if (!stop.IsCancellationRequested) TryDelete(Req(jobs, j.Id));
            return;
        }
        catch (Exception e)
        {
            log.WriteLine("limit: " + e.Message);
            logger.LogWarning("생산량 한계 실패 {Id}: {Error}", j.Id, e.Message);
            info.Error = e.Message; info.Done = true;
        }
        finally { lock (_gate) { _id = null; _cts = null; } }
        // not saved: the request stays, so the step runs again rather than the result being lost
        if (!Save(Res(jobs, j.Id), JsonSerializer.Serialize(info, Json))) logger.LogWarning("생산량 한계 저장 실패 {Id}", j.Id);
        else if (info.Done) TryDelete(Req(jobs, j.Id));
    }

    /// <summary>Whole-file replace, retried: a page reading the file at that moment holds it open for an instant.</summary>
    static bool Save(string path, string text)
    {
        for (int i = 0; ; i++)
            try
            {
                File.WriteAllText(path + ".tmp", text, new UTF8Encoding(false));
                File.Move(path + ".tmp", path, true);
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (i == 4) return false;
                Thread.Sleep(100);
            }
    }

    /// <summary>The progress text, only while the request is there: a cancel deletes it and must not see it come back.</summary>
    static void SetProgress(JobStore js, string id, string text)
    {
        if (File.Exists(Req(js, id))) Save(Req(js, id), text);
    }

    LimitStep Step(JobInfo j, int pct, TextWriter log, CancellationToken ct)
    {
        var proj = jobs.ProjectDir(j.Id);
        var input0 = SearchJob.ReadInput(proj, TextWriter.Null, new List<string>(), out var err) ?? throw new InvalidOperationException(err);
        if (input0.Conveyors.Count > 0 && !ClockFromParams(proj, j.Model)) throw new InvalidOperationException(OldConveyor);
        if (!File.Exists(Path.Combine(proj, j.Model + ".exe")))
        {
            // a result uploaded from a PC, or a purged job: build once in the job's folder (as the scene search does)
            ModelBuilder.PrepareBuildDir(proj, j.Model);
            var b = am.Build(proj, j.Model);
            if (!b.Success) throw new InvalidOperationException("빌드 실패: " + (b.MakeErrText + "\n" + b.Am2ErrText).Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0));
        }
        var input = ProductionLimit.Scale(input0, pct);
        var counts = input.Groups.ToDictionary(g => g.Group, g => j.Decided != null && j.Decided.TryGetValue(g.Group, out var n) ? n : g.MinCount);
        var runsDir = Path.Combine(jobs.JobDir(j.Id), "limit", "p" + pct.ToString(CultureInfo.InvariantCulture));
        if (Directory.Exists(runsDir)) Directory.Delete(runsDir, true); // a step cut short by a restart runs again from the start
        var jr = new AutoModJobRunner(cfg);
        int par = JobProgress.Parallel(cfg), reps = ProductionLimit.Replications(input.Settings), done = 0;
        var progress = new SyncProgress(o =>
        {
            var n = Interlocked.Increment(ref done);
            lock (log)
            {
                log.WriteLine($"run {o.RunId} {(o.Success ? "ok" : "FAILED")} {o.Duration.TotalSeconds:0.0} s");
                SetProgress(jobs, j.Id, $"+{pct}% · {n}/{reps}");
            }
        });
        SetProgress(jobs, j.Id, $"+{pct}% · 0/{reps}");
        var mx = new ModelExecutor(new ExecutorOptions(proj, j.Model, par, jr.RunTimeout, jr.DialogButton, jr.AmHome, RunsDir: runsDir, Progress: progress), input, am.Run, s =>
        {
            var m = ModelVariants.NameFor(input, j.Model, s); // a cycle group's count runs in its own model, built here once (the steps share it)
            if (m != j.Model) ModelVariants.Ensure(proj, j.Model, m, input, s, jr.AmHome, jr.BuildTimeout, am.Build);
            return m;
        });
        var res = SearchStrategy.Confirm(input, new ParallelExecutor(par, mx.Run), counts, ct);
        ct.ThrowIfCancellationRequested(); // a step cut short is no result
        try { return Judged(pct, res.Trace[0], input, counts, runsDir, log); }
        // limit.json keeps what counts; the run folders would only grow (job retention does not know them)
        finally { try { Directory.Delete(runsDir, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    }

    static LimitStep Judged(int pct, SearchStep step0, ProjectInput input, Dictionary<string, int> counts, string runsDir, TextWriter log)
    {
        var bad = step0.VerdictsByGroup.Where(v => !v.Value.Ok).OrderBy(v => Order(v.Value.Rule)).ToList();
        if (bad.Count == 0) return new LimitStep(pct, true, "", "", "");
        var rule = bad[0].Value.Rule;
        var groups = bad.Where(v => v.Value.Rule == rule).Select(v => v.Key).ToList();
        var sets = new List<ResultSet>();
        foreach (var seed in step0.Seeds)
            try { sets.Add(ResultParser.Parse(Path.Combine(runsDir, ModelRunner.RunId(input, new RunScenario(seed, counts)), "result.tsv"))); }
            catch (Exception e) when (e is IOException or InvalidDataException) { } // a failed run has no result to look into
        var step = new LimitStep(pct, false, RuleName(rule), bad[0].Value.Reason, Worst(rule, groups, sets));
        lock (log) log.WriteLine($"limit: +{pct}% {step.Rule} {step.Worst} ({step.Reason})");
        return step;
    }

    /// <summary>Judge's order of the rules, so the step names what the search would have stopped at first.</summary>
    internal static int Order(RuleCode r) => r switch
    {
        RuleCode.NoResult => 0, RuleCode.StopTime => 1, RuleCode.Supply => 2, RuleCode.Deadlock => 3, RuleCode.Follow => 4, RuleCode.Utilization => 5, _ => 6,
    };

    public static string RuleName(RuleCode r) => r switch
    {
        RuleCode.StopTime => "자재 품절", RuleCode.Supply => "생산 미달", RuleCode.Deadlock => "교착", RuleCode.Utilization => "가동률 초과",
        RuleCode.Stock => "재고 부족", RuleCode.Follow => "뒤차 대기", _ => "실행 실패",
    };

    /// <summary>What failed worst under the rule, over the step's seeds: the material stopped longest or furthest under its production,
    /// the block waited at longest in a deadlock, the station whose swaps a waiting vehicle took least often, or the failing group.</summary>
    internal static string Worst(RuleCode rule, List<string> groups, List<ResultSet> sets)
    {
        static string F(double d, string f) => d.ToString(f, CultureInfo.InvariantCulture);
        var mats = sets.SelectMany(s => s.Materials).Where(m => groups.Contains(m.Group)).GroupBy(m => m.Name).ToList();
        switch (rule)
        {
            case RuleCode.StopTime:
                var s = mats.Select(g => (g.Key, V: g.Average(m => m.StopTimeSec))).OrderByDescending(x => x.V).FirstOrDefault();
                return s.Key == null ? "" : $"{s.Key} {F(s.V, "0")}초";
            case RuleCode.Supply:
                var p = mats.Select(g => (g.Key, V: g.Average(m => m.Expected > 0 ? m.Consumed / m.Expected : 1))).OrderBy(x => x.V).FirstOrDefault();
                return p.Key == null ? "" : $"{p.Key} {F(p.V * 100, "0")}%";
            case RuleCode.Deadlock:
                return sets.SelectMany(x => x.Blocks).GroupBy(b => b.Name).OrderByDescending(g => g.Average(b => b.MaxWaitSec)).FirstOrDefault()?.Key ?? "";
            case RuleCode.Follow: // stations without swaps (or from a model before them: NaN) left out
                var w = mats.Where(g => g.Sum(m => m.SwapN) > 0).Select(g => (g.Key, V: g.Sum(m => m.FollowN) / g.Sum(m => m.SwapN))).OrderBy(x => x.V).FirstOrDefault();
                return w.Key == null ? "" : $"{w.Key} {F(w.V * 100, "0")}%";
            case RuleCode.Utilization:
                return groups[0];
            default: return "";
        }
    }

    /// <summary>False for a conveyor model whose line clock was built in (before it read V_takt); an unreadable file lets the run tell.</summary>
    static bool ClockFromParams(string proj, string model)
    {
        try { return ParamsClock.IsMatch(File.ReadAllText(Path.Combine(proj, model + ".arc", model + "~.asy"))); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; }
    }

    static void TryDelete(string path) { try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }

    sealed class SyncProgress(Action<RunOutcome> report) : IProgress<RunOutcome> { public void Report(RunOutcome value) => report(value); }
}
