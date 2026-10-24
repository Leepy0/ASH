# 자료 보관함 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 서버에 올라온 작업의 `input.xlsx`·DXF·DWG를 웹 `/files`에서 검색해 파일별 또는 "이어서 작업 묶음"(`continue.zip`)으로 내려받는다. 플러그인 `AGVSEND`가 DWG를 함께 보내고, 분석에 법인·제품군·라인 태그를 넣어 검색·필터에 쓴다.

**Architecture:** 데이터는 이미 작업 폴더에 있다(`upload.zip` → `project\`). 서버는 새 저장소 없이 `project\input.cells.tsv`에서 자재 코드만 `index.json`으로 색인하고, 나머지(분석·안·버전·보낸 사람·모델·날짜)는 `JobStore`·`AnalysisStore`를 그대로 읽는다. `input.xlsx`는 패키지에 없으므로(회사 문서 암호화) `CellDump.Load`로 되살려 캐시한다. 라우트는 `Routes.Files.cs`의 `MapFiles()` 하나에 모은다(E장: Program.cs·nav는 A 소유). 플러그인은 디스크의 `.dwg`를 패키지에 더하고 `SendDialog`에 태그 3칸을 둔다.

**Tech Stack:** C# — 서버 `src/ASH.Server`(.NET 10 minimal API, 서버 렌더 HTML), Core `src/ASH.Core`(netstandard2.0, ClosedXML), 플러그인 `src/ASH.Plugin`(net48, WinForms, ZWCAD). 테스트 xunit: `tests/ASH.Server.Tests`(`JobEnv` = `WebApplicationFactory<Program>` + `FakeRunner`), `tests/ASH.Tests`.

**Spec:** `C:\Users\ioi\Documents\ASH\docs\superpowers\specs\2026-10-23-ash-web-and-catalog-design.md` 0·D·E·G·H장.

## Global Constraints

- A가 먼저 nav·CSS·`JobPages.Page` 서명을 확정한다. D는 `Program.cs`에 **한 줄** `app.MapFiles();`(A가 넣음)과 Task 3의 폼 필드 3개 읽기만 더한다. 그 외 라우트·페이지는 전부 `src/ASH.Server/Routes.Files.cs`.
- A의 nav에 "보관함" 링크는 A가 넣는다. D는 `/files` 페이지만 만든다. 현황 탭의 "자료 받기"는 `FileRoutes.ContinueLink(JobInfo)`를 A가 호출한다.
- `JobInfo` 필드는 A만 더한다(E장). D는 `JobInfo`를 건드리지 않는다. 색인은 작업 폴더의 `index.json`이다.
- 업로드 상한 `Program.cs` `UploadLimit`는 이미 `512 MB`(DXF 100 + DWG 100 + 모델)이므로 올리지 않는다. Task 1에서 `JobPackage.MaxDwgBytes + MaxDxfBytes < UploadLimit`를 주석으로만 못 박는다.
- 내려받기는 로그인 없음(H: 누구나). 파일은 `JobStore.ProjectDir(id)`·`JobDir(id)` 안의 정해진 이름만 연다. 사용자 입력이 경로가 되는 일은 없다.
- 기존 `/jobs/{id}/files/{name}`(Program.cs)은 그대로 둔다. 새 이름은 리터럴 라우트(`/jobs/{id}/files/drawing.dwg` 등)로 더한다 — ASP.NET은 리터럴을 매개변수보다 먼저 맞춘다.
- `AnalysisSummary`·`JobSubmission`·`GET /api/analyses` 모양은 0.19 플러그인이 읽으므로 필드를 **더하기만** 한다.
- 한국어 문구, 식별자는 영어. 커밋은 작업마다 하나, 메시지 끝에 빈 줄 + `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- 테스트 실행: `dotnet test tests/ASH.Server.Tests --filter "FullyQualifiedName~FileRoutes"` 처럼 클래스로 좁힌다. 플러그인은 ZWCAD 없이는 못 돌리므로 `docs/plugin-manual-test.md`에 행을 더한다.

## Review Focus

1. **라우트 충돌**(Task 5): `/jobs/{id}/files/drawing.dwg`가 기존 `{name}` 라우트와 공존해야 한다. 테스트가 새 이름 4개는 200, 기존 `summary.tsv`·`upload.zip`은 전과 같은 응답임을 함께 확인한다.
2. **되살린 input.xlsx의 손실**(Task 5): `CellDump.Write`는 수식 셀을 버리므로 되살린 파일에는 `CallThresholdTotal` 같은 자동 열이 비어 있다. `ExcelTemplate.Update(wb, null)`로 양식(목록 검증·자동 열)을 다시 입힌 뒤 저장해야 플러그인이 그대로 읽는다. 테스트는 되살린 파일을 `ExcelReader`로 읽어 자재 수가 같음을 확인한다.
3. **색인과 실제의 어긋남**(Task 4·6): `index.json`에는 자재 코드만 둔다. 분석 이름·안·메모·보낸 사람은 매 요청에 `JobStore`·`AnalysisStore`에서 읽는다(이름 바꾸기·옮기기 뒤에도 맞음). 관리자 삭제는 폴더째 지우므로 색인도 같이 간다. 여러 요청이 동시에 같은 작업의 색인을 만들면 `.tmp` → `File.Move(overwrite)`로 마지막 것이 남는다(같은 내용).
4. **DWG 거절·무제 도면 경로**(Task 7): `DBMOD != 0`에서 "아니요"를 고르면 **DWG 없이 보내기는 계속**되어야 한다. `DWGTITLED == 0`(저장한 적 없는 도면)과 100 MB 초과도 같은 "모델만 보냅니다" 경로. 메시지가 명령줄에 남는지 수동 테스트 DW2~DW4.
5. **태그 덮어쓰기**(Task 3): `POST /api/jobs`의 `corp/productLine/line`은 **분석을 새로 만들 때만** 쓴다. 기존 분석(id 또는 이름 일치)으로 보내면 저장된 태그가 바뀌지 않아야 한다. 테스트가 두 경우를 모두 본다.

---

### Task 1: JobPackage — DWG와 ash.settings.tsv를 선택 파일로

**Files:**
- Modify: `src/ASH.Core/Runner/JobPackage.cs`
- Test: `tests/ASH.Tests/Runner/JobPackageTests.cs`

**Interfaces:**
```csharp
public static class JobPackage
{
    public const long MaxDwgBytes = 100L * 1024 * 1024;              // spec G: DWG 동봉 상한
    public const string DwgInPackage = "drawing.dwg", SettingsInPackage = "ash.settings.tsv";
    // OptionalFiles = { DxfInPackage, ModelFrame.FileName, DwgInPackage, SettingsInPackage }
    public static IReadOnlyList<PackageFile> Create(string projectDir, string model, string zipPath, bool includeResults = false, string? dwgPath = null);
}
```
`dwgPath`: 디스크의 열린 도면 파일. null이거나 없거나 `MaxDwgBytes` 초과면 넣지 않는다(복사 없이 그 자리에서 zip에 쓴다). `ash.settings.tsv`는 있으면 넣는다 — 서버는 쓰지 않고 `continue.zip`에만 되돌려 준다(기존 "stays at home" 주석을 이 뜻으로 고친다).

- [ ] **Step 1: 실패하는 테스트**
  ```csharp
  [Fact] public void Package_Includes_Dwg_And_Settings_When_Given()
  {
      var proj = Project(dxf: false, frame: false);
      File.WriteAllText(Path.Combine(proj, "ash.settings.tsv"), "ModelName\tm");
      var dwg = Path.Combine(proj, "sub", "플랜트.dwg"); Directory.CreateDirectory(Path.GetDirectoryName(dwg)!); File.WriteAllBytes(dwg, new byte[16]);
      var zip = Path.Combine(proj, "p.zip");
      var files = JobPackage.Create(proj, "m", zip, dwgPath: dwg);
      using var z = ZipFile.OpenRead(zip);
      Assert.Contains(z.Entries, e => e.FullName == "drawing.dwg");
      Assert.Contains(z.Entries, e => e.FullName == "ash.settings.tsv");
      Assert.Contains(files, f => f.Path == "drawing.dwg" && f.Size == 16);
      Assert.Contains("drawing.dwg", JobPackage.OptionalFiles); Assert.Contains("ash.settings.tsv", JobPackage.OptionalFiles);
  }
  [Fact] public void Package_Skips_Missing_Or_Oversized_Dwg()  // dwgPath 없음·null → 항목 없음, 예외 없음 (크기 초과는 FileInfo.Length 검사를 Create 안에서; 테스트는 "없는 경로"만)
  ```
  기존 `Create_Leaves_Out_The_Asy_Backups_And_The_Local_Settings`의 `DoesNotContain(... "ash.settings.tsv")`를 `Contains`로 바꾸고 이름을 `..._And_Keeps_The_Settings`로.
- [ ] **Step 2: 실행 → FAIL** `dotnet test tests/ASH.Tests --filter "FullyQualifiedName~JobPackageTests"`
- [ ] **Step 3: 구현** 상수·`OptionalFiles`·`Create` 서명. `files.Add((SettingsInPackage, ...))`는 `File.Exists`일 때만, `dwgPath`는 `File.Exists && Length <= MaxDwgBytes`일 때 `(DwgInPackage, dwgPath)`. 상수 옆 주석 한 줄: `// MaxDxfBytes + MaxDwgBytes < ASH.Server UploadLimit(512 MB)`.
- [ ] **Step 4: 실행 → PASS**
- [ ] **Step 5: 커밋** `feat(core): package carries the open DWG and ash.settings.tsv as optional files`

---

### Task 2: Core — 분석 태그, 안 이름 제안, 클라이언트 필드

**Files:**
- Modify: `src/ASH.Core/Analysis/AnalysisModels.cs`, `src/ASH.Core/Runner/JobServerClient.cs`
- Test: `tests/ASH.Tests/Analysis/AnalysisNamesTests.cs`(새 파일), `tests/ASH.Tests/Runner/JobServerClientTests.cs`

**Interfaces:**
```csharp
public sealed class AnalysisInfo { [DataMember] public string Corp { get; set; } = ""; [DataMember] public string ProductLine { get; set; } = ""; [DataMember] public string Line { get; set; } = ""; }
public static class AnalysisNames
{
    /// <summary>"A안"… 중 아직 없는 다음 글자; Z까지 다 쓰면 "새 안".</summary>
    public static string NextPlanName(IEnumerable<string> existing);
    public const string NameRuleHelp = "이름 규칙(권장): <법인/동> <대상> <검토 목적> — 예 \"P동 1층 AGV 대수 검토\"";
    public const string PlanNotePlaceholder = "이 안이 다른 안과 무엇이 다른지";
}
public sealed class AnalysisSummary { [DataMember(Name="corp")] public string Corp {get;set;} = ""; [DataMember(Name="productLine")] public string ProductLine {get;set;} = ""; [DataMember(Name="line")] public string Line {get;set;} = ""; }
public sealed record JobSubmission(string Project, string Plan, string Model, string Submitter, string Impact = "1",
    string? AnalysisId = null, string? PlanId = null, string? AnalysisName = null, string? PlanName = null, string? VersionNote = null,
    string? Corp = null, string? ProductLine = null, string? Line = null);
```
`SubmitAsync`는 `Field("corp", job.Corp)` 등 3개를 더한다(빈 값은 보내지 않음 — 기존 `Field` 규칙).

- [ ] **Step 1: 실패하는 테스트**
  ```csharp
  [Theory]
  [InlineData(new string[0], "A안")] [InlineData(new[] { "A안" }, "B안")] [InlineData(new[] { "A안", "C안", "기타" }, "B안")]
  public void NextPlanName_Takes_The_First_Free_Letter(string[] existing, string want) => Assert.Equal(want, AnalysisNames.NextPlanName(existing));
  [Fact] public void NextPlanName_After_Z_Is_Generic() => Assert.Equal("새 안", AnalysisNames.NextPlanName(Enumerable.Range(0, 26).Select(i => (char)('A' + i) + "안")));
  // JobServerClientTests: ParseAnalyses가 corp 없는 0.19 JSON도 읽고(""), 있으면 채움; SubmitAsync가 corp 폼 파트를 보낸다(기존 핸들러 캡처 패턴 재사용)
  ```
- [ ] **Step 2: 실행 → FAIL** `dotnet test tests/ASH.Tests --filter "FullyQualifiedName~AnalysisNames|FullyQualifiedName~JobServerClient"`
- [ ] **Step 3: 구현** 위 서명대로. `NextPlanName`: `existing` 중 정규식 `^([A-Z])안$`에 맞는 글자를 모아 A부터 첫 빈 글자.
- [ ] **Step 4: 실행 → PASS**
- [ ] **Step 5: 커밋** `feat(core): analysis tags corp/productLine/line, next plan name, submission fields`

---

### Task 3: 서버 — 태그 저장·API·폼 필드

**Files:**
- Modify: `src/ASH.Server/AnalysisStore.cs`, `src/ASH.Server/Program.cs`(`/api/analyses` GET·POST, `/analyses` POST, `/api/jobs` — 각 2~3줄; A의 첫 커밋 뒤에 리베이스)
- Test: `tests/ASH.Server.Tests/AnalysisTests.cs`, `tests/ASH.Server.Tests/JobTests.cs`

**Interfaces:**
```csharp
public sealed class AnalysisStore
{
    public AnalysisInfo CreateAnalysis(string name, string note, string corp = "", string productLine = "", string line = "");
    public void SetTags(string analysisId, string corp, string productLine, string line);   // 웹 "이름 바꾸기" 폼 옆에서 쓴다(Task 6)
    /// <summary>자동완성용: 각 태그의 서로 다른 값(대소문자 무시), 가나다 순.</summary>
    public (IReadOnlyList<string> Corps, IReadOnlyList<string> ProductLines, IReadOnlyList<string> Lines) TagValues();
}
```
태그 길이 상한은 `NameMax`(100)와 같고, 폼 키는 `corp`, `productLine`, `line`. `GET /api/analyses` 항목에 `Corp, ProductLine, Line` 추가. `POST /api/jobs`는 `analyses.CreateAnalysis(analysisName, "", corp, productLine, line)` — 이름으로 **새로 만들 때만**.

- [ ] **Step 1: 실패하는 테스트**
  ```csharp
  [Fact] public void Tags_Are_Saved_And_Listed()
  {
      var st = new AnalysisStore(_dir);
      var a = st.CreateAnalysis("P동 검토", "", "P법인", "냉장고", "1라인");
      st.CreateAnalysis("Q동 검토", "", "p법인", "", "2라인");
      var again = new AnalysisStore(_dir);
      Assert.Equal(("P법인", "냉장고", "1라인"), (again.Get(a.Id)!.Corp, again.Get(a.Id)!.ProductLine, again.Get(a.Id)!.Line));
      Assert.Equal(new[] { "P법인" }, again.TagValues().Corps);           // 대소문자만 다른 값은 하나
      Assert.Equal(new[] { "1라인", "2라인" }, again.TagValues().Lines);
  }
  // JobTests: POST /api/analyses with corp → GET /api/analyses JSON에 "corp":"P법인".
  // POST /api/jobs analysisName=새이름&corp=X → 분석 Corp == "X"; 같은 이름으로 다시 corp=Y → 여전히 "X".
  ```
- [ ] **Step 2: 실행 → FAIL** `dotnet test tests/ASH.Server.Tests --filter "FullyQualifiedName~AnalysisTests|FullyQualifiedName~Tags"`
- [ ] **Step 3: 구현** `NameNote`를 건드리지 않고 핸들러에서 `f["corp"]` 등을 `Trim()`·길이 검사해 넘긴다(읽는 코드는 지역 함수 `Tags(IFormCollection)`로 한 번만).
- [ ] **Step 4: 실행 → PASS**
- [ ] **Step 5: 커밋** `feat(server): analyses carry corp/productLine/line tags; POST /api/analyses and /api/jobs accept them`

---

### Task 4: 서버 — 자재 코드 색인 `FileIndex`

**Files:**
- Create: `src/ASH.Server/FileIndex.cs`
- Modify: `src/ASH.Server/JobWorker.cs`(끝날 때 `store.Update` 뒤 `FileIndex.Ensure(store, job.Id)` 한 줄; Local 결과 작업은 `/files`의 `Ensure`가 처음 볼 때 만든다)
- Test: `tests/ASH.Server.Tests/FileIndexTests.cs`(새 파일)

**Interfaces:**
```csharp
/// <summary>작업 폴더의 index.json: {"materials":["M1",…]}. input.cells.tsv의 Materials 시트 Material 열(1행 머리글)에서 읽는다.</summary>
public static class FileIndex
{
    public const string FileName = "index.json";
    public static IReadOnlyList<string> MaterialsFromCells(string cellsPath);          // 파일 없음·형식 오류 → 빈 목록
    public static IReadOnlyList<string> Ensure(JobStore js, string id);               // index.json 있으면 읽고, 없으면 만들어 쓰고 돌려준다
    public static void BuildMissing(JobStore js, ILogger log);                        // 서버 시작: index.json 없는 작업 전부(끝난 것만)
}
```
`MapFiles`가 `BuildMissing`을 `Task.Run`으로 띄운다(큰 서버에서 시작을 막지 않게). `/files` 검색은 `Ensure`를 호출하므로 백필 전에도 맞는 결과가 나온다.

- [ ] **Step 1: 실패하는 테스트**
  ```csharp
  [Fact] public void Materials_Come_From_The_Materials_Sheet_Only()
  {
      var p = Path.Combine(_dir, "input.cells.tsv");
      File.WriteAllText(p, "#ASH-CELLS 1\nMaterials\t1\t1\tS\tMaterial\nMaterials\t1\t2\tS\tStation\nMaterials\t2\t1\tS\tM-100\nMaterials\t3\t1\tS\tM-200\nRoutes\t2\t1\tS\tM-999\n");
      Assert.Equal(new[] { "M-100", "M-200" }, FileIndex.MaterialsFromCells(p));
      Assert.Empty(FileIndex.MaterialsFromCells(Path.Combine(_dir, "none.tsv")));
  }
  [Fact] public void Ensure_Writes_Index_Once_And_BuildMissing_Fills_Old_Jobs()
  {   // JobStore에 작업 둘(Done), 한쪽은 project\input.cells.tsv만 둠 → BuildMissing 뒤 index.json 존재; Ensure가 그 파일을 읽어 같은 목록. 다른 쪽(파일 없음)은 materials:[].
  }
  ```
- [ ] **Step 2: 실행 → FAIL**
- [ ] **Step 3: 구현** tsv는 `CellDump` 형식(탭 5열; `\t`·`\\` 이스케이프는 자재 코드에 없다고 보고 `Replace("\\\\","\\")`만) 직접 줄 단위로 읽는다 — `CellDump.Load`(ClosedXML 워크북)보다 가볍다. JSON은 `System.Text.Json`, `.tmp` → `File.Move(..., true)`.
- [ ] **Step 4: 실행 → PASS**
- [ ] **Step 5: 커밋** `feat(server): material code index per job (index.json), built at finish and backfilled at start`

---

### Task 5: 서버 — 파일 내려받기와 `continue.zip`

**Files:**
- Create: `src/ASH.Server/Routes.Files.cs`
- Test: `tests/ASH.Server.Tests/FileRoutesTests.cs`(새 파일, `JobEnv.MakePackage(addFiles: …)`로 `ash.drawing.dxf`·`drawing.dwg`·`ash.settings.tsv`를 넣는다. `MakePackage`가 DWG를 못 넣으면 `addFiles`에서 `JobPackage.Create(..., dwgPath:)`를 직접 호출)

**Interfaces:**
```csharp
public static class FileRoutes
{
    public static void MapFiles(this WebApplication app);          // /files, /jobs/{id}/files/{input.xlsx|drawing.dxf|drawing.dwg|continue.zip}, 시작 시 FileIndex.BuildMissing
    /// <summary>현황 탭 버전 줄용 "자료 받기" 앵커(A가 호출). 패키지에 셀 스냅샷이 없는 작업(없음)에는 빈 문자열.</summary>
    public static string ContinueLink(JobInfo j);
    internal static string ReadmeText(AnalysisInfo? a, PlanInfo? p, JobInfo j, bool hasDwg, bool hasSettings);
    internal static string RebuiltXlsx(JobStore js, JobInfo j);     // <job>\input.xlsx 캐시: 없거나 project\input.cells.tsv보다 오래되면 CellDump.Load → ExcelTemplate.Update(wb,null) → SaveAs
}
```
- `drawing.dxf`·`drawing.dwg`: `ProjectDir\drawing.dxf|drawing.dwg` 그대로(`Results.File`, `enableRangeProcessing: true`, 내려받는 이름 `{id}-drawing.dwg`). 없으면 404 "이 작업에는 DWG가 없습니다(0.19 이전 플러그인)".
- `continue.zip`: 메모리 아닌 `<job>\continue.zip.tmp-<guid>`에 만들고 `Results.File(FileStream, FileOptions.DeleteOnClose)`. 내용 `input.xlsx`(되살린 것) + `layout.dwg`(DWG 있으면) 또는 `drawing.dxf` + `ash.settings.tsv`(있으면) + `README.txt`.
- README 본문(spec D.2, UTF-8 BOM):
  ```
  이 자료는 분석 {분석 이름} › 안 {안 이름} v{N}.
  ZWCAD에서 열고 AGVINIT 뒤 AGVSEND 때 같은 분석을 고르면 다음 버전, 새 안을 고르면 새 안.
  ```
  뒤에 "보낸 사람 · 보낸 시각(서울) · 모델"과 빠진 파일 안내("DWG 없음: DXF를 넣었습니다", "ash.settings.tsv 없음: AGVINIT가 새로 만듭니다"). 미분류 작업은 첫 줄을 "이 자료는 분석에 넣지 않은 작업 {id}."로.

- [ ] **Step 1: 실패하는 테스트**
  ```csharp
  [Fact] public async Task Downloads_Each_File_And_Keeps_The_Old_Names()
  {   // POST /api/jobs with dxf+dwg+settings → 끝날 때까지 대기
      Assert.Equal(HttpStatusCode.OK, (await env.Http.GetAsync($"/jobs/{id}/files/drawing.dwg")).StatusCode);
      Assert.Equal("application/zip", (await env.Http.GetAsync($"/jobs/{id}/files/continue.zip")).Content.Headers.ContentType!.MediaType);
      Assert.Equal(HttpStatusCode.OK, (await env.Http.GetAsync($"/jobs/{id}/files/summary.tsv")).StatusCode);   // 기존 라우트 그대로
      Assert.Equal(HttpStatusCode.NotFound, (await env.Http.GetAsync($"/jobs/{id}/files/x.dwg")).StatusCode);
  }
  [Fact] public async Task ContinueZip_Has_The_Bundle_And_Korean_Readme()
  {   // ZipArchive로 읽어 Entries == { "input.xlsx", "layout.dwg", "ash.settings.tsv", "README.txt" }(DWG 없는 작업은 "drawing.dxf")
      Assert.StartsWith("이 자료는 분석 ", readme); Assert.Contains("같은 분석을 고르면 다음 버전, 새 안을 고르면 새 안", readme);
      // input.xlsx를 ExcelReader.Read로 읽어 Materials.Count가 패키지의 것과 같다
  }
  [Fact] public void ContinueLink_Points_At_The_Zip() => Assert.Contains("/jobs/j1/files/continue.zip", FileRoutes.ContinueLink(new JobInfo { Id = "j1", Files = { new("input.cells.tsv", 1, "") } }));
  ```
- [ ] **Step 2: 실행 → FAIL** `dotnet test tests/ASH.Server.Tests --filter "FullyQualifiedName~FileRoutes"`
- [ ] **Step 3: 구현** 위 서명. `Msg`·`H`는 Program.cs의 지역 함수라 못 쓰므로 `Results.Text(..., statusCode)`와 `JobPages.H`를 쓴다. 되살린 xlsx 저장 전 `FontFreeGraphicEngine.Install()`은 `CellDump.Load`가 이미 한다.
- [ ] **Step 4: 실행 → PASS**
- [ ] **Step 5: 커밋** `feat(server): per-file downloads, rebuilt input.xlsx and continue.zip with README`

---

### Task 6: 서버 — `/files` 검색 페이지와 태그 필터

**Files:**
- Modify: `src/ASH.Server/Routes.Files.cs`
- Test: `tests/ASH.Server.Tests/FileRoutesTests.cs`

**Interfaces:**
```csharp
public static class FileRoutes
{
    /// <summary>쿼리: q(분석·안·버전 메모·보낸 사람·모델 부분 일치) material(자재 코드 부분 일치) from/to(yyyy-MM-dd, 서울) corp productLine line(정확히 일치, 대소문자 무시).</summary>
    public sealed record FileQuery(string Q = "", string Material = "", DateTime? FromUtc = null, DateTime? ToUtc = null, string Corp = "", string ProductLine = "", string Line = "")
    { public static FileQuery From(IQueryCollection q); }
    public static IReadOnlyList<JobInfo> Search(FileQuery f, IReadOnlyList<JobInfo> jobs, IReadOnlyList<AnalysisInfo> analyses, Func<string, IReadOnlyList<string>> materialsOf);
    /// <summary>태그 3개 셀렉트 + datalist(기존 값) — /files와 A의 /analyses 폼이 같이 쓴다.</summary>
    public static string TagFilterHtml(FileQuery f, AnalysisStore st);
    public static bool TagMatch(AnalysisInfo a, FileQuery f);          // A가 /analyses 목록 필터에 호출
}
```
페이지: `JobPages.Page("자료 보관함", …)`. 폼 한 줄(`q`·`material`·`from`·`to` + `TagFilterHtml`), 결과 표 열: `분석 › 안 vN`(링크 `/jobs/{id}`) · 날짜 · 보낸 사람 · 모델 · 파일(`xlsx`·`dxf`·`dwg` 각각 있을 때만 링크, 없으면 회색 글자) · `이어서 작업(zip)`. 끝난 작업만, 최신순, 최대 200건("더 좁혀 보세요"). 미분류 작업은 `(미분류) 프로젝트 / 안`. 자재 코드 일치 작업은 그 코드를 작게 표시.

- [ ] **Step 1: 실패하는 테스트**
  ```csharp
  [Fact] public void Search_Matches_Name_Material_Date_And_Tags()
  {   // 분석 A(corp "P") 안 X v1 job j1(materials M-100, 2026-10-01), 분석 B(corp "Q") job j2(M-200, 2026-10-20), 미분류 j3
      Assert.Equal(new[] { "j1" }, Ids(Search(new FileQuery(Q: "X"), …)));
      Assert.Equal(new[] { "j1" }, Ids(Search(new FileQuery(Material: "100"), …)));
      Assert.Equal(new[] { "j2" }, Ids(Search(new FileQuery(FromUtc: 10월 10일 UTC), …)));
      Assert.Equal(new[] { "j2" }, Ids(Search(new FileQuery(Corp: "q"), …)));
      Assert.Equal(3, Search(new FileQuery(), …).Count);
  }
  [Fact] public async Task Files_Page_Lists_Jobs_With_Download_Links()   // GET /files?q=… 200, 본문에 "/jobs/{id}/files/continue.zip"과 "drawing.dwg"
  ```
- [ ] **Step 2: 실행 → FAIL**
- [ ] **Step 3: 구현** `Search`는 순수 함수(저장소 없이 테스트). 날짜는 `SeoulTime` 기준 하루 경계(`to`는 그날 끝까지). 미분류는 `Project/Plan`을 이름 대신 검색.
- [ ] **Step 4: 실행 → PASS**
- [ ] **Step 5: 커밋** `feat(server): /files archive search with material codes, date range and analysis tags`

---

### Task 7: 플러그인 — AGVSEND가 DWG를 보내고 SendDialog에 태그·안 이름 제안

**Files:**
- Modify: `src/ASH.Plugin/Commands/ServerCommands.cs`, `src/ASH.Plugin/Ui/SendDialog.cs`, `docs/plugin-manual-test.md`

**Interfaces:**
```csharp
public sealed class SendDialog : Form
{
    public SendDialog(string title, string info, string okText, IReadOnlyList<AnalysisSummary>? analyses, SendChoice initial);   // 그대로
    public string Corp { get; private set; } = ""; public string ProductLine { get; private set; } = ""; public string Line { get; private set; } = "";
}
public sealed class ServerCommands
{
    /// <summary>열린 도면의 디스크 경로, 또는 null(무제·저장 거절·100 MB 초과). DBMOD != 0이면 저장을 묻고 Yes면 Database.SaveAs(현재 경로)로 저장한다.</summary>
    private static string? DwgToSend(Editor ed);
}
```
- `Send`: `JobPackage.Create(dir, s.ModelName, zip, includeResults: results, dwgPath: DwgToSend(ed))`. `JobSubmission`에 `Corp: dlg.Corp, …`.
- `DwgToSend`: `DWGTITLED == 0` → "저장한 적 없는 도면이라 DWG 없이 보냅니다." · 경로 = `DWGPREFIX + DWGNAME` · `DBMOD != 0` → `MessageBox`(Yes/No) "도면에 저장하지 않은 변경이 있습니다. 저장하고 DWG를 함께 보낼까요? (아니요: DWG 없이 보냄)" · 크기 > `MaxDwgBytes` → "도면(DWG)이 100 MB보다 커서 보내지 않습니다. 모델만 보냅니다." 저장 실패(`ZwSoft.ZwCAD.Runtime.Exception`·IO)는 로그 + DWG 없이 계속.
- `SendDialog`: "분석" 절 아래 태그 3칸(`ComboBox`, `DropDownStyle = DropDown`, `AutoCompleteMode.SuggestAppend`, 항목 = `analyses`의 서로 다른 `Corp/ProductLine/Line`). `Sync()`에서 `_analysisName.Visible`과 같은 조건으로 `Enabled`(새 분석일 때만). 분석 이름 칸 아래 `Theme.Caption(AnalysisNames.NameRuleHelp)`. `FillPlans`가 "새 안…"이고 `planName`이 비면 `_planName.Text = AnalysisNames.NextPlanName(plans.Select(p => p.Name))`. 메모 칸 빈 자리 표시(`EM_SETCUEBANNER` 또는 회색 라벨) `AnalysisNames.PlanNotePlaceholder`. `ClientSize` 높이 +110.
- 수동 테스트 표 `## 자료 보관함 · DWG 동봉 · 태그 (0.21.0)` 행: DW1 저장된 도면 → 패키지에 `drawing.dwg`(작업 페이지 입력 탭 파일 목록), DW2 수정 뒤 보내기 → 저장 질문, 아니요 → "DWG 없이" 전송 성공, DW3 무제 도면 → 메시지 후 전송, DW4 100 MB 초과, DW5 새 분석 때만 태그 칸 활성 + 자동완성, DW6 "새 안…" 기본 이름이 다음 글자, DW7 `/files`에서 자재 코드로 찾아 `continue.zip` 받아 ZWCAD에서 열고 AGVINIT→AGVSEND로 같은 분석 다음 버전, DW8 되살린 `input.xlsx`를 AGVEXCEL "양식 갱신"이 그대로 받아들임.

- [ ] **Step 1: 테스트** 플러그인은 ZWCAD 전용이라 자동 테스트 없음. Core 쪽은 Task 2에서 끝. 수동 표 행을 먼저 적는다(위 DW1~DW8).
- [ ] **Step 2: 빌드 확인** `dotnet build src/ASH.Plugin -c Release` 가 경고 없이 통과(기존 경고 수와 같음).
- [ ] **Step 3: 구현** 위대로. `DwgToSend`는 `Send` 안 "패키지 만드는 중…" 앞에서 부른다(질문은 패키지 작업 전에).
- [ ] **Step 4: 빌드 → PASS**, 가능하면 ZWCAD에서 DW1·DW2 직접 확인하고 표에 날짜 기록.
- [ ] **Step 5: 커밋** `feat(plugin): AGVSEND sends the open DWG (asks to save on DBMOD) and SendDialog takes analysis tags and suggests the next plan name`

---

### Task 8: 마무리 — A와 맞추기

**Files:** `docs/superpowers/specs/2026-10-23-ash-web-and-catalog-design.md`(관문 결과 절에 D 한 줄), A 브랜치에 전달할 목록.

- [ ] **Step 1:** A에게 넘길 호출 지점 세 곳을 PR 설명에 적는다: `app.MapFiles();`(Program.cs), nav "보관함"(`/files`), 현황 탭 버전 줄 `FileRoutes.ContinueLink(j)`, `/analyses` 목록에 `FileRoutes.TagFilterHtml`·`TagMatch`.
- [ ] **Step 2:** 전체 테스트 `dotnet test tests/ASH.Server.Tests` · `dotnet test tests/ASH.Tests` PASS.
- [ ] **Step 3:** 커밋 `docs: file archive notes for the web overhaul merge` (사양 "관문 결과"에 "D: 관문 없음. input.xlsx는 셀 스냅샷에서 되살림, ash.settings.tsv는 0.21 패키지부터 동봉").
