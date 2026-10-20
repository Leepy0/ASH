# ASH 2차 구현 계획: 모듈 C(Excel·공용 로직·모델 생성) + Runner 핵심

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Excel 양식 하나와 레이아웃에서 AutoMod 모델 전체(`agv.asy` + Process `.asy` + `model.amo` + `params.txt`)를 생성·빌드하고, 그 모델을 AGV 대수·시드를 바꿔 창 없이 반복 실행해 그룹별 최소 대수를 찾아 `summary.tsv`로 내는 파이프라인을 체험판으로 끝까지 검증한다.

**Architecture:** 전부 `ASH.Core`(netstandard2.0)에 두고 xunit + 실제 AutoMod 빌드/실행(`ASH_AM_HOME`)으로 검증한다. Excel은 ClosedXML. 공용 로직 `code.m`은 EmbeddedResource 템플릿 + 치환. Process `.asy`는 데모(`agvdemo~.asy`, `vehicles~.asy`)의 줄 문법을 그대로 따른다. `ASH.Runner`는 .NET 10 콘솔로 Core의 `ModelRunner`/`SearchStrategy`를 호출한다. 체험판에서는 Authorization 창의 "Student Version" 버튼을 `BM_CLICK`으로 누른다(USB 키 서버는 창 없음).

**Tech Stack:** C#, ClosedXML(netstandard2.0), xunit, AutoMod 12.6/14.5 (`am264.exe -bq`, `<model>.exe -w`).

**Spec:** `docs/superpowers/specs/2026-10-20-ash-plugin-design.md` §7(모듈 C), §8.1–8.2(Runner·탐색), §14(확정 사항), `docs/gates.md`.

**참고 파일(구현자 필독):** `C:\AutoMod\12_6\demos\amoddemos\demos\agv\agvdemo.arc\agvdemo~.asy`(Process 엔티티 줄과 `code.m` 전체: 로드 생성·`travel to agv:<cp>`·`move into`·`use`·`wait for`·vehicle initialization/task search·`open … for writing`·`print … to`·`terminate with reports`), `…\Vehicles\vehicles.arc\vehicles~.asy:342-372`(BLOCK 엔티티 + UserDef 템플릿), `model.amo`, 매뉴얼 텍스트(`open … for reading`, `read a, b from fp`, `begin model snap function`): scratchpad `ammanual/manual.txt`는 세션 전용이므로 필요하면 `pdftotext -layout "C:\Users\ioi\Documents\Automod\메뉴얼_PDF\automod manual.pdf"`로 다시 뽑는다.

## Global Constraints

- Core `netstandard2.0`; 외부 패키지는 ClosedXML(버전 0.102.x, netstandard2.0 지원)만 추가. 실행 시 네트워크 불필요.
- 파일 이름·위치(설계서 4장): `<Project>\input.xlsx`, `<Project>\<Model>.arc\{model.amo, agv.asy, <Model>~.asy, user_ext.m, params.txt, traffic.tsv}`, `<Project>\<Model>.dir\`, `<Project>\runs\<id>\`, `runs\summary.tsv`. 결과 파일 `result.tsv`는 모델 실행 폴더(`<Project>\`)에 로직이 쓰고 Runner가 `runs\<id>\`로 옮긴다.
- Excel 시트·열은 설계서 7.1 그대로: `Settings`(Units, SimLength, Warmup, Replications, SeedBase, UtilMax, SafetyStock, InitStockMode, InitStockSpread, TrafficMode, WorkDefinition), `AGV_Types`, `AGV_Groups`, `Materials`, `Routes`, `Stations`, `Ext`. 1행 헤더, 2행 설명/단위, 3행부터 데이터. 숫자는 InvariantCulture.
- `params.txt`는 공백 구분 토큰 파일이며 AM `read`로 순서대로 읽는다. 형식(한 줄 = 한 레코드): `SEED <int>` / `GROUPS <n>` 다음 n줄 `<groupIndex> <activeCount>` / `MATERIALS <m>` 다음 m줄 `<materialIndex> <callThreshold> <unloadThreshold> <initStock> <safetyStock> <taktSec> <unitsPerCycle> <unitsPerContainer>` / `SIM <simLenSec> <warmupSec>` / `END`. 키워드 토큰은 로직에서 문자열 변수로 읽어 버린다(검증용).
- `result.tsv` 열(탭 구분, 1행 헤더): `kind name group value1 value2 value3 value4 value5 value6 value7 value8` — kind ∈ {`material`: minStock avgStock supplies expectedSupplies stopTimeSec; `vehicle`: idle travelEmpty travelLoaded load unload waitBuffer blocked down(비율 0~1), `trips`, `distance`; `group`: activeCount utilization blockedRatio; `block`: passes avgWaitSec maxWaitSec utilization; `deadlock`: count; `clock`: realSec simSec}. 빈 열은 0.
- AGVS `.asy` 헤더: `AGVSDEF … ALTERNATE NONE ignore OldBlockClaim ResumeSpeedWhenClaimed`(§14). `AGVSDEF` 줄은 기존 `LayoutExporter`의 헤더 템플릿에서 토큰만 교체한다.
- 체험판 엔티티 200 제한: 통합 테스트 모델은 경로 ≤ 12, CP ≤ 12, 자재 2, 그룹 1, block ≤ 2.
- AM 통합 테스트는 `ASH_AM_HOME` 없으면 Skip(기존 패턴). 실제 실행은 `DialogCloser`가 "Authorization" 창의 "Student Version" 버튼을 클릭(이름은 설정값, 기본 `Student Version`; 서버는 창 없음).
- 공용 로직 파일은 `code.m`(Process `.asy` 안 `SFileBegin name code.m` … `#@!` 종료 마커까지 데모와 동일 구조). `user_ext.m`은 별도 파일로 생성만 하고 덮어쓰지 않으며, `code.m`이 `FExtOnCall/FExtSelectVehicle/FExtBeforeUnload/FExtOnSnap`을 호출한다(설계서 7.3). **AM이 별도 `.m` 파일을 포함하는 문법(`SFileBegin`이 여러 개인지, `#include`가 있는지)을 Task 4에서 데모·매뉴얼로 확인하고, 없으면 `user_ext.m` 내용을 생성 시 `code.m` 뒤에 이어 붙이는 방식으로 대체한다(설계서 목표 "공용 로직을 손으로 고치지 않는다"는 유지).**
- 커밋 메시지는 `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`로 끝난다.

## Review Focus

1. Excel에 Routes가 가리키는 CP 이름이 도면에 없을 때: 생성 전에 `RouteCpMissing`으로 막아야 한다(조용히 AM 빌드 오류로 가면 안 됨). → Task 1 `Validate_Flags_Route_Cp_Missing`.
2. 재고가 0이 되어 공정이 멈춘 자재: `stopTimeSec > 0`이 `result.tsv`에 나와야 하고 Runner 판정이 그 대수를 "부족"으로 분류해야 한다. → Task 4 실행 테스트 `Run_Starved_Material_Reports_Stop_Time`, Task 7 `Judge_Fails_When_Stock_Below_Safety`.
3. 그룹 가동 대수보다 차량이 많을 때 초과 차량은 영구 주차(`wait to be ordered`)되어 통계의 `activeCount`가 설정값과 같아야 한다. → Task 4 `Run_Parks_Excess_Vehicles`.
4. 실행 시간 초과(교착 등): Runner는 프로세스를 죽이고 그 실행을 실패로 기록하되 탐색은 계속해야 한다. → Task 8 `Runner_Kills_On_Timeout_And_Records_Failure`.
5. 같은 대수·시드를 다시 실행하면 결과가 같아야 한다(시드가 `params.txt`로 전달되어 RNSTREAM에 적용). → Task 4 `Run_Same_Seed_Is_Deterministic`.

---

### Task 1: Excel 양식 모델·생성·읽기·검증

**Files:**
- Create: `src/ASH.Core/Input/ProjectInput.cs`(`Settings`, `AgvType`, `AgvGroup`, `Material`, `Route`, `Station`, `ExtHook` 레코드와 `ProjectInput` 컨테이너), `Input/ExcelTemplate.cs`(`static void Write(string path)`), `Input/ExcelReader.cs`(`static ProjectInput Read(string path)`), `Input/InputValidator.cs`(`static List<Finding> Validate(ProjectInput input, IReadOnlyCollection<string>? layoutCpNames)` — `Finding`은 `ASH.Core.Layout.Finding` 재사용, 코드 `FindingCode`에 `InputError` 추가)
- Modify: `src/ASH.Core/ASH.Core.csproj`(ClosedXML PackageReference)
- Test: `tests/ASH.Tests/Input/ExcelRoundTripTests.cs`, `tests/ASH.Tests/Input/InputValidatorTests.cs`

**Interfaces:**
- `ProjectInput { Settings Settings; List<AgvType> AgvTypes; List<AgvGroup> Groups; List<Material> Materials; List<Route> Routes; List<Station> Stations; List<ExtHook> Ext; }` 필드명은 설계서 7.1의 열 이름 그대로(PascalCase).
- `Settings` 기본값: Units "Meters", SimLength 8 h(초로 저장 `SimLengthSec = 28800`), Warmup 3600, Replications 3, SeedBase 1, UtilMax 0.75, SafetyStock 2, InitStockMode "Uniform", InitStockSpread 10, TrafficMode "Block", WorkDefinition "LoadToUnload".
- 검증 규칙: 그룹의 Type이 AGV_Types에 존재; Materials.Group이 Groups에 존재; Routes.Material이 Materials에 존재; Routes의 CP 열(SupplyFromCP, SupplyToCP, BufferCP?, ReturnFromCP?, ReturnToCP?)이 `layoutCpNames`에 존재(목록이 주어졌을 때); MinCount ≥ 1, MaxCount ≥ MinCount; TaktTime > 0, UnitsPerContainer ≥ 1; Stations.CPName 존재; 중복 이름.

- [ ] **Step 1: 테스트** — `Template_Then_Read_RoundTrips_Defaults`(양식 생성 → 읽기 → `Settings` 기본값·시트 7개·헤더 행 일치), `Read_Parses_Sample_Rows`(테스트가 ClosedXML로 샘플 행을 써 넣고 읽어 필드 비교), `Validate_Flags_Route_Cp_Missing`, `Validate_Flags_Unknown_Group_And_Type`, `Validate_Passes_Consistent_Input`.
- [ ] **Step 2: 실패 확인 → 구현 → 통과** `dotnet test tests/ASH.Tests --filter FullyQualifiedName~Input`
- [ ] **Step 3: 커밋** `feat(core): Excel input template, reader, validator`

### Task 2: `params.txt`와 시나리오

**Files:** Create `src/ASH.Core/Model/RunScenario.cs`(`sealed record RunScenario(int Seed, IReadOnlyDictionary<string,int> ActiveByGroup)`), `Model/ParamsFile.cs`(`static string Render(ProjectInput input, RunScenario s)`, `static void Write(ProjectInput, RunScenario, string path)`); Test `tests/ASH.Tests/Model/ParamsFileTests.cs`.
- 형식은 Global Constraints. 그룹·자재 인덱스는 Excel 행 순서 1부터. `InitStockMode`에 따른 초기재고는 **Core가 계산해 params에 넣는다**(Fixed: InitStock, Uniform: ±Spread% 균등난수(시드 기반 `Random(seed)`), Staggered: 자재 순번 × Spread 용기 오프셋 순환). 자재 SafetyStock 비면 Settings 기본.
- [ ] 테스트: `Render_Matches_Spec_Format`(정확한 문자열 비교, 자재 2·그룹 1), `Uniform_Init_Stock_Is_Seeded_Deterministic`, `Staggered_Offsets_Cycle`.
- [ ] 구현 → 통과 → 커밋 `feat(core): params.txt renderer`

### Task 3: Process `.asy` 생성기 (엔티티 부분)

**Files:** Create `src/ASH.Core/Model/ProcessAsyWriter.cs`, `Model/ProcessModel.cs`(엔티티 목록 모델), `Model/Templates/process_header.txt`(데모 `agvdemo~.asy` 1~15행: VERSION·SYSTYPE Process·UNITS·SYSDEF·FLAGS), `Model/Templates/block_template.txt`(`vehicles~.asy` 348~351행 UserDef 블록); Test `tests/ASH.Tests/Model/ProcessAsyWriterTests.cs`.
- `ProcessModel`: Procs(이름, nextproc), LoadTypes, Queues(이름, cap), Blocks(이름, cap, Rect2, Scale), Orders, Tables, Atts, Vars(이름, 차원, 타입, 초기값), Funcs, RnStreams, LogicSource(string).
- `ProcessAsyWriter.Write(ProcessModel m, TextWriter w)`: 줄 순서와 형식은 데모와 같게 — `PROCDEF`, `PROC name <n> 0 traf Infinite nextproc die`, `LDTYPE name <n> 0`, `QUEUE name <n> 0 cap <c>`, `BLOCK name <n> 1 cap <c>` + ` dis 1 picpos begx <cx> begy <cy> endx <cx+1> endy <cy> scx <w> scy <h> scz 1` + 빈 줄 + 템플릿(1×1 상자를 scx/scy로 늘여 사각형을 덮는다; 기준점은 사각형 중심), `ORDER name <n> 0`, `TABLE name <n> 0 bins 1 0 1`, `ATT name <n> 0 type Integer|Real|Time`, `VAR name <n> 0 type <T> [init]` 또는 배열 `VAR name <n> 1 <size> type <T>`, `RNSTREAM stream0 0 type CMRG flags 1` + `	cmrgseed 1 <seed6>`, `FUNC name <n> type Integer`, `SFileBegin	name code.m` + LogicSource + `#@!`.
- [ ] 테스트: `Writes_Demo_Like_Entity_Lines`(각 종류 1개씩 → 정확한 줄 비교), `Block_Rect_Maps_To_Center_And_Scale`(Rect (0,0)-(4,2) → begx 2 begy 1 scx 4 scy 2), `Ends_With_Code_Marker`.
- [ ] AM 통합(SkippableFact): 데모 `.arc` 복사 → `agvdemo~.asy`를 **데모 code.m을 그대로 LogicSource로 넣은** 우리 Writer 출력으로 교체(엔티티는 데모와 같게 구성) → `AmBuilder.Build` 성공. 그리고 block 1개를 추가한 변형도 빌드 성공.
- [ ] 커밋 `feat(core): Process .asy writer verified by AM build`

### Task 4: 공용 로직 `code.m` 템플릿 (설계서 7.2) + 실행 검증

**Files:** Create `src/ASH.Core/Model/Templates/code.m.txt`(EmbeddedResource, 치환 토큰 `{{…}}`), `Model/LogicGenerator.cs`(`static string Render(ProjectInput input, LayoutInfo layout)` — `LayoutInfo`는 CP 이름 목록·block 이름 목록·그룹별 standby CP; `ProcessModel BuildProcessModel(ProjectInput, LayoutInfo)`로 로직이 참조하는 VAR/ATT/PROC/QUEUE/ORDER/FUNC 선언을 함께 만든다), `Model/Templates/user_ext.m.txt`; Test `tests/ASH.Tests/Model/LogicGeneratorTests.cs`, `tests/ASH.Tests/AutoMod/ModelRunIntegrationTests.cs`.
- 로직 구성(데모 문법 기준; 이름 규칙 `P_`, `V_`, `A_`, `Q_`, `OL_`, `F_`):
  - `model initialization function`: `open "params.txt" for reading`, 토큰 순서대로 `read`하여 `V_seed`, `V_active(g)`, 자재 배열들, `V_simLen`, `V_warmup`에 저장; `create 1 load of type L_ctl to P_consume(m)`를 자재마다, `P_snap`에 1개.
  - 차량: `<agv> vehicle initialization procedure`에서 그룹별 순번 `A_index`; `task search procedure`에서 `A_index > V_active(group)`이면 `move into Q_park` + `wait to be ordered on OL_forever`. 그룹은 AGVS 차량 타입 1개 = 그룹 1개이므로 `theVehicle type`로 그룹 판별(또는 타입별 procedure).
  - 소비 `P_consume` 자재마다 1개: `while 1=1`: `wait for V_takt(m) sec`; `dec V_stock(m) by V_unitsPerCycle(m)/V_unitsPerContainer(m)`(실수); 0 이하면 정지 시간 누적 `inc V_stop(m) by …`(다음 보충까지 대기: `wait to be ordered on OL_refill(m)`); `V_stock(m) <= V_call(m)`이고 `V_pending(m)=0`이면 `set V_pending(m) to 1`, `clone 1 load to P_supply(m)`.
  - 공급 `P_supply(m)`: `move into agv:<standbyCP of group>`? — 데모처럼 로드가 `move into agv:<loadCP>`로 차량을 부르고 `travel to agv:<unloadCP>`로 이동하는 **로드 중심 패턴**을 쓴다(차량 배차는 AM 기본 work list). 순서: `move into agv:<SupplyFromCP>`(차량 호출·로딩 대기) → `wait for LoadTime` → `travel to agv:<BufferCP or SupplyToCP>` → BufferCP가 있으면 `while V_stock(m) > V_unload(m) do wait for 5 sec` → `travel to agv:<SupplyToCP>` → `wait for UnloadTime` → `inc V_stock(m) by 1`(용기 1) + `order 1 load from OL_refill(m) to continue` → `set V_pending(m) to 0` → EmptyReturn이면 `travel to agv:<ReturnFromCP>` → `travel to agv:<ReturnToCP>` → `send to die`.
  - 통계: 차량 상태 시간은 AM 기본 보고서(`<model>.report`의 vehicle statistics)에서 Runner가 파싱한다(로직으로 재구현하지 않음). 로직은 `P_snap`이 `V_warmup` 뒤 통계를 `reset statistics`? — AM에 통계 리셋 액션이 있으면 사용, 없으면 warmup은 Runner가 보고서 값에서 처리할 수 없으므로 **로직이 자재별 최소/평균 재고·공급 횟수·정지 시간을 warmup 이후에만 집계**하고 `result.tsv`에 쓴다. `begin model snap function`에서 `open "result.tsv" for writing`, 헤더, 자재 행, 그룹 행(`V_active(g)`, 가동률은 보고서에서), `clock` 행(`ac`와 실제 초 — 실시간은 `system time` 함수가 있으면 사용, 없으면 0), `deadlock` 행(교착 경고 수 = 공급 로드가 `V_deadlockSec`(기본 600초) 이상 한 CP에서 못 움직이면 inc).
  - 훅: `F_ext_on_call(m)` 등 4개는 `user_ext.m` 템플릿이 `return true`로 정의. 포함 방식은 Global Constraints의 확인 항목.
- [ ] 단위 테스트: `Render_Includes_One_Consume_Proc_Per_Material`, `Render_Uses_Route_Cp_Names`, `BuildProcessModel_Declares_All_Vars_Used_In_Logic`(정규식으로 `V_\w+` 수집 → 선언 목록과 비교).
- [ ] AM 통합(SkippableFact, `ModelRunIntegrationTests`): 십자 레이아웃(경로 4 + standby CP `S001` + load `L001` + unload `U001` + buffer `B001`), 자재 2(Takt 60 s, UnitsPerCycle 1, UnitsPerContainer 10, CallThreshold 2, UnloadThreshold 3, InitStock 5), 그룹 `G1`(Type `Tug`, Min 1 Max 3) → Task 5의 `ModelBuilder`로 생성·빌드 → `ModelRunner`(Task 8 전이므로 여기서는 `AmBuilder` 옆에 임시 `AmRunner.RunHeadless(exe, workDir, timeout, dialogButton)` 추가; Task 8이 이를 확장) → `result.tsv` 파싱: `Run_Writes_Result_Tsv_With_Material_Rows`, `Run_Parks_Excess_Vehicles`(active 1, numveh 3 → 보고서/`group` 행 activeCount 1), `Run_Starved_Material_Reports_Stop_Time`(Takt 5 s로 공급이 못 따라가게 → stopTimeSec > 0), `Run_Same_Seed_Is_Deterministic`(두 번 실행 → `result.tsv` 동일).
- [ ] 커밋 `feat(core): common AM logic template with AM run verification`

### Task 5: 모델 생성 오케스트레이션 (`model.amo`, 폴더, 빌드)

**Files:** Create `src/ASH.Core/Model/ModelAmoWriter.cs`(`static string Render(string modelName, string units, double simLenHours, IEnumerable<string> moveSystems /*"agv"*/, Rect2 drawPos)`), `Model/ModelBuilder.cs`(`sealed record ModelBuildRequest(string ProjectDir, string ModelName, ProjectInput Input, Layout Layout, ExportOptions AgvsOptions)`, `sealed record ModelBuildResult(string ArcDir, AmBuildResult Build, List<Finding> Findings, List<string> Warnings)`, `static ModelBuildResult Build(ModelBuildRequest r, string amHome, TimeSpan timeout)`); Test `tests/ASH.Tests/Model/ModelBuilderTests.cs`(AM 없이: 파일 생성·검증 실패 시 중단), AM 통합은 Task 4의 테스트가 겸함.
- 순서: `InputValidator.Validate(input, layout CP names)` → 오류면 빌드 없이 반환 → `ExportRunner.Run`(agv.asy, traffic.tsv) → `ProcessAsyWriter`(`<Model>~.asy`) → `model.amo`(`CONTROL snaplen <SimLength> Hours counts 1` — autoview 없음) → `params.txt`(기본 시나리오: 각 그룹 MinCount, seed SeedBase) → `user_ext.m` 없을 때만 생성 → `AmBuilder.Build`.
- `AGVSDEF` 토큰 교체: `LayoutExporter`의 헤더 템플릿 줄 `ALTERNATE NONE ignore ResumeSpeedWhenClaimed` → `ALTERNATE NONE ignore OldBlockClaim ResumeSpeedWhenClaimed`를 `ExportOptions.ClaimBlockOnNextCp = true`(기본 true)로 제어. 테스트 `Export_Writes_OldBlockClaim_By_Default`.
- [ ] 테스트 → 구현 → 커밋 `feat(core): model.amo and end-to-end model builder`

### Task 6: Plugin `AGVMODEL` 명령 + 버튼 (빌드만)

**Files:** Create `src/ASH.Plugin/Commands/ModelCommands.cs`(`AGVMODEL`: 도면 폴더의 `input.xlsx` 없으면 `ExcelTemplate.Write`로 만들고 메모장 열기 + 안내; 있으면 `ModelBuilder.Build`(amHome은 `ash.settings.tsv`의 `AmHome`, 기본 `C:\AutoMod\12_6`) → 결과·오류를 명령줄과 상태 줄에 출력), `AGVEXCEL`(양식만 생성/열기); Modify 팔레트 "검증·내보내기" 탭과 리본 패널에 버튼 2개, `ProjectSettings`에 `AmHome`, `SimLengthHours` 추가(+ SettingsFile 테스트), `docs/plugin-manual-test.md`에 T16.
- [ ] 빌드 0/0, 테스트, 커밋 `feat(plugin): AGVEXCEL and AGVMODEL commands`

### Task 7: 결과 파서·KPI·판정·요약

**Files:** Create `src/ASH.Core/Runner/ResultSet.cs`(`result.tsv` 행 모델), `Runner/ResultParser.cs`(`static ResultSet Parse(string tsvPath)`, `static VehicleStats ParseReport(string reportPath)` — `<model>.report`의 vehicle/block statistics 표에서 상태 비율 추출; 표 형식은 Task 4 실행에서 생긴 실제 `.report`를 fixture로 저장해 테스트), `Runner/Kpi.cs`(`sealed record RunKpi(string Group, int Active, double Utilization, double MinStockRatio /*min(minStock/safety)*/, int Supplies, int Expected, double StopTimeSec, int Deadlocks)`), `Runner/Judge.cs`(`static Verdict Judge(IEnumerable<RunKpi> reps, Settings s)` — 규칙 순서 설계서 8.2: 재고 ≥ SafetyStock(모든 반복의 평균 최소재고) → 가동률 ≤ UtilMax → 공급 ≥ 기대 95% → 교착 0; `Verdict(bool Ok, string Reason)`), `Runner/SummaryTsv.cs`(누적 append, 열: runId group active seed ok reason utilization minStock supplies expected stopTime deadlocks durationSec); Tests `tests/ASH.Tests/Runner/*`.
- [ ] 테스트: `Parse_Result_Tsv_Rows`, `ParseReport_Extracts_Vehicle_State_Ratios`(fixture), `Judge_Fails_When_Stock_Below_Safety`, `Judge_Fails_When_Util_Over_Max`, `Judge_Passes_Good_Run`, `Summary_Appends_And_Reads_Back`.
- [ ] 커밋 `feat(core): result parser, KPI, judge, summary`

### Task 8: `ModelRunner` (반복 실행 엔진)

**Files:** Create `src/ASH.Core/Runner/ModelRunner.cs`(`sealed record RunRequest(string ProjectDir, string ModelName, RunScenario Scenario, TimeSpan Timeout, string? DialogButton /*"Student Version" or null*/)`, `sealed record RunOutcome(string RunId, bool Success, string RunDir, TimeSpan Duration, string? Error)`, `RunOutcome Run(RunRequest r)`), Modify `AutoMod/DialogCloser.cs`(`Start(pid, titleContains, buttonText?, onClosed)` — 버튼 이름이 주어지면 그 자식 버튼에 `BM_CLICK`, 없으면 기존 OK/WM_CLOSE), Test `tests/ASH.Tests/Runner/ModelRunnerTests.cs`(AM 없이: 가짜 exe 경로 → 실패 기록; SkippableFact: Task 4 모델로 실제 1회 실행 → `runs\<id>\result.tsv`, `<Model>.report`, `.message` 이동 확인; `Runner_Kills_On_Timeout_And_Records_Failure`는 timeout 1 ms로).
- RunId = `g1_n<active>_s<seed>` 형식(그룹 여러 개면 `n1-2-3`). 실행 전 `params.txt` 교체, 실행 폴더의 이전 `result.tsv` 삭제. 동시 실행은 호출자가 제어(Runner exe의 `--parallel`).
- [ ] 커밋 `feat(core): headless model runner with dialog button click`

### Task 9: 탐색 전략

**Files:** Create `src/ASH.Core/Runner/SearchStrategy.cs`(`interface IRunExecutor { IReadOnlyList<RunKpi> Execute(RunScenario s); }`, `sealed record SearchResult(IReadOnlyDictionary<string,int> Decided, List<(RunScenario, Verdict)> Trace)`, `static SearchResult Search(ProjectInput input, IRunExecutor exec, int initialReplications = 1)`), Test `tests/ASH.Tests/Runner/SearchStrategyTests.cs`(가짜 executor: 대수 ≥ k면 통과하는 단조 함수).
- 알고리즘(설계서 8.2 + 12장 반복 수 조절): 그룹마다 독립 이진 탐색 [Min, Max]로 통과 최소 대수(반복 `initialReplications`) → 결정값과 ±1에서 `Replications` 전부 → 모든 그룹 결정값으로 확인 실행 `Replications`회 → 실패한 그룹 +1 후 재확인(최대 3회). 어떤 대수도 통과 못 하면 Max + 사유.
- [ ] 테스트: `Search_Finds_Min_Passing_Count`, `Search_Confirms_With_Replications`, `Search_Bumps_Failing_Group_On_Confirm`, `Search_Reports_Max_When_None_Pass`.
- [ ] 커밋 `feat(core): binary-search sizing strategy`

### Task 10: `ASH.Runner` 콘솔

**Files:** Create `src/ASH.Runner/ASH.Runner.csproj`(net10.0 콘솔, ProjectReference Core), `Program.cs`(`ash-run <ProjectDir> --model <name> [--am-home C:\AutoMod\12_6] [--parallel 1] [--dialog-button "Student Version"] [--timeout-min 30] [--initial-reps 1]` → Excel 읽기 → `SearchStrategy.Search`(실행자는 `ModelRunner` + `ResultParser` + `Kpi`, `--parallel`만큼 동시) → `runs\summary.tsv` 누적 → 콘솔 표(그룹별 결정 대수, 실행 수, 소요 시간)); Modify `ASH.sln`, `scripts/build-release.ps1`(`dist/ASH-Runner-<ver>.zip`), README(실행 예).
- [ ] 통합(SkippableFact, `RunnerEndToEndTests`): Task 4 테스트 모델로 `Search` 실제 실행(Min 1 Max 3, Replications 1, SimLength 1 h) → `summary.tsv` 행 ≥ 2, Decided 존재. 실행 시간을 보고서에 기록.
- [ ] 커밋 `feat(runner): ash-run console`

---

## 미결(구현 중 확인해 원장에 기록)

- `user_ext.m` 포함 문법(Global Constraints).
- AM에 통계 리셋/실시간 시계 액션이 있는지(없으면 `clock` 행 realSec는 Runner가 측정한 값으로 대체).
- `<model>.report`의 vehicle statistics 표 형식(Task 7 fixture로 고정).
- `OldBlockClaim` 토큰 대응(편집기 저장본으로 확정; 틀리면 `ExportOptions` 한 곳만 수정).

---

## 추가 (2026-10-21, 분석 서버 설계서 `ASH_Servrr/docs/superpowers/specs/2026-10-21-ash-server-design.md` §6.7·§14 반영)

### Task 4b: 서버 연동 준비 — blocks.tsv, Debug 플래그, block×그룹 대기, Excel 모르는 열

**Files:** Modify `src/ASH.Core/Export/ExportRunner.cs`(+`BlocksTsv`), `Model/ParamsFile.cs`(`DEBUG <0|1>` 줄을 `SIM` 다음·`END` 앞에 추가; `RunScenario.Debug` 기본 false), `Model/Templates/code*.m.txt` + `LogicGenerator.cs`(`V_debug` 읽기; 모든 `print … to message`는 `if V_debug = 1 then` 안으로; snap에 `blockgroup` 행: `kind=blockgroup name=<block> group=<G> value1=waitSec value2=waitCount` — 공급 로드가 block 앞에서 기다린 시간을 그룹별로 누적: 각 `travel` leg 전후 `ac` 차이에서 기대 이동시간을 뺀 값을 "대기"로 근사하되, 가능하면 block 속성 `average waiting time`×`total`을 그룹별로 나눌 수 없으므로 로드 측에서 `theVehicle current block`류 속성이 없으면 leg 단위 근사를 쓰고 보고서에 방법을 적는다), `Input/ExcelReader.cs`(모르는 열 무시 테스트 `Read_Ignores_Unknown_Columns`), `tests/...`.
- `blocks.tsv` 열: `name cap minx miny maxx maxy` (export 단위, InvariantCulture). 테스트 `Runner_Writes_Blocks_Tsv`.
- AM 통합: 기존 ModelRun 테스트에 `blockgroup` 행 존재(`G1` 행, waitSec ≥ 0)와 Debug=0일 때 `.message`에 print 출력이 없음을 단언.
- 커밋 `feat(core): blocks.tsv, debug flag, block-group wait rows, unknown Excel columns`.

### Task 9 (변경): 묶음 좌표 하강 탐색

서버 설계 §6.7을 따른다. `SearchStrategy.Search(ProjectInput input, IRunExecutor exec, GroupClusters clusters, int initialReplications = 1)`:
1. **묶음**: `GroupClusters.Build(Layout, ProjectInput, TrafficTable)` — 그룹별로 Routes의 CP 쌍 사이 최단 경로(`PathGraph` 위 Dijkstra, 경로 길이 가중)에 쓰이는 경로 Id 집합과 그 경로가 지나는 block 집합을 구하고, 교집합이 있는 그룹을 union-find로 묶는다. 공유 없는 그룹은 단독 묶음. 테스트: 공유 경로 2그룹 → 1묶음, 분리 2그룹 → 2묶음.
2. **묶음별 좌표 하강**: 전 그룹 `MaxCount` 시작 → 묶음 안 그룹을 차례로 이진 탐색으로 줄이되 판정은 **묶음 전체 그룹**에 대해(`Judge` 모든 그룹 Ok) → 한 바퀴에 변화가 있으면 반복, 없으면 종료. 탐색 중 반복 `initialReplications`.
3. 결정값과 −1에서 `Replications`회 판정(−1이 통과하면 채택하고 다시 −1).
4. **확인 실행**: 전 그룹 권장 조합 `Replications`회 → 실패 그룹 +1 → 재확인(최대 3회).
5. 영향표(선택, 기본 켜짐): 그룹마다 +1로 `Replications`회 실행해 다른 그룹 KPI 변화를 `impact.tsv`에 기록(`group plusOne otherGroup utilDelta minStockDelta`).
- `IRunExecutor.Execute(RunScenario) : IReadOnlyList<RunKpi>`(그룹별). 테스트는 가짜 executor(묶음 안 그룹 합이 임계 이상이면 통과하는 함수)로 묶음 판정이 그룹 독립 판정과 다른 결과를 내는 경우를 고정한다.

### Task 10 (변경): `--parallel` 기본값 2.

### Task 5b: 외부 값으로 모델 조립 (서버용)

`ModelBuildRequest`에 `VehicleOverrides`(그룹별 `AGVVEHSEG` 원문 블록과 `AGVSVEH` 속성 블록을 통째로 교체할 수 있는 `string? VehSegText`, `string? VehicleBlockText`)와 `MaterialOverrides`(`MaterialInfo` 목록 치환)를 추가하고, `ModelBuilder.Build`가 있으면 그것을 쓴다. `ExportOptions`에 `VehicleTemplateOverride` 전달 경로 추가. 테스트: 오버라이드 문자열이 `agv.asy`에 그대로 들어가고 자재 치환이 `params.txt`/로직에 반영됨. 커밋 `feat(core): external vehicle/material overrides for server assembly`.

### 후속(서버 구현 시): `ash.settings.tsv`의 `ServerUrl`, 플러그인 "서버로 보내기"/"결과 올리기", 입력 내용 해시 기록.
