# 공급 방식·위치 순환·공급 대기 통계 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 그룹별 공급 방식(호출/순환), 상차·하차 CP 목록(순환), 공급 대기 수량과 순환 그룹 교체 시간 통계를 입력부터 결과 화면까지 넣는다.

**Architecture:** 엑셀에서 하차 CP 목록은 공정 자재(`M001@U002`)로 펼치고, 상차 CP 목록은 Route에 둔다. 로직 생성기는 호출 자재에 상차 차례와 대기 카운터를 더한다. 순환 그룹은 차량마다 끝없이 도는 새 프로시저를 가진다. 순환 그룹은 대수마다 그 대수만큼만 차량을 넣은 모델 변형을 빌드해 돌린다(쓰지 않는 차량이 경로를 막지 않게). 판정은 순환 그룹에 한해 뒤차 대기율로 한다.

**Tech Stack:** C# (Core netstandard2.0 / Plugin net48 / Server net10), ClosedXML, xUnit, AutoMod 14.5 로직(.m) 템플릿.

**Spec:** `docs/superpowers/specs/2026-10-29-supply-modes-design.md`

## Global Constraints

- CP 하나짜리 호출 공급 자재의 결과는 바뀌면 안 된다. 생성 로직은 대기 카운터 줄만 늘어나도 된다.
- 기존 엑셀(공급 방식 열 없음, 쉼표·범위 없음)은 그대로 읽힌다. 빈 공급 방식 = `Call`.
- 공정 자재 이름: `{Material}@{UnloadCP}`. 하차 CP가 하나면 펼치지 않는다.
- 범위: `-` 양쪽이 같은 비어 있지 않은 접두어(숫자가 아닌 글자로 끝남) + 숫자일 때만 범위이고, 자릿수는 시작 쪽을 따른다. 그 밖에는 이름 하나다.
- 순환 권장 기준: 뒤차 대기율 ≥ `Settings.CycleFollowRate`(기본 0.95).
- 뒤차 대기 판정 간격: `(차량 Length + LayoutExporter.StopGapM) / SpeedLoaded + 5` 초 이하.
- UI 문구는 문장 대신 단어 위주로 쓴다. "결품" 대신 "자재 품절"을 쓴다.
- Core는 netstandard2.0 / net48에서도 빌드돼야 한다. 새 NuGet 의존성은 넣지 않는다.
- 한글 파일은 BOM 상태를 보존한다. PowerShell `Set-Content`는 쓰지 않는다.
- 커밋 메시지는 `git commit -F <파일>`로 넘긴다. 끝에 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`을 단다.

## Review Focus

1. CP 이름에 `-`가 든 기존 입력(`A1-B2`, `L001-1`), 쉼표 주위 공백·중복(`L001, L001 ,L002`) → 이름 하나 또는 중복 경고로 읽혀야 한다. Task 1 테스트에서 고정한다.
2. 새 열이 없는 옛 result.tsv(자재 행 값 8개) → 새 통계는 NaN이고 화면은 "-"로 보인다. Task 4 파서 테스트, Task 8 화면 테스트에서 고정한다.
3. 순환 그룹인데 교체가 한 번도 없는 실행(활성 0대, 시뮬레이션이 짧음) → 뒤차 대기율 NaN이면 판정은 "교체 없음"으로 실패하고 0으로 나누지 않는다. Task 7에서 고정한다.
4. 펼친 이름이 다른 자재 이름과 겹치거나, 컨베이어 공급·Output 자재에 하차 CP가 여러 개 → 입력 오류여야 한다. Task 3에서 고정한다.
5. 이어하기(Reuse)·영상(FilmWorker)이 순환 그룹을 기본 모델로 돌리는 경우 → 반드시 같은 대수 변형 모델을 써야 한다. Task 6에서 고정한다.

---

### Task 1: CP 목록 파서

**Files:**
- Create: `src/ASH.Core/Input/CpList.cs`
- Test: `tests/ASH.Tests/Input/CpListTests.cs`

**Interfaces:**
- Produces: `public static List<string> CpList.Parse(string? cell, List<string> errors, List<string> duplicates)`
  - 쉼표로 나누고 각 토큰을 Trim한다. 빈 토큰은 버린다. 범위를 펼친다.
  - 두 번째로 나온 이름은 버리고 `duplicates`에 넣는다.
  - 시작이 끝보다 큰 범위, 1000개를 넘는 범위는 토큰을 `errors`에 넣는다.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
[Theory]
[InlineData("L001", "L001")]
[InlineData("L001-L003,L007", "L001|L002|L003|L007")]
[InlineData("L008-L010", "L008|L009|L010")]
[InlineData("L9-L11", "L9|L10|L11")]
[InlineData("A1-B2", "A1-B2")]
[InlineData("L001-1", "L001-1")]
[InlineData("", "")]
public void Parse_expands_ranges_and_keeps_other_names(string cell, string expected)
{
    var e = new List<string>(); var d = new List<string>();
    Assert.Equal(expected, string.Join("|", CpList.Parse(cell, e, d)));
    Assert.Empty(e); Assert.Empty(d);
}

[Fact] public void Reversed_range_is_an_error()
{ var e = new List<string>(); Assert.Empty(CpList.Parse("L010-L001", e, new List<string>())); Assert.Equal(new[] { "L010-L001" }, e); }

[Fact] public void Duplicates_are_dropped_and_reported()
{ var d = new List<string>(); Assert.Equal(new[] { "L001", "L002" }, CpList.Parse("L001, L001 ,L002", new List<string>(), d)); Assert.Equal(new[] { "L001" }, d); }
```

- [ ] **Step 2: 실패 확인.** `dotnet test tests\ASH.Tests -f net10.0 --filter FullyQualifiedName~CpListTests` → 컴파일 오류(CpList 없음).
- [ ] **Step 3: `CpList.Parse` 구현.** 범위 정규식: `^(?<p>.*\D)(?<a>\d+)-(?<q>.*\D)(?<b>\d+)$`. `p == q`일 때만 범위이고, 자릿수는 `a.Length`로 맞춘다(`ToString("D" + a.Length)`).
- [ ] **Step 4: 통과 확인.** 같은 명령 → PASS. net48도 `-f net48`로 확인한다.
- [ ] **Step 5: 커밋.** "CP 목록 파서(쉼표·범위)"

### Task 2: 입력 모델, 엑셀 읽기·양식, 공정 펼치기

**Files:**
- Modify: `src/ASH.Core/Input/ProjectInput.cs`
- Modify: `src/ASH.Core/Input/ExcelReader.cs` (그룹 150행 근처, 루트 187행, EmptyReturn 206-210행 뒤)
- Modify: `src/ASH.Core/Input/ExcelTemplate.cs` (AGV_Groups 열 23행, 목록 72행, `ApplyCpLists` 747행)
- Modify: `src/ASH.Core/Model/InputSignature.cs`
- Create: `src/ASH.Core/Input/StationExpansion.cs`
- Test: `tests/ASH.Tests/Input/StationExpansionTests.cs`, `tests/ASH.Tests/Input/ExcelReaderTests.cs`(기존 파일에 추가)

**Interfaces:**
- Consumes: `CpList.Parse` (Task 1)
- Produces:
  - `AgvGroup(..., string? FinishCP = null, string SupplyMode = "")`, `bool AgvGroup.IsCycle` (`"Cycle"`, 대소문자 무시)
  - `Route(..., double BufferWaitSec = 0, IReadOnlyList<string>? SupplyFromCPs = null)`, `IReadOnlyList<string> Route.FromCps` (null이면 `[SupplyFromCP]`). `SupplyFromCP`는 늘 첫 CP다.
  - `MaterialInfo(..., bool ConveyorFed = false, string Source = "")`, `string MaterialInfo.SourceName` (Source가 비면 Material)
  - `Settings.CycleFollowRate` (double, 기본 0.95)
  - `static void StationExpansion.Expand(ProjectInput p, IDictionary<string, List<string>> unloadLists)`: 키 = 원래 자재 이름, 값 = 하차 CP 목록(2개 이상만). 자재 행과 Route를 CP마다 하나씩, 원래 위치에 순서대로 바꿔 넣는다. Row는 그대로 둔다.
  - 읽기 오류: CpList의 errors / duplicates를 리더의 기존 경고 목록(`ExcelReader.Read(wb, out warnings)`)에 넣는다. errors는 Task 3이 입력 오류로 올린다. 리더는 `ProjectInput.CpListErrors`(List<(string Sheet, int Row, string Col, string Token)>)에 모은다.

- [ ] **Step 1: 실패하는 테스트 작성**
  - `Expand_makes_one_station_material_per_unload_cp`
    - 입력: M001(TaktTime 30, Row 3)과 그 Route, `unloadLists["M001"] = [U001, U002]`
    - 결과:
      - 자재 이름 `["M001@U001","M001@U002"]`
      - 둘 다 Source `"M001"`, TaktTime 30, Row 3
      - Route.SupplyToCP 각각 `U001` / `U002`
  - `Single_unload_cp_is_not_expanded`: 목록에 없는 자재는 이름 그대로 남는다.
  - ExcelReader:
    - SupplyFromCP `"L001-L002"` → `Route.FromCps == [L001, L002]`, `SupplyFromCP == "L001"`
    - AGV_Groups `SupplyMode` 열이 없는 옛 통합문서 → `IsCycle == false`
    - `SupplyMode` 열에 `Cycle` → `IsCycle == true`
  - ExcelTemplate:
    - 새 양식 AGV_Groups 머리글에 `SupplyMode`(라벨 "공급 방식", 예시 `Call`)와 목록 `Call,Cycle`이 있다.
    - Settings 시트에 `CycleFollowRate`(라벨 "순환 뒤차 대기율", 0.95)가 있다.
  - InputSignature: Call만 쓰는 입력의 서명은 바뀌지 않는다. Cycle 그룹이나 상차 목록이 있으면 서명이 달라진다.
- [ ] **Step 2: 실패 확인.** `dotnet test tests\ASH.Tests -f net10.0 --filter "FullyQualifiedName~StationExpansion|FullyQualifiedName~ExcelReader|FullyQualifiedName~ExcelTemplate|FullyQualifiedName~InputSignature"`
- [ ] **Step 3: 구현.**
  - 리더는 SupplyFromCP / SupplyToCP 칸을 `CpList.Parse`로 읽는다.
  - `Expand`는 EmptyReturn과 ConveyorFed를 정한 뒤 리더 끝에서 부른다. 펼친 사본이 그 값을 물려받게 하기 위해서다.
  - `ApplyCpLists`는 지금 `type == "standby"`에만 있는 "쉼표가 있으면 드롭다운 제거" 규칙을 SupplyFromCP / SupplyToCP에도 적용한다. 조건은 값에 `,`가 있거나 범위 모양일 때다.
  - InputSignature는 Cycle일 때만 `SupplyMode`를 넣고, 목록일 때만 FromCps 전체를 넣는다.
- [ ] **Step 4: 통과 확인.** 같은 명령 + 전체 `tests\ASH.Tests` net10/net48 → PASS.
- [ ] **Step 5: 커밋.** "입력: 공급 방식 열, CP 목록, 공정 펼치기"

### Task 3: 입력 검사

**Files:**
- Modify: `src/ASH.Core/Input/InputValidator.cs`
- Test: `tests/ASH.Tests/Input/InputValidatorTests.cs`(기존 파일에 추가)

**Interfaces:**
- Consumes: `AgvGroup.IsCycle`, `MaterialInfo.SourceName`, `Route.FromCps`, `ProjectInput.CpListErrors` (Task 2)

- [ ] **Step 1: 실패하는 테스트 작성.** 각 경우의 기대 결과는 다음과 같다(메시지 문자열 그대로).
  - Cycle 그룹에 서로 다른 SourceName 자재가 2개 → 오류, AGV_Groups 행. `그룹 'G2'은(는) 순환(Cycle)인데 자재가 2개입니다. 순환 그룹에는 자재 하나만 둡니다.`
  - Cycle 그룹에 자재가 0개 → 같은 문장, 자재 0개.
  - Cycle 그룹 StandbyCPs가 비어 있지 않음 → 경고 `그룹 'G2'은(는) 순환이라 대기 CP를 쓰지 않습니다.` FinishCP도 같은 꼴(`완료 CP`).
  - SupplyMode가 `Call` / `Cycle` / 빈칸이 아님 → 오류 `그룹 'G2'의 공급 방식 'X'은(는) Call 또는 Cycle입니다.`
  - CpListErrors 토큰 → 오류 `'{token}': 범위 시작이 끝보다 큽니다.` (해당 칸)
  - 펼친 이름이 기존 자재 이름과 같음 → 오류 `공정 자재 이름 'M001@U002'이(가) 다른 자재 이름과 같습니다.`
  - ConveyorFed이거나 IsOutput인 자재에 하차 CP가 여러 개 → 오류 `자재 'M001'은(는) 컨베이어/반출 자재라 하차 CP를 하나만 씁니다.`
  - 상차 목록의 각 CP → 지금 SupplyFromCP에 하는 이름·TYPE 검사를 CP마다 한다. 도면에 없는 `L009` → 기존 메시지 그대로 `L009`를 가리키는 오류.
- [ ] **Step 2: 실패 확인.** `--filter FullyQualifiedName~InputValidator`
- [ ] **Step 3: 구현.** 순환 차례(:36행 `cycle` 검사)는 `FromCps[0]`과 해당 공정의 SupplyToCP로 계산한다(첫 CP 대표).
- [ ] **Step 4: 통과 확인**
- [ ] **Step 5: 커밋.** "입력 검사: 순환 그룹, CP 목록, 공정 이름"

### Task 4: 로직 공통(상차 차례, 공급 대기 카운터)과 결과 행 확장

**Files:**
- Modify: `src/ASH.Core/Model/LogicGenerator.cs` (재료 루프 104-180행, 결과 출력 185-188행, 변수 목록 426행)
- Modify: `src/ASH.Core/Model/Templates/code_material.m.txt`
- Modify: `src/ASH.Core/Runner/ResultSet.cs`, `src/ASH.Core/Runner/ResultParser.cs` (47행)
- Test: `tests/ASH.Tests/Model/LogicGeneratorTests.cs`, `tests/ASH.Tests/Runner/ResultParserTests.cs`

**Interfaces:**
- Consumes: `Route.FromCps` (Task 2)
- Produces:
  - 로직 변수(자재 배열): `V_waitQty, V_waitVeh, V_waitSum, V_waitVehSum, V_waitMax, V_fromRot, V_swapN, V_swapSum, V_swapMax, V_follow, V_lastLeave, V_swapOpen`
  - 결과 자재 행 값: V(8) 공급 대기 평균(용기), V(9) 최대, V(10) 대기 차량 평균, V(11) 교체 횟수, V(12) 교체 시간 합(초), V(13) 교체 최대(초), V(14) 뒤차 대기 횟수. 호출 자재는 11-14가 0이다.
  - `MaterialResult(..., double WaitAvg = double.NaN, double WaitMax = double.NaN, double WaitVehAvg = double.NaN, double SwapN = double.NaN, double SwapSum = double.NaN, double SwapMax = double.NaN, double FollowN = double.NaN)`. 값이 없는 옛 행은 NaN이다.
  - 생성 헬퍼: `static string LogicGenerator.HoldBlock(string idx, string holdTail)`: 0.36.0의 `wait` 문자열을 배열 인덱스 `idx`(호출 자재는 `"1"` 같은 숫자, 순환은 `"A_m"`)로 만들고, 앞뒤에 `inc/dec V_waitQty(idx) by A_qty`, `inc/dec V_waitVeh(idx) by 1`을 두른다. Task 5도 쓴다.

- [ ] **Step 1: 실패하는 테스트 작성**
  - 상차 CP가 2개(L001, L002)인 Route:
    - supply 구간에 `inc V_fromRot(1) by 1`이 있다.
    - `if A_from = 1 then begin`… `travel to agv:L001`, `else` … `travel to agv:L002`가 있다.
    - 상차 CP가 하나인 Sample에는 `V_fromRot`가 없다.
  - 대기 카운터: hold 블록이 `inc V_waitQty(1) by A_qty\n\tinc V_waitVeh(1) by 1`로 시작하고, `while` 뒤에 `dec V_waitQty(1) by A_qty\n\tdec V_waitVeh(1) by 1`이 있다. 버퍼가 있을 때와 없을 때 둘 다 확인한다.
  - 소비 루프의 표본 줄: `inc V_waitSum(1) by V_waitQty(1)`, `inc V_waitVehSum(1) by V_waitVeh(1)`, `if V_waitQty(1) > V_waitMax(1) then set V_waitMax(1) to V_waitQty(1)`
  - 단일 CP 호출 자재: 호출 `while` 줄과 `inc V_stock` 줄이 0.36.0과 글자까지 같다.
  - 파서:
    - `material\tM001\tG1\t1\t2\t3\t4\t5\t6\t7\t8` (값 8개) → `WaitAvg` NaN
    - 값 15개 행 → WaitAvg = V(8) … FollowN = V(14)
- [ ] **Step 2: 실패 확인.** `--filter "FullyQualifiedName~LogicGenerator|FullyQualifiedName~ResultParser"`
- [ ] **Step 3: 구현.**
  - 상차 차례는 원래 자재 행 단위로 하나다. 공정 자재들은 SourceName이 같은 첫 자재의 mi를 인덱스로 같이 쓴다.
  - 공급 작업 시작 때 `A_from`을 정하고, PICK_TRAVEL과 상차 구간을 if 사슬로 만든다.
  - 결과 줄은 185-188행 `material` print에 값 7개를 덧붙인다.
- [ ] **Step 4: 통과 확인.** 기존 GeometryTests / LogicGeneratorTests 전부 포함 PASS.
- [ ] **Step 5: 커밋.** "로직: 상차 순환, 공급 대기 카운터, 결과 행 확장"

### Task 5: 순환 그룹 로직과 시작 CP

**Files:**
- Create: `src/ASH.Core/Model/Templates/code_cycle.m.txt` (리소스 등록은 기존 템플릿 방식대로)
- Modify: `src/ASH.Core/Model/LogicGenerator.cs`, `src/ASH.Core/Model/Templates/code_material.m.txt` (호출 블록을 `{{CALL}}` 자리로 옮김)
- Modify: `src/ASH.Core/Model/ModelBuilder.cs` (129행 `VehicleGroupSpec` 생성), `src/ASH.Core/Export/LayoutExporter.cs` (`AddStartLists` 190행)
- Test: `tests/ASH.Tests/Model/CycleLogicTests.cs`, `tests/ASH.Tests/Export/LayoutExporterTests.cs`

**Interfaces:**
- Consumes: `AgvGroup.IsCycle`, `MaterialInfo.SourceName`, `LogicGenerator.HoldBlock` (Task 2, 4)
- Produces:
  - 그룹 인덱스 g마다 프로시저 `P_cycle_{g}`, 로드 타입은 그 그룹 첫 공정 자재의 `L_m{mi}`
  - 그룹 배열 변수 `V_toRot`
  - `VehicleGroupSpec(..., string? CycleStartCp = null)`: Cycle 그룹이면 첫 상차 CP 이름
  - 시작 CP 이름 `SP_{group}_{k}` (k = 1..MaxCount). 첫 상차 CP에서 들어오는 차선을 따라 뒤로 `차량 길이 + StopGapM` 간격으로 둔다. 기존 `StandbyQueueCheck.LaneSpots`를 쓴다.

- [ ] **Step 1: 실패하는 테스트 작성** (G2 = Cycle, 자재 M001, 하차 U001 / U002로 펼침)
  - 공정 자재 `P_consume_*`에 `clone 1 load to P_supply_`이 없다. 호출 블록이 없다.
  - `begin P_cycle_2 arriving procedure`가 있다.
  - 공정 차례:
    - `inc V_toRot(2) by 1`
    - `set A_m to` 사슬
    - `travel to agv:U001` / `travel to agv:U002` 분기
  - 하차 도착 직후 교체 기록:
    - `if V_swapOpen(A_m) = 1 then begin`
    - `set V_x to ac - V_lastLeave(A_m)`
    - `inc V_swapN(A_m) by 1`
    - `if V_x <= {FOLLOW} then inc V_follow(A_m) by 1`
    - FOLLOW는 Global Constraints 식. 테스트값: Length 1.2, StopGapM, SpeedLoaded 1.0
  - 하차 뒤: `set V_lastLeave(A_m) to ac`, `set V_swapOpen(A_m) to 1`
  - hold는 `HoldBlock`을 `A_m` 인덱스로 쓴다. `while V_stock(A_m) > V_unload(A_m) do begin`
  - 시작 부분: 활성 대수만큼 로드를 만들고(`V_active(2)`), 로드 k는 `SP_G2_k`로 들어간다(`move into agv:SP_G2_1` 등의 if 사슬).
  - 워밍업 이전에는 교체·가동 통계를 쌓지 않는다(`if ac >= V_warmup` 안에 있음).
  - LayoutExporter: Cycle 그룹(MaxCount 3, StandbyCPs 비어 있음) → 시작 목록이 `SP_G2_1..3`이고 첫 상차 CP 뒤 차선 위에 있다. Call 그룹 시작 목록은 그대로다.
- [ ] **Step 2: 실패 확인.** `--filter "FullyQualifiedName~CycleLogic|FullyQualifiedName~LayoutExporter"`
- [ ] **Step 3: 구현.**
  - 한 바퀴의 순서:
    1. 바퀴 시작에 `A_tCall`, `A_tPick`을 정한다.
    2. 다음 상차 CP(Task 4 차례)로 가서 LoadTime 동안 싣는다(`A_qty` = Capacity).
    3. 다음 공정을 고른다.
    4. 버퍼가 있으면 버퍼로 가서 HoldBlock, 그다음 하차 CP. 버퍼가 없으면 하차 CP로 가서 HoldBlock.
    5. 교체를 기록한다.
    6. UnloadTime 뒤 `inc V_stock(A_m)` 하고 공급 통계를 쌓는다.
    7. 하차를 기록하고 회수 구간을 지난다.
    8. 가동·시간 분해를 P_supply와 같은 식으로 쌓는다. A_fin = 바퀴 끝이다.
    9. 1로 돌아간다.
  - T_pick / T_deliv는 바퀴 시작 기준으로 잰다.
  - P_supply의 Leg / Fail / 속도 헬퍼를 그대로 쓴다.
- [ ] **Step 4: 통과 확인**
- [ ] **Step 5: 커밋.** "순환 그룹 로직과 시작 CP"

### Task 6: 대수별 모델 변형

**Files:**
- Create: `src/ASH.Core/Runner/ModelVariants.cs`
- Modify: `src/ASH.Core/Model/SourceBuild.cs` (`Run`에 입력 조정 인자), `src/ASH.Core/Runner/ModelExecutor.cs`, `src/ASH.Server/FilmWorker.cs`
- Test: `tests/ASH.Tests/Runner/ModelVariantsTests.cs`, `tests/ASH.Tests/Runner/ModelExecutorTests.cs`

**Interfaces:**
- Consumes: `AgvGroup.IsCycle` (Task 2)
- Produces:
  - `static string ModelVariants.NameFor(ProjectInput input, string model, RunScenario s)`
    - 순환 그룹이 없으면 `model`
    - 있으면 `model + "_c" + string.Join("x", 순환 그룹 대수(그룹 순서))`. 예: `Agv_c3`, `Agv_c3x2`
  - `static void ModelVariants.Ensure(string projectDir, string baseModel, string variant, ProjectInput input, RunScenario s, string? amHome, TimeSpan timeout)`
    - 변형마다 잠금을 잡고 한 번만 만든다.
    - `<projectDir>\<variant>.dir`가 이미 빌드돼 있으면 건너뛴다.
    - `SourceBuild.Run(projectDir, variant, cel, warnings, adjust)`: adjust는 순환 그룹의 MinCount = MaxCount = 그 대수로 바꾼다. cel은 `<baseModel>.arc\assets\<name>.cel`을 읽는다.
    - 그다음 `AmBuilder.Build`. 실패하면 `InvalidOperationException`을 던지고 메시지에 `Facts`를 담는다.
    - 원본 파일(layout.tsv, input.cells.tsv, ash.settings.tsv)이 없으면 `InvalidOperationException("순환 그룹 대수별 모델을 만들 원본이 없습니다: …")`
  - `SourceBuild.Run(..., Func<ProjectInput, ProjectInput>? adjust = null)`
  - `ModelExecutor(ExecutorOptions a, ProjectInput input, Func<RunRequest, RunOutcome>? runner = null, Func<RunScenario, string>? modelFor = null)`
    - 기본 modelFor는 `NameFor` + `Ensure`
    - 작업 슬롯은 `(slot, model)`마다 처음 쓸 때 `PrepareWorkSlot`으로 만든다(잠금).

- [ ] **Step 1: 실패하는 테스트 작성**
  - `NameFor`:
    - Call만 → `"Agv"`
    - G2 Cycle 3대 → `"Agv_c3"`
    - G2 3대, G3 2대 → `"Agv_c3x2"`
  - ModelExecutor (가짜 runner와 modelFor):
    - 시나리오 G2=3, G2=4 두 번 실행 → RunRequest.Model이 `"Agv_c3"`, `"Agv_c4"`
    - 같은 대수 두 번이면 modelFor 슬롯 준비가 한 번이다.
  - Reuse: summary.tsv에 G2=3 행이 있으면 다시 돌리지 않는다. 시나리오 텍스트가 대수를 담고 있으므로 기존 동작 그대로다(회귀 테스트).
  - FilmWorker: 순환 그룹이 있는 영상 컷이 `NameFor` 모델로 돈다. 기존 FilmTests 방식으로 runner를 가로채 확인한다.
- [ ] **Step 2: 실패 확인.** `--filter "FullyQualifiedName~ModelVariants|FullyQualifiedName~ModelExecutor"` 와 `tests\ASH.Server.Tests --filter FullyQualifiedName~FilmTests`
- [ ] **Step 3: 구현.** 원격 실행기(JobPackage)가 원본 세 파일을 받는지 확인한다. 받지 않으면 패키지에 넣는다(`JobPackage` 파일 목록).
- [ ] **Step 4: 통과 확인**
- [ ] **Step 5: 커밋.** "순환 그룹 대수별 모델 변형"

### Task 7: KPI·판정

**Files:**
- Modify: `src/ASH.Core/Runner/Kpi.cs`, `src/ASH.Core/Runner/Judge.cs`, `src/ASH.Core/Input/ExcelReader.cs` (Settings `CycleFollowRate` 읽기)
- Test: `tests/ASH.Tests/Runner/KpiTests.cs`, `tests/ASH.Tests/Runner/JudgeTests.cs`

**Interfaces:**
- Consumes: `MaterialResult.SwapN / SwapSum / SwapMax / FollowN` (Task 4), `AgvGroup.IsCycle`
- Produces:
  - `RunKpi(..., string? Note = null, bool Cycle = false, double FollowRate = double.NaN, double SwapAvgSec = double.NaN, double SwapMaxSec = double.NaN)`
    - FollowRate = ΣFollowN / ΣSwapN (그룹 공정 자재 합). ΣSwapN = 0이면 NaN.
  - `RuleCode.Follow` (열거형 끝에 추가)

- [ ] **Step 1: 실패하는 테스트 작성**
  - Kpi: 공정 2개(SwapN 10/10, FollowN 9/10) → FollowRate 0.95, SwapAvgSec = ΣSwapSum / ΣSwapN
  - Judge (Cycle kpi):
    - FollowRate 0.96 → Ok
    - 0.90 → `Verdict(false, "뒤차 대기 90% < 95%", RuleCode.Follow)`
    - NaN → `Verdict(false, "교체 없음", RuleCode.Follow)`
    - 0.96에 StopTimeSec 30 → Ok, Reason에 `주의: 공정 정지 30초`
    - Deadlocks 1 → Deadlock 실패. 순서: NoResult → Deadlock → Follow
  - Call kpi는 지금 규칙 그대로다(기존 JudgeTests 통과).
- [ ] **Step 2: 실패 확인.** `--filter "FullyQualifiedName~KpiTests|FullyQualifiedName~JudgeTests"`
- [ ] **Step 3: 구현.** `RuleCode` 값으로 분기하는 곳(서버 페이지 포함)을 grep해서 Follow를 "뒤차 대기"로 표시하게 한다.
- [ ] **Step 4: 통과 확인.** ASH.Tests 전체와 ASH.Server.Tests 전체.
- [ ] **Step 5: 커밋.** "순환 그룹 판정: 뒤차 대기율"

### Task 8: 서버 화면·보고서

**Files:**
- Modify: `src/ASH.Server/JobView.cs` (59행 자재 레코드, 372-385행), `src/ASH.Server/JobPages.cs` (자재 표 506행 근처, 결론 858행), `src/ASH.Server/ReportPages.cs` (91행), `src/ASH.Server/JobReport.cs` (자재 시트 259-263행)
- Test: `tests/ASH.Server.Tests/JobPagesTests.cs`, `tests/ASH.Server.Tests/JobReportTests.cs`(있는 파일에 추가)

**Interfaces:**
- Consumes: `MaterialResult` 새 필드(Task 4), `RunKpi.Cycle / FollowRate / SwapAvgSec / SwapMaxSec`(Task 7)
- Produces: 자재 행 레코드에 `WaitAvg, WaitMax, WaitVehAvg` 추가. 그룹 요약에 `FollowRate, SwapAvg, SwapMax` 추가.

- [ ] **Step 1: 실패하는 테스트 작성**
  - 작업 화면 자재 표: 머리글 `공급 대기 평균`, `공급 대기 최대`, `대기 차량`. 값 `1.5` / `3` / `0.8`. 옛 결과(NaN)는 `-`.
  - 순환 그룹이 있는 작업: 그룹 칸에 `뒤차 대기 96%`, `교체 평균 12초`, `교체 최대 40초`. 리드타임 머리글 옆에 `순환: 운행 시작 기준`.
  - 보고서 자재 시트: 열 머리글 `공급 대기 평균`, `공급 대기 최대`, `대기 차량`. 순환 그룹 시트(또는 그룹 시트 열): `뒤차 대기율`, `교체 평균(초)`, `교체 최대(초)`.
  - 결론 탭: 순환 그룹 권장 사유 문구 `뒤차 대기 ≥ 95%`.
- [ ] **Step 2: 실패 확인.** `dotnet test tests\ASH.Server.Tests --filter "FullyQualifiedName~JobPages|FullyQualifiedName~JobReport"`
- [ ] **Step 3: 구현**
- [ ] **Step 4: 통과 확인.** ASH.Server.Tests 전체.
- [ ] **Step 5: 커밋.** "결과 화면·보고서: 공급 대기, 순환 교체 시간"

### Task 9: 품절 원인 — 순환 그룹 판정

**Files:**
- Modify: `src/ASH.Core/Cause/StockoutCauses.cs`
- Test: `tests/ASH.Tests/Cause/StockoutCausesTests.cs`

**Interfaces:**
- Consumes: `AgvGroup.IsCycle`, `MaterialResult` 새 필드, `RunKpi.Active`
- Produces: 순환 그룹 공정의 판정 이름 `구조적`, `대수 부족`, `교통·교착`, `순환 차례 대기` (기존 판정 문자열 형식을 따른다)

- [ ] **Step 1: 실패하는 테스트 작성** (품절이 있는 순환 공정)
  - 자유 순환 시간: 상차 + 하차 + ExtraTime + 도면 최단 주행(상차→[버퍼]→하차→[회수]→상차). 공급 속도 = 대수 × Capacity / 자유 순환 시간.
  - 기대 판정:
    - MaxCount 대로도 공급 속도 < 공정들 소비 속도 합 → `구조적`
    - MaxCount로는 되고 활성 대수로는 안 됨 → `대수 부족`. 제안 문구에 권장 대수가 나온다(호출 기준 문구가 없다).
    - 대수로는 되는데 실제 바퀴 시간이 자유 순환 시간보다 기존 교통 문턱 이상 김 → `교통·교착`
    - 다른 공정의 WaitAvg가 크고(차량이 그 공정에 묶임) 이 공정은 품절 → `순환 차례 대기`
  - 호출 그룹 판정은 그대로다(기존 테스트).
- [ ] **Step 2: 실패 확인.** `--filter FullyQualifiedName~StockoutCauses`
- [ ] **Step 3: 구현.** 최단 주행은 기존 최소 리드타임 계산 헬퍼를 쓴다.
- [ ] **Step 4: 통과 확인**
- [ ] **Step 5: 커밋.** "품절 원인: 순환 그룹 판정"

### Task 10: CP 목록을 읽는 나머지 코드, 플러그인, 문서, AutoMod 통합 시험

**Files:**
- Modify:
  - 분석: `src/ASH.Core/Cause/RouteView.cs`, `DetourCauses.cs`, `src/ASH.Core/Layout/RouteGroupCheck.cs`, `src/ASH.Core/Runner/GroupClusters.cs`
  - 엑셀: `src/ASH.Core/Input/ExcelDrawingDiff.cs`, `ExcelMarks.cs`
  - 플러그인: `src/ASH.Plugin/Commands/RouteCommands.cs`(루트 보기), `src/ASH.Plugin/Ui/InputTable.cs`(AGVSUPPLY)
- Modify (문서): `docs/input-template.md`, `docs/manual/04-input-model.md`, `docs/manual/05-results.md`
- Create: `tests/ASH.Tests/AutoMod/SupplyModesIntegrationTests.cs`

**Interfaces:**
- Consumes: `Route.FromCps`, `MaterialInfo.SourceName` (Task 2)

- [ ] **Step 1: 실패하는 테스트 작성**
  - ExcelDrawingDiff: SupplyFromCP `L001-L003` 중 L003이 도면에 없음 → L003 하나만 "도면에 없음"
  - ExcelMarks: 목록 칸 오류 표시는 그 칸 하나에 붙는다.
  - RouteView / DetourCauses: 상차 목록 → 각 상차 CP에서 하차로 가는 구간을 모두 본다.
  - RouteGroupCheck / GroupClusters: 첫 CP를 대표로 쓴다(기존 테스트 통과로 충분).
  - AGVSUPPLY: 칸 텍스트 `L001,L002`가 저장·다시 읽기 뒤에도 그대로다.
  - 통합(SkippableFact, `ASH_AM_HOME` 없으면 건너뜀):
    - `Cycle_group_two_stations_builds_and_runs`: G2 Cycle 2대, M001 하차 U001,U002 → 빌드·실행 성공. 결과에 `M001@U001`, `M001@U002` 행이 있고 각 SwapN > 0, WaitAvg ≥ 0.
    - `Call_two_load_cps_builds_and_runs`: 상차 L001,L002 → 실행 성공, 공급 > 0.
- [ ] **Step 2: 실패 확인.** 단위 테스트 필터 → FAIL. 통합은 `$env:ASH_AM_HOME='C:\AutoMod\12_6'`(그리고 `NoDefaultCurrentDirectoryInExePath` 제거) 후 `--filter FullyQualifiedName~SupplyModesIntegration`
- [ ] **Step 3: 구현.**
  - 플러그인 루트 보기는 모든 상차·하차 CP를 그린다.
  - 문서에 쓸 내용: 공급 방식 열, CP 목록·범위 표기, 공정 자재 이름, 순환 동작, 공급 대기·교체 통계, 뒤차 대기율 판정.
- [ ] **Step 4: 통과 확인.** 전체: `dotnet build src\ASH.Plugin`, ASH.Tests net10 / net48, ASH.Server.Tests, 통합 테스트.
- [ ] **Step 5: 커밋.** "CP 목록 소비처, 문서, 통합 시험"
