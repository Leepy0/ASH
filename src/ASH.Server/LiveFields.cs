using System.Globalization;
using ASH.Core;
using ASH.Core.Analysis;

namespace ASH.Server;

/// <summary>What the polling script needs: <c>Fields</c> are plain texts (set with textContent), "&lt;prefix&gt;.bar" a percent "0".."100", "&lt;prefix&gt;.state" a job state;
/// <c>Watch</c> maps job/film id to state so the page can tell when something finished.</summary>
public sealed record Live(bool Active, IReadOnlyDictionary<string, string> Fields, IReadOnlyDictionary<string, string> Watch);

public static class LiveFields
{
    static bool Open(string state) => state is JobState.Queued or JobState.Running;
    static string Pct(double? r) => r is { } v ? Math.Round(v * 100).ToString("0", CultureInfo.InvariantCulture) : "0";
    static string Hm(DateTime utc) => SeoulTime.FromUtc(utc).ToString("HH:mm");

    static Live Make(Dictionary<string, string> fields, Dictionary<string, string> watch) => new(watch.Values.Any(Open), fields, watch);

    static void JobKeys(Dictionary<string, string> f, string p, JobInfo j, TimeSpan? typical, int parallel)
    {
        var ratio = JobProgress.Ratio(j);
        f[p + ".state"] = j.State; f[p + ".statetext"] = JobState.Korean(j.State); f[p + ".phase"] = j.Phase ?? "";
        f[p + ".runs"] = $"{j.RunsDone}{(j.ExpectedRuns is { } e ? "/" + e : "")}회";
        f[p + ".progress"] = ratio is { } r ? Pct(r) + "%" : "-";
        f[p + ".eta"] = JobProgress.Eta(j, typical, parallel, DateTime.UtcNow) is { } t ? Hm(t) : "-";
        f[p + ".avg"] = j.RunsDone > 0 ? JobPages.Dur(JobProgress.AvgRun(j)) : "-";
        f[p + ".bar"] = Pct(ratio); f[p + ".last"] = j.LastLine ?? "";
    }

    public static Live OfJob(JobInfo j, TimeSpan? typical, int parallel)
    {
        var f = new Dictionary<string, string>(); JobKeys(f, "job", j, typical, parallel);
        return Make(f, new() { [j.Id] = j.State });
    }

    public static Live OfFilm(FilmInfo f)
    {
        var total = f.Items.Count;
        var k = new Dictionary<string, string>
        {
            ["film.state"] = f.State, ["film.statetext"] = JobState.Korean(f.State), ["film.phase"] = f.Phase ?? "",
            ["film.done"] = $"컷 {total}개 중 {f.Done}개 완료", ["film.bar"] = total > 0 ? Math.Round(100.0 * f.Done / total).ToString("0", CultureInfo.InvariantCulture) : "0",
        };
        return Make(k, new() { [f.Id] = f.State });
    }

    public static Live OfHome(HomeView v)
    {
        var f = new Dictionary<string, string> { ["queued.count"] = v.QueuedCount.ToString(CultureInfo.InvariantCulture) };
        var w = new Dictionary<string, string>();
        if (v.Running is { } r)
        {
            f["running.state"] = r.State; f["running.statetext"] = JobState.Korean(r.State); f["running.phase"] = r.Phase ?? "";
            f["running.runs"] = $"{r.RunsDone}{(r.ExpectedRuns is { } e ? "/" + e : "")}회";
            f["running.progress"] = r.Progress is { } p ? Pct(p) + "%" : "-"; f["running.bar"] = Pct(r.Progress);
            f["running.eta"] = r.Eta ?? "-"; f["running.avg"] = r.AvgRun ?? "-"; f["running.film"] = r.Film ?? "";
            w[r.JobId] = r.State;
        }
        foreach (var q in v.Queued) w[q.JobId] = JobState.Queued;
        foreach (var c in v.InProgress.Concat(v.Finished).SelectMany(a => a.Chips))
        {
            f[$"chip.{c.JobId}.text"] = $"{c.PlanName} v{c.Version}" + (c.State == JobState.Running ? " 진행률 " + Pct(c.Progress) + "%" : c.State == JobState.Queued ? " 대기" : c.Total is { } n ? $" {n}대" : "");
            f[$"chip.{c.JobId}.state"] = c.State; w[c.JobId] = c.State;
        }
        return Make(f, w);
    }

    /// <summary>Version lines "job.&lt;id&gt;.*", the running card "running.*" and the partial result "partial.&lt;group&gt;" of an analysis page.</summary>
    public static Live OfAnalysis(AnalysisInfo a, IReadOnlyList<JobInfo> jobs, IReadOnlyList<FilmInfo> films, TimeSpan? typical, int parallel)
    {
        var f = new Dictionary<string, string>(); var w = new Dictionary<string, string>();
        var mine = jobs.Where(j => j.AnalysisId == a.Id).ToList();
        foreach (var j in mine) { JobKeys(f, "job." + j.Id, j, typical, parallel); w[j.Id] = j.State; }
        if (mine.Where(j => j.State == JobState.Running).OrderBy(j => j.StartedUtc ?? j.CreatedUtc).FirstOrDefault() is { } r)
        {
            JobKeys(f, "running", r, typical, parallel);
            foreach (var kv in r.Partial ?? new()) f["partial." + kv.Key] = kv.Value.ToString(CultureInfo.InvariantCulture);
        }
        foreach (var fm in films.Where(x => x.Request.AnalysisId == a.Id)) w[fm.Id] = fm.State;
        return Make(f, w);
    }
}
