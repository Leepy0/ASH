using System.Globalization;
using System.Text;
using static ASH.Server.JobPages;

namespace ASH.Server;

/// <summary>/films: every film, grouped 만드는 중 (progress, polled) and 완료 (thumbnail, cuts, length, date, 보기·받기), with the home page's 내 것만|전체 toggle.</summary>
public static class FilmPages
{
    /// <summary>A film is "mine" when its analysis has a job that is mine (this IP or a name this IP has used), as the home page decides for analyses.</summary>
    public static IReadOnlyList<FilmInfo> Filter(IReadOnlyList<FilmInfo> films, IReadOnlyList<JobInfo> jobs, string ip, bool? me, out bool onlyMine)
    {
        var names = Mine.Names(jobs, ip);
        var analyses = jobs.Where(j => j.AnalysisId != null && Mine.Is(j, ip, names)).Select(j => j.AnalysisId!).ToHashSet(StringComparer.Ordinal);
        var mine = films.Where(f => analyses.Contains(f.Request.AnalysisId)).ToList();
        onlyMine = (me ?? true) && mine.Count > 0; // nothing mine (even with me=1) shows everything
        return onlyMine ? mine : films;
    }

    /// <summary>Length of what was asked for: every cut once per version (the joined film's length).</summary>
    internal static double Seconds(FilmInfo f) => f.Items.Sum(i => f.Cuts.FirstOrDefault(c => c.Id == i.CutId)?.VideoSeconds ?? 0);

    static string Len(double s)
    {
        var t = TimeSpan.FromSeconds(Math.Round(s));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }

    /// <summary>Fields "film.&lt;id&gt;.*" of the films still being made, for the poll.</summary>
    public static Live LiveOf(IReadOnlyList<FilmInfo> films)
    {
        var f = new Dictionary<string, string>(); var w = new Dictionary<string, string>();
        foreach (var fm in films.Where(x => !x.Finished))
        {
            w[fm.Id] = fm.State; var p = "film." + fm.Id;
            f[p + ".state"] = fm.State; f[p + ".statetext"] = JobState.Korean(fm.State); f[p + ".phase"] = fm.Phase ?? "";
            f[p + ".done"] = LiveFields.FilmDone(fm); f[p + ".bar"] = LiveFields.FilmPct(fm); f[p + ".eta"] = LiveFields.FilmEta(fm);
        }
        return new(w.Count > 0, f, w);
    }

    /// <param name="exists">Whether a file of the film is on disk (film id, file name).</param>
    public static string List(IReadOnlyList<FilmInfo> films, bool onlyMine, Func<string, string, bool> exists, string? script)
    {
        var sb = new StringBuilder(PollSlots);
        sb.Append("<div class=\"head\"><h1>영상</h1><span class=\"sp\"></span><nav class=\"segc\" aria-label=\"보기 범위\">" +
            $"<a href=\"/films?me=1\"{(onlyMine ? " class=\"on\" aria-current=\"true\"" : "")}>내 것만</a><a href=\"/films?me=0\"{(onlyMine ? "" : " class=\"on\" aria-current=\"true\"")}>전체</a></nav></div>");
        sb.Append("""
<style>
.fl-h{display:flex;align-items:center;gap:8px;margin:18px 0 10px}.fl-h h2{margin:0}
.fl-run{display:grid;gap:10px}.fl-run .row{flex-wrap:wrap}.fl-run .prog{flex-basis:100%}
.fl-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(260px,1fr));gap:14px}
.fl-card{padding:12px;gap:4px}.fl-card img,.fl-card .thumb-none{width:100%;height:auto;aspect-ratio:16/9;border-radius:10px;object-fit:cover;background:var(--gray-b);margin-bottom:6px}
.fl-card .t2{font-size:17px;overflow-wrap:anywhere}.fl-card .acts{margin-top:8px}
</style>
""");
        var running = films.Where(f => !f.Finished).OrderBy(f => f.CreatedUtc).ToList();
        var done = films.Where(f => f.Finished).ToList();

        sb.Append($"<div class=\"fl-h\"><h2>만드는 중</h2><span class=\"tag blue\">▶ {running.Count}</span></div>");
        if (running.Count == 0) sb.Append("<div class=\"empty\">지금 만드는 영상이 없습니다.</div>");
        else
        {
            sb.Append("<div class=\"fl-run\">");
            foreach (var f in running)
            {
                var k = "film." + H(f.Id);
                sb.Append($"<a class=\"row\" href=\"/films/{H(f.Id)}\"><div class=\"t\"><div>{H(f.AnalysisName)}</div><small>{H(When(f.CreatedUtc))} · 컷 {f.Cuts.Count}개 · {Len(Seconds(f))}</small></div>" +
                    $"<span class=\"state s-{H(f.State)}\" data-state=\"{k}.state\" data-field=\"{k}.statetext\">{H(JobState.Korean(f.State))}</span>" +
                    $"<span class=\"t4\"><span data-field=\"{k}.phase\">{H(f.Phase)}</span> · <span data-field=\"{k}.done\">{LiveFields.FilmDone(f)}</span> · 완료 예상 <span data-field=\"{k}.eta\">{H(LiveFields.FilmEta(f))}</span></span>" +
                    $"<div class=\"prog\"><i data-bar=\"{k}.bar\" style=\"width:{LiveFields.FilmPct(f)}%\"></i></div></a>");
            }
            sb.Append("</div>");
        }

        sb.Append($"<div class=\"fl-h\"><h2>완료</h2><span class=\"tag green\">✓ {done.Count}</span></div>");
        if (done.Count == 0) sb.Append("<div class=\"empty\">아직 다 만든 영상이 없습니다. <a href=\"/analyses\">분석</a>에서 영상 만들기로 컷을 고르고 만드세요.</div>");
        else
        {
            sb.Append("<div class=\"fl-grid\">");
            foreach (var f in done)
            {
                var fid = H(f.Id);
                bool Has(string n) => exists(f.Id, n);
                // 받기: the joined film, else every cut in one zip (served only when a cut file is still there)
                var get = f.JoinedOk && f.Joined != null && Has(f.Joined) ? AnalysisPages.FileUrl(f, f.Joined)
                    : f.Items.Any(i => i.Ok && Has(i.Name)) ? $"/films/{fid}/files/all.zip" : null;
                sb.Append("<section class=\"card fl-card\">");
                sb.Append(Has("thumb.jpg") ? $"<img src=\"/films/{fid}/files/thumb.jpg\" alt=\"\" loading=\"lazy\">" : "<div class=\"thumb-none\"></div>");
                sb.Append($"<div class=\"t2\">{H(f.AnalysisName)}</div>");
                if (f.State != JobState.Done) sb.Append($"<span class=\"tag {(f.State == JobState.Failed ? "red\">⚠" : "gray\">")} {H(JobState.Korean(f.State))}</span>");
                sb.Append($"<div class=\"t3\">컷 {f.Cuts.Count}개{(f.Request.JobIds.Count > 1 ? $" × 버전 {f.Request.JobIds.Count}" : "")} · {Len(Seconds(f))}</div><div class=\"t4\">{H(When(f.CreatedUtc))}</div>");
                sb.Append($"<div class=\"acts\"><a class=\"btn\" href=\"/films/{fid}\">보기</a>{(get != null ? $"<a class=\"btn pri\" href=\"{get}\" download>받기</a>" : "")}</div></section>");
            }
            sb.Append("</div>");
        }
        return Page("ASH 영상", sb.ToString(), script, nav: "films");
    }
}
