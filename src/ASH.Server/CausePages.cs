using System.Globalization;
using System.Text;
using ASH.Core.Cause;
using ASH.Core.Film;
using ASH.Core.Runner;
using ASH.Core.Settings;
using static ASH.Server.JobPages;

namespace ASH.Server;

/// <summary>The 원인 tab of an analysis: what CauseAnalysis finds for one version's run, grouped by kind, with a map of the findings that have a place,
/// "영상 컷" for the ones that have a place and a time, and where one job's time goes per group. Text is words and short noun phrases.</summary>
public static class CausePages
{
    static readonly Dictionary<string, string> GroupNames = new()
    {
        [CauseKinds.Deadlock] = "교착", [CauseKinds.DeadlockRisk] = "교착 위험", [CauseKinds.BlockHold] = "block 점유",
        [CauseKinds.Detour] = "우회", [CauseKinds.LoadShare] = "주행 부하", [CauseKinds.TimeLoss] = "시간 손실",
    };

    /// <summary>Film cut around a finding: a 30 m square on (x, y), starting 60 s before atSec, 120 s long at 4x (a 30 s video), tilt 30°.</summary>
    internal static CutSpec CutFor(string title, double x, double y, double atSec, string id)
    {
        static double R2(double d) => Math.Round(d, 2);
        var name = title.Trim() is { Length: > 0 } t ? (t.Length > 60 ? t[..60] : t) : "원인";
        return new CutSpec(id, name, CutKind.Hold, Math.Max(0, Math.Floor(atSec - 60)), 120, new Rect(R2(x - 15), R2(y - 15), 30, 30), null, 0, 0, 0, 4, 30, 0, true, true);
    }

    // ---------------------------------------------------------------- data

    /// <summary>One confirm run of a version. Score ranks the outcome: a failed run, then deadlocks, then the busiest group.</summary>
    internal sealed record RunChoice(string Id, string Label, double Score);

    internal static IReadOnlyList<RunChoice> Runs(JobView v, JobInfo j)
    {
        var list = new List<RunChoice>();
        foreach (var id in j.ConfirmRunIds)
        {
            var rows = v.Rows.Where(r => r.RunId == id).ToList();
            int dl = rows.Count == 0 ? 0 : rows.Max(r => r.Deadlocks);
            bool bad = rows.Any(r => !r.Ok && !JobView.UtilOnly(r));
            double u = rows.Select(r => r.Utilization).Where(x => !double.IsNaN(x)).DefaultIfEmpty(0).Max();
            list.Add(new RunChoice(id, $"실행 {list.Count + 1}" + (dl > 0 ? $" · 교착 {dl}회" : "") + (bad ? " · 실패" : ""), (bad ? 1e6 : 0) + dl * 1e3 + u));
        }
        return list;
    }

    /// <summary>result.tsv and the .report of a run; null for a part that is gone (run folders purged) or unreadable.</summary>
    internal static (ResultSet? Result, ReportStats? Report) ReadRun(JobStore js, JobInfo j, string runId)
    {
        var dir = Path.Combine(js.ProjectDir(j.Id), "runs", runId);
        ResultSet? rs = null; ReportStats? rep = null;
        try { var f = Path.Combine(dir, "result.tsv"); if (File.Exists(f)) rs = ResultParser.Parse(f); }
        catch (Exception e) when (e is IOException or InvalidDataException or FormatException) { }
        try { var f = Path.Combine(dir, j.Model + ".report"); if (File.Exists(f)) rep = ResultParser.ParseReport(f); }
        catch (Exception e) when (e is IOException or InvalidDataException or FormatException) { }
        return (rs, rep);
    }

    static ProjectSettings? SettingsOf(JobStore js, JobInfo j)
    {
        try { var f = Path.Combine(js.ProjectDir(j.Id), JobPackage.SettingsInPackage); return File.Exists(f) ? SettingsFile.Load(f) : null; }
        catch (Exception e) when (e is IOException or FormatException) { return null; }
    }

    // ---------------------------------------------------------------- tab

    /// <summary>The tab for one version (?job=, else the latest selected, else the latest finished) and one of its decided-count runs (?run=, else the worst).</summary>
    internal static void Tab(StringBuilder sb, AnalysisPageData d, IQueryCollection q)
    {
        var a = d.A;
        var done = d.Jobs.Where(j => j.State == JobState.Done && j.Decided != null).OrderBy(j => a.Plans.FindIndex(p => p.Id == j.PlanId)).ThenBy(j => j.Version).ToList();
        if (done.Count == 0) { sb.Append("<div class=\"empty\">결과가 나온 버전이 없습니다.</div>"); return; }
        var cur = done.FirstOrDefault(j => j.Id == q["job"].ToString()) ?? AnalysisPages.Pick(done)!;
        var v = JobView.For(d.Store, cur);
        var runs = Runs(v, cur);
        var run = runs.FirstOrDefault(r => r.Id == q["run"].ToString()) ?? runs.MaxBy(r => r.Score);

        sb.Append("<div class=\"tb\"><form class=\"mini\"><input type=\"hidden\" name=\"tab\" value=\"cause\"><select name=\"job\" aria-label=\"버전\" onchange=\"this.form.submit()\">");
        foreach (var j in done) sb.Append($"<option value=\"{H(j.Id)}\"{(j.Id == cur.Id ? " selected" : "")}>{H(AnalysisPages.PlanName(a, j))} v{j.Version}{(j.Selected ? " · 선정" : "")}</option>");
        sb.Append("</select>");
        if (runs.Count > 0)
        {
            sb.Append("<select name=\"run\" aria-label=\"실행\" onchange=\"this.form.submit()\">");
            foreach (var r in runs) sb.Append($"<option value=\"{H(r.Id)}\"{(r.Id == run?.Id ? " selected" : "")}>{H(r.Label)}</option>");
            sb.Append("</select>");
        }
        sb.Append("<noscript><button>보기</button></noscript></form></div>");

        if (v.Layout == null || v.Input == null) { sb.Append("<div class=\"empty\">자료 없음</div>"); return; }
        var (rs, rep) = run == null ? (null, null) : ReadRun(d.Store, cur, run.Id);
        var ctx = CauseFrame.Context(v.Layout, v.Input, SettingsOf(d.Store, cur), rs, rep);
        IReadOnlyList<CauseItem> items = Array.Empty<CauseItem>(); IReadOnlyList<TimeBreakdown> breakdown = Array.Empty<TimeBreakdown>();
        var note = rs == null ? "실행 자료 없음 · 도면 기준만" : "";
        try
        {
            items = rs == null ? CauseAnalysis.Static(ctx) : CauseAnalysis.All(ctx);
            if (rs != null) breakdown = TimeLossCauses.Breakdown(ctx);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { note = "분석 오류: " + e.Message; } // an analyser meeting odd data must not take the page down
        Render(sb, a.Id, cur.Id, items, breakdown, v.Layout, note);
    }

    // ---------------------------------------------------------------- render

    static bool Place(CauseItem i) => i.X is { } x && i.Y is { } y && double.IsFinite(x) && double.IsFinite(y);

    internal static string Hms(double sec)
    {
        long s = (long)Math.Round(Math.Max(0, sec));
        return $"{s / 3600}:{s % 3600 / 60:00}:{s % 60:00}";
    }

    const string Css = @"<style>
.cz{display:grid;grid-template-columns:minmax(0,1fr);gap:14px;margin-top:6px}.cz.map{grid-template-columns:minmax(0,1fr) 420px}
.cz .box{margin-top:0}.cz .col{display:flex;flex-direction:column;gap:14px;min-width:0}.cz-m{position:sticky;top:0;align-self:start}.cz-m .mv{height:380px;flex:none}
.cz h3{margin:14px 0 4px;font-size:15px}.cz h3:first-child{margin-top:0}.cz h3 span{color:var(--sub2);font-weight:600;font-size:13px;margin-left:4px}
.cz ul{list-style:none;margin:0;padding:0}
.cz-r{display:flex;align-items:center;flex-wrap:wrap;gap:4px 10px;min-height:40px;padding:6px 8px;border-bottom:1px solid var(--line);border-radius:6px;font-size:14px}
.cz-r[data-i]{cursor:pointer}.cz-r.on{background:var(--blue-b)}.cz-r .tag{height:22px;font-size:12px;flex:none}
.cz-n{width:22px;height:22px;border-radius:50%;display:inline-grid;place-items:center;background:var(--gray-b);color:var(--sub2);font-size:12px;font-weight:700;flex:none}
.cz-r.on .cz-n{background:var(--blue);color:#fff}.cz-w{color:var(--sub2)}.cz-d{color:var(--sub2);flex:1 1 200px;min-width:0}
.cz-f{color:var(--ink);flex:1 1 200px;min-width:0}.cz-f em{font-style:normal;font-weight:600;color:var(--blue-d);margin-right:4px}
.cz-t{font-variant-numeric:tabular-nums;flex:none}.cz-r form{margin:0;flex:none}.cz-r .btn{min-height:28px;padding:0 10px;font-size:13px}
.cz-r.cz-ex{cursor:pointer}.cz-c{width:14px;flex:none;color:var(--sub2);font-weight:700;transition:transform .15s}.cz-r.open .cz-c{transform:rotate(90deg)}
.cz-x{display:none;flex:1 1 100%;gap:12px;align-items:flex-start;flex-wrap:wrap;padding:6px 0 4px;cursor:default}.cz-r.open .cz-x{display:flex}
.cz-nm{display:flex;flex-direction:column;gap:4px;min-width:120px}.cz-nm .chip{font-size:13px;white-space:nowrap}
.cz-k{display:inline-block;width:14px;height:6px;border-radius:3px;margin-right:6px;vertical-align:middle}.cz-k.p{background:#007aff}.cz-k.c{background:#d70015;width:8px;height:8px;border-radius:50%}.cz-k.b{background:none;border:2px solid #ff9500;width:12px;height:8px;border-radius:2px}
.cz-s{width:320px;height:240px;max-width:100%;background:var(--bg,#fff);border:1px solid var(--line);border-radius:6px}
.cm{cursor:pointer}.cm .rg{stroke:none}.cm.on .rg{stroke:#007aff;stroke-width:4;fill-opacity:.4}
.czb th,.czb td{padding:6px 8px;text-align:center}.czb th:first-child,.czb td:first-child{text-align:left}
@media(max-width:1300px){.cz.map{grid-template-columns:minmax(0,1fr)}.cz-m{position:static}}
</style>";

    const string Script = @"<script>(function(){
var rows=document.querySelectorAll('.cz-r[data-i]'),mk=document.querySelectorAll('.cm');
function pick(i,scroll){rows.forEach(function(r){r.classList.toggle('on',r.dataset.i===i);});mk.forEach(function(m){m.classList.toggle('on',m.dataset.i===i);});
 if(scroll){var r=document.querySelector('.cz-r[data-i=""'+i+'""]');if(r)r.scrollIntoView({block:'nearest'});}}
document.querySelectorAll('.cz-r').forEach(function(r){r.addEventListener('click',function(e){if(e.target.closest('button,a,form,.cz-x'))return;
 if(r.dataset.i)pick(r.dataset.i,false);
 if(r.dataset.ex){var o=r.classList.toggle('open');r.setAttribute('aria-expanded',o);}});});
mk.forEach(function(m){m.addEventListener('click',function(){pick(m.dataset.i,true);});});})();</script>";

    static readonly string[] SevColor = { "gray", "orange", "red" }, SevText = { "참고", "주의", "심각" };
    static readonly string[] SevFill = { "#8e8e93", "#ff9500", "#d70015" };

    /// <summary>The findings by kind, the map beside them, and the time breakdown. Takes the findings as a list so a page can be drawn from any of them.
    /// <paramref name="note"/> is a short line above (data missing, analysis error).</summary>
    internal static void Render(StringBuilder sb, string analysisId, string jobId, IReadOnlyList<CauseItem> items, IReadOnlyList<TimeBreakdown> breakdown,
        ASH.Core.Layout.Layout? layout, string note = "")
    {
        sb.Append(Css);
        if (note.Length > 0) sb.Append($"<p class=\"muted\" role=\"status\">{H(note)}</p>");
        var kinds = CauseKinds.Order.Concat(items.Select(i => i.Kind).Distinct().Except(CauseKinds.Order)).ToList();
        // marker numbers follow the order the rows are shown in
        var nums = new Dictionary<CauseItem, int>(ReferenceEqualityComparer.Instance);
        foreach (var k in kinds) foreach (var i in items.Where(i => i.Kind == k && Place(i))) nums[i] = nums.Count + 1;
        var bb = layout == null || nums.Count == 0 ? null : LayoutBounds(layout);

        if (items.Count > 0)
            sb.Append(Legend((SevFill[0], SevText[0]), (SevFill[1], SevText[1]), (SevFill[2], SevText[2]), ("#007aff", "경로"), ("#d70015", "CP"), ("#ff9500", "block")));
        sb.Append($"<div class=\"cz{(bb != null ? " map" : "")}\"><div class=\"col\">");
        if (items.Count == 0) sb.Append("<section class=\"box\"><div class=\"empty\">원인 없음</div></section>");
        else
        {
            sb.Append("<section class=\"box\">");
            foreach (var k in kinds)
            {
                var g = items.Where(i => i.Kind == k).ToList();
                if (g.Count == 0) continue;
                sb.Append($"<h3>{H(GroupNames.TryGetValue(k, out var gn) ? gn : k)}<span>{g.Count}</span></h3><ul>");
                foreach (var i in g) Row(sb, analysisId, jobId, i, nums.TryGetValue(i, out var n) ? n : 0, layout);
                sb.Append("</ul>");
            }
            sb.Append("</section>");
        }
        if (breakdown.Count > 0) Breakdown(sb, breakdown);
        sb.Append("</div>");
        if (bb != null)
        {
            sb.Append("<section class=\"box cz-m\"><div class=\"mv\">");
            Map(sb, layout!, bb.Value, nums);
            sb.Append("</div></section>");
        }
        sb.Append("</div>").Append(Script);
    }

    static void Row(StringBuilder sb, string analysisId, string jobId, CauseItem i, int n, ASH.Core.Layout.Layout? layout)
    {
        var sev = Math.Clamp(i.Severity, 0, 2);
        var refs = Resolve(layout, i.Refs);
        sb.Append($"<li class=\"cz-r{(refs != null ? " cz-ex" : "")}\"{(n > 0 ? $" data-i=\"{n}\"" : "")}{(refs != null ? " data-ex=\"1\" aria-expanded=\"false\"" : "")}>" +
            (refs != null ? "<span class=\"cz-c\" aria-hidden=\"true\">›</span>" : "") +
            (n > 0 ? $"<span class=\"cz-n\" aria-hidden=\"true\">{n}</span>" : "") +
            $"<span class=\"tag {SevColor[sev]}\">{SevText[sev]}</span><b>{H(i.Title)}</b>" +
            (i.Where.Length > 0 ? $"<span class=\"cz-w\">{H(i.Where)}</span>" : "") +
            (i.Detail.Length > 0 ? $"<span class=\"cz-d\">{H(i.Detail)}</span>" : "") +
            (i.Fix.Length > 0 ? $"<span class=\"cz-f\"><em>조치</em>{H(i.Fix)}</span>" : "") +
            (i.AtSec is { } at && double.IsFinite(at) ? $"<span class=\"cz-t\">{Hms(at)}</span>" : ""));
        if (i.AtSec is { } t && double.IsFinite(t) && Place(i))
            sb.Append($"<form method=\"post\" action=\"/analyses/{H(analysisId)}/cuts/cause\">" +
                $"<input type=\"hidden\" name=\"job\" value=\"{H(jobId)}\"><input type=\"hidden\" name=\"name\" value=\"{H(i.Title)}\">" +
                $"<input type=\"hidden\" name=\"x\" value=\"{F(i.X!.Value, "0.###")}\"><input type=\"hidden\" name=\"y\" value=\"{F(i.Y!.Value, "0.###")}\"><input type=\"hidden\" name=\"at\" value=\"{F(t, "0.#")}\">" +
                "<button class=\"btn\" title=\"이 장면의 컷을 만들고 영상 만들기를 엽니다\">영상 컷</button></form>");
        if (refs != null) Detail(sb, layout!, refs.Value);
        sb.Append("</li>");
    }

    // ---------------------------------------------------------------- accordion detail

    /// <summary>The layout objects a finding's Refs name (paths, CPs, blocks); null when none resolves.</summary>
    internal static (List<ASH.Core.Layout.LayoutPath> Paths, List<ASH.Core.Layout.LayoutCp> Cps, List<ASH.Core.Layout.LayoutBlock> Blocks)? Resolve(ASH.Core.Layout.Layout? l, IReadOnlyList<string>? refs)
    {
        if (l == null || refs == null || refs.Count == 0) return null;
        var set = new HashSet<string>(refs);
        var r = (Paths: l.Paths.Where(p => set.Contains(p.Name)).ToList(), Cps: l.Cps.Where(c => set.Contains(c.Name)).ToList(), Blocks: l.Blocks.Where(b => set.Contains(b.Name)).ToList());
        return r.Paths.Count + r.Cps.Count + r.Blocks.Count == 0 ? null : r;
    }

    static List<ASH.Core.Geometry.Point2> Sample(ASH.Core.Layout.LayoutPath path)
    {
        int steps = path.Segments.Any(s => s is ASH.Core.Geometry.ArcSeg) ? 16 : 1; double len = path.Length;
        return Enumerable.Range(0, steps + 1).Select(k => path.PointAt(len * k / steps)).ToList();
    }

    /// <summary>Names and a small map zoomed to the referenced objects: their paths thick blue, CPs as labelled dots, blocks outlined; other paths nearby thin grey.</summary>
    static void Detail(StringBuilder sb, ASH.Core.Layout.Layout l, (List<ASH.Core.Layout.LayoutPath> Paths, List<ASH.Core.Layout.LayoutCp> Cps, List<ASH.Core.Layout.LayoutBlock> Blocks) r)
    {
        var pts = new List<ASH.Core.Geometry.Point2>();
        var psamp = r.Paths.Select(p => (p, Sample(p))).ToList();
        foreach (var (_, s) in psamp) pts.AddRange(s);
        pts.AddRange(r.Cps.Select(c => c.Position));
        foreach (var b in r.Blocks) { pts.Add(b.Rect.Min); pts.Add(b.Rect.Max); }
        double x0 = pts.Min(q => q.X), x1 = pts.Max(q => q.X), y0 = pts.Min(q => q.Y), y1 = pts.Max(q => q.Y);
        double span = Math.Max(Math.Max(x1 - x0, y1 - y0), 8), pad = span * 0.2 + 1;
        double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, half = span / 2 + pad; // square view, so the map keeps its shape
        double vx0 = cx - half, vx1 = cx + half, vy0 = cy - half, vy1 = cy + half;
        string X(double x) => F(x, "0.###");
        string Y(double y) => F(-y, "0.###"); // y up in the model, down on screen
        double fs = half / 9, dot = half / 28;
        sb.Append("<div class=\"cz-x\"><div class=\"cz-nm\">");
        foreach (var p in r.Paths) sb.Append($"<span class=\"chip\"><i class=\"cz-k p\"></i>경로 {H(p.Name)}</span>");
        foreach (var c in r.Cps) sb.Append($"<span class=\"chip\"><i class=\"cz-k c\"></i>CP {H(c.Name)}</span>");
        foreach (var b in r.Blocks) sb.Append($"<span class=\"chip\"><i class=\"cz-k b\"></i>block {H(b.Name)}</span>");
        sb.Append($"</div><svg class=\"cz-s\" viewBox=\"{X(vx0)} {Y(vy1)} {F(2 * half, "0.###")} {F(2 * half, "0.###")}\" preserveAspectRatio=\"xMidYMid meet\" role=\"img\" aria-label=\"관련 경로·CP 지도\">");
        bool In(ASH.Core.Geometry.Point2 q) => q.X >= vx0 && q.X <= vx1 && q.Y >= vy0 && q.Y <= vy1;
        var mine = new HashSet<ASH.Core.Layout.LayoutPath>(r.Paths);
        foreach (var p in l.Paths.Where(p => !mine.Contains(p)))
        {
            var s = Sample(p); if (!s.Any(In)) continue; // only what shows in the view
            sb.Append($"<polyline points=\"{string.Join(" ", s.Select(q => X(q.X) + "," + Y(q.Y)))}\" fill=\"none\" stroke=\"#c7c7cc\" stroke-width=\"2\" vector-effect=\"non-scaling-stroke\"/>");
        }
        foreach (var b in l.Blocks)
        {
            double bx0 = Math.Min(b.Rect.Min.X, b.Rect.Max.X), bx1 = Math.Max(b.Rect.Min.X, b.Rect.Max.X), by0 = Math.Min(b.Rect.Min.Y, b.Rect.Max.Y), by1 = Math.Max(b.Rect.Min.Y, b.Rect.Max.Y);
            bool hot = r.Blocks.Contains(b);
            if (!hot && (bx1 < vx0 || bx0 > vx1 || by1 < vy0 || by0 > vy1)) continue;
            sb.Append($"<rect x=\"{X(bx0)}\" y=\"{Y(by1)}\" width=\"{F(bx1 - bx0, "0.###")}\" height=\"{F(by1 - by0, "0.###")}\" " + (hot
                ? "fill=\"#ff9500\" fill-opacity=\".12\" stroke=\"#ff9500\" stroke-width=\"3\""
                : "fill=\"none\" stroke=\"#d1d1d6\" stroke-width=\"1\"") + $" vector-effect=\"non-scaling-stroke\"><title>{H(b.Name)}</title></rect>");
            if (hot) sb.Append($"<text x=\"{X(bx0)}\" y=\"{Y(by1 + fs * 0.3)}\" font-size=\"{F(fs, "0.###")}\" fill=\"#c93400\" font-weight=\"700\">{H(b.Name)}</text>");
        }
        foreach (var (p, s) in psamp)
        {
            sb.Append($"<polyline points=\"{string.Join(" ", s.Select(q => X(q.X) + "," + Y(q.Y)))}\" fill=\"none\" stroke=\"#007aff\" stroke-width=\"6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" stroke-opacity=\".75\" vector-effect=\"non-scaling-stroke\"><title>{H(p.Name)}</title></polyline>");
            var m = p.PointAt(p.Length / 2);
            sb.Append($"<text x=\"{X(m.X)}\" y=\"{Y(m.Y + fs * 0.4)}\" font-size=\"{F(fs, "0.###")}\" fill=\"#0040dd\" font-weight=\"700\" text-anchor=\"middle\">{H(p.Name)}</text>");
        }
        foreach (var c in r.Cps)
            sb.Append($"<circle cx=\"{X(c.Position.X)}\" cy=\"{Y(c.Position.Y)}\" r=\"{F(dot, "0.###")}\" fill=\"#d70015\" stroke=\"#fff\" stroke-width=\"1\" vector-effect=\"non-scaling-stroke\"><title>{H(c.Name)}</title></circle>" +
                $"<text x=\"{X(c.Position.X + dot * 1.6)}\" y=\"{Y(c.Position.Y - dot * 0.6)}\" font-size=\"{F(fs, "0.###")}\" fill=\"#d70015\" font-weight=\"700\">{H(c.Name)}</text>");
        sb.Append("</svg></div>");
    }

    /// <summary>The layout's paths and blocks in grey, one numbered marker per finding with a place (clicking it picks its row).</summary>
    static void Map(StringBuilder sb, ASH.Core.Layout.Layout l, (double X0, double X1, double Y0, double Y1) bb, Dictionary<CauseItem, int> nums)
    {
        // x from the layout's left, y down from its top, as the 병목 지도 draws it
        string X(double x) => F(x - bb.X0, "0.###");
        string Y(double y) => F(bb.Y1 - y, "0.###");
        double w = bb.X1 - bb.X0, h = bb.Y1 - bb.Y0, p = Math.Max(1e-6, 0.05 * Math.Max(w, h));
        sb.Append($"<svg class=\"bm\" viewBox=\"{F(-p, "0.###")} {F(-p, "0.###")} {F(w + 2 * p, "0.###")} {F(h + 2 * p, "0.###")}\" preserveAspectRatio=\"xMidYMid meet\" role=\"img\" aria-label=\"원인 지도\">");
        foreach (var b in l.Blocks)
        {
            double x0 = Math.Min(b.Rect.Min.X, b.Rect.Max.X), x1 = Math.Max(b.Rect.Min.X, b.Rect.Max.X), y0 = Math.Min(b.Rect.Min.Y, b.Rect.Max.Y), y1 = Math.Max(b.Rect.Min.Y, b.Rect.Max.Y);
            sb.Append($"<rect x=\"{X(x0)}\" y=\"{Y(y1)}\" width=\"{F(x1 - x0, "0.###")}\" height=\"{F(y1 - y0, "0.###")}\" fill=\"none\" stroke=\"#c7c7cc\" stroke-width=\"1\" vector-effect=\"non-scaling-stroke\"><title>{H(b.Name)}</title></rect>");
        }
        foreach (var path in l.Paths)
        {
            int steps = path.Segments.Any(s => s is ASH.Core.Geometry.ArcSeg) ? 16 : 1; double len = path.Length;
            var pts = Enumerable.Range(0, steps + 1).Select(k => path.PointAt(len * k / steps)).Select(pt => X(pt.X) + "," + Y(pt.Y));
            sb.Append($"<polyline points=\"{string.Join(" ", pts)}\" fill=\"none\" stroke=\"#aeaeb2\" stroke-width=\"3\" stroke-linecap=\"round\" vector-effect=\"non-scaling-stroke\"><title>{H(path.Name)}</title></polyline>");
        }
        foreach (var (i, n) in nums.OrderByDescending(kv => kv.Value).Select(kv => (kv.Key, kv.Value))) // the first drawn last, on top
        {
            string cx = X(i.X!.Value), cy = Y(i.Y!.Value);
            var fill = SevFill[Math.Clamp(i.Severity, 0, 2)];
            sb.Append($"<g class=\"mk cm\" data-i=\"{n}\" data-x=\"{cx}\" data-y=\"{cy}\" transform=\"translate({cx} {cy}) scale({F(Math.Max(1e-6, w * 1.1) / 400, "0.######")})\"><title>{n}. {H(i.Title)}</title>" +
                $"<circle class=\"rg\" r=\"20\" fill=\"{fill}\" fill-opacity=\"0.25\"/><circle r=\"11\" fill=\"{fill}\"/>" +
                $"<text y=\"4\" text-anchor=\"middle\" font-size=\"12\" font-weight=\"700\" fill=\"#fff\">{n}</text></g>");
        }
        sb.Append("</svg>").Append(AnalysisPages.FitScript);
    }

    static readonly (string Name, string Css)[] Parts = { ("주행", "u-ld"), ("block 대기", "u-bx"), ("CP 대기", "u-rp"), ("상하차", "u-hd"), ("기타", "u-id") };

    /// <summary>Per group, seconds of one job (after warmup): a stacked bar and the numbers.</summary>
    static void Breakdown(StringBuilder sb, IReadOnlyList<TimeBreakdown> rows)
    {
        double[] V(TimeBreakdown r) => new[] { r.PureSec, r.BlockWaitSec, r.QueueSec, r.HandlingSec, r.OtherSec };
        var max = rows.Max(r => Math.Max(r.JobSec, V(r).Where(x => x > 0).Sum()));
        sb.Append("<section class=\"box\"><div class=\"box-head\"><h3>작업당 시간</h3><span class=\"lg\">");
        foreach (var (name, css) in Parts) sb.Append($"<span><i class=\"{css}\"></i>{name}</span>");
        sb.Append("</span></div><table class=\"czb\"><thead><tr><th>그룹</th><th>작업</th><th></th>");
        foreach (var (name, _) in Parts) sb.Append($"<th>{name}</th>");
        sb.Append("<th>합계</th></tr></thead><tbody>");
        foreach (var r in rows)
        {
            sb.Append($"<tr><td>{H(r.Group)}</td><td>{r.Jobs.ToString(CultureInfo.InvariantCulture)}</td><td><span class=\"sbar\"><span>");
            var v = V(r);
            for (int k = 0; k < v.Length; k++)
                if (v[k] > 0 && max > 0) sb.Append($"<i class=\"{Parts[k].Css}\" style=\"width:{F(v[k] / max * 100, "0.##")}%\" title=\"{Parts[k].Name} {F(v[k], "0")}초\"></i>");
            sb.Append("</span></span></td>");
            foreach (var x in v) sb.Append($"<td class=\"num\">{F(x, "0")}</td>");
            sb.Append($"<td class=\"num\"><b>{F(r.JobSec, "0")}</b></td></tr>");
        }
        sb.Append("</tbody></table><div class=\"t4\">작업 1건당 초</div></section>");
    }
}
