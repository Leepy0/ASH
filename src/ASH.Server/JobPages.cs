using System.Globalization;
using System.Net;
using System.Text;
using ASH.Core;
using ASH.Core.Geometry;

namespace ASH.Server;

/// <summary>The job pages: the queue (/jobs) and one job (/jobs/{id}) with the tabs 결론, 상세, 입력, 다운로드. Plain server-rendered HTML with inline SVG charts;
/// nothing is loaded from another host (intranet).</summary>
public static class JobPages
{
    static string H(string? t) => WebUtility.HtmlEncode(t ?? "");
    static string F(double d, string fmt = "0.##") => double.IsNaN(d) || double.IsInfinity(d) ? "-" : d.ToString(fmt, CultureInfo.InvariantCulture);
    static string Pct(double d) => double.IsNaN(d) ? "-" : (d * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
    static string When(DateTime? utc) => utc is { } t ? SeoulTime.Text(t) : "-";
    static string Dur(TimeSpan? d) => d is { } x ? (x.TotalHours >= 1 ? $"{(int)x.TotalHours}시간 {x.Minutes}분" : x.TotalMinutes >= 1 ? $"{(int)x.TotalMinutes}분 {x.Seconds}초" : $"{x.Seconds}초") : "-";

    const string Css = @"
body{font-family:'Malgun Gothic','Segoe UI',sans-serif;margin:0;color:#2c2c2a;background:#fafaf7}
header{background:#1f3a5f;color:#fff;padding:12px 24px}header a{color:#fff;text-decoration:none;margin-right:18px}
main{padding:20px 24px;max-width:1200px}
h1{font-size:22px;font-weight:600;margin:0 0 12px}h2{font-size:17px;margin:24px 0 8px}
table{border-collapse:collapse;background:#fff}td,th{border:1px solid #d3d1c7;padding:6px 10px;text-align:left;font-size:14px}th{background:#f1efe8}
.num{text-align:right}.muted{color:#888780}.bad{color:#a32d2d}.ok{color:#3b6d11}
.cards{display:flex;gap:14px;flex-wrap:wrap}.card{background:#fff;border:1px solid #d3d1c7;border-radius:10px;padding:14px 18px;min-width:200px}
.card .big{font-size:30px;font-weight:600}.card .sub{color:#5f5e5a;font-size:13px}
.tabs a{display:inline-block;padding:8px 14px;border:1px solid #d3d1c7;border-bottom:none;border-radius:8px 8px 0 0;background:#f1efe8;color:#2c2c2a;text-decoration:none;margin-right:4px}
.tabs a.on{background:#fff;font-weight:600}.panel{background:#fff;border:1px solid #d3d1c7;padding:16px}
.bar{background:#e6f1fb;height:14px;position:relative;width:180px;display:inline-block;vertical-align:middle}.bar i{display:block;height:14px;background:#378add}
.bar b{position:absolute;top:-3px;bottom:-3px;width:2px;background:#a32d2d}
.state{display:inline-block;padding:2px 8px;border-radius:10px;font-size:13px}
.s-queued{background:#faeeda;color:#633806}.s-running{background:#e6f1fb;color:#0c447c}.s-done{background:#eaf3de;color:#27500a}.s-failed{background:#fcebeb;color:#791f1f}.s-cancelled{background:#f1efe8;color:#444441}
pre{background:#f1efe8;padding:10px;overflow:auto;max-height:420px;font-size:12px}
button{padding:6px 14px}";

    public static string Page(string title, string body, bool refresh = false) =>
        "<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\"><title>" + H(title) + "</title>" + (refresh ? "<meta http-equiv=\"refresh\" content=\"10\">" : "") +
        "<style>" + Css + "</style></head><body><header><a href=\"/jobs\"><b>ASH 서버</b></a><a href=\"/jobs\">작업 대기열</a><a href=\"/\">배포</a></header><main>" + body + "</main></body></html>";

    static string State(JobInfo j) => $"<span class=\"state s-{H(j.State)}\">{H(JobState.Korean(j.State))}</span>";

    public static string List(IReadOnlyList<JobInfo> jobs, (int Ahead, DateTime? Start, TimeSpan? Typical) q, bool workerEnabled)
    {
        var sb = new StringBuilder("<h1>작업 대기열</h1>");
        sb.Append("<p>").Append(workerEnabled ? "" : "<b class=\"bad\">이 서버는 작업을 실행하지 않습니다(Jobs:WorkerEnabled=false).</b> ")
          .Append($"대기 {q.Ahead}건").Append(q.Typical is { } t ? $" · 보통 한 작업 {Dur(t)}" : "").Append(q.Start is { } s ? $" · 지금 보내면 시작 예상 {H(SeoulTime.Text(s))}" : "").Append("</p>");
        sb.Append("<p class=\"muted\">작업은 ZWCAD의 ASH에서 \"서버로 보내기\"로 올립니다. 먼저 온 순서로 하나씩 실행합니다.</p>");
        sb.Append("<table><tr><th>작업</th><th>상태</th><th>프로젝트 / 안</th><th>모델</th><th>보낸 사람</th><th>보낸 시각(서울)</th><th>실행</th><th>권장 총 대수</th></tr>");
        foreach (var j in jobs)
        {
            var total = j.Decided?.Values.Sum();
            sb.Append($"<tr><td><a href=\"/jobs/{H(j.Id)}\">{H(j.Id)}</a></td><td>{State(j)}{(j.Phase != null ? " " + H(j.Phase) : "")}</td><td>{H(j.Project)} / {H(j.Plan)}</td><td>{H(j.Model)}</td>" +
                $"<td>{H(j.Submitter)} <span class=\"muted\">{H(j.Ip)}</span></td><td>{H(When(j.CreatedUtc))}</td><td class=\"num\">{j.RunsDone}{(j.RunsFailed > 0 ? $" <span class=\"bad\">(실패 {j.RunsFailed})</span>" : "")}</td>" +
                $"<td class=\"num\">{(total is { } n ? n.ToString(CultureInfo.InvariantCulture) : "-")}</td></tr>");
        }
        if (jobs.Count == 0) sb.Append("<tr><td colspan=\"8\" class=\"muted\">아직 작업이 없습니다.</td></tr>");
        sb.Append("</table>");
        return Page("ASH 작업 대기열", sb.ToString(), jobs.Any(j => !j.Finished));
    }

    public static string Job(JobInfo j, JobView? v, string tab, string logTail, (int Ahead, DateTime? Start, TimeSpan? Typical) q, bool canCancel)
    {
        var sb = new StringBuilder();
        sb.Append($"<h1>{H(j.Project)} / {H(j.Plan)} <span class=\"muted\">· 모델 {H(j.Model)}</span></h1>");
        sb.Append($"<p>{State(j)} {(j.Phase != null ? H(j.Phase) + " · " : "")}작업 {H(j.Id)} · 보낸 사람 {H(j.Submitter)} ({H(j.Ip)}) · 보낸 시각 {H(When(j.CreatedUtc))}");
        if (j.StartedUtc != null) sb.Append($" · 실행 시간 {Dur(j.Duration)}");
        sb.Append($" · 모델 실행 {j.RunsDone}회{(j.RunsFailed > 0 ? $" <span class=\"bad\">(실패 {j.RunsFailed})</span>" : "")}</p>");
        if (j.State == JobState.Queued) sb.Append($"<p>{(q.Ahead == 0 ? "다음 차례입니다" : $"앞에 {q.Ahead}건이 있습니다")}{(q.Start is { } s ? $" · 시작 예상 {H(SeoulTime.Text(s))}" : "")}.</p>");
        if (j.State == JobState.Running && j.LastLine != null) sb.Append($"<p class=\"muted\">마지막 출력: {H(j.LastLine)}</p>");
        if (j.Error != null) sb.Append($"<p class=\"bad\"><b>{(j.State == JobState.Done ? "주의" : "오류")}:</b> {H(j.Error)}</p>");
        if (canCancel && !j.Finished)
            sb.Append($"<form method=\"post\" action=\"/jobs/{H(j.Id)}/cancel\"><button>{(j.State == JobState.Queued ? "대기 취소" : "실행 취소")}</button> <span class=\"muted\">보낸 PC와 관리자만 취소할 수 있습니다. 실행 중인 모델 실행은 끝까지 돌고 새 실행만 멈춥니다.</span></form>");

        var tabs = new[] { ("summary", "결론"), ("detail", "상세"), ("input", "입력·추적"), ("files", "다운로드·로그") };
        sb.Append("<div class=\"tabs\">");
        foreach (var (k, name) in tabs) sb.Append($"<a href=\"/jobs/{H(j.Id)}?tab={k}\"{(k == tab ? " class=\"on\"" : "")}>{name}</a>");
        sb.Append("</div><div class=\"panel\">");
        switch (tab)
        {
            case "detail": Detail(sb, j, v); break;
            case "input": InputTab(sb, j, v); break;
            case "files": FilesTab(sb, j, logTail); break;
            default: Summary(sb, j, v); break;
        }
        sb.Append("</div>");
        return Page($"ASH 작업 {j.Id}", sb.ToString(), !j.Finished);
    }

    static void Summary(StringBuilder sb, JobInfo j, JobView? v)
    {
        if (j.Decided == null || v?.Input == null)
        {
            sb.Append(j.Finished ? "<p>결과가 없습니다. 다운로드·로그 탭의 로그를 확인하세요.</p>" : "<p>실행이 끝나면 결과가 나옵니다. 이 페이지는 10초마다 새로 고칩니다.</p>");
            if (v?.InputError != null && j.Finished) sb.Append($"<p class=\"bad\">{H(v.InputError)}</p>");
            return;
        }
        int total = j.Decided.Values.Sum(), min = v.Input.Groups.Sum(g => g.MinCount), max = v.Input.Groups.Sum(g => g.MaxCount);
        bool allOk = v.Groups.All(g => g.AllOk);
        sb.Append("<div class=\"cards\">");
        sb.Append($"<div class=\"card\"><div class=\"sub\">권장 AGV 총 대수</div><div class=\"big\">{total}대</div><div class=\"sub\">시험 범위 {min}~{max}대</div></div>");
        sb.Append($"<div class=\"card\"><div class=\"sub\">확인 실행 판정</div><div class=\"big {(allOk ? "ok" : "bad")}\">{(allOk ? "통과" : "실패")}</div><div class=\"sub\">재고 · 가동률 상한 {Pct(v.UtilMax)} · 공급 · 교착</div></div>");
        sb.Append($"<div class=\"card\"><div class=\"sub\">모델 실행</div><div class=\"big\">{j.RunsDone}회</div><div class=\"sub\">실패 {j.RunsFailed}회 · 경고 {j.Warnings.Count}건 · {Dur(j.Duration)}</div></div>");
        sb.Append("</div>");

        sb.Append("<h2>그룹별 권장 대수와 부하율</h2><table><tr><th>그룹</th><th>AGV 타입</th><th>권장</th><th>범위</th><th>가동률(확인 실행 평균)</th><th>최소 재고/안전재고</th><th>판정</th></tr>");
        foreach (var g in v.Groups)
        {
            var w = double.IsNaN(g.Util) ? 0 : Math.Min(1, g.Util);
            sb.Append($"<tr><td>{H(g.Group)}</td><td>{H(g.Type)}</td><td class=\"num\"><b>{(g.Decided?.ToString(CultureInfo.InvariantCulture) ?? "-")}</b></td><td class=\"num\">{g.Min}~{g.Max}</td>" +
                $"<td><span class=\"bar\"><i style=\"width:{F(w * 100, "0.#")}%\"></i><b style=\"left:{F(Math.Min(1, v.UtilMax) * 100, "0.#")}%\"></b></span> {Pct(g.Util)}</td>" +
                $"<td class=\"num\">{F(g.MinStockRatio)}</td><td class=\"{(g.AllOk ? "ok" : "bad")}\">{(g.AllOk ? "통과" : "실패: " + H(g.Reason))}</td></tr>");
        }
        sb.Append("</table><p class=\"muted\">빨간 세로선은 가동률 상한(Settings의 UtilMax)입니다.</p>");
        if (j.Warnings.Count > 0)
        {
            sb.Append("<h2>경고</h2><ul>");
            foreach (var w in j.Warnings.Take(30)) sb.Append($"<li>{H(w)}</li>");
            sb.Append("</ul>");
        }
        sb.Append("<h2>병목 지도 (권장 대수 기준)</h2>");
        if (v.Layout == null) { sb.Append("<p class=\"muted\">레이아웃을 읽지 못했습니다.</p>"); return; }
        if (v.Blocks.Count == 0) sb.Append("<p class=\"muted\">교통제어 block 결과가 없습니다(block이 없거나 확인 실행 결과가 없음). 경로만 그립니다.</p>");
        sb.Append("<div class=\"cards\">");
        sb.Append("<div>").Append(Map(v, "block 점유율", b => b.Utilization, x => Pct(x))).Append("</div>");
        sb.Append("<div>").Append(Map(v, "block 평균 대기(초)", b => b.AvgWaitSec, x => F(x, "0.0") + "초")).Append("</div>");
        sb.Append("</div>");
        if (v.Blocks.Count > 0)
        {
            sb.Append("<h2>대기가 긴 block</h2><table><tr><th>block</th><th>평균 대기</th><th>점유율</th><th>통과 수</th></tr>");
            foreach (var b in v.Blocks.Take(10)) sb.Append($"<tr><td>{H(b.Name)}</td><td class=\"num\">{F(b.AvgWaitSec, "0.0")}초</td><td class=\"num\">{Pct(b.Utilization)}</td><td class=\"num\">{F(b.Passes, "0")}</td></tr>");
            sb.Append("</table>");
        }
    }

    static string Heat(double t)
    {
        // light grey -> amber -> red
        t = double.IsNaN(t) ? 0 : Math.Clamp(t, 0, 1);
        (int r, int g, int b) a = (241, 239, 232), m = (239, 159, 39), z = (226, 75, 74);
        var (p, q, u) = t < 0.5 ? (a, m, t * 2) : (m, z, (t - 0.5) * 2);
        int L(int x, int y) => (int)Math.Round(x + (y - x) * u);
        return $"rgb({L(p.r, q.r)},{L(p.g, q.g)},{L(p.b, q.b)})";
    }

    /// <summary>Paths in grey, blocks coloured by a value (highest = red), CPs as dots. Model coordinates, y up.</summary>
    static string Map(JobView v, string title, Func<BlockView, double> value, Func<double, string> label)
    {
        var l = v.Layout!;
        var pts = l.Paths.SelectMany(p => new[] { p.Start, p.End }).Concat(l.Blocks.SelectMany(b => new[] { b.Rect.Min, b.Rect.Max })).Concat(l.Cps.Select(c => c.Position)).ToList();
        if (pts.Count == 0) return "<p class=\"muted\">그릴 경로가 없습니다.</p>";
        double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
        double w = 520, h = 360, pad = 12, sx = (w - 2 * pad) / Math.Max(1e-6, x1 - x0), sy = (h - 2 * pad) / Math.Max(1e-6, y1 - y0), s = Math.Min(sx, sy);
        string X(double x) => F(pad + (x - x0) * s, "0.#");
        string Y(double y) => F(h - pad - (y - y0) * s, "0.#");
        var byName = v.Blocks.ToDictionary(b => b.Name, b => value(b), StringComparer.Ordinal);
        double hi = byName.Values.Where(x => !double.IsNaN(x)).DefaultIfEmpty(0).Max();
        var sb = new StringBuilder($"<div style=\"font-weight:600;margin:6px 0\">{H(title)}</div><svg width=\"{w}\" height=\"{h}\" viewBox=\"0 0 {w} {h}\" style=\"background:#fff;border:1px solid #d3d1c7\">");
        foreach (var b in l.Blocks)
        {
            var val = byName.TryGetValue(b.Name, out var d) ? d : double.NaN;
            var fill = double.IsNaN(val) ? "#f1efe8" : Heat(hi > 0 ? val / hi : 0);
            double bx = Math.Min(b.Rect.Min.X, b.Rect.Max.X), by = Math.Max(b.Rect.Min.Y, b.Rect.Max.Y);
            sb.Append($"<rect x=\"{X(bx)}\" y=\"{Y(by)}\" width=\"{F(Math.Abs(b.Rect.Max.X - b.Rect.Min.X) * s, "0.#")}\" height=\"{F(Math.Abs(b.Rect.Max.Y - b.Rect.Min.Y) * s, "0.#")}\" fill=\"{fill}\" stroke=\"#888780\" stroke-width=\"0.5\"><title>{H(b.Name)}: {H(double.IsNaN(val) ? "결과 없음" : label(val))}</title></rect>");
        }
        foreach (var p in l.Paths)
        {
            var pl = new StringBuilder();
            int n = p.Segments.Any(sg => sg is ArcSeg) ? 16 : 1;
            double len = p.Length;
            for (int i = 0; i <= n; i++) { var q = p.PointAt(len * i / n); pl.Append(X(q.X)).Append(',').Append(Y(q.Y)).Append(' '); }
            sb.Append($"<polyline points=\"{pl}\" fill=\"none\" stroke=\"#5f5e5a\" stroke-width=\"1.2\"><title>{H(p.Name)}</title></polyline>");
        }
        foreach (var c in l.Cps) sb.Append($"<circle cx=\"{X(c.Position.X)}\" cy=\"{Y(c.Position.Y)}\" r=\"2\" fill=\"#185fa5\"><title>{H(c.Name)}</title></circle>");
        sb.Append("</svg>");
        sb.Append($"<div class=\"muted\" style=\"font-size:12px\">색: 연회색 0 → 주황 → 빨강 최대({H(label(hi))}). block에 마우스를 올리면 값이 보입니다.</div>");
        return sb.ToString();
    }

    static void Detail(StringBuilder sb, JobInfo j, JobView? v)
    {
        if (v?.Input == null || v.Rows.Count == 0) { sb.Append("<p>아직 실행 결과가 없습니다.</p>"); return; }
        sb.Append("<h2>그룹별 대수에 따른 가동률</h2><p class=\"muted\">파란 선은 그 대수로 돌린 실행들의 평균, 띠는 최소~최대(다른 그룹 대수와 시드가 달라 생기는 폭), 빨간 점선은 가동률 상한, 초록 세로선은 권장 대수, 빨간 점은 판정 실패가 있었던 대수입니다.</p>");
        sb.Append("<div class=\"cards\">");
        foreach (var g in v.Input.Groups)
        {
            if (!v.Curves.TryGetValue(g.Group, out var pts) || pts.Count == 0) continue;
            int? dec = j.Decided != null && j.Decided.TryGetValue(g.Group, out var d) ? d : null;
            sb.Append("<div>").Append(Curve(g.Group, g.MinCount, g.MaxCount, pts, v.UtilMax, dec)).Append("</div>");
        }
        sb.Append("</div>");
        if (j.Clusters is { Count: > 0 })
        {
            sb.Append("<h2>함께 탐색한 묶음</h2><p class=\"muted\">경로나 block을 함께 쓰는 그룹은 서로 영향을 주므로 한 묶음으로 대수를 정했습니다.</p><ul>");
            foreach (var c in j.Clusters) sb.Append($"<li>{H(string.Join(", ", c))}</li>");
            sb.Append("</ul>");
        }
        if (v.Impact.Count > 0)
        {
            var groups = v.Input.Groups.Select(x => x.Group).ToList();
            sb.Append("<h2>영향표</h2><p class=\"muted\">행 그룹을 권장 대수에서 1대 늘렸을 때 열 그룹의 가동률 변화(%p). 진할수록 영향이 큽니다.</p><table><tr><th>+1대 한 그룹</th>");
            foreach (var g in groups) sb.Append($"<th>{H(g)}</th>");
            sb.Append("</tr>");
            double hi = v.Impact.Select(r => Math.Abs(r.UtilDelta)).Where(x => !double.IsNaN(x)).DefaultIfEmpty(0).Max();
            foreach (var row in v.Impact.GroupBy(r => r.Group))
            {
                sb.Append($"<tr><th>{H(row.Key)} ({row.First().PlusOne}대)</th>");
                foreach (var g in groups)
                {
                    var c = row.FirstOrDefault(r => r.OtherGroup == g);
                    if (c == null) { sb.Append("<td class=\"muted\">-</td>"); continue; }
                    var bg = Heat(hi > 0 ? Math.Abs(c.UtilDelta) / hi : 0);
                    sb.Append($"<td class=\"num\" style=\"background:{bg}\">{F(c.UtilDelta * 100, "+0.0;-0.0;0")}</td>");
                }
                sb.Append("</tr>");
            }
            sb.Append("</table>");
        }
        sb.Append("<h2>실행 목록</h2><table><tr><th>실행</th><th>그룹</th><th>대수</th><th>시드</th><th>판정</th><th>가동률</th><th>최소재고비</th><th>공급</th><th>기대</th><th>결품 시간(초)</th><th>교착</th><th>시간(초)</th></tr>");
        foreach (var r in v.Rows.Take(1000))
            sb.Append($"<tr><td>{H(r.RunId)}</td><td>{H(r.Group)}</td><td class=\"num\">{r.Active}</td><td class=\"num\">{r.Seed}</td><td class=\"{(r.Ok ? "ok" : "bad")}\">{(r.Ok ? "통과" : H(r.Reason))}</td>" +
                $"<td class=\"num\">{Pct(r.Utilization)}</td><td class=\"num\">{F(r.MinStockRatio)}</td><td class=\"num\">{r.Supplies}</td><td class=\"num\">{F(r.Expected, "0.#")}</td><td class=\"num\">{F(r.StopTimeSec, "0")}</td><td class=\"num\">{r.Deadlocks}</td><td class=\"num\">{F(r.DurationSec, "0")}</td></tr>");
        sb.Append("</table>");
        if (v.Rows.Count > 1000) sb.Append($"<p class=\"muted\">처음 1000행만 보여 줍니다(전체 {v.Rows.Count}행은 summary.tsv로 받으세요).</p>");
    }

    static string Curve(string group, int min, int max, IReadOnlyList<CountPoint> pts, double utilMax, int? decided)
    {
        double w = 360, h = 220, l = 44, r = 12, t = 22, b = 34;
        int lo = Math.Min(min, pts.Min(p => p.Active)), hi = Math.Max(max, pts.Max(p => p.Active));
        if (hi == lo) hi = lo + 1;
        string X(double a) => F(l + (a - lo) / (hi - lo) * (w - l - r), "0.#");
        string Y(double u) => F(h - b - Math.Clamp(double.IsNaN(u) ? 0 : u, 0, 1) * (h - t - b), "0.#");
        var sb = new StringBuilder($"<svg width=\"{w}\" height=\"{h}\" viewBox=\"0 0 {w} {h}\" style=\"background:#fff;border:1px solid #d3d1c7\">");
        sb.Append($"<text x=\"{l}\" y=\"15\" font-size=\"13\" font-weight=\"600\">{H(group)}</text>");
        foreach (var u in new[] { 0, 0.25, 0.5, 0.75, 1.0 })
            sb.Append($"<line x1=\"{l}\" x2=\"{w - r}\" y1=\"{Y(u)}\" y2=\"{Y(u)}\" stroke=\"#eeede8\"/><text x=\"{l - 6}\" y=\"{Y(u)}\" font-size=\"10\" text-anchor=\"end\" dominant-baseline=\"middle\">{(u * 100):0}%</text>");
        for (int a = lo; a <= hi; a++)
            sb.Append($"<text x=\"{X(a)}\" y=\"{h - b + 14}\" font-size=\"10\" text-anchor=\"middle\">{a}</text>");
        sb.Append($"<text x=\"{F((l + w - r) / 2)}\" y=\"{h - 6}\" font-size=\"10\" text-anchor=\"middle\" fill=\"#5f5e5a\">대수</text>");
        var ok = pts.Where(p => !double.IsNaN(p.Mean)).ToList();
        if (ok.Count > 0)
        {
            var band = string.Join(" ", ok.Select(p => X(p.Active) + "," + Y(p.Hi)).Concat(ok.AsEnumerable().Reverse().Select(p => X(p.Active) + "," + Y(p.Lo))));
            sb.Append($"<polygon points=\"{band}\" fill=\"#b5d4f4\" opacity=\"0.6\"/>");
            sb.Append($"<polyline points=\"{string.Join(" ", ok.Select(p => X(p.Active) + "," + Y(p.Mean)))}\" fill=\"none\" stroke=\"#185fa5\" stroke-width=\"2\"/>");
            foreach (var p in ok)
                sb.Append($"<circle cx=\"{X(p.Active)}\" cy=\"{Y(p.Mean)}\" r=\"{(p.Failed > 0 ? 4 : 2.5)}\" fill=\"{(p.Failed > 0 ? "#e24b4a" : "#185fa5")}\"><title>{p.Active}대: 평균 {Pct(p.Mean)} ({p.Runs}회, 실패 {p.Failed}회)</title></circle>");
        }
        sb.Append($"<line x1=\"{l}\" x2=\"{w - r}\" y1=\"{Y(utilMax)}\" y2=\"{Y(utilMax)}\" stroke=\"#a32d2d\" stroke-dasharray=\"5 4\"/>");
        if (decided is { } d) sb.Append($"<line x1=\"{X(d)}\" x2=\"{X(d)}\" y1=\"{t}\" y2=\"{h - b}\" stroke=\"#3b6d11\" stroke-width=\"2\"/><text x=\"{X(d)}\" y=\"{t - 4}\" font-size=\"10\" text-anchor=\"middle\" fill=\"#3b6d11\">권장 {d}</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    static void InputTab(StringBuilder sb, JobInfo j, JobView? v)
    {
        sb.Append("<h2>이 결과를 만든 입력</h2><table>");
        sb.Append($"<tr><th>작업</th><td>{H(j.Id)}</td></tr><tr><th>프로젝트 / 안</th><td>{H(j.Project)} / {H(j.Plan)}</td></tr><tr><th>모델</th><td>{H(j.Model)}</td></tr>");
        sb.Append($"<tr><th>보낸 사람</th><td>{H(j.Submitter)} ({H(j.Ip)})</td></tr><tr><th>보낸 시각 / 시작 / 끝(서울)</th><td>{H(When(j.CreatedUtc))} / {H(When(j.StartedUtc))} / {H(When(j.FinishedUtc))}</td></tr>");
        if (v?.Input is { } inp)
        {
            var s = inp.Settings;
            sb.Append($"<tr><th>반복 / 시드 시작</th><td>{s.Replications}회 / {s.SeedBase} (탐색 중 반복 {j.InitialReps}회)</td></tr>");
            sb.Append($"<tr><th>시뮬레이션 / 워밍업</th><td>{F(s.SimLengthSec / 3600)}시간 / {F(s.WarmupSec / 3600)}시간</td></tr>");
            sb.Append($"<tr><th>가동률 상한 / 안전재고</th><td>{Pct(s.UtilMax)} / {F(s.SafetyStock)}용기</td></tr>");
            sb.Append($"<tr><th>영향표</th><td>{(j.Impact ? "켬" : "끔")}</td></tr>");
        }
        else if (v?.InputError != null) sb.Append($"<tr><th>입력</th><td class=\"bad\">{H(v.InputError)}</td></tr>");
        sb.Append("</table>");
        sb.Append("<h2>올린 파일 (SHA-256)</h2><table><tr><th>파일</th><th>크기</th><th>SHA-256</th></tr>");
        foreach (var f in j.Files) sb.Append($"<tr><td>{H(f.Path)}</td><td class=\"num\">{f.Size:N0}</td><td style=\"font-family:monospace;font-size:12px\">{H(f.Sha256)}</td></tr>");
        sb.Append("</table>");
        if (v?.Input != null)
        {
            sb.Append("<h2>그룹</h2><table><tr><th>그룹</th><th>타입</th><th>최소</th><th>최대</th><th>대기 CP</th></tr>");
            foreach (var g in v.Input.Groups) sb.Append($"<tr><td>{H(g.Group)}</td><td>{H(g.Type)}</td><td class=\"num\">{g.MinCount}</td><td class=\"num\">{g.MaxCount}</td><td>{H(string.Join(", ", g.StandbyCPs))}</td></tr>");
            sb.Append("</table><h2>자재</h2><table><tr><th>자재</th><th>그룹</th><th>형태</th><th>T/T(초)</th><th>용기당</th><th>호출 임계</th></tr>");
            foreach (var m in v.Input.Materials) sb.Append($"<tr><td>{H(m.Material)}</td><td>{H(m.Group)}</td><td>{H(m.Form.Length == 0 ? "Box" : m.Form)}</td><td class=\"num\">{F(m.TaktTime)}</td><td class=\"num\">{F(m.UnitsPerContainer)}</td><td class=\"num\">{F(m.CallThreshold)}</td></tr>");
            sb.Append("</table>");
        }
    }

    static void FilesTab(StringBuilder sb, JobInfo j, string logTail)
    {
        string Link(string name, string label) => $"<li><a href=\"/jobs/{H(j.Id)}/files/{name}\">{label}</a></li>";
        sb.Append("<h2>내려받기</h2><ul>");
        sb.Append(Link("summary.tsv", "summary.tsv — 실행별·그룹별 결과"));
        sb.Append(Link("impact.tsv", "impact.tsv — 영향표"));
        sb.Append(Link("runs.zip", "runs.zip — 모든 실행 폴더(결과·보고서·메시지)"));
        sb.Append(Link("upload.zip", "upload.zip — 올린 원본"));
        sb.Append(Link("job.log", "job.log — 작업 로그 전체"));
        sb.Append("</ul>");
        if (j.FailureMessages.Count > 0)
        {
            sb.Append("<h2>실패한 실행</h2><ul>");
            foreach (var m in j.FailureMessages) sb.Append($"<li>{H(m)}</li>");
            sb.Append("</ul>");
        }
        sb.Append("<h2>로그 (끝부분)</h2><pre>").Append(H(logTail)).Append("</pre>");
    }
}
