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
    internal static string H(string? t) => WebUtility.HtmlEncode(t ?? "");
    internal static string F(double d, string fmt = "0.##") => double.IsNaN(d) || double.IsInfinity(d) ? "-" : d.ToString(fmt, CultureInfo.InvariantCulture);
    static string Pct(double d) => double.IsNaN(d) ? "-" : P(d) + "%";
    /// <summary>Percent without the sign, for table cells whose header carries "(%)".</summary>
    internal static string P(double d) => double.IsNaN(d) ? "-" : (d * 100).ToString("0.0", CultureInfo.InvariantCulture);
    internal static string When(DateTime? utc) => utc is { } t ? SeoulTime.Text(t) : "-";
    internal static string Dur(TimeSpan? d) => d is { } x ? (x.TotalHours >= 1 ? $"{(int)x.TotalHours}시간 {x.Minutes}분" : x.TotalMinutes >= 1 ? $"{(int)x.TotalMinutes}분 {x.Seconds}초" : $"{x.Seconds}초") : "-";

    const string Css = @"
:root{--ink:#2c2c2a;--bg:#fafaf7;--line:#d3d1c7;--navy:#1f3a5f;--link:#185fa5;--muted:#5f5e5a;--ok:#3b6d11;--bad:#a32d2d;--chip:#f1efe8;--blue:#378add}
body{font-family:'Malgun Gothic','Segoe UI',sans-serif;margin:0;color:var(--ink);background:var(--bg);line-height:1.45}
header{background:var(--navy);color:#fff;padding:12px 24px}header a{color:#fff;text-decoration:none;margin-right:18px}header a:hover{text-decoration:underline}
main{padding:20px 24px;max-width:1600px;overflow-x:auto}
h1{font-size:22px;font-weight:600;margin:0 0 12px}h2{font-size:17px;margin:24px 0 8px}
a{color:var(--link)}code{font-family:Consolas,monospace;font-size:12px;color:var(--muted)}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:16px;margin-top:16px}
.box{background:#fff;border:1px solid var(--line);border-radius:10px;padding:14px 18px;margin-top:16px}.grid .box{margin-top:0}.box h2{margin:0 0 10px}
.box-head{display:flex;justify-content:space-between;align-items:baseline;gap:12px;flex-wrap:wrap}
dl.kv{display:grid;grid-template-columns:max-content 1fr;gap:6px 14px;margin:0;font-size:14px}dl.kv dt{color:var(--muted)}dl.kv dd{margin:0;overflow-wrap:anywhere}
details summary{cursor:pointer;color:var(--link);font-size:14px;margin-top:10px}
.msg{padding:10px 14px;border-radius:8px;margin:10px 0}.msg.ok{background:#eaf3de}.msg.bad{background:#fcebeb}.msg.warn{background:#faeeda;color:#633806}
.field{display:grid;grid-template-columns:200px 1fr;gap:4px 16px;padding:10px 0;border-top:1px solid #eeede8}.field:first-of-type{border-top:none}
.field label{font-weight:600;font-size:14px;padding-top:6px}.help{font-size:13px;color:var(--muted);margin-top:4px}
input[type=text],input[type=number],input[type=password],select{font:inherit;padding:6px 8px;border:1px solid #b4b2a9;border-radius:6px;width:100%;max-width:420px;box-sizing:border-box;background:#fff}
.tag{display:inline-block;font-size:12px;padding:1px 7px;border-radius:8px;background:#eaf3de;color:#27500a;margin-left:6px}.tag.rs{background:#faeeda;color:#633806}
.actions{display:flex;gap:10px;align-items:center;flex-wrap:wrap;margin-top:14px}
button.primary{background:var(--navy);color:#fff;border:1px solid var(--navy);border-radius:6px;padding:7px 16px;font:inherit;cursor:pointer}
.scroll{overflow-x:auto;max-width:100%}.scroll td,.scroll th{white-space:nowrap}.box,.grid>*{min-width:0}
table{border-collapse:collapse;background:#fff}td,th{border:1px solid var(--line);padding:6px 10px;text-align:left;font-size:14px}th{background:var(--chip)}
.crumb{font-size:14px;color:var(--muted);margin:0 0 10px}.mini{display:flex;gap:8px;align-items:center;flex-wrap:wrap}.mini input[type=text],.mini select{width:auto;flex:1 1 160px;min-width:0}
.num{text-align:right}.muted{color:#888780}.bad{color:var(--bad)}.ok{color:var(--ok)}
.cards{display:flex;gap:14px;flex-wrap:wrap}.card{background:#fff;border:1px solid var(--line);border-radius:10px;padding:14px 18px;min-width:200px}
.card .big{font-size:30px;font-weight:600}.card .sub{color:var(--muted);font-size:13px}
.tabs a{display:inline-block;padding:8px 14px;border:1px solid var(--line);border-bottom:none;border-radius:8px 8px 0 0;background:var(--chip);color:var(--ink);text-decoration:none;margin-right:4px}
.tabs a.on{background:#fff;font-weight:600}.panel{background:#fff;border:1px solid var(--line);padding:16px}
.bar{background:#e6f1fb;height:14px;position:relative;width:180px;display:inline-block;vertical-align:middle}.bar i{display:block;height:14px;background:var(--blue)}
.bar b{position:absolute;top:-3px;bottom:-3px;width:2px;background:var(--bad)}
.state{display:inline-block;padding:2px 8px;border-radius:10px;font-size:13px}
.s-queued{background:#faeeda;color:#633806}.s-running{background:#e6f1fb;color:#0c447c}.s-done{background:#eaf3de;color:#27500a}.s-failed{background:#fcebeb;color:#791f1f}.s-cancelled{background:var(--chip);color:#444441}
pre{background:var(--chip);padding:10px;overflow:auto;max-height:420px;font-size:12px}
button{padding:6px 14px}
th[aria-sort],table.sort td{white-space:nowrap}th button{all:unset;cursor:pointer;white-space:nowrap}th button:focus-visible{outline:2px solid var(--link)}
th button::after{content:'▲';display:inline-block;width:1.2em;margin-left:.2em;text-align:center;visibility:hidden}
th[aria-sort=ascending] button::after,th[aria-sort=descending] button::after{visibility:visible}th[aria-sort=descending] button::after{content:'▼'}
.grid2{display:grid;grid-template-columns:1fr 1fr;gap:16px}@media(max-width:1024px){.grid2{grid-template-columns:1fr}}
.prog{background:#e6f1fb;height:10px;border-radius:5px;overflow:hidden}.prog i{display:block;height:10px;background:var(--blue)}
.chip{display:inline-block;padding:1px 8px;border-radius:10px;background:var(--chip);font-size:13px;margin-right:4px}.chip.sel{background:var(--navy);color:#fff}
.alist{list-style:none;padding:0;margin:0}.alist li{padding:6px 0;border-top:1px solid var(--line)}
#reload-bar{display:none;position:sticky;top:0;background:#faeeda;padding:8px 24px}#net{font-size:12px;color:var(--muted)}
@media (max-width:640px){header{padding:10px 16px}header a{margin-right:12px}main{padding:16px}.card{min-width:0;flex:1 1 130px;padding:10px 14px}.card .big{font-size:22px}
.grid{grid-template-columns:1fr}dl.kv,.field{grid-template-columns:1fr}dl.kv dt{margin-top:6px}.field label{padding-top:0}}";

    /// <summary>Click a header button of a table.sort to sort its rows; numbers as numbers ("-" last), text in Korean order with digits as numbers.</summary>
    internal const string SortScript = @"<script>
document.querySelectorAll('table.sort').forEach(function(t){
  var hs=t.tHead.rows[0].cells;
  Array.prototype.forEach.call(hs,function(th,i){
    var b=th.querySelector('button'); if(!b) return;
    b.addEventListener('click',function(){
      var up=th.getAttribute('aria-sort')!=='ascending', body=t.tBodies[0], rows=Array.prototype.slice.call(body.rows);
      Array.prototype.forEach.call(hs,function(h){ if(h.querySelector('button')) h.setAttribute('aria-sort','none'); });
      th.setAttribute('aria-sort',up?'ascending':'descending');
      function key(r){ var s=r.cells[i].textContent.trim(), n=parseFloat(s); return isNaN(n)?s:n; }
      rows.sort(function(a,c){
        var x=key(a), y=key(c);
        if(x==='-'||y==='-') return (x==='-')-(y==='-');
        var d=typeof x==='number'&&typeof y==='number'?x-y:String(x).localeCompare(String(y),'ko',{numeric:true});
        return up?d:-d;
      });
      rows.forEach(function(r){ body.appendChild(r); });
    });
  });
});
</script>";

    /// <summary>A header cell the sort script can sort by.</summary>
    internal static string Th(string label) => $"<th aria-sort=\"none\"><button type=\"button\">{label}</button></th>";

    /// <summary>The shared shell; <paramref name="script"/> (a whole &lt;script&gt; element) goes after &lt;/main&gt;.</summary>
    public static string Page(string title, string body, string? script = null) =>
        "<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>" + H(title) + "</title>" +
        "<style>" + Css + "</style></head><body><header><a href=\"/\"><b>ASH</b></a><a href=\"/\">홈</a><a href=\"/analyses\">분석</a><a href=\"/admin/settings\">설정</a></header><main>" + body + "</main>" + script + "</body></html>";


    /// <summary>Polls <paramref name="url"/> every 10 s and sets the data-field/data-bar/data-state elements (textContent only); shows #reload-bar when a watched job/film ends.</summary>
    public static string PollScript(string url, IReadOnlyDictionary<string, string>? initialWatch = null) => @"<script>
(function(){
  var prev=" + (initialWatch == null ? "null" : System.Text.Json.JsonSerializer.Serialize(initialWatch)) + @", reloading=false, net=document.getElementById('net'), timer=null;
  function sel(a,k){ return '['+a+'=""'+k.replace(/[""\\]/g,'\\$&')+'""]'; }
  function tick(){
    fetch('" + url.Replace("\\", "\\\\").Replace("'", "\\'") + @"',{cache:'no-store'}).then(function(r){ if(!r.ok) throw 0; return r.json(); }).then(function(d){
      if(net) net.textContent='';
      var f=d.fields||{};
      Object.keys(f).forEach(function(k){
        var v=String(f[k]);
        if(/\.bar$/.test(k)) document.querySelectorAll(sel('data-bar',k)+' i').forEach(function(e){ e.style.width=v+'%'; });
        else if(/\.state$/.test(k)) document.querySelectorAll(sel('data-state',k)).forEach(function(e){ e.className='state s-'+v; });
        document.querySelectorAll(sel('data-field',k)).forEach(function(e){ e.textContent=v; });
      });
      var w=d.watch||{}, ended=false;
      if(prev) Object.keys(w).forEach(function(id){ if(prev[id]==='queued'&&w[id]==='running'){ location.reload(); return; } if((prev[id]==='queued'||prev[id]==='running')&&w[id]!=='queued'&&w[id]!=='running') ended=true; });
      prev=w;
      if(ended&&!reloading){
        reloading=true;
        var b=document.getElementById('reload-bar');
        if(b){ b.style.display='block'; b.textContent='결과가 나왔습니다 — '; var a=document.createElement('a'); a.href='javascript:location.reload()'; a.textContent='새로 읽기'; b.appendChild(a); }
        setTimeout(function(){ location.reload(); },60000);
      }
      if(d.active===false) clearInterval(timer);
    }).catch(function(){ if(net) net.textContent='연결 끊김 · 다시 시도 중'; });
  }
  timer=setInterval(tick,10000); tick();
})();
</script>";

    /// <summary>Goes first in a page body: the "새로 읽기" bar and the connection note the polling script fills.</summary>
    internal const string PollSlots = "<div id=\"reload-bar\"></div><span id=\"net\"></span>";

    internal static string State(JobInfo j) => $"<span class=\"state s-{H(j.State)}\">{H(JobState.Korean(j.State))}</span>";

    public static string List(IReadOnlyList<JobInfo> jobs, (int Ahead, DateTime? Start, TimeSpan? Typical) q, bool workerEnabled)
    {
        var sb = new StringBuilder("<h1>작업 대기열</h1>");
        sb.Append("<p>").Append(workerEnabled ? "" : "<b class=\"bad\">이 서버는 작업을 실행하지 않습니다(Jobs:WorkerEnabled=false).</b> ")
          .Append($"대기 {q.Ahead}건").Append(q.Typical is { } t ? $" · 보통 한 작업 {Dur(t)}" : "").Append(q.Start is { } s ? $" · 지금 보내면 시작 예상 {H(SeoulTime.Text(s))}" : "").Append("</p>");
        sb.Append("<p class=\"muted\">작업은 ZWCAD의 ASH에서 \"서버로 보내기\"로 올립니다. 먼저 온 순서로 하나씩 실행합니다.</p>");
        JobTable(sb, jobs);
        return Page("ASH 작업 대기열", sb.ToString());
    }

    /// <summary>The job table of /jobs, also the "최근 작업" box of the home page.</summary>
    internal static void JobTable(StringBuilder sb, IReadOnlyList<JobInfo> jobs)
    {
        sb.Append("<div class=\"scroll\"><table><tr><th>작업</th><th>상태</th><th>프로젝트 / 안</th><th>모델</th><th>보낸 사람</th><th>보낸 시각(서울)</th><th>실행(회)</th><th>권장 총 대수(대)</th></tr>");
        foreach (var j in jobs)
        {
            var total = j.Decided?.Values.Sum();
            sb.Append($"<tr><td><a href=\"/jobs/{H(j.Id)}\">{H(j.Id)}</a></td><td>{State(j)}{(j.Phase != null ? " " + H(j.Phase) : "")}</td><td>{H(j.Project)} / {H(j.Plan)}</td><td>{H(j.Model)}</td>" +
                $"<td>{H(j.Submitter)} <span class=\"muted\">{H(j.Ip)}</span></td><td>{H(When(j.CreatedUtc))}</td><td class=\"num\">{j.RunsDone}{(j.RunsFailed > 0 ? $" <span class=\"bad\">(실패 {j.RunsFailed})</span>" : "")}</td>" +
                $"<td class=\"num\">{(total is { } n ? n.ToString(CultureInfo.InvariantCulture) : "-")}</td></tr>");
        }
        if (jobs.Count == 0) sb.Append("<tr><td colspan=\"8\" class=\"muted\">아직 작업이 없습니다.</td></tr>");
        sb.Append("</table></div>");
    }

    public static string Job(JobInfo j, JobView? v, string tab, string logTail, (int Ahead, DateTime? Start, TimeSpan? Typical) q, bool canCancel, IReadOnlyList<ASH.Core.Analysis.AnalysisInfo>? analyses = null,
        IReadOnlyList<FilmInfo>? films = null, string? busy = null, int parallel = 1)
    {
        var sb = new StringBuilder(PollSlots);
        if (analyses != null) AnalysisPages.JobHeader(sb, j, analyses);
        sb.Append($"<h1>{H(j.Project)} / {H(j.Plan)} <span class=\"muted\">· 모델 {H(j.Model)}</span></h1>");
        if (j.Source == JobSource.Local) sb.Append("<p><span class=\"state s-cancelled\">내 PC에서 실행한 결과</span> ash-run으로 돌린 결과를 올린 것입니다. 서버에서 다시 실행하지 않았습니다.</p>");
        sb.Append($"<p><span class=\"state s-{H(j.State)}\" data-state=\"job.state\" data-field=\"job.statetext\">{H(JobState.Korean(j.State))}</span> <span data-field=\"job.phase\">{H(j.Phase)}</span><span data-field=\"job.sep\">{(j.Phase != null ? " · " : "")}</span>작업 {H(j.Id)} · 보낸 사람 {H(j.Submitter)} ({H(j.Ip)}) · 보낸 시각 {H(When(j.CreatedUtc))}");
        if (j.StartedUtc != null) sb.Append($" · 실행 시간 {Dur(j.Duration)}");
        sb.Append($" · 모델 실행 <span data-field=\"job.runs\">{j.RunsDone}{(j.ExpectedRuns is { } er ? "/" + er : "")}회</span>{(j.RunsFailed > 0 ? $" <span class=\"bad\">(실패 {j.RunsFailed})</span>" : "")}</p>");
        if (j.State == JobState.Queued) sb.Append($"<p>{(q.Ahead == 0 ? "다음 차례입니다" : $"앞에 {q.Ahead}건이 있습니다")}{(busy != null ? $"({H(busy)}: 끝나야 시작합니다)" : "")}{(q.Start is { } s ? $" · 시작 예상 {H(SeoulTime.Text(s))}" : "")}.</p>");
        if (!j.Finished)
        {
            var lf = LiveFields.OfJob(j, q.Typical, parallel).Fields; // same texts the poll sets, so first paint and live never drift
            sb.Append($"<div class=\"prog\"><i data-bar=\"job.bar\" style=\"width:{lf["job.bar"]}%\"></i></div>");
            sb.Append($"<p>진행률 <b data-field=\"job.progress\">{H(lf["job.progress"])}</b> · 끝날 예상 <span data-field=\"job.eta\">{H(lf["job.eta"])}</span> · 한 번 평균 <span data-field=\"job.avg\">{H(lf["job.avg"])}</span></p>");
            sb.Append($"<p class=\"muted\">마지막 출력: <span data-field=\"job.last\">{H(lf["job.last"])}</span></p>");
        }
        if (j.Error != null) sb.Append($"<p class=\"bad\"><b>{(j.State == JobState.Done ? "주의" : "오류")}:</b> {H(j.Error)}</p>");
        if (canCancel && !j.Finished)
            sb.Append($"<form method=\"post\" action=\"/jobs/{H(j.Id)}/cancel\"><button>{(j.State == JobState.Queued ? "대기 취소" : "실행 취소")}</button> <span class=\"muted\">보낸 PC와 관리자만 취소할 수 있습니다. 실행 중인 모델 실행은 끝까지 돌고 새 실행만 멈춥니다.</span></form>");

        var mine = (films ?? Array.Empty<FilmInfo>()).Where(f => f.Items.Any(i => i.JobId == j.Id && i.Ok)).ToList();
        if (mine.Count > 0)
        {
            sb.Append("<div class=\"box\" style=\"margin:0 0 14px\"><b>이 버전의 영상</b><ul>");
            foreach (var f in mine)
            {
                sb.Append($"<li><a href=\"/films/{H(f.Id)}\">{H(When(f.CreatedUtc))}</a>: ");
                sb.Append(f.KeepEach ? string.Join(" · ", f.Items.Where(i => i.JobId == j.Id && i.Ok).Select(i => $"<a href=\"{AnalysisPages.FileUrl(f, i.Name)}\">{H(i.Label)}</a>"))
                    : f.JoinedOk ? $"<a href=\"{AnalysisPages.FileUrl(f, f.Joined!)}\">이어 붙인 영상</a>" : "<span class=\"muted\">이어 붙인 영상만 만들었습니다</span>");
                sb.Append("</li>");
            }
            sb.Append("</ul></div>");
        }

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
        return Page($"ASH 작업 {j.Id}", sb.ToString(), j.Finished ? null : PollScript("/api/jobs/" + j.Id, new Dictionary<string, string> { [j.Id] = j.State }));
    }

    static void Summary(StringBuilder sb, JobInfo j, JobView? v)
    {
        if (j.Decided == null || v?.Input == null)
        {
            sb.Append(j.Finished ? "<p>결과가 없습니다. 다운로드·로그 탭의 로그를 확인하세요.</p>" : "<p>실행이 끝나면 결과가 나옵니다. 실행 중에는 10초마다 진행 상황이 갱신됩니다.</p>");
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

        sb.Append($"<p><a href=\"/jobs/{H(j.Id)}/files/report.xlsx\">Excel 보고서 받기</a></p>");
        // units sit in the headers, so the cells are plain numbers (and JobReport uses the same headers)
        sb.Append("<h2>그룹별 권장 대수와 부하율</h2><table><tr><th>그룹</th><th>AGV 타입</th><th>권장(대)</th><th>범위(대)</th><th>가동률(%, 확인 실행 평균)</th><th>최소 재고/안전재고(배)</th><th>판정</th></tr>");
        foreach (var g in v.Groups)
        {
            var w = double.IsNaN(g.Util) ? 0 : Math.Min(1, g.Util);
            sb.Append($"<tr><td>{H(g.Group)}</td><td>{H(g.Type)}</td><td class=\"num\"><b>{(g.Decided?.ToString(CultureInfo.InvariantCulture) ?? "-")}</b></td><td class=\"num\">{g.Min}~{g.Max}</td>" +
                $"<td><span class=\"bar\"><i style=\"width:{F(w * 100, "0.#")}%\"></i><b style=\"left:{F(Math.Min(1, v.UtilMax) * 100, "0.#")}%\"></b></span> {P(g.Util)}</td>" +
                $"<td class=\"num\">{F(g.MinStockRatio)}</td><td class=\"{(g.AllOk ? "ok" : "bad")}\">{(g.AllOk ? "통과" : "실패: " + H(g.Reason))}</td></tr>");
        }
        sb.Append("</table><p class=\"muted\">빨간 세로선은 가동률 상한(Settings의 UtilMax)입니다.</p>");
        if (v.VehicleGroups.Count > 0)
        {
            sb.Append("<h2>차량</h2><table><tr><th>그룹</th><th>대수(대)</th><th>평균 가동률(%)</th><th>작업/대(회)</th><th>주행 거리/대(m)</th><th>거리/작업(m)</th><th>시간/작업(초)</th></tr>");
            foreach (var g in v.VehicleGroups)
                sb.Append($"<tr><td>{H(g.Group)}</td><td class=\"num\">{g.Count}</td><td class=\"num\">{P(g.Util)}</td><td class=\"num\">{F(g.Jobs, "0.#")}</td>" +
                    $"<td class=\"num\">{F(g.Distance, "0")}</td><td class=\"num\">{F(g.DistPerJob, "0.#")}</td><td class=\"num\">{F(g.BusyPerJob, "0.#")}</td></tr>");
            sb.Append("</table><p class=\"muted\">확인 실행 평균, 워밍업 이후, 권장 대수만큼 움직인 차량만 셉니다. 가동률은 작업 중 시간(호출 차량의 이동 + 작업)을 측정 시간으로 나눈 값입니다.</p>");
        }
        if (v.Materials.Count > 0)
        {
            sb.Append("<h2>자재별 리드타임·재고</h2><table><tr><th>자재</th><th>그룹</th><th>공급/기대(회)</th><th>결품 시간(초)</th><th>호출→상차 평균/최대(초)</th><th>호출→하차 평균/최대(초)</th>" +
                "<th>재고 평균(용기)</th><th>표준편차(용기)</th><th>최소(용기)</th><th>최대(용기)</th><th>주의</th></tr>");
            foreach (var m in v.Materials)
                sb.Append($"<tr><td>{H(m.Name)}</td><td>{H(m.Group)}</td><td class=\"num\">{F(m.Supplies, "0.#")} / {F(m.Expected, "0.#")}</td><td class=\"num\">{F(m.StopSec, "0")}</td>" +
                    $"<td class=\"num\">{F(m.PickAvg, "0.0")} / {F(m.PickMax, "0.0")}</td><td class=\"num\">{F(m.DelivAvg, "0.0")} / {F(m.DelivMax, "0.0")}</td>" +
                    $"<td class=\"num\">{F(m.StockAvg, "0.0")}</td><td class=\"num\">{F(m.StockStd, "0.0")}</td><td class=\"num\">{F(m.StockMin, "0.#")}</td><td class=\"num\">{F(m.StockMax, "0.#")}</td>" +
                    (m.Warn ? $"<td class=\"bad\" title=\"재고 평균 − 2σ {F(m.StockAvg - 2 * m.StockStd, "0.0")} &lt; 안전재고 {F(m.Safety)}\"><b>주의</b></td></tr>" : "<td></td></tr>"));
            sb.Append("</table><p class=\"muted\">호출→상차는 호출부터 상차 CP 도착까지, 호출→하차는 호출부터 하차 완료까지입니다. 재고는 takt마다 한 번 잰 값(용기)이고 최대·최소는 확인 실행 전체의 극값입니다. " +
                "주의는 재고 평균 − 2σ가 안전재고보다 낮은 자재로, 경고일 뿐 판정에는 쓰이지 않습니다.</p>");
        }
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
        sb.Append("<div>").Append(Map(v, "block 점유율(%)", b => b.Utilization, x => Pct(x))).Append("</div>");
        sb.Append("<div>").Append(Map(v, "block 평균 대기(초)", b => b.AvgWaitSec, x => F(x, "0.0") + "초")).Append("</div>");
        sb.Append("</div>");
        if (v.Blocks.Count > 0)
        {
            sb.Append("<h2>대기가 긴 block</h2><table><tr><th>block</th><th>평균 대기(초)</th><th>점유율(%)</th><th>통과 수(회)</th></tr>");
            foreach (var b in v.Blocks.Take(10)) sb.Append($"<tr><td>{H(b.Name)}</td><td class=\"num\">{F(b.AvgWaitSec, "0.0")}</td><td class=\"num\">{P(b.Utilization)}</td><td class=\"num\">{F(b.Passes, "0")}</td></tr>");
            sb.Append("</table>");
        }
    }

    /// <summary>The 결론 block of the analysis page: three numbers, group bars per cluster, one bottleneck map, the three strongest cross-group effects.
    /// <paramref name="small"/> = per-plan listing (smaller map). Plan B inserts its investment card at the marked point.</summary>
    public static void Conclusion(StringBuilder sb, JobInfo j, JobView v, bool small, string metric = "wait")
    {
        if (j.Decided == null || v.Input == null) { sb.Append("<p class=\"muted\">결과가 없습니다.</p>"); return; }
        int total = j.Decided.Values.Sum(), min = v.Input.Groups.Sum(g => g.MinCount), max = v.Input.Groups.Sum(g => g.MaxCount);
        var top = v.Groups.Where(g => !double.IsNaN(g.Util)).OrderByDescending(g => g.Util).FirstOrDefault();
        bool occ = metric == "occ";
        double Val(BlockView b) => occ ? b.Utilization : b.AvgWaitSec;
        var worst = v.Blocks.Where(b => !double.IsNaN(Val(b))).OrderByDescending(Val).FirstOrDefault();
        var id = H(j.Id);
        sb.Append("<div class=\"grid2\"><div>");
        sb.Append("<div class=\"cards\">");
        sb.Append($"<div class=\"card\"><div class=\"sub\">권장 총 대수</div><div class=\"big\">{total}대</div><div class=\"sub\">시험 범위 {min}~{max}대</div></div>");
        sb.Append($"<div class=\"card\"><div class=\"sub\">최대 가동률</div><div class=\"big\">{(top == null ? "-" : Pct(top.Util))}</div><div class=\"sub\">상한 {Pct(v.UtilMax)} · 그룹 {H(top?.Group ?? "-")}</div></div>");
        sb.Append($"<div class=\"card\"><div class=\"sub\">최대 병목</div><div class=\"big\">{H(worst?.Name ?? "-")}</div><div class=\"sub\">{(occ ? "점유율" : "평균 대기")} {(worst == null ? "-" : occ ? Pct(worst.Utilization) : F(worst.AvgWaitSec, "0.0") + "초")}</div></div>");
        sb.Append("</div>");
        ClusterBars(sb, j, v);
        sb.Append("<h3>서로 영향이 큰 그룹</h3>");
        var effects = v.Impact.Where(r => r.OtherGroup != r.Group && !double.IsNaN(r.UtilDelta)).OrderByDescending(r => Math.Abs(r.UtilDelta)).Take(3).ToList();
        if (effects.Count == 0) sb.Append("<p class=\"muted\">영향표 없음</p>");
        else
        {
            sb.Append("<ol>");
            foreach (var r in effects) sb.Append($"<li>{H(r.Group)} +{r.PlusOne}대 → {H(r.OtherGroup)} 가동률 {F(r.UtilDelta * 100, "+0.0;-0.0;0")}%p</li>");
            sb.Append($"</ol><p><a href=\"/jobs/{id}?tab=detail\">영향표 전체 →</a></p>");
        }
        // plan B: investment card goes here
        sb.Append("</div><div>");
        if (v.Layout == null) sb.Append("<p class=\"muted\">레이아웃을 읽지 못했습니다.</p>");
        else
        {
            var (w, h) = small ? (360d, 250d) : (520d, 360d);
            var view = small ? "plans" : "one";
            sb.Append(occ ? Map(v, "block 점유율(%)", b => b.Utilization, x => Pct(x), w, h, true) : Map(v, "block 평균 대기(초)", b => b.AvgWaitSec, x => F(x, "0.0") + "초", w, h, true));
            sb.Append($"<p class=\"mini\">기준: <a href=\"?tab=conclusion&amp;view={view}&amp;job={id}&amp;metric=wait\"{(occ ? "" : " class=\"chip sel\"")}>평균 대기</a> <a href=\"?tab=conclusion&amp;view={view}&amp;job={id}&amp;metric=occ\"{(occ ? " class=\"chip sel\"" : "")}>점유율</a></p>");
        }
        sb.Append("</div></div>");
        sb.Append($"<p><a href=\"/jobs/{id}?tab=detail\">상세 보기</a> · <a href=\"/jobs/{id}/files/report.xlsx\">Excel 보고서</a> · <a href=\"?tab=films\">이 버전의 영상</a></p>");
    }

    /// <summary>One bar line per group (utilisation vs the limit, recommended count in bold), under a "묶음: G1, G2" heading per cluster; flat when the job has none.</summary>
    internal static void ClusterBars(StringBuilder sb, JobInfo j, JobView v)
    {
        void Row(GroupView g)
        {
            var w = double.IsNaN(g.Util) ? 0 : Math.Min(1, g.Util);
            sb.Append($"<div>{H(g.Group)} · {H(g.Type)} · <span class=\"bar\"><i style=\"width:{F(w * 100, "0.#")}%{(g.AllOk ? "" : ";background:var(--bad)")}\"></i><b style=\"left:{F(Math.Min(1, v.UtilMax) * 100, "0.#")}%\"></b></span> " +
                $"가동률 {Pct(g.Util)} · <b>권장 {(g.Decided?.ToString(CultureInfo.InvariantCulture) ?? "-")}대</b> · 범위 {g.Min}~{g.Max}대" +
                (g.AllOk ? "" : $" · <span class=\"bad\">실패: {H(g.Reason)}</span>") + "</div>");
        }
        if (j.Clusters is not { Count: > 0 }) { foreach (var g in v.Groups) Row(g); return; }
        var shown = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in j.Clusters)
        {
            sb.Append($"<h3>묶음: {H(string.Join(", ", c))}</h3>");
            foreach (var g in v.Groups.Where(g => c.Contains(g.Group) && shown.Add(g.Group))) Row(g);
        }
        foreach (var g in v.Groups.Where(g => !shown.Contains(g.Group))) Row(g);
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
    internal static string Map(JobView v, string title, Func<BlockView, double> value, Func<double, string> label, double w = 520, double h = 360, bool topLabels = false)
    {
        var l = v.Layout!;
        var pts = l.Paths.SelectMany(p => new[] { p.Start, p.End }).Concat(l.Blocks.SelectMany(b => new[] { b.Rect.Min, b.Rect.Max })).Concat(l.Cps.Select(c => c.Position)).ToList();
        if (pts.Count == 0) return "<p class=\"muted\">그릴 경로가 없습니다.</p>";
        double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
        double pad = 12, sx = (w - 2 * pad) / Math.Max(1e-6, x1 - x0), sy = (h - 2 * pad) / Math.Max(1e-6, y1 - y0), s = Math.Min(sx, sy);
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
        if (topLabels)
            foreach (var b in l.Blocks.Where(b => byName.TryGetValue(b.Name, out var d) && !double.IsNaN(d) && d > 0).OrderByDescending(b => byName[b.Name]).Take(3))
                sb.Append($"<text x=\"{X((b.Rect.Min.X + b.Rect.Max.X) / 2)}\" y=\"{Y((b.Rect.Min.Y + b.Rect.Max.Y) / 2)}\" font-size=\"11\" font-weight=\"600\" text-anchor=\"middle\" fill=\"#2c2c2a\" stroke=\"#fff\" stroke-width=\"3\" paint-order=\"stroke\">{H(b.Name)} {H(label(byName[b.Name]))}</text>");
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
            sb.Append("<h2>영향표</h2><p class=\"muted\">행 그룹을 권장 대수에서 1대 늘렸을 때 열 그룹의 가동률 변화(%p). 진할수록 영향이 큽니다.</p><table><tr><th>+1대 한 그룹 → 가동률 변화(%p)</th>");
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
        if (v.Vehicles.Count > 0)
        {
            bool down = v.Vehicles.Any(x => x.Down > 0);
            sb.Append("<h2>차량별</h2><p class=\"muted\">권장 대수만큼 움직인 차량의 확인 실행 평균입니다.</p><table><tr><th>차량</th><th>그룹</th><th>가동률(%)</th><th>작업 수(회)</th><th>주행 거리(m)</th><th>거리/작업(m)</th><th>시간/작업(초)</th>" +
                (down ? "<th>고장 시간(초)</th>" : "") + "</tr>");
            foreach (var x in v.Vehicles)
                sb.Append($"<tr><td>{H(x.Name)}</td><td>{H(x.Group)}</td><td class=\"num\">{P(x.Util)}</td><td class=\"num\">{F(x.Jobs, "0.#")}</td><td class=\"num\">{F(x.Distance, "0")}</td>" +
                    $"<td class=\"num\">{F(x.DistPerJob, "0.#")}</td><td class=\"num\">{F(x.BusyPerJob, "0.#")}</td>" + (down ? $"<td class=\"num\">{F(x.Down, "0")}</td>" : "") + "</tr>");
            sb.Append("</table>");
        }
        // one row per run and group; every group's count of the run is a column, so the list sorts by any of them
        var gs = v.Input.Groups;
        var counts = v.RunCounts();
        sb.Append("<h2>실행 목록</h2><p class=\"muted\">제목을 누르면 그 열로 정렬합니다(다시 누르면 반대로). \"받기\"는 그 실행의 모델과 조건(params.txt)을 zip으로 받아 AutoMod가 있는 PC에서 run.bat으로 창을 띄워 볼 수 있게 합니다.</p>");
        sb.Append("<table class=\"sort\"><thead><tr>").Append(Th("실행"));
        foreach (var g in gs) sb.Append(Th(H(g.Group) + " 대수(대)"));
        sb.Append(Th("시드")).Append(Th("반복(회차)")).Append(Th("그룹")).Append(Th("판정")).Append(Th("가동률(%)")).Append(Th("최소 재고/안전재고(배)")).Append(Th("공급(회)"))
          .Append(Th("기대 공급(회)")).Append(Th("결품 시간(초)")).Append(Th("교착(회)")).Append(Th("실행 시간(초)")).Append("<th>모델</th></tr></thead><tbody>");
        foreach (var r in v.Rows.Take(1000))
        {
            sb.Append($"<tr><td>{H(r.RunId)}</td>");
            var c = counts[r.RunId];
            foreach (var g in gs) sb.Append($"<td class=\"num\">{(c.TryGetValue(g.Group, out var n) ? n : g.MinCount)}</td>");
            sb.Append($"<td class=\"num\">{r.Seed}</td><td class=\"num\">{r.Seed - v.Input.Settings.SeedBase + 1}</td><td>{H(r.Group)}</td><td class=\"{(r.Ok ? "ok" : "bad")}\">{(r.Ok ? "통과" : H(r.Reason))}</td>" +
                $"<td class=\"num\">{P(r.Utilization)}</td><td class=\"num\">{F(r.MinStockRatio)}</td><td class=\"num\">{r.Supplies}</td><td class=\"num\">{F(r.Expected, "0.#")}</td><td class=\"num\">{F(r.StopTimeSec, "0")}</td><td class=\"num\">{r.Deadlocks}</td><td class=\"num\">{F(r.DurationSec, "0")}</td>" +
                $"<td><a href=\"/jobs/{H(j.Id)}/runs/{Uri.EscapeDataString(r.RunId)}/model.zip\" download>받기</a></td></tr>");
        }
        sb.Append("</tbody></table>").Append(SortScript);
        if (v.Rows.Count > 1000) sb.Append($"<p class=\"muted\">처음 1000행만 보여 주고 그 안에서 정렬합니다(전체 {v.Rows.Count}행은 summary.tsv로 받으세요).</p>");
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
        sb.Append("<h2>올린 파일 (SHA-256)</h2><table><tr><th>파일</th><th>크기(바이트)</th><th>SHA-256</th></tr>");
        foreach (var f in j.Files) sb.Append($"<tr><td>{H(f.Path)}</td><td class=\"num\">{f.Size:N0}</td><td style=\"font-family:monospace;font-size:12px\">{H(f.Sha256)}</td></tr>");
        sb.Append("</table>");
        if (v?.Input != null)
        {
            sb.Append("<h2>그룹</h2><table><tr><th>그룹</th><th>타입</th><th>최소(대)</th><th>최대(대)</th><th>대기 CP</th></tr>");
            foreach (var g in v.Input.Groups) sb.Append($"<tr><td>{H(g.Group)}</td><td>{H(g.Type)}</td><td class=\"num\">{g.MinCount}</td><td class=\"num\">{g.MaxCount}</td><td>{H(string.Join(", ", g.StandbyCPs))}</td></tr>");
            sb.Append("</table><h2>자재</h2><table><tr><th>자재</th><th>그룹</th><th>형태</th><th>T/T(초)</th><th>용기당 수량(개)</th><th>호출 임계(용기)</th></tr>");
            foreach (var m in v.Input.Materials) sb.Append($"<tr><td>{H(m.Material)}</td><td>{H(m.Group)}</td><td>{H(m.Form.Length == 0 ? "Box" : m.Form)}</td><td class=\"num\">{F(m.TaktTime)}</td><td class=\"num\">{F(m.UnitsPerContainer)}</td><td class=\"num\">{F(m.CallThreshold)}</td></tr>");
            sb.Append("</table>");
        }
    }

    static void FilesTab(StringBuilder sb, JobInfo j, string logTail)
    {
        string Link(string name, string label) => $"<li><a href=\"/jobs/{H(j.Id)}/files/{name}\">{label}</a></li>";
        sb.Append("<h2>내려받기</h2><ul>");
        sb.Append(Link("report.xlsx", "report.xlsx — Excel 보고서(요약, 대수별 가동률, 실행 목록, 영향표, block, 입력)"));
        sb.Append(Link("summary.tsv", "summary.tsv — 실행별·그룹별 결과"));
        sb.Append(Link("impact.tsv", "impact.tsv — 영향표"));
        if (j.RunsPurgedUtc is { } purged) sb.Append($"<li class=\"muted\">runs.zip — 실행 원본은 보관 기간이 지나 {H(SeoulTime.Text(purged))}에 정리되었습니다</li>");
        else sb.Append(Link("runs.zip", "runs.zip — 모든 실행 폴더(결과·보고서·메시지)"));

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
