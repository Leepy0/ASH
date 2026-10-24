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

    /// <summary>The plan chip's text, shared by the home page and the poll.</summary>
    internal static string ChipText(PlanChip p) => $"{p.PlanName} v{p.Version}" +
        (p.State == JobState.Running ? " 진행률 " + (p.Progress is { } r ? Pct(r) + "%" : "-") : p.State == JobState.Queued ? " 대기" : p.State == JobState.Failed ? " 실패" : p.State == JobState.Cancelled ? " 취소" : p.Total is { } n ? $" {n}대" : "");

    static Live Make(Dictionary<string, string> fields, Dictionary<string, string> watch) => new(watch.Values.Any(Open), fields, watch);

    static void JobKeys(Dictionary<string, string> f, string p, JobInfo j, TimeSpan? typical, int parallel)
    {
        var ratio = JobProgress.Ratio(j);
        f[p + ".sep"] = j.Phase != null ? " · " : ""; f[p + ".state"] = j.State; f[p + ".statetext"] = JobState.Korean(j.State); f[p + ".phase"] = j.Phase ?? "";
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

    internal static string FilmDone(FilmInfo f) => $"컷 {f.Done} / {f.Items.Count}";
    internal static string FilmPct(FilmInfo f) => f.Items.Count == 0 ? "0" : Math.Round(100.0 * f.Done / f.Items.Count).ToString("0", CultureInfo.InvariantCulture);
    /// <summary>now + (cuts left) × (elapsed ÷ cuts done); "-" until one cut is done.</summary>
    internal static string FilmEta(FilmInfo f, DateTime? now = null) => f.StartedUtc is { } s && f.Done > 0 ? Hm((now ?? DateTime.UtcNow) + ((now ?? DateTime.UtcNow) - s) * (f.Items.Count - f.Done) / Math.Max(1, f.Done)) : "-";

    public static Live OfFilm(FilmInfo f)
    {
        var total = f.Items.Count;
        var k = new Dictionary<string, string>
        {
            ["film.state"] = f.State, ["film.statetext"] = JobState.Korean(f.State), ["film.phase"] = f.Phase ?? "",
            ["film.sep"] = f.Phase != null ? " · " : "", ["film.done"] = $"컷 {total}개 중 {f.Done}개 완료", ["film.bar"] = FilmPct(f),
        };
        return Make(k, new() { [f.Id] = f.State });
    }

    /// <summary>The home film card's line under the bar, shared by the page and the poll.</summary>
    internal static string FilmNote(FilmCard m) => m.State == JobState.Queued ? "대기 · 다른 작업 뒤 순서" : m.Eta == "-" ? "제작 중" : $"제작 중 · {m.Eta} 끝";

    public static Live OfHome(HomeView v)
    {
        var f = new Dictionary<string, string> { ["queued.count"] = v.QueuedCount.ToString(CultureInfo.InvariantCulture) };
        var w = new Dictionary<string, string>();
        if (v.Running is { } r)
        {
            f["running.state"] = r.State; f["running.statetext"] = JobState.Korean(r.State); f["running.phase"] = r.Phase ?? "";
            f["running.runs"] = $"{r.RunsDone}{(r.ExpectedRuns is { } e ? "/" + e : "")}회";
            f["running.progress"] = r.Progress is { } p ? Pct(p) + "%" : "-"; f["running.bar"] = Pct(r.Progress);
            f["running.eta"] = r.Eta ?? "-"; f["running.avg"] = r.AvgRun ?? "-"; f["running.film"] = r.Film ?? ""; f["running.note"] = r.Note;
            f["running.sep"] = r.Phase != null ? " · " : ""; f["running.left"] = r.Left ?? "";
            w[r.JobId] = r.State;
        }
        foreach (var q in v.Queued) w[q.JobId] = JobState.Queued;
        foreach (var m in v.Films)
        {
            var p = "film." + m.FilmId;
            f[p + ".done"] = $"{m.Done} / {m.Total}"; f[p + ".bar"] = m.Total == 0 ? "0" : Math.Round(100.0 * m.Done / m.Total).ToString("0", CultureInfo.InvariantCulture);
            f[p + ".note"] = FilmNote(m); w[m.FilmId] = m.State;
        }
        foreach (var c in v.InProgress.Concat(v.Finished).SelectMany(a => a.Chips))
        {
            f[$"chip.{c.JobId}.text"] = ChipText(c); w[c.JobId] = c.State;
        }
        return Make(f, w);
    }

    /// <summary>Version lines "job.&lt;id&gt;.*", the running card "running.*" and the partial result "partial.text" of an analysis page.</summary>
    public static Live OfAnalysis(AnalysisInfo a, IReadOnlyList<JobInfo> jobs, IReadOnlyList<FilmInfo> films, TimeSpan? typical, int parallel, JobStore store)
    {
        var f = new Dictionary<string, string>(); var w = new Dictionary<string, string>();
        var mine = jobs.Where(j => j.AnalysisId == a.Id).ToList();
        foreach (var j in mine) { JobKeys(f, "job." + j.Id, j, typical, parallel); w[j.Id] = j.State; }
        if (mine.Where(j => j.State == JobState.Running).OrderBy(j => j.StartedUtc ?? j.CreatedUtc).FirstOrDefault() is { } r)
        {
            JobKeys(f, "running", r, typical, parallel);
            f["partial.text"] = AnalysisPages.PartialText(store, r);
        }
        foreach (var fm in films.Where(x => x.Request.AnalysisId == a.Id))
        {
            w[fm.Id] = fm.State; var p = "film." + fm.Id;
            f[p + ".state"] = fm.State; f[p + ".statetext"] = JobState.Korean(fm.State); f[p + ".phase"] = fm.Phase ?? "";
            f[p + ".done"] = FilmDone(fm); f[p + ".bar"] = FilmPct(fm); f[p + ".eta"] = FilmEta(fm);
        }
        return Make(f, w);
    }
}
