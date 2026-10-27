using System.Globalization;
using System.Text;
using ASH.Core.Cause;
using ASH.Core.Runner;
using ASH.Core.Settings;
using static ASH.Server.JobPages;

namespace ASH.Server;

/// <summary>The cards under the 비교 tab's main table: one dense table per topic (materials, blocks, time split, causes, production limit),
/// rows = groups / materials / blocks, columns = the chosen versions; the best value of a row is green, the worst red (only when they differ).</summary>
internal static class CompareDetail
{
    internal const string Css = @"<style>
.cmpg{display:grid;grid-template-columns:repeat(auto-fit,minmax(380px,1fr));gap:14px;margin-top:14px}.cmpg .box{margin:0;min-width:0}
.cmpg h3{margin:0 0 6px;font-size:15px}.cmpg h3 span{color:var(--sub2);font-weight:600;font-size:12px;margin-left:6px}
.cmp{width:100%;border-collapse:collapse;font-size:13px}.cmp th,.cmp td{padding:4px 8px;border-bottom:1px solid var(--line);text-align:center;white-space:nowrap}
.cmp th:first-child{text-align:left;font-weight:500;max-width:150px;overflow:hidden;text-overflow:ellipsis}.cmp thead th{font-size:12px;color:var(--sub2)}
td.best{background:var(--green-b);color:#1c5c2e;font-weight:600}td.worst{background:var(--red-b);color:#8f000e;font-weight:600}
</style>";

    /// <summary>One cell: the value to rank (null = none) and its text.</summary>
    internal readonly record struct Cell(double? V, string Text);
    internal sealed record CRow(string Label, IReadOnlyList<Cell> Cells);

    static Cell N(double d, string fmt = "0.#") => double.IsNaN(d) ? new(null, "-") : new(d, F(d, fmt));
    static readonly Cell None = new(null, "-");

    /// <summary>The cells' classes: best / worst when at least two different values exist (lower is better unless <paramref name="higher"/>).</summary>
    internal static string[] Marks(IReadOnlyList<Cell> cells, bool higher)
    {
        var r = new string[cells.Count];
        var vs = cells.Where(c => c.V != null).Select(c => Math.Round(c.V!.Value, 3)).ToList();
        if (vs.Distinct().Count() < 2) return r;
        double lo = vs.Min(), hi = vs.Max();
        for (int i = 0; i < cells.Count; i++)
            if (cells[i].V is { } v) { v = Math.Round(v, 3); r[i] = v == lo ? higher ? "worst" : "best" : v == hi ? higher ? "best" : "worst" : ""; }
        return r;
    }

    static void Card(StringBuilder sb, string title, string note, IReadOnlyList<Col> cols, IEnumerable<CRow> rows, bool higher = false, int max = 12, string empty = "없음")
    {
        var list = rows.Where(r => r.Cells.Any(c => c.V != null || c.Text != "-")).ToList();
        // the rows that differ most first, so a long list shows what matters
        if (list.Count > max) list = list.OrderByDescending(r => { var v = r.Cells.Where(c => c.V != null).Select(c => c.V!.Value).ToList(); return v.Count < 2 ? 0 : v.Max() - v.Min(); }).Take(max).ToList();
        sb.Append($"<section class=\"box\"><h3>{H(title)}{(note.Length > 0 ? $"<span>{H(note)}</span>" : "")}</h3>");
        if (list.Count == 0) { sb.Append($"<p class=\"muted\">{H(empty)}</p></section>"); return; }
        sb.Append("<div class=\"scroll\"><table class=\"cmp\"><thead><tr><th></th>");
        foreach (var c in cols) sb.Append($"<th>{H(c.Name)} v{c.Job.Version}</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var r in list)
        {
            var mk = Marks(r.Cells, higher);
            sb.Append($"<tr><th title=\"{H(r.Label)}\">{H(r.Label)}</th>");
            for (int i = 0; i < r.Cells.Count; i++) sb.Append($"<td class=\"num{(string.IsNullOrEmpty(mk[i]) ? "" : " " + mk[i])}\">{H(r.Cells[i].Text)}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table></div></section>");
    }

    internal sealed record Col(JobInfo Job, string Name, AnalysisPages.Metrics? M, IReadOnlyList<TimeBreakdown> Time, IReadOnlyList<CauseItem>? Causes, LimitInfo? Limit);

    /// <summary>The worst run of the version (as the 원인 tab picks it): its time split and cause findings; empty / null when the run is gone or the analysers fail.</summary>
    static (IReadOnlyList<TimeBreakdown>, IReadOnlyList<CauseItem>?) Causes(JobStore js, JobInfo j, JobView v)
    {
        try
        {
            if (v.Layout == null || v.Input == null) return (Array.Empty<TimeBreakdown>(), null);
            var run = CausePages.Runs(v, j).MaxBy(r => r.Score);
            if (run == null) return (Array.Empty<TimeBreakdown>(), null);
            var (rs, rep) = CausePages.ReadRun(js, j, run.Id);
            if (rs == null) return (Array.Empty<TimeBreakdown>(), null);
            ProjectSettings? s = null;
            try { var f = Path.Combine(js.ProjectDir(j.Id), JobPackage.SettingsInPackage); if (File.Exists(f)) s = SettingsFile.Load(f); }
            catch (Exception e) when (e is IOException or FormatException) { }
            var ctx = CauseFrame.Context(v.Layout, v.Input, s, rs, rep);
            return (TimeLossCauses.Breakdown(ctx), CauseAnalysis.All(ctx));
        }
        catch (Exception e) when (e is not OutOfMemoryException) { return (Array.Empty<TimeBreakdown>(), null); }
    }

    internal static void Append(StringBuilder sb, AnalysisPageData d, IReadOnlyList<AnalysisPages.CompareColumn> cc)
    {
        var cols = cc.Select(c =>
        {
            if (c.M == null) return new Col(c.Job, c.PlanName, null, Array.Empty<TimeBreakdown>(), null, null);
            var (t, ca) = Causes(d.Store, c.Job, c.M.View);
            return new Col(c.Job, c.PlanName, c.M, t, ca, LimitWorker.Read(d.Store, c.Job.Id));
        }).ToList();
        IEnumerable<CRow> Rows(IEnumerable<string> keys, Func<Col, string, Cell> cell) => keys.Select(k => new CRow(k, cols.Select(c => cell(c, k)).ToList()));
        IEnumerable<string> Keys(Func<AnalysisPages.Metrics, IEnumerable<string>> f) => cols.Where(c => c.M != null).SelectMany(c => f(c.M!)).Distinct(StringComparer.Ordinal);
        MaterialView? Mat(Col c, string k) => c.M?.View.Materials.FirstOrDefault(x => x.Name == k);
        Cell MatCell(Col c, string k, Func<MaterialView, double> f, string fmt = "0.#") => Mat(c, k) is { } m ? N(f(m), fmt) : None;
        var mats = Keys(m => m.View.Materials.Select(x => x.Name)).ToList();

        sb.Append(Legend(("var(--green-b)", "최선"), ("var(--red-b)", "최악")));
        sb.Append("<div class=\"cmpg\">");

        // production limit (higher = better) and deadlocks / causes (lower = better)
        Card(sb, "생산량 한계 (T/T)", "높을수록 좋음", cols, new[]
        {
            new CRow("통과", cols.Select(c => c.Limit == null ? None : c.Limit.Passed is { } p ? new Cell(p, "+" + p + "%") : new Cell(-1, "미통과")).ToList()),
        }, higher: true, empty: "미계산");
        var kinds = CauseKinds.Order.Concat(cols.Where(c => c.Causes != null).SelectMany(c => c.Causes!.Select(x => x.Kind)).Distinct().Except(CauseKinds.Order)).Where(k => k != CauseKinds.Deadlock);
        var summary = new List<CRow> { new("교착(회)", cols.Select(c => c.M == null ? None : N(c.M.View.Deadlocks, "0")).ToList()) };
        summary.AddRange(kinds.Select(k => new CRow(KindName(k), cols.Select(c => c.Causes == null ? None : N(c.Causes.Count(x => x.Kind == k), "0")).ToList())));
        Card(sb, "원인 수", "최악 실행 기준", cols, summary);

        Card(sb, "자재 품절(초)", "", cols, Rows(mats, (c, k) => MatCell(c, k, m => m.StopSec, "0")).Where(r => r.Cells.Any(x => x.V > 0)));
        Card(sb, "리드타임 평균(초)", "호출~하역", cols, Rows(mats, (c, k) => MatCell(c, k, m => m.DelivAvg)));
        Card(sb, "리드타임 최대(초)", "호출~하역", cols, Rows(mats, (c, k) => MatCell(c, k, m => m.DelivMax)));
        Card(sb, "최소 재고", "높을수록 좋음", cols, Rows(mats, (c, k) => MatCell(c, k, m => m.StockMin)), higher: true);
        Card(sb, "공급 부족(실제/예상)", "", cols, Rows(mats, (c, k) => Mat(c, k) is { } m ? new Cell(Math.Max(0, m.Expected - m.Supplies), $"{F(m.Supplies, "0.#")}/{F(m.Expected, "0.#")}") : None));

        var blocks = Keys(m => m.View.Blocks.Select(x => x.Name)).Select(k => new CRow(k, cols.Select(c => c.M?.View.Blocks.FirstOrDefault(x => x.Name == k) is { } b ? N(b.AvgWaitSec) : None).ToList()))
            .Where(r => r.Cells.Any(x => x.V > 0)).OrderByDescending(r => r.Cells.Max(x => x.V ?? 0)).ToList();
        Card(sb, "block 대기 상위(초)", "평균 대기", cols, blocks, max: 8);

        var tgroups = cols.SelectMany(c => c.Time.Select(t => t.Group)).Distinct(StringComparer.Ordinal).ToList();
        void TimeCard(string title, Func<TimeBreakdown, double> f) =>
            Card(sb, title, "최악 실행 기준", cols, Rows(tgroups, (c, g) => c.Time.FirstOrDefault(x => x.Group == g) is { } t ? N(f(t)) : None));
        if (tgroups.Count > 0)
        {
            TimeCard("작업 1회 시간(초)", t => t.JobSec);
            TimeCard("작업 1회 block 대기(초)", t => t.BlockWaitSec);
            TimeCard("작업 1회 CP 대기(초)", t => t.QueueSec);
        }
        sb.Append("</div>");
    }

    static string KindName(string k) => k switch
    {
        CauseKinds.DeadlockRisk => "교착 위험", CauseKinds.BlockHold => "block 점유",
        CauseKinds.Detour => "우회", CauseKinds.LoadShare => "주행 부하", CauseKinds.TimeLoss => "시간 손실", _ => k,
    };
}
