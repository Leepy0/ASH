# 웹 개편 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 홈에서 내 분석의 진행(예상 실행 수·완료 수·단계·완료 예상)을 보고, 새로고침 없이 갱신되며, 분석 하나의 결론·비교·영상을 탭 4개(현황·결론·비교·영상)로 본다.

**Architecture:** 서버 렌더 HTML + 작은 인라인 JS 그대로. 라우트는 `Routes.*.cs` 확장 파일로 나누고(`Routes.cs`에 공용 helper), 페이지 공통 껍데기는 `JobPages.Page`(nav·CSS 토큰·폴링 스크립트 자리)로 한 번 확정한다. 진행 예상은 Core(`SearchEstimate`)가 탐색 로직에서 계산하고, `SearchJob.Run`의 `onProgress` 콜백이 워커에 그룹 확정·단계를 알려 `JobInfo.ExpectedRuns/Partial`에 쓴다. 홈·현황·작업·영상 JSON은 모두 `{active, fields, watch}` 한 모양이고, 페이지의 `data-field` 요소만 그 `fields`로 바꾼다. 뒤의 B(`Routes.Catalog.cs`)·C·D(`Routes.Files.cs`)는 같은 `partial class Routes`에 자기 파일만 더한다.

**Tech Stack:** C# .NET 10 서버(`src/ASH.Server`, minimal API), Core netstandard2.0(`src/ASH.Core`), xunit(`tests/ASH.Server.Tests`, `tests/ASH.Tests`), ffmpeg(썸네일, 설정 `Film:FfmpegPath`).

**Spec:** `docs/superpowers/specs/2026-10-23-ash-web-and-catalog-design.md` (0, A, E, G, H)

## Global Constraints

- PC 브라우저만, 기준 폭 1280~1920: `main{max-width:1600px}`, 2열 격자 `.grid2`는 1024px 이하에서 1열.
- 외부 자원 금지: 스크립트·CSS는 페이지 안(`JobPages.Page`에 인라인).
- 로그인 없음. "내 것" = `JobInfo.Ip == 접속 IP` 또는 `JobInfo.Submitter ∈ {그 IP로 보낸 작업들의 Submitter}`; 분석은 어느 버전이라도 내 것이면 내 것.
- 관리자 기능(`/admin/jobs`, `/admin/settings`의 서버 현황)은 기존 `Admin:Token`(헤더 `X-ASH-Admin-Token` 또는 `/admin/login` 쿠키) 뒤에서만.
- 기존 API(`/api/jobs`, `/api/jobs/{id}`, `/api/jobs/queue`, `/api/analyses*`, `/api/ash/*`)와 0.18~0.19 플러그인의 보내기는 그대로. `/api/jobs/{id}`는 필드를 더하기만 한다.
- 문구 한국어, 표 제목에 단위 `(대)` `(%)` `(초)` `(회)` `(원)`.
- 메뉴 홈·분석·설정. `/jobs` → `/admin/jobs` 301. `/jobs/{id}` 주소·내용 유지. `/analyses/unclassified` 유지.
- `<meta http-equiv="refresh">` 금지. 폴링 10초, 실행·대기 항목이 없으면 폴링 없음. 끝나면 띠 "결과가 나왔습니다 — 새로 읽기", 60초 뒤 한 번 자동 재읽기. 네트워크 오류는 회색 글자 "연결 끊김 · 다시 시도 중".
- 진행률 = `min(RunsDone / ExpectedRuns, 0.99)`, `Done`일 때만 1.0. 실제 > 예상이면 예상 = `실제 + 남은 묶음의 그룹 수`.
- 완료 예상 = 지금 + (예상 − 실행 수) × (실행 1회 평균 = `RunSeconds / RunsDone`) ÷ `Jobs:Parallel`(기본 2). 실행 0회면 `StartedUtc + QueueInfo.Typical`.
- `JobInfo` 추가 필드는 A만: `ExpectedRuns`, `Partial`, `RunSeconds`. 옛 job.json(필드 없음)도 읽힌다.
- 결론 탭은 `JobView` 그대로(새 계산 없음). 투자비 행·카드는 B가 더한다(이 계획은 자리만 비워 둔다).
- 영상 썸네일 `thumb.jpg`(첫 완료 컷 MP4의 1초 지점, ffmpeg), 없으면 회색 틀. `/films/{id}/files/all.zip` = 그 영상의 MP4 전부.
- 커밋 메시지는 `feat(server): …`/`feat(core): …`, 끝에 빈 줄 + `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.

## Review Focus

1. **진행률 상한과 완료 표시**: 실제 실행이 예상을 넘는 탐색(확인 라운드 반복), 실패·취소로 끝난 작업 → 100% 초과 금지, `Done`만 100%, 끝난 뒤 "실행 중" 잔상 없음. Task 4 `Progress_Never_Exceeds_One_And_Is_Full_Only_When_Done`.
2. **그룹 20개 분석의 결론 화면**: 묶음 막대·지도 1개·상위 3만 → 세로 길이가 그룹 수에 비례해 끝없이 길어지지 않는다(지도는 1개, 표는 막대 1줄/그룹). Task 9 `Conclusion_Renders_One_Map_And_Cluster_Bars_For_20_Groups`.
3. **한 IP에 이름 여럿·내 것 0건**: 같은 IP에서 보낸 이름이 둘이면 둘 다 내 것, 내 것이 0건이면 전체로. Task 5 `Mine_Collects_Every_Submitter_Of_The_Ip` / Task 6 `Home_Falls_Back_To_All_When_Nothing_Is_Mine`.
4. **0.20 이전 job.json(Clusters·ExpectedRuns 없음)과 PC 결과(local)**: 진행률 null, 결론은 평평한 막대, JSON 역직렬화 실패 없음. Task 9 `Conclusion_Without_Clusters_Is_Flat`, Task 4 `Api_Job_Has_Null_Progress_For_Legacy_Job`.
5. **폴링 JSON의 문자열 주입과 불필요한 폴링**: `fields` 값은 JS가 `textContent`로만 넣는다(HTML 삽입 금지), 실행·대기 항목이 없으면 스크립트가 아예 나가지 않는다. Task 7 `Poll_Script_Only_When_Active` + `Fields_Are_Plain_Text`.

---

### Task 1: 공통 껍데기 — nav·CSS 토큰·`Page` 서명·라우트 파일 분리 (spec E)

**Files:**
- Modify: `src/ASH.Server/JobPages.cs` (`Css`, `Page`, `List`/`Job` 호출), `src/ASH.Server/AnalysisPages.cs`·`src/ASH.Server/ServerPages.cs`·`src/ASH.Server/CutEditorPage.cs` (`Page` 호출의 `refresh` 인자 제거)
- Create: `src/ASH.Server/Routes.cs` (공용 helper), `src/ASH.Server/Routes.Jobs.cs`, `src/ASH.Server/Routes.Analysis.cs`
- Modify: `src/ASH.Server/Program.cs` (릴리스·관리자·홈만 남기고 `app.MapJobs(); app.MapAnalysis();`)
- Test: `tests/ASH.Server.Tests/PageTests.cs` (Create), 기존 `SettingsTests.Home_page_links_jobs_settings_and_shows_sections` 수정

**Interfaces:**
- Produces: `public static string JobPages.Page(string title, string body, string? script = null)` — `script`는 `</main>` 뒤에 그대로 넣는 `<script>…</script>` 문자열(폴링용, Task 7), `refresh` 인자는 없앤다.
- Produces: `internal static partial class Routes` (파일 `Routes.cs`)에 Program.cs의 local function들을 옮긴 정적 메서드: `Msg(int, string)`, `Html(string, int = 200)`, `ClientIp(HttpContext)`, `AcceptsJobs(IConfiguration)`, `WorkerOn(IConfiguration)`, `Queue(JobStore, FilmWorker, string? = null)`, `SameOrigin(HttpContext)`, `HeaderAdmin(HttpContext, ReleaseStore)`, `CookieAdmin(HttpContext, AdminSessions)`, `DenyPost(HttpContext, ReleaseStore, AdminSessions)`, `CrossSiteBrowser(HttpContext)`, `FormFrom(HttpContext)`, `Back(IFormCollection?, string)`, `NameNote(HttpContext)`, `Tail(string, int)`, `CanCancel(HttpContext, JobInfo, ReleaseStore, string?)`, 상수 `NameMax=100`, `NoteMax=200`, `CrossSite`, `NameNoteError`, `WorkerOff`, `DeltaMax=20`, `UploadLimit`.
- Produces: `public static void Routes.MapJobs(this WebApplication app)` (`/api/jobs*`, `/jobs/{id}*`, `/jobs/{id}/files/{name}`, `drawing.json`, `scenes`, `runs/{run}/model.zip`), `public static void Routes.MapAnalysis(this WebApplication app)` (`/api/analyses*`, `/analyses*`, `/admin/analyses/{id}/delete`, `/jobs/{id}/move|select`, cuts, films, `/films/{id}*`). B·C·D는 `Routes.Catalog.cs`·`Routes.Files.cs`에 `MapCatalog`/`MapFiles`를 같은 방식으로 더한다.
- CSS 토큰(`:root`): `--ink:#2c2c2a --bg:#fafaf7 --line:#d3d1c7 --navy:#1f3a5f --link:#185fa5 --muted:#5f5e5a --ok:#3b6d11 --bad:#a32d2d --chip:#f1efe8 --blue:#378add`. 기존 색 literal을 토큰으로 바꾼다. 추가 class: `.grid2{display:grid;grid-template-columns:1fr 1fr;gap:16px}` + `@media(max-width:1024px){.grid2{grid-template-columns:1fr}}`, `.prog{background:#e6f1fb;height:10px;border-radius:5px;overflow:hidden}.prog i{display:block;height:10px;background:var(--blue)}`, `.chip{display:inline-block;padding:1px 8px;border-radius:10px;background:var(--chip);font-size:13px;margin-right:4px}.chip.sel{background:var(--navy);color:#fff}`, `#reload-bar{display:none;position:sticky;top:0;background:#faeeda;padding:8px 24px}`, `#net{font-size:12px;color:var(--muted)}`.
- nav: `<a href="/"><b>ASH</b></a><a href="/">홈</a><a href="/analyses">분석</a><a href="/admin/settings">설정</a>` ("작업" 없음).

- [ ] **Step 1: 실패하는 테스트 작성** — `PageTests.cs`
  ```csharp
  [Fact] public void Page_Has_Nav_Tokens_And_No_Meta_Refresh()
  {
      var html = JobPages.Page("t", "<p>b</p>", "<script>1</script>");
      Assert.DoesNotContain("http-equiv", html);
      Assert.Contains("href=\"/analyses\"", html); Assert.Contains("href=\"/admin/settings\"", html);
      Assert.DoesNotContain("href=\"/jobs\"", html);
      Assert.Contains("--navy:#1f3a5f", html); Assert.Contains(".grid2", html);
      Assert.EndsWith("<script>1</script></body></html>", html);
  }
  ```
  `SettingsTests.Home_page_links_jobs_settings_and_shows_sections`: 기대 문자열에서 `href="/jobs"`, `최근 작업`, `배포`, `서버 상태`를 빼고 `href="/analyses"`를 넣는다(홈 내용은 Task 6에서 바뀜).
- [ ] **Step 2: 실행** — `dotnet test tests/ASH.Server.Tests --filter "FullyQualifiedName~PageTests"` → 컴파일 오류(`Page` 3번째 인자 bool) FAIL.
- [ ] **Step 3: 구현** — `Page` 서명 변경, CSS 토큰, nav, `Routes.cs`·`Routes.Jobs.cs`·`Routes.Analysis.cs`로 라우트 이동(동작 변경 없음), Program.cs는 `app.MapJobs(); app.MapAnalysis();`. `JobPages.List/Job`, `AnalysisPages.Detail/Film`은 `Page(title, body)`로 호출.
- [ ] **Step 4: 실행** — `dotnet test ASH.sln` → PASS(`FilmTests` 169행의 `DoesNotContain("http-equiv")` 포함).
- [ ] **Step 5: 커밋** — `git add -A && git commit -m "feat(server): shared page shell (nav 홈·분석·설정, CSS tokens, no meta refresh) and routes split into Routes.*.cs"` + Co-Authored-By.

### Task 2: `/jobs` → `/admin/jobs` 301, 서버 현황을 설정으로

**Files:**
- Modify: `src/ASH.Server/Program.cs` (`/admin/jobs`, `/admin/settings`에 현황 절), `src/ASH.Server/Routes.Jobs.cs` (`/jobs` 301), `src/ASH.Server/ServerPages.cs`
- Test: `tests/ASH.Server.Tests/JobTests.cs` (177행 `/jobs` → `/admin/jobs` + 토큰), `tests/ASH.Server.Tests/ServerTests.cs` (120·130·193·275·303·316·337행의 `/`를 `/admin/settings`로), `tests/ASH.Server.Tests/SettingsTests.cs` (Create test `Admin_Jobs_Is_Gated_And_Old_Jobs_Url_Redirects`)

**Interfaces:**
- Produces: `public sealed record ServerPages.StatusData(ReleaseStore Releases, GitHubSync GitHub, ServerUpdater Updater, RunnerStore Runner, IReadOnlyList<JobInfo> Jobs, (int Ahead, DateTime? Start, TimeSpan? Typical) Queue, bool WorkerOn)` (기존 `HomeData` 이름 변경).
- Produces: `public static string ServerPages.Status(StatusData d)` — 기존 `Home`의 카드 4개 + 배포 + 서버 상태 절을 `<section class="box"><h2>서버 현황</h2>…` 조각으로(`Page`로 감싸지 않음); `<a href="/admin/jobs">작업 대기열(관리자) →</a>` 포함(B의 `/admin/types` 링크는 B가 같은 자리에 더한다). `ServerPages.Home`은 삭제.
- Produces: `public static string ServerPages.Settings(SettingsView v, string statusHtml)` — 맨 위에 `statusHtml`.
- 라우트: `GET /admin/jobs` = `/admin/settings`와 같은 가드(비로그인 → `Login(...)` 401/403) 뒤 `JobPages.List(js.All(), Queue(js, fw), WorkerOn(cfg))`; `GET /jobs` → `Results.Redirect("/admin/jobs", permanent: true)`. `GET /`는 Task 6까지 임시로 `Results.Redirect("/analyses")`.

- [ ] **Step 1: 실패하는 테스트** — `SettingsTests.cs`
  ```csharp
  [Fact] public async Task Admin_Jobs_Is_Gated_And_Old_Jobs_Url_Redirects()
  {
      using var e = new SettingsEnv();
      var noRedirect = e.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
      var r = await noRedirect.GetAsync("/jobs");
      Assert.Equal(HttpStatusCode.MovedPermanently, r.StatusCode); Assert.Equal("/admin/jobs", r.Headers.Location!.ToString());
      Assert.Equal(HttpStatusCode.Unauthorized, (await e.Http.GetAsync("/admin/jobs")).StatusCode);
      var req = new HttpRequestMessage(HttpMethod.Get, "/admin/jobs"); req.Headers.Add("X-ASH-Admin-Token", SettingsEnv.Token);
      Assert.Contains("작업 대기열", await (await e.Http.SendAsync(req)).Content.ReadAsStringAsync());
      var settings = new HttpRequestMessage(HttpMethod.Get, "/admin/settings"); settings.Headers.Add("X-ASH-Admin-Token", SettingsEnv.Token);
      var page = await (await e.Http.SendAsync(settings)).Content.ReadAsStringAsync();
      foreach (var s in new[] { "서버 현황", "배포", "서버 상태", "href=\"/admin/jobs\"" }) Assert.Contains(s, page);
  }
  ```
  `ServerTests`: `Env`에 `public Task<string> Admin(string path)`(헤더 `X-ASH-Admin-Token: Token`으로 GET) 추가, `/` 조회 7곳을 `Admin("/admin/settings")`로. 120·130행(토큰 비었거나 짧음)은 `/admin/settings`가 403 로그인 화면이므로 `Assert.Contains("꺼져", …)`로 바꾼다. `JobTests` 177행은 `X-ASH-Admin-Token` 헤더로 `/admin/jobs`(`JobEnv(adminToken: "test-token-1234567890")`).
- [ ] **Step 2: 실행** — `dotnet test tests/ASH.Server.Tests --filter "FullyQualifiedName~SettingsTests|FullyQualifiedName~ServerTests"` → FAIL(404/`/`가 홈).
- [ ] **Step 3: 구현** — 위 Interfaces대로.
- [ ] **Step 4: 실행** — `dotnet test ASH.sln` → PASS.
- [ ] **Step 5: 커밋** — `feat(server): /jobs moves to /admin/jobs (301), server status lives on the settings page`.

### Task 3: Core — `SearchEstimate.Expected`와 `SearchJob.Run`의 `onProgress`

**Files:**
- Create: `src/ASH.Core/Runner/SearchEstimate.cs`
- Modify: `src/ASH.Core/Runner/SearchStrategy.cs`, `src/ASH.Core/Runner/SearchJob.cs`
- Test: `tests/ASH.Tests/Runner/SearchEstimateTests.cs` (Create), `tests/ASH.Tests/Runner/SearchStrategyTests.cs`

**Interfaces:**
- Produces: `public sealed record SearchProgress(string Phase, IReadOnlyDictionary<string, int> Confirmed, IReadOnlyDictionary<string, int> Current, int GroupsLeft, int ExpectedRuns)` — `Confirmed` = 묶음 탐색이 끝난 그룹의 대수, `Current` = 지금 시험 중인 묶음의 그룹별 대수(그 외 그룹은 없음), `GroupsLeft` = 아직 끝나지 않은 묶음들의 그룹 수 합, `ExpectedRuns` = 시작 때 계산값(이후 호출도 같은 값).
- Produces: `public static int SearchEstimate.Expected(ProjectInput input, GroupClusters clusters, int initialReps, bool impact)` =
  `Σ_b [ initReps × (1 + Σ_g ⌈log2(Max_g−Min_g+1)⌉ + k_b) + (reps − initReps) + (reps > 1 ? k_b × reps : 0) ] + reps + (impact ? k × reps : 0)`,
  `reps = max(1, Settings.Replications)`, `initReps = max(1, initialReps)`, `InitStockMode == "Fixed"`면 둘 다 1, `k` = 전체 그룹 수. 범위가 1(Max==Min)인 그룹은 log 항 0.
- Produces: `public static int SearchEstimate.Raise(int expected, int runsDone, int groupsLeft) => runsDone >= expected ? runsDone + Math.Max(1, groupsLeft) : expected`.
- Produces: `SearchStrategy.Search(ProjectInput input, IRunExecutor exec, GroupClusters clusters, SearchOptions opts, CancellationToken ct = default, Action<SearchProgress>? onProgress = null)` — 호출 시점: 묶음 시작(`Phase = "탐색 " + 묶음 이름`), 묶음 끝(`"확정 " + 묶음 이름`, `Confirmed`에 그 묶음 그룹 추가), 확인(`"확인"`), 영향표(`"영향표"`). `ExpectedRuns`는 인자로 받은 값을 그대로 넣는다 → `SearchOptions`에 `int ExpectedRuns = 0` 필드 추가.
- Produces: `SearchJob.Run(SearchJobOptions a, ProjectInput? input, TextWriter o, IProgress<RunOutcome>? progress = null, CancellationToken ct = default, Action<SearchProgress>? onProgress = null)` — 묶음을 만든 직후 `Expected`를 계산해 `o.WriteLine("expected runs: N")`, `onProgress(new SearchProgress("탐색 시작", {}, {}, k, N))`, 이어 `Search(..., new SearchOptions(a.InitialReps, a.Impact, ExpectedRuns: N), ct, onProgress)`. 기존 호출(Runner CLI·플러그인)은 그대로 컴파일된다.

- [ ] **Step 1: 실패하는 테스트** — `SearchEstimateTests.cs`(`SearchStrategyTests`의 `Input`/`Case` 헬퍼를 `internal static`으로 공유)
  ```csharp
  [Fact] public void Expected_Counts_Search_Decided_Minus1_Confirm_And_Impact()
  {
      var p = SearchStrategyTests.Input(("G1", 1, 4), ("G2", 1, 4), ("G3", 1, 3)); // Replications 2
      var c = new GroupClusters { Clusters = new IReadOnlyList<string>[] { new[] { "G1", "G2" }, new[] { "G3" } } };
      Assert.Equal(27, SearchEstimate.Expected(p, c, 1, true));   // (7+1+4) + (4+1+2) + 2 + 6
      Assert.Equal(21, SearchEstimate.Expected(p, c, 1, false));  // 영향표 없음: 12 + 7 + 2
  }
  [Fact] public void Raise_Lifts_Expected_Only_When_Reached()
  {
      Assert.Equal(27, SearchEstimate.Raise(27, 20, 3));
      Assert.Equal(30, SearchEstimate.Raise(27, 27, 3));
      Assert.Equal(31, SearchEstimate.Raise(27, 30, 0));
  }
  [Fact] public void Search_Reports_Progress_And_Never_Exceeds_Expected()
  {
      var (p, c, f) = SearchStrategyTests.Case();
      var seen = new List<SearchProgress>();
      var n = SearchEstimate.Expected(p, c, 1, true);
      SearchStrategy.Search(p, f, c, new SearchOptions(ImpactTable: true, ExpectedRuns: n), default, seen.Add);
      Assert.True(f.Calls <= n, $"{f.Calls} > {n}");
      Assert.Equal(new[] { "탐색 G1,G2", "확정 G1,G2", "탐색 G3", "확정 G3", "확인", "영향표" }, seen.Select(s => s.Phase));
      Assert.Equal(4, seen[1].Confirmed["G2"]); Assert.Equal(1, seen[1].GroupsLeft); Assert.Equal(n, seen[0].ExpectedRuns);
  }
  ```
- [ ] **Step 2: 실행** — `dotnet test tests/ASH.Tests --filter "FullyQualifiedName~SearchEstimateTests"` → FAIL(타입 없음).
- [ ] **Step 3: 구현** — Interfaces대로. `SearchStrategy`의 `foreach (var cluster …)` 앞뒤와 confirm/impact 앞에 콜백 4곳.
- [ ] **Step 4: 실행** — `dotnet test tests/ASH.Tests` → PASS(`RunnerCliTests`·`RunnerEndToEndTests` 포함).
- [ ] **Step 5: 커밋** — `feat(core): SearchEstimate.Expected from the search logic, SearchJob.Run onProgress callback`.

### Task 4: `JobInfo.ExpectedRuns/Partial/RunSeconds`, 워커 연결, `/api/jobs/{id}` 확장

**Files:**
- Modify: `src/ASH.Server/JobStore.cs` (필드), `src/ASH.Server/JobWorker.cs` (`IJobRunner.Run` 서명, `RunOne`), `src/ASH.Server/Routes.Jobs.cs` (`/api/jobs/{id}`)
- Create: `src/ASH.Server/JobProgress.cs`
- Test: `tests/ASH.Server.Tests/JobProgressTests.cs` (Create), `tests/ASH.Server.Tests/JobTests.cs` (`FakeRunner`)

**Interfaces:**
- Produces (`JobInfo`): `public int? ExpectedRuns { get; set; }`, `public Dictionary<string, int>? Partial { get; set; }`, `public double RunSeconds { get; set; }` (완료 실행의 `RunOutcome.Duration` 초 합).
- Produces: `public interface IJobRunner { JobOutcome Run(JobInfo job, string projectDir, TextWriter log, Action<string> phase, IProgress<RunOutcome> progress, Action<SearchProgress> search, CancellationToken ct); }` — `AutoModJobRunner`는 `search`를 `SearchJob.Run(..., onProgress: search)`로 넘긴다. 테스트 `FakeRunner`는 `phase("대수 탐색")` 뒤 `search(new SearchProgress("탐색 시작", {}, {}, 1, 3))`, 실행 3회 뒤 `search(new SearchProgress("확정 G1", {G1=2}, {}, 0, 3))`.
- 워커(`RunOne`): `search` 콜백 → `store.Update(id, j => { j.ExpectedRuns ??= p.ExpectedRuns; j.Partial = p.Confirmed.Count == 0 ? null : new(p.Confirmed); j.Phase = p.Phase; })`, `groupsLeft` 보관; `progress` 콜백에 `j.RunSeconds += r.Duration.TotalSeconds; if (j.ExpectedRuns is { } e) j.ExpectedRuns = SearchEstimate.Raise(e, j.RunsDone, groupsLeft);` 추가.
- Produces: `public static class JobProgress` — `public static double? Ratio(JobInfo j)` (`Done` → 1.0; `ExpectedRuns is null or <= 0` → null; `Queued` → 0; 그 외 `Math.Min(RunsDone / (double)ExpectedRuns, 0.99)`), `public static TimeSpan? AvgRun(JobInfo j)` (`RunsDone > 0 ? FromSeconds(RunSeconds / RunsDone) : null`), `public static DateTime? Eta(JobInfo j, TimeSpan? typical, int parallel, DateTime nowUtc)` (`State != Running` → null; `AvgRun`·`ExpectedRuns` 있으면 `now + (Expected − RunsDone) × avg ÷ max(1, parallel)`; `RunsDone == 0` → `StartedUtc + typical`(둘 중 null이면 null)), `public static int Parallel(IConfiguration cfg) => Math.Max(1, cfg.GetValue("Jobs:Parallel", 2))`.
- `/api/jobs/{id}` 응답에 추가: `expectedRuns`, `progress`(= `Ratio`), `partial`, `eta`(`yyyy-MM-ddTHH:mm:ssZ` 또는 null), `avgRunSec`, 그리고 Task 7의 `active`/`fields`/`watch`는 Task 7에서 더한다.

- [ ] **Step 1: 실패하는 테스트** — `JobProgressTests.cs`
  ```csharp
  [Fact] public void Progress_Never_Exceeds_One_And_Is_Full_Only_When_Done()
  {
      var j = new JobInfo { State = JobState.Running, ExpectedRuns = 10, RunsDone = 10 };
      Assert.Equal(0.99, JobProgress.Ratio(j)!.Value, 3);
      j.RunsDone = 25; Assert.Equal(0.99, JobProgress.Ratio(j)!.Value, 3);
      j.State = JobState.Failed; Assert.Equal(0.99, JobProgress.Ratio(j)!.Value, 3);
      j.State = JobState.Done; Assert.Equal(1.0, JobProgress.Ratio(j));
      Assert.Null(JobProgress.Ratio(new JobInfo { State = JobState.Running }));
      Assert.Equal(0.0, JobProgress.Ratio(new JobInfo { State = JobState.Queued, ExpectedRuns = 5 }));
  }
  [Fact] public void Eta_Uses_Average_Run_Over_Parallel()
  {
      var now = new DateTime(2026, 10, 23, 1, 0, 0, DateTimeKind.Utc);
      var j = new JobInfo { State = JobState.Running, ExpectedRuns = 12, RunsDone = 4, RunSeconds = 480, StartedUtc = now.AddMinutes(-10) };
      Assert.Equal(now.AddMinutes(8), JobProgress.Eta(j, null, 2, now));          // (12−4)×120 s ÷ 2
      Assert.Equal(TimeSpan.FromMinutes(2), JobProgress.AvgRun(j));
      var fresh = new JobInfo { State = JobState.Running, StartedUtc = now.AddMinutes(-1), ExpectedRuns = 12 };
      Assert.Equal(now.AddMinutes(29), JobProgress.Eta(fresh, TimeSpan.FromMinutes(30), 2, now));
      Assert.Null(JobProgress.Eta(fresh, null, 2, now));
  }
  ```
  `JobTests.cs`에 추가:
  ```csharp
  [Fact] public async Task Api_Job_Carries_Expected_Progress_And_Partial()
  {
      using var env = new JobEnv();
      var id = Id(await (await env.Send(JobEnv.MakePackage(env.Dir))).Content.ReadAsStringAsync());
      var j = await env.WaitFinished(id);
      Assert.Equal(3, j.ExpectedRuns); Assert.Equal(2, j.Partial!["G1"]); Assert.Equal(15.0, j.RunSeconds);
      var api = JsonDocument.Parse(await env.Http.GetStringAsync("/api/jobs/" + id)).RootElement;
      Assert.Equal(1.0, api.GetProperty("progress").GetDouble()); Assert.Equal(3, api.GetProperty("expectedRuns").GetInt32());
      Assert.Equal(2, api.GetProperty("partial").GetProperty("G1").GetInt32());
  }
  [Fact] public async Task Api_Job_Has_Null_Progress_For_Legacy_Job()
  {
      using var env = new JobEnv(worker: false);
      var dir = Path.Combine(env.Store.Dir, "20250101-000000-abcd"); Directory.CreateDirectory(dir);
      File.WriteAllText(Path.Combine(dir, "job.json"), "{\"Id\":\"20250101-000000-abcd\",\"State\":\"done\",\"RunsDone\":7}");
      var js = new JobStore(env.Store.Dir, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
      var j = js.Get("20250101-000000-abcd")!;
      Assert.Null(j.ExpectedRuns); Assert.Null(j.Partial); Assert.Equal(1.0, JobProgress.Ratio(j)); // done → 100%
      j.State = JobState.Failed; Assert.Null(JobProgress.Ratio(j));
  }
  ```
- [ ] **Step 2: 실행** — `dotnet test tests/ASH.Server.Tests --filter "FullyQualifiedName~JobProgressTests|FullyQualifiedName~JobTests"` → FAIL.
- [ ] **Step 3: 구현** — Interfaces대로(`FakeRunner`도 새 서명).
- [ ] **Step 4: 실행** — `dotnet test ASH.sln` → PASS.
- [ ] **Step 5: 커밋** — `feat(server): job progress (ExpectedRuns, Partial, RunSeconds, eta) from the search callback, /api/jobs/{id} fields`.

### Task 5: `HomeView` — 내 것 판정·실행/대기/분석 목록 모델과 `GET /api/home`

**Files:**
- Create: `src/ASH.Server/HomeView.cs`
- Modify: `src/ASH.Server/Routes.Analysis.cs` (`/api/home`)
- Test: `tests/ASH.Server.Tests/HomeViewTests.cs` (Create)

**Interfaces:**
- Produces: `public static class Mine` — `public static HashSet<string> Names(IReadOnlyList<JobInfo> jobs, string ip)` (Ip == ip인 작업들의 Submitter, 빈 문자열 제외), `public static bool Is(JobInfo j, string ip, ISet<string> names) => j.Ip == ip || names.Contains(j.Submitter)`.
- Produces (records, JSON 그대로 내보냄):
  - `public sealed record RunningCard(string JobId, string Title, string State, string? Phase, double? Progress, int RunsDone, int? ExpectedRuns, string? AvgRun, string? Eta, string Note, string? Film)` — `Title` = `"분석 › 안 vN"`(미분류면 `"Project / Plan"`), `AvgRun` = `JobPages.Dur`, `Eta` = `HH:mm`(서울), `Film` = `"촬영 k/K"` 또는 null.
  - `public sealed record QueuedLine(string JobId, string Title, string? Start)` (`Start` = 시작 예상 `HH:mm`).
  - `public sealed record PlanChip(string PlanId, string PlanName, string JobId, int Version, string State, int? Total, double? Progress, bool Selected)`.
  - `public sealed record AnalysisRow(string Id, string Name, bool Active, bool Mine, int Plans, int Versions, int Selected, int Films, DateTime? LastSentUtc, IReadOnlyList<PlanChip> Chips, IReadOnlyList<string> Submitters)` — `Chips` = 안별 최신 버전, `Submitters` = 이 분석 작업의 보낸 사람(중복 제거; `/analyses` 검색용).
  - `public sealed record HomeView(RunningCard? Running, bool WorkerOn, int QueuedCount, IReadOnlyList<QueuedLine> Queued, bool Me, IReadOnlyList<AnalysisRow> InProgress, IReadOnlyList<AnalysisRow> Finished, int Unclassified)`.
- Produces: `public static HomeView HomeView.Build(IReadOnlyList<AnalysisInfo> analyses, IReadOnlyList<JobInfo> jobs, IReadOnlyList<FilmInfo> films, FilmInfo? filming, (int Ahead, DateTime? Start, TimeSpan? Typical) queue, bool workerOn, int parallel, string ip, bool? me, DateTime nowUtc)` — `me == null`이면 내 것이 1건 이상일 때 `Me = true`, 0건이면 `false`; `Queued`는 앞 3건, 시작 예상 = `queue.Start + Typical × i`; 각 절은 `LastSentUtc` 역순.
- 라우트: `GET /api/home?me=1|0`(없으면 null) → `Results.Json(HomeView.Build(...))` + Task 7의 `fields`.

- [ ] **Step 1: 실패하는 테스트** — `HomeViewTests.cs`(`JobInfo`를 직접 만들고 `AnalysisStore` 없이 `AnalysisInfo` 객체 사용)
  ```csharp
  static JobInfo J(string id, string ip, string who, string state = "done", string? analysis = null, string? plan = null, int version = 1, bool selected = false, int? expected = null, int runs = 0) =>
      new() { Id = id, Ip = ip, Submitter = who, State = state, AnalysisId = analysis, PlanId = plan, Version = version, Selected = selected, ExpectedRuns = expected, RunsDone = runs,
              CreatedUtc = new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc), Decided = state == "done" ? new() { ["G1"] = 2 } : null };
  static readonly DateTime now = new(2026, 10, 23, 1, 0, 0, DateTimeKind.Utc);
  static AnalysisInfo A(string id, string name, string planId) => new() { Id = id, Name = name, Plans = { new PlanInfo { Id = planId, Name = "A안" } } };
  // 분석 A(a1/p1): v1 done 선정, v2 running(10회 중 3회), 보낸 IP 10.0.0.5 — 분석 B(a2/p2): v1 done, 보낸 IP 10.0.0.9
  static readonly AnalysisInfo[] analyses = { A("a1", "A", "p1"), A("a2", "B", "p2") };
  static readonly JobInfo[] jobs = { J("j1", "10.0.0.5", "kim", analysis: "a1", plan: "p1", selected: true), J("j2", "10.0.0.5", "kim", "running", "a1", "p1", 2, expected: 10, runs: 3), J("j3", "10.0.0.9", "park", analysis: "a2", plan: "p2") };

  [Fact] public void Mine_Collects_Every_Submitter_Of_The_Ip()
  {
      var jobs = new[] { J("a", ip: "10.0.0.5", who: "kim"), J("b", ip: "10.0.0.5", who: "lee"), J("c", ip: "10.0.0.9", who: "lee"), J("d", ip: "10.0.0.9", who: "park") };
      var names = Mine.Names(jobs, "10.0.0.5");
      Assert.Equal(new[] { "kim", "lee" }, names.OrderBy(x => x));
      Assert.True(Mine.Is(jobs[2], "10.0.0.5", names)); Assert.False(Mine.Is(jobs[3], "10.0.0.5", names));
  }
  [Fact] public void Build_Splits_In_Progress_And_Finished_And_Falls_Back_To_All()
  {
      var v = HomeView.Build(analyses, jobs, [], null, (1, null, null), true, 2, "10.0.0.5", me: null, now);
      Assert.True(v.Me); Assert.Single(v.InProgress); Assert.Equal("A", v.InProgress[0].Name); Assert.Empty(v.Finished); // B는 내 것 아님
      Assert.Equal(0.3, v.Running!.Progress!.Value, 3); Assert.Equal("A › A안 v2", v.Running.Title);
      Assert.Equal(new[] { "kim" }, v.InProgress[0].Submitters);
      var all = HomeView.Build(analyses, jobs, [], null, (1, null, null), true, 2, "10.0.0.5", me: false, now);
      Assert.Single(all.Finished); Assert.Equal("B", all.Finished[0].Name);
      var nobody = HomeView.Build(analyses, jobs, [], null, (1, null, null), true, 2, "10.9.9.9", me: null, now);
      Assert.False(nobody.Me); Assert.Equal(2, nobody.InProgress.Count + nobody.Finished.Count);
  }
  ```
- [ ] **Step 2: 실행** — `dotnet test tests/ASH.Server.Tests --filter "FullyQualifiedName~HomeViewTests"` → FAIL.
- [ ] **Step 3: 구현** — Interfaces대로 + `/api/home`.
- [ ] **Step 4: 실행** → PASS.
- [ ] **Step 5: 커밋** — `feat(server): HomeView model (mine by ip+submitter, running/queued/analysis rows) and GET /api/home`.

### Task 6: 홈 페이지와 분석 목록 페이지

**Files:**
- Create: `src/ASH.Server/HomePages.cs`
- Modify: `src/ASH.Server/Program.cs` (`GET /`), `src/ASH.Server/Routes.Analysis.cs` (`GET /analyses?q=`), `src/ASH.Server/AnalysisPages.cs` (`Index` 삭제)
- Test: `tests/ASH.Server.Tests/HomePageTests.cs` (Create), `tests/ASH.Server.Tests/AnalysisTests.cs` 303행(`/analyses`에 이름 포함 — 그대로 통과해야 함)

**Interfaces:**
- Produces: `public static string HomePages.Home(HomeView v, string? pollScript)` — 순서: 실행 카드(`없으면 "실행 중인 작업 없음 · 작업 실행 켜짐/꺼짐"`), 대기 한 줄, 토글 `<a href="/?me=1">내 것만</a> | <a href="/?me=0">전체</a>`(현재 쪽 `class="chip sel"`; 인라인 JS가 클릭 때 `localStorage.ashMe`에 저장하고, `?me` 없이 열리면 저장값으로 `location.replace`), 절 `<h2>진행 중</h2>`·`<h2>완료</h2>`(`AnalysisList`), `<details><summary>분석에 넣지 않은 작업 n건</summary><a href="/analyses/unclassified">…`.
- Produces: `internal static void HomePages.RunningCard(StringBuilder sb, RunningCard c, string keyPrefix)` — `data-field="{keyPrefix}.phase|.runs|.progress|.eta|.avg|.film"`·`data-bar="{keyPrefix}.bar"`·`data-state="{keyPrefix}.state"`(Task 7 키 규칙). 현황 탭도 이 메서드를 쓴다.
- Produces: `internal static void HomePages.AnalysisList(StringBuilder sb, IReadOnlyList<AnalysisRow> rows)` — 한 줄: `<a href="/analyses/{id}">이름</a> · 상태 칩 · 안 a · 버전 v · 선정 s · 영상 f · 마지막 보냄 · 칩(`A안 v2 24대`, 선정은 `chip sel`, 실행 중은 `진행률 %`). 빈 목록은 `"분석이 없습니다."`.
- Produces: `public static string HomePages.Analyses(IReadOnlyList<AnalysisRow> rows, string q, int unclassified)` — `/analyses`: 검색 폼 `<form><input name="q" placeholder="이름·보낸 사람">` + `AnalysisList`(전체, 진행 중·완료 한 목록) + 기존 "새 분석" 폼 + 미분류 링크. 검색은 `Name` 또는 `Submitters`에 `q` 포함(대소문자 무시).
- 라우트: `GET /?me=` → `HomeView.Build(..., me: query "1"→true, "0"→false, 없음→null)`, `filming = fs.All().FirstOrDefault(f => f.State == JobState.Running)`; `GET /analyses?q=`.
- 테스트 헬퍼(`JobTests.cs`의 `JobEnv`에 추가): `public Task<string> SendFiled(string analysis, string plan, string submitter = "tester")`(`FilmTests.Send`를 옮긴 것: analysisName/planName으로 보내고 id 반환), `public static Task<T> Until<T>(Func<T?> get, int seconds = 60) where T : class`(`FilmTests.Until`을 옮김; `FilmTests`는 이 둘을 쓴다).

- [ ] **Step 1: 실패하는 테스트** — `HomePageTests.cs`
  ```csharp
  [Fact] public async Task Home_Shows_Running_Card_And_My_Analyses()
  {
      using var env = new JobEnv(); env.Runner.Gate.Reset();
      var id = await env.SendFiled("라인 증설", "A안");
      await JobEnv.Until(() => env.Store.Get(id) is { State: JobState.Running } x ? x : null);
      var html = await env.Http.GetStringAsync("/");
      foreach (var s in new[] { "라인 증설 › A안 v1", "data-field=\"running.phase\"", "data-bar=\"running.bar\"", "<h2>진행 중</h2>", "내 것만", "href=\"/analyses/unclassified\"" }) Assert.Contains(s, html);
      Assert.DoesNotContain("http-equiv", html);
      env.Runner.Gate.Set(); await env.WaitFinished(id);
      var done = await env.Http.GetStringAsync("/");
      Assert.Contains("실행 중인 작업 없음", done); Assert.Contains("<h2>완료</h2>", done); Assert.Contains("A안 v1 2대", done);
  }
  [Fact] public async Task Home_Falls_Back_To_All_When_Nothing_Is_Mine()
  {
      using var env = new JobEnv(); await env.WaitFinished(await env.SendFiled("남의 분석", "A안", submitter: "other"));
      env.Store.Update(env.Store.All()[0].Id, j => j.Ip = "10.9.9.9"); // 테스트 클라이언트 IP가 아님
      var html = await env.Http.GetStringAsync("/");
      Assert.Contains("남의 분석", html); Assert.Contains("class=\"chip sel\">전체", html);
      Assert.DoesNotContain("남의 분석", await env.Http.GetStringAsync("/?me=1"));
  }
  [Fact] public async Task Analyses_List_Searches_Name_And_Submitter()
  {
      using var env = new JobEnv();
      await env.WaitFinished(await env.SendFiled("라인 증설", "A안", submitter: "kim")); await env.WaitFinished(await env.SendFiled("창고 이전", "A안", submitter: "lee"));
      Assert.Contains("라인 증설", await env.Http.GetStringAsync("/analyses?q=kim"));
      Assert.DoesNotContain("창고 이전", await env.Http.GetStringAsync("/analyses?q=kim"));
      Assert.Contains("창고 이전", await env.Http.GetStringAsync("/analyses?q=창고"));
  }
  ```
- [ ] **Step 2: 실행** — `dotnet test tests/ASH.Server.Tests --filter "FullyQualifiedName~HomePageTests"` → FAIL.
- [ ] **Step 3: 구현** — Interfaces대로. `AnalysisPages.Index` 삭제.
- [ ] **Step 4: 실행** — `dotnet test ASH.sln` → PASS.
- [ ] **Step 5: 커밋** — `feat(server): home with running card, queue line, 내 것만|전체 toggle and 진행 중/완료 analysis lists; /analyses search`.

### Task 7: 폴링 — `{active, fields, watch}` JSON과 `data-field` 갱신 스크립트

**Files:**
- Create: `src/ASH.Server/LiveFields.cs`
- Modify: `src/ASH.Server/JobPages.cs` (`PollScript`, 작업 페이지에 `data-field`), `src/ASH.Server/HomePages.cs`, `src/ASH.Server/HomeView.cs` (`Fields()`), `src/ASH.Server/Routes.Jobs.cs` (`/api/jobs/{id}`), `src/ASH.Server/Routes.Analysis.cs` (`/api/home`, `/api/films/{id}`), `src/ASH.Server/AnalysisPages.cs` (`Film` 페이지)
- Test: `tests/ASH.Server.Tests/LiveFieldsTests.cs` (Create), `tests/ASH.Server.Tests/HomePageTests.cs`

**Interfaces:**
- Produces: `public sealed record Live(bool Active, IReadOnlyDictionary<string, string> Fields, IReadOnlyDictionary<string, string> Watch)` — `Fields` 키 규칙: `"<prefix>.<name>"` 텍스트, `"<prefix>.bar"` 퍼센트 숫자 문자열(`"0"~"100"`), `"<prefix>.state"` 상태값(`queued|running|done|failed|cancelled`). `Watch` = 작업/영상 id → 상태(끝남 감지용). `Active` = `Watch`에 `queued|running`이 하나라도 있음.
- Produces: `public static class LiveFields` — `public static Live OfHome(HomeView v)` (키 `running.*`, `queued.count`, `chip.<jobId>.text|.state`), `public static Live OfJob(JobInfo j, TimeSpan? typical, int parallel)` (키 `job.state|.phase|.runs|.progress|.eta|.avg|.bar|.last`), `public static Live OfFilm(FilmInfo f)` (키 `film.state|.phase|.done|.bar`), `public static Live OfAnalysis(AnalysisInfo a, IReadOnlyList<JobInfo> jobs, IReadOnlyList<FilmInfo> films, TimeSpan? typical, int parallel)` (버전 줄 `job.<id>.*` + 실행 카드 `running.*` + 중간 결과 `partial.<group>`; Task 8에서 씀).
- Produces: `public static string JobPages.PollScript(string url)` — `<script>`: 10초마다 `fetch(url, {cache:'no-store'})`; `fields`를 `document.querySelectorAll('[data-field="k"]')`에 `textContent`로, `.bar` 키는 `[data-bar="k"] i`의 `style.width = v + '%'`, `.state` 키는 `[data-state="k"]`의 `className = 'state s-' + v`; `watch`를 이전 값과 비교해 `queued|running → 그 외`가 생기면 `#reload-bar`를 보이고(`"결과가 나왔습니다 — "` + `<a href="javascript:location.reload()">새로 읽기</a>`) `setTimeout(location.reload, 60000)` 한 번; `active === false`면 `clearInterval`; fetch 실패 시 `#net`에 `"연결 끊김 · 다시 시도 중"`, 성공 시 비움. 페이지는 `Page(title, body, live.Active ? PollScript(url) : null)`로 만들고 `body` 맨 앞에 `<div id="reload-bar"></div><span id="net"></span>`.
- `/api/jobs/{id}`: 기존 필드 + Task 4 필드 + `active`, `fields`, `watch`. `/api/home`: `HomeView` 필드 + `active`, `fields`, `watch`. 신설 `GET /api/films/{id}` → `OfFilm` + `done`, `total`, `state`.
- 작업 페이지(`JobPages.Job`)의 상태·단계·실행 수·마지막 출력에 `data-field="job.*"`, 실행 중이면 진행바 `data-bar="job.bar"`; `AnalysisPages.Film`의 상태·컷 k/K에 `data-field="film.*"`.

- [ ] **Step 1: 실패하는 테스트** — `LiveFieldsTests.cs`
  ```csharp
  [Fact] public void Fields_Are_Plain_Text()
  {
      var j = new JobInfo { Id = "20260101-000000-aaaa", State = JobState.Running, Phase = "<b>탐색</b>", RunsDone = 3, ExpectedRuns = 10, LastLine = "a&b" };
      var l = LiveFields.OfJob(j, null, 2);
      Assert.True(l.Active); Assert.Equal("<b>탐색</b>", l.Fields["job.phase"]); Assert.Equal("30", l.Fields["job.bar"]); Assert.Equal("running", l.Fields["job.state"]);
      Assert.Equal("running", l.Watch["20260101-000000-aaaa"]);
      Assert.False(LiveFields.OfJob(new JobInfo { Id = "20260101-000000-bbbb", State = JobState.Done }, null, 2).Active);
  }
  [Fact] public void Poll_Script_Uses_TextContent_Only()
  {
      var s = JobPages.PollScript("/api/home?me=1");
      Assert.Contains("textContent", s); Assert.DoesNotContain("innerHTML", s); Assert.Contains("10000", s); Assert.Contains("60000", s); Assert.Contains("연결 끊김 · 다시 시도 중", s);
  }
  ```
  `HomePageTests.cs`에 추가:
  ```csharp
  [Fact] public async Task Poll_Script_Only_When_Active()
  {
      using var env = new JobEnv(); env.Runner.Gate.Reset();
      var id = await env.SendFiled("라인 증설", "A안");
      await JobEnv.Until(() => env.Store.Get(id) is { State: JobState.Running } x ? x : null);
      Assert.Contains("fetch('/api/home", await env.Http.GetStringAsync("/"));
      Assert.Contains("fetch('/api/jobs/" + id, await env.Http.GetStringAsync("/jobs/" + id));
      var api = JsonDocument.Parse(await env.Http.GetStringAsync("/api/home?me=1")).RootElement;
      Assert.True(api.GetProperty("active").GetBoolean()); Assert.Equal("running", api.GetProperty("fields").GetProperty("running.state").GetString());
      env.Runner.Gate.Set(); await env.WaitFinished(id);
      Assert.DoesNotContain("fetch(", await env.Http.GetStringAsync("/")); Assert.DoesNotContain("fetch(", await env.Http.GetStringAsync("/jobs/" + id));
  }
  ```
- [ ] **Step 2: 실행** — `dotnet test tests/ASH.Server.Tests --filter "FullyQualifiedName~LiveFieldsTests|FullyQualifiedName~HomePageTests"` → FAIL.
- [ ] **Step 3: 구현** — Interfaces대로.
- [ ] **Step 4: 실행** — `dotnet test ASH.sln` → PASS.
- [ ] **Step 5: 커밋** — `feat(server): 10 s polling of data-field elements from {active, fields, watch} JSON on home, job and film pages; GET /api/films/{id}`.

### Task 8: 분석 화면 탭 껍데기 + 현황 탭 + `GET /api/analyses/{id}/status`

**Files:**
- Modify: `src/ASH.Server/AnalysisPages.cs` (`Detail` → `Shell` + `StatusTab`), `src/ASH.Server/Routes.Analysis.cs`
- Test: `tests/ASH.Server.Tests/AnalysisPageTests.cs` (Create), `tests/ASH.Server.Tests/AnalysisTests.cs` 296·315행(현황 탭이 기본이라 그대로 통과; 316행 "선정 버전 비교"는 Task 10에서 바꿈), `tests/ASH.Server.Tests/AnalysisDeleteTests.cs` 137행(현황 탭에 링크 유지)

**Interfaces:**
- Produces: `public sealed record AnalysisPageData(AnalysisInfo A, IReadOnlyList<JobInfo> Jobs, IReadOnlyList<FilmInfo> Films, JobStore Store, (int Ahead, DateTime? Start, TimeSpan? Typical) Queue, int Parallel, int CutCount)` — `Jobs`는 이 분석의 작업만.
- Produces: `public static string AnalysisPages.Detail(AnalysisPageData d, string tab, IQueryCollection q, string? pollScript)` — `tab ∈ status|conclusion|compare|films`(그 외 → status). 상단 `<p class="crumb">`·`<h1>`·메모·만든 시각·숫자 카드 `안 · 버전 · 선정 · 영상` + `<div class="tabs">`(`?tab=`) + `<div class="panel">`에 탭 본문. 탭 본문은 `StatusTab`(이 Task), `ConclusionTab`(Task 9), `CompareTab`(Task 10), `FilmsTab`(Task 11).
- Produces: `static void AnalysisPages.StatusTab(StringBuilder sb, AnalysisPageData d)` — 실행·대기 버전 카드(`HomePages.RunningCard`, prefix `running`; 대기 버전은 `queued.<id>`), 실행 중이면 `<p>중간 결과: <span data-field="partial.text">G01 4 · G02 3 · G03 5 시험 중 · G04 탐색 중</span></p>`(`Partial`에 있으면 `G 4`, 없고 `Phase`가 `"탐색 "`로 시작하며 그 묶음에 속하면 `G n 시험 중`(n = 그 그룹 Max), 그 외 `탐색 중`), 안별 절(버전 줄 `v2 [선정] · 24대 · 가동률 78% · 결품 0초 · 날짜 · 보낸 사람 · 메모` + 선정 토글 `SelectForm` + `열기`; 실패 버전은 `<span class="state s-failed">실패</span>` + `Error` 첫 줄), `<details><summary>안 추가 · 이름 바꾸기 · 지우기</summary>` 안에 기존 폼 3개, 끝에 `관리자: 분석 삭제…` 링크.
- 라우트: `GET /analyses/{id}?tab=` → `Detail(...)`, 폴링 URL `/api/analyses/{id}/status`(현황 탭에서만 `pollScript`). `GET /api/analyses/{id}/status` → `LiveFields.OfAnalysis(...)` + `analysis` 요약(`{plans, versions, selected, films}`).

- [ ] **Step 1: 실패하는 테스트** — `AnalysisPageTests.cs`
  ```csharp
  [Fact] public async Task Status_Tab_Is_Default_And_Shows_Partial_While_Running()
  {
      using var env = new JobEnv(); env.Runner.Gate.Reset();
      var id = await env.SendFiled("라인 증설", "A안");
      await JobEnv.Until(() => env.Store.Get(id) is { State: JobState.Running } x ? x : null);
      env.Store.Update(id, j => { j.Partial = new() { ["G1"] = 2 }; j.Phase = "확정 G1"; });
      var aid = env.Factory.Services.GetRequiredService<AnalysisStore>().All().Single().Id;
      var html = await env.Http.GetStringAsync("/analyses/" + aid);
      foreach (var s in new[] { "class=\"tabs\"", "?tab=status\" class=\"on\"", "?tab=conclusion", "?tab=compare", "?tab=films", "G1 2", "data-field=\"partial.text\"", "fetch('/api/analyses/" + aid + "/status", "관리자: 분석 삭제" }) Assert.Contains(s, html);
      var api = JsonDocument.Parse(await env.Http.GetStringAsync($"/api/analyses/{aid}/status")).RootElement;
      Assert.Equal("G1 2", api.GetProperty("fields").GetProperty("partial.text").GetString());
      env.Runner.Gate.Set(); await env.WaitFinished(id);
      var done = await env.Http.GetStringAsync("/analyses/" + aid + "?tab=status");
      Assert.Contains("v1", done); Assert.Contains("2대", done); Assert.DoesNotContain("fetch(", done);
  }
  ```
- [ ] **Step 2: 실행** — `dotnet test tests/ASH.Server.Tests --filter "FullyQualifiedName~AnalysisPageTests"` → FAIL.
- [ ] **Step 3: 구현** — Interfaces대로. 결론 탭은 빈 `<p class="muted">준비 중</p>`(Task 9가 채움), 비교 탭은 기존 `Compare(sb, a, jobs, metrics)` 표(Task 10이 바꿈), 영상 탭은 기존 `FilmList(sb, films, a.Id)` 표(Task 11이 바꿈)를 그대로 넣는다. `AnalysisTests` 316행은 `Get(env, "/analyses/" + a.Id + "?tab=compare")`로, `FilmTests` 172행은 `"/analyses/" + aid + "?tab=films"`로 바꾼다.
- [ ] **Step 4: 실행** — `dotnet test ASH.sln` → PASS.
- [ ] **Step 5: 커밋** — `feat(server): analysis page with tabs; 현황 tab with running card, partial results and version rows; GET /api/analyses/{id}/status`.

### Task 9: 결론 탭 — 숫자 3개·묶음별 그룹 막대·병목 지도 1개·영향 상위 3

**Files:**
- Modify: `src/ASH.Server/JobPages.cs` (`Conclusion`, `ClusterBars`, `Map` 크기 인자), `src/ASH.Server/AnalysisPages.cs` (`ConclusionTab`)
- Test: `tests/ASH.Server.Tests/ConclusionTests.cs` (Create)

**Interfaces:**
- Produces: `internal static string JobPages.Map(JobView v, string title, Func<BlockView, double> value, Func<double, string> label, double w = 520, double h = 360)`; 가장 심한 block 3개에 `<text>` 라벨(`이름 값`).
- Produces: `public static void JobPages.Conclusion(StringBuilder sb, JobInfo j, JobView v, bool small)` — `<div class="grid2">` 왼쪽: 숫자 카드 3개(`권장 총 대수 N대 · 시험 범위 a~b대`, `최대 가동률 x% · 상한 u% · 그룹 G`, `최대 병목 B · 평균 대기 s초`), `ClusterBars`, 상위 3 문장; 오른쪽: 지도(`small`이면 `w=360,h=250`) + 토글 링크 `?metric=wait|occ`(기본 wait); 아래 버튼 `상세 보기(/jobs/{id}?tab=detail)` · `Excel 보고서(/jobs/{id}/files/report.xlsx)` · `이 버전의 영상(?tab=films)`.
- Produces: `internal static void JobPages.ClusterBars(StringBuilder sb, JobInfo j, JobView v)` — `j.Clusters`마다 `<h3>묶음: G1, G2</h3>`(없으면 묶음 제목 없이 전체 한 절), 그룹마다 한 줄 `그룹 · 타입 · <span class="bar"><i width=util%><b left=utilMax%></span> · 가동률 · <b>권장 n대</b> · 범위`; `!AllOk`면 `<i>`에 `style="background:var(--bad)"` + `실패: 이유`.
- 상위 3: `v.Impact.OrderByDescending(r => Math.Abs(r.UtilDelta)).Take(3)` → `"{Group} +1대 → {OtherGroup} 가동률 {+x.x}%p"`; `Impact.Count == 0`이면 `"영향표 없음"`; 링크 `영향표 전체 → /jobs/{id}?tab=detail`.
- Produces: `static void AnalysisPages.ConclusionTab(StringBuilder sb, AnalysisPageData d, IQueryCollection q)` — 라디오 링크 `?tab=conclusion&view=one`(기본) / `&view=plans`; `one`: 버전 칩(`?tab=conclusion&view=one&job=<id>`; 기본 = 가장 최근 선정, 없으면 가장 최근 완료) + 그 버전 `Conclusion(..., small:false)`; `plans`: 안마다 선정(없으면 최신 완료) 버전의 `Conclusion(..., small:true)`. 완료 버전이 없으면 `"결과가 나온 버전이 없습니다."`. 인라인 JS: 라디오 클릭 때 `localStorage.ashConclusionView` 저장, `view` 없이 열면 저장값으로 `location.replace`.

- [ ] **Step 1: 실패하는 테스트** — `ConclusionTests.cs`(`JobView` 없이 `JobPages.ClusterBars`는 `JobView`가 필요하므로 `JobEnv`로 완료 작업을 만든 뒤 `JobView.For`)
  ```csharp
  [Fact] public async Task Conclusion_Renders_One_Map_And_Cluster_Bars_For_20_Groups()
  {
      using var env = new JobEnv(); var id = await env.SendFiled("라인 증설", "A안"); await env.WaitFinished(id);
      // 묶음 4개 × 그룹 5개(G1..G20); 입력에 없는 그룹(G2..)은 묶음 제목만 나오고 막대 줄은 G1 하나
      env.Store.Update(id, x => x.Clusters = Enumerable.Range(0, 4).Select(b => Enumerable.Range(1, 5).Select(i => $"G{b * 5 + i}").ToList()).ToList());
      var j = env.Store.Get(id)!; var v = JobView.For(env.Store, j);
      var sb = new StringBuilder(); JobPages.ClusterBars(sb, j, v);
      Assert.Equal(4, Regex.Matches(sb.ToString(), "<h3>묶음:").Count); Assert.Equal(1, Regex.Matches(sb.ToString(), "<span class=\"bar\">").Count);
      var page = new StringBuilder(); JobPages.Conclusion(page, j, v, small: false);
      Assert.Equal(1, Regex.Matches(page.ToString(), "<svg").Count);
      foreach (var s in new[] { "권장 총 대수", "최대 가동률", "최대 병목", "상세 보기", "?tab=detail", "/files/report.xlsx", "class=\"grid2\"" }) Assert.Contains(s, page.ToString());
  }
  [Fact] public async Task Conclusion_Without_Clusters_Is_Flat()
  {
      using var env = new JobEnv(); var id = await env.SendFiled("라인 증설", "A안"); await env.WaitFinished(id);
      env.Store.Update(id, x => x.Clusters = null);
      var j = env.Store.Get(id)!; var sb = new StringBuilder(); JobPages.ClusterBars(sb, j, JobView.For(env.Store, j));
      Assert.DoesNotContain("<h3>묶음:", sb.ToString()); Assert.Contains("<b>권장 2대</b>", sb.ToString());
  }
  [Fact] public async Task Conclusion_Tab_Defaults_To_Selected_Version_And_Lists_Per_Plan()
  {
      using var env = new JobEnv(); var a = await env.WaitFinished(await env.SendFiled("라인 증설", "A안")); var b = await env.WaitFinished(await env.SendFiled("라인 증설", "B안"));
      var st = env.Factory.Services.GetRequiredService<AnalysisStore>(); var aid = st.All().Single().Id; st.SetSelected(env.Store, a.Id, true);
      var one = await env.Http.GetStringAsync($"/analyses/{aid}?tab=conclusion");
      Assert.Contains($"job={a.Id}\" class=\"chip sel\"", one); Assert.Equal(1, Regex.Matches(one, "권장 총 대수").Count);
      var plans = await env.Http.GetStringAsync($"/analyses/{aid}?tab=conclusion&view=plans");
      Assert.Equal(2, Regex.Matches(plans, "권장 총 대수").Count); Assert.Equal(2, Regex.Matches(plans, "<svg").Count);
  }
  ```
  (첫 테스트의 `ClusterBars` 호출은 `env.Store.Update(id, x => x.Clusters = [[G1..G5],[G6..G10],[G11..G15],[G16..G20]])` 뒤 `env.Store.Get(id)!`로 한다.)
- [ ] **Step 2: 실행** — `dotnet test tests/ASH.Server.Tests --filter "FullyQualifiedName~ConclusionTests"` → FAIL.
- [ ] **Step 3: 구현** — Interfaces대로. `JobPages.Summary`(작업 페이지 결론 탭)는 그대로 둔다.
- [ ] **Step 4: 실행** — `dotnet test ASH.sln` → PASS.
- [ ] **Step 5: 커밋** — `feat(server): 결론 tab — three numbers, cluster bars, one bottleneck map, top-3 impact; one version or per plan`.

### Task 10: 비교 탭 — 지표 표(기준 대비 증감)·그룹 가동률 묶음 막대·지도 나란히

**Files:**
- Modify: `src/ASH.Server/AnalysisPages.cs` (`Compare` 삭제 → `CompareTab`, `CompareChart`)
- Test: `tests/ASH.Server.Tests/CompareTests.cs` (Create), `tests/ASH.Server.Tests/AnalysisTests.cs` 314~322행(`?tab=compare`, 제목 `"비교"`)

**Interfaces:**
- Produces: `internal sealed record CompareColumn(JobInfo Job, string PlanName, AnalysisPages.Metrics? M)`.
- Produces: `static void AnalysisPages.CompareTab(StringBuilder sb, AnalysisPageData d, IQueryCollection q)` — 선택 = `?jobs=id1,id2,…`(없으면 선정 버전 전부, 안 순서·버전 순). 버전 칩은 전부 보이고 고른 것은 `chip sel`, 칩 링크는 그 id를 토글한 `?tab=compare&jobs=…`; 각 열 머리에 `기준으로` 링크(그 id를 맨 앞으로). 2개 미만이면 `"버전을 2개 이상 고르세요."` + 칩만. 표(`<div class="scroll">`): 행 = `총 대수(대)`, `최대 가동률(%)`, `결품 합계(초)`, `교착(회)`, `최대 병목(block·초)`, 그룹별 `권장(대)`, 그룹별 `가동률(%)`; 기준 외 열은 값 뒤 `<small class="ok|bad">+Δ</small>`(작을수록 좋음: 대수·가동률·결품·교착·대기 → Δ<0 `ok`, Δ>0 `bad`, 0은 class 없음; 결과 없는 버전은 `-`). 투자비 행은 B가 더한다(여기서는 없음). 아래 `CompareChart`, 버튼 `병목 지도 나란히`(`&maps=1` → 고른 버전의 `JobPages.Map(wait, w=360,h=250)`을 `<div class="scroll"><div style="display:flex;gap:12px">`에 가로 배열).
- Produces: `internal static string AnalysisPages.CompareChart(IReadOnlyList<CompareColumn> cols, IReadOnlyList<string> groups, double utilMax)` — SVG 폭 `max(520, groups.Count × (cols.Count × 14 + 20))`, 그룹마다 버전별 막대(색 순환 `#1f3a5f #378add #e24b4a #3b6d11 #ef9f27 #7a4fb3`), 상한 점선, `<title>`에 값.

- [ ] **Step 1: 실패하는 테스트** — `CompareTests.cs`
  ```csharp
  [Fact] public async Task Compare_Tab_Shows_Deltas_Against_The_Base_And_Scrolls()
  {
      using var env = new JobEnv(); var a = await env.WaitFinished(await env.SendFiled("라인 증설", "A안")); var b = await env.WaitFinished(await env.SendFiled("라인 증설", "B안"));
      var st = env.Factory.Services.GetRequiredService<AnalysisStore>(); var aid = st.All().Single().Id;
      st.SetSelected(env.Store, a.Id, true); st.SetSelected(env.Store, b.Id, true);
      var html = await env.Http.GetStringAsync($"/analyses/{aid}?tab=compare");
      foreach (var s in new[] { "총 대수(대)", "최대 가동률(%)", "결품 합계(초)", "교착(회)", "최대 병목(block·초)", "G1 권장(대)", "G1 가동률(%)", "class=\"scroll\"", "<svg", "기준으로", "병목 지도 나란히" }) Assert.Contains(s, html);
      Assert.Contains("<small", html); // 같은 결과라 Δ 0: class 없음
      Assert.DoesNotContain("class=\"bad\">+", html);
      var maps = await env.Http.GetStringAsync($"/analyses/{aid}?tab=compare&maps=1");
      Assert.Equal(3, Regex.Matches(maps, "<svg").Count); // 차트 1 + 지도 2
      Assert.Contains("버전을 2개 이상 고르세요", await env.Http.GetStringAsync($"/analyses/{aid}?tab=compare&jobs={a.Id}"));
  }
  ```
  `AnalysisTests` 316행: `html = Get(env, "/analyses/" + a.Id + "?tab=compare")`, `IndexOf("비교")`로.
- [ ] **Step 2: 실행** — `dotnet test tests/ASH.Server.Tests --filter "FullyQualifiedName~CompareTests|FullyQualifiedName~AnalysisTests"` → FAIL.
- [ ] **Step 3: 구현** — Interfaces대로, 옛 `Compare` 삭제.
- [ ] **Step 4: 실행** — `dotnet test ASH.sln` → PASS.
- [ ] **Step 5: 커밋** — `feat(server): 비교 tab — metric table with deltas vs base, grouped utilization chart, side-by-side maps`.

### Task 11: 영상 탭 — 진행 카드·완료 카드·썸네일(ffmpeg)·`all.zip`

**Files:**
- Modify: `src/ASH.Core/Film/AutoViewRenderer.cs` (`IFilmRenderer.Thumbnail`, 구현), `src/ASH.Server/LiveFilmRenderer.cs` (`Thumbnail` 위임), `src/ASH.Server/FilmWorker.cs` (첫 완료 컷 뒤 썸네일), `src/ASH.Server/Routes.Analysis.cs` (`/films/{id}/files/{name}`에 `thumb.jpg`·`all.zip`), `src/ASH.Server/AnalysisPages.cs` (`FilmsTab`, `FilmList` 삭제)
- Test: `tests/ASH.Server.Tests/FilmTests.cs` (`FakeFilm.Thumbnail`, 새 테스트), `tests/ASH.Server.Tests/FilmTests.cs` 172행(`?tab=films`)

**Interfaces:**
- Produces (`IFilmRenderer`): `void Thumbnail(string mp4, string outJpg, double atSec, CancellationToken ct = default)` — `AutoViewRenderer`: `ffmpeg -y -ss {atSec} -i "{mp4}" -frames:v 1 -vf scale=320:-2 -q:v 4 "{outJpg}"`, 실패는 `InvalidOperationException`. `FakeFilm`: `File.WriteAllText(outJpg, "jpg")`, `Thumbnails` 목록에 기록.
- `FilmWorker`: 컷 하나가 처음 `Ok`가 된 직후 `films.FilmDir(id)\thumb.jpg`가 없으면 `renderer.Thumbnail(mp4, thumb, 1.0, stop)`; 예외는 `say`로 기록하고 계속(썸네일 없는 영상은 회색 틀).
- 라우트 `/films/{id}/files/{name}`: `thumb.jpg` → `image/jpeg`(없으면 404); `all.zip` → `Finished`가 아니면 409, 있으면 `Ok` 아이템 MP4 전부 + `Joined`(있으면)를 `runs.zip`처럼 `.tmp`에 만들어 `File.Move`(최초 1회, `film.json`보다 오래되면 다시), `application/zip`, 파일명 `{AnalysisName 안전화}-{id}.zip`.
- Produces: `static void AnalysisPages.FilmsTab(StringBuilder sb, AnalysisPageData d)` — 맨 위 `<a class="primary" href="/analyses/{id}/cuts">새 영상 만들기</a> · 컷 리스트 {CutCount}개`; 진행 중 영상 카드(`data-field="film.<id>.phase"`, `data-bar="film.<id>.bar"`, `컷 k / K`, 완료 예상 = `StartedUtc + (Items.Count − Done) × (now − StartedUtc) ÷ max(1, Done)`(Done 0이면 `-`), 취소 폼 `/films/{id}/cancel`); 완료 카드(`요청 시각 · 컷 n × 버전 m · WxH · fps`, 썸네일 `<img src="/films/{id}/files/thumb.jpg" width="320">` 또는 `<div class="thumb-none">`(`.thumb-none{width:320px;height:180px;background:var(--chip)}`), `<video controls preload="none" src=…>`(이어 붙인 영상, 없으면 첫 Ok 컷), 컷별 `<ul>` 링크, `전체 zip 받기(/films/{id}/files/all.zip)`, `자세히(/films/{id})`). 영상 없으면 `"아직 만든 영상이 없습니다."`. 폴링: 진행 중 영상이 있으면 `/api/analyses/{id}/status`(`OfAnalysis`에 `film.<id>.*` 키 추가).

- [ ] **Step 1: 실패하는 테스트** — `FilmTests.cs`에 추가
  ```csharp
  [Fact] public async Task Films_Tab_Shows_Thumbnail_Player_And_All_Zip()
  {
      using var env = new JobEnv(); var j1 = await Done(env, "라인 증설", "A안"); var (st, aid) = Analysis(env);
      env.Factory.Services.GetRequiredService<CutStore>().Save(aid, new[] { Hold("c1"), Hold("c2", "출구") });
      var f = await Film(env, aid, Body(new[] { "c1", "c2" }, new[] { j1 }));
      Assert.Single(env.Film.Thumbnails); Assert.True(File.Exists(Path.Combine(env.Factory.Services.GetRequiredService<FilmStore>().FilmDir(f.Id), "thumb.jpg")));
      var tab = await env.Http.GetStringAsync($"/analyses/{aid}?tab=films");
      foreach (var s in new[] { "새 영상 만들기", "컷 리스트 2개", $"/films/{f.Id}/files/thumb.jpg", "<video", $"/films/{f.Id}/files/all.zip", $"href=\"/films/{f.Id}\"" }) Assert.Contains(s, tab);
      var zip = await env.Http.GetAsync($"/films/{f.Id}/files/all.zip");
      Assert.Equal("application/zip", zip.Content.Headers.ContentType!.MediaType);
      using var z = new ZipArchive(await zip.Content.ReadAsStreamAsync());
      Assert.Equal(3, z.Entries.Count); // 컷 2 + 이어 붙인 1
      Assert.Equal(HttpStatusCode.OK, (await env.Http.GetAsync($"/films/{f.Id}/files/thumb.jpg")).StatusCode);
  }
  [Fact] public async Task Films_Tab_Polls_While_Filming()
  {
      using var env = new JobEnv(); var j1 = await Done(env, "라인 증설", "A안"); var (st, aid) = Analysis(env);
      env.Factory.Services.GetRequiredService<CutStore>().Save(aid, new[] { Hold("c1") }); env.Film.BlockOn = "time 7200";
      var res = await Post(env, aid, Body(new[] { "c1" }, new[] { j1 })); var fid = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
      var fs = env.Factory.Services.GetRequiredService<FilmStore>(); await JobEnv.Until(() => fs.Get(fid) is { State: "running" } x ? x : null);
      var tab = await env.Http.GetStringAsync($"/analyses/{aid}?tab=films");
      Assert.Contains($"data-bar=\"film.{fid}.bar\"", tab); Assert.Contains("fetch('/api/analyses/", tab); Assert.Contains("취소", tab);
      await env.Http.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/films/{fid}/cancel") { Headers = { { "Origin", Ok } } });
  }
  ```
- [ ] **Step 2: 실행** — `dotnet test tests/ASH.Server.Tests --filter "FullyQualifiedName~FilmTests"` → FAIL(컴파일: `Thumbnail` 없음).
- [ ] **Step 3: 구현** — Interfaces대로. `FilmList` 삭제(현황 탭에는 영상 수 카드만).
- [ ] **Step 4: 실행** — `dotnet test ASH.sln` → PASS.
- [ ] **Step 5: 커밋** — `feat(server): 영상 tab with filming progress, finished cards (ffmpeg thumb.jpg, player, per-cut list) and /films/{id}/files/all.zip`.

### Task 12: 마무리 — 작업 페이지 헤더·죽은 코드·README

**Files:**
- Modify: `src/ASH.Server/JobPages.cs` (`Summary`의 "이 페이지는 10초마다 새로 고칩니다" 문구 → "결과가 나오면 위 띠로 알립니다"), `src/ASH.Server/AnalysisPages.cs` (`JobHeader`의 분석 링크를 `?tab=status`로), `src/ASH.Server/README-server.txt` (주소 표: `/`, `/analyses`, `/analyses/{id}?tab=`, `/admin/jobs`, `/api/home`, `/api/analyses/{id}/status`, `/api/films/{id}`, `/films/{id}/files/all.zip`), `docs/superpowers/specs/2026-10-23-ash-web-and-catalog-design.md` 끝 "관문 결과"에 A 한 줄(`A: 관문 없음, 0.21.0`)
- Test: 기존 전체

- [ ] **Step 1: 확인** — `grep -rn "http-equiv\|refresh: true\|ServerPages.Home\|AnalysisPages.Index\|FilmList\|HomeData" src/ASH.Server` → 결과 없음이어야 한다.
- [ ] **Step 2: 수정** — 위 파일들.
- [ ] **Step 3: 실행** — `dotnet test ASH.sln` → PASS.
- [ ] **Step 4: 커밋** — `feat(server): web overhaul wrap-up — wording, job header tab link, README routes`.
