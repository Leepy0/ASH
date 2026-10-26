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
    if((s==='0'||s==='1')&&!/[?&]me=/.test(location.search)&&s!=='" + (me ? "1" : "0") + @"') location.replace('/?me='+s+(location.search?'&'+location.search.slice(1):''));
  }catch(e){}
})();
</script>";

    // Must come after the toggle links: stores the clicked side.
    const string SaveScript = @"<script>
document.querySelectorAll('a[data-me]').forEach(function(a){
  a.addEventListener('click',function(){ try{ localStorage.setItem('ashMe',a.getAttribute('data-me')); }catch(e){} });
});
</script>";

    static string Pct(double? r) => r is { } v ? Math.Round(v * 100).ToString("0") + "%" : "-";

    /// <summary>The "지금 실행 중" card. Fields carry data-field/data-bar/data-state keys "{keyPrefix}.x" for the polling script.</summary>
    internal static void RunningCard(StringBuilder sb, RunningCard c, string keyPrefix)
    {
        var k = H(keyPrefix);
        sb.Append($"<div class=\"box\"><div class=\"box-head\"><h2><a href=\"{H(c.Href ?? "/jobs/" + c.JobId)}\">{H(c.Title)}</a></h2>" +
            $"<span class=\"state s-{H(c.State)}\" data-state=\"{k}.state\" data-field=\"{k}.statetext\">{H(JobState.Korean(c.State))}</span></div>");
        sb.Append($"<p><span data-field=\"{k}.phase\">{H(c.Phase ?? "")}</span> · 실행 <span data-field=\"{k}.runs\">{c.RunsDone}{(c.ExpectedRuns is { } e ? "/" + e : "")}회</span></p>");
        if (c.Film == null || c.Note.Length > 0) sb.Append($"<p class=\"muted\">메모 <span data-field=\"{k}.note\">{H(c.Note)}</span></p>");
        sb.Append($"<div class=\"prog\"><i data-bar=\"{k}.bar\" style=\"width:{Math.Round((c.Progress ?? 0) * 100)}%\"></i></div>");
        sb.Append($"<p>진행률 <b data-field=\"{k}.progress\">{Pct(c.Progress)}</b> · 끝날 예상 <span data-field=\"{k}.eta\">{H(c.Eta ?? "-")}</span> · 한 번 평균 <span data-field=\"{k}.avg\">{H(c.AvgRun ?? "-")}</span>" +
            $" · <span data-field=\"{k}.film\">{H(c.Film ?? "")}</span></p></div>");
    }

    // page-only CSS for the four lanes; the shared parts (.lanes .card .row .grp …) are in JobPages.Css
    const string HomeCss = @"<style>
.lanes .card{gap:2px}.lanes .card a.t2{color:var(--ink);text-decoration:none}.lanes .card .t1{color:var(--c)}.lanes .card .pv{margin:10px 0 6px}
.acts{display:flex;gap:8px;justify-content:center;flex-wrap:wrap;margin-top:12px}.lanes .warn{margin-top:10px}.lanes .warn a{color:var(--blue-d)}
.sec{font-size:13px;color:var(--sub2);text-align:center;margin-top:4px}
.grp .wn b{color:var(--orange)}
.qn{flex:none;width:28px;height:28px;border-radius:14px;background:var(--gray-b);color:var(--sub2);display:grid;place-items:center;font-weight:700;font-size:13px}.qn.me{background:var(--blue-b);color:var(--blue-d)}
.row .n{font-size:18px;font-weight:700;white-space:nowrap}.row .n small{font-size:13px;color:var(--sub2)}
.tagf{display:flex;gap:6px;align-items:center;flex-wrap:wrap;margin:0}.tagf label{display:flex;gap:4px;align-items:center;font-size:13px;color:var(--sub2)}.tagf select{width:auto;min-height:30px;padding:2px 6px}
</style>";

    static string Md(DateTime? utc) => utc is { } t ? ASH.Core.SeoulTime.FromUtc(t).ToString("MM/dd", System.Globalization.CultureInfo.InvariantCulture) : "-";
    static string LaneHead(string cls, string name, int count, string small = "") =>
        $"<section class=\"lane {cls}\" aria-label=\"{name}\"><div class=\"lane-h\"><b>{name}</b><span class=\"cnt\">{count}</span>{(small.Length > 0 ? "<small>" + small + "</small>" : "")}</div><div class=\"lane-b\">";
    const string LaneEnd = "</div></section>";

    /// <summary>The home page: four lanes 진행 | 결론 확인 | 영상 | 보고서 filling the window, the 내 것/전체 toggle and the tag filter in the header row.</summary>
    public static string Home(HomeView v, string tagFilter, string tagQuery, string? pollScript)
    {
        var sb = new StringBuilder(PollSlots + ToggleScript(v.Me)); // first, so a saved choice redirects before the lists are painted
        sb.Append(HomeCss);
        sb.Append($"<div class=\"head\"><h1>{(v.Me ? "내 작업" : "전체 작업")}</h1><nav class=\"segc\" aria-label=\"보기\">" +
            $"<a href=\"/?me=1{H(tagQuery)}\" data-me=\"1\"{(v.Me ? " class=\"on\" aria-current=\"true\"" : "")}>내 것</a>" +
            $"<a href=\"/?me=0{H(tagQuery)}\" data-me=\"0\"{(v.Me ? "" : " class=\"on\" aria-current=\"true\"")}>전체</a></nav>").Append(SaveScript);
        sb.Append($"<span class=\"sp\"></span><form class=\"tagf\"><input type=\"hidden\" name=\"me\" value=\"{(v.Me ? 1 : 0)}\">{tagFilter}<button>검색</button></form></div>");
        sb.Append("<div class=\"lanes\">");

        // 1. 진행: the running job (or film) and the queue
        sb.Append(LaneHead("c-blue", "진행", (v.Running != null ? 1 : 0) + v.QueuedCount, $"대기 <span data-field=\"queued.count\">{v.QueuedCount}</span>건"));
        if (v.Running is { } r)
        {
            var href = H(r.Href ?? "/jobs/" + r.JobId);
            sb.Append($"<div class=\"card hot\"{(r.Note.Length > 0 ? $" title=\"{H(r.Note)}\"" : "")}><div class=\"t4\"><span data-field=\"running.statetext\">{H(JobState.Korean(r.State))}</span>" +
                $"<span data-field=\"running.sep\">{(r.Phase != null ? " · " : "")}</span><span data-field=\"running.phase\">{H(r.Phase ?? "")}</span></div>" +
                $"<a class=\"t2\" href=\"{href}\">{H(r.Name ?? r.Title)}</a><div class=\"t4\">{H(r.Sub ?? "")}</div>" +
                $"<div class=\"t1\" data-field=\"running.progress\">{Pct(r.Progress)}</div><div class=\"pv\" data-bar=\"running.bar\"><i style=\"width:{Math.Round((r.Progress ?? 0) * 100)}%\"></i></div>" +
                $"<div class=\"t3\" data-field=\"running.left\">{H(r.Left ?? "")}</div>");
            if (r.Film != null) sb.Append($"<div class=\"t4\" data-field=\"running.film\">{H(r.Film)}</div>");
            if (r.Failed > 0) sb.Append($"<div class=\"warn\" role=\"alert\">▲ {(r.Href == null ? "실행" : "컷")} {r.Failed}{(r.Href == null ? "회" : "개")} 실패 <a href=\"{href}\">원인 보기</a></div>");
            sb.Append("</div>");
        }
        else sb.Append($"<div class=\"empty\">실행 중인 작업 없음{(v.WorkerOn ? "" : " · 작업 실행 꺼짐")}</div>");
        if (v.Queued.Count > 0) sb.Append("<div class=\"sec\">대기열</div>");
        foreach (var (q, i) in v.Queued.Select((q, i) => (q, i)))
            sb.Append($"<a class=\"row\" href=\"/jobs/{H(q.JobId)}\"><span class=\"qn{(q.Mine ? " me" : "")}\">{i + 1}</span><span class=\"t\"><div>{H(q.Title)}{(q.Mine ? " <span class=\"tag blue\">내 작업</span>" : "")}</div>" +
                $"<small>{H(q.Submitter.Length > 0 ? q.Submitter : "-")} · {(q.Start != null ? H(q.Start) + " 시작" : "시작 시각 미정")}</small></span></a>");
        sb.Append(LaneEnd);

        // 2. 결론 확인: a conclusion and no film yet
        sb.Append(LaneHead("c-indigo", "결론 확인", v.Conclusions.Count));
        foreach (var (c, i) in v.Conclusions.Select((c, i) => (c, i)))
        {
            var over = c.Util > c.UtilMax; var stop = c.Stop > 0;
            sb.Append($"<div class=\"card{(i == 0 ? " hot" : "")}\"><a class=\"t2\" href=\"/analyses/{H(c.AnalysisId)}\">{H(c.Name)}</a><div class=\"t4\">{H(c.Plan)} v{c.Version} · {Md(c.FinishedUtc)}</div>" +
                $"<div class=\"t1\">{c.Total}<small>대</small></div><div class=\"t4\">권장 대수</div><div class=\"grp\">" +
                $"<div{(over ? " class=\"wn\"" : "")}{(double.IsNaN(c.UtilMax) ? "" : $" title=\"{(over ? "경고: 가동률 상한" : "상한")} {Pct(c.UtilMax)}{(over ? " 초과" : "")}\"")}><b>{(over ? "⚠ " : "")}{Pct(double.IsNaN(c.Util) ? null : c.Util)}</b><small>최대 가동률</small></div>" +
                $"<div{(stop ? " class=\"bad\"" : "")}><b>{(stop ? "▲ " : "")}{(double.IsNaN(c.Stop) ? "-" : F(c.Stop, "0") + "초")}</b><small>결품</small></div>" +
                $"<div><b>{H(c.Block ?? "-")}</b><small>병목</small></div></div>" +
                $"<div class=\"acts\"><a class=\"btn\" href=\"/analyses/{H(c.AnalysisId)}\">결론 보기</a><a class=\"btn pri\" href=\"/analyses/{H(c.AnalysisId)}/cuts?job={H(c.JobId)}\">영상 만들기</a></div></div>");
        }
        if (v.Conclusions.Count == 0) sb.Append("<div class=\"empty\">결론이 나오면 여기에 나타납니다</div>");
        sb.Append(LaneEnd);

        // 3. 영상: films queued or being made
        sb.Append(LaneHead("c-orange", "영상", v.Films.Count));
        foreach (var f in v.Films)
        {
            var k = "film." + H(f.FilmId);
            sb.Append($"<div class=\"card\"><a class=\"t2\" href=\"/films/{H(f.FilmId)}\">{H(f.Name)}</a><div class=\"t1\"><span data-field=\"{k}.done\">{f.Done} / {f.Total}</span><small>컷</small></div>" +
                $"<div class=\"pv\" data-bar=\"{k}.bar\"><i style=\"width:{(f.Total == 0 ? 0 : Math.Round(100.0 * f.Done / f.Total))}%\"></i></div><div class=\"t4\" data-field=\"{k}.note\">{H(LiveFields.FilmNote(f))}</div>" +
                $"<div class=\"acts\"><a class=\"btn\" href=\"/films/{H(f.FilmId)}\">진행 보기</a></div></div>");
        }
        if (v.Films.Count == 0) sb.Append("<div class=\"empty\">결론을 확인한 뒤<br>\"영상 만들기\"로 여기에 추가</div>");
        sb.Append(LaneEnd);

        // 4. 보고서: the latest finished film as a card, older ones as rows
        sb.Append(LaneHead("c-green", "보고서", v.Reports.Count));
        foreach (var (p, i) in v.Reports.Select((p, i) => (p, i)))
        {
            var sub = (p.Plan != null ? $"{H(p.Plan)} v{p.Version}{(p.Selected ? " 선정" : "")} · " : "") + Md(p.FinishedUtc);
            var total = p.Total is { } n ? $"{n}<small>대</small>" : "-";
            if (i == 0)
                sb.Append($"<div class=\"card hot\"><a class=\"t2\" href=\"/analyses/{H(p.AnalysisId)}\">{H(p.Name)}</a><div class=\"t4\">{sub}</div><div class=\"t1\">{total}</div><div class=\"acts\">" +
                    (p.JobId != null ? $"<a class=\"btn\" href=\"/films/{H(p.FilmId)}\">영상 보기</a><a class=\"btn pri\" href=\"/analyses/{H(p.AnalysisId)}/report?job={H(p.JobId)}\">보고서 받기</a>"
                        : $"<a class=\"btn pri\" href=\"/films/{H(p.FilmId)}\">영상 보기</a>") + "</div></div>");
            else
            {
                if (i == 1) sb.Append("<div class=\"sec\">지난 보고서</div>");
                sb.Append($"<a class=\"row\" href=\"/analyses/{H(p.AnalysisId)}\"><span class=\"t\"><div>{H(p.Name)}</div><small>{sub}</small></span><span class=\"n\">{total}</span></a>");
            }
        }
        if (v.Reports.Count == 0) sb.Append("<div class=\"empty\">영상이 끝나면 보고서를 받을 수 있습니다</div>");
        sb.Append(LaneEnd).Append("</div>");
        return Page("ASH", sb.ToString(), pollScript, nav: "home", fill: true);
    }
}
