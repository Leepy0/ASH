using System.Text;
using System.Text.Json.Nodes;
using ASH.Core;
using static ASH.Server.JobPages;

namespace ASH.Server;

/// <summary>The server status section and the admin pages (/admin/settings, login, restart). Same shared CSS and header as the job pages.</summary>
public static class ServerPages
{
    public sealed record StatusData(ReleaseStore Releases, GitHubSync GitHub, ServerUpdater Updater, RunnerStore Runner, IReadOnlyList<JobInfo> Jobs,
        (int Ahead, DateTime? Start, TimeSpan? Typical) Queue, bool WorkerOn, IReadOnlyList<ASH.Core.Analysis.AnalysisInfo>? Analyses = null);

    static string Card(string label, string big, string sub, string cls = "") =>
        $"<div class=\"card\"><div class=\"sub\">{label}</div><div class=\"big {cls}\">{big}</div><div class=\"sub\">{sub}</div></div>";

    /// <summary>Page-only CSS of the admin pages (tabs, compact status, settings rows, switch, sticky bar, login card). The 기준 정보 page adds its own.</summary>
    internal const string AdminCss = @"<style>
.adm-h{display:flex;align-items:center;gap:14px;flex-wrap:wrap;margin-bottom:12px}.adm-h h1{margin:0}
.segc form{display:contents}.segc .out{color:var(--sub2);min-height:0}
.cards.cmp{grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:10px}.cards.cmp .card{padding:10px 12px}.cards.cmp .big{font-size:22px}
.st{gap:10px;align-items:start}.tag[hidden]{display:none}.st .box{padding:12px 16px}.st h2{font-size:15px;margin:0 0 8px}.st dl.kv{font-size:13px;gap:4px 12px}.st details summary{font-size:13px}
.sec{font-size:13px;font-weight:600;color:var(--sub2);margin:18px 4px 6px}
.sgrp{background:#fff;border:1px solid var(--line);border-radius:12px}.scols{display:grid;grid-template-columns:repeat(auto-fit,minmax(680px,1fr));gap:4px 18px;align-items:start}.scols>div>.sec:first-child{margin-top:8px}
.srow{display:flex;align-items:center;gap:12px;min-height:46px;padding:6px 14px;border-top:1px solid var(--line)}.sgrp>.srow:first-child{border-top:0}
.srow label{flex:1;min-width:170px;display:flex;align-items:center;gap:6px;flex-wrap:wrap;font-size:14px}
.srow .ctl{display:flex;align-items:center;gap:8px;justify-content:flex-end;flex:0 1 380px;min-width:0}
.srow .ctl input[type=text]{max-width:none}.srow .ctl input[type=number]{width:96px;text-align:right}.srow .ctl .pick{flex:none;white-space:nowrap}
.srow .unit{color:var(--sub2);font-size:13px;white-space:nowrap}
.srow.chg{background:#fffbea}.srow-m{min-height:0;padding:0 14px 8px;border-top:0}.srow-m .msg{margin:4px 0;flex:1}
.info{display:inline-grid;place-items:center;width:18px;height:18px;border-radius:50%;border:1px solid var(--sub2);color:var(--sub2);font-size:12px;font-weight:600;cursor:help;line-height:1}
.sw{position:relative;display:inline-block;width:46px;height:28px;flex:none}
.sw input{appearance:none;-webkit-appearance:none;width:46px;height:28px;margin:0;border-radius:14px;background:#8e8e93;cursor:pointer;transition:background .15s}
.sw input::after{content:'';position:absolute;top:2px;left:2px;width:24px;height:24px;border-radius:50%;background:#fff;box-shadow:0 1px 3px rgba(0,0,0,.3);transition:transform .15s}
.sw input:checked{background:var(--green)}.sw input:checked::after{transform:translateX(18px)}
.sbar{position:sticky;bottom:-22px;margin:18px -26px -22px;padding:10px 26px;background:#f5f5f7;border-top:1px solid var(--line2);display:flex;align-items:center;gap:10px;z-index:5}
.sbar .sp{flex:1;color:var(--sub2);font-size:13px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.login{min-height:70vh;display:grid;place-items:center}.login .box{width:360px;max-width:100%;margin:0}.login h1{font-size:20px;text-align:center;margin:0 0 14px}
.login input{max-width:none}.login .actions button{flex:1}.login .help{margin-top:8px}
.note{max-width:640px;margin:40px auto 0}
</style>";

    static readonly (string Key, string Name, string Href)[] Tabs = { ("jobs", "작업 현황", "/admin/settings"), ("catalog", "기준 정보", "/admin/types"), ("server", "서버 설정", "/admin/settings?tab=server") };

    /// <summary>The admin tab bar (a segmented control) shared by /admin/settings (작업 현황, 서버 설정) and /admin/types (기준 정보), with the logout button at its end.</summary>
    public static string AdminTabs(string on)
    {
        var sb = new StringBuilder("<div class=\"segc tabs admin-tabs\">");
        foreach (var (k, name, href) in Tabs) sb.Append($"<a href=\"{href}\"{(k == on ? " class=\"on\" aria-current=\"page\"" : "")}>{name}</a>");
        return sb.Append("<form method=\"post\" action=\"/admin/logout\"><button class=\"out\">로그아웃</button></form></div>").ToString();
    }

    /// <summary>The page heading and tabs every admin page starts with, with the page-only CSS.</summary>
    public static string AdminHead(string on) => AdminCss + "<div class=\"adm-h\"><h1>관리</h1>" + AdminTabs(on) + "</div>";

    /// <summary>작업 현황 tab: the running/queue/version cards and the five latest jobs.</summary>
    public static string JobsStatus(StatusData d)
    {
        var (s, u) = (d.Releases, d.Updater);
        var cur = s.GetCurrent()?.Manifest;
        var running = d.Jobs.FirstOrDefault(j => j.State == JobState.Running);
        int queued = d.Jobs.Count(j => j.State == JobState.Queued);
        var sb = new StringBuilder("<div class=\"cards cmp\">");

        sb.Append(Card("지금 실행", running == null ? "없음" : State(running),
            running == null ? (d.WorkerOn ? "작업 실행 켜짐" : "<span class=\"bad\">작업 실행 꺼짐</span>")
                : $"<a href=\"/jobs/{H(running.Id)}\">{H(JobName(running, d.Analyses))}</a>{(running.Phase != null ? " · " + H(running.Phase) : "")} · 실행 {running.RunsDone}회"));
        sb.Append(Card("대기", $"{queued}건", d.Queue.Typical is { } t ? $"보통 한 작업 {Dur(t)}" + (d.Queue.Start is { } st ? $" · 시작 예상 {H(SeoulTime.Text(st))}" : "") : "먼저 온 순서로 하나씩"));
        sb.Append(Card("ASH 배포 버전", cur == null ? "-" : H(cur.Version), cur == null ? "게시된 버전이 없습니다." : "게시 " + H(SeoulTime.FromIso(cur.Published))));
        sb.Append(Card("서버 프로그램", H(u.Current), H(u.LastResult)));
        sb.Append("</div>");

        sb.Append("<section class=\"box\"><div class=\"box-head\"><h2>최근 작업</h2><a href=\"/admin/jobs\">작업 대기열(관리자) →</a></div>");
        JobTable(sb, d.Jobs.Take(5).ToList(), analyses: d.Analyses);
        sb.Append("</section>");
        return sb.ToString();
    }

    /// <summary>The 배포 and 서버 상태 boxes at the top of the 서버 설정 tab.</summary>
    public static string ServerStatus(StatusData d)
    {
        var (s, g, u, r) = (d.Releases, d.GitHub, d.Updater, d.Runner);
        var cur = s.GetCurrent()?.Manifest;
        var sb = new StringBuilder("<div class=\"grid st\"><section class=\"box\"><h2>배포</h2><dl class=\"kv\">");
        if (cur == null) sb.Append("<dt>ASH</dt><dd>게시된 버전이 없습니다.</dd>");
        else sb.Append($"<dt>현재 버전</dt><dd><b>{H(cur.Version)}</b></dd><dt>최소 지원</dt><dd>{H(cur.MinSupported)}</dd>")
               .Append($"<dt>게시 시각(서울)</dt><dd>{H(SeoulTime.FromIso(cur.Published))}</dd>")
               .Append($"<dt>설치 파일</dt><dd><a href=\"/download/{Uri.EscapeDataString(cur.File)}\">{H(cur.File)}</a> <span class=\"muted\">({cur.Size:N0} bytes)</span></dd>");
        sb.Append("<dt>Runner(ash-run)</dt><dd>").Append(r.Current is { } rc ? H(rc.Version) + $" <span class=\"muted\">({rc.Size:N0} bytes)</span>" : "게시된 버전 없음")
          .Append("<br><span class=\"muted\">마지막 확인: ").Append(H(g.RunnerResult)).Append("</span></dd></dl>");
        var history = s.History();
        if (history.Count > 0)
        {
            sb.Append($"<details><summary>이전 버전 {history.Count}개</summary><ul>");
            foreach (var h in history) sb.Append($"<li>{H(h.Version)} <span class=\"muted\">({H(SeoulTime.FromIso(h.Published))})</span></li>");
            sb.Append("</ul></details>");
        }
        sb.Append("</section>");

        sb.Append("<section class=\"box\"><h2>서버 상태</h2><dl class=\"kv\">");
        sb.Append("<dt>서버 프로그램</dt><dd>").Append(H(u.Current)).Append(" — 업데이트: ").Append(H(u.LastResult)).Append("</dd>");
        if (u.PreviousRun != null) sb.Append("<dt>지난 업데이트</dt><dd>").Append(H(u.PreviousRun)).Append("</dd>");
        sb.Append("<dt>GitHub 가져오기</dt><dd>").Append(g.BaseUrl == null ? "꺼짐"
            : H(g.BaseUrl) + $" <span class=\"muted\">({g.Interval.TotalMinutes:0}분마다, DNS {(g.DnsServers.Count == 0 ? "PC 기본" : H(string.Join(", ", g.DnsServers)))})</span>").Append("</dd>");
        sb.Append("<dt>마지막 확인(서울)</dt><dd>").Append(g.LastCheckUtc is { } lc ? H(SeoulTime.Text(lc)) + " — " + H(g.LastResult) : H(g.LastResult)).Append("</dd>");
        sb.Append("<dt>무결성 확인</dt><dd>").Append(s.Key == null
            ? (s.AllowUnsigned ? "없음 — PC가 github.com 릴리스와 대조해 확인" : "<b class=\"bad\">서명 키 없음 — 업로드 거절(Update:AllowUnsigned=false)</b>")
            : H(s.Key.KeyId)).Append("</dd>");
        sb.Append("<dt>수동 업로드</dt><dd>").Append(s.UploadsEnabled ? "가능" : "꺼짐 <span class=\"muted\">(Admin:Password가 규칙에 맞지 않음, GitHub 가져오기만 사용)</span>").Append("</dd>");
        // the clock behind every time on this page: if it differs from the wall clock the PC's own date and time is wrong
        sb.Append("<dt>서버 시각(서울)</dt><dd>").Append(H(SeoulTime.NowText)).Append(" <span class=\"muted\">· PC 표준 시간대 ").Append(H(TimeZoneInfo.Local.Id)).Append("</span></dd>");
        sb.Append("</dl></section></div>");
        return sb.ToString();
    }

    /// <summary>The update banner (GET /admin/update-banner, admins only) every page loads into #upd: a staged version with "지금"/"작업 끝나고",
    /// or the armed one with 취소. <paramref name="busy"/> = work runs now (지금 cuts it off; it continues after the restart).</summary>
    public static string UpdateBanner(string version, bool armed, bool busy)
    {
        static string Act(string act, string label) =>
            $"<form method=\"post\" action=\"/admin/update\"><input type=\"hidden\" name=\"act\" value=\"{act}\"><input type=\"hidden\" name=\"back\" value=\"\"><button class=\"btn\">{label}</button></form>";
        var sb = new StringBuilder("<style>.upd{display:flex;align-items:center;gap:10px;flex-wrap:wrap;background:var(--blue-b);color:var(--blue-d);border-radius:10px;padding:8px 14px;margin:0 0 12px;font-size:14px}" +
            ".upd.armed{background:var(--orange-b);color:var(--orange)}.upd form{margin:0}.upd details{position:relative}.upd summary{list-style:none;cursor:pointer}.upd summary::-webkit-details-marker{display:none}" +
            ".upd .m{position:absolute;z-index:20;top:calc(100% + 6px);left:0;background:#fff;border:1px solid var(--line);border-radius:10px;padding:10px;display:flex;flex-direction:column;gap:6px;min-width:240px;box-shadow:0 4px 18px rgba(0,0,0,.15)}" +
            ".upd .m .btn{width:100%}.upd .w{color:var(--red);font-size:13px}</style>");
        if (armed) return sb.Append($"<div class=\"upd armed\" role=\"status\"><b>작업 끝나면 업데이트 {H(version)}</b>{Act("cancel", "취소")}</div>").ToString();
        sb.Append($"<div class=\"upd\" role=\"status\"><b>업데이트 {H(version)} 준비됨</b><details><summary class=\"btn pri\">업데이트</summary><div class=\"m\">");
        sb.Append(Act("now", "지금 업데이트"));
        if (busy) sb.Append("<span class=\"w\">실행 중 작업 중단 · 재시작 뒤 이어서</span>");
        return sb.Append(Act("after", "진행 중 작업 끝나고 업데이트")).Append("</div></details></div>").ToString();
    }

    public static string Message(string title, string html, string? refreshTo = null) =>
        Page(title, AdminCss + $"<div class=\"note\"><h1>{H(title)}</h1>{html}</div>" + (refreshTo != null ? $"<script>setTimeout(function(){{location.href='{refreshTo}'}},15000)</script>" : ""), nav: "admin");

    /// <param name="back">a path on this site to go to after the login (default: the settings page)</param>
    public static string Login(bool uploadsEnabled, string? error = null, string? back = null, string title = "서버 설정")
    {
        var sb = new StringBuilder(AdminCss + "<div class=\"login\"><section class=\"box\">");
        sb.Append($"<h1>{H(title)}</h1>");
        if (!uploadsEnabled)
        {
            sb.Append("<div class=\"msg warn\">웹 설정이 꺼져 있습니다. 서버 PC의 appsettings.json에 <code>Admin:Password</code>(4~20자, 영문·숫자·특수문자, 공백·한글 없음)를 바르게 적고 서비스를 다시 시작하면 쓸 수 있습니다.</div></section></div>");
            return Page("ASH 서버 설정", sb.ToString(), nav: "admin");
        }
        if (error != null) sb.Append($"<div class=\"msg bad\" role=\"alert\">{H(error)}</div>");
        sb.Append("<form method=\"post\" action=\"/admin/login\">")
          .Append(back == null ? "" : $"<input type=\"hidden\" name=\"back\" value=\"{H(back)}\">")
          .Append("<label for=\"token\"><b>관리자 암호</b></label><div style=\"margin-top:6px\"><input type=\"password\" id=\"token\" name=\"token\" autocomplete=\"current-password\" required autofocus>")
          .Append("<div class=\"help\">appsettings.json의 Admin:Password 값입니다. 확인되면 이 브라우저는 8시간 동안 로그인 상태로 남습니다.</div></div>")
          .Append("<div class=\"actions\"><button class=\"primary\">확인</button></div></form></section></div>");
        return Page("ASH 서버 설정", sb.ToString(), nav: "admin");
    }

    public sealed record SettingsView(JsonObject File, string FilePath, Func<string, string?>? Submitted, SettingsSave? Saved, string? RestartBlocked, JobInfo? Running);

    static readonly System.Text.RegularExpressions.Regex UnitInLabel = new(@"^(.*?)\((분|시간|일)\)$");

    /// <summary>One setting row: label + (i) tooltip + restart tag on the left, the control on the right. A bool posts through ONE hidden input that the script keeps in step with the (unnamed) switch checkbox.</summary>
    static void SettingRow(StringBuilder sb, SettingDef d, string value)
    {
        var id = "f-" + d.Key.Replace(':', '-');
        var m = UnitInLabel.Match(d.Label);
        var label = m.Success ? m.Groups[1].Value : d.Label;
        var def = d.Kind == SettingKind.Bool ? (d.Default == "true" ? "켬" : "끔") : d.Default.Length == 0 ? "(비움)" : d.Default;
        var tip = $"{d.Help}\n기본값 {def}{(d.Kind == SettingKind.Int ? $" · 범위 {d.Min}~{d.Max}" : "")} · {d.Key}";
        var rs = d.Live ? "0" : "1";
        sb.Append($"<div class=\"srow\"><label for=\"{id}\">{H(label)} <span class=\"info\" tabindex=\"0\" role=\"img\" title=\"{H(tip)}\" aria-label=\"{H(tip)}\">i</span>")
          .Append(d.Live ? "" : "<span class=\"tag rs\">재시작 필요</span>").Append("</label><div class=\"ctl\">");
        switch (d.Kind)
        {
            case SettingKind.Bool:
                var on = !value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase);
                var cur = on ? "true" : "false";
                sb.Append($"<span class=\"sw\"><input type=\"hidden\" name=\"{H(d.Key)}\" value=\"{cur}\" data-orig=\"{cur}\" data-rs=\"{rs}\">")
                  .Append($"<input type=\"checkbox\" role=\"switch\" id=\"{id}\"{(on ? " checked" : "")}></span>");
                break;
            case SettingKind.Int:
                sb.Append($"<input type=\"number\" id=\"{id}\" name=\"{H(d.Key)}\" value=\"{H(value)}\" data-orig=\"{H(value)}\" data-rs=\"{rs}\" min=\"{d.Min}\" max=\"{d.Max}\" step=\"1\" required>");
                if (m.Success) sb.Append($"<span class=\"unit\">{m.Groups[2].Value}</span>");
                break;
            default:
                sb.Append($"<input type=\"text\" id=\"{id}\" name=\"{H(d.Key)}\" value=\"{H(value)}\" data-orig=\"{H(value)}\" data-rs=\"{rs}\" spellcheck=\"false\">");
                if (d.Pick != null) sb.Append($"<button type=\"button\" class=\"btn pick\" data-pick=\"{d.Pick}\" data-for=\"{id}\" data-title=\"{H(label)}\">찾아보기…</button>");
                break;
        }
        sb.Append("</div></div>");
    }

    // counts the controls that differ from their saved value (data-orig); a switch checkbox has no name, its hidden twin carries the true/false
    const string SettingsScript = @"<script>
(function(){
  var f=document.getElementById('sf'); if(!f) return;
  var n=document.getElementById('nchg'), r=document.getElementById('nrs');
  function sync(){ f.querySelectorAll('input[type=checkbox]').forEach(function(t){ t.previousElementSibling.value=t.checked?'true':'false'; }); }
  function upd(){
    var c=0,x=0;
    f.querySelectorAll('[data-orig]').forEach(function(e){
      var d=e.value!==e.getAttribute('data-orig'), row=e.closest('.srow'); if(row) row.classList.toggle('chg',d);
      if(d){ c++; if(e.getAttribute('data-rs')==='1') x++; }
    });
    n.textContent=c; r.textContent='재시작 '+x; r.hidden=x===0;
  }
  f.addEventListener('input',upd);
  f.addEventListener('change',function(){ sync(); upd(); });
  f.addEventListener('reset',function(){ setTimeout(function(){ sync(); upd(); },0); });
  sync(); upd();
})();
</script>";

    /// <param name="tab">"jobs" (작업 현황, the default) or "server" (서버 설정: status, the settings form, restart)</param>
    public static string Settings(SettingsView v, StatusData status, string tab)
    {
        if (tab != "server") return Page("ASH 서버 설정", AdminHead("jobs") + "<div class=\"panel\">" + JobsStatus(status) + "</div>", nav: "admin");
        var sb = new StringBuilder(AdminHead("server")).Append("<div class=\"panel\">").Append(ServerStatus(status));
        if (v.Saved is { } r)
        {
            if (r.Errors.Count > 0) sb.Append($"<div class=\"msg bad\" role=\"alert\">저장하지 않았습니다. 아래 빨간 항목 {r.Errors.Count}개를 고치세요.</div>");
            else if (r.Changed.Count == 0) sb.Append("<div class=\"msg ok\">바뀐 값이 없습니다.</div>");
            else
            {
                var live = r.Changed.Where(d => d.Live).Select(d => d.Label).ToList();
                var restart = r.Changed.Where(d => !d.Live).Select(d => d.Label).ToList();
                sb.Append($"<div class=\"msg ok\">저장했습니다({r.Changed.Count}개).");
                if (live.Count > 0) sb.Append(" 바로 적용: ").Append(H(string.Join(", ", live))).Append('.');
                if (restart.Count > 0) sb.Append(" <b>다시 시작해야 적용: ").Append(H(string.Join(", ", restart))).Append(".</b> 아래 \"서비스 다시 시작\"을 누르세요.");
                if (r.Backup != null) sb.Append($"<br><span class=\"muted\">이전 파일: {H(Path.GetFileName(r.Backup))}</span>");
                sb.Append("</div>");
            }
        }

        // sections side by side on a wide screen instead of one narrow column
        sb.Append("<form method=\"post\" action=\"/admin/settings\" id=\"sf\"><div class=\"scols\">");
        foreach (var section in ServerSettings.All.GroupBy(d => ServerSettings.Section(d.Key)))
        {
            sb.Append($"<div><div class=\"sec\">{H(section.Key)}</div><div class=\"sgrp\">");
            foreach (var d in section)
            {
                var value = v.Submitted?.Invoke(d.Key) ?? AppSettingsFile.Get(v.File, d.Key) ?? d.Default;
                SettingRow(sb, d, value);
                string? err = null;
                if (v.Saved?.Errors.TryGetValue(d.Key, out var e) == true) err = e;
                if (err != null) sb.Append($"<div class=\"srow srow-m\"><div class=\"msg bad\" role=\"alert\">{H(err)}</div></div>");
                else if (ServerSettings.Warning(d, value.Trim()) is { } w) sb.Append($"<div class=\"srow srow-m\"><div class=\"msg warn\">{H(w)}</div></div>");
            }
            sb.Append("</div></div>");
        }
        sb.Append("</div><div class=\"sbar\"><span>변경 <b id=\"nchg\">0</b>개</span><span class=\"tag rs\" id=\"nrs\" hidden>재시작 0</span>")
          .Append($"<span class=\"sp\" title=\"저장할 때 이전 파일을 appsettings.json.날짜.bak으로 남깁니다. Admin:Password, Update:PublicKeyPem, Logging은 웹에서 바꾸지 않습니다(서버 PC에서 파일을 직접 고침).\">{H(v.FilePath)}</span>")
          .Append("<button type=\"reset\" class=\"btn\">되돌리기</button><button class=\"primary\">저장</button></div></form>");

        sb.Append("<section class=\"box\"><h2>서비스 다시 시작</h2>");
        if (v.RestartBlocked != null) sb.Append($"<p>여기서는 다시 시작할 수 없습니다: {H(v.RestartBlocked)}</p>");
        else
        {
            sb.Append("<form method=\"post\" action=\"/admin/restart\"><p>서비스를 멈췄다가 다시 시작합니다(약 10~20초). 그동안 페이지와 업로드가 응답하지 않습니다.</p>");
            if (v.Running != null)
                sb.Append($"<div class=\"msg warn\">지금 실행 중인 작업이 있습니다: <a href=\"/jobs/{H(v.Running.Id)}\">{H(v.Running.Project)} / {H(v.Running.Plan)}</a>. 다시 시작하면 이 작업은 중단되고 실패로 남습니다(대기 중인 작업은 이어서 실행).")
                  .Append("<br><label><input type=\"checkbox\" name=\"confirm\" value=\"1\" required> 실행 중인 작업을 중단하고 다시 시작합니다</label></div>");
            sb.Append("<div class=\"actions\"><button class=\"primary\">서비스 다시 시작</button></div></form>");
        }
        sb.Append("</section>").Append(FsPicker.Html).Append("</div>");
        return Page("ASH 서버 설정", sb.ToString(), SettingsScript + FsPicker.Script, "admin");
    }
}
