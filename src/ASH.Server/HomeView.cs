using ASH.Core;
using ASH.Core.Analysis;

namespace ASH.Server;

/// <summary>Who "mine" is: the jobs sent from this IP, and every other job under a name that IP has used.</summary>
public static class Mine
{
    public static HashSet<string> Names(IReadOnlyList<JobInfo> jobs, string ip) =>
        jobs.Where(j => j.Ip == ip && j.Submitter != "").Select(j => j.Submitter).ToHashSet(StringComparer.Ordinal);

    public static bool Is(JobInfo j, string ip, ISet<string> names) => j.Ip == ip || names.Contains(j.Submitter);
}

/// <summary>Name/Sub = analysis name and "plan vN" for the home card; Failed = failed runs (cuts for a film); Left = "약 22분 남음 · 02:31 끝".</summary>
public sealed record RunningCard(string JobId, string Title, string State, string? Phase, double? Progress, int RunsDone, int? ExpectedRuns, string? AvgRun, string? Eta, string Note, string? Film, string? Href = null,
    string? Name = null, string? Sub = null, int Failed = 0, string? Left = null);
public sealed record QueuedLine(string JobId, string Title, string? Start, string Submitter = "", bool Mine = false);
/// <summary>결론 확인 lane: the version whose conclusion an analysis without a film shows (selected, else the latest). Util/Stop NaN when unknown; Block = longest mean wait.</summary>
public sealed record ConclusionCard(string AnalysisId, string Name, string JobId, string Plan, int Version, DateTime? FinishedUtc, int Total, double Util, double UtilMax, double Stop, string? Block);
/// <summary>영상 lane: a queued or running film of a shown analysis.</summary>
public sealed record FilmCard(string FilmId, string AnalysisId, string Name, string State, int Done, int Total, string Eta);
/// <summary>보고서 lane: an analysis' latest finished film; JobId = the version the report is taken from (selected, else the film's first), null when gone.</summary>
public sealed record ReportCard(string AnalysisId, string Name, string FilmId, string? JobId, string? Plan, int Version, bool Selected, int? Total, DateTime? FinishedUtc);
public sealed record PlanChip(string PlanId, string PlanName, string JobId, int Version, string State, int? Total, double? Progress, bool Selected);
public sealed record AnalysisRow(string Id, string Name, bool Active, bool Mine, int Plans, int Versions, int Selected, int Films, DateTime? LastSentUtc, IReadOnlyList<PlanChip> Chips, IReadOnlyList<string> Submitters);

public sealed record HomeView(RunningCard? Running, bool WorkerOn, int QueuedCount, IReadOnlyList<QueuedLine> Queued, bool Me, IReadOnlyList<AnalysisRow> InProgress, IReadOnlyList<AnalysisRow> Finished, int Unclassified)
{
    public IReadOnlyList<ConclusionCard> Conclusions { get; init; } = [];
    public IReadOnlyList<FilmCard> Films { get; init; } = [];
    public IReadOnlyList<ReportCard> Reports { get; init; } = [];

    static string Hm(DateTime utc) => SeoulTime.FromUtc(utc).ToString("HH:mm");

    public static HomeView Build(IReadOnlyList<AnalysisInfo> analyses, IReadOnlyList<JobInfo> jobs, IReadOnlyList<FilmInfo> films, FilmInfo? filming,
        (int Ahead, DateTime? Start, TimeSpan? Typical) queue, bool workerOn, int parallel, string ip, bool? me, DateTime nowUtc, FileRoutes.FileQuery? tags = null,
        JobStore? store = null)
    {
        var names = Mine.Names(jobs, ip);
        var hasMine = jobs.Any(j => Mine.Is(j, ip, names));
        var onlyMine = (me ?? true) && hasMine; // nothing mine (even with me=1) shows everything

        string PlanName(AnalysisInfo a, JobInfo j) => a.Plans.FirstOrDefault(p => p.Id == j.PlanId)?.Name ?? j.Plan;
        (string Name, string Sub) Parts(JobInfo j) => analyses.FirstOrDefault(x => x.Id == j.AnalysisId) is { } a ? (a.Name, $"{PlanName(a, j)} v{j.Version}") : (j.Project, j.Plan);
        string Title(JobInfo j) { var (n, s) = Parts(j); return analyses.Any(x => x.Id == j.AnalysisId) ? $"{n} › {s}" : $"{n} / {s}"; }
        string Left(DateTime? end)
        {
            if (end is not { } e) return "끝날 시각 계산 중";
            var m = Math.Max(1, (int)Math.Ceiling((e - nowUtc).TotalMinutes));
            return $"약 {(m >= 60 ? $"{m / 60}시간 {m % 60}분" : $"{m}분")} 남음 · {Hm(e)} 끝";
        }

        RunningCard? running = null;
        if (jobs.Where(j => j.State == JobState.Running).OrderBy(j => j.StartedUtc ?? j.CreatedUtc).FirstOrDefault() is { } r)
        {
            var eta = JobProgress.Eta(r, queue.Typical, parallel, nowUtc);
            running = new(r.Id, Title(r), r.State, r.Phase, JobProgress.Ratio(r), r.RunsDone, r.ExpectedRuns, r.RunsDone > 0 ? JobPages.Dur(JobProgress.AvgRun(r)) : null,
                eta is { } e ? Hm(e) : null, r.VersionNote.Length > 0 ? r.VersionNote : r.LastLine ?? "",
                filming is { Cuts.Count: > 0 } f ? $"촬영 {f.Done}/{f.Cuts.Count * f.Request.JobIds.Count}" : null, null, Parts(r).Name, Parts(r).Sub, r.RunsFailed, Left(eta));
        }
        // the worker runs jobs and films one at a time, so a film is never "running" beside a job: show it in the card
        DateTime? filmFree = null;
        if (running == null && filming is { State: JobState.Running } fm)
        {
            var total = Math.Max(1, fm.Items.Count);
            if (fm.StartedUtc is { } fs0 && fm.Done > 0) filmFree = nowUtc + (nowUtc - fs0) * (total - fm.Done) / fm.Done;
            running = new(fm.Id, $"{fm.AnalysisName} › 영상", fm.State, fm.Phase, (double)fm.Done / total, fm.Done, fm.Items.Count, null,
                filmFree is { } ff ? Hm(ff) : null, "", LiveFields.FilmDone(fm), "/films/" + fm.Id, fm.AnalysisName, "영상", fm.Failed, Left(filmFree));
            filmFree ??= nowUtc;
        }

        var queuedJobs = jobs.Where(j => j.State == JobState.Queued).OrderBy(j => j.CreatedUtc).ToList();
        var queued = queuedJobs.Select((j, i) => new QueuedLine(j.Id, Title(j),
            queue.Typical is { } t && (filmFree ?? queue.Start - t * queuedJobs.Count) is { } first ? Hm(first + t * i) : null, j.Submitter, Mine.Is(j, ip, names))).ToList();

        var rows = new List<AnalysisRow>();
        var conclusions = new List<ConclusionCard>(); var filmCards = new List<FilmCard>(); var reports = new List<ReportCard>();
        foreach (var a in analyses)
        {
            if (tags != null && !FileRoutes.TagMatch(a, tags)) continue; // before the 진행 중/완료 split
            var mine = jobs.Where(j => j.AnalysisId == a.Id).ToList();
            var isMine = mine.Any(j => Mine.Is(j, ip, names));
            if (onlyMine && !isMine) continue;
            var chips = a.Plans.Select(p => mine.Where(j => j.PlanId == p.Id).OrderByDescending(j => j.Version).FirstOrDefault() is { } j
                ? new PlanChip(p.Id, p.Name, j.Id, j.Version, j.State, j.Decided?.Values.Sum(), JobProgress.Ratio(j), j.Selected) : null).OfType<PlanChip>().ToList();
            rows.Add(new(a.Id, a.Name, mine.Any(j => j.State is JobState.Queued or JobState.Running), isMine, a.Plans.Count, mine.Count, mine.Count(j => j.Selected),
                films.Count(f => f.Request.AnalysisId == a.Id), mine.Count == 0 ? null : mine.Max(j => j.CreatedUtc), chips,
                mine.Select(j => j.Submitter).Where(s => s != "").Distinct().ToList()));

            // lanes: a queued/running film -> 영상, a finished film -> 보고서, otherwise a conclusion -> 결론 확인 (a failed/cancelled film counts as none)
            var aFilms = films.Where(f => f.Request.AnalysisId == a.Id).ToList();
            foreach (var f in aFilms.Where(f => f.State is JobState.Queued or JobState.Running).OrderBy(f => f.CreatedUtc))
                filmCards.Add(new(f.Id, a.Id, a.Name, f.State, f.Done, f.Items.Count, LiveFields.FilmEta(f, nowUtc)));
            var decided = mine.Where(j => j.State == JobState.Done && j.Decided != null).OrderByDescending(j => j.Selected).ThenByDescending(j => j.FinishedUtc ?? j.CreatedUtc).ToList();
            if (aFilms.Where(f => f.State == JobState.Done).MaxBy(f => f.FinishedUtc ?? f.CreatedUtc) is { } done)
            {
                var rj = decided.FirstOrDefault(j => j.Selected) ?? done.Request.JobIds.Select(id => mine.FirstOrDefault(j => j.Id == id)).FirstOrDefault(j => j is { State: JobState.Done });
                reports.Add(new(a.Id, a.Name, done.Id, rj?.Id, rj == null ? null : PlanName(a, rj), rj?.Version ?? 0, rj?.Selected == true, rj?.Decided?.Values.Sum(), done.FinishedUtc ?? done.CreatedUtc));
            }
            else if (aFilms.Count(f => f.State is JobState.Queued or JobState.Running) == 0 && decided.FirstOrDefault() is { } cj)
            {
                var m = store == null ? null : AnalysisPages.MetricsOf(store, cj);
                var block = m?.View.Blocks.Where(b => !double.IsNaN(b.AvgWaitSec)).MaxBy(b => b.AvgWaitSec)?.Name;
                conclusions.Add(new(a.Id, a.Name, cj.Id, PlanName(a, cj), cj.Version, cj.FinishedUtc, cj.Decided!.Values.Sum(), m?.Util ?? double.NaN, m?.View.UtilMax ?? double.NaN, m?.Stop ?? double.NaN, block));
            }
        }
        rows = rows.OrderByDescending(x => x.LastSentUtc).ToList();

        return new(running, workerOn, queuedJobs.Count, queued, onlyMine, rows.Where(x => x.Active).ToList(), rows.Where(x => !x.Active).ToList(),
            jobs.Count(j => j.AnalysisId == null))
        {
            Conclusions = conclusions.OrderByDescending(c => c.FinishedUtc).ToList(), Films = filmCards,
            Reports = reports.OrderByDescending(x => x.FinishedUtc).ToList(),
        };
    }
}
