using System.Globalization;
using System.Text;
using ASH.Core.Analysis;
using ASH.Core.Runner;
using static ASH.Server.JobPages;

namespace ASH.Server;

/// <summary>The analysis pages: the list (/analyses), one analysis with its plans' versions and the compare table of the selected ones
/// (/analyses/{id}), the jobs not filed anywhere (/analyses/unclassified), and the "where is this job filed" header of a job page.</summary>
public sealed record AnalysisPageData(AnalysisInfo A, IReadOnlyList<JobInfo> Jobs, IReadOnlyList<FilmInfo> Films, JobStore Store, (int Ahead, DateTime? Start, TimeSpan? Typical) Queue, int Parallel, int CutCount, FilmStore? Fs = null);

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
            sb.Append($"<div class=\"crumb\"><a href=\"/analyses/{H(a.Id)}?tab=status\">{H(a.Name)}</a> › {H(p.Name)} › v{j.Version}{(j.VersionNote.Length > 0 ? " · " + H(j.VersionNote) : "")}{(j.Selected ? " <span class=\"tag\">선정</span>" : "")}</div>");
            sb.Append("<div class=\"mini\">").Append(MoveForm(j, analyses, "/jobs/" + j.Id, "옮기기")).Append(SelectForm(j, "/jobs/" + j.Id)).Append("</div>");
            sb.Append("<div class=\"help\">다른 안으로 옮기면 그 안의 다음 버전이 됩니다.</div>");
        }
        sb.Append("</div>");
    }

    static string SelectForm(JobInfo j, string back) =>
        $"<form method=\"post\" action=\"/jobs/{H(j.Id)}/select\" class=\"mini\"><input type=\"hidden\" name=\"on\" value=\"{(j.Selected ? 0 : 1)}\"><input type=\"hidden\" name=\"back\" value=\"{H(back)}\">" +
        $"<button>{(j.Selected ? "선정 해제" : "선정")}</button></form>";

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

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<(string Name, int Max)>> GroupCache = new();

    /// <summary>"G01 4 · G02 3 · G03 5 시험 중 · G04 탐색 중": confirmed groups with their count, the ones in the cluster being searched, the rest still waiting.
    /// Group order and Max come from the job's input; without it only the confirmed groups are listed.</summary>
    internal static string PartialText(JobStore store, JobInfo j)
    {
        var done = j.Partial ?? new();
        // the input of a job never changes, so polling must not re-parse the workbook; only a successful read is cached
        if (!GroupCache.TryGetValue(j.Id, out var groups))
        {
            try { groups = SearchJob.ReadInput(store.ProjectDir(j.Id), TextWriter.Null, new List<string>(), out _)?.Groups.Select(g => (g.Group, g.MaxCount)).ToList(); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { }
            if (groups != null) GroupCache[j.Id] = groups;
        }
        if (groups == null || groups.Count == 0) return string.Join(" · ", done.Select(kv => $"{kv.Key} {kv.Value}"));
        var trying = j.Phase is { } ph && ph.StartsWith("탐색 ", StringComparison.Ordinal) ? ph[3..].Split(',') : Array.Empty<string>();
        return string.Join(" · ", groups.Select(g => done.TryGetValue(g.Name, out var n) ? $"{g.Name} {n}" : trying.Contains(g.Name) ? $"{g.Name} {g.Max} 시험 중" : $"{g.Name} 탐색 중"));
    }

    static readonly (string Key, string Name)[] Tabs = { ("status", "현황"), ("conclusion", "결론"), ("compare", "비교"), ("films", "영상") };

    public static string Detail(AnalysisPageData d, string tab, IQueryCollection q, string? pollScript)
    {
        var a = d.A;
        if (!Tabs.Any(t => t.Key == tab)) tab = "status";
        var sb = new StringBuilder(PollSlots + $"<p class=\"crumb\"><a href=\"/analyses\">분석</a> › {H(a.Name)}</p><h1>{H(a.Name)}</h1>");
        if (a.Note.Length > 0) sb.Append($"<p>{H(a.Note)}</p>");
        sb.Append($"<p class=\"muted\">만든 시각 {H(When(a.CreatedUtc))} · <a href=\"/analyses/{H(a.Id)}/cuts\">컷 영상 →</a></p>");
        sb.Append("<div class=\"cards\">");
        foreach (var (n, label) in new[] { (a.Plans.Count, "안"), (d.Jobs.Count, "버전"), (d.Jobs.Count(j => j.Selected), "선정"), (d.Films.Count, "영상") })
            sb.Append($"<div class=\"card\"><div class=\"big\">{n}</div><div class=\"sub\">{label}</div></div>");
        sb.Append("</div><div class=\"tabs\" style=\"margin-top:14px\">");
        foreach (var (k, name) in Tabs) sb.Append($"<a href=\"/analyses/{H(a.Id)}?tab={k}\"{(k == tab ? " class=\"on\"" : "")}>{name}</a>");
        sb.Append("</div><div class=\"panel\">");
        switch (tab)
        {
            case "conclusion": ConclusionTab(sb, d, q); break;
            case "compare": CompareTab(sb, d, q); break;
            case "films": FilmsTab(sb, d); break;
            default: StatusTab(sb, d); break;
        }
        sb.Append("</div>");
        return Page("ASH 분석 " + a.Name, sb.ToString(), pollScript);
    }

    /// <summary>결론 tab: one version (the latest selected, else the latest finished; chips pick another) or every plan's version one under the other.
    /// The chosen view is kept in localStorage and applied when the tab is opened without ?view=.</summary>
    internal static void ConclusionTab(StringBuilder sb, AnalysisPageData d, IQueryCollection q)
    {
        var a = d.A; var plans = q["view"] == "plans"; var metric = q["metric"] == "occ" ? "occ" : "wait";
        bool Ok(JobInfo j) => j.State == JobState.Done && j.Decided != null;
        DateTime At(JobInfo j) => j.FinishedUtc ?? j.CreatedUtc;
        JobInfo? Pick(IEnumerable<JobInfo> js) { var l = js.Where(Ok).ToList(); return (l.Where(j => j.Selected).OrderByDescending(At).FirstOrDefault()) ?? l.OrderByDescending(At).FirstOrDefault(); }
        var done = d.Jobs.Where(Ok).OrderBy(j => a.Plans.FindIndex(p => p.Id == j.PlanId)).ThenBy(j => j.Version).ToList();
        string PlanName(JobInfo j) => a.Plans.FirstOrDefault(p => p.Id == j.PlanId)?.Name ?? j.Plan;
        string Radio(string v, string text) => $"<a href=\"?tab=conclusion&amp;view={v}\" data-view=\"{v}\" class=\"chip{((v == "plans") == plans ? " sel" : "")}\">{text}</a>";
        sb.Append("<p>보기: ").Append(Radio("one", "선정 버전 하나")).Append(Radio("plans", "안별 펼침")).Append("</p>");
        sb.Append("<script>(function(){try{var k='ashConclusionView',r=document.querySelectorAll('a[data-view]');" +
            "r.forEach(function(e){e.addEventListener('click',function(){localStorage.setItem(k,e.getAttribute('data-view'));});});" +
            "var s=localStorage.getItem(k);if((s==='one'||s==='plans')&&!/[?&]view=/.test(location.search)&&/[?&]tab=conclusion/.test(location.search)){var u=new URL(location.href);u.searchParams.set('view',s);location.replace(u.toString());}}catch(e){}})();</script>");
        if (done.Count == 0) { sb.Append("<p class=\"muted\">결과가 나온 버전이 없습니다.</p>"); return; }
        if (plans)
        {
            foreach (var p in a.Plans)
            {
                if (Pick(d.Jobs.Where(j => j.PlanId == p.Id)) is not { } j) { sb.Append($"<section class=\"box\"><h2>{H(p.Name)}</h2><p class=\"muted\">완료된 버전 없음</p></section>"); continue; }
                {
                    sb.Append($"<section class=\"box\"><h2>{H(p.Name)} · <a href=\"/jobs/{H(j.Id)}\">v{j.Version}</a>{(j.Selected ? " <span class=\"tag\">선정</span>" : "")}</h2>");
                    Conclusion(sb, j, JobView.For(d.Store, j), true, metric);
                    sb.Append("</section>");
                }
            }
            return;
        }
        var cur = done.FirstOrDefault(j => j.Id == q["job"].ToString()) ?? Pick(done)!;
        sb.Append("<p>");
        foreach (var j in done) sb.Append($"<a href=\"?tab=conclusion&amp;view=one&amp;job={H(j.Id)}\" class=\"chip{(j.Id == cur.Id ? " sel" : "")}\">{H(PlanName(j))} v{j.Version}{(j.Selected ? " · 선정" : "")}</a>");
        sb.Append("</p>");
        Conclusion(sb, cur, JobView.For(d.Store, cur), false, metric);
    }

    internal static void StatusTab(StringBuilder sb, AnalysisPageData d)
    {
        var a = d.A; var jobs = d.Jobs; var back = "/analyses/" + a.Id;
        string Title(JobInfo j) => $"{a.Name} › {a.Plans.FirstOrDefault(p => p.Id == j.PlanId)?.Name ?? j.Plan} v{j.Version}";
        var running = jobs.Where(j => j.State == JobState.Running).OrderBy(j => j.StartedUtc ?? j.CreatedUtc).FirstOrDefault();
        if (running != null)
        {
            var eta = JobProgress.Eta(running, d.Queue.Typical, d.Parallel, DateTime.UtcNow);
            HomePages.RunningCard(sb, new(running.Id, Title(running), running.State, running.Phase, JobProgress.Ratio(running), running.RunsDone, running.ExpectedRuns,
                running.RunsDone > 0 ? Dur(JobProgress.AvgRun(running)) : null, eta is { } e ? ASH.Core.SeoulTime.FromUtc(e).ToString("HH:mm") : null, running.LastLine ?? "", null), "running");
            sb.Append($"<p>중간 결과: <span data-field=\"partial.text\">{H(PartialText(d.Store, running))}</span></p>");
        }
        foreach (var j in jobs.Where(j => j.State == JobState.Queued).OrderBy(j => j.CreatedUtc))
            HomePages.RunningCard(sb, new(j.Id, Title(j), j.State, j.Phase, null, j.RunsDone, j.ExpectedRuns, null, null, "", null), "queued." + j.Id);

        foreach (var p in a.Plans)
        {
            sb.Append($"<section class=\"box\"><h2>{H(p.Name)}</h2>");
            if (p.Note.Length > 0) sb.Append($"<p class=\"muted\">{H(p.Note)}</p>");
            var versions = jobs.Where(j => j.PlanId == p.Id).OrderBy(j => j.Version).ToList();
            if (versions.Count == 0) sb.Append("<p class=\"muted\">아직 보낸 작업이 없습니다. ZWCAD의 ASH에서 이 분석과 안을 골라 \"서버로 보내기\"를 하면 v1이 됩니다.</p>");
            foreach (var j in versions)
            {
                var m = MetricsOf(d.Store, j);
                var parts = new List<string> { $"<a href=\"/jobs/{H(j.Id)}\">v{j.Version}</a>{(j.Selected ? " <span class=\"tag\">선정</span>" : "")}" };
                if (j.State == JobState.Failed) parts.Add($"<span class=\"state s-failed\">실패</span> <span class=\"bad\">{H((j.Error ?? "").Split('\n')[0].Trim())}</span>");
                else if (!j.Finished || j.State == JobState.Cancelled) parts.Add(State(j));
                else if (m?.Total is { } t) parts.Add($"{t}대 · 가동률 {(double.IsNaN(m.Util) ? "-" : Math.Round(m.Util * 100).ToString("0", CultureInfo.InvariantCulture) + "%")} · 결품 {(double.IsNaN(m.Stop) ? "-" : Num(m.Stop, "0"))}초");
                parts.Add(H(When(j.CreatedUtc))); parts.Add(H(j.Submitter)); if (j.VersionNote.Length > 0) parts.Add(H(j.VersionNote));
                // plan D: the "자료 받기" link (FileRoutes.ContinueLink(j)) goes here, at the end of the version row
                sb.Append($"<div class=\"mini\" style=\"margin:6px 0\">{string.Join(" · ", parts.Where(x => x.Length > 0))} {SelectForm(j, back)} <a href=\"/jobs/{H(j.Id)}\">열기</a></div>");
            }
            sb.Append("</section>");
        }
        if (a.Plans.Count == 0) sb.Append("<p class=\"muted\">아직 안이 없습니다. 아래에서 안을 추가하거나, ZWCAD에서 보낼 때 안 이름을 적으면 만들어집니다.</p>");

        sb.Append("<details><summary>안 추가 · 이름 바꾸기 · 지우기</summary><div class=\"box\">");
        sb.Append($"<form method=\"post\" action=\"/analyses/{H(a.Id)}/plans\" class=\"mini\"><input type=\"hidden\" name=\"back\" value=\"{H(back)}\">" +
            "<input type=\"text\" name=\"name\" required maxlength=\"100\" placeholder=\"안 이름(예: A안)\" aria-label=\"안 이름\"><input type=\"text\" name=\"note\" maxlength=\"200\" placeholder=\"메모(선택)\" aria-label=\"메모\">" +
            "<button class=\"primary\">추가</button></form>");
        sb.Append($"<form method=\"post\" action=\"/analyses/{H(a.Id)}/rename\" class=\"mini\" style=\"margin-top:12px\"><input type=\"hidden\" name=\"back\" value=\"{H(back)}\">" +
            $"<input type=\"text\" name=\"name\" required maxlength=\"100\" value=\"{H(a.Name)}\" aria-label=\"분석 이름\"><button>분석 이름 바꾸기</button></form>");
        foreach (var p in a.Plans)
            sb.Append($"<form method=\"post\" action=\"/analyses/{H(a.Id)}/plans/{H(p.Id)}/rename\" class=\"mini\" style=\"margin-top:8px\"><input type=\"hidden\" name=\"back\" value=\"{H(back)}\">" +
                $"<input type=\"text\" name=\"name\" required maxlength=\"100\" value=\"{H(p.Name)}\" aria-label=\"{H(p.Name)} 새 이름\"><button>안 이름 바꾸기</button></form>");
        if (jobs.Count == 0)
            sb.Append($"<form method=\"post\" action=\"/analyses/{H(a.Id)}/delete\" style=\"margin-top:12px\"><button>이 분석 지우기</button> <span class=\"muted\">보낸 작업이 없을 때만 지울 수 있습니다.</span></form>");
        else sb.Append("<p class=\"muted\">보낸 작업이 있는 분석은 지울 수 없습니다.</p>");
        sb.Append("</div></details>");
        sb.Append($"<p class=\"muted\" style=\"font-size:13px\"><a href=\"/admin/analyses/{H(a.Id)}/delete\">관리자: 분석 삭제…</a></p>");
    }

    /// <summary>/admin/analyses/{id}/delete: what the delete does, and the form that needs the analysis name typed.</summary>
    public static string DeleteConfirm(AnalysisInfo a, int jobCount, IReadOnlyList<FilmInfo> films, int cutCount)
    {
        var active = films.Where(f => !f.Finished).ToList();
        var sb = new StringBuilder($"<p class=\"crumb\"><a href=\"/analyses\">분석</a> › <a href=\"/analyses/{H(a.Id)}\">{H(a.Name)}</a> › 삭제</p><h1>분석 삭제(관리자)</h1>");
        sb.Append($"<section class=\"box\"><h2>{H(a.Name)}</h2><p>이 분석을 지우면 다음과 같이 됩니다. 되돌릴 수 없습니다.</p><ul>" +
            $"<li><b>보낸 작업 {jobCount}건은 그대로 남고</b> \"분석에 넣지 않은 작업\"이 됩니다(안·버전·선정은 해제, 버전 메모는 유지).</li>" +
            $"<li>분석과 안 {a.Plans.Count}개가 지워집니다.</li>" +
            $"<li>컷 목록({cutCount}개)이 지워집니다.</li>" +
            $"<li>만든 영상 {films.Count}건의 기록과 영상 파일이 지워집니다.</li></ul></section>");
        if (active.Count > 0)
        {
            sb.Append("<div class=\"msg bad\">대기 중이거나 만드는 중인 영상이 있어 지울 수 없습니다. 먼저 취소하세요: ");
            sb.Append(string.Join(", ", active.Select(f => $"<a href=\"/films/{H(f.Id)}\">{H(When(f.CreatedUtc))}</a>"))).Append("</div>");
            return Page("ASH 분석 삭제", sb.ToString());
        }
        sb.Append($"<form method=\"post\" action=\"/admin/analyses/{H(a.Id)}/delete\" class=\"box\"><div class=\"field\"><label for=\"name\">분석 이름 입력</label><div>" +
            $"<input type=\"text\" id=\"name\" name=\"name\" required autocomplete=\"off\" maxlength=\"100\"><div class=\"help\">지울 분석의 이름 <b>{H(a.Name)}</b> 을(를) 그대로 입력하세요.</div></div></div>" +
            "<div class=\"actions\"><button class=\"primary\">분석 지우기</button><a href=\"/analyses/" + H(a.Id) + "\">취소</a></div></form>");
        return Page("ASH 분석 삭제", sb.ToString());
    }

    internal static string FileUrl(FilmInfo f, string name) => $"/films/{H(f.Id)}/files/{H(Uri.EscapeDataString(name))}";

    /// <summary>영상 tab: the new-film button, the films being made (progress bar, estimate, cancel) and the finished ones (thumbnail, player, per-cut links, zip).</summary>
    static string StateSpan(FilmInfo f, string? liveKey) =>
        $"<span class=\"state s-{H(f.State)}\"{(liveKey != null ? $" data-state=\"{liveKey}.state\" data-field=\"{liveKey}.statetext\"" : "")}>{H(JobState.Korean(f.State))}</span>";

    internal static void FilmsTab(StringBuilder sb, AnalysisPageData d)
    {
        var id = H(d.A.Id);
        sb.Append($"<p><a class=\"primary\" href=\"/analyses/{id}/cuts\">새 영상 만들기</a> · 컷 리스트 {d.CutCount}개</p>");
        var films = d.Films.OrderByDescending(f => f.CreatedUtc).ToList();
        if (films.Count == 0) { sb.Append("<p class=\"muted\">아직 만든 영상이 없습니다.</p>"); return; }
        foreach (var f in films.Where(f => !f.Finished))
        {
            var k = "film." + H(f.Id); var total = f.Items.Count;
            sb.Append($"<section class=\"box\"><div class=\"box-head\"><h2><a href=\"/films/{H(f.Id)}\">{H(When(f.CreatedUtc))}</a></h2>" +
                StateSpan(f, k) + "</div>");
            sb.Append($"<p><span data-field=\"{k}.phase\">{H(f.Phase)}</span> · <span data-field=\"{k}.done\">{LiveFields.FilmDone(f)}</span></p>");
            sb.Append($"<div class=\"prog\"><i data-bar=\"{k}.bar\" style=\"width:{LiveFields.FilmPct(f)}%\"></i></div>");
            sb.Append($"<p>완료 예상 <span data-field=\"{k}.eta\">{H(LiveFields.FilmEta(f))}</span></p>");
            sb.Append(f.CancelRequested ? "<p class=\"muted\">취소하는 중입니다.</p>"
                : $"<form method=\"post\" action=\"/films/{H(f.Id)}/cancel\"><button>{(f.State == JobState.Queued ? "대기 취소" : "취소")}</button></form>");
            sb.Append("</section>");
        }
        foreach (var f in films.Where(f => f.Finished))
        {
            var r = f.Request; var fid = H(f.Id);
            sb.Append($"<section class=\"box\"><h2><a href=\"/films/{fid}\">{H(When(f.CreatedUtc))}</a>{(f.State != JobState.Done ? " " + StateSpan(f, null) : "")}</h2>" +
                $"<p class=\"muted\">{H(When(f.CreatedUtc))} · 컷 {f.Cuts.Count} × 버전 {r.JobIds.Count} · {r.Width}×{r.Height} · {r.Fps} fps</p>");
            sb.Append(d.Fs != null && File.Exists(Path.Combine(d.Fs.FilmDir(f.Id), "thumb.jpg")) ? $"<img src=\"/films/{fid}/files/thumb.jpg\" width=\"320\" alt=\"\">" : "<div class=\"thumb-none\"></div>");
            bool Has(string n) => d.Fs != null && File.Exists(Path.Combine(d.Fs.FilmDir(f.Id), n));
            var joinedOk = f.JoinedOk && f.Joined != null && Has(f.Joined);
            var oks = f.Items.Where(i => i.Ok && Has(i.Name)).ToList();
            var main = joinedOk ? f.Joined : oks.FirstOrDefault()?.Name;
            if (main != null) sb.Append($"<div><video controls preload=\"none\" src=\"{FileUrl(f, main)}\" style=\"width:100%;max-width:720px;background:#000\"></video></div>");
            if (oks.Count > 0)
            {
                sb.Append("<ul>");
                foreach (var i in oks) sb.Append($"<li><a href=\"{FileUrl(f, i.Name)}\">{H(i.Label)}</a></li>");
                sb.Append("</ul>");
            }
            else if (joinedOk) sb.Append("<p class=\"muted\">이어 붙인 영상에만 넣었습니다.</p>");
            if (f.Error != null) sb.Append($"<p class=\"bad\">{H(f.Error)}</p>");
            sb.Append((oks.Count > 0 || joinedOk ? $"<a href=\"/films/{fid}/files/all.zip\">전체 zip 받기</a> · " : "") + $"<a href=\"/films/{fid}\">자세히</a></section>");
        }
    }

    /// <summary>/films/{id}: progress while it runs, then the players and downloads. <paramref name="exists"/> tells which files are still on disk.</summary>
    public static string Film(FilmInfo f, Func<string, bool> exists, string logTail)
    {
        var r = f.Request;
        var sb = new StringBuilder(PollSlots + $"<p class=\"crumb\"><a href=\"/analyses\">분석</a> › <a href=\"/analyses/{H(r.AnalysisId)}\">{H(f.AnalysisName)}</a> › 영상</p>");
        sb.Append($"<h1>영상 <span class=\"muted\">· {H(When(f.CreatedUtc))}</span></h1>");
        sb.Append($"<p><span class=\"state s-{H(f.State)}\" data-state=\"film.state\" data-field=\"film.statetext\">{H(JobState.Korean(f.State))}</span> <span data-field=\"film.phase\">{H(f.Phase)}</span><span data-field=\"film.sep\">{(f.Phase != null ? " · " : "")}</span><span data-field=\"film.done\">컷 {f.Items.Count}개 중 {f.Done}개 완료</span>" +
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
        return Page("ASH 영상 " + f.AnalysisName, sb.ToString(), f.Finished ? null : PollScript("/api/films/" + f.Id, new Dictionary<string, string> { [f.Id] = f.State }));
    }

    internal sealed record CompareColumn(JobInfo Job, string PlanName, Metrics? M);

    static readonly string[] Palette = { "#1f3a5f", "#378add", "#e24b4a", "#3b6d11", "#ef9f27", "#7a4fb3" };

    /// <summary>비교 tab: version chips (?jobs=a,b — the first is the base), the metric table with changes against the base, the grouped utilization chart, and on request (&amp;maps=1) the bottleneck maps side by side.</summary>
    internal static void CompareTab(StringBuilder sb, AnalysisPageData d, IQueryCollection q)
    {
        var a = d.A;
        int PlanIx(JobInfo j) => a.Plans.FindIndex(p => p.Id == j.PlanId);
        var all = d.Jobs.OrderBy(PlanIx).ThenBy(j => j.Version).ToList();
        var byId = all.ToDictionary(j => j.Id);
        var ids = q["jobs"].Count > 0
            ? q["jobs"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(byId.ContainsKey).Distinct().ToList()
            : all.Where(j => j.Selected).Select(j => j.Id).ToList();
        string Url(IEnumerable<string> l, bool maps = false) => $"?tab=compare&amp;jobs={string.Join(",", l.Select(Uri.EscapeDataString))}{(maps ? "&amp;maps=1" : "")}";
        string PlanName(JobInfo j) => a.Plans.FirstOrDefault(p => p.Id == j.PlanId)?.Name ?? j.Plan;
        sb.Append("<section class=\"box\"><h2>버전 비교</h2><p>");
        foreach (var j in all)
            sb.Append($"<a href=\"{Url(ids.Contains(j.Id) ? ids.Where(x => x != j.Id) : ids.Append(j.Id))}\" class=\"chip{(ids.Contains(j.Id) ? " sel" : "")}\">{H(PlanName(j))} v{j.Version}{(j.Selected ? " · 선정" : "")}</a>");
        sb.Append("</p>");
        if (ids.Count < 2) { sb.Append("<p class=\"muted\">버전을 2개 이상 고르세요.</p></section>"); return; }
        var cols = ids.Select(id => new CompareColumn(byId[id], PlanName(byId[id]), MetricsOf(d.Store, byId[id]))).ToList();
        var groups = cols.Where(c => c.M != null).SelectMany(c => c.M!.View.Groups.Select(g => g.Group)).Distinct(StringComparer.Ordinal).ToList();
        var baseM = cols[0].M;

        sb.Append("<div class=\"scroll\"><table><tr><th>지표</th>");
        for (int i = 0; i < cols.Count; i++)
        {
            var c = cols[i];
            sb.Append($"<th><a href=\"/jobs/{H(c.Job.Id)}\">{H(c.PlanName)} v{c.Job.Version}</a><br>")
              .Append(i == 0 ? "<span class=\"tag\">기준</span>" : $"<a href=\"{Url(ids.Where(x => x == c.Job.Id).Concat(ids.Where(x => x != c.Job.Id)))}\">기준으로</a>").Append("</th>");
        }
        sb.Append("</tr>");
        // every row is "smaller is better": a negative change against the base is ok, a positive one bad
        // labels are trusted constants (plus H(group)); H() would turn "·" into &#183;, so they are not encoded again here
        void Row(string label /* already HTML */, Func<Metrics, double?> get, Func<Metrics, string> show)
        {
            sb.Append($"<tr><th>{label}</th>");
            for (int i = 0; i < cols.Count; i++)
            {
                var m = cols[i].M;
                if (m == null) { sb.Append("<td class=\"num muted\">-</td>"); continue; }
                sb.Append($"<td class=\"num\">{H(show(m))}");
                if (i > 0 && baseM != null && get(m) is { } v && !double.IsNaN(v) && get(baseM) is { } b && !double.IsNaN(b))
                {
                    var dl = Math.Round(v - b, 1);
                    sb.Append($" <small{(dl < 0 ? " class=\"ok\"" : dl > 0 ? " class=\"bad\"" : "")}>{H(F(dl, "+0.#;-0.#;0"))}</small>");
                }
                sb.Append("</td>");
            }
            sb.Append("</tr>");
        }
        string Pc(double u) => double.IsNaN(u) ? "-" : Num(u * 100);
        double? Pv(double u) => double.IsNaN(u) ? null : u * 100;
        BlockView? Worst(Metrics m) => m.View.Blocks.Where(x => !double.IsNaN(x.AvgWaitSec)).OrderByDescending(x => x.AvgWaitSec).FirstOrDefault();
        Row("총 대수(대)", m => m.Total, m => m.Total?.ToString(CultureInfo.InvariantCulture) ?? "-");
        Row("최대 가동률(%)", m => Pv(m.Util), m => Pc(m.Util));
        Row("결품 합계(초)", m => m.Stop, m => double.IsNaN(m.Stop) ? "-" : Num(m.Stop, "0"));
        Row("교착(회)", m => m.View.Deadlocks, m => m.View.Deadlocks.ToString(CultureInfo.InvariantCulture));
        Row("최대 병목(block·초)", m => Worst(m)?.AvgWaitSec, m => Worst(m) is { } w ? $"{w.Name}·{Num(w.AvgWaitSec)}" : "-");
        // plan B inserts the 투자비(원) row here (Row(...), smaller is better)
        foreach (var g in groups)
        {
            GroupView? G(Metrics m) => m.View.Groups.FirstOrDefault(x => x.Group == g);
            Row($"{H(g)} 권장(대)", m => G(m)?.Decided, m => G(m)?.Decided?.ToString(CultureInfo.InvariantCulture) ?? "-");
            Row($"{H(g)} 가동률(%)", m => G(m) is { } x ? Pv(x.Util) : null, m => G(m) is { } x ? Pc(x.Util) : "-");
        }
        sb.Append("</table></div>");
        sb.Append("<div class=\"scroll\" style=\"margin-top:12px\">").Append(CompareChart(cols, groups, baseM?.View.UtilMax ?? cols.Select(c => c.M?.View.UtilMax).FirstOrDefault(x => x != null) ?? 0.75)).Append("</div>");
        if (q["maps"] != "1") { sb.Append($"<p><a class=\"btn\" href=\"{Url(ids, true)}\">병목 지도 나란히</a></p></section>"); return; }
        sb.Append("<h2>병목 지도(block 평균 대기, 같은 축척)</h2><div class=\"scroll\"><div style=\"display:flex;gap:12px\">");
        // one scale for every map: the union of the layouts' bounds and the largest wait over all versions
        var bs = cols.Where(c => c.M?.View.Layout != null).Select(c => LayoutBounds(c.M!.View.Layout!)).Where(b => b != null).Select(b => b!.Value).ToList();
        (double, double, double, double)? shared = bs.Count == 0 ? null : (bs.Min(b => b.X0), bs.Max(b => b.X1), bs.Min(b => b.Y0), bs.Max(b => b.Y1));
        var hiWait = cols.Where(c => c.M != null).SelectMany(c => c.M!.View.Blocks.Select(b => b.AvgWaitSec)).Where(x => !double.IsNaN(x)).DefaultIfEmpty(0).Max();
        foreach (var c in cols)
            sb.Append("<div>").Append(c.M?.View.Layout == null ? $"<div style=\"font-weight:600\">{H(c.PlanName)} v{c.Job.Version}</div><p class=\"muted\">지도가 없습니다.</p>"
                : Map(c.M.View, $"{c.PlanName} v{c.Job.Version}", b => b.AvgWaitSec, x => F(x, "0.0") + "초", 360, 250, false, shared, hiWait)).Append("</div>");
        sb.Append("</div></div></section>");
    }

    /// <summary>Grouped bars: per group one bar per version (palette colour per version), dashed line = utilization limit.</summary>
    internal static string CompareChart(IReadOnlyList<CompareColumn> cols, IReadOnlyList<string> groups, double utilMax)
    {
        double w = Math.Max(520, groups.Count * (cols.Count * 14 + 20)), h = 240, l = 36, bt = 28, top = 12, ph = h - bt - top;
        var vals = cols.SelectMany(c => c.M?.View.Groups.Select(g => g.Util) ?? Enumerable.Empty<double>()).Where(u => !double.IsNaN(u));
        double hi = Math.Max(1, Math.Max(utilMax, vals.DefaultIfEmpty(0).Max()));
        string Y(double u) => F(top + ph * (1 - u / hi), "0.#");
        var sb = new StringBuilder($"<svg width=\"{F(w, "0.#")}\" height=\"{h}\" viewBox=\"0 0 {F(w, "0.#")} {h}\" style=\"background:#fff;border:1px solid #d3d1c7\">");
        sb.Append($"<line x1=\"{l}\" x2=\"{F(w - 4, "0.#")}\" y1=\"{Y(0)}\" y2=\"{Y(0)}\" stroke=\"#888780\"/>");
        double slot = (w - l - 4) / Math.Max(1, groups.Count);
        for (int gi = 0; gi < groups.Count; gi++)
        {
            double gx = l + gi * slot, bw = Math.Min(14, (slot - 6) / Math.Max(1, cols.Count));
            for (int ci = 0; ci < cols.Count; ci++)
            {
                var gv = cols[ci].M?.View.Groups.FirstOrDefault(x => x.Group == groups[gi]);
                if (gv == null || double.IsNaN(gv.Util)) continue;
                double x = gx + 3 + ci * bw;
                sb.Append($"<rect x=\"{F(x, "0.#")}\" y=\"{Y(gv.Util)}\" width=\"{F(bw - 1, "0.#")}\" height=\"{F(ph * gv.Util / hi, "0.#")}\" fill=\"{Palette[ci % Palette.Length]}\"><title>{H(groups[gi])} · {H(cols[ci].PlanName)} v{cols[ci].Job.Version}: {H(F(gv.Util * 100, "0.#"))}%</title></rect>");
            }
            sb.Append($"<text x=\"{F(gx + slot / 2, "0.#")}\" y=\"{h - 10}\" font-size=\"11\" text-anchor=\"middle\">{H(groups[gi])}</text>");
        }
        sb.Append($"<line x1=\"{l}\" x2=\"{F(w - 4, "0.#")}\" y1=\"{Y(utilMax)}\" y2=\"{Y(utilMax)}\" stroke=\"#a32d2d\" stroke-dasharray=\"5 4\"><title>가동률 상한 {H(F(utilMax * 100, "0.#"))}%</title></line>");
        sb.Append($"<text x=\"{l - 4}\" y=\"{Y(utilMax)}\" font-size=\"10\" text-anchor=\"end\" fill=\"#a32d2d\">{H(F(utilMax * 100, "0"))}%</text></svg>");
        sb.Append("<div class=\"muted\" style=\"font-size:12px\">");
        for (int ci = 0; ci < cols.Count; ci++) sb.Append($"<span style=\"color:{Palette[ci % Palette.Length]}\">■</span> {H(cols[ci].PlanName)} v{cols[ci].Job.Version} &nbsp;");
        return sb.Append("· 가동률(%) · 빨간 점선은 상한</div>").ToString();
    }
}
