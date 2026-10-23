using System.Text;
using static ASH.Server.JobPages;

namespace ASH.Server;

/// <summary>The home page (/) and the analysis list (/analyses): running card, queue line, 내 것만|전체 toggle and the 진행 중/완료 analysis lists.</summary>
public static class HomePages
{
    // Remembers the toggle in localStorage.ashMe; opened without ?me it jumps to the saved side. Inline, nothing external.
    static string ToggleScript(bool me) => @"<script>
(function(){
  try{
    var s=localStorage.getItem('ashMe');
    if((s==='0'||s==='1')&&!/[?&]me=/.test(location.search)&&s!=='" + (me ? "1" : "0") + @"') location.replace('/?me='+s);
  }catch(e){}
  document.querySelectorAll('a[data-me]').forEach(function(a){
    a.addEventListener('click',function(){ try{ localStorage.setItem('ashMe',a.getAttribute('data-me')); }catch(e){} });
  });
})();
</script>";

    static string Pct(double? r) => r is { } v ? Math.Round(v * 100).ToString("0") + "%" : "-";

    /// <summary>The "지금 실행 중" card. Fields carry data-field/data-bar/data-state keys "{keyPrefix}.x" for the polling script.</summary>
    internal static void RunningCard(StringBuilder sb, RunningCard c, string keyPrefix)
    {
        var k = H(keyPrefix);
        sb.Append($"<div class=\"box\"><div class=\"box-head\"><h2><a href=\"/jobs/{H(c.JobId)}\">{H(c.Title)}</a></h2>" +
            $"<span class=\"state s-{H(c.State)}\" data-state=\"{k}.state\">{H(JobState.Korean(c.State))}</span></div>");
        sb.Append($"<p><span data-field=\"{k}.phase\">{H(c.Phase ?? "")}</span> · 실행 <span data-field=\"{k}.runs\">{c.RunsDone}{(c.ExpectedRuns is { } e ? "/" + e : "")}회</span></p>");
        sb.Append($"<div class=\"prog\"><i data-bar=\"{k}.bar\" style=\"width:{Math.Round((c.Progress ?? 0) * 100)}%\"></i></div>");
        sb.Append($"<p>진행률 <b data-field=\"{k}.progress\">{Pct(c.Progress)}</b> · 끝날 예상 <span data-field=\"{k}.eta\">{H(c.Eta ?? "-")}</span> · 한 번 평균 <span data-field=\"{k}.avg\">{H(c.AvgRun ?? "-")}</span>" +
            $" · <span data-field=\"{k}.film\">{H(c.Film ?? "")}</span></p></div>");
    }

    static string Chip(PlanChip p)
    {
        var t = $"{H(p.PlanName)} v{p.Version}" + (p.State == JobState.Running ? " 진행률 " + Pct(p.Progress) : p.State == JobState.Queued ? " 대기" : p.Total is { } n ? $" {n}대" : "");
        return $"<a class=\"chip{(p.Selected ? " sel" : "")}\" href=\"/jobs/{H(p.JobId)}\">{t}</a>";
    }

    internal static void AnalysisList(StringBuilder sb, IReadOnlyList<AnalysisRow> rows)
    {
        if (rows.Count == 0) { sb.Append("<p class=\"muted\">분석이 없습니다.</p>"); return; }
        sb.Append("<ul class=\"alist\">");
        foreach (var r in rows)
            sb.Append($"<li><a href=\"/analyses/{H(r.Id)}\">{H(r.Name)}</a> · <span class=\"chip\">{(r.Active ? "진행 중" : "완료")}</span> " +
                $"안 {r.Plans} · 버전 {r.Versions} · 선정 {r.Selected} · 영상 {r.Films} · <span class=\"muted\">{H(When(r.LastSentUtc))}</span> " +
                string.Concat(r.Chips.Select(Chip)) + "</li>");
        sb.Append("</ul>");
    }

    public static string Home(HomeView v, string? pollScript)
    {
        var sb = new StringBuilder(ToggleScript(v.Me)); // first, so a saved choice redirects before the lists are painted
        sb.Append("<h1>ASH</h1>");
        if (v.Running is { } r) RunningCard(sb, r, "running");
        else sb.Append($"<div class=\"box\">실행 중인 작업 없음 · 작업 실행 {(v.WorkerOn ? "켜짐" : "꺼짐")}</div>");

        sb.Append("<p>");
        if (v.QueuedCount == 0) sb.Append("대기 없음");
        else sb.Append($"대기 {v.QueuedCount}건: " + string.Join(" · ", v.Queued.Select(q => $"<a href=\"/jobs/{H(q.JobId)}\">{H(q.Title)}</a>{(q.Start != null ? " (" + H(q.Start) + " 시작 예상)" : "")}")));
        sb.Append("</p>");

        sb.Append($"<p><a href=\"/?me=1\" data-me=\"1\" class=\"chip{(v.Me ? " sel" : "")}\">내 것만</a> | <a href=\"/?me=0\" data-me=\"0\" class=\"chip{(v.Me ? "" : " sel")}\">전체</a></p>");
        sb.Append("<h2>진행 중</h2>"); AnalysisList(sb, v.InProgress);
        sb.Append("<h2>완료</h2>"); AnalysisList(sb, v.Finished);
        sb.Append($"<details><summary>분석에 넣지 않은 작업 {v.Unclassified}건</summary><p><a href=\"/analyses/unclassified\">분석에 넣지 않은 작업 보기 →</a></p></details>");
        return Page("ASH", sb.ToString(), pollScript);
    }

    public static string Analyses(IReadOnlyList<AnalysisRow> rows, string q, int unclassified)
    {
        var sb = new StringBuilder("<h1>분석</h1>");
        sb.Append("<p class=\"muted\">분석은 한 가지 검토(예: 라인 증설)이고, 그 안에 여러 안(A안, B안)이 있습니다. 같은 안을 고쳐 다시 보낼 때마다 버전(v1, v2…)이 올라갑니다.</p>");
        sb.Append($"<form class=\"mini\"><input type=\"text\" name=\"q\" value=\"{H(q)}\" placeholder=\"이름·보낸 사람\" aria-label=\"검색\"><button>검색</button></form>");
        // plan D: FileRoutes.TagFilterHtml (tag filters) goes here, between the search form and the list.
        var shown = q.Length == 0 ? rows : rows.Where(r => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Submitters.Any(s => s.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
        AnalysisList(sb, shown);
        sb.Append($"<p><a href=\"/analyses/unclassified\">분석에 넣지 않은 작업 {unclassified}건 →</a></p>");
        sb.Append("<section class=\"box\"><h2>새 분석</h2><form method=\"post\" action=\"/analyses\" class=\"mini\">" +
            "<input type=\"text\" name=\"name\" required maxlength=\"100\" placeholder=\"분석 이름(100자까지)\" aria-label=\"분석 이름\">" +
            "<input type=\"text\" name=\"note\" maxlength=\"200\" placeholder=\"메모(선택, 200자까지)\" aria-label=\"메모\"><button class=\"primary\">만들기</button></form></section>");
        return Page("ASH 분석", sb.ToString());
    }
}
