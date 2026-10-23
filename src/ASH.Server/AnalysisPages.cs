using System.Globalization;
using System.Text;
using ASH.Core.Analysis;
using ASH.Core.Runner;
using static ASH.Server.JobPages;

namespace ASH.Server;

/// <summary>The analysis pages: the list (/analyses), one analysis with its plans' versions and the compare table of the selected ones
/// (/analyses/{id}), the jobs not filed anywhere (/analyses/unclassified), and the "where is this job filed" header of a job page.</summary>
public sealed record AnalysisPageData(AnalysisInfo A, IReadOnlyList<JobInfo> Jobs, IReadOnlyList<FilmInfo> Films, JobStore Store, (int Ahead, DateTime? Start, TimeSpan? Typical) Queue, int Parallel, int CutCount);

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

    /// <summary>"G01 4 · G02 3 · G03 5 시험 중 · G04 탐색 중": confirmed groups with their count, the ones in the cluster being searched, the rest still waiting.
    /// Group order and Max come from the job's input; without it only the confirmed groups are listed.</summary>
    internal static string PartialText(JobStore store, JobInfo j)
    {
        var done = j.Partial ?? new();
        List<(string Name, int Max)>? groups = null;
        try { groups = SearchJob.ReadInput(store.ProjectDir(j.Id), TextWriter.Null, new List<string>(), out _)?.Groups.Select(g => (g.Group, g.MaxCount)).ToList(); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { }
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
            case "conclusion": // Task 9 fills this
                if (d.Jobs.FirstOrDefault(j => j.Selected) is { } sel) sb.Append($"<p><a href=\"/jobs/{H(sel.Id)}\">선정한 버전 v{sel.Version}의 결론 보기 →</a></p>");
                else sb.Append("<p class=\"muted\">준비 중</p>");
                break;
            case "compare": // Task 10 replaces this
                Compare(sb, a, d.Jobs.ToList(), d.Jobs.ToDictionary(j => j.Id, j => MetricsOf(d.Store, j)));
                break;
            case "films": // Task 11 replaces this
                FilmList(sb, d.Films.Where(f => f.Request.AnalysisId == a.Id).ToList(), a.Id);
                break;
            default: StatusTab(sb, d); break;
        }
        sb.Append("</div>");
        return Page("ASH 분석 " + a.Name, sb.ToString(), pollScript);
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
