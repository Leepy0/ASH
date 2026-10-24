using ASH.Core;
using ASH.Core.Input;

namespace ASH.Server;

internal static class FilePages
{
    const int Max = 200;
    static string H(string? t) => JobPages.H(t);

    public static string Files(FileRoutes.FileQuery f, JobStore js, AnalysisStore an)
    {
        var analyses = an.All();
        var byId = analyses.ToDictionary(a => a.Id, StringComparer.Ordinal);
        var all = FileRoutes.Search(f, js.All().Where(j => j.Finished).ToList(), analyses, id => FileIndex.Ensure(js, id));
        var sb = new System.Text.StringBuilder();
        sb.Append("<h1>자료 보관함</h1><form method=\"get\" action=\"/files\" class=\"filters\">")
          .Append($"<input name=\"q\" value=\"{H(f.Q)}\" placeholder=\"분석·안·메모·보낸 사람·모델\"> ")
          .Append($"<input name=\"material\" value=\"{H(f.Material)}\" placeholder=\"자재 코드\"> ")
          .Append($"<input type=\"date\" name=\"from\" value=\"{H(Day(f.FromUtc, 0))}\"> ~ <input type=\"date\" name=\"to\" value=\"{H(Day(f.ToUtc, -1))}\"> ")
          .Append(FileRoutes.TagFilterHtml(f, an)).Append("<button type=\"submit\">검색</button></form>");
        if (all.Count == 0) return JobPages.Page("자료 보관함", sb.Append("<p>조건에 맞는 작업이 없습니다.</p>").ToString());
        sb.Append("<table class=\"sort\"><thead><tr>").Append(JobPages.Th("분석 › 안")).Append(JobPages.Th("날짜")).Append(JobPages.Th("보낸 사람")).Append(JobPages.Th("모델"))
          .Append("<th>파일</th><th></th></tr></thead><tbody>");
        foreach (var j in all.Take(Max))
        {
            var a = j.AnalysisId != null && byId.TryGetValue(j.AnalysisId, out var x) ? x : null;
            var title = a != null ? $"{a.Name} › {a.Plans.FirstOrDefault(p => p.Id == j.PlanId)?.Name ?? "?"} v{j.Version}" : $"(미분류) {(j.Project.Length > 0 ? j.Project : j.Id)} / {j.Plan}";
            var hits = f.Material.Length == 0 ? "" : string.Join(" ", FileIndex.Ensure(js, j.Id).Where(m => m.Contains(f.Material, StringComparison.CurrentCultureIgnoreCase)).Take(5).Select(m => $"<small>{H(m)}</small>"));
            string Link(string name, string label, bool ok) => ok ? $"<a href=\"/jobs/{H(j.Id)}/files/{name}\">{label}</a>" : $"<span style=\"color:#999\">{label}</span>";
            var proj = js.ProjectDir(j.Id);
            sb.Append($"<tr><td><a href=\"/jobs/{H(j.Id)}\">{H(title)}</a> {hits}</td><td>{H(SeoulTime.Text(j.CreatedUtc))}</td><td>{H(j.Submitter)}</td><td>{H(j.Model)}</td><td>")
              .Append(Link("input.xlsx", "xlsx", j.Files.Any(x => x.Path == CellDump.FileName))).Append(' ')
              .Append(Link("drawing.dxf", "dxf", File.Exists(Path.Combine(proj, "drawing.dxf")))).Append(' ')
              .Append(Link("drawing.dwg", "dwg", File.Exists(Path.Combine(proj, "drawing.dwg")))).Append("</td><td>")
              .Append(FileRoutes.ContinueLink(j).Replace(">자료 받기<", ">이어서 작업(zip)<")).Append("</td></tr>");
        }
        sb.Append("</tbody></table>");
        if (all.Count > Max) sb.Append($"<p>{all.Count}건 중 {Max}건만 보입니다. 더 좁혀 보세요.</p>");
        return JobPages.Page("자료 보관함", sb.Append(JobPages.SortScript).ToString());
    }

    static string Day(DateTime? utc, int addDays) => utc is { } u ? SeoulTime.FromUtc(u).AddDays(addDays).ToString("yyyy-MM-dd") : "";
}
