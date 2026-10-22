using System.Globalization;
using System.Text;
using ASH.Core.Analysis;
using static ASH.Server.JobPages;

namespace ASH.Server;

/// <summary>The analysis pages: the list (/analyses), one analysis with its plans' versions and the compare table of the selected ones
/// (/analyses/{id}), the jobs not filed anywhere (/analyses/unclassified), and the "where is this job filed" header of a job page.</summary>
public static class AnalysisPages
{
    /// <summary>Result numbers of a finished job, taken from its JobView (the same ones the 결론 tab shows). Util = the busiest group, Stop = all materials.</summary>
    internal sealed record Metrics(int? Total, double Util, double Stop, JobView View);

    internal static Metrics? MetricsOf(JobStore js, JobInfo j)
    {
        if (j.State != JobState.Done || j.Decided == null) return null;
        var v = JobView.For(js, j);
        if (v.Input == null) return null;
        var utils = v.Groups.Select(g => g.Util).Where(u => !double.IsNaN(u)).ToList();
        var stops = v.Materials.Select(m => m.StopSec).Where(s => !double.IsNaN(s)).ToList();
        return new Metrics(j.Decided.Values.Sum(), utils.Count == 0 ? double.NaN : utils.Max(), stops.Count == 0 ? double.NaN : stops.Sum(), v);
    }

    static string Num(double d, string fmt = "0.#") => F(d, fmt);

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
            sb.Append($"<div class=\"crumb\"><a href=\"/analyses/{H(a.Id)}\">{H(a.Name)}</a> › {H(p.Name)} › v{j.Version}{(j.VersionNote.Length > 0 ? " · " + H(j.VersionNote) : "")}{(j.Selected ? " <span class=\"tag\">선정</span>" : "")}</div>");
            sb.Append("<div class=\"mini\">").Append(MoveForm(j, analyses, "/jobs/" + j.Id, "옮기기")).Append(SelectForm(j, "/jobs/" + j.Id)).Append("</div>");
            sb.Append("<div class=\"help\">다른 안으로 옮기면 그 안의 다음 버전이 됩니다.</div>");
        }
        sb.Append("</div>");
    }

    static string SelectForm(JobInfo j, string back) =>
        $"<form method=\"post\" action=\"/jobs/{H(j.Id)}/select\" class=\"mini\"><input type=\"hidden\" name=\"on\" value=\"{(j.Selected ? 0 : 1)}\"><input type=\"hidden\" name=\"back\" value=\"{H(back)}\">" +
        $"<button>{(j.Selected ? "선정 해제" : "선정")}</button></form>";

    public static string Index(IReadOnlyList<AnalysisInfo> analyses, IReadOnlyList<JobInfo> jobs)
    {
        var sb = new StringBuilder("<h1>분석</h1>");
        sb.Append("<p class=\"muted\">분석은 한 가지 검토(예: 라인 증설)이고, 그 안에 여러 안(A안, B안)이 있습니다. 같은 안을 고쳐 다시 보낼 때마다 버전(v1, v2…)이 올라갑니다.</p>");
        int loose = jobs.Count(j => j.AnalysisId == null);
        sb.Append($"<p><a href=\"/analyses/unclassified\">분석에 넣지 않은 작업 {loose}건 →</a></p>");
        sb.Append("<div class=\"scroll\"><table><tr><th>분석</th><th>메모</th><th>안(개)</th><th>버전(개)</th><th>선정(개)</th><th>마지막 보낸 시각(서울)</th></tr>");
        foreach (var a in analyses.OrderByDescending(x => jobs.Where(j => j.AnalysisId == x.Id).Select(j => j.CreatedUtc).DefaultIfEmpty(x.CreatedUtc).Max()))
        {
            var js = jobs.Where(j => j.AnalysisId == a.Id).ToList();
            sb.Append($"<tr><td><a href=\"/analyses/{H(a.Id)}\">{H(a.Name)}</a></td><td>{H(a.Note)}</td><td class=\"num\">{a.Plans.Count}</td><td class=\"num\">{js.Count}</td><td class=\"num\">{js.Count(j => j.Selected)}</td>" +
                $"<td>{H(When(js.Count == 0 ? null : js.Max(j => j.CreatedUtc)))}</td></tr>");
        }
        if (analyses.Count == 0)
            sb.Append("<tr><td colspan=\"6\" class=\"muted\">아직 분석이 없습니다. ZWCAD의 ASH에서 \"서버로 보내기\"를 할 때 분석과 안을 고르면 자동으로 만들어지고, 아래 \"새 분석\"에서 미리 만들 수도 있습니다.</td></tr>");
        sb.Append("</table></div>");
        sb.Append("<section class=\"box\"><h2>새 분석</h2><form method=\"post\" action=\"/analyses\" class=\"mini\">" +
            "<input type=\"text\" name=\"name\" required maxlength=\"100\" placeholder=\"분석 이름(100자까지)\" aria-label=\"분석 이름\">" +
            "<input type=\"text\" name=\"note\" maxlength=\"200\" placeholder=\"메모(선택, 200자까지)\" aria-label=\"메모\"><button class=\"primary\">만들기</button></form></section>");
        return Page("ASH 분석", sb.ToString());
    }

    public static string Unclassified(IReadOnlyList<JobInfo> jobs, IReadOnlyList<AnalysisInfo> analyses)
    {
        var sb = new StringBuilder("<p class=\"crumb\"><a href=\"/analyses\">분석</a> › 분석에 넣지 않은 작업</p><h1>분석에 넣지 않은 작업</h1>");
        sb.Append("<p class=\"muted\">분석을 고르지 않고 보낸 작업과 예전 작업입니다. 안을 골라 옮기면 그 안의 다음 버전이 됩니다.</p>");
        sb.Append("<div class=\"scroll\"><table><tr><th>작업</th><th>상태</th><th>프로젝트 / 안</th><th>보낸 사람</th><th>보낸 시각(서울)</th><th>권장 총 대수(대)</th><th>옮기기</th></tr>");
        foreach (var j in jobs.Where(j => j.AnalysisId == null))
        {
            var total = j.Decided?.Values.Sum();
            sb.Append($"<tr><td><a href=\"/jobs/{H(j.Id)}\">{H(j.Id)}</a></td><td>{State(j)}</td><td>{H(j.Project)} / {H(j.Plan)}</td><td>{H(j.Submitter)}</td><td>{H(When(j.CreatedUtc))}</td>" +
                $"<td class=\"num\">{(total is { } n ? n.ToString(CultureInfo.InvariantCulture) : "-")}</td><td>{MoveForm(j, analyses, "/analyses/unclassified", "옮기기")}</td></tr>");
        }
        if (!jobs.Any(j => j.AnalysisId == null))
            sb.Append("<tr><td colspan=\"7\" class=\"muted\">분석에 넣지 않은 작업이 없습니다. ZWCAD의 ASH에서 \"서버로 보내기\"를 할 때 분석과 안을 고르면 처음부터 분석에 들어갑니다.</td></tr>");
        sb.Append("</table></div>");
        return Page("ASH 분석에 넣지 않은 작업", sb.ToString());
    }

    public static string Detail(AnalysisInfo a, IReadOnlyList<JobInfo> allJobs, JobStore store, IReadOnlyList<FilmInfo>? films = null)
    {
        var jobs = allJobs.Where(j => j.AnalysisId == a.Id).ToList();
        var back = "/analyses/" + a.Id;
        var sb = new StringBuilder($"<p class=\"crumb\"><a href=\"/analyses\">분석</a> › {H(a.Name)}</p><h1>{H(a.Name)}</h1>");
        if (a.Note.Length > 0) sb.Append($"<p>{H(a.Note)}</p>");
        sb.Append($"<p class=\"muted\">만든 시각 {H(When(a.CreatedUtc))} · 안 {a.Plans.Count}개 · 버전 {jobs.Count}개 · <a href=\"/analyses/{H(a.Id)}/cuts\">컷 영상 →</a></p>");
        if (jobs.Any(j => !j.Finished)) sb.Append("<p class=\"muted\">실행 중인 작업이 있습니다. 새로 고치면 결과가 갱신됩니다.</p>");

        var metrics = jobs.ToDictionary(j => j.Id, j => MetricsOf(store, j));
        Compare(sb, a, jobs, metrics);

        foreach (var p in a.Plans)
        {
            sb.Append($"<section class=\"box\"><h2>{H(p.Name)}</h2>");
            if (p.Note.Length > 0) sb.Append($"<p class=\"muted\">{H(p.Note)}</p>");
            var versions = jobs.Where(j => j.PlanId == p.Id).OrderBy(j => j.Version).ToList();
            if (versions.Count == 0) sb.Append("<p class=\"muted\">아직 보낸 작업이 없습니다. ZWCAD의 ASH에서 이 분석과 안을 골라 \"서버로 보내기\"를 하면 v1이 됩니다.</p>");
            else
            {
                sb.Append("<div class=\"scroll\"><table class=\"sort\"><thead><tr>").Append(Th("버전")).Append(Th("보낸 시각(서울)")).Append(Th("보낸 사람")).Append(Th("메모")).Append(Th("상태"))
                  .Append(Th("권장 총 대수(대)")).Append(Th("가동률(%)")).Append(Th("결품 시간(초)")).Append("<th>선정</th></tr></thead><tbody>");
                foreach (var j in versions)
                {
                    var m = metrics[j.Id];
                    sb.Append($"<tr><td><a href=\"/jobs/{H(j.Id)}\">v{j.Version}</a></td><td>{H(When(j.CreatedUtc))}</td><td>{H(j.Submitter)}</td><td>{H(j.VersionNote)}</td><td>{State(j)}</td>" +
                        $"<td class=\"num\">{(m?.Total is { } t ? t.ToString(CultureInfo.InvariantCulture) : "-")}</td><td class=\"num\">{(m == null ? "-" : P(m.Util))}</td><td class=\"num\">{(m == null ? "-" : Num(m.Stop, "0"))}</td>" +
                        $"<td>{SelectForm(j, back)}</td></tr>");
                }
                sb.Append("</tbody></table></div>").Append(SortScript);
            }
            sb.Append("</section>");
        }
        if (a.Plans.Count == 0) sb.Append("<p class=\"muted\">아직 안이 없습니다. 아래에서 안을 추가하거나, ZWCAD에서 보낼 때 안 이름을 적으면 만들어집니다.</p>");
        else sb.Append("<p class=\"muted\">가동률(%)은 그 버전에서 가장 높은 그룹의 확인 실행 평균, 결품 시간(초)은 모든 자재의 합계입니다. 결과가 없는 버전(대기·실행 중·실패)은 \"-\"입니다.</p>");

        sb.Append("<section class=\"box\"><h2>안 추가</h2>").Append($"<form method=\"post\" action=\"/analyses/{H(a.Id)}/plans\" class=\"mini\"><input type=\"hidden\" name=\"back\" value=\"{H(back)}\">" +
            "<input type=\"text\" name=\"name\" required maxlength=\"100\" placeholder=\"안 이름(예: A안)\" aria-label=\"안 이름\"><input type=\"text\" name=\"note\" maxlength=\"200\" placeholder=\"메모(선택)\" aria-label=\"메모\">" +
            "<button class=\"primary\">추가</button></form></section>");

        FilmList(sb, (films ?? Array.Empty<FilmInfo>()).Where(f => f.Request.AnalysisId == a.Id).ToList(), a.Id);

        sb.Append($"<details><summary>이름 바꾸기 · 지우기</summary><div class=\"box\"><form method=\"post\" action=\"/analyses/{H(a.Id)}/rename\" class=\"mini\"><input type=\"hidden\" name=\"back\" value=\"{H(back)}\">" +
            $"<input type=\"text\" name=\"name\" required maxlength=\"100\" value=\"{H(a.Name)}\" aria-label=\"분석 이름\"><button>분석 이름 바꾸기</button></form>");
        foreach (var p in a.Plans)
            sb.Append($"<form method=\"post\" action=\"/analyses/{H(a.Id)}/plans/{H(p.Id)}/rename\" class=\"mini\" style=\"margin-top:8px\"><input type=\"hidden\" name=\"back\" value=\"{H(back)}\">" +
                $"<input type=\"text\" name=\"name\" required maxlength=\"100\" value=\"{H(p.Name)}\" aria-label=\"{H(p.Name)} 새 이름\"><button>안 이름 바꾸기</button></form>");
        if (jobs.Count == 0)
            sb.Append($"<form method=\"post\" action=\"/analyses/{H(a.Id)}/delete\" style=\"margin-top:12px\"><button>이 분석 지우기</button> <span class=\"muted\">보낸 작업이 없을 때만 지울 수 있습니다.</span></form>");
        else sb.Append("<p class=\"muted\">보낸 작업이 있는 분석은 지울 수 없습니다.</p>");
        sb.Append("</div></details>");
        return Page("ASH 분석 " + a.Name, sb.ToString(), jobs.Any(j => !j.Finished));
    }

    internal static string FileUrl(FilmInfo f, string name) => $"/films/{H(f.Id)}/files/{H(Uri.EscapeDataString(name))}";

    static void FilmList(StringBuilder sb, List<FilmInfo> films, string analysisId)
    {
        sb.Append("<section class=\"box\"><h2>만든 영상</h2>");
        if (films.Count == 0) { sb.Append($"<p class=\"muted\">아직 만든 영상이 없습니다. <a href=\"/analyses/{H(analysisId)}/cuts\">컷 영상</a>에서 만들면 여기에 목록이 생깁니다.</p></section>"); return; }
        sb.Append("<div class=\"scroll\"><table><tr><th>요청 시각(서울)</th><th>상태</th><th>컷 × 버전</th><th>만든 컷</th><th>이어 붙인 영상</th></tr>");
        foreach (var f in films)
            sb.Append($"<tr><td><a href=\"/films/{H(f.Id)}\">{H(When(f.CreatedUtc))}</a></td><td><span class=\"state s-{H(f.State)}\">{H(JobState.Korean(f.State))}</span>{(f.Phase != null ? " " + H(f.Phase) : "")}</td>" +
                $"<td class=\"num\">{f.Cuts.Count} × {f.Request.JobIds.Count}</td><td class=\"num\">{f.Done}{(f.Failed > 0 ? $" <span class=\"bad\">(실패 {f.Failed})</span>" : "")}</td>" +
                $"<td>{(f.JoinedOk ? $"<a href=\"{FileUrl(f, f.Joined!)}\">{H(f.Joined)}</a>" : "-")}</td></tr>");
        sb.Append("</table></div></section>");
    }

    /// <summary>/films/{id}: progress while it runs (10 s refresh), then the players and downloads. <paramref name="exists"/> tells which files are still on disk.</summary>
    public static string Film(FilmInfo f, Func<string, bool> exists, string logTail)
    {
        var r = f.Request;
        var sb = new StringBuilder($"<p class=\"crumb\"><a href=\"/analyses\">분석</a> › <a href=\"/analyses/{H(r.AnalysisId)}\">{H(f.AnalysisName)}</a> › 영상</p>");
        sb.Append($"<h1>영상 <span class=\"muted\">· {H(When(f.CreatedUtc))}</span></h1>");
        sb.Append($"<p><span class=\"state s-{H(f.State)}\">{H(JobState.Korean(f.State))}</span> {(f.Phase != null ? H(f.Phase) + " · " : "")}컷 {f.Items.Count}개 중 {f.Done}개 완료" +
            $"{(f.Failed > 0 ? $" · <span class=\"bad\">실패 {f.Failed}개</span>" : "")}{(f.StartedUtc != null ? " · 걸린 시간 " + Dur((f.FinishedUtc ?? DateTime.UtcNow) - f.StartedUtc.Value) : "")}</p>");
        if (f.State == JobState.Queued) sb.Append("<p class=\"muted\">작업 대기열에서 차례를 기다립니다. 대수 탐색 작업 사이에 하나씩 찍습니다.</p>");
        if (!f.Finished)
            sb.Append(f.CancelRequested ? "<p class=\"muted\">취소하는 중입니다. 지금 단계가 끝나면 멈춥니다.</p>"
                : $"<form method=\"post\" action=\"/films/{H(f.Id)}/cancel\"><button>{(f.State == JobState.Queued ? "대기 취소" : "촬영 취소")}</button> <span class=\"muted\">찍는 동안 다른 작업은 기다립니다. 다 찍은 컷 영상은 남습니다.</span></form>");
        if (f.Error != null) sb.Append($"<p class=\"bad\"><b>{(f.State == JobState.Done ? "주의" : "오류")}:</b> {H(f.Error)}</p>");
        foreach (var w in f.Warnings) sb.Append($"<div class=\"msg warn\">{H(w)}</div>");
        sb.Append($"<p class=\"muted\">{r.Width}×{r.Height} · {r.Fps} fps · 자막 {(r.Caption ? "켬" : "끔")}" +
            $"{(r.SameCounts != null ? " · 같은 대수 " + H(string.Join(", ", r.SameCounts.Select(kv => kv.Key + " " + kv.Value + "대"))) : r.VehicleDelta != 0 ? $" · 결론 {r.VehicleDelta:+0;-0}대" : " · 결론 대수")}" +
            $" · 시드 {(r.Seed?.ToString(CultureInfo.InvariantCulture) ?? "첫 버전의 시작 시드")}</p>");
        string Player(string name) =>
            $"<video controls playsinline preload=\"metadata\" src=\"{FileUrl(f, name)}\" style=\"width:100%;max-width:720px;background:#000;display:block\"></video>" +
            $"<a href=\"{FileUrl(f, name)}\" download=\"{H(name)}\">{H(name)} 받기</a>";
        if (f.Joined != null)
            sb.Append("<section class=\"box\"><h2>이어 붙인 영상</h2>").Append(f.JoinedOk && exists(f.Joined) ? Player(f.Joined) : $"<p class=\"muted\">{(f.Finished ? "만들지 못했습니다." : "컷을 다 찍은 뒤 이어 붙입니다.")}</p>").Append("</section>");
        sb.Append("<section class=\"box\"><h2>컷별 영상</h2><div class=\"grid\">");
        foreach (var i in f.Items)
        {
            sb.Append($"<div><b>{H(i.Label)}</b><br>");
            if (i.Ok && exists(i.Name)) sb.Append(Player(i.Name));
            else if (i.Ok) sb.Append("<span class=\"muted\">이어 붙인 영상에만 넣었습니다.</span>");
            else if (i.Error != null) sb.Append($"<span class=\"bad\">{H(i.Error)}</span>");
            else sb.Append($"<span class=\"muted\">{(f.Finished ? "만들지 못했습니다." : "차례를 기다립니다.")}</span>");
            sb.Append("</div>");
        }
        sb.Append("</div></section>");
        sb.Append($"<details><summary>촬영 기록 (film.log)</summary><pre>{H(logTail)}</pre><a href=\"/films/{H(f.Id)}/files/film.log\">film.log 전체</a></details>");
        return Page("ASH 영상 " + f.AnalysisName, sb.ToString(), !f.Finished);
    }

    /// <summary>Rows = selected versions; per group 권장(대) · 가동률(%) · 최소 재고/안전재고(배) · 결품(초), then 교착(회) once.</summary>
    static void Compare(StringBuilder sb, AnalysisInfo a, List<JobInfo> jobs, Dictionary<string, Metrics?> metrics)
    {
        sb.Append("<section class=\"box\"><h2>선정 버전 비교</h2>");
        var picked = jobs.Where(j => j.Selected).OrderBy(j => a.Plans.FindIndex(p => p.Id == j.PlanId)).ThenBy(j => j.Version).ToList();
        if (picked.Count == 0) { sb.Append("<p class=\"muted\">선정한 버전이 없습니다. 아래 안별 표에서 비교할 버전의 \"선정\"을 누르면 여기에 나란히 놓입니다.</p></section>"); return; }
        var groups = picked.Select(j => metrics[j.Id]).Where(m => m != null).SelectMany(m => m!.View.Groups.Select(g => g.Group)).Distinct(StringComparer.Ordinal).ToList();
        sb.Append("<div class=\"scroll\"><table><tr><th rowspan=\"2\">안 · 버전</th>");
        foreach (var g in groups) sb.Append($"<th colspan=\"4\">{H(g)}</th>");
        sb.Append("<th rowspan=\"2\">교착(회)</th></tr><tr>");
        foreach (var _ in groups) sb.Append("<th>권장(대)</th><th>가동률(%)</th><th>최소 재고/안전재고(배)</th><th>결품(초)</th>");
        sb.Append("</tr>");
        foreach (var j in picked)
        {
            var plan = a.Plans.FirstOrDefault(p => p.Id == j.PlanId);
            sb.Append($"<tr><td><a href=\"/jobs/{H(j.Id)}\">{H(plan?.Name)} · v{j.Version}</a></td>");
            if (metrics[j.Id] is not { } m) { sb.Append($"<td colspan=\"{groups.Count * 4 + 1}\" class=\"muted\">결과가 없습니다({H(JobState.Korean(j.State))})</td></tr>"); continue; }
            foreach (var g in groups)
            {
                var gv = m.View.Groups.FirstOrDefault(x => x.Group == g);
                if (gv == null) { sb.Append("<td class=\"muted\" colspan=\"4\">-</td>"); continue; }
                var stops = m.View.Materials.Where(x => x.Group == g).Select(x => x.StopSec).Where(s => !double.IsNaN(s)).ToList();
                sb.Append($"<td class=\"num\">{(gv.Decided?.ToString(CultureInfo.InvariantCulture) ?? "-")}</td><td class=\"num\">{P(gv.Util)}</td><td class=\"num\">{F(gv.MinStockRatio)}</td><td class=\"num\">{(stops.Count == 0 ? "-" : Num(stops.Sum(), "0"))}</td>");
            }
            sb.Append($"<td class=\"num\">{m.View.Deadlocks}</td></tr>");
        }
        sb.Append("</table></div></section>");
    }
}
