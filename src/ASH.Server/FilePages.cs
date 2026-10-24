using ASH.Core;
using ASH.Core.Input;

namespace ASH.Server;

internal static class FilePages
{
    const int Max = 200;
    static string H(string? t) => JobPages.H(t);

    // screen-only rules: the filter bar on one line, the results table in a box
    const string Style = @"<style>
.fbar{display:flex;align-items:center;flex-wrap:wrap;gap:8px 10px;margin-top:0;padding:10px 14px}
.fbar h1{font-size:20px;margin:0 6px 0 0;white-space:nowrap}
.fbar input,.fbar select{font:inherit;font-size:14px;min-height:34px;padding:5px 9px;border:1px solid var(--line2);border-radius:8px;background:#fff;color:var(--ink);width:auto;max-width:none}
.fbar input[name=q]{width:240px}.fbar input[name=material]{width:140px}.fbar input[type=date]{width:150px}
.fbar label{display:inline-flex;align-items:center;gap:6px;font-size:13px;color:var(--sub2)}
.fbar .btn.pri{margin-left:auto}
.fbar+.empty{margin-top:14px}
.tbl{padding:0;overflow:auto}.tbl table{width:100%}
.tbl td{white-space:nowrap}.tbl td.nm{white-space:normal}
.tbl .chips{display:flex;flex-wrap:wrap;gap:4px;margin-top:4px}
.tbl .dl{display:flex;gap:6px;flex-wrap:wrap}
.tbl .dl .btn{min-height:34px;padding:0 10px;font-size:13px}
.fcount{margin:8px 2px 0}
</style>";

    public static string Files(FileRoutes.FileQuery f, JobStore js, AnalysisStore an)
    {
        var analyses = an.All();
        var byId = analyses.ToDictionary(a => a.Id, StringComparer.Ordinal);
        var all = FileRoutes.Search(f, js.All().Where(j => j.Finished).ToList(), analyses, id => FileIndex.Ensure(js, id));
        var sb = new System.Text.StringBuilder(Style);
        sb.Append("<form method=\"get\" action=\"/files\" class=\"box fbar\"><h1>보관함</h1>")
          .Append($"<input name=\"q\" value=\"{H(f.Q)}\" placeholder=\"분석·안·메모·보낸 사람·모델\" aria-label=\"검색\"> ")
          .Append($"<input name=\"material\" value=\"{H(f.Material)}\" placeholder=\"자재 코드\" aria-label=\"자재 코드\"> ")
          .Append($"<span class=\"t3\">기간</span> <input type=\"date\" name=\"from\" value=\"{H(Day(f.FromUtc, 0))}\" aria-label=\"시작일\"> <span class=\"t4\">~</span> <input type=\"date\" name=\"to\" value=\"{H(Day(f.ToUtc, -1))}\" aria-label=\"끝일\"> ")
          .Append(FileRoutes.TagFilterHtml(f, an))
          .Append("<button class=\"btn pri\" type=\"submit\">거르기</button></form>");
        if (all.Count == 0) return JobPages.Page("보관함", sb.Append("<div class=\"empty\">조건에 맞는 작업이 없습니다.</div>").ToString(), null, "files");
        sb.Append("<div class=\"box tbl\"><table class=\"sort\"><thead><tr>").Append(JobPages.Th("분석·안")).Append(JobPages.Th("날짜")).Append(JobPages.Th("보낸이")).Append(JobPages.Th("모델"))
          .Append("<th>파일</th><th>이어서</th></tr></thead><tbody>");
        foreach (var j in all.Take(Max))
        {
            var a = j.AnalysisId != null && byId.TryGetValue(j.AnalysisId, out var x) ? x : null;
            var title = a != null ? $"{a.Name} › {a.Plans.FirstOrDefault(p => p.Id == j.PlanId)?.Name ?? "?"} v{j.Version}" : $"(미분류) {(j.Project.Length > 0 ? j.Project : j.Id)} / {j.Plan}";
            // matching codes first, then the first three; the rest collapse into "외 n"
            var mats = FileIndex.Ensure(js, j.Id).OrderBy(m => m.Contains(f.Material, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1).ToList();
            var chips = mats.Count == 0 ? "" : "<div class=\"chips\">" + string.Concat(mats.Take(3).Select(m => $"<span class=\"tag gray\">{H(m)}</span>")) +
                (mats.Count > 3 ? $"<span class=\"tag gray\">외 {mats.Count - 3}</span>" : "") + "</div>";
            var hasCells = j.Files.Any(fl => fl.Path == CellDump.FileName);
            var proj = js.ProjectDir(j.Id);
            string Dl(string name, string label, bool ok) => ok ? $"<a class=\"btn\" href=\"/jobs/{H(j.Id)}/files/{name}\">{label}</a>" : $"<span class=\"btn dis\">{label}</span>";
            sb.Append($"<tr><td class=\"nm\"><a href=\"/jobs/{H(j.Id)}\">{H(title)}</a>{chips}</td><td class=\"t4\">{H(SeoulTime.Text(j.CreatedUtc))}</td><td>{H(j.Submitter)}</td><td>{H(j.Model)}</td>")
              .Append("<td><div class=\"dl\">").Append(Dl("input.xlsx", "xlsx", hasCells)).Append(Dl("drawing.dxf", "dxf", File.Exists(Path.Combine(proj, "drawing.dxf"))))
              .Append(Dl("drawing.dwg", "dwg", File.Exists(Path.Combine(proj, "drawing.dwg")))).Append("</div></td>")
              .Append("<td>").Append(Dl("continue.zip", "이어서 작업(zip)", hasCells)).Append("</td></tr>");
        }
        sb.Append("</tbody></table></div>");
        sb.Append($"<p class=\"t4 fcount\">{all.Count}건{(all.Count > Max ? $" 중 {Max}건만 보입니다. 더 좁혀 보세요." : "")}</p>");
        return JobPages.Page("보관함", sb.ToString(), JobPages.SortScript, "files");
    }

    static string Day(DateTime? utc, int addDays) => utc is { } u ? SeoulTime.FromUtc(u).AddDays(addDays).ToString("yyyy-MM-dd") : "";
}
