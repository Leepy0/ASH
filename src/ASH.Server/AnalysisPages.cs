using System.Globalization;
using System.Text;
using ASH.Core;
using ASH.Core.Analysis;
using ASH.Core.Geometry;
using ASH.Core.Runner;
using static ASH.Server.JobPages;

namespace ASH.Server;

/// <summary>The analysis pages: the list (/analyses), one analysis with its plans' versions and the compare table of the selected ones
/// (/analyses/{id}), the jobs not filed anywhere (/analyses/unclassified), and the "where is this job filed" header of a job page.</summary>
public sealed record AnalysisPageData(AnalysisInfo A, IReadOnlyList<JobInfo> Jobs, IReadOnlyList<FilmInfo> Films, JobStore Store, (int Ahead, DateTime? Start, TimeSpan? Typical) Queue, int Parallel, int CutCount, FilmStore? Fs = null, IReadOnlyDictionary<string, PriceRow>? Prices = null);

/// <summary>The left list of the analysis pages: rows (in progress first), whether only mine are shown, the search text, the tag selects,
/// the list's own query (me, q, tags) that row links keep, the unclassified count and every film (for the "영상 만드는 중" stage).</summary>
/// <summary>Running = what the home page's 진행 lane shows (a job, also one not filed in an analysis, or a film), pinned above the list.</summary>
public sealed record AnalysisListData(IReadOnlyList<AnalysisRow> Rows, bool Me, string Q, string TagFilter, string Keep, int Unclassified, IReadOnlyList<FilmInfo> Films,
    RunningCard? Running = null, int Queued = 0);

public static class AnalysisPages
{
    /// <summary>Result numbers of a finished job, taken from its JobView (the same ones the 결론 tab shows). Util = the busiest group, Stop = all materials.</summary>
    internal sealed record Metrics(int? Total, double Util, double Stop, JobView View, InvestmentView? Investment = null);

    internal static Metrics? MetricsOf(JobStore js, JobInfo j, IReadOnlyDictionary<string, PriceRow>? prices = null)
    {
        if (j.State != JobState.Done || j.Decided == null) return null;
        var v = JobView.For(js, j);
        if (v.Input == null) return null;
        var utils = v.Groups.Select(g => g.Util).Where(u => !double.IsNaN(u)).ToList();
        var stops = v.Materials.Select(m => m.StopSec).Where(s => !double.IsNaN(s)).ToList();
        return new Metrics(j.Decided.Values.Sum(), utils.Count == 0 ? double.NaN : utils.Max(), stops.Count == 0 ? double.NaN : stops.Sum(), v, prices == null ? null : InvestmentOf(v.Input, j.Decided, prices));
    }

    static string Num(double d, string fmt = "0.#") => F(d, fmt);
    static string Pc0(double u) => double.IsNaN(u) ? "-" : F(u * 100, "0") + "%";
    static string Md(DateTime? utc) => utc is { } t ? SeoulTime.FromUtc(t).ToString("MM/dd", CultureInfo.InvariantCulture) : "";

    static bool Ok(JobInfo j) => j.State == JobState.Done && j.Decided != null;
    static DateTime At(JobInfo j) => j.FinishedUtc ?? j.CreatedUtc;
    /// <summary>The version a plan (or the analysis) stands for: the latest selected finished one, else the latest finished one.</summary>
    internal static JobInfo? Pick(IEnumerable<JobInfo> js) { var l = js.Where(Ok).ToList(); return l.Where(j => j.Selected).OrderByDescending(At).FirstOrDefault() ?? l.OrderByDescending(At).FirstOrDefault(); }
    internal static string PlanName(AnalysisInfo a, JobInfo j) => a.Plans.FirstOrDefault(p => p.Id == j.PlanId)?.Name ?? j.Plan;

    /// <summary>One option per "analysis › plan", value "analysisId|planId" (the move endpoint splits it).</summary>
    static string Options(IReadOnlyList<AnalysisInfo> analyses, string? planId = null)
    {
        var sb = new StringBuilder();
        foreach (var a in analyses) foreach (var p in a.Plans)
            sb.Append($"<option value=\"{H(a.Id)}|{H(p.Id)}\"{(p.Id == planId ? " selected" : "")}>{H(a.Name)} › {H(p.Name)}</option>");
        return sb.ToString();
    }

    static string MoveForm(JobInfo j, IReadOnlyList<AnalysisInfo> analyses, string back, string label)
    {
        var opts = Options(analyses, j.PlanId);
        if (opts.Length == 0) return "<span class=\"muted\">옮길 안이 없습니다. <a href=\"/analyses\">분석 목록</a>에서 분석과 안을 먼저 만드세요.</span>";
        return $"<form method=\"post\" action=\"/jobs/{H(j.Id)}/move\" class=\"mini\"><input type=\"hidden\" name=\"back\" value=\"{H(back)}\"><select name=\"target\" aria-label=\"옮길 안\">{opts}</select><button>{label}</button></form>";
    }

    /// <summary>Top of a job page: "분석 › 안 › v2 · 메모" with a form to move it to another plan, or the way to file it when it has none.</summary>
    internal static void JobHeader(StringBuilder sb, JobInfo j, IReadOnlyList<AnalysisInfo> analyses)
    {
        var a = j.AnalysisId == null ? null : analyses.FirstOrDefault(x => x.Id == j.AnalysisId);
        var p = a?.Plans.FirstOrDefault(x => x.Id == j.PlanId);
        sb.Append("<div class=\"box\" style=\"margin:0 0 14px\">");
        if (a == null || p == null)
            sb.Append("<div class=\"crumb\">분석에 넣지 않은 작업입니다.</div>").Append(MoveForm(j, analyses, "/jobs/" + j.Id, "이 안으로 옮기기"));
        else
        {
            sb.Append($"<div class=\"crumb\"><a href=\"/analyses/{H(a.Id)}?tab=status\">{H(a.Name)}</a> › {H(p.Name)} › v{j.Version}{(j.VersionNote.Length > 0 ? " · " + H(j.VersionNote) : "")}{(j.Selected ? " <span class=\"tag\">선정</span>" : "")}</div>");
            sb.Append("<div class=\"mini\">").Append(MoveForm(j, analyses, "/jobs/" + j.Id, "옮기기")).Append(SelectForm(j, "/jobs/" + j.Id));
            if (Ok(j)) sb.Append($"<a class=\"btn\" href=\"/analyses/{H(a.Id)}/cuts?job={H(j.Id)}\">◐ 이 버전으로 영상 만들기</a>");
            sb.Append("</div>");
            sb.Append("<div class=\"help\">다른 안으로 옮기면 그 안의 다음 버전이 됩니다.</div>");
        }
        sb.Append("</div>");
    }

    static string SelectForm(JobInfo j, string back) =>
        $"<form method=\"post\" action=\"/jobs/{H(j.Id)}/select\" class=\"mini\"><input type=\"hidden\" name=\"on\" value=\"{(j.Selected ? 0 : 1)}\"><input type=\"hidden\" name=\"back\" value=\"{H(back)}\">" +
        $"<button>{(j.Selected ? "선정 해제" : "선정")}</button></form>";

    public static string Unclassified(IReadOnlyList<JobInfo> jobs, IReadOnlyList<AnalysisInfo> analyses)
    {
        var sb = new StringBuilder("<p class=\"crumb\"><a href=\"/analyses\">분석</a> › 분석에 넣지 않은 작업</p><div class=\"head\"><h1>분석에 넣지 않은 작업</h1>" +
            $"<span class=\"tag\">{jobs.Count(j => j.AnalysisId == null)}건</span></div>");
        sb.Append("<p class=\"t3\">안을 골라 옮기면 그 안의 다음 버전이 됩니다.</p>");
        sb.Append("<div class=\"box scroll\"><table><tr><th>작업</th><th>상태</th><th>프로젝트 / 안</th><th>보낸 사람</th><th>보낸 시각</th><th>권장(대)</th><th>옮기기</th></tr>");
        foreach (var j in jobs.Where(j => j.AnalysisId == null))
        {
            var total = j.Decided?.Values.Sum();
            sb.Append($"<tr><td><a href=\"/jobs/{H(j.Id)}\">{H(j.Id)}</a></td><td>{State(j)}</td><td>{H(j.Project)} / {H(j.Plan)}</td><td>{H(j.Submitter)}</td><td>{H(When(j.CreatedUtc))}</td>" +
                $"<td class=\"num\">{(total is { } n ? n.ToString(CultureInfo.InvariantCulture) : "-")}</td><td>{MoveForm(j, analyses, "/analyses/unclassified", "옮기기")}</td></tr>");
        }
        if (!jobs.Any(j => j.AnalysisId == null))
            sb.Append("<tr><td colspan=\"7\" class=\"muted\">없습니다. ZWCAD에서 보낼 때 분석과 안을 고르면 처음부터 분석에 들어갑니다.</td></tr>");
        sb.Append("</table></div>");
        return Page("ASH 분석에 넣지 않은 작업", sb.ToString(), nav: "analyses");
    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<(string Name, int Max)>> GroupCache = new();

    /// <summary>With more than <see cref="ManyGroups"/> groups: "확정 12 / 20 그룹 · 탐색 중: G13~G16" (the cluster being searched, else the next unconfirmed groups).
    /// The page and the polled "partial.text" key both use this, so they stay identical.</summary>
    internal static string PartialText(JobStore store, JobInfo j)
    {
        var groups = InputGroups(store, j);
        if (groups == null || groups.Count <= ManyGroups) return PartialList(store, j);
        var done = j.Partial ?? new();
        var trying = Trying(j);
        var names = groups.Select(g => g.Name).ToList();
        var next = trying.Where(names.Contains).ToList(); // a phase like "탐색 2패스" names no group
        var cluster = next.Count > 0;
        if (!cluster) next = names.Where(n => !done.ContainsKey(n)).ToList();
        string Show(List<string> l)
        {
            int a = names.IndexOf(l[0]), b = names.IndexOf(l[^1]);
            if (cluster && l.Count >= 3 && b - a + 1 == l.Count && l.SequenceEqual(names.GetRange(a, l.Count))) return $"{l[0]}~{l[^1]}"; // a cluster of neighbours
            return string.Join(", ", l.Take(4)) + (l.Count > 4 ? $" 외 {l.Count - 4}개" : "");
        }
        return $"확정 {names.Count(done.ContainsKey)} / {names.Count} 그룹" + (next.Count > 0 ? $" · 탐색 중: {Show(next)}" : "");
    }

    /// <summary>The per-group list for the page's closed details when <see cref="PartialText"/> is the short form; null otherwise.</summary>
    internal static string? PartialDetail(JobStore store, JobInfo j) => InputGroups(store, j) is { Count: > ManyGroups } ? PartialList(store, j) : null;

    static string[] Trying(JobInfo j) => j.Phase is { } ph && ph.StartsWith("탐색 ", StringComparison.Ordinal) ? ph[3..].Split(',') : Array.Empty<string>();

    static List<(string Name, int Max)>? InputGroups(JobStore store, JobInfo j)
    {
        // the input of a job never changes, so polling must not re-parse the workbook; only a successful read is cached
        if (!GroupCache.TryGetValue(j.Id, out var groups))
        {
            try { groups = SearchJob.ReadInput(store.ProjectDir(j.Id), TextWriter.Null, new List<string>(), out _)?.Groups.Select(g => (g.Group, g.MaxCount)).ToList(); }
            catch (Exception e) { Console.Error.WriteLine($"PartialText {j.Id}: {e.Message}"); groups = new(); } // cached too: a bad workbook logs once
            if (groups != null) GroupCache[j.Id] = groups;
        }
        return groups;
    }

    /// <summary>"G01 4 · G02 3 · G03 5 시험 중 · G04 탐색 중": confirmed groups with their count, the ones in the cluster being searched, the rest still waiting.
    /// Group order and Max come from the job's input; without it only the confirmed groups are listed.</summary>
    internal static string PartialList(JobStore store, JobInfo j)
    {
        var done = j.Partial ?? new();
        var groups = InputGroups(store, j);
        if (groups == null || groups.Count == 0) return string.Join(" · ", done.Select(kv => $"{kv.Key} {kv.Value}"));
        var trying = Trying(j);
        return string.Join(" · ", groups.Select(g => done.TryGetValue(g.Name, out var n) ? $"{g.Name} {n}" : trying.Contains(g.Name) ? $"{g.Name} {g.Max} 시험 중" : $"{g.Name} 탐색 중"));
    }

    // ---------------------------------------------------------------- master-detail shell

    // page-only CSS: the 270px list, the detail pane (scrolls by itself) and the 결론 grid that takes the remaining height
    internal const string Css = @"<style>
.ana{flex:1;min-height:0;display:grid;grid-template-columns:270px minmax(0,1fr);grid-template-rows:minmax(0,1fr);margin:-18px -26px -22px}
.lp{background:#fff;border-right:1px solid var(--line);display:flex;flex-direction:column;min-height:0}
.lp-h{display:flex;align-items:center;gap:8px;padding:14px 14px 8px}.lp-h b{font-size:16px;flex:1}
.lp-new{position:relative}.lp-new summary{list-style:none;margin:0;width:34px;padding:0;font-size:20px}.lp-new summary::-webkit-details-marker{display:none}
.lp-new form{position:absolute;right:0;top:40px;z-index:20;width:260px;background:#fff;border:1px solid var(--line2);border-radius:12px;padding:12px;box-shadow:0 6px 24px rgba(0,0,0,.14);display:flex;flex-direction:column;gap:8px}
.lp-f{padding:0 14px 10px}.lp-f input[type=search]{max-width:none}.lp-f details summary{font-size:13px;margin-top:6px}.lp-f label{display:block;font-size:13px;color:var(--sub2);margin-top:6px}.lp-f select{max-width:none}.lp-f button{margin-top:8px}
.lp-l{flex:1;min-height:0;overflow:auto;border-top:1px solid var(--line)}
.lr{display:flex;align-items:center;gap:10px;min-height:56px;padding:8px 14px;border-bottom:1px solid var(--line);color:var(--ink);text-decoration:none}
.lr:hover{background:var(--gray-b)}.lr.on{background:var(--blue);color:#fff}.lr.on .st{color:#fff}.lr.on .dot{background:#fff}
.lr .dot{width:8px;height:8px;border-radius:50%;background:var(--c);flex:none}.lr .t{flex:1;min-width:0}
.lr .t b{display:block;font-size:14px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.lr .st{font-size:13px;color:var(--sub2)}.lr em{font-style:normal;font-weight:700;font-size:15px}
.lr-now{background:var(--blue-b);border-top:1px solid var(--line)}.lr-now .st:first-child{font-size:12px;font-weight:600;color:var(--blue-d)}
.lp-x{display:block;padding:12px 14px;border-top:1px solid var(--line);font-size:13px;color:var(--sub2);text-decoration:none}
.dp{min-height:0;overflow:auto;padding:16px 24px 20px;display:flex;flex-direction:column}.dp>*{flex:none}.dp>.fill{flex:1}
.hint{margin:auto;text-align:center;color:var(--sub2)}
.dh{display:flex;align-items:center;gap:8px;flex-wrap:wrap}.dh h1{font-size:22px;margin:0 6px 0 0}.dh .sp{flex:1}
.tb{display:flex;justify-content:center;align-items:center;gap:10px;flex-wrap:wrap;margin:14px 0 12px}.tb form{flex-wrap:nowrap}.tb select{flex:none;width:auto;min-height:32px}
.kpi .t4{font-weight:600;margin-top:4px}.kpi .t1{margin-top:4px}
.cg{flex:1;min-height:520px;display:grid;grid-template-columns:minmax(360px,560px) minmax(0,1fr);gap:14px;margin-top:14px}
.cg .col{display:flex;flex-direction:column;gap:14px;min-height:0}.cg .box{margin-top:0;display:flex;flex-direction:column;min-height:0}.cg .gbox{flex:none;min-height:auto}
.gl{flex:none;overflow:visible}
.gr{display:grid;grid-template-columns:92px minmax(60px,1fr) 52px 66px;align-items:center;gap:10px;min-height:36px;padding:0 8px;border-bottom:1px solid var(--line);font-size:14px}
.gr.g-fail{--c:var(--red);background:var(--red-b);color:var(--red);border-radius:6px}.gr.g-over{--c:var(--orange);background:var(--orange-b);color:var(--orange);border-radius:6px}.wn{color:var(--orange)}.gr.g-under{--c:var(--gray);color:var(--sub2)}.gr.g-ok{--c:var(--blue)}
.gr .gn{white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.gr .gb{position:relative;height:6px;border-radius:3px;background:var(--fill)}
.gr .gb i{display:block;height:100%;border-radius:3px;background:var(--c)}.gr .gb b{position:absolute;top:-5px;bottom:-5px;width:2px;background:var(--ink);opacity:.55}
.gr .gc{text-align:right;font-weight:700;font-size:17px}.gr .gc span,.pr .v b span{font-size:12px;font-weight:600}.gr .gu{text-align:right;font-size:13px;color:var(--c)}.gr.g-ok .gu{color:var(--sub2)}
.lg{display:flex;gap:14px;justify-content:center;font-size:13px;color:var(--sub2);padding-top:10px}.lg i{display:inline-block;width:10px;height:10px;border-radius:2px;background:var(--c);margin-right:4px}
.pl{display:flex;flex-direction:column;gap:8px}
.pr{display:grid;grid-template-columns:minmax(0,1fr) repeat(4,64px);align-items:center;gap:6px;border:1px solid var(--line);border-radius:12px;padding:8px 14px;color:var(--ink);text-decoration:none}
.pr.on{border:2px solid var(--blue);background:var(--blue-b)}.pr .pn b{display:block}.pr .pn span{font-size:13px;color:var(--sub2)}
.pr .v{text-align:center}.pr .v b{display:block;font-size:16px}.pr .v>span{font-size:12px;color:var(--sub2)}
.mapb{min-width:0}.mw{flex:1;min-height:0;display:flex;gap:14px}
.mv{flex:1;min-width:0;min-height:260px;position:relative;background:var(--gray-b);border-radius:10px;overflow:hidden}
.mv svg.bm{position:absolute;inset:0;width:100%;height:100%}
.mv svg.mm{position:absolute;top:10px;right:10px;width:150px;height:96px;background:#fff;border:1px solid var(--line2);border-radius:8px}
.mv .segc{position:absolute;left:10px;bottom:10px}
.mv .zb{position:absolute;right:10px;bottom:10px;display:flex;gap:4px}.mv .zb button{min-height:32px;min-width:36px;padding:0 10px;background:#fff;border:1px solid var(--line2)}
.rk{width:220px;flex:none;display:flex;flex-direction:column}.rk ol{list-style:none;margin:0;padding:0}
.rk li{display:flex;align-items:center;gap:8px;min-height:36px;border-bottom:1px solid var(--line)}.rk li span{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.rk li i{width:22px;height:22px;border-radius:50%;display:grid;place-items:center;font-style:normal;font-size:12px;font-weight:700;background:var(--gray-b);color:var(--sub2);flex:none}
.rk li:first-child i{background:var(--red-b);color:var(--red)}.rk li b{font-variant-numeric:tabular-nums}.rk li .btn{min-height:28px;padding:0 8px;font-size:13px;flex:none}
.rk .end{margin-top:auto;text-decoration:none;text-align:center;padding-top:10px;border-top:1px solid var(--line);font-weight:600}
.vr{display:flex;align-items:center;gap:10px;flex-wrap:wrap;min-height:44px;border-top:1px solid var(--line);padding:6px 0}.vr:first-of-type{border-top:0}.vr .vn{text-decoration:none;font-weight:700;font-size:16px;min-width:34px}.vr .sp{flex:1}
.fgrid{display:grid;grid-template-columns:repeat(auto-fill,minmax(340px,1fr));gap:14px}.fgrid .box{margin-top:0}.fgrid video,.fgrid img{width:100%;border-radius:10px;background:#000;display:block}
.cl{margin:8px 0 0;padding-left:18px}
@media(max-width:1500px){.cg{grid-template-columns:minmax(340px,460px) minmax(0,1fr)}.rk{width:180px}}
</style>";

    /// <summary>Colour (a .c-* class) and one-line stage of an analysis row: running with its progress, queued, a film being made,
    /// report ready (a finished version and a made film), a new conclusion (a finished version), failed, or nothing yet.</summary>
    internal static (string Color, string Text) Stage(AnalysisRow r, IEnumerable<FilmInfo> films)
    {
        if (r.Chips.FirstOrDefault(c => c.State == JobState.Running) is { } run) return ("blue", $"실행 중 {Math.Round((run.Progress ?? 0) * 100):0}%");
        if (r.Chips.Any(c => c.State == JobState.Queued)) return ("gray", "대기");
        var mine = films.Where(f => f.Request.AnalysisId == r.Id).ToList();
        if (mine.Any(f => !f.Finished)) return ("orange", "영상 만드는 중");
        var done = r.Chips.Any(c => c.State == JobState.Done && c.Total != null);
        if (done && mine.Any(f => f.State == JobState.Done)) return ("green", "보고서 준비");
        if (done) return ("indigo", "새 결론");
        if (r.Chips.Any(c => c.State == JobState.Failed)) return ("red", "⚠ 실패");
        return ("gray", r.Versions == 0 ? "버전 없음" : "결과 없음");
    }

    static void ListPane(StringBuilder sb, AnalysisListData l, string? currentId)
    {
        var path = currentId == null ? "/analyses" : "/analyses/" + currentId;
        var others = string.Join("&", l.Keep.Split('&', StringSplitOptions.RemoveEmptyEntries).Where(x => !x.StartsWith("me=", StringComparison.Ordinal)));
        string MeHref(int me) => H($"{path}?me={me}{(others.Length > 0 ? "&" + others : "")}");
        sb.Append("<aside class=\"lp\" aria-label=\"분석 목록\"><div class=\"lp-h\"><b>분석</b>");
        sb.Append($"<span class=\"segc\"><a href=\"{MeHref(1)}\"{(l.Me ? " class=\"on\" aria-current=\"true\"" : "")}>내 것</a><a href=\"{MeHref(0)}\"{(l.Me ? "" : " class=\"on\" aria-current=\"true\"")}>전체</a></span>");
        sb.Append("<details class=\"lp-new\"><summary class=\"btn\" aria-label=\"새 분석\" title=\"새 분석\">+</summary><form method=\"post\" action=\"/analyses\">" +
            "<input type=\"text\" name=\"name\" required maxlength=\"100\" placeholder=\"분석 이름\" aria-label=\"분석 이름\">" +
            "<input type=\"text\" name=\"note\" maxlength=\"200\" placeholder=\"메모(선택)\" aria-label=\"메모\"><button class=\"primary\">만들기</button></form></details></div>");
        sb.Append($"<form class=\"lp-f\" action=\"{H(path)}\"><input type=\"hidden\" name=\"me\" value=\"{(l.Me ? 1 : 0)}\">" +
            $"<input type=\"search\" name=\"q\" value=\"{H(l.Q)}\" placeholder=\"이름·보낸 사람\" aria-label=\"검색\">" +
            $"<details><summary>법인·제품군·라인</summary>{l.TagFilter}<button>검색</button></details></form>");
        if (l.Running is { } run)
            sb.Append($"<a class=\"lr c-blue lr-now\" href=\"{H(run.Href ?? "/jobs/" + run.JobId)}\"><i class=\"dot\" aria-hidden=\"true\"></i><span class=\"t\"><span class=\"st\">지금 실행 중{(l.Queued > 0 ? $" · 대기 {l.Queued}건" : "")}</span>" +
                $"<b>{H(run.Name ?? run.Title)}</b><span class=\"st\">{H(run.Sub ?? "")} · {Math.Round((run.Progress ?? 0) * 100):0}%</span></span></a>");
        var rows = l.Q.Length == 0 ? l.Rows : l.Rows.Where(r => r.Name.Contains(l.Q, StringComparison.OrdinalIgnoreCase) || r.Submitters.Any(s => s.Contains(l.Q, StringComparison.OrdinalIgnoreCase))).ToList();
        sb.Append("<nav class=\"lp-l\">");
        if (rows.Count == 0) sb.Append("<div class=\"empty\" style=\"margin:14px\">분석 없음</div>");
        foreach (var r in rows)
        {
            var (c, text) = Stage(r, l.Films);
            var on = r.Id == currentId;
            // the open analysis' running percent follows the poll
            var st = on && c == "blue" ? $"실행 중 <span data-field=\"running.progress\">{H(text[5..])}</span>" : H(text);
            var when = Md(r.LastSentUtc);
            var count = r.Chips.Where(x => x.Total != null).OrderByDescending(x => x.Selected).Select(x => x.Total).FirstOrDefault();
            sb.Append($"<a class=\"lr c-{c}{(on ? " on" : "")}\" href=\"/analyses/{H(r.Id)}{(l.Keep.Length > 0 ? "?" + H(l.Keep) : "")}\"{(on ? " aria-current=\"page\"" : "")}>" +
                $"<i class=\"dot\" aria-hidden=\"true\"></i><span class=\"t\"><b>{H(r.Name)}</b><span class=\"st\">{st}{(when.Length > 0 && c != "blue" ? " · " + when : "")}</span></span>" +
                (count is { } n ? $"<em>{n}대</em>" : "") + "</a>");
        }
        sb.Append("</nav>");
        sb.Append($"<a class=\"lp-x\" href=\"/analyses/unclassified\">분석에 넣지 않은 작업 {l.Unclassified}건 ›</a></aside>");
    }

    static string Shell(AnalysisListData? l, string? currentId, string detail)
    {
        var sb = new StringBuilder(Css);
        if (l == null) return sb.Append("<div class=\"ana\" style=\"grid-template-columns:minmax(0,1fr)\"><div class=\"dp\">").Append(detail).Append("</div></div>").ToString();
        sb.Append("<div class=\"ana\">");
        ListPane(sb, l, currentId);
        return sb.Append("<div class=\"dp\">").Append(detail).Append("</div></div>").ToString();
    }

    /// <summary>/analyses: the list and an empty detail.</summary>
    public static string Index(AnalysisListData l) =>
        Page("ASH 분석", Shell(l, null, "<div class=\"hint\"><div class=\"t2\">분석을 고르세요</div><div class=\"t4\">새 분석은 목록 위 + 버튼</div></div>"), nav: "analyses", fill: true);

    // ---------------------------------------------------------------- detail

    static readonly (string Key, string Name)[] Tabs = { ("conclusion", "결론"), ("compare", "비교"), ("cause", "원인"), ("films", "영상"), ("status", "버전") };

    /// <summary>The tab to show: a known ?tab=, else 결론 when a version has a result, else 버전(status).</summary>
    public static string TabOf(AnalysisPageData d, string? tab) => Tabs.Any(t => t.Key == tab) ? tab! : d.Jobs.Any(Ok) ? "conclusion" : "status";

    public static string Detail(AnalysisPageData d, string? tab, IQueryCollection q, string? pollScript, AnalysisListData? list = null)
    {
        var a = d.A; var id = H(a.Id);
        tab = TabOf(d, tab);
        var cur = Pick(d.Jobs);
        var sb = new StringBuilder(PollSlots);
        sb.Append($"<div class=\"dh\"><h1 title=\"{H(a.Note)}\">{H(a.Name)}</h1>");
        foreach (var t in new[] { a.Corp, a.ProductLine, a.Line }.Where(t => t.Length > 0)) sb.Append($"<span class=\"tag\">{H(t)}</span>");
        sb.Append("<span class=\"sp\"></span>");
        // one blue button: the next step (영상 once there is a result, then the report once a film is made)
        var report = $"/analyses/{id}/report"; // the report page offers PDF, PowerPoint and Excel
        if (cur == null) sb.Append($"<a class=\"btn\" href=\"/analyses/{id}/cuts\">컷 편집</a>");
        else if (d.Films.Any(f => f.State == JobState.Done))
            sb.Append($"<a class=\"btn\" href=\"/analyses/{id}?tab=films\">◐ 영상 보기</a><a class=\"btn\" href=\"/analyses/{id}/cuts?job={H(cur.Id)}\">영상 만들기</a><a class=\"btn pri\" href=\"{report}\">✓ 보고서 받기</a>");
        else sb.Append($"<a class=\"btn\" href=\"{report}\">✓ 보고서 받기</a><a class=\"btn pri\" href=\"/analyses/{id}/cuts?job={H(cur.Id)}\">◐ 영상 만들기</a>");
        sb.Append("</div><div class=\"tb\"><nav class=\"segc\" aria-label=\"보기\">");
        foreach (var (k, name) in Tabs)
        {
            var n = k == "films" ? $" <em>{d.Films.Count}</em>" : k == "status" ? $" <em>{d.Jobs.Count}</em>" : "";
            sb.Append($"<a href=\"/analyses/{id}?tab={k}\"{(k == tab ? " class=\"on\" aria-current=\"page\"" : "")}>{name}{n}</a>");
        }
        sb.Append("</nav>");
        var done = d.Jobs.Where(Ok).OrderBy(j => a.Plans.FindIndex(p => p.Id == j.PlanId)).ThenBy(j => j.Version).ToList();
        var shown = done.FirstOrDefault(j => j.Id == q["job"].ToString()) ?? cur;
        if (tab == "conclusion" && shown != null && q["view"] != "plans")
        {
            sb.Append("<form class=\"mini\"><input type=\"hidden\" name=\"tab\" value=\"conclusion\">" + (q["metric"] == "occ" ? "<input type=\"hidden\" name=\"metric\" value=\"occ\">" : "") +
                "<select name=\"job\" aria-label=\"버전\" onchange=\"this.form.submit()\">");
            foreach (var j in done) sb.Append($"<option value=\"{H(j.Id)}\"{(j.Id == shown.Id ? " selected" : "")}>{H(PlanName(a, j))} v{j.Version}{(j.Selected ? " · 선정" : "")}</option>");
            sb.Append($"</select><noscript><button>보기</button></noscript><a class=\"btn\" href=\"/jobs/{H(shown.Id)}?tab=detail\">상세</a></form>");
        }
        sb.Append("</div>");
        switch (tab)
        {
            case "conclusion": sb.Append("<div class=\"fill\">"); ConclusionTab(sb, d, q); sb.Append("</div>"); break;
            case "compare": CompareTab(sb, d, q); break;
            case "cause": CausePages.Tab(sb, d, q); break;
            case "films": FilmsTab(sb, d); break;
            default: StatusTab(sb, d); break;
        }
        return Page("ASH 분석 " + a.Name, Shell(list, a.Id, sb.ToString()), pollScript, "analyses", true);
    }

    // ---------------------------------------------------------------- 결론

    /// <summary>Utilisation under this is "too much slack" (gray ▼).</summary>
    internal const double UnderUtil = 0.10;

    /// <summary>"fail" (red ⚠: judgement failed), "over" (orange ▲ warning: above UtilMax), "under" (gray ▼: below 10 %) or "ok" (blue).</summary>
    internal static string GroupClass(GroupView g, double utilMax) =>
        !g.AllOk ? "fail" : Over(g, utilMax) ? "over" : !double.IsNaN(g.Util) && g.Util < UnderUtil ? "under" : "ok";

    /// <summary>Groups with the problems first: failed, over (busiest first), then under (idlest first), then the rest in input order.</summary>
    internal static List<GroupView> ProblemsFirst(IEnumerable<GroupView> groups, double utilMax) =>
        groups.Select((g, i) => (g, i, c: GroupClass(g, utilMax)))
            .OrderBy(x => x.c == "fail" ? 0 : x.c == "over" ? 1 : x.c == "under" ? 2 : 3)
            .ThenBy(x => x.c is "fail" or "over" ? -x.g.Util : x.c == "under" ? x.g.Util : x.i)
            .Select(x => x.g).ToList();

    /// <summary>"1.4억", "3,500만", or the plain amount (with the currency when it is not KRW).</summary>
    internal static string Money(decimal d, string currency = "KRW")
    {
        if (currency is not ("" or "KRW")) return Won(d) + " " + currency;
        var x = Math.Abs(d);
        return x >= 100_000_000m ? F((double)(d / 100_000_000m), "0.##") + "억" : x >= 10_000m ? F((double)(d / 10_000m), "#,0") + "만" : Won(d);
    }

    internal static string Kpi(string label, string value, string unit, string color, string sub, string subColor = "") =>
        $"<div class=\"kpi\" style=\"--c:var(--{color})\"><div class=\"t3\">{label}</div><div class=\"t1\">{value}<small>{unit}</small></div><div class=\"t4\"{(subColor.Length > 0 ? $" style=\"color:var(--{subColor})\"" : "")}>{sub}</div></div>";

    /// <summary>결론 tab: four numbers, then 그룹별 권장 대수 (problems first) and 안별 결론 beside the bottleneck map, for one version
    /// (?job=, else the latest selected, else the latest finished). ?view=plans keeps the older per-plan listing.</summary>
    internal static void ConclusionTab(StringBuilder sb, AnalysisPageData d, IQueryCollection q)
    {
        var a = d.A; var metric = q["metric"] == "occ" ? "occ" : "wait";
        var done = d.Jobs.Where(Ok).ToList();
        if (done.Count == 0) { sb.Append("<div class=\"empty\">결과가 나온 버전이 없습니다.</div>"); return; }
        if (q["view"] == "plans")
        {
            foreach (var p in a.Plans)
            {
                if (Pick(d.Jobs.Where(j => j.PlanId == p.Id)) is not { } pj) { sb.Append($"<section class=\"box\"><h2>{H(p.Name)}</h2><p class=\"muted\">완료된 버전 없음</p></section>"); continue; }
                sb.Append($"<section class=\"box\"><h2>{H(p.Name)} · <a href=\"/jobs/{H(pj.Id)}\">v{pj.Version}</a>{(pj.Selected ? " <span class=\"tag\">선정</span>" : "")}</h2>");
                Conclusion(sb, pj, JobView.For(d.Store, pj), true, metric);
                sb.Append("</section>");
            }
            return;
        }
        var cur = done.FirstOrDefault(j => j.Id == q["job"].ToString()) ?? Pick(done)!;
        var m = MetricsOf(d.Store, cur, d.Prices);
        var v = m?.View ?? JobView.For(d.Store, cur);
        if (m == null || v.Input == null) { sb.Append("<div class=\"empty\">결과를 읽지 못했습니다.</div>"); return; }
        // the other plans' versions, each plan's own pick; the first one is what the numbers are compared with
        var picks = a.Plans.Select(p => (Plan: p, J: p.Id == cur.PlanId ? cur : Pick(d.Jobs.Where(j => j.PlanId == p.Id)))).ToList();
        var other = picks.Where(x => x.J != null && x.Plan.Id != cur.PlanId).Select(x => (x.Plan.Name, M: MetricsOf(d.Store, x.J!, d.Prices))).FirstOrDefault(x => x.M != null);

        // ---- 4 numbers
        sb.Append("<div class=\"kpis\">");
        var total = m.Total ?? 0;
        var diff = other.M?.Total is { } ot ? total - ot : (int?)null;
        sb.Append(Kpi("권장 대수", total.ToString(CultureInfo.InvariantCulture), "대", "blue",
            diff is { } df ? df == 0 ? $"{H(other.Name)} 대비 동일" : $"{H(other.Name)}보다 {Math.Abs(df)}대 {(df < 0 ? "적음" : "많음")}" : $"시험 {v.Input.Groups.Sum(g => g.MinCount)}~{v.Input.Groups.Sum(g => g.MaxCount)}대",
            diff is < 0 ? "green" : ""));
        var top = v.Groups.Where(g => !double.IsNaN(g.Util)).OrderByDescending(g => g.Util).FirstOrDefault();
        var over = top != null && Over(top, v.UtilMax);
        sb.Append(Kpi("최대 가동률", top == null ? "-" : F(top.Util * 100, "0"), top == null ? "" : "%", over ? "orange" : "ink",
            ""));
        var stop = m.Stop;
        sb.Append(double.IsNaN(stop) ? Kpi("자재 품절", "-", "", "ink", "")
            : stop <= 0 ? Kpi("자재 품절", "0", "초", "ink", "") : Kpi("자재 품절", F(stop, "0"), "초", "red", "⚠ 자재 품절", "red"));
        if (m.Investment is { AnyPriced: true } inv)
        {
            var o = other.M?.Investment is { AnyPriced: true } oi && oi.Currency == inv.Currency ? oi : null;
            var dm = o == null ? (decimal?)null : inv.Total - o.Total;
            var s = Money(inv.Total, inv.Currency);
            int cut = s.Length; while (cut > 0 && !char.IsDigit(s[cut - 1])) cut--; // "1.4억" → "1.4" + "억"
            sb.Append(Kpi("투자비", H(s[..cut]), H(s[cut..]), "ink",
                dm is { } x ? x == 0 ? $"{H(other.Name)} 대비 동일" : $"{H(other.Name)}보다 {H(Money(Math.Abs(x), inv.Currency))} {(x < 0 ? "절감" : "증가")}" : inv.Unpriced.Count > 0 ? $"단가 없음: {H(string.Join(", ", inv.Unpriced))}" : ""));
        }
        sb.Append("</div><div class=\"cg\"><div class=\"col\">");

        // ---- 그룹별 권장 대수
        var groups = ProblemsFirst(v.Groups, v.UtilMax);
        sb.Append($"<section class=\"box gbox\"><div class=\"box-head\"><h3>그룹별 권장 대수</h3><span class=\"t4\">{groups.Count}개 · 문제 먼저</span></div><div class=\"gl\">");
        var tick = F(Math.Min(1, v.UtilMax) * 100, "0.#");
        foreach (var g in groups)
        {
            var c = GroupClass(g, v.UtilMax);
            var sym = c == "fail" ? "⚠ " : c == "over" ? "▲ " : c == "under" ? "▼ " : "";
            var w = double.IsNaN(g.Util) ? 0 : Math.Clamp(g.Util, 0, 1);
            var tip = $"{g.Group} · {g.Type} · 범위 {g.Min}~{g.Max}대" + (c == "fail" ? " · 실패: " + g.Reason : c == "over" ? " · 경고: 가동률 초과" : "");
            sb.Append($"<div class=\"gr g-{c}\" title=\"{H(tip)}\"><span class=\"gn\">{H(g.Group)}</span><span class=\"gb\"><i style=\"width:{F(w * 100, "0.#")}%\"></i><b style=\"left:{tick}%\"></b></span>" +
                $"<span class=\"gc\">{g.Decided?.ToString(CultureInfo.InvariantCulture) ?? "-"}<span>대</span></span><span class=\"gu\">{sym}{Pc0(g.Util)}</span></div>");
        }
        int nFail = groups.Count(g => GroupClass(g, v.UtilMax) == "fail"), nOver = groups.Count(g => GroupClass(g, v.UtilMax) == "over"), nUnder = groups.Count(g => GroupClass(g, v.UtilMax) == "under");
        sb.Append($"</div><div class=\"lg\">{(nFail > 0 ? $"<span class=\"c-red\"><i></i>실패 {nFail}</span>" : "")}<span class=\"c-orange\"><i></i>경고: 상한 {F(v.UtilMax * 100, "0")}% 초과 {nOver}</span><span class=\"c-blue\"><i></i>적정 {groups.Count - nFail - nOver - nUnder}</span>" +
            $"<span class=\"c-gray\"><i></i>10% 미만 {nUnder}</span></div></section>");

        // ---- 안별 결론
        sb.Append("<section class=\"box\"><h3 style=\"text-align:center\">안별 결론</h3><div class=\"pl\">");
        var priced = d.Prices is { Count: > 0 };
        foreach (var (p, pj) in picks)
        {
            if (pj == null) { sb.Append($"<div class=\"pr\"><span class=\"pn\"><b>{H(p.Name)}</b><span>완료된 버전 없음</span></span></div>"); continue; }
            var pm = pj.Id == cur.Id ? m : MetricsOf(d.Store, pj, d.Prices);
            string V(string val, string unit, string label, string color = "") =>
                $"<span class=\"v\"><b{(color.Length > 0 ? $" style=\"color:var(--{color})\"" : "")}>{val}<span>{unit}</span></b><span>{label}</span></span>";
            var pu = pm == null ? double.NaN : pm.Util; var ps = pm == null ? double.NaN : pm.Stop;
            sb.Append($"<a class=\"pr{(pj.Id == cur.Id ? " on" : "")}\" href=\"?tab=conclusion&amp;job={H(pj.Id)}\"{(pj.Id == cur.Id ? " aria-current=\"true\"" : "")}>" +
                $"<span class=\"pn\"><b>{H(p.Name)} v{pj.Version}{(pj.Selected ? " · 선정" : "")}</b><span>{Md(At(pj))}</span></span>" +
                V(pm?.Total?.ToString(CultureInfo.InvariantCulture) ?? "-", "대", "권장", pj.Id == cur.Id ? "blue" : "") +
                V(double.IsNaN(pu) ? "-" : F(pu * 100, "0"), "%", "가동률", pm != null && pu > pm.View.UtilMax ? "orange" : "") +
                V(double.IsNaN(ps) ? "-" : F(ps, "0"), "초", "자재 품절", double.IsNaN(ps) ? "" : ps > 0 ? "red" : "green") +
                (priced ? V(pm?.Investment is { AnyPriced: true } pi ? H(Money(pi.Total, pi.Currency)) : "-", "", "투자비") : "") + "</a>");
        }
        sb.Append("</div></section>");

        // ---- 생산량 한계
        LimitPage.Section(sb, d.Store, cur, $"/analyses/{a.Id}?tab=conclusion&job={cur.Id}");
        sb.Append("</div>");

        // ---- 병목 지도
        // the map's own view: block wait, block occupancy, or path traffic (vehicles through each path, the busiest coloured)
        var view = q["metric"] == "traffic" ? "traffic" : metric;
        bool occ = view == "occ", trf = view == "traffic";
        string Url(string? mt, string? map) => $"?tab=conclusion&amp;job={H(cur.Id)}{((mt ?? view) is "occ" or "traffic" ? "&amp;metric=" + (mt ?? view) : "")}{((map ?? q["map"].ToString()) == "all" ? "&amp;map=all" : "")}";
        string On(bool on) => on ? " class=\"on\"" : "";
        sb.Append("<section class=\"box mapb\"><div class=\"box-head\"><h3>병목 지도</h3>" +
            $"<span class=\"segc\"><a href=\"{Url("wait", null)}\"{On(!occ && !trf)}>평균 대기</a><a href=\"{Url("occ", null)}\"{On(occ)}>점유율</a><a href=\"{Url("traffic", null)}\"{On(trf)} title=\"경로마다 지나간 차량 수(보고서 Guide Path Total, 확인 실행 평균)\">통행량</a></span></div>");
        if (v.Layout == null) sb.Append("<div class=\"empty\">레이아웃을 읽지 못했습니다.</div>");
        else BottleneckMap(sb, v, occ ? b => b.Utilization : b => b.AvgWaitSec, trf ? x => F(x, "#,0.#") + "대" : occ ? x => F(x * 100, "0") + "%" : x => F(x, x < 10 ? "0.#" : "0") + "초",
            q["map"] == "all", Url(null, "zoom"), Url(null, "all"), $"/jobs/{H(cur.Id)}?tab=detail",
            b => $"/analyses/{H(a.Id)}/cuts?job={H(cur.Id)}&amp;add={H(Uri.EscapeDataString(CutEditorPage.AddBlock + b))}", trf ? v.PathTraffic : null, occ ? "점유율" : "평균 대기");
        sb.Append("</section></div>");
    }

    /// <summary>Colour position 0.1..1 of a traffic value between the least and the busiest path with traffic (0.1 keeps the quietest visibly above the grey of "no traffic");
    /// 0.5 when all paths carry the same.</summary>
    internal static double TrafficT(double v, double lo, double hi) => hi > lo ? 0.1 + 0.9 * (v - lo) / (hi - lo) : 0.5;

    /// <summary>The part of the layout to show first: the hot blocks with their own size again as margin, at least 30 % of the layout each way,
    /// kept inside the layout. Model coordinates; the whole layout when nothing is hot.</summary>
    internal static (double X0, double X1, double Y0, double Y1) ZoomBox((double X0, double X1, double Y0, double Y1) all, IReadOnlyList<(double X0, double X1, double Y0, double Y1)> hot)
    {
        if (hot.Count == 0) return all;
        double hx0 = hot.Min(h => h.X0), hx1 = hot.Max(h => h.X1), hy0 = hot.Min(h => h.Y0), hy1 = hot.Max(h => h.Y1);
        double W = all.X1 - all.X0, Hh = all.Y1 - all.Y0;
        double w = Math.Min(W, Math.Max((hx1 - hx0) * 2, W * 0.3)), h = Math.Min(Hh, Math.Max((hy1 - hy0) * 2, Hh * 0.3));
        double cx = Math.Clamp((hx0 + hx1) / 2, all.X0 + w / 2, all.X1 - w / 2), cy = Math.Clamp((hy0 + hy1) / 2, all.Y0 + h / 2, all.Y1 - h / 2);
        return (cx - w / 2, cx + w / 2, cy - h / 2, cy + h / 2);
    }

    // markers and labels are drawn in screen px inside a group scaled by "user units per px"; this keeps them the same size at any zoom or window size.
    // Wheel zooms at the cursor, drag pans (a drag never follows a marker link), double-click or 맞춤 goes back to the first view.
    internal const string FitScript = @"<script>(function(){
function fit(){document.querySelectorAll('svg.bm').forEach(function(s){var m=s.getScreenCTM();if(!m||!m.a)return;var k=1/m.a;
 s.querySelectorAll('g.mk').forEach(function(g){g.setAttribute('transform','translate('+g.getAttribute('data-x')+' '+g.getAttribute('data-y')+') scale('+k+')');});});}
document.querySelectorAll('svg.bm').forEach(function(s){
 if(s.dataset.zoom)return;s.dataset.zoom='1';
 var b0=s.getAttribute('viewBox').split(' ').map(Number),v=b0.slice(),d=null,moved=false;
 function set(){s.setAttribute('viewBox',v.join(' '));fit();}
 function at(x,y){var m=s.getScreenCTM();if(!m)return{x:v[0]+v[2]/2,y:v[1]+v[3]/2};var p=s.createSVGPoint();p.x=x;p.y=y;return p.matrixTransform(m.inverse());}
 function zoom(f,c){var w=v[2]*f;if(w>b0[2]*4)f=b0[2]*4/v[2];if(w<b0[2]/40)f=b0[2]/40/v[2];v=[c.x-(c.x-v[0])*f,c.y-(c.y-v[1])*f,v[2]*f,v[3]*f];set();}
 function mid(){var r=s.getBoundingClientRect();return at(r.left+r.width/2,r.top+r.height/2);}
 s.style.cursor='grab';s.style.touchAction='none';
 s.addEventListener('wheel',function(e){e.preventDefault();zoom(e.deltaY<0?1/1.25:1.25,at(e.clientX,e.clientY));},{passive:false});
 s.addEventListener('pointerdown',function(e){if(e.button!==0)return;var m=s.getScreenCTM();d={x:e.clientX,y:e.clientY,v:v.slice(),k:m&&m.a?1/m.a:1};moved=false;});
 s.addEventListener('pointermove',function(e){if(!d)return;var dx=e.clientX-d.x,dy=e.clientY-d.y;if(!moved&&Math.abs(dx)+Math.abs(dy)<4)return;
  if(!moved){moved=true;s.setPointerCapture(e.pointerId);s.style.cursor='grabbing';}v=[d.v[0]-dx*d.k,d.v[1]-dy*d.k,v[2],v[3]];set();});
 function up(){d=null;s.style.cursor='grab';}
 s.addEventListener('pointerup',up);s.addEventListener('pointercancel',up);
 s.addEventListener('click',function(e){if(moved){e.preventDefault();e.stopPropagation();moved=false;}},true);
 s.addEventListener('dblclick',function(){v=b0.slice();set();});
 var bar=document.createElement('div');bar.className='zb';
 [['＋','확대',function(){zoom(1/1.5,mid());}],['－','축소',function(){zoom(1.5,mid());}],['맞춤','처음 보기로',function(){v=b0.slice();set();}]].forEach(function(t){
  var b=document.createElement('button');b.type='button';b.textContent=t[0];b.title=t[1];b.setAttribute('aria-label','지도 '+t[1]);b.addEventListener('click',t[2]);bar.appendChild(b);});
 s.parentNode.appendChild(bar);
});
fit();addEventListener('resize',fit);})();</script>";

    /// <summary>Paths, blocks shaded by the value, the top 5 blocks as numbered markers; zoomed to the hot blocks (minimap + 전체 toggle) unless
    /// <paramref name="whole"/>. The SVG fills its box (sized by the window), so a large drawing never makes the page larger. Beside it the ranked list.
    /// <paramref name="cutHref"/> (block name -> cut editor link, HTML-encoded) makes each marker a link and adds "＋ 컷" to each ranked block.
    /// <paramref name="traffic"/> (path name -> vehicles that entered it, the report's Guide Path Total): the paths are coloured and widened by it instead,
    /// the blocks are only outlined, and the busiest paths are ranked and marked.</summary>
    internal static void BottleneckMap(StringBuilder sb, JobView v, Func<BlockView, double> value, Func<double, string> label, bool whole, string zoomHref, string allHref, string impactHref,
        Func<string, string>? cutHref = null, IReadOnlyDictionary<string, double>? traffic = null, string metricName = "평균 대기")
    {
        var l = v.Layout!;
        sb.Append("<div class=\"mw\"><div class=\"mv\">");
        if (LayoutBounds(l) is not { } bb) { sb.Append("<div class=\"hint\" style=\"padding-top:40px\">그릴 경로가 없습니다.</div></div></div>"); return; }
        bool tr = traffic != null;
        if (tr) cutHref = null; // a cut is made around a block
        (double X0, double X1, double Y0, double Y1) R(Rect2 r) => (Math.Min(r.Min.X, r.Max.X), Math.Max(r.Min.X, r.Max.X), Math.Min(r.Min.Y, r.Max.Y), Math.Max(r.Min.Y, r.Max.Y));
        IEnumerable<Point2> Pts(ASH.Core.Layout.LayoutPath p) { int n = p.Segments.Any(sg => sg is ArcSeg) ? 16 : 1; double len = p.Length; for (int i = 0; i <= n; i++) yield return p.PointAt(len * i / n); }
        var rects = l.Blocks.GroupBy(b => b.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => R(g.First().Rect), StringComparer.Ordinal);
        // what is ranked and marked: blocks by the value, or paths by their traffic
        var boxes = tr ? l.Paths.GroupBy(p => p.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => { var q = Pts(g.First()).ToList(); return (X0: q.Min(t => t.X), X1: q.Max(t => t.X), Y0: q.Min(t => t.Y), Y1: q.Max(t => t.Y)); }, StringComparer.Ordinal) : rects;
        var vals = tr ? traffic!.Where(kv => !double.IsNaN(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)
            : v.Blocks.Where(b => !double.IsNaN(value(b))).ToDictionary(b => b.Name, value, StringComparer.Ordinal);
        double hi = vals.Values.DefaultIfEmpty(0).Max();
        // traffic colour: min..max over the paths that carry traffic (not value / max, which paints every path of a loop orange);
        // linear rather than rank percentile so the legend's min and max are true values and the colour still tracks the magnitude
        double lo = tr ? vals.Values.Where(x => x > 0).DefaultIfEmpty(0).Min() : 0;
        var ranked = vals.Where(kv => kv.Value > 0 && (!tr || boxes.ContainsKey(kv.Key))).OrderByDescending(kv => kv.Value).Take(5).Select(kv => kv.Key).ToList();
        var zb = ZoomBox(bb, ranked.Take(3).Where(boxes.ContainsKey).Select(n => boxes[n]).ToList());
        double Area((double X0, double X1, double Y0, double Y1) b) => Math.Max(1e-9, b.X1 - b.X0) * Math.Max(1e-9, b.Y1 - b.Y0);
        var zoomed = Area(zb) < 0.8 * Area(bb);
        var box = whole || !zoomed ? bb : zb;
        // drawing coordinates: x from the layout's left, y down from its top
        string X(double x) => F(x - bb.X0, "0.###");
        string Y(double y) => F(bb.Y1 - y, "0.###");
        string ViewBox((double X0, double X1, double Y0, double Y1) b)
        {
            double w = b.X1 - b.X0, h = b.Y1 - b.Y0, p = Math.Max(1e-6, 0.05 * Math.Max(w, h));
            return $"{F(b.X0 - bb.X0 - p, "0.###")} {F(bb.Y1 - b.Y1 - p, "0.###")} {F(w + 2 * p, "0.###")} {F(h + 2 * p, "0.###")}";
        }
        string Line(ASH.Core.Layout.LayoutPath p, string stroke, string width, string title) =>
            "<polyline points=\"" + string.Join(" ", Pts(p).Select(pt => X(pt.X) + "," + Y(pt.Y))) +
            $"\" fill=\"none\" stroke=\"{stroke}\" stroke-width=\"{width}\" stroke-linecap=\"round\" vector-effect=\"non-scaling-stroke\"><title>{H(title)}</title></polyline>";
        string Paths(string stroke, string width) => string.Concat(l.Paths.Select(p => Line(p, stroke, width, p.Name)));
        double k0 = Math.Max(1e-6, (box.X1 - box.X0) * 1.1) / 700; // first guess of units per px until the script measures it
        sb.Append($"<svg class=\"bm\" viewBox=\"{ViewBox(box)}\" preserveAspectRatio=\"xMidYMid meet\" role=\"img\" aria-label=\"병목 지도\">");
        foreach (var b in l.Blocks)
        {
            var r = R(b.Rect); var has = !tr && vals.TryGetValue(b.Name, out var val) && val > 0;
            var tip = tr ? "" : ": " + (vals.TryGetValue(b.Name, out var bv) ? label(bv) : "결과 없음");
            sb.Append($"<rect x=\"{X(r.X0)}\" y=\"{Y(r.Y1)}\" width=\"{F(r.X1 - r.X0, "0.###")}\" height=\"{F(r.Y1 - r.Y0, "0.###")}\" fill=\"{(has ? Heat(hi > 0 ? vals[b.Name] / hi : 0) : "none")}\" fill-opacity=\"0.55\" " +
                $"stroke=\"#c7c7cc\" stroke-width=\"1\" vector-effect=\"non-scaling-stroke\"><title>{H(b.Name + tip)}</title></rect>");
        }
        if (!tr) sb.Append(Paths("#aeaeb2", "4"));
        else // quiet paths thin and grey under the busy ones, each coloured and widened by its share of the busiest
            foreach (var p in l.Paths.OrderBy(p => vals.TryGetValue(p.Name, out var t) ? t : -1))
            {
                var has = vals.TryGetValue(p.Name, out var t) && t > 0;
                var nt = TrafficT(t, lo, hi);
                sb.Append(Line(p, has ? Heat(nt) : "#d1d1d6", has ? F(3 + 7 * nt, "0.#") : "2", $"{p.Name}: {(has ? label(t) : "통행 없음")}"));
            }
        string[] col = { "#d70015", "#ff9500", "#ffcc00", "#ffcc00", "#ffcc00" };
        for (int i = ranked.Count - 1; i >= 0; i--) // the first drawn last, on top
        {
            var name = ranked[i];
            if (!boxes.TryGetValue(name, out var r)) continue;
            string cx = X((r.X0 + r.X1) / 2), cy = Y((r.Y0 + r.Y1) / 2);
            var text = $"{H(name)} · {H(label(vals[name]))}"; // H() would turn "·" into &#183;
            if (cutHref != null) sb.Append($"<a href=\"{cutHref(name)}\" aria-label=\"{H(name)} 컷 만들기\">");
            sb.Append($"<g class=\"mk\" data-x=\"{cx}\" data-y=\"{cy}\" transform=\"translate({cx} {cy}) scale({F(k0, "0.######")})\"><title>{i + 1}. {text}</title>" +
                $"<circle r=\"20\" fill=\"{col[i]}\" fill-opacity=\"0.25\"/><circle r=\"11\" fill=\"{col[i]}\"/>" +
                $"<text y=\"4\" text-anchor=\"middle\" font-size=\"12\" font-weight=\"700\" fill=\"{(i < 2 ? "#fff" : "#1d1d1f")}\">{i + 1}</text>" +
                (i == 0 ? $"<text x=\"18\" y=\"26\" font-size=\"13\" font-weight=\"600\" fill=\"#d70015\" stroke=\"#fff\" stroke-width=\"3\" paint-order=\"stroke\">{text}</text>" : "") + "</g>");
            if (cutHref != null) sb.Append("</a>");
        }
        sb.Append("</svg>");
        if (zoomed)
        {
            if (!whole)
                sb.Append($"<svg class=\"mm\" viewBox=\"{ViewBox(bb)}\" preserveAspectRatio=\"xMidYMid meet\" aria-hidden=\"true\">{Paths("#c7c7cc", "1.5")}" +
                    $"<rect x=\"{X(zb.X0)}\" y=\"{Y(zb.Y1)}\" width=\"{F(zb.X1 - zb.X0, "0.###")}\" height=\"{F(zb.Y1 - zb.Y0, "0.###")}\" fill=\"#007aff\" fill-opacity=\"0.15\" stroke=\"#007aff\" stroke-width=\"1.5\" vector-effect=\"non-scaling-stroke\"/></svg>");
            sb.Append($"<span class=\"segc\"><a href=\"{zoomHref}\"{(whole ? "" : " class=\"on\"")}>{(tr ? "붐비는 곳" : "병목 주변")}</a><a href=\"{allHref}\"{(whole ? " class=\"on\"" : "")}>전체</a></span>");
        }
        sb.Append("</div><div class=\"rk\">");
        if (ranked.Count == 0) sb.Append(tr ? "<div class=\"t4\">통행량 자료 없음(이 결과의 보고서에 경로 통계가 없음)</div>" : "<div class=\"t4\">병목 없음</div>");
        else
        {
            sb.Append("<ol>");
            for (int i = 0; i < ranked.Count; i++) sb.Append($"<li><i>{i + 1}</i><span title=\"{H(ranked[i])}\">{H(ranked[i])}</span><b>{H(label(vals[ranked[i]]))}</b>" +
                (cutHref != null && rects.ContainsKey(ranked[i]) ? $"<a class=\"btn\" href=\"{cutHref(ranked[i])}\" title=\"이 블록을 찍는 새 컷을 영상 만들기에서 엽니다\" aria-label=\"{H(ranked[i])} 컷 만들기\">＋ 컷</a>" : "") + "</li>");
            sb.Append("</ol>");
        }
        if (tr) sb.Append(vals.Values.Any(x => x > 0)
            ? HeatLegend("통행량", label(lo), label(hi), 0.1) + Legend(("#d1d1d6", "통행 없음"), ("#d70015", "1위"), ("#ff9500", "2위"), ("#ffcc00", "3~5위"))
            : Legend(("#d1d1d6", "통행 없음")));
        else sb.Append(HeatLegend(metricName, label(0), label(hi)) + Legend(("#fff", "값 없음"), ("#d70015", "1위"), ("#ff9500", "2위"), ("#ffcc00", "3~5위")));
        if (tr && vals.Count > 0) sb.Append($"<div class=\"t4\">통행량 = 확인 실행에서 그 경로에 들어간 차량 수의 평균(보고서 Guide Path Total) · 전체 {H(F(vals.Values.Sum(), "#,0"))}대</div>");
        sb.Append($"<a class=\"end\" href=\"{impactHref}\">영향표 전체 ›</a></div></div>").Append(FitScript);
    }

    // ---------------------------------------------------------------- 버전(status)

    internal static void StatusTab(StringBuilder sb, AnalysisPageData d)
    {
        var a = d.A; var jobs = d.Jobs; var back = "/analyses/" + a.Id;
        string Title(JobInfo j) => $"{a.Name} › {PlanName(a, j)} v{j.Version}";
        var running = jobs.Where(j => j.State == JobState.Running).OrderBy(j => j.StartedUtc ?? j.CreatedUtc).FirstOrDefault();
        if (running != null)
        {
            var eta = JobProgress.Eta(running, d.Queue.Typical, d.Parallel, DateTime.UtcNow);
            HomePages.RunningCard(sb, new(running.Id, Title(running), running.State, running.Phase, JobProgress.Ratio(running), running.RunsDone, running.ExpectedRuns,
                running.RunsDone > 0 ? Dur(JobProgress.AvgRun(running)) : null, eta is { } e ? SeoulTime.FromUtc(e).ToString("HH:mm") : null, running.LastLine ?? "", null), "running");
            sb.Append($"<p>중간 결과: <span data-field=\"partial.text\">{H(PartialText(d.Store, running))}</span></p>");
            if (PartialDetail(d.Store, running) is { } all) sb.Append($"<details><summary>그룹별 중간 결과</summary><p>{H(all)}</p></details>");
        }
        foreach (var j in jobs.Where(j => j.State == JobState.Queued).OrderBy(j => j.CreatedUtc))
            HomePages.RunningCard(sb, new(j.Id, Title(j), j.State, j.Phase, null, j.RunsDone, j.ExpectedRuns, null, null, "", null), "queued." + j.Id);

        foreach (var p in a.Plans)
        {
            sb.Append($"<section class=\"box\"><div class=\"box-head\"><h3>{H(p.Name)}</h3>{(p.Note.Length > 0 ? $"<span class=\"t4\">{H(p.Note)}</span>" : "")}</div>");
            var versions = jobs.Where(j => j.PlanId == p.Id).OrderByDescending(j => j.Version).ToList();
            if (versions.Count == 0) sb.Append("<div class=\"t4\">버전 없음 · ZWCAD에서 이 안으로 보내면 v1</div>");
            foreach (var j in versions)
            {
                var m = MetricsOf(d.Store, j);
                sb.Append($"<div class=\"vr\"><a class=\"vn\" href=\"/jobs/{H(j.Id)}\">v{j.Version}</a>{(j.Selected ? "<span class=\"tag green\">✓ 선정</span>" : "")}");
                if (j.State == JobState.Failed) sb.Append($"<span class=\"state s-failed\">⚠ 실패</span><span class=\"bad\">{H((j.Error ?? "").Split('\n')[0].Trim())}</span>");
                else if (!j.Finished || j.State == JobState.Cancelled) sb.Append(State(j));
                else if (m?.Total is { } t)
                    sb.Append($"<b>{t}대</b><span class=\"{(m.Util > m.View.UtilMax ? "wn" : "")}\">{(m.Util > m.View.UtilMax ? "⚠ " : "")}가동률 {Pc0(m.Util)}</span><span class=\"{(m.Stop > 0 ? "bad" : "")}\">자재 품절 {(double.IsNaN(m.Stop) ? "-" : Num(m.Stop, "0"))}초</span>");
                sb.Append($"<span class=\"t4\">{H(When(j.CreatedUtc))} · {H(j.Submitter)}{(j.VersionNote.Length > 0 ? " · " + H(j.VersionNote) : "")}</span><span class=\"sp\"></span>");
                sb.Append(FileRoutes.ContinueLink(j)).Append(SelectForm(j, back)).Append($"<a class=\"btn\" href=\"/jobs/{H(j.Id)}\">열기</a></div>");
            }
            sb.Append("</section>");
        }
        if (a.Plans.Count == 0) sb.Append("<div class=\"empty\">안 없음 · 아래에서 추가하거나 ZWCAD에서 보낼 때 안 이름을 적으세요</div>");

        sb.Append("<details><summary>안 추가 · 이름 바꾸기 · 지우기</summary><div class=\"box\">");
        sb.Append($"<form method=\"post\" action=\"/analyses/{H(a.Id)}/plans\" class=\"mini\"><input type=\"hidden\" name=\"back\" value=\"{H(back)}\">" +
            "<input type=\"text\" name=\"name\" required maxlength=\"100\" placeholder=\"안 이름(예: A안)\" aria-label=\"안 이름\"><input type=\"text\" name=\"note\" maxlength=\"200\" placeholder=\"메모(선택)\" aria-label=\"메모\">" +
            "<button class=\"primary\">추가</button></form>");
        sb.Append($"<form method=\"post\" action=\"/analyses/{H(a.Id)}/rename\" class=\"mini\" style=\"margin-top:12px\"><input type=\"hidden\" name=\"back\" value=\"{H(back)}\">" +
            $"<input type=\"text\" name=\"name\" required maxlength=\"100\" value=\"{H(a.Name)}\" aria-label=\"분석 이름\"><button>분석 이름 바꾸기</button></form>");
        foreach (var p in a.Plans)
            sb.Append($"<form method=\"post\" action=\"/analyses/{H(a.Id)}/plans/{H(p.Id)}/rename\" class=\"mini\" style=\"margin-top:8px\"><input type=\"hidden\" name=\"back\" value=\"{H(back)}\">" +
                $"<input type=\"text\" name=\"name\" required maxlength=\"100\" value=\"{H(p.Name)}\" aria-label=\"{H(p.Name)} 새 이름\"><button>안 이름 바꾸기</button></form>");
        if (jobs.Count == 0)
            sb.Append($"<form method=\"post\" action=\"/analyses/{H(a.Id)}/delete\" style=\"margin-top:12px\"><button class=\"btn danger\">이 분석 지우기</button> <span class=\"t4\">보낸 작업이 없을 때만</span></form>");
        else sb.Append("<p class=\"t4\">보낸 작업이 있는 분석은 지울 수 없습니다.</p>");
        sb.Append("</div></details>");
        sb.Append($"<p class=\"t4\"><a href=\"/admin/analyses/{H(a.Id)}/delete\">관리자: 분석 삭제…</a></p>");
    }

    /// <summary>/admin/analyses/{id}/delete: what the delete does, and the form that needs the analysis name typed.</summary>
    public static string DeleteConfirm(AnalysisInfo a, int jobCount, IReadOnlyList<FilmInfo> films, int cutCount)
    {
        var active = films.Where(f => !f.Finished).ToList();
        var sb = new StringBuilder($"<p class=\"crumb\"><a href=\"/analyses\">분석</a> › <a href=\"/analyses/{H(a.Id)}?tab=status\">{H(a.Name)}</a> › 삭제</p><h1>분석 삭제(관리자)</h1>");
        sb.Append($"<section class=\"box\"><h2>{H(a.Name)}</h2><p class=\"bad\">⚠ 되돌릴 수 없습니다.</p><ul>" +
            $"<li><b>보낸 작업 {jobCount}건은 그대로 남고</b> \"분석에 넣지 않은 작업\"이 됩니다(안·버전·선정은 해제, 버전 메모는 유지).</li>" +
            $"<li>분석과 안 {a.Plans.Count}개가 지워집니다.</li>" +
            $"<li>컷 목록({cutCount}개)이 지워집니다.</li>" +
            $"<li>만든 영상 {films.Count}건의 기록과 영상 파일이 지워집니다.</li></ul></section>");
        if (active.Count > 0)
        {
            sb.Append("<div class=\"msg bad\" role=\"alert\">대기 중이거나 만드는 중인 영상이 있어 지울 수 없습니다. 먼저 취소하세요: ");
            sb.Append(string.Join(", ", active.Select(f => $"<a href=\"/films/{H(f.Id)}\">{H(When(f.CreatedUtc))}</a>"))).Append("</div>");
            return Page("ASH 분석 삭제", sb.ToString(), nav: "analyses");
        }
        sb.Append($"<form method=\"post\" action=\"/admin/analyses/{H(a.Id)}/delete\" class=\"box\"><div class=\"field\"><label for=\"name\">분석 이름 입력</label><div>" +
            $"<input type=\"text\" id=\"name\" name=\"name\" required autocomplete=\"off\" maxlength=\"100\"><div class=\"help\">지울 분석의 이름 <b>{H(a.Name)}</b> 을(를) 그대로 입력하세요.</div></div></div>" +
            "<div class=\"actions\"><button class=\"btn danger\">분석 지우기</button><a class=\"btn\" href=\"/analyses/" + H(a.Id) + "\">취소</a></div></form>");
        return Page("ASH 분석 삭제", sb.ToString(), nav: "analyses");
    }

    // ---------------------------------------------------------------- 영상

    internal static string FileUrl(FilmInfo f, string name) => $"/films/{H(f.Id)}/files/{H(Uri.EscapeDataString(name))}";

    static string StateSpan(FilmInfo f, string? liveKey) =>
        $"<span class=\"state s-{H(f.State)}\"{(liveKey != null ? $" data-state=\"{liveKey}.state\" data-field=\"{liveKey}.statetext\"" : "")}>{H(JobState.Korean(f.State))}</span>";

    /// <summary>영상 tab: the new-film button, the films being made (progress bar, estimate, cancel) and the finished ones (player, per-cut links, zip).</summary>
    internal static void FilmsTab(StringBuilder sb, AnalysisPageData d)
    {
        var id = H(d.A.Id);
        sb.Append($"<div class=\"box-head\"><span class=\"t3\">컷 리스트 {d.CutCount}개</span><a class=\"btn\" href=\"/analyses/{id}/cuts\">◐ 새 영상 만들기</a></div>");
        var films = d.Films.OrderByDescending(f => f.CreatedUtc).ToList();
        if (films.Count == 0) { sb.Append("<div class=\"empty\">아직 만든 영상이 없습니다.</div>"); return; }
        sb.Append("<div class=\"fgrid\">");
        foreach (var f in films.Where(f => !f.Finished))
        {
            var k = "film." + H(f.Id);
            sb.Append($"<section class=\"box\"><div class=\"box-head\"><h3><a href=\"/films/{H(f.Id)}\">{H(When(f.CreatedUtc))}</a></h3>" + StateSpan(f, k) + "</div>");
            sb.Append($"<p><span data-field=\"{k}.phase\">{H(f.Phase)}</span> · <span data-field=\"{k}.done\">{LiveFields.FilmDone(f)}</span></p>");
            sb.Append($"<div class=\"prog c-orange\"><i data-bar=\"{k}.bar\" style=\"width:{LiveFields.FilmPct(f)}%\"></i></div>");
            sb.Append($"<p class=\"t4\">완료 예상 <span data-field=\"{k}.eta\">{H(LiveFields.FilmEta(f))}</span></p>");
            sb.Append(f.CancelRequested ? "<p class=\"t4\">취소하는 중</p>"
                : $"<form method=\"post\" action=\"/films/{H(f.Id)}/cancel\"><button>{(f.State == JobState.Queued ? "대기 취소" : "취소")}</button></form>");
            sb.Append("</section>");
        }
        foreach (var f in films.Where(f => f.Finished))
        {
            var r = f.Request; var fid = H(f.Id);
            bool Has(string n) => d.Fs != null && File.Exists(Path.Combine(d.Fs.FilmDir(f.Id), n));
            var thumb = Has("thumb.jpg");
            var joinedOk = f.JoinedOk && f.Joined != null && Has(f.Joined);
            var oks = f.Items.Where(i => i.Ok && Has(i.Name)).ToList();
            var main = joinedOk ? f.Joined : oks.FirstOrDefault()?.Name;
            sb.Append($"<section class=\"box\"><div class=\"box-head\"><h3><a href=\"/films/{fid}\">{H(When(f.CreatedUtc))}</a></h3>{(f.State != JobState.Done ? StateSpan(f, null) : "")}</div>");
            if (main != null) sb.Append($"<video controls preload=\"none\" src=\"{FileUrl(f, main)}\"{(thumb ? $" poster=\"/films/{fid}/files/thumb.jpg\"" : "")}></video>");
            else sb.Append(thumb ? $"<img src=\"/films/{fid}/files/thumb.jpg\" alt=\"\">" : "<div class=\"thumb-none\"></div>");
            sb.Append($"<p class=\"t4\">컷 {f.Cuts.Count} × 버전 {r.JobIds.Count} · {r.Width}×{r.Height} · {r.Fps} fps</p>");
            if (oks.Count > 0)
            {
                sb.Append("<ul class=\"cl\">");
                foreach (var i in oks) sb.Append($"<li><a href=\"{FileUrl(f, i.Name)}\">{H(i.Label)}</a></li>");
                sb.Append("</ul>");
            }
            else if (joinedOk) sb.Append("<p class=\"t4\">이어 붙인 영상에만 넣었습니다.</p>");
            if (f.Error != null) sb.Append($"<p class=\"bad\">⚠ {H(f.Error)}</p>");
            sb.Append("<div class=\"actions\">" + (oks.Count > 0 || joinedOk ? $"<a class=\"btn\" href=\"/films/{fid}/files/all.zip\">↓ 전체 zip</a>" : "") + $"<a class=\"btn\" href=\"/films/{fid}\">자세히</a></div></section>");
        }
        sb.Append("</div>");
    }

    // clicking ▶ on a cut plays it in the page's player instead of opening the file
    const string PlayScript = "<script>document.querySelectorAll('a[data-play]').forEach(function(a){a.addEventListener('click',function(e){var v=document.getElementById('fv');if(!v)return;" +
        "e.preventDefault();v.src=a.getAttribute('href');var t=document.getElementById('fvt');if(t)t.textContent=a.getAttribute('data-play');v.play();v.scrollIntoView({block:'nearest'});});});</script>";

    /// <summary>/films/{id}: the player first (the joined film, else the first cut), progress while it runs, the cuts with play and download, the log.
    /// <paramref name="exists"/> tells which files are still on disk.</summary>
    public static string Film(FilmInfo f, Func<string, bool> exists, string logTail)
    {
        var r = f.Request; var fid = H(f.Id);
        var sb = new StringBuilder(PollSlots + "<style>.fv{width:100%;max-height:62vh;background:#000;border-radius:10px;display:block}.fc .row{margin-top:8px}.fc .row .t>div{font-weight:600}</style>");
        sb.Append($"<p class=\"crumb\"><a href=\"/analyses\">분석</a> › <a href=\"/analyses/{H(r.AnalysisId)}?tab=films\">{H(f.AnalysisName)}</a> › 영상</p>");
        var joinedOk = f.Joined != null && f.JoinedOk && exists(f.Joined);
        var oks = f.Items.Where(i => i.Ok && exists(i.Name)).ToList();
        sb.Append($"<div class=\"head\"><h1>영상</h1><span class=\"state s-{H(f.State)}\" data-state=\"film.state\" data-field=\"film.statetext\">{H(JobState.Korean(f.State))}</span>" +
            $"<span class=\"t4\">{H(When(f.CreatedUtc))}</span><span class=\"sp\"></span>");
        if (f.Finished && (joinedOk || oks.Count > 0)) sb.Append($"<a class=\"btn pri\" href=\"/films/{fid}/files/all.zip\">↓ 전체 zip</a>");
        sb.Append("</div>");
        sb.Append($"<p class=\"t3\"><span data-field=\"film.phase\">{H(f.Phase)}</span><span data-field=\"film.sep\">{(f.Phase != null ? " · " : "")}</span><span data-field=\"film.done\">컷 {f.Items.Count}개 중 {f.Done}개 완료</span>" +
            $"{(f.Failed > 0 ? $" · <span class=\"bad\">⚠ 실패 {f.Failed}개</span>" : "")}{(f.StartedUtc != null ? " · 걸린 시간 " + Dur((f.FinishedUtc ?? DateTime.UtcNow) - f.StartedUtc.Value) : "")}</p>");
        sb.Append($"<div class=\"prog c-orange\"><i data-bar=\"film.bar\" style=\"width:{LiveFields.FilmPct(f)}%\"></i></div>");
        if (!f.Finished)
        {
            if (f.State == JobState.Queued) sb.Append("<p class=\"t4\">대수 탐색 작업 사이에 하나씩 찍습니다.</p>");
            sb.Append(f.CancelRequested ? "<p class=\"t4\">취소하는 중 · 지금 단계가 끝나면 멈춥니다</p>"
                : $"<form method=\"post\" action=\"/films/{fid}/cancel\" class=\"actions\"><button>{(f.State == JobState.Queued ? "대기 취소" : "촬영 취소")}</button> <span class=\"t4\">다 찍은 컷은 남습니다</span></form>");
        }
        if (f.Error != null) sb.Append($"<div class=\"msg {(f.State == JobState.Done ? "warn" : "bad")}\" role=\"alert\"><b>{(f.State == JobState.Done ? "주의" : "오류")}:</b> {H(f.Error)}</div>");
        foreach (var w in f.Warnings) sb.Append($"<div class=\"msg warn\">{H(w)}</div>");

        string Dl(string name) => $"<a class=\"btn\" href=\"{FileUrl(f, name)}\" download=\"{H(name)}\">↓ 받기</a>";
        if (f.Joined != null || oks.Count > 0)
        {
            var main = joinedOk ? f.Joined : f.Joined == null ? oks[0].Name : null;
            var title = f.Joined != null ? "이어 붙인 영상" : oks[0].Label;
            sb.Append($"<section class=\"box\"><div class=\"box-head\"><h2 id=\"fvt\">{H(title)}</h2>{(main != null ? Dl(main) : "")}</div>");
            if (main != null) sb.Append($"<video id=\"fv\" class=\"fv\" controls playsinline preload=\"metadata\" src=\"{FileUrl(f, main)}\"></video>");
            else sb.Append($"<p class=\"t4\">{(f.Finished ? "만들지 못했습니다." : "컷을 다 찍은 뒤 이어 붙입니다.")}</p>");
            sb.Append("</section>");
        }
        sb.Append($"<section class=\"box fc\"><h2>컷 {f.Items.Count}개</h2>");
        foreach (var i in f.Items)
        {
            sb.Append($"<div class=\"row\"><span class=\"t\"><div>{H(i.Label)}</div></span>");
            if (i.Ok && exists(i.Name)) sb.Append($"<a class=\"btn\" href=\"{FileUrl(f, i.Name)}\" data-play=\"{H(i.Label)}\">▶ 재생</a>").Append(Dl(i.Name));
            else if (i.Ok) sb.Append("<span class=\"t4\">이어 붙인 영상에만 넣었습니다.</span>");
            else if (i.Error != null) sb.Append($"<span class=\"bad\">⚠ {H(i.Error)}</span>");
            else sb.Append($"<span class=\"t4\">{(f.Finished ? "만들지 못했습니다." : "차례를 기다립니다.")}</span>");
            sb.Append("</div>");
        }
        sb.Append("</section>");
        sb.Append($"<p class=\"t4\">{r.Width}×{r.Height} · {r.Fps} fps · 자막 {(r.Caption ? "켬" : "끔")}" +
            $"{(r.SameCounts != null ? " · 같은 대수 " + H(string.Join(", ", r.SameCounts.Select(kv => kv.Key + " " + kv.Value + "대"))) : r.VehicleDelta != 0 ? $" · 결론 {r.VehicleDelta:+0;-0}대" : " · 결론 대수")}" +
            $" · 시드 {(r.Seed?.ToString(CultureInfo.InvariantCulture) ?? "첫 버전의 시작 시드")}</p>");
        sb.Append($"<details><summary>촬영 기록 (film.log)</summary><pre>{H(logTail)}</pre><a href=\"/films/{fid}/files/film.log\">film.log 전체</a></details>");
        sb.Append(PlayScript);
        return Page("ASH 영상 " + f.AnalysisName, sb.ToString(), f.Finished ? null : PollScript("/api/films/" + f.Id, new Dictionary<string, string> { [f.Id] = f.State }), "films");
    }

    // ---------------------------------------------------------------- 비교

    internal sealed record CompareColumn(JobInfo Job, string PlanName, Metrics? M);

    static readonly string[] Palette = { "#1f3a5f", "#378add", "#e24b4a", "#3b6d11", "#ef9f27", "#7a4fb3" };

    /// <summary>비교 tab: version chips (?jobs=a,b — the first is the base), the metric table with changes against the base, the grouped utilization chart, and on request (&amp;maps=1) the bottleneck maps side by side.</summary>
    internal static void CompareTab(StringBuilder sb, AnalysisPageData d, IQueryCollection q)
    {
        var a = d.A;
        int PlanIx(JobInfo j) => a.Plans.FindIndex(p => p.Id == j.PlanId);
        var all = d.Jobs.OrderBy(PlanIx).ThenBy(j => j.Version).ToList();
        var byId = all.ToDictionary(j => j.Id);
        var ids = q["jobs"].Count > 0
            ? q["jobs"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(byId.ContainsKey).Distinct().ToList()
            : all.Where(j => j.Selected).Select(j => j.Id).ToList();
        var showAll = q["groups"] == "all";
        string Url(IEnumerable<string> l, bool maps = false, bool? all = null) =>
            $"?tab=compare&amp;jobs={string.Join(",", l.Select(Uri.EscapeDataString))}{(maps ? "&amp;maps=1" : "")}{((all ?? showAll) ? "&amp;groups=all" : "")}";
        string Name(JobInfo j) => PlanName(a, j);
        sb.Append("<section class=\"box\"><div class=\"box-head\"><h2>버전 비교</h2><span class=\"t4\">첫 열이 기준 · 작을수록 좋음</span></div><p>");
        foreach (var j in all)
            sb.Append($"<a href=\"{Url(ids.Contains(j.Id) ? ids.Where(x => x != j.Id) : ids.Append(j.Id))}\" class=\"chip{(ids.Contains(j.Id) ? " sel" : "")}\">{H(Name(j))} v{j.Version}{(j.Selected ? " · 선정" : "")}</a>");
        sb.Append("</p>");
        if (ids.Count < 2) { sb.Append("<p class=\"muted\">버전을 2개 이상 고르세요.</p></section>"); return; }
        var cols = ids.Select(id => new CompareColumn(byId[id], Name(byId[id]), MetricsOf(d.Store, byId[id], d.Prices))).ToList();
        var groups = cols.Where(c => c.M != null).SelectMany(c => c.M!.View.Groups.Select(g => g.Group)).Distinct(StringComparer.Ordinal).ToList();
        var baseM = cols[0].M;
        var many = groups.Count > ManyGroups;

        sb.Append(CompareDetail.Css).Append("<div class=\"scroll\"><table><tr><th>지표</th>");
        for (int i = 0; i < cols.Count; i++)
        {
            var c = cols[i];
            sb.Append($"<th><a href=\"/jobs/{H(c.Job.Id)}\">{H(c.PlanName)} v{c.Job.Version}</a><br>")
              .Append(i == 0 ? "<span class=\"tag\">기준</span>" : $"<a href=\"{Url(ids.Where(x => x == c.Job.Id).Concat(ids.Where(x => x != c.Job.Id)))}\">기준으로</a>").Append("</th>");
        }
        sb.Append("</tr>");
        // every row is "smaller is better": a negative change against the base is ok, a positive one bad
        // labels are trusted constants (plus H(group)); H() would turn "·" into &#183;, so they are not encoded again here
        void Row(string label /* already HTML */, Func<Metrics, double?> get, Func<Metrics, string> show)
        {
            sb.Append($"<tr><th>{label}</th>");
            var mk = CompareDetail.Marks(cols.Select(c => c.M != null && get(c.M) is { } x && !double.IsNaN(x) ? new CompareDetail.Cell(x, "") : new CompareDetail.Cell(null, "")).ToList(), false);
            for (int i = 0; i < cols.Count; i++)
            {
                var m = cols[i].M;
                if (m == null) { sb.Append("<td class=\"num muted\">-</td>"); continue; }
                sb.Append($"<td class=\"num{(string.IsNullOrEmpty(mk[i]) ? "" : " " + mk[i])}\">{H(show(m))}");
                if (i > 0 && baseM != null && get(m) is { } v && !double.IsNaN(v) && get(baseM) is { } b && !double.IsNaN(b))
                {
                    var dl = Math.Round(v - b, 1);
                    sb.Append($" <small{(dl < 0 ? " class=\"ok\"" : dl > 0 ? " class=\"bad\"" : "")}>{H(F(dl, "+0.#;-0.#;0"))}</small>");
                }
                sb.Append("</td>");
            }
            sb.Append("</tr>");
        }
        string Pc(double u) => double.IsNaN(u) ? "-" : Num(u * 100);
        double? Pv(double u) => double.IsNaN(u) ? null : u * 100;
        BlockView? Worst(Metrics m) => m.View.Blocks.Where(x => !double.IsNaN(x.AvgWaitSec)).OrderByDescending(x => x.AvgWaitSec).FirstOrDefault();
        Row("총 대수(대)", m => m.Total, m => m.Total?.ToString(CultureInfo.InvariantCulture) ?? "-");
        Row("최대 가동률(%)", m => Pv(m.Util), m => Pc(m.Util));
        Row("자재 품절 합계(초)", m => m.Stop, m => double.IsNaN(m.Stop) ? "-" : Num(m.Stop, "0"));
        Row("교착(회)", m => m.View.Deadlocks, m => m.View.Deadlocks.ToString(CultureInfo.InvariantCulture));
        Row("최대 병목(block·초)", m => Worst(m)?.AvgWaitSec, m => Worst(m) is { } w ? $"{w.Name}·{Num(w.AvgWaitSec)}" : "-");
        if (d.Prices is { Count: > 0 })
            Row("투자비(원)", m => m.Investment is { AnyPriced: true } i ? (double)i.Total : null,
                m => Cost(m.Investment) + (m.Investment is { Unpriced.Count: > 0 } x ? $" (단가 없음: {string.Join(", ", x.Unpriced)})" : ""));
        foreach (var g in many ? new List<string>() : groups) // many groups: per-group numbers live in the heat grid below
        {
            GroupView? G(Metrics m) => m.View.Groups.FirstOrDefault(x => x.Group == g);
            Row($"{H(g)} 권장(대)", m => G(m)?.Decided, m => G(m)?.Decided?.ToString(CultureInfo.InvariantCulture) ?? "-");
            Row($"{H(g)} 가동률(%)", m => G(m) is { } x ? Pv(x.Util) : null, m => G(m) is { } x ? Pc(x.Util) : "-");
        }
        sb.Append("</table></div>");
        if (many)
        {
            var changed = groups.Where(g => Changed(cols, g)).ToList();
            sb.Append($"<p class=\"mini\" style=\"margin-top:12px\">그룹 {groups.Count}개: <a href=\"{Url(ids, all: false)}\" class=\"chip{(showAll ? "" : " sel")}\">버전 간 달라진 그룹만 ({changed.Count})</a>" +
                $"<a href=\"{Url(ids, all: true)}\" class=\"chip{(showAll ? " sel" : "")}\">전체 그룹</a></p>");
            var shown = showAll ? groups : changed;
            if (shown.Count == 0) sb.Append($"<p class=\"muted\">버전 간 달라진 그룹이 없습니다. <a href=\"{Url(ids, all: true)}\">전체 그룹 보기</a></p>");
            else sb.Append("<div class=\"scroll\">").Append(HeatGrid(cols, shown)).Append("</div>");
        }
        else sb.Append("<div class=\"scroll\" style=\"margin-top:12px\">").Append(CompareChart(cols, groups, baseM?.View.UtilMax ?? cols.Select(c => c.M?.View.UtilMax).FirstOrDefault(x => x != null) ?? 0.75)).Append("</div>");
        sb.Append("</section>");
        CompareDetail.Append(sb, d, cols);
        sb.Append("<section class=\"box\">");
        if (q["maps"] != "1") { sb.Append($"<p><a class=\"btn\" href=\"{Url(ids, true)}\">병목 지도 나란히</a></p></section>"); return; }
        sb.Append("<h2>병목 지도(block 평균 대기, 같은 축척)</h2><div class=\"scroll\"><div style=\"display:flex;gap:12px\">");
        // one scale for every map: the union of the layouts' bounds and the largest wait over all versions
        var bs = cols.Where(c => c.M?.View.Layout != null).Select(c => LayoutBounds(c.M!.View.Layout!)).Where(b => b != null).Select(b => b!.Value).ToList();
        (double, double, double, double)? shared = bs.Count == 0 ? null : (bs.Min(b => b.X0), bs.Max(b => b.X1), bs.Min(b => b.Y0), bs.Max(b => b.Y1));
        var hiWait = cols.Where(c => c.M != null).SelectMany(c => c.M!.View.Blocks.Select(b => b.AvgWaitSec)).Where(x => !double.IsNaN(x)).DefaultIfEmpty(0).Max();
        foreach (var c in cols)
            sb.Append("<div>").Append(c.M?.View.Layout == null ? $"<div style=\"font-weight:600\">{H(c.PlanName)} v{c.Job.Version}</div><p class=\"muted\">지도가 없습니다.</p>"
                : Map(c.M.View, $"{c.PlanName} v{c.Job.Version}", b => b.AvgWaitSec, x => F(x, "0.0") + "초", 360, 250, false, shared, hiWait)).Append("</div>");
        sb.Append("</div></div></section>");
    }

    /// <summary>A group changed between the chosen versions: its recommended count differs, or its utilisation spans 5 %p or more.</summary>
    internal static bool Changed(IReadOnlyList<CompareColumn> cols, string group)
    {
        var gs = cols.Where(c => c.M != null).Select(c => c.M!.View.Groups.FirstOrDefault(x => x.Group == group)).ToList();
        var us = gs.Where(g => g != null && !double.IsNaN(g.Util)).Select(g => g!.Util).ToList();
        return gs.Select(g => g?.Decided).Distinct().Count() > 1 || (us.Count > 1 && us.Max() - us.Min() >= 0.05 - 1e-9);
    }

    /// <summary>Rows = groups, columns = versions; cell colour = utilisation (the map's heat scale), text = recommended count, red outline = judgement failed.</summary>
    internal static string HeatGrid(IReadOnlyList<CompareColumn> cols, IReadOnlyList<string> groups)
    {
        var sb = new StringBuilder("<table class=\"heat\"><tr><th>그룹</th>");
        foreach (var c in cols) sb.Append($"<th>{H(c.PlanName)} v{c.Job.Version}</th>");
        sb.Append("</tr>");
        foreach (var g in groups)
        {
            sb.Append($"<tr><th>{H(g)}</th>");
            foreach (var c in cols)
            {
                var gv = c.M?.View.Groups.FirstOrDefault(x => x.Group == g);
                if (gv == null) { sb.Append("<td class=\"num hc muted\">-</td>"); continue; }
                var n = gv.Decided?.ToString(CultureInfo.InvariantCulture) ?? "-";
                var u = double.IsNaN(gv.Util) ? "-" : F(gv.Util * 100, "0") + "%";
                sb.Append($"<td class=\"num hc\" style=\"background:{Heat(gv.Util)}{(gv.AllOk ? "" : ";outline:2px solid #a32d2d;outline-offset:-2px")}\" title=\"{H(g)} · {H(c.PlanName)} v{c.Job.Version} · {n}대 · 가동률 {u}{(gv.AllOk ? "" : " · 판정 실패")}\">{n}</td>");
            }
            sb.Append("</tr>");
        }
        return sb.Append("</table><div class=\"t4\">숫자 = 권장(대)</div>").Append(HeatLegend("가동률", "0%", "100%"))
            .Append(Legend(("#fff;outline:2px solid #a32d2d;outline-offset:-2px", "판정 실패"))).ToString();
    }

    /// <summary>Grouped bars: per group one bar per version (palette colour per version), dashed line = utilization limit.</summary>
    internal static string CompareChart(IReadOnlyList<CompareColumn> cols, IReadOnlyList<string> groups, double utilMax)
    {
        double w = Math.Max(520, groups.Count * (cols.Count * 14 + 20)), h = 240, l = 36, bt = 28, top = 12, ph = h - bt - top;
        var vals = cols.SelectMany(c => c.M?.View.Groups.Select(g => g.Util) ?? Enumerable.Empty<double>()).Where(u => !double.IsNaN(u));
        double hi = Math.Max(1, Math.Max(utilMax, vals.DefaultIfEmpty(0).Max()));
        string Y(double u) => F(top + ph * (1 - u / hi), "0.#");
        var sb = new StringBuilder($"<svg width=\"{F(w, "0.#")}\" height=\"{h}\" viewBox=\"0 0 {F(w, "0.#")} {h}\" style=\"background:#fff;border:1px solid var(--line);border-radius:10px\">");
        sb.Append($"<line x1=\"{l}\" x2=\"{F(w - 4, "0.#")}\" y1=\"{Y(0)}\" y2=\"{Y(0)}\" stroke=\"#888780\"/>");
        double slot = (w - l - 4) / Math.Max(1, groups.Count);
        for (int gi = 0; gi < groups.Count; gi++)
        {
            double gx = l + gi * slot, bw = Math.Min(14, (slot - 6) / Math.Max(1, cols.Count));
            for (int ci = 0; ci < cols.Count; ci++)
            {
                var gv = cols[ci].M?.View.Groups.FirstOrDefault(x => x.Group == groups[gi]);
                if (gv == null || double.IsNaN(gv.Util)) continue;
                double x = gx + 3 + ci * bw;
                sb.Append($"<rect x=\"{F(x, "0.#")}\" y=\"{Y(gv.Util)}\" width=\"{F(bw - 1, "0.#")}\" height=\"{F(ph * gv.Util / hi, "0.#")}\" fill=\"{Palette[ci % Palette.Length]}\"><title>{H(groups[gi])} · {H(cols[ci].PlanName)} v{cols[ci].Job.Version}: {H(F(gv.Util * 100, "0.#"))}%</title></rect>");
            }
            sb.Append($"<text x=\"{F(gx + slot / 2, "0.#")}\" y=\"{h - 10}\" font-size=\"13\" text-anchor=\"middle\">{H(groups[gi])}</text>");
        }
        sb.Append($"<line x1=\"{l}\" x2=\"{F(w - 4, "0.#")}\" y1=\"{Y(utilMax)}\" y2=\"{Y(utilMax)}\" stroke=\"#a32d2d\" stroke-dasharray=\"5 4\"><title>가동률 상한 {H(F(utilMax * 100, "0.#"))}%</title></line>");
        sb.Append($"<text x=\"{l - 4}\" y=\"{Y(utilMax)}\" font-size=\"11\" text-anchor=\"end\" fill=\"#a32d2d\">{H(F(utilMax * 100, "0"))}%</text></svg>");
        sb.Append("<div class=\"t4\">");
        for (int ci = 0; ci < cols.Count; ci++) sb.Append($"<span style=\"color:{Palette[ci % Palette.Length]}\">■</span> {H(cols[ci].PlanName)} v{cols[ci].Job.Version} &nbsp;");
        return sb.Append("· 가동률(%) · 빨간 점선은 상한</div>").ToString();
    }
}
