using ASH.Core.AutoMod;
using ASH.Core.Model;
using ASH.Core.Runner;

namespace ASH.Server;

/// <summary>What a job produced, for job.json.</summary>
public sealed record JobOutcome(int ExitCode, string? Error, IReadOnlyDictionary<string, int>? Decided, IReadOnlyList<IReadOnlyList<string>>? Clusters,
    IReadOnlyList<string> ConfirmRunIds, IReadOnlyList<string> Warnings, IReadOnlyList<string> FailureMessages);

/// <summary>Builds and searches one job's project folder. The server's own runner drives AutoMod; tests plug in a fake.</summary>
public interface IJobRunner
{
    JobOutcome Run(JobInfo job, string projectDir, TextWriter log, Action<string> phase, IProgress<RunOutcome> progress, Action<SearchProgress> search, CancellationToken ct);
}

/// <summary>Jobs:AmHome (AutoMod folder), Jobs:Parallel (model runs at a time), Jobs:DialogButton (trial installs: "Student Version"),
/// Jobs:RunTimeoutMin, Jobs:BuildTimeoutMin.</summary>
public sealed class AutoModJobRunner(IConfiguration cfg) : IJobRunner
{
    public string AmHome => cfg["Jobs:AmHome"] is { Length: > 0 } h ? h : @"C:\AutoMod\12_6";
    int Parallel => JobProgress.Parallel(cfg);
    internal string? DialogButton => cfg["Jobs:DialogButton"] is { Length: > 0 } b ? b : null;
    internal TimeSpan RunTimeout => TimeSpan.FromMinutes(Math.Max(1, cfg.GetValue("Jobs:RunTimeoutMin", 30)));
    internal TimeSpan BuildTimeout => TimeSpan.FromMinutes(Math.Max(1, cfg.GetValue("Jobs:BuildTimeoutMin", 10)));

    public JobOutcome Run(JobInfo job, string projectDir, TextWriter log, Action<string> phase, IProgress<RunOutcome> progress, Action<SearchProgress> search, CancellationToken ct)
    {
        static JobOutcome Fail(string e) => new(1, e, null, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        var am = AmBuilder.FindBuildExe(AmHome);
        if (am == null) return Fail($"서버에 AutoMod가 없습니다: {AmBuilder.BuildExeCandidates(AmHome)} (appsettings.json의 Jobs:AmHome)");

        phase("빌드");
        log.WriteLine($"build: {am} -bq {job.Model}");
        ModelBuilder.PrepareBuildDir(projectDir, job.Model);
        var b = AmBuilder.Build(AmHome, projectDir, job.Model, BuildTimeout);
        foreach (var t in b.ClosedDialogTitles) log.WriteLine("build: closed dialog " + t);
        if (!b.Success)
        {
            var text = string.Join("\n", new[] { b.MakeErrText, b.Am2ErrText }.Where(x => !string.IsNullOrWhiteSpace(x)));
            foreach (var l in text.Split('\n').Take(40)) log.WriteLine("build: " + l.TrimEnd());
            return Fail("AutoMod 빌드 실패" + (text.Length > 0 ? ": " + text.Split('\n').First(x => x.Trim().Length > 0).Trim() : " (오류 출력 없음)"));
        }
        ct.ThrowIfCancellationRequested();

        phase("대수 탐색");
        var r = SearchJob.Run(new SearchJobOptions(projectDir, job.Model, AmHome, Parallel, DialogButton, RunTimeout, job.InitialReps, job.Impact), null, log, progress, ct, onProgress: search);
        var sum = SearchSummaryFile.From(r);
        return new JobOutcome(r.ExitCode, r.Error, r.Search?.Decided, r.Clusters?.Clusters, sum.ConfirmRunIds, sum.Warnings, sum.FailureMessages);
    }
}

/// <summary>One job at a time, oldest first. A job that runs past Jobs:JobTimeoutHours (default 24) is cancelled, so one stuck job never blocks the queue.
/// Filming work (scene requests, films) runs on the same loop after each job, so AutoMod never runs twice at once.</summary>
public sealed class JobWorker(JobStore store, IJobRunner runner, FilmWorker films, IConfiguration cfg, ILogger<JobWorker> logger) : BackgroundService
{
    readonly object _gate = new();
    string? _runningId;
    CancellationTokenSource? _runningCts;

    bool Enabled => cfg.GetValue("Jobs:WorkerEnabled", true);

    /// <summary>Cancels the job if it is the one running now; false otherwise.</summary>
    public bool CancelRunning(string id)
    {
        lock (_gate)
        {
            if (_runningId != id || _runningCts == null) return false;
            _runningCts.Cancel();
            return true;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!Enabled) { logger.LogInformation("작업 실행이 꺼져 있습니다(Jobs:WorkerEnabled=false)."); return; }
        while (!stop.IsCancellationRequested)
        {
            var job = store.TakeNext();
            if (job != null) await Task.Run(() => RunOne(job, stop), CancellationToken.None);
            var filmed = await Task.Run(() =>
            {
                try { return films.RunNext(stop); }
                catch (Exception e) { logger.LogError(e, "영상 작업 실패"); return false; }
            }, CancellationToken.None);
            if (job == null && !filmed)
                try { await store.WaitForWorkAsync(TimeSpan.FromSeconds(10), stop); } catch (OperationCanceledException) { return; }
        }
    }

    internal void RunOne(JobInfo job, CancellationToken stop)
    {
        var hours = Math.Max(1, cfg.GetValue("Jobs:JobTimeoutHours", 24));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stop);
        cts.CancelAfter(TimeSpan.FromHours(hours));
        lock (_gate) { _runningId = job.Id; _runningCts = cts; }
        if (store.Get(job.Id)?.CancelRequested == true) cts.Cancel();
        using var logFile = new StreamWriter(store.LogPath(job.Id), true, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
        var log = new LineWriter(logFile, line => store.Update(job.Id, j => j.LastLine = line.Length > 300 ? line[..300] : line));
        void Phase(string p) { store.Update(job.Id, j => j.Phase = p); log.WriteLine($"[{ASH.Core.SeoulTime.NowText}] {p}"); }
        int groupsLeft = 1;
        var progress = new SyncProgress<RunOutcome>(r =>
        {
            store.Update(job.Id, j =>
            {
                j.RunsDone++; if (!r.Success) j.RunsFailed++;
                j.RunSeconds += r.Duration.TotalSeconds;
                if (j.ExpectedRuns is { } e && j.RunsDone > e) j.ExpectedRuns = SearchEstimate.Raise(e, j.RunsDone, groupsLeft);
            });
            lock (log) log.WriteLine($"run {r.RunId} {(r.Success ? "ok" : "FAILED")} {r.Duration.TotalSeconds:0.0} s");
        });
        void Search(SearchProgress p)
        {
            try
            {
                store.Update(job.Id, j => { groupsLeft = p.GroupsLeft; j.ExpectedRuns ??= p.ExpectedRuns; j.Partial = p.Confirmed.Count == 0 ? null : new(p.Confirmed); j.Phase = p.Phase; });
            }
            catch (Exception e) { logger.LogWarning(e, "작업 {Id} 진행 표시 실패", job.Id); }
        }
        JobOutcome? o = null; string? error = null; bool cancelled = false;
        try
        {
            log.WriteLine($"[{ASH.Core.SeoulTime.NowText}] 시작: {job.Project} / {job.Plan} / 모델 {job.Model} (보낸 사람 {job.Submitter}, {job.Ip})");
            o = runner.Run(job, store.ProjectDir(job.Id), log, Phase, progress, Search, cts.Token);
            error = o.Error;
            // scene candidates for the cut editor (spec 6.2): one more run at the conclusion with the event log; a failure only leaves a note in scenes.json
            if (job.Source == JobSource.Server && o.ExitCode != 1 && o.Decided != null)
            {
                Phase("장면 후보 찾기");
                films.FindScenes(job, o.Decided, 0, log);
            }
        }
        catch (Exception e) when (e is OperationCanceledException || e.InnerException is OperationCanceledException)
        {
            cancelled = true;
            error = stop.IsCancellationRequested ? "서버가 멈추어 중단되었습니다." : store.Get(job.Id)?.CancelRequested == true ? "취소되었습니다." : $"시간 제한({hours}시간)을 넘어 중단되었습니다.";
        }
        catch (Exception e)
        {
            error = e.Message;
            log.WriteLine(e.ToString());
            logger.LogError(e, "작업 {Id} 실패", job.Id);
        }
        finally { lock (_gate) { _runningId = null; _runningCts = null; } }
        log.WriteLine($"[{ASH.Core.SeoulTime.NowText}] 끝: {error ?? "정상"}");
        store.Update(job.Id, j =>
        {
            j.FinishedUtc = DateTime.UtcNow;
            j.Error = error;
            j.Phase = null;
            j.State = cancelled && j.CancelRequested ? JobState.Cancelled : o != null && o.ExitCode != 1 ? JobState.Done : JobState.Failed;
            if (o == null) return;
            j.ExitCode = o.ExitCode;
            j.Decided = o.Decided?.ToDictionary(kv => kv.Key, kv => kv.Value);
            j.Clusters = o.Clusters?.Select(c => c.ToList()).ToList();
            j.ConfirmRunIds = o.ConfirmRunIds.ToList();
            j.Warnings = o.Warnings.ToList();
            j.FailureMessages = o.FailureMessages.ToList();
        });
    }

    /// <summary>Text writer that also reports each finished line (the page shows the last one).</summary>
    sealed class LineWriter(TextWriter inner, Action<string> onLine) : TextWriter
    {
        readonly System.Text.StringBuilder _line = new();
        public override System.Text.Encoding Encoding => inner.Encoding;
        public override void Write(char value)
        {
            lock (_line)
            {
                inner.Write(value);
                if (value == '\n') { var l = _line.ToString().TrimEnd('\r'); _line.Clear(); if (l.Length > 0) onLine(l); }
                else _line.Append(value);
            }
        }
        public override void Write(string? value) { if (value != null) foreach (var c in value) Write(c); }
        public override void WriteLine(string? value) { Write(value); Write('\n'); }
    }

    sealed class SyncProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
}
