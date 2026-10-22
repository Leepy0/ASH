# 분석·안·버전 + 컷 동영상 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 작업을 분석 → 안 → 버전으로 묶고 보고용 버전을 고르며, 웹 컷 편집기(DXF 바탕)로 만든 컷 리스트를 서버가 AutoView로 촬영해 MP4로 내보낸다.

**Architecture:** 서버(ASP.NET Core minimal API, `src/ASH.Server`)에 분석 저장소·컷 저장소·촬영 큐를 더한다. 계산은 Core(`src/ASH.Core`)의 순수 함수(DXF 단순화, 카메라 수학, 카메라 스크립트, 장면 후보 찾기, 이벤트 로그 파서)로 두고 서버·플러그인이 쓴다. 플러그인은 보내기 창에서 서버의 분석 목록을 고르고, DXF와 좌표 기준 파일을 패키지에 더한다. 모델 로직은 params의 새 줄로 이벤트 로그를 켠다.

**Tech Stack:** C# (.NET 10 서버, netstandard2.0 Core, net48 플러그인 WinForms), xunit + Xunit.SkippableFact, AutoMod/AutoView 14.5(`aview64.exe`), ffmpeg(서버 PATH 또는 설정 경로).

**Spec:** `docs/superpowers/specs/2026-10-23-analysis-and-cut-video-design.md` (AutoView 규칙은 `docs/autoview-guide.md`)

## Global Constraints

- 사내망: 웹 페이지는 외부 자원(CDN, 폰트) 금지. 스크립트·CSS는 페이지 안에.
- UI 문구는 한국어, 표의 수치에는 단위(0.18 규칙: 제목에 "(초)", "(%)", "(대)" 등).
- Core는 netstandard2.0(플러그인 net48과 공유): System.Text.Json 금지, JSON은 DataContractJsonSerializer(Core) / System.Text.Json(서버만).
- 서버 상태 파일은 tmp에 쓰고 `File.Move(tmp, p, true)`로 바꾼다(JobStore 패턴).
- 분석·안 만들기/이름/작업 옮기기/선정은 로그인 없이. 분석 삭제는 비어 있을 때만.
- 기존 0.18 플러그인의 `POST /api/jobs`(분석 필드 없음)는 계속 받는다 → "미분류".
- 영상은 MP4만(H.264, yuv420p, `-movflags +faststart`). AVI 금지.
- AutoView: 좌표 µm, 배속 = 0.5 × timeScale × fps, 컷 1개당 `aview64 -n` 1회, 작업 폴더 분리, `camera.des` 고정 이름, 대화상자 자동 클릭, `aviewerr.dat` 검사, 자식 프로세스 환경에서 `NoDefaultCurrentDirectoryInExePath` 제거.
- 촬영 조건: 대수 기본 = 버전의 결론 대수(`JobInfo.Decided`), 변경 = "결론 −N대" 또는 "모든 버전 같은 대수(그룹별)". 시드 기본 = `input.Settings.SeedBase`, 같은 내보내기의 모든 버전에 같은 시드.
- 자막 수치 = 그 버전 결론 지표(결론 표와 같은 값). 대수를 바꾼 컷: 그 대수 탐색 결과가 summary.tsv에 있으면 그 값, 없으면 촬영 실행 값 + "이 실행 1회 값".
- 장면 후보: 대수 탐색이 끝나면 결론 대수·SeedBase로 이벤트 로그 실행 1회 자동, 다른 대수는 "후보 찾기". PC 결과 업로드(local)는 자동 실행 없음. 로그 없는 모델은 "후보 없음(새 버전으로 다시 보내면 생김)".
- 컷 좌표는 모델 좌표(m, Origin 기준, ArcLayout과 같은 기준)로 저장.

## Review Focus

1. **버전 레이아웃이 다른 분석**: 같은 분석의 버전끼리 레이아웃 범위가 크게 다르면 비교 컷이 어긋난다 → 편집기가 경고(Task 12 테스트: 범위 중심이 레이아웃 크기의 20% 넘게 다르면 경고 문구).
2. **DXF가 없는 버전(0.18 이하 플러그인으로 보낸 작업)**: 편집기·촬영이 경로만으로 동작해야 한다(Task 7, 12 테스트: dxf 없는 작업으로 편집기 200, 촬영 계획 생성 성공).
3. **이름에 한글·공백·특수문자**: 분석·안·컷 이름이 파일 이름과 HTML에 들어간다 → 파일 이름은 안전 문자로 바꾸고 HTML은 `H()`(Task 3, 13 테스트: `"A/B안 <테스트>"`가 파일명 `A_B안__테스트_`, 페이지에 이스케이프되어 보임).
4. **촬영 중 서버 재시작·AutoView 멈춤**: 컷 하나가 시간 제한을 넘으면 그 컷만 실패로 남고 다음 컷 진행, 재시작 시 running 촬영은 failed(Task 13 테스트: 가짜 렌더러가 멈추면 해당 컷 실패, 나머지 성공).
5. **동시 요청**: 두 사람이 같은 안에 동시에 보내면 버전 번호가 겹치지 않아야 한다(Task 2 테스트: 병렬 20회 Assign → 1..20 중복 없음).

---

## File Structure

Core (`src/ASH.Core/`)
- `Analysis/AnalysisModels.cs` — `AnalysisInfo`, `PlanInfo`, 이름 안전화 `SafeFileName`.
- `Dxf/DxfLite.cs` — DXF 텍스트 → `DxfDrawing`(선분·원호·글자), 블록 펼침, 좌표 변환.
- `Dxf/ModelFrame.cs` — `ash.frame.tsv`(Units, Origin, Scale) 읽기/쓰기.
- `Film/CameraMath.cs` — 사각형·기울기·방향·원근 → AutoView 뷰 값.
- `Film/CameraScript.cs` — 컷 → `camera.des` 텍스트, timeScale 계산.
- `Film/CutModels.cs` — `CutSpec`, `CutKind`, `FilmRequest`.
- `Film/SceneFinder.cs` — 이벤트 로그 → 장면 후보.
- `Runner/EventLog.cs` — `events.tsv` 파서.
- 수정: `Model/RunScenario.cs`(Events), `Model/ParamsFile.cs`(EVENTS 줄), `Model/Templates/code.m.txt`·`code_material.m.txt`·`code_leg.m.txt`(이벤트 기록), `Model/LogicGenerator.cs`(V_ev FilePtr), `Model/ModelAmoWriter.cs`(기록 구간), `Runner/JobPackage.cs`(dxf·frame 선택 파일), `Runner/JobServerClient.cs`(분석 API).

Server (`src/ASH.Server/`)
- `AnalysisStore.cs` — `analyses.json` 저장소, 버전 배정.
- `AnalysisPages.cs` — 분석 목록·분석 화면·비교 표·미분류.
- `CutStore.cs` — 분석별 `cuts.json`.
- `CutEditorPage.cs` — 편집기 HTML/JS.
- `FilmStore.cs`, `FilmWorker.cs` — 촬영 요청 큐와 실행(재실행 → aview64 → ffmpeg).
- `IFilmRenderer.cs` — AutoView·ffmpeg 호출을 감싼 인터페이스(테스트에서 가짜로).
- 수정: `JobStore.cs`(JobInfo 필드), `Program.cs`(라우트), `JobPages.cs`(nav "분석", 작업 화면에 분석/버전 표시·영상 목록), `JobWorker.cs`(탐색 뒤 이벤트 실행), `ServerSettings.cs`(Film 설정 키).

Plugin (`src/ASH.Plugin/`)
- `Ui/SendDialog.cs` — 분석·안·메모 고르기.
- 수정: `Commands/ServerCommands.cs`(창, DXF·frame 저장), `Commands/ExportCommands.cs`(필요 시 DXF 저장 도우미).

Tests: `tests/ASH.Tests/{Analysis,Dxf,Film,Runner}/*Tests.cs`, `tests/ASH.Server.Tests/{AnalysisTests,CutTests,FilmTests}.cs`, AutoMod/AutoView 통합은 `tests/ASH.Tests/AutoMod/FilmIntegrationTests.cs`(`ASH_AM_HOME` 없으면 건너뜀).

---

### Task 1: 시험 — 기울인 시점과 DXF 배경 (이 PC)

**Files:**
- Modify: `docs/autoview-guide.md` (새 절 "10. 기울인 시점", "11. 도면 배경")
- Scratch only: `C:\Users\ioi\AppData\Local\Temp\claude\C--Users-ioi\f1b6fa66-4240-52ee-87fb-e5a0bce80e7e\scratchpad\autoview\` (가이드 7장의 도구 `tools\run_aview.ps1` 재사용)

**Interfaces:**
- Produces: 가이드에 (a) 기울기 θ·방향 φ에서 사각형을 담는 `camera`/`focus`/`upvector`/`scale` 공식과 실측 표, (b) 정적 도면 선을 AutoMod 모델에 넣는 방법(파일 형식·위치·한 줄 예시)과 AutoView 영상에 보이는지 여부. Task 8·11이 이 공식과 형식을 그대로 쓴다.

- [ ] **Step 1:** 가이드 4장 평면 공식 모델(agvdemo)에서 θ = 0/30/45/60°, φ = 0/90°로 카메라를 사각형 중심에서 (거리 D, 고도 θ, 방위 φ) 위치로 옮기고 `upvector 0,0,1`(θ>0)로 1프레임 컷을 만든다. 원근/평행 둘 다.
- [ ] **Step 2:** 각 프레임에서 사각형 네 모서리가 화면 안에 들어오는지와 여백을 측정해 scale 보정 계수 k(θ)를 표로 적는다. 평행투영에서 화면 세로 방향 크기가 h·sinθ + 높이성분으로 줄어드는 것을 확인하고 공식으로 쓴다.
- [ ] **Step 3:** AutoMod 도움말(amh_* 폴더)과 데모 `.arc`에서 정적 그래픽(cell/`.cel`/static graphics/DXF import) 형식을 찾는다. 선분 몇 개짜리 정적 그래픽을 데모 모델에 넣고 빌드·기록·AutoView 1프레임으로 영상에 보이는지 확인한다.
- [ ] **Step 4:** 결과를 가이드 10·11장에 [실험] 표시로 적는다. 정적 그래픽이 불가하면 11장에 "불가: 이유, 대안" 을 적고 Task 8은 "편집기 배경만" 경로로 간다.
- [ ] **Step 5:** Commit `docs: AutoView tilted views and drawing background findings`.

### Task 2: 분석 저장소와 버전 배정 (서버)

**Files:**
- Create: `src/ASH.Core/Analysis/AnalysisModels.cs`, `src/ASH.Server/AnalysisStore.cs`
- Modify: `src/ASH.Server/JobStore.cs` (JobInfo 필드), `src/ASH.Server/Program.cs` (DI 등록)
- Test: `tests/ASH.Server.Tests/AnalysisTests.cs`, `tests/ASH.Tests/Analysis/SafeFileNameTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class AnalysisInfo { string Id; string Name; string Note; DateTime CreatedUtc; List<PlanInfo> Plans; }`, `public sealed class PlanInfo { string Id; string Name; string Note; }` (Core, DataContract).
  - `public static string AnalysisNames.SafeFileName(string name)` — 파일명에 못 쓰는 문자·공백 → `_`, 60자 제한.
  - JobInfo 추가: `string? AnalysisId`, `string? PlanId`, `int Version`(0 = 미분류), `string VersionNote = ""`, `bool Selected`.
  - `AnalysisStore` (`<Jobs:Dir>\analyses.json`): `IReadOnlyList<AnalysisInfo> All()`, `AnalysisInfo? Get(string id)`, `AnalysisInfo CreateAnalysis(string name, string note)`, `PlanInfo CreatePlan(string analysisId, string name, string note)`, `void Rename(string analysisId, string? planId, string name)`, `bool DeleteIfEmpty(string analysisId, JobStore jobs)`, `(string AnalysisId, string PlanId, int Version) Assign(JobStore jobs, string jobId, string analysisId, string planId, string note)` — 같은 안의 최대 Version + 1을 잠금 안에서 배정, `void Move(JobStore jobs, string jobId, string analysisId, string planId)`(새 안에서 다음 버전), `void SetSelected(JobStore jobs, string jobId, bool on)`.
  - Id 형식: `a-` + 8 hex, `p-` + 8 hex. 이름 중복은 분석 안에서 안 이름만 금지.

- [ ] **Step 1: 실패하는 테스트**
  - `Assign_Gives_Next_Version_Per_Plan`: 안 A에 3번 → 1,2,3; 안 B → 1.
  - `Assign_Is_Unique_Under_Parallel_Calls`: `Parallel.For(0, 20, …)` → Version 집합 = 1..20.
  - `Survives_Reload`: 저장소를 새로 만들어도 분석·안·이름 유지.
  - `DeleteIfEmpty_Refuses_When_A_Job_Points_At_It`.
  - `Duplicate_Plan_Name_In_Analysis_Throws_ArgumentException`.
  - `SafeFileName("A/B안 <테스트>") == "A_B안__테스트_"`.
- [ ] **Step 2:** `dotnet test tests/ASH.Server.Tests --filter AnalysisTests` → FAIL(타입 없음).
- [ ] **Step 3:** 구현. 잠금은 `lock`, 저장은 JobStore 패턴(tmp → Move). JobStore에 `Update` 재사용.
- [ ] **Step 4:** 테스트 PASS, 기존 서버 테스트 PASS.
- [ ] **Step 5:** Commit `feat(server): analyses, plans and per-plan job versions`.

### Task 3: 분석 API와 제출 확장

**Files:**
- Modify: `src/ASH.Server/Program.cs`, `src/ASH.Core/Runner/JobServerClient.cs`
- Test: `tests/ASH.Server.Tests/AnalysisTests.cs`, `tests/ASH.Tests/Runner/JobServerClientTests.cs`(있으면 거기, 없으면 생성)

**Interfaces:**
- Consumes: Task 2 `AnalysisStore`.
- Produces (서버):
  - `GET /api/analyses` → `[{id,name,note,plans:[{id,name,nextVersion}]}]`.
  - `POST /api/analyses` form `name, note` → `{id}`; `POST /api/analyses/{id}/plans` form `name, note` → `{id}`.
  - `POST /api/jobs` 추가 form 필드(선택): `analysisId`, `planId`, `analysisName`, `planName`, `versionNote`. id가 있으면 그것, 없고 이름이 있으면 이름으로 찾거나 만든다, 둘 다 없으면 미분류. 응답에 `version` 추가.
  - 웹 폼용: `POST /analyses/{id}/rename`, `/analyses/{id}/plans/{pid}/rename`, `/analyses/{id}/delete`, `/jobs/{id}/move`(form `analysisId, planId`), `/jobs/{id}/select`(form `on=1|0`). 브라우저 POST는 `SameOrigin(ctx)` 확인(로그인 불필요).
- Produces (Core 클라이언트): `JobSubmission`에 선택 인자 `string? AnalysisId = null, string? PlanId = null, string? AnalysisName = null, string? PlanName = null, string? VersionNote = null` 추가(기존 위치 인자 유지). `Task<IReadOnlyList<AnalysisSummary>> GetAnalysesAsync(CancellationToken ct)`, `AnalysisSummary{Id, Name, Plans: PlanSummary{Id, Name, NextVersion}}`(DataContract). `JobReceipt`에 `Version`.

- [ ] **Step 1: 실패하는 테스트**
  - `Submit_With_Names_Creates_Analysis_And_Plan_And_Returns_Version_1`, 두 번째 제출 → `version == 2`.
  - `Submit_Without_Analysis_Fields_Is_Unclassified` (0.18 호환).
  - `Api_Analyses_Lists_Next_Version`.
  - `Move_And_Select_Require_Same_Origin`(Origin 다르면 403).
  - 클라이언트: `GetAnalysesAsync` JSON 역직렬화(가짜 HttpMessageHandler), `SubmitAsync`가 새 필드를 multipart에 싣고 값 없는 필드는 보내지 않음.
- [ ] **Step 2:** 실행 → FAIL.
- [ ] **Step 3:** 구현. 이름 길이 100자, 메모 200자 제한(초과 400).
- [ ] **Step 4:** PASS(서버·Core 테스트 전체).
- [ ] **Step 5:** Commit `feat: analysis API and versioned submissions`.

### Task 4: 분석 화면 (웹)

**Files:**
- Create: `src/ASH.Server/AnalysisPages.cs`
- Modify: `src/ASH.Server/JobPages.cs`(nav에 `분석 /analyses`, 작업 화면 상단에 "분석 › 안 › v2 · 메모"와 옮기기 폼), `src/ASH.Server/Program.cs`(`GET /analyses`, `GET /analyses/{id}`, `GET /analyses/unclassified`)
- Test: `tests/ASH.Server.Tests/AnalysisTests.cs`

**Interfaces:**
- Consumes: Task 2·3. 결론 지표는 `JobView`의 `GroupView`(Decided, Util, MinStockRatio, AllOk)와 `MaterialView.StopSec`.
- Produces: 분석 화면 — 안별 버전 표(버전, 보낸 시각(서울), 보낸 사람, 메모, 상태, 권장 총 대수(대), 가동률(%), 결품 시간(초), 선정 체크), "선정 버전 비교" 표(행 = 선정 버전, 열 = 그룹별 권장(대)·가동률(%)·최소재고/안전재고(배)·결품(초)·교착(회)), "컷 영상" 링크(`/analyses/{id}/cuts`), 만든 영상 목록(Task 13 이후 채워짐; 지금은 빈 상태 문구).

- [ ] **Step 1: 실패하는 테스트**: `Analysis_Page_Shows_Plans_Versions_And_Selection`(HTML에 "A안", "v2", 단위 제목 "권장 총 대수(대)"), `Selected_Versions_Appear_In_Compare_Table`, `Unclassified_Lists_Jobs_Without_Analysis`, `Names_Are_Escaped`(`<테스트>` → `&lt;테스트&gt;`).
- [ ] **Step 2:** FAIL 확인.
- [ ] **Step 3:** 구현. `JobPages.Page`/`Css`/`H`/`SortScript` 재사용, 390px에서 가로 스크롤은 표 안에서만.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** Commit `feat(server): analysis pages with version compare`.

### Task 5: 보내기 창 (플러그인)

**Files:**
- Create: `src/ASH.Plugin/Ui/SendDialog.cs`, `src/ASH.Core/Analysis/SendChoice.cs`
- Modify: `src/ASH.Plugin/Commands/ServerCommands.cs`
- Test: `tests/ASH.Tests/Analysis/SendChoiceTests.cs`

**Interfaces:**
- Consumes: Task 3 `GetAnalysesAsync`, `JobSubmission` 새 인자.
- Produces:
  - Core `SendChoice` (`<도면 폴더>\ash.send.tsv`, key TAB value: `AnalysisId, PlanId, AnalysisName, PlanName`): `static SendChoice? Load(string dir)`, `void Save(string dir)`, `static SendChoice Default(IReadOnlyList<AnalysisSummary> list, string folderName, string drawingName, SendChoice? last)` — 지난 선택이 목록에 있으면 그것, 없으면 이름이 폴더/도면 이름과 같은 분석·안, 없으면 새로 만들 이름(폴더/도면 이름).
  - `SendDialog : Form`(Theme 사용): 분석 콤보(+ "새 분석…"), 안 콤보(+ "새 안…"), 다음 버전 표시 "다음: v3", 메모 입력, 확인/취소. 결과 `SendChoice` + `string Note`.
  - ServerCommands.Send: 대기 정보 조회 뒤 Y/N 질문 대신 SendDialog를 띄운다(대기 건수는 창에 표시). AGVRESULTUP도 같은 창.

- [ ] **Step 1: 실패하는 테스트**: `Default_Prefers_Last_Choice_When_Still_On_Server`, `Default_Matches_Folder_And_Drawing_Names`, `Default_Proposes_New_Names_When_Nothing_Matches`, `Save_Load_Round_Trip`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** 구현(창은 테스트 없이, `scripts/PaletteHost`에 `--send` 미리보기 옵션을 더해 그림으로 확인).
- [ ] **Step 4:** PASS, `dotnet build src/ASH.Plugin -c Release` 오류 0.
- [ ] **Step 5:** Commit `feat(plugin): choose analysis and plan when sending`.

### Task 6: DXF와 좌표 기준을 패키지에 (플러그인 → 서버)

**Files:**
- Create: `src/ASH.Core/Dxf/ModelFrame.cs`
- Modify: `src/ASH.Plugin/Commands/ServerCommands.cs`, `src/ASH.Core/Runner/JobPackage.cs`
- Test: `tests/ASH.Tests/Runner/JobPackageTests.cs`, `tests/ASH.Tests/Dxf/ModelFrameTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record ModelFrame(string Units, double OriginX, double OriginY, double Scale)`, `const string FileName = "ash.frame.tsv"`, `static ModelFrame Read(string path)`, `void Write(string path)`. AmHome 등 다른 값은 쓰지 않는다.
  - `JobPackage.Create`: 프로젝트 폴더에 `drawing.dxf`(최대 100 MB)와 `ash.frame.tsv`가 있으면 넣는다(필수 아님). `JobPackage.OptionalFiles = { "drawing.dxf", ModelFrame.FileName }`. 서버 Extract 규칙은 그대로.
  - 플러그인: 보내기 직전에 `db.DxfOut(<도면폴더>\drawing.dxf, 16, DwgVersion.Current)`(ZWCAD API 이름은 확인), 현재 설정으로 `ash.frame.tsv` 저장. DXF 저장 실패는 경고만 하고 계속 보낸다.

- [ ] **Step 1: 실패하는 테스트**: `Frame_Round_Trip`, `Package_Includes_Dxf_And_Frame_When_Present`, `Package_Without_Dxf_Still_Valid`, `Frame_File_Has_No_AmHome`.
- [ ] **Step 2:** FAIL.  **Step 3:** 구현.  **Step 4:** PASS + 플러그인 Release 빌드.
- [ ] **Step 5:** Commit `feat: send drawing.dxf and model frame with the job`.

### Task 7: DXF 단순화 (Core) + 편집기용 도면 API

**Files:**
- Create: `src/ASH.Core/Dxf/DxfLite.cs`
- Modify: `src/ASH.Server/Program.cs` (`GET /jobs/{id}/drawing.json`)
- Test: `tests/ASH.Tests/Dxf/DxfLiteTests.cs` (+ `tests/ASH.Tests/Dxf/Fixtures/*.dxf` 작은 손작성 파일)

**Interfaces:**
- Consumes: Task 6 `ModelFrame`.
- Produces:
  - `public sealed record DxfSeg(double X1, double Y1, double X2, double Y2, string Layer)`, `DxfText(double X, double Y, double Height, string Text, string Layer)`, `DxfDrawing(IReadOnlyList<DxfSeg> Segs, IReadOnlyList<DxfText> Texts, IReadOnlyList<string> Layers)`.
  - `public static DxfDrawing DxfLite.Read(TextReader r)` — ASCII DXF(R12~2018 그룹 코드). LINE, LWPOLYLINE(bulge는 호를 8분할 근사), POLYLINE/VERTEX, CIRCLE·ARC(36분할), TEXT/MTEXT(서식 코드 제거), INSERT(블록 펼침: 위치·축척·회전, 중첩 4단계까지). 그 밖은 무시. 바이너리 DXF는 `InvalidDataException("binary DXF")`.
  - `public static DxfDrawing DxfLite.ToModel(DxfDrawing d, ModelFrame f)` — (p − Origin) × Scale → m (ArcLayout과 같은 기준).
  - `GET /jobs/{id}/drawing.json` → `{segs:[[x1,y1,x2,y2,layerIndex]...], texts:[...], layers:[...], paths:[...], cps:[...], blocks:[...], bounds:[minx,miny,maxx,maxy]}` (paths/cps/blocks는 `ArcLayout.Load`, m 단위). dxf나 frame이 없으면 segs/texts 빈 배열. 결과는 `<job>\drawing.json`에 캐시. 선분 200,000개 초과면 짧은 선분부터 버리고 `truncated:true`.

- [ ] **Step 1: 실패하는 테스트**: `Line_And_Lwpolyline_Read`, `Bulge_Arc_Is_Approximated_Within_1Percent`, `Insert_Expands_Block_With_Scale_And_Rotation`, `Mtext_Formatting_Is_Stripped`, `ToModel_Applies_Origin_And_Scale`(mm 도면, Scale 0.001), `Binary_Dxf_Throws`, 서버 `Drawing_Json_Without_Dxf_Has_Paths_Only`.
- [ ] **Step 2:** FAIL. **Step 3:** 구현. **Step 4:** PASS.
- [ ] **Step 5:** Commit `feat: DXF reader and drawing.json for the cut editor`.

### Task 8: 도면을 모델 고정 그래픽으로 (Task 1 결과에 따름)

**Files:**
- Create: `src/ASH.Core/Film/StaticDrawing.cs`
- Modify: 서버 촬영 준비(Task 13)에서 호출. 모델 생성 경로는 건드리지 않는다(촬영용 복사본에만 넣음).
- Test: `tests/ASH.Tests/Film/StaticDrawingTests.cs`, 통합 `tests/ASH.Tests/AutoMod/FilmIntegrationTests.cs::Drawing_Background_Builds`

**Interfaces:**
- Consumes: Task 7 `DxfDrawing`(모델 m), Task 1 가이드 11장 형식.
- Produces: `public static void StaticDrawing.AddTo(string arcDir, DxfDrawing drawingInModelMetres, string units, IReadOnlySet<string>? hiddenLayers)` — 가이드 11장 형식으로 정적 그래픽 파일을 쓰고 모델 파일에 연결. Task 1이 "불가"면 이 Task는 `AddTo`가 아무것도 안 하고 `bool Supported => false`를 노출, 편집기·내보내기 화면에 "영상에는 도면 배경이 나오지 않습니다(AutoMod 제약)"를 표시.

- [ ] **Step 1:** 실패하는 테스트: 출력 파일에 선분 수만큼 프리미티브, 단위 변환(m → 모델 단위), 숨김 레이어 제외.
- [ ] **Step 2:** FAIL. **Step 3:** 구현. **Step 4:** PASS + 통합(ASH_AM_HOME) 빌드 성공.
- [ ] **Step 5:** Commit `feat: drawing background in film models`.

### Task 9: 이벤트 로그 (모델 로직)

**Files:**
- Create: `src/ASH.Core/Runner/EventLog.cs`
- Modify: `src/ASH.Core/Model/RunScenario.cs`, `src/ASH.Core/Model/ParamsFile.cs`, `src/ASH.Core/Model/LogicGenerator.cs`, `src/ASH.Core/Model/Templates/code.m.txt`, `code_material.m.txt`, `code_leg.m.txt`, `src/ASH.Core/Runner/ModelRunner.cs`(events.tsv도 runs\<id>로 옮김, RunId에 `_ev`)
- Test: `tests/ASH.Tests/Runner/EventLogTests.cs`, `tests/ASH.Tests/Model/LogicGeneratorTests.cs`, 통합 `ModelRunIntegrationTests.Run_With_Events_Writes_Events_Tsv`

**Interfaces:**
- Produces:
  - `RunScenario(int Seed, IReadOnlyDictionary<string,int> ActiveByGroup, bool Debug = false, bool Events = false)`.
  - params.txt: DEBUG 줄 다음 `EVENTS 0|1` 줄(모델은 DEBUG 뒤에 읽음; 이 줄 없는 옛 params는 이 버전 모델과 같이 쓰이지 않음 — InputSignature로 이미 막힘).
  - `events.tsv`(Events=1일 때만, 워밍업 이후 이벤트만): 헤더 `t kind mat cp value`. kind: `call`(호출, value=수량), `pick`(적재 완료, value=호출→적재 초), `deliver`(하역 완료, value=호출→하역 초), `stockout`(결품 시작), `stockin`(결품 끝, value=결품 초), `wait`(구간 끝, cp=도착 CP, value=대기 초, mat=자재 번호). t = 시뮬레이션 초.
  - `public sealed record SimEvent(double T, string Kind, string Material, string Cp, double Value)`, `public static IReadOnlyList<SimEvent> EventLog.Read(string path)` — 잘린 마지막 줄 무시.

- [ ] **Step 1: 실패하는 테스트**: `Params_Has_Events_Line`, `Render_Opens_Events_File_Only_When_Asked`(로직 텍스트에 `V_ev`와 `if V_events = 1`), `EventLog_Read_Parses_And_Skips_Partial_Line`.
- [ ] **Step 2:** FAIL. **Step 3:** 구현(로직: init에서 `V_events`를 읽고 1이면 `open "events.tsv" for writing save result as V_ev`; 각 지점에서 `if V_events = 1 and ac >= V_warmup then print … to V_ev`). **Step 4:** PASS + `ASH_AM_HOME=/c/AutoMod/12_6`로 통합 테스트(events.tsv에 deliver 1행 이상).
- [ ] **Step 5:** Commit `feat(model): optional event log for scene finding`.

### Task 10: 장면 후보 찾기 (Core)

**Files:**
- Create: `src/ASH.Core/Film/SceneFinder.cs`
- Test: `tests/ASH.Tests/Film/SceneFinderTests.cs`

**Interfaces:**
- Consumes: Task 9 `SimEvent`, `ArcLayout.Load(...).Layout`(CP 위치, m).
- Produces:
  - `public enum SceneKind { Stockout, DelaySpike, Bottleneck, Whole }`
  - `public sealed record SceneCandidate(SceneKind Kind, string Title, double StartSec, double EndSec, double CenterX, double CenterY, double Severity, string Detail)` — Title/Detail은 한국어 문구(예: `"결품: M004"`, `"05:20~05:35 재고 0 (900초)"`).
  - `public static IReadOnlyList<SceneCandidate> SceneFinder.Find(IReadOnlyList<SimEvent> events, Layout.Layout layout, IReadOnlyDictionary<string,string> unloadCpByMaterial, double simSec, double warmupSec)`:
    - Stockout: stockout~stockin 구간마다, 위치 = 그 자재의 unload CP, 앞뒤 60초 여유.
    - DelaySpike: deliver.value가 그 자재 평균의 3배 이상이고 상위 1% 안인 호출, 구간 = [호출 시각 − 30, 하역 + 30], 위치 = unload CP. Detail에 "평균 92초 → 410초".
    - Bottleneck: 10분 창을 1분씩 밀며 wait.value 합이 가장 큰 창 1개(그 창의 대기 CP 중 합이 가장 큰 CP 위치).
    - Whole: [warmup, simSec], 위치 = 레이아웃 중심.
    - Severity로 내림차순, 종류별 최대 5개(Whole 1개).

- [ ] **Step 1: 실패하는 테스트**: `Stockout_Window_With_Margin_At_Unload_Cp`, `Delay_Spike_Uses_3x_Mean`, `Bottleneck_Picks_Max_Wait_Window`, `No_Events_Gives_Only_Whole`, `At_Most_Five_Per_Kind`.
- [ ] **Step 2:** FAIL. **Step 3:** 구현. **Step 4:** PASS.
- [ ] **Step 5:** Commit `feat: scene candidates from the event log`.

### Task 11: 카메라 수학과 카메라 스크립트 (Core)

**Files:**
- Create: `src/ASH.Core/Film/CutModels.cs`, `src/ASH.Core/Film/CameraMath.cs`, `src/ASH.Core/Film/CameraScript.cs`
- Test: `tests/ASH.Tests/Film/CameraMathTests.cs`, `tests/ASH.Tests/Film/CameraScriptTests.cs`

**Interfaces:**
- Consumes: 가이드 4장(평면 공식, F0 = DRAWPOS 3D 대각선 근사), 5장(배속), Task 1의 10장(기울기 공식·k(θ)).
- Produces:
  - `public enum CutKind { Hold, Move, Whole }`
  - `public sealed record Rect(double X, double Y, double W, double H)` (m)
  - `public sealed record CutSpec(string Id, string Name, CutKind Kind, double StartSec, double HoldSec, Rect A, Rect? B, double HoldASec, double MoveSec, double HoldBSec, double Speed, double TiltDeg, double DirDeg, bool Perspective, bool Caption, int VehicleDelta = 0)` — Hold/Whole은 HoldSec와 A만, Move는 A,B,HoldASec,MoveSec,HoldBSec. `double SimSeconds`, `double VideoSeconds => SimSeconds / Speed`.
  - `public sealed record View(double FocusX, double FocusY, double FocusZ, double CamX, double CamY, double CamZ, double UpX, double UpY, double UpZ, double Scale)` (µm, AutoView 단위)
  - `public static View CameraMath.Frame(Rect r, double unitsPerMeter, double f0Metres, int width, int height, double tiltDeg, double dirDeg, double margin = 0.08)`
  - `public static double CameraMath.TimeScale(double speed, int fps, double displayStep = 0.5)` = speed / (displayStep × fps)
  - `public static string CameraScript.Build(CutSpec cut, double f0Metres, int width, int height, int fps, int quality, string movieFile)` — `orthogonal`/`perspective`, 시작 시각으로 `jump to time`, Hold: 뷰 고정 + `record movie` HoldSec, Move: A 고정 HoldASec → `pan` MoveSec → B 고정 HoldBSec, 끝에 `generate movie`. 문법은 가이드 3장 그대로.
- [ ] **Step 1: 실패하는 테스트**:
  - `Frame_TopView_Matches_Guide_Formula`: 가이드 4장 예시 값(사각형·화면 1280×720·F0)으로 Scale이 가이드 값과 1% 이내.
  - `Frame_Fits_Wider_Rect_By_Width`: w·H/W > h이면 Scale이 w 기준.
  - `Frame_Tilted_Puts_Camera_South_And_Up`(θ=45, φ=0): CamY < FocusY, CamZ > 0, Up=(0,0,1); Scale은 Task 1 표의 k(45°) 반영.
  - `TimeScale_10x_15fps == 1.3333`(가이드 5장 실측).
  - `Script_Hold_Has_Jump_Record_And_Generate`, `Script_Move_Has_Pan_With_MoveSec`, `Script_Orthogonal_When_Perspective_Off`, `Coordinates_Are_Micrometres`(1 m → 1,000,000).
- [ ] **Step 2:** FAIL. **Step 3:** 구현. **Step 4:** PASS.
- [ ] **Step 5:** Commit `feat: camera math and AutoView camera scripts`.

### Task 12: 컷 저장소와 컷 편집기 (웹)

**Files:**
- Create: `src/ASH.Server/CutStore.cs`, `src/ASH.Server/CutEditorPage.cs`
- Modify: `src/ASH.Server/Program.cs`, `src/ASH.Server/AnalysisPages.cs`(링크)
- Test: `tests/ASH.Server.Tests/CutTests.cs`

**Interfaces:**
- Consumes: Task 7 `drawing.json`, Task 10 후보(`<job>\scenes.json`, Task 13이 만듦; 없으면 빈 배열), Task 11 `CutSpec`.
- Produces:
  - `CutStore` (`<Jobs:Dir>\analyses\<analysisId>\cuts.json`): `IReadOnlyList<CutSpec> Get(string analysisId)`, `void Save(string analysisId, IReadOnlyList<CutSpec> cuts)`(검증: Speed 1~600, Tilt 0~70, Dir 0~359, 시간 ≥ 0, Move는 B 필수, 이름 1~60자).
  - `GET /analyses/{id}/cuts` → 편집기 HTML. 바탕 작업 선택(기본: 선정 버전 중 최신, 없으면 분석의 최신 버전).
  - `GET /api/analyses/{id}/cuts`, `PUT /api/analyses/{id}/cuts`(JSON 배열, SameOrigin) → 400 + 한국어 사유.
  - `GET /api/jobs/{id}/scenes` → 후보 배열, `POST /api/jobs/{id}/scenes?delta=-1` → 다른 대수 후보 찾기 요청(Task 13 큐에 넣음, 202).
  - 편집기 화면은 시안(https://claude.ai/artifact/PWyTLkNxAbByK7WKiXwJgP) 기준, **PC 배치 우선**: 도면 영역 = 폭의 2/3 이상·높이 화면의 70%, 오른쪽 컷 설정, 아래 컷 리스트·장면 후보·내보내기. 확대/이동(버튼, Ctrl+휠, 끌기 with 이동 모드), 휠 = 기울기, 레이어 켬/끔, 사각형 그리기·끌어서 고치기, 버전 범위 차이 경고(Review Focus 1). 스크립트는 페이지 안.
- [ ] **Step 1: 실패하는 테스트**: `Save_And_Get_Round_Trip`, `Invalid_Cut_Returns_400_With_Reason`(Tilt 80 → "기울기는 0~70°"), `Put_Requires_Same_Origin`, `Editor_Renders_Without_Dxf`(Review Focus 2), `Editor_Warns_When_Version_Layouts_Differ`(Review Focus 1).
- [ ] **Step 2:** FAIL. **Step 3:** 구현. 서버를 임시 폴더로 띄워 Playwright로 1920×1080·390px 스크린샷을 찍어 배치 확인(scratchpad).
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** Commit `feat(server): cut list and cut editor`.

### Task 13: 촬영 큐와 실행 (서버)

**Files:**
- Create: `src/ASH.Server/FilmStore.cs`, `src/ASH.Server/FilmWorker.cs`, `src/ASH.Server/IFilmRenderer.cs`(+ `AutoViewRenderer` 구현)
- Modify: `src/ASH.Server/JobWorker.cs`(탐색 뒤 이벤트 실행 → `scenes.json`), `src/ASH.Server/Program.cs`, `src/ASH.Server/ServerSettings.cs`(키 `Film:FfmpegPath`(기본 "ffmpeg"), `Film:AviewExe`(기본 `<AmHome>\bin\aview64.exe`), `Film:CutTimeoutMin`(기본 20)), `src/ASH.Server/AnalysisPages.cs`·`JobPages.cs`(영상 목록·재생), `src/ASH.Server/JobRetention.cs`(영상은 지우지 않음)
- Test: `tests/ASH.Server.Tests/FilmTests.cs`, 통합 `tests/ASH.Tests/AutoMod/FilmIntegrationTests.cs::One_Hold_Cut_Makes_A_Playable_Mp4`

**Interfaces:**
- Consumes: Task 8 `StaticDrawing`, Task 9 Events·EventLog, Task 10 SceneFinder, Task 11 CameraScript, Task 12 CutStore, `ModelRunner.Run(RunRequest)`, `ModelAmoWriter.Render(..., autoView: true)`, `AmBuilder.Build`.
- Produces:
  - `public sealed record FilmRequest(string Id, string AnalysisId, IReadOnlyList<string> CutIds, IReadOnlyList<string> JobIds, int Width, int Height, int Fps, string Output /* "each+joined"|"joined"|"each" */, bool Caption, int? Seed, int VehicleDelta, IReadOnlyDictionary<string,int>? SameCounts)` — 저장 `<Jobs:Dir>\films\<id>\film.json` + 상태(State, Done, Failed, 컷별 결과).
  - `POST /api/analyses/{id}/films`(SameOrigin) → `{id}`; `GET /films/{id}`(진행·결과 페이지, 실행 중 10초 새로고침); `GET /films/{id}/files/{name}`(범위 요청).
  - `interface IFilmRenderer { RenderResult Render(string workDir, string modelName, string cameraScript, TimeSpan timeout, CancellationToken ct); string Finish(IReadOnlyList<string> mp4s, IReadOnlyList<string> captions, string outPath, bool join); }` — 테스트는 가짜.
  - 실행 순서(버전마다): 프로젝트 폴더를 `films\<id>\<jobId>\` 로 복사(원본 작업 폴더를 건드리지 않음) → 대수(결론 / −N / 같은 대수) 계산 → `StaticDrawing.AddTo`(지원 시) → model.amo를 `autoView`로 다시 쓰되 기록 구간 = 이 버전 컷들의 [min start, max end] → 빌드 → `ModelRunner.Run`(Seed) → 컷마다 `camera.des` 쓰고 `Render` → `aviewerr.dat` 비었는지 → ffmpeg 후처리(yuv420p, faststart, 자막 `drawtext`: "분석 · 안 v버전 · 컷 이름 · hh:mm:ss · 권장 7대 · 가동률 68% · 결품 0초"). 파일 이름 `SafeFileName("{안}_v{버전}_{컷이름}.mp4")`, 이어 붙인 것 `SafeFileName("{분석}_{날짜}.mp4")`.
  - 촬영 큐는 JobWorker와 같은 스레드에서 작업 사이에 돈다(동시 AutoMod 실행 금지). 컷 시간 제한 초과 → 그 컷 실패, 다음 진행(Review Focus 4). 서버 재시작 시 running 촬영 → failed.
  - 탐색 끝(JobWorker): Source가 server이고 Decided가 있으면 결론 대수·SeedBase·Events=true로 1회 실행 → `SceneFinder.Find` → `<job>\scenes.json`. 실패해도 작업은 성공 유지(경고만). 이벤트 없는 모델(옛 로직)이면 scenes.json = `{none:"새 버전으로 다시 보내면 생깁니다"}`.
- [ ] **Step 1: 실패하는 테스트**(가짜 렌더러·가짜 러너):
  - `Film_Runs_Each_Cut_For_Each_Version_And_Joins`
  - `Hung_Cut_Fails_Alone`(Review Focus 4)
  - `Vehicle_Delta_Lowers_Decided_Counts_Not_Below_Min`
  - `Same_Seed_For_All_Versions`
  - `Caption_Uses_Job_Conclusion_Numbers`
  - `File_Names_Are_Safe`(Review Focus 3)
  - `Scenes_Json_Written_After_Search`, `Local_Result_Gets_No_Auto_Scene_Run`
  - 통합(`ASH_AM_HOME` + ffmpeg): 구역 고정 컷 1개로 mp4 생성, ffprobe 결과 yuv420p·길이 = 영상 초 ± 10%.
- [ ] **Step 2:** FAIL. **Step 3:** 구현(`AutoViewRenderer`는 가이드 6.3: `aview64 -n <model>`을 `.avw` 폴더 부모에서, 환경에서 `NoDefaultCurrentDirectoryInExePath` 제거, DialogCloser로 "Attention!"·영상 확인 창 OK, ffmpeg PATH 확인). **Step 4:** PASS(단위 + 통합).
- [ ] **Step 5:** Commit `feat(server): film queue rendering cuts with AutoView`.

### Task 14: 문서·수동 시험·릴리스

**Files:**
- Modify: `docs/server-install.md`(ffmpeg 설치, Film 설정, 서비스 세션 렌더 확인), `src/ASH.Server/README-server.txt`, `docs/plugin-manual-test.md`(보내기 창, DXF), `docs/autoview-guide.md`(구현이 쓰는 규칙 연결), `Directory.Build.props`(0.19.0)
- Test: 전체

- [ ] **Step 1:** `dotnet test ASH.sln` 전부 PASS, `ASH_AM_HOME=/c/AutoMod/12_6`로 AutoMod·Film 통합 PASS(이 PC에 다른 AutoMod 창이 없는 상태에서).
- [ ] **Step 2:** 수동 시험 행 추가: 서버에서 서비스 세션 렌더(로그온 없이), ffmpeg 경로, 컷 편집기 PC·휴대폰 배치, 내보낸 영상 휴대폰·PPT 재생.
- [ ] **Step 3:** 버전 0.19.0, commit `release 0.19.0`, `git push origin feat/core-and-layout` 그리고 `git push origin HEAD:main`, `gh run watch`로 CI 성공 확인, 릴리스 자산 7개 확인.
