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

public sealed record RunningCard(string JobId, string Title, string State, string? Phase, double? Progress, int RunsDone, int? ExpectedRuns, string? AvgRun, string? Eta, string Note, string? Film);
public sealed record QueuedLine(string JobId, string Title, string? Start);
public sealed record PlanChip(string PlanId, string PlanName, string JobId, int Version, string State, int? Total, double? Progress, bool Selected);
public sealed record AnalysisRow(string Id, string Name, bool Active, bool Mine, int Plans, int Versions, int Selected, int Films, DateTime? LastSentUtc, IReadOnlyList<PlanChip> Chips, IReadOnlyList<string> Submitters);

public sealed record HomeView(RunningCard? Running, bool WorkerOn, int QueuedCount, IReadOnlyList<QueuedLine> Queued, bool Me, IReadOnlyList<AnalysisRow> InProgress, IReadOnlyList<AnalysisRow> Finished, int Unclassified)
{
    static string Hm(DateTime utc) => SeoulTime.FromUtc(utc).ToString("HH:mm");

    public static HomeView Build(IReadOnlyList<AnalysisInfo> analyses, IReadOnlyList<JobInfo> jobs, IReadOnlyList<FilmInfo> films, FilmInfo? filming,
        (int Ahead, DateTime? Start, TimeSpan? Typical) queue, bool workerOn, int parallel, string ip, bool? me, DateTime nowUtc)
    {
        var names = Mine.Names(jobs, ip);
        var hasMine = jobs.Any(j => Mine.Is(j, ip, names));
        var onlyMine = (me ?? true) && hasMine; // nothing mine (even with me=1) shows everything

        string Title(JobInfo j)
        {
            var a = analyses.FirstOrDefault(x => x.Id == j.AnalysisId);
            if (a == null) return $"{j.Project} / {j.Plan}";
            return $"{a.Name} › {a.Plans.FirstOrDefault(p => p.Id == j.PlanId)?.Name ?? j.Plan} v{j.Version}";
        }

        RunningCard? running = null;
        if (jobs.Where(j => j.State == JobState.Running).OrderBy(j => j.StartedUtc ?? j.CreatedUtc).FirstOrDefault() is { } r)
        {
            var eta = JobProgress.Eta(r, queue.Typical, parallel, nowUtc);
            running = new(r.Id, Title(r), r.State, r.Phase, JobProgress.Ratio(r), r.RunsDone, r.ExpectedRuns, r.RunsDone > 0 ? JobPages.Dur(JobProgress.AvgRun(r)) : null,
                eta is { } e ? Hm(e) : null, r.LastLine ?? "",
                filming is { Cuts.Count: > 0 } f ? $"촬영 {f.Done}/{f.Cuts.Count * f.Request.JobIds.Count}" : null);
        }

        var queuedJobs = jobs.Where(j => j.State == JobState.Queued).OrderBy(j => j.CreatedUtc).ToList();
        var queued = queuedJobs.Take(3).Select((j, i) => new QueuedLine(j.Id, Title(j),
            queue.Start is { } s && queue.Typical is { } t ? Hm(s + t * i) : null)).ToList();

        var rows = new List<AnalysisRow>();
        foreach (var a in analyses)
        {
            var mine = jobs.Where(j => j.AnalysisId == a.Id).ToList();
            var isMine = mine.Any(j => Mine.Is(j, ip, names));
            if (onlyMine && !isMine) continue;
            var chips = a.Plans.Select(p => mine.Where(j => j.PlanId == p.Id).OrderByDescending(j => j.Version).FirstOrDefault() is { } j
                ? new PlanChip(p.Id, p.Name, j.Id, j.Version, j.State, j.Decided?.Values.Sum(), JobProgress.Ratio(j), j.Selected) : null).OfType<PlanChip>().ToList();
            rows.Add(new(a.Id, a.Name, mine.Any(j => j.State is JobState.Queued or JobState.Running), isMine, a.Plans.Count, mine.Count, mine.Count(j => j.Selected),
                films.Count(f => f.Request.AnalysisId == a.Id), mine.Count == 0 ? null : mine.Max(j => j.CreatedUtc), chips,
                mine.Select(j => j.Submitter).Where(s => s != "").Distinct().ToList()));
        }
        rows = rows.OrderByDescending(x => x.LastSentUtc).ToList();

        return new(running, workerOn, queuedJobs.Count, queued, onlyMine, rows.Where(x => x.Active).ToList(), rows.Where(x => !x.Active).ToList(),
            jobs.Count(j => j.AnalysisId == null));
    }
}
