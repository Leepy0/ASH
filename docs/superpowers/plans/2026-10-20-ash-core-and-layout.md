# ASH (AGV Simul Helper) 1차 구현 계획: Core 라이브러리 + 모듈 B(레이아웃 작성·검증·내보내기)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ZWCAD 도면의 경로·CP·block을 검증하고 AutoMod `agv.asy`로 내보내는 플러그인과, 그 계산 전부를 담는 CAD 독립 라이브러리를 만든다.

**Architecture:** 기하 계산(경로 그래프, 분할, block/CP 자동 생성, 검증)과 `.asy` 읽기/쓰기는 전부 `ASH.Core`(.NET Standard 2.0)에 두고 xunit으로 검증한다. `ASH.Plugin`은 ZWCAD 엔티티 ↔ `Layout` 객체 변환과 명령 등록만 하는 얇은 어댑터다. 이 PC에는 ZWCAD이 없으므로 Core는 여기서 전부 테스트하고, Plugin은 빌드만 하고 수동 테스트는 ZWCAD PC에서 한다.

**Tech Stack:** C#, .NET Standard 2.0 (Core), .NET Framework 4.8 (Plugin, ZWCAD .NET API `ZwSoft.ZwCAD.*`), .NET 10 (Tests, xunit). Git.

**Spec:** `docs/superpowers/specs/2026-10-20-ash-plugin-design.md` (6장 모듈 B, 4장 구성, 3장 제약, 11장 관문, 12장 빌더 재량)

**후속 계획서(이 계획 뒤에 따로 작성):** 2차 모듈 C(Excel·공용 로직·모델 생성·빌드), 3차 모듈 D(Runner), 4차 모듈 A(경량화). 2·3차는 11장 관문 1~4의 결과가 나와야 쓸 수 있다.

## Global Constraints

- 솔루션 `ASH`, 프로젝트 `ASH.Core`, `ASH.Plugin`, `ASH.Runner`(이번 계획에서는 빈 껍데기), `ASH.Tests`. 저장소 폴더는 `C:\Users\ioi\Documents\ASH`.
- Core는 `netstandard2.0`, Plugin은 `net48`, Tests는 `net10.0`(이 PC에 .NET 8 SDK가 없어 설계서의 .NET 8 대신 .NET 10 LTS를 쓴다. Runner도 `net10.0`).
- 외부 호스트 의존 없음: NuGet 복원은 빌드 시점에만, 실행 시 네트워크 불필요.
- 레이어 이름: `AGV_PATH`, `AGV_PATH_SPUR`, `AGV_DIR`, `AGV_CP`, `AGV_BLOCK`, `AGV_STATION`, `AGV_RESULT_*`.
- CP 어트리뷰트: `NAME`, `TYPE`(standby/load/unload/rtnload/rtnunload/traffic/buffer), `GROUP`, `STATION`, `CAP`(기본 1). block 어트리뷰트: `NAME`, `CAP`(기본 1).
- 자동 이름 접두어: T=traffic, L=load, U=unload, RL=rtnload, RU=rtnunload, S=standby, B=buffer, block은 `B`. 번호는 세 자리 0 채움(`T001`), 접두어별로 따로 센다. 시작 번호 = 도면의 같은 접두어 최대 번호 + 1.
- 기본값: `SafetyGap` 0.5 m, `CPSpacing` 10 m, `ClaimDist` = SafetyGap, `BlockReach` = 차량 Length + SafetyGap. 연결 허용 오차 1 mm(= 0.001 m).
- `.asy` 헤더: `VERSION 12.6.1.19`, 데모 `agv.asy`의 `SYSDEF`/`FLAGS`/`AGVSTOL` 줄을 템플릿으로 쓴다.
- 원호 `angle`: 0.1도 단위 정수, **양수 = 반시계(CCW), 음수 = 시계(CW)**. 데모 path16(`cenx -50 ceny 47 begx -53 begy 47 angle -900`)의 끝점이 (-50, 50)인 것으로 확정.
- release용 CP는 만들지 않는다. block 경계 이탈 시 AM이 자동 release한다.
- 명령은 모두 `AGV` 접두어. 기존 플러그인과의 명령 호환은 요구하지 않는다.
- 커밋 메시지는 한국어 또는 영어 conventional commit(`feat:`, `test:`, `chore:`).

## Review Focus

설계서가 명시하지 않지만 사용자가 실제로 만나는 입력. 각 항목의 테스트는 지정한 Task에 들어 있다.

1. **역방향 폴리라인의 원호**: 사용자가 폴리라인을 거꾸로 그리고 `AGVREVERSE`하면 bulge 원호의 시작·끝이 바뀌고 `angle` 부호도 뒤집혀야 한다. → Task 10 `Export_ReversedArc_FlipsAngleSign`.
2. **닫힌 루프 경로**: 순환 통로(닫힌 폴리라인 또는 끝점이 자기 시작점에 닿는 경로)에서 그래프 구성과 분할이 무한 루프나 중복 조각을 만들지 않아야 한다. → Task 6 `SplitAll_ClosedLoop_ProducesOnePiecePerArc`.
3. **허용 오차 경계의 끝점**: 끝점 거리 0.9 mm는 연결, 1.1 mm는 끊김으로 판정해야 하고, 그리드 셀 경계에 걸쳐도 같아야 한다. → Task 5 `Graph_EndpointsWithinTolerance_Connect`.
4. **기존 CP 이름이 규칙을 벗어남**: 도면에 `T9`, `T010`, `Tx`, `t3`가 섞여 있을 때 다음 자동 번호는 `T011`이어야 하고 `Tx`는 무시한다. → Task 8 `AutoNamerTests.NextName_IgnoresNonNumericSuffix`.
5. **block 안에 완전히 들어간 경로**: 사각형 안에서 시작해 안에서 끝나는 짧은 경로는 진입 교차가 없으니 CP를 만들지 않고 경고만 낸다. → Task 9 `Generate_PathFullyInsideBlock_WarnsNoCp`.

---

### Task 0: 분석 서버 관문 확인 절차 (코드 없음, 다른 Task와 병행)

**Files:**
- Create: `docs/gates.md`

설계서 11장 1~5번을 USB 키가 있는 분석 서버에서 확인하고 결과를 기록한다. 이 Task는 코드 Task와 독립이며 2·3차 계획서의 전제다.

- [ ] **Step 1: `docs/gates.md`에 다음 절차와 빈 결과 표를 쓴다**

절차(서버에서):
1. `C:\AutoMod\12_6\demos\amoddemos\demos\agv\agvdemo.arc`를 `%TEMP%\gate\agvdemo.arc`로 복사.
2. `cd %TEMP%\gate` 후 `"C:\AutoMod\12_6\bin\am264.exe" -bq agvdemo`. 뜨는 대화상자 제목과 내용을 기록. `agvdemo.dir\agvdemo64.dll`과 `agvdemo.exe` 생성 확인.
3. `agvdemo.exe -w` 실행. Authorization 대화상자가 뜨는지, 뜨지 않고 `agvdemo.report`가 채워지는지 기록. (관문 1)
4. 3을 두 창에서 동시에 실행해 두 번째가 라이선스 오류 없이 돌아가는지 기록. (관문 2)
5. 2에서 "Entity Limit Status" 대화상자의 숫자(현재/최대)와 문구를 그대로 기록. 사용자가 그동안 CP 1000개 규모 모델을 이 서버에서 빌드했는지 함께 적는다. (관문 3)
6. AM 편집기에서 Process 시스템에 block 하나(`Btest`, cap 1)와 CP의 claim 속성 하나를 만들어 저장한 뒤 `<model>~.asy`에서 `Btest`가 들어간 줄과 CP 속성 줄을 그대로 복사해 둔다. (관문 4)

결과 표 열: 관문 번호, 확인 날짜, 결과(통과/실패/보류), 원문 메시지, 영향(8.1 모드 결정).

- [ ] **Step 2: 커밋**

```bash
git add docs/gates.md
git commit -m "docs: add analysis-server gate checklist"
```

---

### Task 1: 저장소와 솔루션 골격

**Files:**
- Create: `ASH.sln`, `Directory.Build.props`, `.gitignore`, `.editorconfig`
- Create: `src/ASH.Core/ASH.Core.csproj`
- Create: `tests/ASH.Tests/ASH.Tests.csproj`, `tests/ASH.Tests/SmokeTests.cs`
- Move: `docs/superpowers/specs/2026-10-20-ash-plugin-design.md` (이미 제자리. 그대로 커밋)

**Interfaces:**
- Produces: `ASH.Core` 네임스페이스 루트. 테스트 프로젝트는 `ASH.Core`를 프로젝트 참조한다.

- [ ] **Step 1: `git init`, `.gitignore`(Visual Studio 표준 + `lib/zwcad/`, `runs/`, `report/`, `*.bak.*`)**

Run: `cd C:\Users\ioi\Documents\ASH; git init -b main`

- [ ] **Step 2: 솔루션과 두 프로젝트 생성**

Run:
```bash
dotnet new sln -n ASH
dotnet new classlib -n ASH.Core -o src/ASH.Core -f netstandard2.0
dotnet new xunit -n ASH.Tests -o tests/ASH.Tests -f net10.0
dotnet sln add src/ASH.Core tests/ASH.Tests
dotnet add tests/ASH.Tests reference src/ASH.Core
```
`Directory.Build.props`: `<LangVersion>latest</LangVersion>`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>false</TreatWarningsAsErrors>`. Core csproj에 `<PackageReference Include="Microsoft.Bcl.HashCode" Version="6.0.0" />`는 넣지 않는다(불필요). Core는 BCL만 쓴다.

- [ ] **Step 3: 스모크 테스트**

```csharp
public class SmokeTests
{
    [Fact] public void CoreAssemblyLoads() => Assert.NotNull(typeof(ASH.Core.Geometry.Point2).Assembly);
}
```
`src/ASH.Core/Geometry/Point2.cs`에 `public readonly record struct Point2(double X, double Y)`를 만든다. `DistanceTo(Point2) : double` 포함.

- [ ] **Step 4: 실행**

Run: `dotnet test tests/ASH.Tests`
Expected: `Passed! - Failed: 0, Passed: 1`

- [ ] **Step 5: 커밋**

```bash
git add -A
git commit -m "chore: solution skeleton with Core and Tests"
```

---

### Task 2: AGVS `.asy` 모델 객체와 Reader

**Files:**
- Create: `src/ASH.Core/Asy/AgvsSystem.cs`, `src/ASH.Core/Asy/GuidePath.cs`, `src/ASH.Core/Asy/ControlPoint.cs`, `src/ASH.Core/Asy/AsyReader.cs`
- Create: `tests/ASH.Tests/Asy/AsyReaderTests.cs`
- Create: `tests/ASH.Tests/Fixtures/agvdemo/agv.asy` (`C:\AutoMod\12_6\demos\amoddemos\demos\agv\agvdemo.arc\agv.asy` 복사, csproj에서 `CopyToOutputDirectory=PreserveNewest`)

**Interfaces:**
- Produces:
  - `sealed class AgvsSystem { string Version; string[] UnitsTokens /*["Feet","Seconds"]*/; List<string> HeaderLines /*SYSDEF, FLAGS 블록, AGVSDEF 블록, AGVSTOL: 원문 그대로*/; List<string> PathTypeLines; List<GuidePath> Paths; List<string> CpTypeLines; List<ControlPoint> ControlPoints; Dictionary<string,List<string>> NameLists; List<string> ParkListLines; List<string> WorkListLines; List<string> VehicleSegmentLines /*AGVVEHSEG부터 다음 최상위 키워드 전까지 원문*/; List<string> VehicleLines /*AGVSVEH 블록 원문*/; }`
  - `abstract record PathPiece; record LinePiece(Point2 Beg, Point2 End) : PathPiece; record ArcPiece(Point2 Center, Point2 Beg, int AngleTenths) : PathPiece { Point2 End { get; } /*중심 기준 Beg 각도 + AngleTenths/10도*/ }`
  - `sealed record GuidePath(string Name, string Type, bool IsSpur, bool IsCrab, PathPiece Piece)`
  - `sealed record ControlPoint(string Name, string Type, string PathName, double Dist, string? Align)`
  - `static class AsyReader { static AgvsSystem Read(TextReader r); static AgvsSystem ReadFile(string path); }`

- [ ] **Step 1: 실패하는 테스트**

```csharp
public class AsyReaderTests
{
    static AgvsSystem Demo() => AsyReader.ReadFile(Path.Combine(AppContext.BaseDirectory, "Fixtures/agvdemo/agv.asy"));

    [Fact] public void Reads_Header() { var s = Demo(); Assert.Equal("12.6.1.19", s.Version); Assert.Equal(new[]{"Feet","Seconds"}, s.UnitsTokens); }
    [Fact] public void Reads_21_Paths_11_Spur_5_Arcs() { var s = Demo(); Assert.Equal(21, s.Paths.Count); Assert.Equal(11, s.Paths.Count(p => p.IsSpur && p.IsCrab)); Assert.Equal(5, s.Paths.Count(p => p.Piece is ArcPiece)); }
    [Fact] public void Reads_32_ControlPoints() { var s = Demo(); Assert.Equal(32, s.ControlPoints.Count); var z1 = s.ControlPoints.Single(c => c.Name == "z1"); Assert.Equal("path11_1", z1.PathName); Assert.Equal(50, z1.Dist); }
    [Fact] public void Reads_Workcheck_Align() { var c = Demo().ControlPoints.Single(x => x.Name == "workcheck"); Assert.Equal("leadingpap", c.Align); Assert.Equal(1.10268372703412, c.Dist, 9); }
    [Fact] public void Merges_NameList_Lines() { var s = Demo(); Assert.Equal(new[]{"p_1","p_2","p_3","p_4","p_5"}, s.NameLists["nlwork"]); }
    [Fact] public void Arc_End_Is_Computed_CW_For_Negative_Angle()
    {
        var p16 = Demo().Paths.Single(p => p.Name == "path16").Piece as ArcPiece;
        Assert.Equal(-900, p16!.AngleTenths);
        Assert.Equal(-50, p16.End.X, 6); Assert.Equal(50, p16.End.Y, 6);
    }
    [Fact] public void Keeps_VehicleSegment_And_Vehicle_Raw() { var s = Demo(); Assert.StartsWith("AGVVEHSEG name DefSegment", s.VehicleSegmentLines[0]); Assert.StartsWith("AGVSVEH type DefVehicle numveh 10", s.VehicleLines[0]); Assert.Equal(6, s.ParkListLines.Count); Assert.Single(s.WorkListLines); }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test tests/ASH.Tests --filter FullyQualifiedName~AsyReaderTests`
Expected: 컴파일 오류(타입 없음).

- [ ] **Step 3: 모델 객체와 `AsyReader.Read` 구현**

파서 규칙: 줄 첫 토큰이 최상위 키워드(`VERSION SYSTYPE UNITS SYSDEF FLAGS AGVSDEF AGVSTOL GPATHTYPE GPATH CPOINTTYPE CPOINT NAMELST PARKLST WORKLST AGVVEHSEG AGVSVEH`)면 새 블록, 탭·공백으로 시작하거나 키워드가 아니면 직전 블록의 연속 줄. `GPATH`는 `piece` 뒤 토큰을 키-값으로 읽고 `cenx`가 있으면 `ArcPiece`, 없으면 `LinePiece`. `spur`/`crab`는 `type <T>` 뒤 `piece` 앞의 플래그. `CPOINT … at <path> <dist>`, 그 사이 `align <v>`는 선택. `NAMELST name X item a item b`는 같은 이름에 누적. `ArcPiece.End`: `θ0 = atan2(Beg-Center)`, `θ1 = θ0 + AngleTenths/10 * π/180`, `End = Center + R(cos θ1, sin θ1)`.

- [ ] **Step 4: 통과 확인**

Run: `dotnet test tests/ASH.Tests --filter FullyQualifiedName~AsyReaderTests`
Expected: `Passed: 7`

- [ ] **Step 5: 커밋**

```bash
git add -A
git commit -m "feat(core): AGVS .asy model and reader verified against agvdemo"
```

---

### Task 3: AGVS `.asy` Writer (텍스트 왕복)

**Files:**
- Create: `src/ASH.Core/Asy/AsyWriter.cs`
- Create: `tests/ASH.Tests/Asy/AsyWriterTests.cs`

**Interfaces:**
- Consumes: Task 2의 `AgvsSystem`, `GuidePath`, `ControlPoint`.
- Produces: `static class AsyWriter { static void Write(AgvsSystem s, TextWriter w); static void WriteFile(AgvsSystem s, string path); static string FormatPath(GuidePath p); static string FormatCp(ControlPoint c); }`

- [ ] **Step 1: 실패하는 테스트**

```csharp
public class AsyWriterTests
{
    [Fact] public void Line_Path_Format() => Assert.Equal("GPATH name path11 type DefaultGuidePath piece begx -50 begy 50 endx 11 endy 50 upz 1",
        AsyWriter.FormatPath(new GuidePath("path11","DefaultGuidePath",false,false,new LinePiece(new(-50,50), new(11,50)))));
    [Fact] public void Arc_Spur_Format() => Assert.Equal("GPATH name path16 type DefaultGuidePath piece cenx -50 ceny 47 begx -53 begy 47 upz 1 angle -900",
        AsyWriter.FormatPath(new GuidePath("path16","DefaultGuidePath",false,false,new ArcPiece(new(-50,47), new(-53,47), -900))));
    [Fact] public void Spur_Crab_Keywords() => Assert.Contains(" spur crab piece ", AsyWriter.FormatPath(new GuidePath("p","T",true,true,new LinePiece(new(0,0),new(1,0)))));
    [Fact] public void Cp_Format_With_Align() => Assert.Equal("CPOINT name workcheck type DefaultControlPoint align leadingpap at path30 1.10268372703412",
        AsyWriter.FormatCp(new ControlPoint("workcheck","DefaultControlPoint","path30",1.10268372703412,"leadingpap")));
    [Fact] public void RoundTrip_Demo_Preserves_All_Entity_Lines()
    {
        var src = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures/agvdemo/agv.asy"));
        var sys = AsyReader.Read(new StringReader(string.Join("\n", src)));
        var sw = new StringWriter(); AsyWriter.Write(sys, sw);
        var outLines = sw.ToString().Split('\n').Select(l => l.TrimEnd('\r'));
        // 숫자 토큰을 double로 읽어 "R"로 다시 써서 비교한다(데모의 "0.0"과 우리 출력 "0"을 같게 본다)
        static string Norm(string l) => string.Join(" ", l.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d.ToString("R", CultureInfo.InvariantCulture) : t));
        string[] keys = { "GPATH ", "CPOINT ", "GPATHTYPE ", "CPOINTTYPE ", "PARKLST ", "WORKLST " };
        foreach (var k in keys)
            Assert.Equal(src.Where(l => l.StartsWith(k)).Select(Norm), outLines.Where(l => l.StartsWith(k)).Select(Norm));
        Assert.Equal(src.Count(l => l.StartsWith("AGVVEHSEG")), outLines.Count(l => l.StartsWith("AGVVEHSEG")));
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test tests/ASH.Tests --filter FullyQualifiedName~AsyWriterTests`
Expected: 컴파일 오류.

- [ ] **Step 3: 구현**

숫자는 `double.ToString("R", InvariantCulture)`. 쓰는 순서: `VERSION`, `SYSTYPE AGVS`, `UNITS`, HeaderLines, PathTypeLines, Paths, CpTypeLines, ControlPoints, NAMELST(이름당 `NAMELST name X` 한 줄 + `NAMELST name X item …` 한 줄), ParkListLines, WorkListLines, VehicleSegmentLines, VehicleLines. 줄 끝은 `\n`.

- [ ] **Step 4: 통과 확인**

Run: `dotnet test tests/ASH.Tests --filter FullyQualifiedName~AsyWriterTests`
Expected: `Passed: 5`

- [ ] **Step 5: 커밋**

```bash
git add -A
git commit -m "feat(core): AGVS .asy writer with demo round-trip test"
```

---

### Task 4: AM 빌드 실행기와 대화상자 닫기 (통합 테스트, AM 설치 PC 전용)

**Files:**
- Create: `src/ASH.Core/AutoMod/AmBuilder.cs`, `src/ASH.Core/AutoMod/DialogCloser.cs`
- Create: `tests/ASH.Tests/AutoMod/AmBuildIntegrationTests.cs`

**Interfaces:**
- Produces:
  - `sealed record AmBuildResult(bool Success, string MakeErrText, string Am2ErrText, IReadOnlyList<string> ClosedDialogTitles)`
  - `static class AmBuilder { static AmBuildResult Build(string amHome /*C:\AutoMod\12_6*/, string modelDir /*<Model>.arc의 부모*/, string modelName, TimeSpan timeout); }`
  - `static class DialogCloser { static IDisposable Start(int ownerPid, string[] titleContains /*{"Entity Limit"}*/, Action<string> onClosed); }` — Win32 `EnumWindows`/`GetWindowThreadProcessId`/`GetWindowText`로 제목에 지정 문구가 들어간 창에만 `WM_CLOSE`(`SendMessage 0x0010`)를 보낸다. "Authorization", "Error" 포함 창은 건드리지 않는다.

- [ ] **Step 1: 환경변수 없으면 건너뛰는 통합 테스트**

```csharp
public class AmBuildIntegrationTests
{
    static string? AmHome => Environment.GetEnvironmentVariable("ASH_AM_HOME"); // 예: C:\AutoMod\12_6
    [SkippableFact] public void Rewritten_Demo_Builds()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var work = Path.Combine(Path.GetTempPath(), "ash_build_" + Guid.NewGuid().ToString("N"));
        CopyDir(Path.Combine(AmHome!, @"demos\amoddemos\demos\agv\agvdemo.arc"), Path.Combine(work, "agvdemo.arc"));
        var asy = Path.Combine(work, "agvdemo.arc", "agv.asy");
        AsyWriter.WriteFile(AsyReader.ReadFile(asy), asy);           // 우리 Writer 출력으로 교체
        var r = AmBuilder.Build(AmHome!, work, "agvdemo", TimeSpan.FromMinutes(5));
        Assert.True(r.Success, r.MakeErrText + r.Am2ErrText);
        Assert.True(File.Exists(Path.Combine(work, "agvdemo.dir", "agvdemo64.dll")));
    }
}
```
`Xunit.SkippableFact` 패키지 추가: `dotnet add tests/ASH.Tests package Xunit.SkippableFact`. 템플릿이 xunit v3를 만들었으면 패키지 대신 `[Fact]` + `Assert.Skip("ASH_AM_HOME not set")`을 쓴다.

- [ ] **Step 2: 실패 확인**

Run: `$env:ASH_AM_HOME="C:\AutoMod\12_6"; dotnet test tests/ASH.Tests --filter FullyQualifiedName~AmBuildIntegrationTests`
Expected: 컴파일 오류.

- [ ] **Step 3: 구현**

`AmBuilder.Build`: `Process.Start(new ProcessStartInfo(Path.Combine(amHome,"bin","am264.exe"), $"-bq {modelName}") { WorkingDirectory = modelDir })`, 시작 직후 `DialogCloser.Start(pid, new[]{"Entity Limit"}, …)`. 종료 대기(timeout 초과 시 Kill, Success=false). 성공 판정 = 종료 코드 0 **그리고** `<modelDir>\<modelName>.dir\<modelName>64.dll` 존재. `make.err`와 `am2err.dat` 내용을 결과에 담는다.

- [ ] **Step 4: 통과 확인(이 PC는 AM이 있으므로 실제 실행)**

Run: `$env:ASH_AM_HOME="C:\AutoMod\12_6"; dotnet test tests/ASH.Tests --filter FullyQualifiedName~AmBuildIntegrationTests`
Expected: `Passed: 1`, `ClosedDialogTitles`에 "Entity Limit Status" 1건. 환경변수 없이 실행하면 `Skipped: 1`.

- [ ] **Step 5: 커밋**

```bash
git add -A
git commit -m "feat(core): am264 -bq build runner with info-dialog closer; demo integration test"
```

---

### Task 5: 레이아웃 모델, 공간 그리드, 경로 그래프

**Files:**
- Create: `src/ASH.Core/Layout/Layout.cs`, `LayoutPath.cs`, `LayoutCp.cs`, `LayoutBlock.cs`, `Segment.cs`
- Create: `src/ASH.Core/Geometry/SpatialGrid.cs`, `src/ASH.Core/Geometry/Rect2.cs`
- Create: `src/ASH.Core/Layout/PathGraph.cs`
- Create: `tests/ASH.Tests/Layout/PathGraphTests.cs`, `tests/ASH.Tests/Layout/L.cs` (테스트용 레이아웃 빌더)

**Interfaces:**
- Produces:
  - `abstract record Segment { Point2 Start; Point2 End; double Length; Point2 PointAt(double dist); Segment Reversed(); }`, `record LineSeg(Point2 Start, Point2 End)`, `record ArcSeg(Point2 Center, Point2 Start, double SweepDeg /*CCW 양수*/)` (`End`, `Radius` 계산 속성)
  - `sealed class LayoutPath { LayoutPath(string name); string Name; string Id /*CAD 핸들 또는 GUID, 기본 = Guid 문자열*/; bool IsSpur; List<Segment> Segments = new() /*그린 순서 = 진행 방향*/; Point2 Start; Point2 End; double Length; Point2 PointAt(double dist); }`
  - `sealed class LayoutCp { string Name; string Type; string Group; string Station; int Cap; Point2 Position; string Id; }`
  - `sealed class LayoutBlock { string Name; int Cap; Rect2 Rect; string Id; }`
  - `sealed class LayoutStation { string Name; Point2 Position; }`
  - `sealed class Layout { List<LayoutPath> Paths { get; set; } = new(); List<LayoutCp> Cps = new(); List<LayoutBlock> Blocks = new(); List<LayoutStation> Stations = new(); double Tolerance = 0.001; }`
  - `readonly record struct Rect2(Point2 Min, Point2 Max) { bool Contains(Point2); Rect2 Expand(double d); bool Intersects(Rect2); }`
  - `sealed class SpatialGrid<T> { SpatialGrid(double cellSize); void Add(Point2 p, T item); IEnumerable<T> Near(Point2 p, double radius); }` — 반지름이 셀 경계에 걸치면 이웃 셀도 본다.
  - `sealed class PathNode { Point2 Position; List<(LayoutPath Path, bool AtStart)> Ends; int Degree => Ends.Count; bool IsJunction => Degree >= 3; int InCount /*AtStart==false인 끝 = 들어오는 경로*/; int OutCount; }`
  - `sealed class PathGraph { static PathGraph Build(Layout l); IReadOnlyList<PathNode> Nodes; IEnumerable<PathNode> Junctions; IEnumerable<(LayoutPath Path, bool AtStart)> DanglingEnds /*Degree==1인 노드의 끝*/; }`
  - 테스트 헬퍼 `static class L { static LayoutPath Line(string name, double x1,double y1,double x2,double y2); static Layout Of(params LayoutPath[] paths); }`

- [ ] **Step 1: 실패하는 테스트**

```csharp
public class PathGraphTests
{
    [Fact] public void Cross_Of_Four_Arms_Is_One_Junction_Degree_4()
    {
        var g = PathGraph.Build(L.Of(L.Line("a",-10,0,0,0), L.Line("b",0,0,10,0), L.Line("c",0,-10,0,0), L.Line("d",0,0,0,10)));
        var j = Assert.Single(g.Junctions); Assert.Equal(4, j.Degree); Assert.Equal(2, j.InCount); Assert.Equal(2, j.OutCount);
        Assert.Equal(4, g.DanglingEnds.Count());
    }
    [Fact] public void Graph_EndpointsWithinTolerance_Connect()
    {
        var g1 = PathGraph.Build(L.Of(L.Line("a",0,0,10,0), L.Line("b",10.0009,0,20,0)));
        Assert.Single(g1.Nodes, n => n.Degree == 2);
        var g2 = PathGraph.Build(L.Of(L.Line("a",0,0,10,0), L.Line("b",10.0011,0,20,0)));
        Assert.DoesNotContain(g2.Nodes, n => n.Degree == 2);
    }
    [Fact] public void Grid_Near_Crosses_Cell_Boundary()
    {
        var grid = new SpatialGrid<int>(1.0); grid.Add(new(0.9999,0), 1); grid.Add(new(1.0001,0), 2);
        Assert.Equal(new[]{1,2}, grid.Near(new(1.0,0), 0.001).OrderBy(x=>x));
    }
    [Fact] public void ArcSeg_End_And_Length() { var a = new ArcSeg(new(0,0), new(1,0), 90); Assert.Equal(0, a.End.X, 9); Assert.Equal(1, a.End.Y, 9); Assert.Equal(Math.PI/2, a.Length, 9); }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test tests/ASH.Tests --filter FullyQualifiedName~PathGraphTests`
Expected: 컴파일 오류.

- [ ] **Step 3: 구현**

`PathGraph.Build`: 그리드 셀 크기 = `max(Tolerance*10, 0.01)`. 각 경로의 Start/End를 그리드에 넣고, 반경 Tolerance 안의 기존 노드가 있으면 합치고 없으면 새 노드. 경로 End가 모이면 `InCount`, Start가 모이면 `OutCount`.

- [ ] **Step 4: 통과 확인**

Run: `dotnet test tests/ASH.Tests --filter FullyQualifiedName~PathGraphTests`
Expected: `Passed: 4`

- [ ] **Step 5: 커밋**

```bash
git add -A
git commit -m "feat(core): layout model, spatial grid, path graph"
```

---

### Task 6: 경로 상호 분할 (`AGVSPLIT`의 계산부)

**Files:**
- Create: `src/ASH.Core/Geometry/Intersections.cs`, `src/ASH.Core/Layout/PathSplitter.cs`
- Create: `tests/ASH.Tests/Layout/PathSplitterTests.cs`

**Interfaces:**
- Consumes: Task 5의 `LayoutPath`, `Segment`, `SpatialGrid`.
- Produces:
  - `static class Intersections { static List<Point2> LineLine(LineSeg a, LineSeg b, double tol); static List<Point2> LineArc(LineSeg a, ArcSeg b, double tol); static List<Point2> ArcArc(ArcSeg a, ArcSeg b, double tol); static List<Point2> SegSeg(Segment a, Segment b, double tol); }` — 끝점끼리 맞닿는 점(두 세그먼트 모두 끝점에서 tol 안)은 결과에서 제외한다.
  - `sealed record SplitResult(List<LayoutPath> Paths, int IntersectionCount, Dictionary<string, List<string>> Renamed /*원래 이름 → 조각 이름들*/)`
  - `static class PathSplitter { static SplitResult SplitAll(IReadOnlyList<LayoutPath> paths, double tol); static List<LayoutPath> SplitAt(LayoutPath p, IEnumerable<double> distances, double tol); static IEnumerable<Point2> FindUnsplitIntersections(IReadOnlyList<LayoutPath> paths, double tol); }`
  - 조각 이름 `원래이름_1`, `_2`, … 진행 방향 순. `IsSpur`, `Id`(원본 Id + `#k`) 유지. 분할점이 경로의 어느 끝에서 tol 안이면 자르지 않는다.

- [ ] **Step 1: 실패하는 테스트**

```csharp
public class PathSplitterTests
{
    [Fact] public void Cross_Splits_Both_Into_Two()
    {
        var r = PathSplitter.SplitAll(new[]{ L.Line("h",-10,0,10,0), L.Line("v",0,-10,0,10) }, 0.001);
        Assert.Equal(1, r.IntersectionCount); Assert.Equal(4, r.Paths.Count);
        Assert.Equal(new[]{"h_1","h_2"}, r.Renamed["h"]);
        var h1 = r.Paths.Single(p => p.Name=="h_1"); Assert.Equal(-10, h1.Start.X); Assert.Equal(0, h1.End.X, 9);
    }
    [Fact] public void T_Junction_Splits_Only_The_Crossed_Path()
    {
        var r = PathSplitter.SplitAll(new[]{ L.Line("h",-10,0,10,0), L.Line("v",0,0,0,10) }, 0.001);
        Assert.Equal(3, r.Paths.Count); Assert.Single(r.Paths, p => p.Name == "v");
    }
    [Fact] public void N_Intersections_Give_N_Plus_One_Pieces()
    {
        var paths = new List<LayoutPath>{ L.Line("h",0,0,100,0) };
        for (int i = 1; i <= 5; i++) paths.Add(L.Line("v"+i, i*10,-5, i*10,5));
        var r = PathSplitter.SplitAll(paths, 0.001);
        Assert.Equal(6, r.Renamed["h"].Count);
    }
    [Fact] public void Line_Arc_Intersection_Splits_Arc_Keeping_Sweep_Sign()
    {
        var arc = new LayoutPath("arc"){ Segments = { new ArcSeg(new(0,0), new(10,0), 180) } };   // (10,0)→(-10,0) 위쪽 반원
        var r = PathSplitter.SplitAll(new[]{ arc, L.Line("v",0,-20,0,20) }, 0.001);
        var pieces = r.Paths.Where(p => p.Name.StartsWith("arc_")).ToList();
        Assert.Equal(2, pieces.Count); Assert.All(pieces, p => Assert.True(((ArcSeg)p.Segments[0]).SweepDeg > 0));
        Assert.Equal(0, pieces[0].End.X, 6); Assert.Equal(10, pieces[0].End.Y, 6);
    }
    [Fact] public void Touching_Endpoints_Are_Not_Intersections()
    {
        var r = PathSplitter.SplitAll(new[]{ L.Line("a",0,0,10,0), L.Line("b",10,0,20,0) }, 0.001);
        Assert.Equal(0, r.IntersectionCount); Assert.Equal(2, r.Paths.Count);
    }
    [Fact] public void SplitAll_ClosedLoop_ProducesOnePiecePerArc()
    {
        var loop = new LayoutPath("loop"){ Segments = { new ArcSeg(new(0,0), new(10,0), 180), new ArcSeg(new(0,0), new(-10,0), 180) } };
        var r = PathSplitter.SplitAll(new[]{ loop, L.Line("v",0,-20,0,20) }, 0.001);
        Assert.Equal(2, r.IntersectionCount); Assert.Equal(3, r.Renamed["loop"].Count);  // 루프는 시작점에서 이미 끊겨 있으므로 2교차 → 3조각
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test tests/ASH.Tests --filter FullyQualifiedName~PathSplitterTests`
Expected: 컴파일 오류.

- [ ] **Step 3: 구현**

`SplitAll`: 각 세그먼트의 바운딩 박스를 `SpatialGrid`(셀 = 전체 범위/64)에 넣어 후보 쌍만 교차 계산. 경로별로 교차점의 "시작부터 거리" 목록을 모아 정렬·중복 제거(tol)한 뒤 `SplitAt`. T자 접점 = 한 경로의 끝점이 다른 경로 내부(두 끝에서 tol 밖)에 tol 안으로 닿는 경우. `SplitAt`은 세그먼트 경계와 교차점을 모두 끊되 조각 사이의 세그먼트 경계는 한 조각 안에 둔다(조각은 여러 세그먼트 가능). 원호 분할: 시작각부터의 sweep 비율로 나눈다.

- [ ] **Step 4: 통과 확인**

Run: `dotnet test tests/ASH.Tests --filter FullyQualifiedName~PathSplitterTests`
Expected: `Passed: 6`

- [ ] **Step 5: 커밋**

```bash
git add -A
git commit -m "feat(core): mutual path splitting at intersections and T-junctions"
```

---

### Task 7: 레이아웃 검증 (`AGVCHECK`의 계산부)

**Files:**
- Create: `src/ASH.Core/Layout/LayoutChecker.cs`, `src/ASH.Core/Layout/Finding.cs`
- Create: `tests/ASH.Tests/Layout/LayoutCheckerTests.cs`

**Interfaces:**
- Consumes: Task 5 `PathGraph`, Task 6 `PathSplitter.FindUnsplitIntersections`.
- Produces:
  - `enum FindingCode { DanglingEnd, DeadEndNode, DuplicateCpName, UnnamedCp, CpOffPath, BlockTouchesNoPath, UnsplitIntersection, RouteCpMissing }`
  - `sealed record Finding(FindingCode Code, string Message, Point2 Where, string? EntityId)`
  - `sealed record CheckOptions(double CpMaxOffset = 0.05 /*m*/, IReadOnlyCollection<string>? RouteCpNames = null /*Excel Routes 시트 CP 이름, 없으면 검사 생략*/)`
  - `static class LayoutChecker { static List<Finding> Check(Layout l, CheckOptions o); static (LayoutPath Path, double Dist, double Offset)? Project(Layout l, Point2 p) /*가장 가까운 경로와 그 위의 거리*/; }`
  - `DeadEndNode` = Degree ≥ 2인데 `OutCount == 0`(들어오기만 함) 또는 `InCount == 0`인 노드. `DanglingEnd` = Degree 1인 노드의 끝(지선 `IsSpur` 경로의 자유 끝은 제외).

- [ ] **Step 1: 실패하는 테스트** — 코드별 한 건씩

```csharp
public class LayoutCheckerTests
{
    static CheckOptions O => new();
    [Fact] public void Dangling_End_Reported_Except_Spur() {
        var l = L.Of(L.Line("a",0,0,10,0), L.Spur("s",5,0,5,3));
        var f = LayoutChecker.Check(l, O).Where(x => x.Code == FindingCode.DanglingEnd).ToList();
        Assert.Equal(2, f.Count); Assert.DoesNotContain(f, x => x.EntityId == "s");
    }
    [Fact] public void Dead_End_Node() {
        var l = L.Of(L.Line("a",-10,0,0,0), L.Line("b",10,0,0,0)); // 둘 다 (0,0)으로 들어옴
        Assert.Contains(LayoutChecker.Check(l, O), x => x.Code == FindingCode.DeadEndNode && x.Where == new Point2(0,0));
    }
    [Fact] public void Duplicate_And_Unnamed_Cp() {
        var l = L.Of(L.Line("a",0,0,10,0)); l.Cps.Add(L.Cp("T001", 2,0)); l.Cps.Add(L.Cp("T001", 4,0)); l.Cps.Add(L.Cp("", 6,0));
        var f = LayoutChecker.Check(l, O);
        Assert.Equal(2, f.Count(x => x.Code == FindingCode.DuplicateCpName)); Assert.Single(f, x => x.Code == FindingCode.UnnamedCp);
    }
    [Fact] public void Cp_Off_Path() {
        var l = L.Of(L.Line("a",0,0,10,0)); l.Cps.Add(L.Cp("T001", 5, 0.06));
        Assert.Single(LayoutChecker.Check(l, O), x => x.Code == FindingCode.CpOffPath);
    }
    [Fact] public void Block_Touches_No_Path() {
        var l = L.Of(L.Line("a",0,0,10,0)); l.Blocks.Add(L.Block("B001", 20,20, 25,25));
        Assert.Single(LayoutChecker.Check(l, O), x => x.Code == FindingCode.BlockTouchesNoPath);
    }
    [Fact] public void Unsplit_Intersection() {
        var l = L.Of(L.Line("h",-10,0,10,0), L.Line("v",0,-10,0,10));
        Assert.Single(LayoutChecker.Check(l, O), x => x.Code == FindingCode.UnsplitIntersection && x.Where == new Point2(0,0));
    }
    [Fact] public void Route_Cp_Missing_Only_When_List_Given() {
        var l = L.Of(L.Line("a",0,0,10,0)); l.Cps.Add(L.Cp("L001", 2,0));
        Assert.Empty(LayoutChecker.Check(l, O).Where(x => x.Code == FindingCode.RouteCpMissing));
        Assert.Single(LayoutChecker.Check(l, new CheckOptions(RouteCpNames: new[]{"L001","U001"})), x => x.Code == FindingCode.RouteCpMissing && x.Message.Contains("U001"));
    }
}
```
`L.cs`에 `Spur(name, x1, y1, x2, y2)`(IsSpur=true), `Cp(name, x, y, type="traffic", group="")`, `Block(name, minx, miny, maxx, maxy, cap=1)` 헬퍼 추가.

- [ ] **Step 2: 실패 확인**

Run: `dotnet test tests/ASH.Tests --filter FullyQualifiedName~LayoutCheckerTests`
Expected: 컴파일 오류.

- [ ] **Step 3: 구현**

`Project`: 세그먼트 바운딩 박스 그리드로 후보를 좁힌 뒤 선분·원호에 수직 투영. `BlockTouchesNoPath`: 어떤 경로의 세그먼트도 `Rect`와 교차하지 않고 안에 들어 있지도 않음.

- [ ] **Step 4: 통과 확인**

Run: `dotnet test tests/ASH.Tests --filter FullyQualifiedName~LayoutCheckerTests`
Expected: `Passed: 7`

- [ ] **Step 5: 커밋**

```bash
git add -A
git commit -m "feat(core): layout checker with per-code findings"
```

---

### Task 8: block 자동 생성 (`AGVBLOCKAUTO`의 계산부)

**Files:**
- Create: `src/ASH.Core/Traffic/TrafficParams.cs`, `src/ASH.Core/Traffic/BlockGenerator.cs`, `src/ASH.Core/Traffic/AutoNamer.cs`
- Create: `tests/ASH.Tests/Traffic/BlockGeneratorTests.cs`, `tests/ASH.Tests/Traffic/AutoNamerTests.cs`

**Interfaces:**
- Consumes: Task 5 `PathGraph`, `Rect2`.
- Produces:
  - `sealed record TrafficParams(double VehicleLength, double VehicleWidth, double SafetyGap = 0.5, double? BlockReach = null, double? ClaimDist = null, double CpSpacing = 10, bool SpacingCps = false, bool CurveCps = false) { double EffectiveBlockReach => BlockReach ?? VehicleLength + SafetyGap; double EffectiveClaimDist => ClaimDist ?? SafetyGap; }`
  - `static class AutoNamer { static string Next(string prefix, IEnumerable<string> existingNames) /*prefix+숫자 형태만 집계, 대소문자 구분, 3자리 0 채움, 더 길면 그대로*/; static string PrefixFor(string cpType); }`
  - `sealed record BlockProposal(List<LayoutBlock> Blocks, List<List<string>> OverlapGroups /*겹치는 block 이름 묶음, 사용자에게 합칠지 물을 대상*/, List<string> Warnings)`
  - `static class BlockGenerator { static BlockProposal Generate(Layout l, TrafficParams p); static LayoutBlock Merge(IEnumerable<LayoutBlock> blocks, string name) /*합집합 바운딩 박스, cap 1*/; }`
  - 노드 block: `IsJunction` 노드마다, 노드에 닿는 각 경로를 노드에서 `EffectiveBlockReach`만큼 따라간 점들(세그먼트 꺾임점 포함, 경로가 더 짧으면 그 끝까지)의 바운딩 박스를 `VehicleWidth/2 + SafetyGap`만큼 `Expand`. 이미 같은 노드를 덮는 block(`Rect.Contains(node)`)이 있으면 건너뛴다. 이름은 `AutoNamer.Next("B", 기존 block 이름)`.
  - 구간 block(`SpacingCps == true`일 때만): Task 9가 간격 CP를 만든 뒤 호출하는 `static List<LayoutBlock> GenerateCorridorBlocks(Layout l, TrafficParams p)` — 같은 경로 위에서 인접한 traffic CP 쌍 사이 구간의 바운딩 박스를 같은 폭으로 Expand.

- [ ] **Step 1: 실패하는 테스트**

```csharp
public class AutoNamerTests
{
    [Fact] public void First_Name_Is_001() => Assert.Equal("T001", AutoNamer.Next("T", Array.Empty<string>()));
    [Fact] public void NextName_IgnoresNonNumericSuffix() => Assert.Equal("T011", AutoNamer.Next("T", new[]{"T9","T010","Tx","t3","B002"}));
    [Fact] public void Wide_Numbers_Keep_Width() => Assert.Equal("T1000", AutoNamer.Next("T", new[]{"T999"}));
    [Fact] public void Prefix_Per_Type() { Assert.Equal("RL", AutoNamer.PrefixFor("rtnload")); Assert.Equal("S", AutoNamer.PrefixFor("standby")); Assert.Equal("B", AutoNamer.PrefixFor("buffer")); }
}
public class BlockGeneratorTests
{
    static readonly TrafficParams P = new(VehicleLength: 2.0, VehicleWidth: 1.0, SafetyGap: 0.5);   // reach 2.5, expand 1.0
    [Fact] public void Cross_Gives_One_Block_Of_Expected_Size()
    {
        var l = L.Of(L.Line("a",-10,0,0,0), L.Line("b",0,0,10,0), L.Line("c",0,-10,0,0), L.Line("d",0,0,0,10));
        var r = BlockGenerator.Generate(l, P);
        var b = Assert.Single(r.Blocks); Assert.Equal("B001", b.Name); Assert.Equal(1, b.Cap);
        Assert.Equal(new Rect2(new(-3.5,-3.5), new(3.5,3.5)), b.Rect);
    }
    [Fact] public void Short_Arm_Clamps_At_Path_End()
    {
        var l = L.Of(L.Line("a",-10,0,0,0), L.Line("b",0,0,1,0), L.Line("c",0,-10,0,0));
        var b = Assert.Single(BlockGenerator.Generate(l, P).Blocks);
        Assert.Equal(2.0, b.Rect.Max.X, 9);   // 1.0 + expand 1.0
    }
    [Fact] public void Two_Close_Junctions_Reported_As_Overlap_Group()
    {
        var l = L.Of(L.Line("a",-10,0,0,0), L.Line("b",0,0,3,0), L.Line("c",3,0,13,0), L.Line("d",0,0,0,10), L.Line("e",3,0,3,10));
        var r = BlockGenerator.Generate(l, P);
        Assert.Equal(2, r.Blocks.Count); var g = Assert.Single(r.OverlapGroups); Assert.Equal(new[]{"B001","B002"}, g);
    }
    [Fact] public void Existing_Block_Covering_Node_Is_Kept()
    {
        var l = L.Of(L.Line("a",-10,0,0,0), L.Line("b",0,0,10,0), L.Line("c",0,-10,0,0)); l.Blocks.Add(L.Block("B007",-1,-1,1,1));
        var r = BlockGenerator.Generate(l, P); Assert.Empty(r.Blocks);
    }
    [Fact] public void Merge_Is_Union_Bbox() => Assert.Equal(new Rect2(new(0,0), new(5,5)), BlockGenerator.Merge(new[]{ L.Block("x",0,0,2,2), L.Block("y",3,3,5,5) }, "B001").Rect);
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test tests/ASH.Tests --filter "FullyQualifiedName~BlockGeneratorTests|FullyQualifiedName~AutoNamerTests"`
Expected: 컴파일 오류.

- [ ] **Step 3: 구현**

경로 따라가기: 노드가 경로의 End면 뒤쪽으로, Start면 앞쪽으로 `reach`만큼 `PointAt`으로 샘플(세그먼트 끝점과 최종점). 노드는 `(X, Y)` 오름차순으로 처리해 이름이 안정적이게 한다. 겹침 묶음은 `Rect2.Intersects`로 union-find.

- [ ] **Step 4: 통과 확인**

Run: 같은 필터. Expected: `Passed: 9`

- [ ] **Step 5: 커밋**

```bash
git add -A
git commit -m "feat(core): junction block auto-generation and auto-naming"
```

---

### Task 9: traffic CP 자동 배치와 교통제어 테이블 (`AGVCPAUTO`의 계산부)

**Files:**
- Create: `src/ASH.Core/Traffic/TrafficCpGenerator.cs`, `src/ASH.Core/Traffic/TrafficTable.cs`
- Create: `tests/ASH.Tests/Traffic/TrafficCpGeneratorTests.cs`

**Interfaces:**
- Consumes: Task 8 `TrafficParams`, `AutoNamer`; Task 7 `LayoutChecker.Project`; Task 5 `Rect2`.
- Produces:
  - `sealed record BlockEntry(string BlockName, string PathName, double BoundaryDist /*경로 시작부터 경계 교차점까지*/, string? CpName)`
  - `sealed class TrafficTable { List<BlockEntry> Entries; static TrafficTable Build(Layout l) /*현재 도면의 block과 경로 교차에서 다시 계산, 진입 CP는 경계 앞 가장 가까운 traffic CP*/; void WriteTsv(TextWriter w) /*열: block, path, boundary_dist, entry_cp*/; }`
  - `sealed record CpProposal(List<LayoutCp> Cps, List<string> Warnings)`
  - `static class TrafficCpGenerator { static CpProposal Generate(Layout l, TrafficParams p); }`
  - 진입 판정: 경로를 진행 방향으로 따라가며 `Rect` 밖 → 안으로 바뀌는 교차점마다 한 건. CP 위치 = 그 교차점에서 거슬러 `EffectiveClaimDist`. 거슬러 갈 거리가 부족하면 경로 시작점 + `Tolerance`에 두고 `Warnings`에 `"{path}: ClaimDist 부족, {실제거리:F3} m에 배치"`. 같은 위치(`Tolerance*10` 안)에 CP가 이미 있으면 생성하지 않는다. 경로가 사각형 안에서 시작해 안에서 끝나면 CP 없음 + 경고. block이 하나도 없으면 `Warnings = {"block이 없습니다. AGVBLOCKAUTO를 먼저 실행하세요."}`, CP 없음.
  - 간격 CP(`SpacingCps`): 각 경로의 `CpSpacing`, `2*CpSpacing`, … 위치. 곡선 CP(`CurveCps`): 각 `ArcSeg`의 시작·끝. 모두 traffic 타입, 이름 `AutoNamer.Next("T", …)`을 순서대로.

- [ ] **Step 1: 실패하는 테스트**

```csharp
public class TrafficCpGeneratorTests
{
    static readonly TrafficParams P = new(VehicleLength: 2.0, VehicleWidth: 1.0, SafetyGap: 0.5);   // ClaimDist 0.5
    static Layout CrossWithBlock() { var l = L.Of(L.Line("a",-10,0,0,0), L.Line("b",0,0,10,0), L.Line("c",0,-10,0,0), L.Line("d",0,0,0,10)); l.Blocks.Add(L.Block("B001",-3.5,-3.5,3.5,3.5)); return l; }

    [Fact] public void Entering_Paths_Get_One_Cp_Before_Boundary()
    {
        var r = TrafficCpGenerator.Generate(CrossWithBlock(), P);
        Assert.Equal(2, r.Cps.Count);                                  // a, c만 진입
        var ca = r.Cps.Single(c => Math.Abs(c.Position.Y) < 1e-9); Assert.Equal(-4.0, ca.Position.X, 9); Assert.Equal("T001", ca.Name); Assert.Equal("traffic", ca.Type);
        Assert.Empty(r.Warnings);
    }
    [Fact] public void Existing_Cp_At_Same_Spot_Is_Not_Duplicated()
    {
        var l = CrossWithBlock(); l.Cps.Add(L.Cp("T005", -4.0, 0));
        var r = TrafficCpGenerator.Generate(l, P); Assert.Single(r.Cps); Assert.Equal("T006", r.Cps[0].Name);
    }
    [Fact] public void Short_Approach_Warns_And_Clamps()
    {
        var l = L.Of(L.Line("a",-3.7,0,0,0), L.Line("b",0,0,10,0), L.Line("c",0,-10,0,0)); l.Blocks.Add(L.Block("B001",-3.5,-3.5,3.5,3.5));
        var r = TrafficCpGenerator.Generate(l, P);
        Assert.Contains(r.Warnings, w => w.StartsWith("a:")); Assert.Equal(-3.7 + 0.001, r.Cps.Single(c => Math.Abs(c.Position.Y) < 1e-9).Position.X, 6);
    }
    [Fact] public void Generate_PathFullyInsideBlock_WarnsNoCp()
    {
        var l = L.Of(L.Line("in",-1,1,1,1)); l.Blocks.Add(L.Block("B001",-3.5,-3.5,3.5,3.5));
        var r = TrafficCpGenerator.Generate(l, P); Assert.Empty(r.Cps); Assert.Contains(r.Warnings, w => w.Contains("in"));
    }
    [Fact] public void No_Blocks_Gives_Guidance_Warning() { var r = TrafficCpGenerator.Generate(L.Of(L.Line("a",0,0,10,0)), P); Assert.Empty(r.Cps); Assert.Contains("AGVBLOCKAUTO", r.Warnings[0]); }
    [Fact] public void Spacing_Cps_Every_10m()
    {
        var l = L.Of(L.Line("a",0,0,35,0)); l.Blocks.Add(L.Block("B001",100,100,101,101));
        var r = TrafficCpGenerator.Generate(l, P with { SpacingCps = true });
        Assert.Equal(new[]{10.0,20.0,30.0}, r.Cps.Select(c => c.Position.X).OrderBy(x=>x));
    }
    [Fact] public void Traffic_Table_Links_Entry_Cp()
    {
        var l = CrossWithBlock(); l.Cps.AddRange(TrafficCpGenerator.Generate(l, P).Cps);
        var t = TrafficTable.Build(l);
        Assert.Equal(2, t.Entries.Count); Assert.All(t.Entries, e => Assert.NotNull(e.CpName));
        Assert.Equal(6.5, t.Entries.Single(e => e.PathName=="a").BoundaryDist, 9);   // a 시작 (-10,0)에서 경계 x=-3.5까지
        var sw = new StringWriter(); t.WriteTsv(sw); Assert.StartsWith("block\tpath\tboundary_dist\tentry_cp", sw.ToString());
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test tests/ASH.Tests --filter FullyQualifiedName~TrafficCpGeneratorTests`
Expected: 컴파일 오류.

- [ ] **Step 3: 구현**

경계 교차: 각 세그먼트와 `Rect2`의 네 변(LineSeg)으로 `Intersections.SegSeg`, 교차점을 경로 거리로 바꿔 정렬하고 직전 샘플점의 안/밖 상태로 "밖→안"만 고른다.

- [ ] **Step 4: 통과 확인**

Run: 같은 필터. Expected: `Passed: 7`

- [ ] **Step 5: 커밋**

```bash
git add -A
git commit -m "feat(core): traffic CP auto-placement before block boundaries; traffic table"
```

---

### Task 10: 레이아웃 → AGVS `.asy` 내보내기 (`AGVEXPORT`의 계산부)

**Files:**
- Create: `src/ASH.Core/Export/ExportOptions.cs`, `src/ASH.Core/Export/LayoutExporter.cs`, `src/ASH.Core/Export/ExportDiff.cs`
- Create: `src/ASH.Core/Export/Templates/agvs_header.txt`, `agvs_vehseg.txt` (데모 `agv.asy`의 `SYSDEF`~`AGVSTOL` 줄과 `AGVVEHSEG`~`PAP` 블록을 EmbeddedResource로)
- Create: `tests/ASH.Tests/Export/LayoutExporterTests.cs`

**Interfaces:**
- Consumes: Task 2·3 `AgvsSystem`/`AsyWriter`, Task 5 `Layout`, Task 7 `LayoutChecker.Project`, Task 9 `TrafficTable`.
- Produces:
  - `sealed record VehicleGroupSpec(string Group, string TypeName, int MaxCount, double Speed /*m/s*/, double Length, double Width)`
  - `sealed record ExportOptions(string Units = "Meters", Point2 Origin = default /*모듈 A가 저장한 이동량; 좌표에서 뺀다*/, double Scale = 1.0 /*DWG 단위→Units*/, IReadOnlyList<VehicleGroupSpec>? Groups = null, string Version = "12.6.1.19", string PathType = "DefaultGuidePath", string CpType = "DefaultControlPoint")`
  - `static class LayoutExporter { static AgvsSystem ToAgvs(Layout l, ExportOptions o); static GuidePath ToGuidePath(LayoutPath p, Segment s, int index, ExportOptions o) /*세그먼트당 GPATH 하나, 이름 = 세그먼트 1개면 p.Name, 여러 개면 p.Name + "_s" + index*/; }`
  - `sealed record ExportSummary(int PathsAdded, int PathsRemoved, int PathsMoved, int CpsAdded, int CpsRemoved, int CpsMoved)`
  - `static class ExportDiff { static ExportSummary Compare(AgvsSystem? previous, AgvsSystem current) }` — 이름으로 짝 짓고 좌표가 1e-6 넘게 다르면 Moved.
  - `sealed record ExportResult(string AsyPath, string? BackupPath, string TrafficTsvPath, ExportSummary Summary)`
  - `static class ExportRunner { static ExportResult Run(Layout l, ExportOptions o, string arcDir /*<Model>.arc*/) }` — 기존 `agv.asy`가 있으면 `agv.asy.bak.yyyyMMdd_HHmmss`로 복사한 뒤 새 파일을 쓰고, `traffic.tsv`도 같은 폴더에 쓴다.
  - 매핑: `LineSeg` → `piece begx … endx …`, `ArcSeg` → `piece cenx … begx … angle round(SweepDeg*10)`. `IsSpur` → `spur crab`. CP → 가장 가까운 GPATH 조각과 그 조각 시작부터의 거리. standby/buffer CP는 GROUP별 `NAMELST name nl_<GROUP>`에 넣는다. Groups가 있으면 그룹마다 `AGVSVEH type <TypeName> numveh <MaxCount>` + `vehsegs item Seg_<TypeName>` + `start nl_<GROUP>` + 데모의 `load Default/Empty` 블록에 `vel`을 Speed로 치환, `AGVVEHSEG name Seg_<TypeName>` 블록은 템플릿 복사. `GPATHTYPE … vel 1 <maxSpeed>`는 Groups 중 최대 Speed. Groups가 없으면 데모 그대로 `DefVehicle numveh 1`, `DefSegment`.

- [ ] **Step 1: 실패하는 테스트**

```csharp
public class LayoutExporterTests
{
    [Fact] public void Line_Path_Maps_To_Single_GPATH()
    {
        var s = LayoutExporter.ToAgvs(L.Of(L.Line("a",0,0,10,0)), new ExportOptions());
        var p = Assert.Single(s.Paths); Assert.Equal("a", p.Name); Assert.Equal(new LinePiece(new(0,0), new(10,0)), p.Piece);
        Assert.Equal(new[]{"Meters","Seconds"}, s.UnitsTokens); Assert.Equal("12.6.1.19", s.Version);
    }
    [Fact] public void Ccw_Arc_Positive_Angle()
    {
        var arc = new LayoutPath("r"){ Segments = { new ArcSeg(new(0,0), new(1,0), 90) } };
        var piece = Assert.IsType<ArcPiece>(LayoutExporter.ToAgvs(L.Of(arc), new()).Paths[0].Piece); Assert.Equal(900, piece.AngleTenths);
    }
    [Fact] public void Export_ReversedArc_FlipsAngleSign()
    {
        var arc = new LayoutPath("r"){ Segments = { new ArcSeg(new(0,0), new(1,0), 90).Reversed() } };   // (0,1)→(1,0) 시계
        var piece = Assert.IsType<ArcPiece>(LayoutExporter.ToAgvs(L.Of(arc), new()).Paths[0].Piece);
        Assert.Equal(-900, piece.AngleTenths); Assert.Equal(0, piece.Beg.X, 9); Assert.Equal(1, piece.Beg.Y, 9);
    }
    [Fact] public void MultiSegment_Path_Gets_Suffixed_Pieces()
    {
        var p = new LayoutPath("m"){ Segments = { new LineSeg(new(0,0), new(5,0)), new LineSeg(new(5,0), new(5,5)) } };
        Assert.Equal(new[]{"m_s1","m_s2"}, LayoutExporter.ToAgvs(L.Of(p), new()).Paths.Select(x => x.Name));
    }
    [Fact] public void Origin_And_Scale_Applied()
    {
        var s = LayoutExporter.ToAgvs(L.Of(L.Line("a",1000,2000,2000,2000)), new ExportOptions(Origin: new(1000,2000), Scale: 0.001));  // mm 도면 → m
        Assert.Equal(new LinePiece(new(0,0), new(1,0)), s.Paths[0].Piece);
    }
    [Fact] public void Cp_Projects_To_Path_And_Distance()
    {
        var l = L.Of(L.Line("a",0,0,10,0)); l.Cps.Add(L.Cp("T001", 4, 0.01));
        var c = Assert.Single(LayoutExporter.ToAgvs(l, new()).ControlPoints); Assert.Equal("a", c.PathName); Assert.Equal(4, c.Dist, 6); Assert.Equal("DefaultControlPoint", c.Type);
    }
    [Fact] public void Standby_Cps_Make_Group_NameList_And_Vehicle()
    {
        var l = L.Of(L.Line("a",0,0,10,0)); l.Cps.Add(L.Cp("S001", 2, 0, "standby", group: "G1"));
        var s = LayoutExporter.ToAgvs(l, new ExportOptions(Groups: new[]{ new VehicleGroupSpec("G1","Tug",5,1.2,2.0,1.0) }));
        Assert.Equal(new[]{"S001"}, s.NameLists["nl_G1"]);
        Assert.StartsWith("AGVSVEH type Tug numveh 5", s.VehicleLines[0]); Assert.Contains(s.VehicleLines, x => x.Trim() == "start nl_G1");
        Assert.StartsWith("AGVVEHSEG name Seg_Tug", s.VehicleSegmentLines[0]);
    }
    [Fact] public void Diff_Counts_Added_Removed_Moved()
    {
        var prev = LayoutExporter.ToAgvs(L.Of(L.Line("a",0,0,10,0), L.Line("b",0,0,0,10)), new());
        var cur  = LayoutExporter.ToAgvs(L.Of(L.Line("a",0,0,11,0), L.Line("c",5,5,6,6)), new());
        Assert.Equal(new ExportSummary(1,1,1,0,0,0), ExportDiff.Compare(prev, cur));
    }
    [Fact] public void Runner_Backs_Up_And_Writes_Traffic_Tsv()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash_exp_" + Guid.NewGuid().ToString("N"), "m.arc"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "agv.asy"), "VERSION 12.6.1.19\nSYSTYPE AGVS\nUNITS Meters Seconds\n");
        var r = ExportRunner.Run(L.Of(L.Line("a",0,0,10,0)), new(), dir);
        Assert.NotNull(r.BackupPath); Assert.Matches(@"agv\.asy\.bak\.\d{8}_\d{6}$", r.BackupPath!); Assert.True(File.Exists(r.TrafficTsvPath));
        Assert.Contains("GPATH name a ", File.ReadAllText(r.AsyPath));
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test tests/ASH.Tests --filter FullyQualifiedName~LayoutExporterTests`
Expected: 컴파일 오류.

- [ ] **Step 3: 구현**

`Segment.Reversed()`(Task 5)가 `ArcSeg`에서 `Start = End, SweepDeg = -SweepDeg`를 돌려주는지 확인하고 아니면 여기서 고친다. 좌표 변환은 `(p - Origin) * Scale`을 세그먼트 생성 시 한 번만 적용.

- [ ] **Step 4: 통과 확인**

Run: 같은 필터. Expected: `Passed: 9`

- [ ] **Step 5: 데모 모델로 통합 확인(AM 설치 PC)**

`AmBuildIntegrationTests`에 `Exported_Cross_Layout_Builds` 추가. 데모 `agvdemo~.asy`의 `code.m`은 `nlwork`, `p_1`~`p_5`, `d_1`~`d_6`, `workcheck` 이름을 참조하므로, 십자 레이아웃에 그 이름의 CP를 만들고(`p_*`·`d_*`는 load, `workcheck`는 standby, GROUP "work", Groups 1개 `("work","DefVehicle",10,1.0,2,1)`) `LayoutExporter.ToAgvs`로 `AgvsSystem`을 얻은 뒤 테스트 안에서 `NameLists` 키 `nl_work`를 `nlwork`로 바꾸고 `VehicleLines`의 `start nl_work`도 치환한 다음 `AsyWriter.WriteFile`로 복사본의 `agv.asy`를 덮어쓰고 `AmBuilder.Build`. Expected: `Success == true`.

- [ ] **Step 6: 커밋**

```bash
git add -A
git commit -m "feat(core): layout to AGVS .asy export with backup, diff summary, traffic.tsv"
```

---

### Task 11: ZWCAD 플러그인 골격, 양식 초기화(`AGVINIT`), 도면 ↔ Layout 어댑터

**Files:**
- Create: `src/ASH.Plugin/ASH.Plugin.csproj` (`net48`, `<ZwCadDir Condition="'$(ZwCadDir)'==''">$(MSBuildThisFileDirectory)..\..\lib\zwcad</ZwCadDir>`, `ZwDatabaseMgd.dll`·`ZwManaged.dll` 참조 `Private=false`)
- Create: `src/ASH.Plugin/PluginEntry.cs` (`IExtensionApplication`), `Conventions.cs`, `Commands/InitCommands.cs`, `Cad/DrawingReader.cs`, `Cad/DrawingWriter.cs`, `Cad/XData.cs`
- Create: `lib/zwcad/README.md` ("ZWCAD 설치 폴더에서 `ZwDatabaseMgd.dll`, `ZwManaged.dll`을 여기로 복사. git 제외")
- Create: `docs/plugin-manual-test.md` (수동 테스트 기록표)

**Interfaces:**
- Consumes: Task 5 `Layout`.
- Produces:
  - `static class Conventions { const string AppName = "ASH"; const string LayerPath = "AGV_PATH"; … LayerResultPrefix = "AGV_RESULT_"; const string CpBlockName = "AGV_CP"; const string StationBlockName = "AGV_STATION"; const string DirBlockName = "AGV_DIR"; string[] CpAttributes = { "NAME","TYPE","GROUP","STATION","CAP" }; string[] BlockAttributes = { "NAME","CAP" }; }`
  - XData(regapp `ASH`) 경로 엔티티 필드 순서: `(1000) name`, `(1070) dir /*0 = 정점 순서가 진행 방향, 1 = 반대*/`, `(1000) ptype /*"path"|"spur"*/`. `static class XData { static (string Name, bool Reversed, bool IsSpur)? ReadPath(Entity e); static void WritePath(Entity e, string name, bool reversed, bool isSpur, Transaction tr); }`
  - `static class DrawingReader { static Layout Read(Database db, Transaction tr) }` — `AGV_PATH`/`AGV_PATH_SPUR` 레이어의 `Line`, `Arc`, `Polyline`(bulge → `ArcSeg`: `sweep = 4*atan(bulge)` 라디안, 양수 CCW)을 `LayoutPath`로, `dir==1`이면 세그먼트 순서를 뒤집고 각 세그먼트 `Reversed()`. `AGV_CP` 블록 참조 → `LayoutCp`(Position = 삽입점), `AGV_BLOCK` 레이어의 닫힌 폴리라인 사각형 → `LayoutBlock`(어트리뷰트는 같은 위치의 `AGV_BLOCK` 블록 참조에서, 없으면 NAME 빈 문자열), `AGV_STATION` → `LayoutStation`. 모든 `Id` = `ObjectId.Handle.ToString()`.
  - `static class DrawingWriter { static ObjectId AddCp(Database db, Transaction tr, LayoutCp cp); static ObjectId AddBlock(Database db, Transaction tr, LayoutBlock b); static void ReplacePaths(Database db, Transaction tr, IEnumerable<string> removeIds, IEnumerable<LayoutPath> add); static void RefreshDirectionArrows(Database db, Transaction tr) /*AGV_DIR 전부 지우고 각 세그먼트 중점에 진행 방향 화살표 블록 삽입*/; }`
  - 명령 `AGVINIT`: 레이어 7종(색: PATH 3, PATH_SPUR 2, DIR 8, CP 1, BLOCK 5, STATION 6, RESULT_ 미생성)과 블록 정의 `AGV_CP`(원 반지름 0.3 + 어트리뷰트 5개), `AGV_STATION`(사각 0.6 + `NAME`), `AGV_DIR`(화살표 선 3개) 생성. 이미 있으면 건너뛴다. regapp `ASH` 등록.

- [ ] **Step 1: 프로젝트 생성과 솔루션 추가**

Run:
```bash
dotnet new classlib -n ASH.Plugin -o src/ASH.Plugin -f net48
dotnet sln add src/ASH.Plugin
dotnet add src/ASH.Plugin reference src/ASH.Core
```
csproj에 `Microsoft.NETFramework.ReferenceAssemblies` 패키지(`PrivateAssets=all`)로 net48 빌드가 이 PC에서 되게 한다.

- [ ] **Step 2: `Conventions`, `XData`, `DrawingReader`, `DrawingWriter`, `AGVINIT` 구현**

bulge 변환과 `dir` 뒤집기는 Core의 `Segment.Reversed()`를 쓴다. 폴리라인 세그먼트 i: 정점 i→i+1, bulge 0이면 `LineSeg`, 아니면 중심 = 현의 중점에서 수직으로 `(1-b²)/(4b)*현길이`만큼(b>0이면 왼쪽).

- [ ] **Step 3: 빌드**

Run: `dotnet build src/ASH.Plugin -p:ZwCadDir="<dll 폴더>"`
Expected: `Build succeeded`. (dll이 없으면 참조 오류. ZWCAD PC에서 복사해 `lib/zwcad/`에 둔 뒤 다시 빌드.)

- [ ] **Step 4: 수동 테스트(ZWCAD PC)**

`NETLOAD`로 `ASH.Plugin.dll` 로드 → `AGVINIT` → 레이어 패널에 `AGV_*` 6개, `AGV_CP` 블록에 어트리뷰트 5개. 결과를 `docs/plugin-manual-test.md`에 날짜·ZWCAD 버전과 함께 기록.

- [ ] **Step 5: 커밋**

```bash
git add -A
git commit -m "feat(plugin): ZWCAD plugin skeleton, AGVINIT, drawing<->layout adapters"
```

---

### Task 12: 경로 명령 (`AGVPATH`, `AGVREVERSE`, `AGVOFFSET`, `AGVARRAY`, `AGVSPLIT`, `AGVJOIN`, `AGVFILLET`)

**Files:**
- Create: `src/ASH.Plugin/Commands/PathCommands.cs`
- Create: `src/ASH.Core/Layout/PathOps.cs`, `tests/ASH.Tests/Layout/PathOpsTests.cs`

**Interfaces:**
- Consumes: Task 6 `PathSplitter`, Task 11 `DrawingReader/Writer`, `XData`.
- Produces (Core, 테스트 가능):
  - `static class PathOps { static LayoutPath Reverse(LayoutPath p); static LayoutPath Offset(LayoutPath p, double d /*양수 = 진행 방향 왼쪽*/, bool reverseDirection); static List<LayoutPath> Array(LayoutPath p, double dx, double dy, int count, string nameSuffixStart = "_1"); static LayoutPath? Join(LayoutPath a, LayoutPath b, double tol) /*a.End≈b.Start일 때만, 이름은 a.Name*/; static (LayoutPath A, LayoutPath B, LayoutPath Fillet)? Fillet(LayoutPath a, LayoutPath b, double radius, double tol) /*두 직선 경로, a.End≈b.Start, 끝을 잘라 ArcSeg 하나짜리 경로 "<a>_f" 삽입*/; }`
- 명령:
  - `AGVPATH`: 사용자가 폴리라인을 그리는 동안 `Editor.Drag` 대신 ZWCAD 기본 `PLINE`을 `SendStringToExecute`로 실행한 뒤 `Database.ObjectAppended` 이벤트로 새 폴리라인을 잡아 `AGV_PATH` 레이어로 옮기고 XData(`name = AutoNamer.Next("P", 기존 경로 이름)`, `dir 0`, `ptype path`)를 쓴다. 옵션 `S`로 지선.
  - `AGVREVERSE`: 선택 경로의 `dir` 토글 후 `RefreshDirectionArrows`.
  - `AGVOFFSET`: 거리·방향(좌/우)·`반대 방향 설정(Y/N)` 입력 → `PathOps.Offset` → 새 엔티티.
  - `AGVARRAY`: 선택(경로·CP·block) + 간격 벡터 + 개수. CP 이름은 `AutoNamer.Next(접두어, …)`로 증가.
  - `AGVSPLIT`: 선택 없이 Enter면 `AGV_PATH` 전체. `PathSplitter.SplitAll` → 원본 삭제, 조각 추가, 요약 "교차 n개, 경로 m개 → k개" 출력. 옵션 `P`: 점 하나 찍어 그 경로만 `SplitAt`.
  - `AGVJOIN`, `AGVFILLET`: `PathOps` 결과로 엔티티 교체.

- [ ] **Step 1: `PathOpsTests` 작성**

```csharp
public class PathOpsTests
{
    [Fact] public void Reverse_Swaps_Ends() { var r = PathOps.Reverse(L.Line("a",0,0,10,0)); Assert.Equal(new Point2(10,0), r.Start); Assert.Equal(new Point2(0,0), r.End); }
    [Fact] public void Offset_Left_Of_Direction() { var o = PathOps.Offset(L.Line("a",0,0,10,0), 2, reverseDirection: true); Assert.Equal(new Point2(10,2), o.Start); Assert.Equal(new Point2(0,2), o.End); }
    [Fact] public void Array_Names_And_Positions() { var a = PathOps.Array(L.Line("a",0,0,1,0), 0, 5, 3); Assert.Equal(new[]{"a_1","a_2","a_3"}, a.Select(p => p.Name)); Assert.Equal(15, a[2].Start.Y); }
    [Fact] public void Join_Requires_Touching_Ends() { Assert.NotNull(PathOps.Join(L.Line("a",0,0,5,0), L.Line("b",5,0,10,0), 0.001)); Assert.Null(PathOps.Join(L.Line("a",0,0,5,0), L.Line("b",6,0,10,0), 0.001)); }
    [Fact] public void Fillet_Inserts_Ccw_Arc_On_Left_Turn()
    {
        var f = PathOps.Fillet(L.Line("a",0,0,10,0), L.Line("b",10,0,10,10), 2, 0.001)!.Value;
        Assert.Equal(8, f.A.End.X, 9); Assert.Equal(2, f.B.Start.Y, 9);
        var arc = Assert.IsType<ArcSeg>(f.Fillet.Segments[0]); Assert.Equal(90, arc.SweepDeg, 9); Assert.Equal(new Point2(8,2), arc.Center);
    }
}
```

- [ ] **Step 2: 실패 확인 → `PathOps` 구현 → 통과**

Run: `dotnet test tests/ASH.Tests --filter FullyQualifiedName~PathOpsTests` → Expected: `Passed: 5`

- [ ] **Step 3: `PathCommands` 구현, 빌드**

Run: `dotnet build src/ASH.Plugin` → `Build succeeded`

- [ ] **Step 4: 수동 테스트(ZWCAD PC)** — `docs/plugin-manual-test.md`에 추가

십자 모양 폴리라인 2개를 `AGVPATH`로 그림 → `AGVSPLIT` Enter → 경로 4개, 각 조각의 XData `name`이 `P001_1`, `P001_2`, `P002_1`, `P002_2`, 화살표가 원래 방향. `AGVREVERSE`로 하나 뒤집고 화살표 방향 확인. `AGVFILLET` 반경 1 → 원호 삽입.

- [ ] **Step 5: 커밋**

```bash
git add -A
git commit -m "feat(plugin): path drawing and editing commands backed by Core PathOps"
```

---

### Task 13: CP 명령 (`AGVCP` 연속/상세 모드) 와 팔레트

**Files:**
- Create: `src/ASH.Plugin/Commands/CpCommands.cs`, `src/ASH.Plugin/Ui/AgvPalette.cs` (`PaletteSet` + WinForms `UserControl`), `src/ASH.Plugin/Ui/CpSettings.cs`
- Create: `src/ASH.Core/Traffic/CpNaming.cs`, `tests/ASH.Tests/Traffic/CpNamingTests.cs`

**Interfaces:**
- Consumes: Task 8 `AutoNamer`, Task 7 `LayoutChecker.Project`, Task 11 `DrawingWriter.AddCp`.
- Produces:
  - `sealed class CpSettings { string Type = "traffic"; string Group = ""; string Station = ""; string Prefix = "T"; int Cap = 1; bool DetailedMode = false; }` — 팔레트 상태. `Type` 변경 시 `Prefix = AutoNamer.PrefixFor(Type)`.
  - `sealed class CpNaming { CpNaming(IEnumerable<string> existingNames); string Next(string prefix) /*접두어별 카운터, 호출마다 증가*/; }`
  - `AGVCP`: 루프 — 점 입력(Enter/Esc 종료) → `LayoutChecker.Project`로 가장 가까운 경로에 투영(허용 `CpMaxOffset` 0.05 m 밖이면 "경로에서 너무 멉니다" 후 계속) → 연속 모드면 `CpSettings` 값 + `CpNaming.Next(Prefix)`로 즉시 삽입, 상세 모드면 어트리뷰트 대화상자(ZWCAD 기본 `ATTDIA` 동작: 삽입 후 `-ATTEDIT` 대신 `Editor.GetString` 5회) 후 삽입.
  - 팔레트 `AgvPalette`: 버튼 = 양식 초기화, 경로(7개), CP 모드 전환·TYPE/GROUP/STATION/접두어 입력, 교통제어 자동 생성(Task 14), 검증, 내보내기. 각 버튼은 `Document.SendStringToExecute("<명령> ", true, false, true)`.

- [ ] **Step 1: `CpNamingTests`**

```csharp
public class CpNamingTests
{
    [Fact] public void Counters_Are_Per_Prefix() { var n = new CpNaming(new[]{"T003","L001"}); Assert.Equal("T004", n.Next("T")); Assert.Equal("T005", n.Next("T")); Assert.Equal("L002", n.Next("L")); Assert.Equal("S001", n.Next("S")); }
}
```

- [ ] **Step 2: 실패 확인 → 구현 → 통과**

Run: `dotnet test tests/ASH.Tests --filter FullyQualifiedName~CpNamingTests` → `Passed: 1`

- [ ] **Step 3: `CpCommands`, `AgvPalette` 구현, 빌드**

Run: `dotnet build src/ASH.Plugin` → `Build succeeded`

- [ ] **Step 4: 수동 테스트(ZWCAD PC)**

팔레트 열기(`AGVPALETTE`) → TYPE load, GROUP G1 설정 → `AGVCP`로 경로 위 3번 클릭 → `L001`, `L002`, `L003`이 경로 위에 투영되어 삽입, 어트리뷰트 GROUP=G1. TYPE을 traffic으로 바꾸면 접두어 `T`로 바뀌고 다음 이름 `T001`.

- [ ] **Step 5: 커밋**

```bash
git add -A
git commit -m "feat(plugin): AGVCP continuous/detailed modes and side palette"
```

---

### Task 14: 자동 교통제어·검증·내보내기 명령 (`AGVBLOCKAUTO`, `AGVCPAUTO`, `AGVTRAFFIC`, `AGVCHECK`, `AGVEXPORT`)

**Files:**
- Create: `src/ASH.Plugin/Commands/TrafficCommands.cs`, `Commands/CheckCommands.cs`, `Commands/ExportCommands.cs`, `src/ASH.Plugin/Settings/ProjectSettings.cs`
- Create: `src/ASH.Core/Settings/SettingsFile.cs`, `tests/ASH.Tests/Settings/SettingsFileTests.cs`

**Interfaces:**
- Consumes: Task 8 `BlockGenerator`, Task 9 `TrafficCpGenerator`, Task 7 `LayoutChecker`, Task 10 `ExportRunner`, Task 11 어댑터.
- Produces:
  - `sealed record ProjectSettings(double SafetyGap = 0.5, double CpSpacing = 10, double? ClaimDist = null, double? BlockReach = null, double VehicleLength = 2.0, double VehicleWidth = 1.0, string Units = "Meters", double Scale = 1.0, Point2 Origin = default, string ModelName = "model")` — 2차 계획의 Excel Settings 시트가 생기기 전까지 도면 옆 `ash.settings.tsv`(키\t값)에서 읽는다. `static class SettingsFile { static ProjectSettings Load(string path) /*없으면 기본값*/; static void Save(ProjectSettings s, string path); }`
  - `AGVBLOCKAUTO`: `DrawingReader.Read` → `BlockGenerator.Generate(layout, TrafficParams(settings))` → block 사각형(닫힌 폴리라인 + `AGV_BLOCK` 어트리뷰트 블록) 삽입. `OverlapGroups`가 있으면 묶음마다 "B001, B002 합칠까요? [Y/N]"을 `Editor.GetKeywords`로 묻고 Y면 `Merge`.
  - `AGVCPAUTO`: `TrafficCpGenerator.Generate` → CP 삽입, `Warnings`를 명령줄에 출력.
  - `AGVTRAFFIC`: 위 둘을 순서대로 실행(팔레트 "교통제어 자동 생성").
  - `AGVCHECK`: `LayoutChecker.Check` 결과를 명령줄에 `[코드] 메시지 (x, y)`로 출력하고 첫 항목으로 `Editor.ZoomWindow`(폭 10 m). `UnsplitIntersection`이 있으면 "지금 분할할까요? [Y/N]" → Y면 `AGVSPLIT` 실행 후 재검사.
  - `AGVEXPORT`: `ExportRunner.Run(layout, ExportOptions(settings), <도면 폴더>\<ModelName>.arc)` → `Process.Start(asyPath)`로 기본 텍스트 편집기 열기, `Summary`를 "경로 +a −r ~m, CP +a −r ~m" 형식으로 출력. 검증 `Finding` 중 `DanglingEnd`·`UnsplitIntersection`·`DuplicateCpName`이 있으면 내보내기 전에 "오류 n건이 있습니다. 계속할까요? [Y/N]".

- [ ] **Step 1: `SettingsFileTests`**

```csharp
public class SettingsFileTests
{
    [Fact] public void Missing_File_Gives_Defaults() => Assert.Equal(new ProjectSettings(), SettingsFile.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".tsv")));
    [Fact] public void Save_Then_Load_RoundTrips()
    {
        var p = Path.GetTempFileName(); var s = new ProjectSettings(SafetyGap: 0.8, ClaimDist: 1.0, Origin: new(100,200), ModelName: "plantA");
        SettingsFile.Save(s, p); Assert.Equal(s, SettingsFile.Load(p));
    }
}
```

- [ ] **Step 2: 실패 확인 → 구현 → 통과**

Run: `dotnet test tests/ASH.Tests --filter FullyQualifiedName~SettingsFileTests` → `Passed: 2`

- [ ] **Step 3: 명령 구현, 빌드**

Run: `dotnet build` (솔루션 전체) → `Build succeeded`, `dotnet test tests/ASH.Tests` → 전체 통과(통합 테스트는 `ASH_AM_HOME` 없으면 Skipped).

- [ ] **Step 4: 수동 테스트(ZWCAD PC)** — 끝에서 끝까지

십자 경로 4개(`AGVSPLIT` 완료) + standby CP 1개(GROUP G1) → `AGVTRAFFIC` → block `B001` 사각형 1개와 traffic CP 2개(`T001`, `T002`)가 진입 경로에만 생김 → `AGVCHECK` 결과 0건(지선 없음, 끝 4개는 DanglingEnd로 보고됨: 예상 4건) → `AGVEXPORT` → 메모장에 `agv.asy`가 열리고 `GPATH` 4줄, `CPOINT` 3줄, `NAMELST name nl_G1 item S001`, 같은 폴더에 `traffic.tsv` 2행. 이 `.arc`를 AM 편집기에서 열어 경로·CP가 보이는지 확인.

- [ ] **Step 5: 커밋**

```bash
git add -A
git commit -m "feat(plugin): auto traffic control, check, and export commands"
```

---

### Task 15: 성능 확인 (CP 1000개·경로 수천 개)

**Files:**
- Create: `tests/ASH.Tests/Perf/LargeLayoutTests.cs`

- [ ] **Step 1: 합성 레이아웃 테스트**

```csharp
public class LargeLayoutTests
{
    static Layout Grid(int n /*n×n 격자, 가로·세로 통로*/) { var l = new Layout(); for (int i = 0; i <= n; i++) { l.Paths.Add(L.Line("h"+i, 0, i*10, n*10, i*10)); l.Paths.Add(L.Line("v"+i, i*10, 0, i*10, n*10)); } return l; }
    [Fact] public void Split_Check_Export_On_40x40_Grid_Under_5s()
    {
        var sw = Stopwatch.StartNew();
        var l = Grid(40);                                                       // 82개 경로, 1681 교차
        l.Paths = PathSplitter.SplitAll(l.Paths, 0.001).Paths;                  // 약 3400 조각
        var blocks = BlockGenerator.Generate(l, new(2,1)); l.Blocks.AddRange(blocks.Blocks);
        l.Cps.AddRange(TrafficCpGenerator.Generate(l, new(2,1)).Cps);           // 교차마다 진입 CP
        var findings = LayoutChecker.Check(l, new());
        var sys = LayoutExporter.ToAgvs(l, new());
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), sw.Elapsed.ToString());
        Assert.True(l.Cps.Count > 1000); Assert.Equal(l.Paths.Count, sys.Paths.Count);
    }
}
```

- [ ] **Step 2: 실행**

Run: `dotnet test tests/ASH.Tests --filter FullyQualifiedName~LargeLayoutTests`
Expected: `Passed: 1`. 5초를 넘으면 `SpatialGrid` 셀 크기와 `Project`의 후보 축소를 프로파일링해 고친다(O(n²) 루프가 남아 있는 곳을 먼저 본다).

- [ ] **Step 3: 커밋**

```bash
git add -A
git commit -m "test(core): large-layout performance guard"
```

---

## 설계서 보정 (이 계획에서 반영한 불일치)

1. 설계서 7.2 "교통제어" 항목의 "구간을 벗어난 CP에서 `release`"는 6.1·6.2의 "경계 이탈 시 AM 자동 release"와 어긋난다. 자동 release가 사용자 확인 사항이므로 7.2는 2차 계획서에서 "진입 CP에서 `claim`, release는 AM 자동"으로 고친다.
2. 설계서 12장 "기존 명령은 그대로 동작해야 한다"는 3장의 "재작성, 명령 호환 불요"와 어긋난다. 3장을 따른다.
3. .NET 8 → .NET 10(Tests/Runner). 이 PC의 SDK 때문이며 Core는 netstandard2.0이라 영향 없다.
4. 설계서 11장 5번(원호 부호)은 데모 분석으로 해결: 음수 = 시계 방향. Task 2·10의 테스트가 고정한다.
