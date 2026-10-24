using System.Text;
using System.Text.Json.Nodes;
using ASH.Core;
using static ASH.Server.JobPages;

namespace ASH.Server;

/// <summary>The server status section and the admin pages (/admin/settings, login, restart). Same shared CSS and header as the job pages.</summary>
public static class ServerPages
{
    public sealed record StatusData(ReleaseStore Releases, GitHubSync GitHub, ServerUpdater Updater, RunnerStore Runner, IReadOnlyList<JobInfo> Jobs,
        (int Ahead, DateTime? Start, TimeSpan? Typical) Queue, bool WorkerOn);

    static string Card(string label, string big, string sub, string cls = "") =>
        $"<div class=\"card\"><div class=\"sub\">{label}</div><div class=\"big {cls}\">{big}</div><div class=\"sub\">{sub}</div></div>";

    public static string Status(StatusData d)
    {
        var (s, g, u, r) = (d.Releases, d.GitHub, d.Updater, d.Runner);
        var cur = s.GetCurrent()?.Manifest;
        var running = d.Jobs.FirstOrDefault(j => j.State == JobState.Running);
        int queued = d.Jobs.Count(j => j.State == JobState.Queued);
        var sb = new StringBuilder("<section class=\"box\"><h2>서버 현황</h2><div class=\"cards\">");

        sb.Append(Card("지금 실행", running == null ? "없음" : State(running),
            running == null ? (d.WorkerOn ? "작업 실행 켜짐" : "<span class=\"bad\">작업 실행 꺼짐</span>")
                : $"<a href=\"/jobs/{H(running.Id)}\">{H(running.Project)} / {H(running.Plan)}</a>{(running.Phase != null ? " · " + H(running.Phase) : "")} · 실행 {running.RunsDone}회"));
        sb.Append(Card("대기", $"{queued}건", d.Queue.Typical is { } t ? $"보통 한 작업 {Dur(t)}" + (d.Queue.Start is { } st ? $" · 시작 예상 {H(SeoulTime.Text(st))}" : "") : "먼저 온 순서로 하나씩"));
        sb.Append(Card("ASH 배포 버전", cur == null ? "-" : H(cur.Version), cur == null ? "게시된 버전이 없습니다." : "게시 " + H(SeoulTime.FromIso(cur.Published))));
        sb.Append(Card("서버 프로그램", H(u.Current), H(u.LastResult)));
        sb.Append("</div>");

        sb.Append("<section class=\"box\"><div class=\"box-head\"><h2>최근 작업</h2><a href=\"/admin/jobs\">작업 대기열(관리자) →</a></div>");
        JobTable(sb, d.Jobs.Take(5).ToList());
        sb.Append("</section>");

        sb.Append("<div class=\"grid\"><section class=\"box\"><h2>배포</h2><dl class=\"kv\">");
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
        sb.Append("<dt>서버 프로그램</dt><dd>").Append(H(u.Current)).Append(" — 자동 업데이트: ").Append(H(u.LastResult)).Append("</dd>");
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
        sb.Append("</dl></section></div></section>");
        return sb.ToString();
    }

    public static string Message(string title, string html, string? refreshTo = null) =>
        Page(title, $"<h1>{H(title)}</h1>{html}" + (refreshTo != null ? $"<script>setTimeout(function(){{location.href='{refreshTo}'}},15000)</script>" : ""));

    /// <param name="back">a path on this site to go to after the login (default: the settings page)</param>
    public static string Login(bool uploadsEnabled, string? error = null, string? back = null, string title = "서버 설정")
    {
        var sb = new StringBuilder($"<h1>{H(title)}</h1>");
        if (!uploadsEnabled)
        {
            sb.Append("<div class=\"msg warn\">웹 설정이 꺼져 있습니다. 서버 PC의 appsettings.json에 <code>Admin:Password</code>(4~20자, 영문·숫자·특수문자, 공백·한글 없음)를 바르게 적고 서비스를 다시 시작하면 쓸 수 있습니다.</div>");
            return Page("ASH 서버 설정", sb.ToString());
        }
        if (error != null) sb.Append($"<div class=\"msg bad\">{H(error)}</div>");
        sb.Append("<section class=\"box\"><h2>관리자 확인</h2><form method=\"post\" action=\"/admin/login\">")
          .Append(back == null ? "" : $"<input type=\"hidden\" name=\"back\" value=\"{H(back)}\">")
          .Append("<div class=\"field\"><label for=\"token\">관리자 암호</label><div><input type=\"password\" id=\"token\" name=\"token\" autocomplete=\"current-password\" required>")
          .Append("<div class=\"help\">appsettings.json의 Admin:Password 값입니다. 확인되면 이 브라우저는 8시간 동안 로그인 상태로 남습니다.</div></div></div>")
          .Append("<div class=\"actions\"><button class=\"primary\">확인</button></div></form></section>");
        return Page("ASH 서버 설정", sb.ToString());
    }

    public sealed record SettingsView(JsonObject File, string FilePath, Func<string, string?>? Submitted, SettingsSave? Saved, string? RestartBlocked, JobInfo? Running);

    public static string Settings(SettingsView v, string statusHtml)
    {
        var sb = new StringBuilder("<h1>서버 설정</h1>");
        sb.Append(statusHtml);
        sb.Append("<p><a href=\"/admin/types\">타입·투자비 관리</a></p>");
        sb.Append($"<p class=\"muted\">파일: <code>{H(v.FilePath)}</code> · <span class=\"tag\">바로 적용</span> 저장하면 다음 작업·다음 확인부터 적용 · <span class=\"tag rs\">재시작 필요</span> 서비스를 다시 시작해야 적용</p>");
        if (v.Saved is { } r)
        {
            if (r.Errors.Count > 0) sb.Append($"<div class=\"msg bad\">저장하지 않았습니다. 아래 빨간 항목 {r.Errors.Count}개를 고치세요.</div>");
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

        sb.Append("<form method=\"post\" action=\"/admin/settings\">");
        foreach (var section in ServerSettings.All.GroupBy(d => ServerSettings.Section(d.Key)))
        {
            sb.Append($"<section class=\"box\"><h2>{H(section.Key)}</h2>");
            foreach (var d in section)
            {
                var value = v.Submitted?.Invoke(d.Key) ?? AppSettingsFile.Get(v.File, d.Key) ?? d.Default;
                var id = "f-" + d.Key.Replace(':', '-');
                string? err = null;
                if (v.Saved?.Errors.TryGetValue(d.Key, out var e) == true) err = e;
                sb.Append($"<div class=\"field\"><label for=\"{id}\">{H(d.Label)}</label><div>");
                switch (d.Kind)
                {
                    case SettingKind.Bool:
                        bool on = !value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase);
                        sb.Append($"<select id=\"{id}\" name=\"{H(d.Key)}\"><option value=\"true\"{(on ? " selected" : "")}>켬</option><option value=\"false\"{(on ? "" : " selected")}>끔</option></select>");
                        break;
                    case SettingKind.Int:
                        sb.Append($"<input type=\"number\" id=\"{id}\" name=\"{H(d.Key)}\" value=\"{H(value)}\" min=\"{d.Min}\" max=\"{d.Max}\" step=\"1\" required>");
                        break;
                    default:
                        sb.Append($"<input type=\"text\" id=\"{id}\" name=\"{H(d.Key)}\" value=\"{H(value)}\" spellcheck=\"false\">");
                        break;
                }
                sb.Append(d.Live ? "<span class=\"tag\">바로 적용</span>" : "<span class=\"tag rs\">재시작 필요</span>");
                var def = d.Kind == SettingKind.Bool ? (d.Default == "true" ? "켬" : "끔") : d.Default.Length == 0 ? "(비움)" : d.Default;
                sb.Append($"<div class=\"help\">{H(d.Help)} 기본값 {H(def)}{(d.Kind == SettingKind.Int ? $" · 범위 {d.Min}~{d.Max}" : "")} · <code>{H(d.Key)}</code></div>");
                if (err != null) sb.Append($"<div class=\"msg bad\">{H(err)}</div>");
                else if (ServerSettings.Warning(d, value.Trim()) is { } w) sb.Append($"<div class=\"msg warn\">{H(w)}</div>");
                sb.Append("</div></div>");
            }
            sb.Append("</section>");
        }
        sb.Append("<div class=\"actions\"><button class=\"primary\">저장</button><span class=\"muted\">저장할 때 이전 파일을 appsettings.json.날짜.bak으로 남깁니다. ")
          .Append("Admin:Password, Update:PublicKeyPem, Logging은 웹에서 바꾸지 않습니다(서버 PC에서 파일을 직접 고침).</span></div></form>");

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
        sb.Append("</section>");
        sb.Append("<form method=\"post\" action=\"/admin/logout\" class=\"actions\"><button>로그아웃</button></form>");
        return Page("ASH 서버 설정", sb.ToString());
    }
}
