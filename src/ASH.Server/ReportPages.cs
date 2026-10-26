using System.Globalization;
using System.Text;
using ASH.Core.Analysis;
using static ASH.Server.JobPages;

namespace ASH.Server;

/// <summary>One number card: label, value, unit, the --colour of the value, the line under it and its colour (as <see cref="AnalysisPages.Kpi"/> takes them). Plain text.</summary>
internal sealed record ReportKpi(string Label, string Value, string Unit, string Color, string Sub, string SubColor = "");

/// <summary>A table of plain-text cells; Cls per row is a space-separated status: "cur" (the version the report is about), "fail" (red), "over" (orange warning), "under" (gray).</summary>
internal sealed record ReportTable(string[] Head, List<(string[] Cells, string Cls)> Rows);

/// <summary>A finished film: its thumb.jpg on disk (null when missing) and the file a viewer opens (the joined film, else the first cut).</summary>
internal sealed record ReportFilm(FilmInfo Film, string? Thumb, string Main);

/// <summary>One bar of the PowerPoint's comparison: a plan's pick (or a version of the report's plan), its label and numbers.</summary>
internal sealed record ReportPlan(string Label, JobInfo Job, AnalysisPages.Metrics M);

/// <summary>Everything the report shows, worked out once for the HTML page and the PowerPoint: the version (?job=, else the analysis' pick),
/// its numbers, the plans side by side, the group and material tables, the top-5 blocks by mean wait and the analysis' films.
/// Compare = the plans' picks with results, or (fewer than two) the finished versions of the report's plan, ByPlan says which.</summary>
internal sealed record ReportData(AnalysisInfo A, JobInfo Job, string Version, JobView View, List<ReportKpi> Kpis, ReportTable Plans, ReportTable Groups,
    ReportTable Materials, List<BlockView> Top, List<ReportFilm> Films, IReadOnlyList<JobInfo> Done, List<ReportPlan> Compare, bool ByPlan);

/// <summary>/analyses/{id}/report: the print-ready report (A4 landscape, a page per section, "PDF로 저장" = the browser's print), with the PowerPoint and Excel downloads.</summary>
public static class ReportPages
{
    static bool Ok(JobInfo j) => j.State == JobState.Done && j.Decided != null;

    internal static string Version(AnalysisInfo a, JobInfo j) => $"{AnalysisPages.PlanName(a, j)} v{j.Version}{(j.Selected ? " · 선정" : "")}";

    /// <summary>The report of <paramref name="jobId"/> when it is a finished version of this analysis, else of the analysis' pick; null without a readable result.</summary>
    internal static ReportData? Data(AnalysisPageData d, string? jobId)
    {
        var a = d.A;
        var done = d.Jobs.Where(Ok).OrderBy(j => a.Plans.FindIndex(p => p.Id == j.PlanId)).ThenBy(j => j.Version).ToList();
        var cur = done.FirstOrDefault(j => j.Id == jobId) ?? AnalysisPages.Pick(done);
        if (cur == null || AnalysisPages.MetricsOf(d.Store, cur, d.Prices) is not { } m) return null;
        var v = m.View; var input = v.Input!;
        var lim = F(v.UtilMax * 100, "0");
        bool OverUtil(double u, JobView w) => !double.IsNaN(u) && u > w.UtilMax;

        // ---- the numbers (as the 결론 tab shows them)
        var kpis = new List<ReportKpi> { new("권장 대수", (m.Total ?? 0).ToString(CultureInfo.InvariantCulture), "대", "blue", $"시험 {input.Groups.Sum(g => g.MinCount)}~{input.Groups.Sum(g => g.MaxCount)}대") };
        var top = v.Groups.Where(g => !double.IsNaN(g.Util)).OrderByDescending(g => g.Util).FirstOrDefault();
        var over = top != null && Over(top, v.UtilMax);
        kpis.Add(top == null ? new("최대 가동률", "-", "", "ink", "")
            : new("최대 가동률", F(top.Util * 100, "0"), "%", over ? "orange" : "ink", over ? $"⚠ 경고: 상한 {lim}% 초과 · {top.Group}" : $"✓ 상한 {lim}% 이하 · {top.Group}", over ? "orange" : ""));
        kpis.Add(double.IsNaN(m.Stop) ? new("결품", "-", "", "ink", "") : m.Stop <= 0 ? new("결품", "0", "초", "ink", "✓ 없음", "green") : new("결품", F(m.Stop, "0"), "초", "red", "⚠ 결품 있음", "red"));
        if (m.Investment is { AnyPriced: true } inv)
        {
            var s = AnalysisPages.Money(inv.Total, inv.Currency);
            int cut = s.Length; while (cut > 0 && !char.IsDigit(s[cut - 1])) cut--; // "1.4억" -> "1.4" + "억"
            kpis.Add(new("투자비", s[..cut], s[cut..], "ink", inv.Unpriced.Count > 0 ? "단가 없음: " + string.Join(", ", inv.Unpriced) : ""));
        }

        // ---- 안별 결론: each plan's own pick, this version for its plan
        var priced = d.Prices is { Count: > 0 };
        var plans = new ReportTable(new[] { "안", "버전", "완료", "권장(대)", "최대 가동률(%)", "결품(초)" }.Concat(priced ? new[] { "투자비" } : Array.Empty<string>()).ToArray(), new());
        var compare = new List<ReportPlan>();
        foreach (var p in a.Plans)
        {
            var pj = p.Id == cur.PlanId ? cur : AnalysisPages.Pick(d.Jobs.Where(j => j.PlanId == p.Id));
            var pm = pj == null ? null : pj.Id == cur.Id ? m : AnalysisPages.MetricsOf(d.Store, pj, d.Prices);
            if (pj == null || pm == null) { plans.Rows.Add((new[] { p.Name, "완료된 버전 없음", "", "-", "-", "-" }.Concat(priced ? new[] { "-" } : Array.Empty<string>()).ToArray(), "")); continue; }
            compare.Add(new ReportPlan(p.Name, pj, pm));
            var ou = OverUtil(pm.Util, pm.View); var stop = !double.IsNaN(pm.Stop) && pm.Stop > 0;
            plans.Rows.Add((new[] { p.Name, $"v{pj.Version}{(pj.Selected ? " · 선정" : "")}", When(pj.FinishedUtc), pm.Total?.ToString(CultureInfo.InvariantCulture) ?? "-",
                    (ou ? "▲ " : "") + (double.IsNaN(pm.Util) ? "-" : F(pm.Util * 100, "0")), (stop ? "⚠ " : "") + (double.IsNaN(pm.Stop) ? "-" : F(pm.Stop, "0")) }
                .Concat(priced ? new[] { pm.Investment is { AnyPriced: true } pi ? AnalysisPages.Money(pi.Total, pi.Currency) : "-" } : Array.Empty<string>()).ToArray(),
                string.Join(" ", new[] { pj.Id == cur.Id ? "cur" : "", stop ? "fail" : ou ? "over" : "" }.Where(x => x.Length > 0))));
        }

        // ---- 그룹별: problems first, the same numbers as the job page's 결론 table
        var byGroup = v.VehicleGroups.ToDictionary(x => x.Group, StringComparer.Ordinal);
        var groups = new ReportTable(new[] { "그룹", "AGV 타입", "권장(대)", "범위(대)", "가동률(%)", "공급 횟수(회)", "총 이동 거리(km)", "공급당 이동 거리(m)", "판정" }, new());
        foreach (var g in AnalysisPages.ProblemsFirst(v.Groups, v.UtilMax))
        {
            var c = AnalysisPages.GroupClass(g, v.UtilMax);
            var vg = byGroup.GetValueOrDefault(g.Group);
            groups.Rows.Add((new[] { g.Group, g.Type, g.Decided?.ToString(CultureInfo.InvariantCulture) ?? "-", $"{g.Min}~{g.Max}",
                (c == "over" ? "▲ " : c == "under" ? "▼ " : "") + P(g.Util), vg == null ? "-" : F(vg.Jobs * vg.Count, "0"), vg == null ? "-" : F(vg.Distance * vg.Count / 1000, "0.0"),
                vg == null ? "-" : F(vg.DistPerJob, "0.#"), c == "fail" ? "⚠ 실패: " + g.Reason : c == "over" ? "⚠ 경고: 가동률 초과" : "✓ 통과" }, c == "ok" ? "" : c));
        }

        // ---- 자재별: supplies, frequency, lead time, quantities (as the 상세 tab)
        var mats = new ReportTable(new[] { "자재", "그룹", "공급 횟수(회)", "공급 빈도(초)", "리드 타임 평균/최대(초)", "결품(초)", "용기 평균/최소/최대", "자재 평균/최소/최대(개)" }, new());
        foreach (var x in v.Materials)
        {
            var per = PerContainer(v, x);
            string Three(double k, string f) => $"{F(x.StockAvg * k, f)} / {F(x.StockMin * k, f)} / {F(x.StockMax * k, f)}";
            var stop = x.StopSec > 0;
            mats.Rows.Add((new[] { x.Name, x.Group, F(x.Trips, "0.#"), F(SupplyInterval(v, x), "0"), $"{F(x.DelivAvg, "0.0")} / {F(x.DelivMax, "0.0")}",
                (stop ? "⚠ " : "") + F(x.StopSec, "0"), Three(1, "0.#"), Three(per, "0") }, stop ? "fail" : ""));
        }

        var films = new List<ReportFilm>();
        foreach (var f in d.Films.Where(f => f.Finished).OrderByDescending(f => f.CreatedUtc))
        {
            var dir = d.Fs?.FilmDir(f.Id);
            bool Has(string n) => dir != null && File.Exists(Path.Combine(dir, n));
            var main = f.JoinedOk && f.Joined != null && Has(f.Joined) ? f.Joined : f.Items.FirstOrDefault(i => i.Ok && Has(i.Name))?.Name;
            if (main != null) films.Add(new ReportFilm(f, Has("thumb.jpg") ? Path.Combine(dir!, "thumb.jpg") : null, main));
        }
        // the same ranking as the bottleneck map's list (mean wait, positive only)
        var topBlocks = v.Blocks.Where(b => !double.IsNaN(b.AvgWaitSec) && b.AvgWaitSec > 0).OrderByDescending(b => b.AvgWaitSec).Take(5).ToList();
        var byPlan = compare.Count >= 2;
        if (!byPlan)
            compare = done.Where(j => j.PlanId == cur.PlanId).Select(j => (j, jm: j.Id == cur.Id ? m : AnalysisPages.MetricsOf(d.Store, j, d.Prices)))
                .Where(x => x.jm != null).Select(x => new ReportPlan($"v{x.j.Version}", x.j, x.jm!)).ToList();
        return new ReportData(a, cur, Version(a, cur), v, kpis, plans, groups, mats, topBlocks, films, done, compare, byPlan);
    }

    // page-only CSS: the paper-width column on screen, A4 landscape when printed (no sidebar, a page per section, colours kept)
    const string Css = @"<style>
.rbar{display:flex;align-items:center;gap:8px;flex-wrap:wrap;max-width:277mm;margin:0 auto 6px}.rbar .sp{flex:1}.rbar select{width:auto}
.rp{max-width:277mm;margin:0 auto}
.rs{background:#fff;border:1px solid var(--line);border-radius:14px;padding:14px 18px;margin-top:14px}
.rs>h2{margin:0 0 10px;font-size:19px}.rs h3{margin:14px 0 8px}
.rc h1{font-size:26px;margin:0 0 6px}.rc .tags{display:flex;gap:6px;flex-wrap:wrap;margin-bottom:6px}
.rt{width:100%}.rt th,.rt td{padding:6px 8px;font-size:13px}.rt td:first-child{text-align:left;font-weight:600}
.rt tr.cur td{background:var(--blue-b);font-weight:700}.rt tr.fail td{background:var(--red-b);color:var(--red)}.rt tr.over td{background:var(--orange-b);color:var(--orange)}.rt tr.under td{color:var(--sub2)}
.rmap .mw{height:150mm}.rmap .mv{min-height:0}
.rf{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:14px}.rf img,.rf .thumb-none{width:100%;aspect-ratio:16/9;height:auto;object-fit:cover;border-radius:10px;background:var(--gray-b);display:block}
.rf .t4{margin-top:4px;overflow-wrap:anywhere}
@page{size:A4 landscape;margin:10mm}
@media print{
html,body{height:auto;background:#fff;display:block}main{margin:0;height:auto;overflow:visible;padding:0}
.side,.noprint,.mv .segc,.mv .zb,.rk .end,#reload-bar,#net{display:none!important}
.rp{max-width:none}.rs{border:0;border-radius:0;padding:0;margin:0;break-before:page}.rs:first-child{break-before:auto}
tr{break-inside:avoid}thead{display:table-header-group}.kpi,.rf>div{break-inside:avoid}
*{-webkit-print-color-adjust:exact;print-color-adjust:exact}a{color:inherit;text-decoration:none}}
</style>";

    static void Table(StringBuilder sb, ReportTable t, string empty)
    {
        if (t.Rows.Count == 0) { sb.Append($"<div class=\"empty\">{empty}</div>"); return; }
        sb.Append("<table class=\"rt\"><thead><tr>");
        foreach (var h in t.Head) sb.Append($"<th>{H(h)}</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var (cells, cls) in t.Rows)
            sb.Append(cls.Length > 0 ? $"<tr class=\"{cls}\">" : "<tr>").Append(string.Concat(cells.Select(c => $"<td>{H(c)}</td>"))).Append("</tr>");
        sb.Append("</tbody></table>");
    }

    /// <summary>The report page; <paramref name="r"/> null = no finished version yet (the page says so). ?map=all shows the whole layout.</summary>
    internal static string Page(AnalysisInfo a, ReportData? r, bool wholeMap)
    {
        var id = H(a.Id);
        var sb = new StringBuilder(AnalysisPages.Css).Append(Css);
        sb.Append($"<div class=\"rbar noprint\"><span class=\"crumb\" style=\"margin:0\"><a href=\"/analyses\">분석</a> › <a href=\"/analyses/{id}\">{H(a.Name)}</a> › 보고서</span><span class=\"sp\"></span>");
        if (r != null)
        {
            sb.Append("<form class=\"mini\"><select name=\"job\" aria-label=\"버전\" onchange=\"this.form.submit()\">");
            foreach (var j in r.Done) sb.Append($"<option value=\"{H(j.Id)}\"{(j.Id == r.Job.Id ? " selected" : "")}>{H(Version(a, j))}</option>");
            sb.Append("</select><noscript><button>보기</button></noscript></form>");
            sb.Append($"<a class=\"btn\" href=\"/analyses/{id}/report.pptx?job={H(r.Job.Id)}\" download>↓ PPT</a><a class=\"btn\" href=\"/jobs/{H(r.Job.Id)}/files/report.xlsx\" download>↓ Excel</a>" +
                "<button type=\"button\" class=\"primary\" onclick=\"window.print()\">PDF로 저장</button>");
        }
        sb.Append("</div><article class=\"rp\">");

        // ---- 1. cover + numbers + 안별 결론
        sb.Append($"<section class=\"rs rc\"><h1>{H(a.Name)}</h1><div class=\"tags\">");
        foreach (var t in new[] { a.Corp, a.ProductLine, a.Line }.Where(t => t.Length > 0)) sb.Append($"<span class=\"tag\">{H(t)}</span>");
        sb.Append("</div>");
        if (r == null)
        {
            sb.Append("<div class=\"empty\">결과가 나온 버전이 없습니다. 버전이 끝나면 보고서를 만들 수 있습니다.</div></section></article>");
            return JobPages.Page("ASH 보고서 " + a.Name, sb.ToString(), nav: "analyses");
        }
        sb.Append($"<p class=\"t3\">기준 버전 <b>{H(r.Version)}</b> · 완료 {H(When(r.Job.FinishedUtc))} · 작성 {H(When(DateTime.UtcNow))}{(a.Note.Length > 0 ? " · " + H(a.Note) : "")}</p>");
        sb.Append("<div class=\"kpis\">");
        foreach (var k in r.Kpis) sb.Append(AnalysisPages.Kpi(H(k.Label), H(k.Value), H(k.Unit), k.Color, H(k.Sub), k.SubColor));
        sb.Append("</div><h3>안별 결론</h3>");
        Table(sb, r.Plans, "안이 없습니다.");
        sb.Append("</section>");

        // ---- 2. groups, 3. materials
        sb.Append($"<section class=\"rs\"><h2>그룹별 권장 대수</h2>");
        Table(sb, r.Groups, "그룹 결과가 없습니다.");
        sb.Append($"<div class=\"t4\">문제 먼저 · ▲ 상한 {F(r.View.UtilMax * 100, "0")}% 초과(경고) · ▼ 10% 미만 · 공급·거리 = 권장 대수 차량의 합</div></section>");
        sb.Append("<section class=\"rs\"><h2>자재별 공급·리드 타임·수량</h2>");
        Table(sb, r.Materials, "자재 결과가 없습니다(모델이 자재 통계를 쓰지 않았거나 확인 실행 결과가 없음).");
        sb.Append("<div class=\"t4\">확인 실행 평균 · 공급 빈도 = 측정 시간 ÷ 공급 횟수 · 리드 타임 = 호출부터 하차 완료까지</div></section>");

        // ---- 4. bottleneck map (the 결론 tab's renderer) + top 5
        sb.Append("<section class=\"rs rmap\"><h2>병목 지도 <span class=\"t4\">block 평균 대기 · 권장 대수 기준</span></h2>");
        var self = $"/analyses/{id}/report?job={H(r.Job.Id)}";
        if (r.View.Layout == null) sb.Append("<div class=\"empty\">레이아웃을 읽지 못했습니다.</div>");
        else AnalysisPages.BottleneckMap(sb, r.View, b => b.AvgWaitSec, x => F(x, x < 10 ? "0.#" : "0") + "초", wholeMap, self, self + "&amp;map=all", $"/jobs/{H(r.Job.Id)}?tab=detail");
        sb.Append("</section>");

        // ---- 5. films
        sb.Append($"<section class=\"rs\"><h2>영상 <span class=\"t4\">{r.Films.Count}건</span></h2>");
        if (r.Films.Count == 0) sb.Append("<div class=\"empty\">만든 영상이 없습니다.</div>");
        else
        {
            sb.Append("<div class=\"rf\">");
            foreach (var f in r.Films)
            {
                var fid = H(f.Film.Id);
                sb.Append($"<div><a href=\"/films/{fid}\">{(f.Thumb != null ? $"<img src=\"/films/{fid}/files/thumb.jpg\" alt=\"영상 {H(When(f.Film.CreatedUtc))}\">" : "<div class=\"thumb-none\"></div>")}</a>" +
                    $"<div class=\"t4\">{H(When(f.Film.CreatedUtc))} · 컷 {f.Film.Cuts.Count} × 버전 {f.Film.Request.JobIds.Count}</div><div class=\"t4\"><a href=\"{AnalysisPages.FileUrl(f.Film, f.Main)}\">{H(f.Main)}</a></div></div>");
            }
            sb.Append("</div>");
        }
        sb.Append("</section></article>");
        return JobPages.Page("ASH 보고서 " + a.Name, sb.ToString(), nav: "analyses");
    }
}
