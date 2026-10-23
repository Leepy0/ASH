# AGV 타입 카탈로그 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 서버에 AGV 타입 카탈로그(`types.json`)와 투자비(`prices.csv`)를 두고, 플러그인 `AGVEXCEL`이 그 값으로 `AGV_Types`를 채우며, 결론·비교 화면에 투자비를 보인다. 곡선 속도는 관문 B-G1 결과에 따라 모델에 반영하거나 "미반영" 경고로 둔다.

**Architecture:** 카탈로그 데이터 모델(`CatalogType`)과 Excel 연동·검증·HTTP 읽기는 Core(netstandard2.0, 플러그인과 서버가 공유). 저장소(`CatalogStore`)·API·관리자 화면·투자비 계산은 Server. 플러그인은 `ModelCommands.AgvExcel`에서 Core의 `CatalogFetch`를 부르는 것뿐. 서버 라우트는 `Routes.Catalog.cs`의 `MapCatalog()` 한 파일(A가 `Program.cs`·nav를 소유). Core에 분기 상수 `CurveSpeed.Applied` 하나를 두고, 검증 경고·결과 화면 입력 탭·`InputSignature`가 그 값으로 갈린다.

**Tech Stack:** C# / .NET 10 (Server, ASP.NET minimal API, System.Text.Json), netstandard2.0 Core (ClosedXML, DataContractJsonSerializer), net48 플러그인(ZWCAD), xUnit. 빌드·테스트: `dotnet test ASH.sln`.

**Spec:** `docs/superpowers/specs/2026-10-23-ash-web-and-catalog-design.md` — 0, B, E, G, H장.

## Global Constraints

- 코드 수정은 B 소유 파일만: `ExcelTemplate`·`ExcelReader`·`ProjectInput.AgvType`·`InputValidator`·`InputSignature`·`LayoutExporter`(곡선 속도)·`ModelCommands`(AGVEXCEL)·새 파일(`Catalog/*`, `CatalogStore`, `CatalogPages`, `Routes.Catalog.cs`). `Program.cs`는 A의 첫 커밋 뒤에 **세 줄만**(DI 등록, `app.MapCatalog()`, `/jobs/{id}`·`/analyses/{id}` 라우트에 `CatalogStore` 인자). `JobPages.Page`·nav·CSS는 손대지 않는다.
- `AgvType` 레코드에 더하는 필드는 모두 **뒤쪽 선택 인자**(기존 생성자 호출·테스트가 그대로 컴파일).
- 0.18 파일 호환: 새 열이 없어도 `ExcelReader`가 읽는다(`OptNumIfColumn`/`StrIfColumn`). 갱신(U)은 사용자 셀을 절대 덮어쓰지 않는다.
- 카탈로그 API 모양은 고정: `GET /api/catalog/types` → `{"types":[{"name","label","category","kind","defaults":{...},"assets":[...]}]}`. 숨김 타입은 빠진다. 플러그인은 `DataContractJsonSerializer`로 읽으므로 모든 키는 소문자 camelCase, 날짜는 ISO 문자열.
- 폴백 규칙(H): 서버 주소 없음·연결 실패·**3초** 타임아웃·404·JSON 오류·빈 목록 → 전부 같은 결과 `null` → 지금처럼 `T_AGV` 한 행.
- `Asset` 열은 이름만 넣고 비워 둔다(검증·드롭다운 없음, C가 채움). `Category`는 메모(모델 무관).
- 투자비는 결론을 볼 때 현재 `prices.csv`로 계산(작업에 고정하지 않음). 단가 없는 타입은 "단가 없음: T_X", 합계는 있는 것만.
- 사내망: 페이지에 외부 자원 없음. 문구 한국어, 단위는 제목에.
- 관리자 인증은 `Program.cs`의 `HeaderAdmin`/`CookieAdmin`/`DenyPost`와 같은 규칙. 그 함수들은 top-level 지역 함수라 다른 파일에서 못 쓰므로 `CatalogRoutes`에 같은 10줄을 private로 둔다(`// shortcut: Program.cs와 중복, A가 AdminAuth로 빼면 교체`).
- 릴리스 번호(0.22.0)·릴리스 작업은 이 계획 밖.

## Review Focus

1. **옛 양식 읽기 깨짐** — 곡선 열이 없는 0.18 `input.xlsx`를 읽으면 `SpeedLoadedCurve == null`이어야 한다. Task 2 `Read_Without_Curve_Columns_Gives_Null_Curve`.
2. **갱신이 사용자 값을 덮어씀** — 카탈로그 기본값과 다른 셀이 U 뒤에도 그대로여야 하고, 없는 타입만 뒤에 붙어야 한다. Task 4 `Update_Keeps_User_Values_And_Appends_Missing_Types`.
3. **폴백이 멈추거나 갈라짐** — 응답이 3초를 넘기면 `null`, 404·빈 목록·깨진 JSON도 `null`. Task 8 `TryGet_Falls_Back_On_Timeout_404_Empty_And_BadJson` (가짜 `HttpMessageHandler`).
4. **투자비 CSV 부분 저장** — 오류 행이 하나라도 있으면 파일을 바꾸지 않고 행 번호·이유를 돌려준다. 저장 성공 시 이전 파일은 `.bak`. Task 5 `SavePrices_Rejects_Whole_File_On_Bad_Row`.
5. **단가 없는 타입이 합계를 0으로 만들거나 숨김** — 모르는 타입은 `Unpriced`에 이름이 들어가고 합계는 아는 타입만. Task 7 `Investment_Lists_Unpriced_Types_And_Sums_Known_Only`.

---

### Task 1: 관문 B-G1 — 곡선 속도를 AutoMod가 받는가

**Files:** `docs/gates.md`(절 추가), spec의 "관문 결과" 절. 코드 없음.

**Interfaces:** 없음. 결과는 Task 9의 분기(9A 반영 / 9B 미반영 경고)를 정한다.

배경(이미 확인): 데모 `C:\AutoMod\12_6\demos\amoddemos\demos\agv\agvdemo.arc\agv.asy` 218~246행의 `AGVSVEH … load Default` 블록에 `vel`·`crvvel`(곡선)·`sprvel`(spur)·`rvel`·`rcrvvel`(후진 곡선)이 **차량 타입별**로 따로 있고, ASH 템플릿 `src/ASH.Core/Export/Templates/agvs_veh.txt`도 같은 줄을 가진다(지금은 7줄 모두 같은 속도로 치환). `GPATHTYPE … vel 1 0`은 경로 타입 단위 상한(1 = 차량 값 따름). 즉 세그먼트 속성이나 `AGVVEHSEG`를 찾을 필요 없이 **차량의 `crvvel`이 호(arc) 구간에서 실제로 속도를 제한하는지**만 확인하면 된다.

- [ ] **Step 1: 시험 모델 준비**
  `agvdemo.arc`를 `%TEMP%\gateB\base\agvdemo.arc`와 `%TEMP%\gateB\slow\agvdemo.arc`로 복사. `slow\agv.asy`의 `load Default` 블록에서 `crvvel 0 3 Feet Seconds`와 `rcrvvel 0 3 Feet Seconds`만 `0 0.3`으로 바꾼다(`vel`은 3 그대로). `model.amo`의 `CONTROL` snap은 둘 다 그대로(8 h).
- [ ] **Step 2: 빌드·실행**
  각 폴더에서 `"C:\AutoMod\12_6\bin\am264.exe" -bq agvdemo` 뒤 `agvdemo.exe -w`(체험판은 `docs/gates.md` 관문 1의 "Student Version" 버튼 처리). 두 `agvdemo.report`의 차량 통계(`percent time in motion`, 처리 수)와 `.message`를 기록한다.
- [ ] **Step 3: 판정**
  - **통과**: `slow`의 차량 이동 시간·처리 수가 `base`와 뚜렷이 다르다(경로의 호 구간 20여 개를 10배 느리게 돌므로 처리 수가 눈에 띄게 준다). → Task 9A 실행, Task 9B 생략.
  - **실패**: 두 결과가 같다(`crvvel`이 호 구간에 안 먹음). → Task 9B 실행, Task 9A 생략. (`LogicGenerator`에서 호 구간 진입마다 `set velocity`하는 대안은 쓰지 않는다: `code_leg.m.txt`의 이동은 `travel to <CP>` 한 줄이라 구간 진입 이벤트가 없다. 스펙 B.4의 "경고" 경로.)
- [ ] **Step 4: 기록**
  `docs/gates.md` 끝에 "관문 B-G1 곡선 속도" 절: 날짜, 바꾼 줄, 두 report 수치, 판정. 스펙 "관문 결과" 절에 `B-G1: 통과/실패 — 근거 한 줄 — Task 9A/9B`. 커밋:
  ```
  docs: gate B-G1 curve speed result

  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  ```

---

### Task 2: AgvType 필드·Excel 열 추가 (Core)

**Files:** `src/ASH.Core/Input/ProjectInput.cs`, `src/ASH.Core/Input/ExcelTemplate.cs`, `src/ASH.Core/Input/ExcelReader.cs`, `docs/input-template.md`, 테스트 `tests/ASH.Tests/Input/ExcelRoundTripTests.cs`.

**Interfaces:**
- Produces `public sealed record AgvType(string Type, double SpeedLoaded, double SpeedEmpty, double Accel, double Decel, int Capacity, double LoadTime, double UnloadTime, double MTBF, double MTTR, double Length, double Width, string Color, int Row = 0, string Kind = "", double? SpeedLoadedCurve = null, double? SpeedEmptyCurve = null, string Category = "", string Asset = "")` — `null` 곡선 = 직선과 같음.
- Produces `ExcelTemplate.Sheets`의 `AGV_Types` 열: `Type, Category, SpeedLoaded, SpeedLoadedCurve, SpeedEmpty, SpeedEmptyCurve, Accel, Decel, Capacity, LoadTime, UnloadTime, MTBF, MTTR, Length, Width, Color, Kind, Asset`(설명 행과 예시 행도 같은 길이로; 곡선·Category·Asset 예시는 빈칸).
- Consumes `Row.OptNumIfColumn`, `Row.StrIfColumn`(기존).

- [ ] **Step 1: 실패 테스트**
  ```csharp
  [Fact] public void Template_Has_Curve_Category_Asset_Columns_In_Order()
  {   // Write(tmp) → open → AGV_Types 1행 == 위 18개 이름 순서; 2행 설명 길이 18
  }
  [Fact] public void Read_Without_Curve_Columns_Gives_Null_Curve()
  {   // 0.18 양식(옛 14열)을 ClosedXML로 직접 만들어 Read → t.SpeedLoadedCurve == null, t.Category == "", t.Asset == ""
  }
  [Fact] public void Read_Curve_Dash_Or_Blank_Is_Null_And_Number_Is_Kept()
  {   // SpeedLoadedCurve "-" → null, SpeedEmptyCurve 0.7 → 0.7
  }
  ```
- [ ] **Step 2: 실행** `dotnet test tests/ASH.Tests --filter ExcelRoundTripTests` → 컴파일 오류(FAIL).
- [ ] **Step 3: 구현** `AgvType` 서명 위와 같이. `ExcelTemplate.Sheets` 열·설명·예시 갱신(설명: "분류(메모)", "적재 곡선 속도(m/s, 빈칸=직선)", "공차 곡선 속도(m/s, 빈칸=직선)", "에셋(C에서 채움)"). `Seed`는 `Example` 복제라 자동 추종; 갱신(U)은 기존 로직으로 빠진 열을 뒤에 더한다(열 순서가 다른 옛 파일은 그대로 둠 — 읽기는 이름 기준). `ExcelReader`: `r.OptNumIfColumn("SpeedLoadedCurve"), r.OptNumIfColumn("SpeedEmptyCurve"), r.StrIfColumn("Category"), r.StrIfColumn("Asset")`.
- [ ] **Step 4: 실행** 같은 명령 → PASS. `dotnet test ASH.sln` 전체 PASS(기존 `AgvType` 생성 호출은 뒤 인자 기본값으로 컴파일).
- [ ] **Step 5: 문서** `docs/input-template.md` 29행 AGV_Types 열 목록과 "타입과 그룹" 절에 곡선 속도·Category·Asset 설명 두 줄.
- [ ] **Step 6: 커밋**
  ```
  feat(core): curve speeds, category and asset columns in AGV_Types

  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  ```

---

### Task 3: 카탈로그 모델·검증 (Core)

**Files:** 새 `src/ASH.Core/Catalog/CatalogType.cs`, `src/ASH.Core/Input/InputValidator.cs`, 테스트 새 `tests/ASH.Tests/Input/CatalogTests.cs`, `tests/ASH.Tests/Input/InputValidatorTests.cs`.

**Interfaces:**
- Produces (`ASH.Core.Catalog`, 모두 `[DataContract]`, 멤버는 **public 속성**(`{ get; set; }` — 서버의 System.Text.Json은 필드를 안 씀), 이름은 소문자 camelCase `[DataMember(Name="…")]`):
  ```csharp
  public sealed class CatalogDefaults { double SpeedLoadedStraight, SpeedLoadedCurve, SpeedEmptyStraight, SpeedEmptyCurve, Accel, Decel; int Capacity; double LoadTime, UnloadTime, MTBF, MTTR, Length, Width; string Color = ""; }
  public sealed class CatalogType { string Name = "", Label = "", Category = "", Kind = ""; CatalogDefaults Defaults = new(); List<string> Assets = new(); bool Hidden; string UpdatedUtc = "", UpdatedBy = ""; }
  public sealed class CatalogTypes { List<CatalogType> Types = new(); }   // API 본문
  ```
  `Hidden`·`UpdatedUtc`·`UpdatedBy`는 서버 파일용이며 API 응답에서는 `[DataMember(EmitDefaultValue=false)]` + 서버가 숨김 타입을 걸러 보냄.
- Produces `public static string[] CatalogType.ToTemplateRow(CatalogType t)` — `ExcelTemplate` AGV_Types 열 순서의 문자열 18개(곡선이 직선과 같거나 0이면 빈칸, `Asset`은 빈칸).
- Produces `public static List<Finding> InputValidator.CatalogDiff(ProjectInput input, IReadOnlyList<CatalogType> catalog)` — `AGV_Types` 행마다 카탈로그에 같은 이름이 있고 수치 열(속도 4·가감속·용기·상하차·MTBF·MTTR·길이·폭)이 다르면 `InputWarning` 한 건: `타입 'T_X': SpeedLoaded 1.2 ≠ 카탈로그 1.0 (의도한 값이면 무시)`. 카탈로그에 없는 타입은 경고 없음.
- 기존 `Validate`에 추가: 곡선 속도가 있으면 `>= 0`(오류), 곡선 > 직선이면 경고.

- [ ] **Step 1: 실패 테스트**
  ```csharp
  [Fact] public void ToTemplateRow_Matches_Sheet_Columns()
  {   // CatalogType{Name="T_A", Defaults{SpeedLoadedStraight=1, SpeedLoadedCurve=0.5, SpeedEmptyStraight=1.5, SpeedEmptyCurve=1.5,…}}
      // row.Length == ExcelTemplate.Sheets AGV_Types Cols.Length; row[0]=="T_A"; row[3]=="0.5"; row[5]=="" (공차 곡선=직선 → 빈칸); row[17]==""
  }
  [Fact] public void CatalogDiff_Warns_Only_On_Differing_Known_Types()
  {   // input: T_A SpeedLoaded 1.2 (카탈로그 1.0), T_B 없음 → 경고 1건, 메시지에 "T_A"와 "SpeedLoaded"
  }
  [Fact] public void Validate_Curve_Speed_Rules()
  {   // SpeedLoadedCurve -1 → InputError; SpeedLoadedCurve 2 > SpeedLoaded 1 → InputWarning
  }
  [Fact] public void CatalogTypes_RoundTrips_Through_DataContractJson()
  {   // {"types":[{"name":"T_A","label":"표준","category":"AGV","kind":"Standard","defaults":{"speedLoadedStraight":1.0,…},"assets":[]}]} → Types[0].Name == "T_A"
  }
  ```
- [ ] **Step 2: 실행** `dotnet test tests/ASH.Tests --filter "CatalogTests|InputValidatorTests"` → FAIL.
- [ ] **Step 3: 구현** 위 서명대로. 숫자 문자열은 `CultureInfo.InvariantCulture` `"0.###"`.
- [ ] **Step 4: 실행** → PASS.
- [ ] **Step 5: 커밋**
  ```
  feat(core): catalog type model, template row and catalog diff warnings

  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  ```

---

### Task 4: 양식 생성·갱신에 카탈로그 반영 (Core)

**Files:** `src/ASH.Core/Input/ExcelTemplate.cs`, 테스트 `tests/ASH.Tests/Input/ExcelTemplateSeedTests.cs`.

**Interfaces:**
- Produces `public static void ExcelTemplate.Write(string path, Layout? layout, IReadOnlyList<CatalogType>? catalog = null)`
- Produces `public static List<string> ExcelTemplate.Update(string path, Layout? layout, IReadOnlyList<CatalogType>? catalog = null)` 와 `Update(XLWorkbook wb, Layout? layout, List<ExcelEdit>? log = null, IReadOnlyList<CatalogType>? catalog = null)`.
- 규칙: `catalog`가 `null`이거나 비면 지금 그대로(`T_AGV`). 있으면 `Seed`의 `AGV_Types`가 `catalog.Select(ToTemplateRow)` 전부(숨김은 호출자가 이미 뺌), 그룹의 `Type`은 첫 타입. 도면이 없어도(`layout == null`) 카탈로그만으로 `AGV_Types`를 채운다. 갱신(U)은 `Values(tws,"Type")`에 없는 이름만 `Append`; 있는 행은 셀 하나도 바꾸지 않는다. 변경 로그: `AGV_Types: 타입 T_X 추가(카탈로그)`.

- [ ] **Step 1: 실패 테스트**
  ```csharp
  [Fact] public void Write_Fills_Types_From_Catalog_And_Groups_Use_First()
  {   // catalog [T_A, T_B], Drawing() → p.AgvTypes.Select(Type) == [T_A,T_B]; 모든 그룹 Type == "T_A"; 카탈로그 없이 쓰면 여전히 T_AGV 하나
  }
  [Fact] public void Update_Keeps_User_Values_And_Appends_Missing_Types()
  {   // Write(catalog [T_A]) → T_A SpeedLoaded 셀을 9.9로 바꿔 저장 → Update(catalog [T_A(1.0), T_B]) →
      // AgvTypes == [T_A(9.9), T_B]; changes에 "T_B 추가" 1건, "T_A" 없음
  }
  ```
- [ ] **Step 2: 실행** `dotnet test tests/ASH.Tests --filter ExcelTemplateSeedTests` → FAIL.
- [ ] **Step 3: 구현** `Seed(layout, catalog)`로 시그니처 확장(내부). `Update`의 `types.Count == 0` 분기 뒤에 카탈로그 누락 타입 추가 루프.
- [ ] **Step 4: 실행** → PASS. 전체 `dotnet test ASH.sln` PASS.
- [ ] **Step 5: 커밋**
  ```
  feat(core): fill and update AGV_Types from the type catalog

  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  ```

---

### Task 5: CatalogStore와 `GET /api/catalog/types` (Server)

**Files:** 새 `src/ASH.Server/CatalogStore.cs`, 새 `src/ASH.Server/Routes.Catalog.cs`, `src/ASH.Server/Program.cs`(DI 1줄 + `app.MapCatalog()` 1줄), 테스트 새 `tests/ASH.Server.Tests/CatalogTests.cs`.

**Interfaces:**
- Produces
  ```csharp
  public sealed record PriceRow(string Type, decimal UnitPrice, string Currency, string Note);
  public sealed record PriceSave(int Changed, IReadOnlyList<(int Row, string Why)> Errors, string? Backup);
  public sealed class CatalogStore
  {
      public CatalogStore(string jobsDir, ILogger log);          // Dir = jobsDir\catalog, 없으면 만든다
      public string Dir { get; }
      public IReadOnlyList<CatalogType> Types(bool includeHidden = false);   // 이름순
      public CatalogType? Get(string name);
      public string? Save(CatalogType t, string by);            // 이름으로 넣거나 덮어씀, UpdatedUtc/By 채움, types.json.bak, 반환 = 오류 문구 또는 null
      public IReadOnlyDictionary<string, PriceRow> Prices();    // prices.csv, 없으면 빈 사전
      public string PricesCsv();                                // 파일 원문(없으면 헤더만)
      public PriceSave SavePrices(string csv);                  // 헤더 Type,UnitPrice,Currency,Note; 검증 실패면 파일 그대로
  }
  public static class CatalogRoutes { public static void MapCatalog(this WebApplication app); }
  ```
- `Save` 검증: `InputValidator.IsAmIdentifier(Name)`, `VehicleKinds.Find(Kind) != null`, 수치 `>= 0`, `Capacity >= 1`.
- `SavePrices` 검증: 행마다 열 4개(Note는 빈칸 허용), `Type`이 `Types(includeHidden:true)`에 있음, `UnitPrice`는 `decimal` 파싱(쉼표 제거), `Currency` 빈칸이면 `KRW`. 쓰기는 `.tmp` 뒤 교체(`ExcelTemplate.Save`와 같은 방식).
- API: `GET /api/catalog/types` → `Results.Json(new { types = store.Types().Select(t => new { name = t.Name, label = t.Label, category = t.Category, kind = t.Kind, defaults = t.Defaults, assets = t.Assets }) })`, `Cache-Control: no-store`. 익명 투영이라 `hidden`·`updated*`는 나가지 않고, `defaults`는 System.Text.Json 기본 camelCase로 `CatalogDefaults`의 `DataMember` 이름과 같다.
- Consumes `JobStore.Dir`(기존), `ClientIp`는 쓰지 않음.

- [ ] **Step 1: 실패 테스트**
  ```csharp
  [Fact] public void Save_Then_Types_RoundTrips_And_Hides_Hidden()
  {   // Save(T_A), Save(T_B{Hidden=true}) → Types() == [T_A]; Types(true) == [T_A,T_B]; 두 번째 Save 뒤 types.json.bak 존재
  }
  [Fact] public void SavePrices_Rejects_Whole_File_On_Bad_Row()
  {   // Save(T_A); SavePrices("Type,UnitPrice,Currency,Note\nT_A,1000,KRW,\nT_X,abc,,") → Errors == [(3,"타입 T_X 없음"), (3,"숫자가 아닙니다")] 중 최소 1건, Changed 0, Prices() 비어 있음(파일 안 바뀜)
      // SavePrices 정상 2행 → Changed 2, Prices()["T_A"].UnitPrice == 1000m
  }
  [Fact] public async Task Api_Types_Has_Stable_Shape()
  {   // JobEnv; store.Save(T_A{Defaults.SpeedLoadedStraight=1.5, Assets=["front"]}) → GET /api/catalog/types 200
      // JsonDocument: root.types[0].name=="T_A", .label, .category, .kind, .defaults.speedLoadedStraight==1.5, .assets[0]=="front"; "hidden" 키 없음
      // (플러그인 쪽 파서 검사는 Task 8의 CatalogFetchTests가 맡는다)
  }
  ```
- [ ] **Step 2: 실행** `dotnet test tests/ASH.Server.Tests --filter CatalogTests` → FAIL.
- [ ] **Step 3: 구현** 위 서명. `Program.cs`: `builder.Services.AddSingleton(sp => new CatalogStore(sp.GetRequiredService<JobStore>().Dir, …CreateLogger<CatalogStore>()));` 와 `app.MapCatalog();`(A의 라우트 등록 뒤).
- [ ] **Step 4: 실행** → PASS.
- [ ] **Step 5: 커밋**
  ```
  feat(server): type catalog store, prices.csv and GET /api/catalog/types

  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  ```

---

### Task 6: 관리자 화면 `/admin/types` (Server)

**Files:** 새 `src/ASH.Server/CatalogPages.cs`, `src/ASH.Server/Routes.Catalog.cs`, 테스트 `tests/ASH.Server.Tests/CatalogTests.cs`.

**Interfaces:**
- Produces `public static string CatalogPages.Types(IReadOnlyList<CatalogType> types, IReadOnlyDictionary<string, PriceRow> prices, string pricesCsv, CatalogType? editing, string? msgHtml)` — `JobPages.Page("타입·투자비", body)`로 감싼다.
- Routes(모두 `Routes.Catalog.cs`, 인증은 Global Constraints의 private 복사본):
  - `GET /admin/types[?edit=NAME|&clone=NAME]` — 미로그인이면 `ServerPages.Login(rs.UploadsEnabled, null, "/admin/types", "타입·투자비(관리자)")` 401/403.
  - `POST /admin/types` 폼: `name,label,category,kind,speedLoadedStraight,speedLoadedCurve,speedEmptyStraight,speedEmptyCurve,accel,decel,capacity,loadTime,unloadTime,mtbf,mttr,length,width,color,hidden,unitPrice` → `store.Save`; `unitPrice`가 비어 있지 않으면 `prices.csv`의 그 타입 행을 바꿔 `SavePrices`(같은 파일, 스펙 B.2). 오류면 400 + 폼 유지.
  - `POST /admin/types/prices` 폼 `csv` → `SavePrices`, 결과 문구(바뀐 행 수 / 오류 행 번호·이유).
- 화면: 표(이름·라벨·분류·형상·속도 4·가감속·용기·상하차·길이·폭·색·단가·에셋 수·숨김), 행 클릭(`?edit=`)으로 아래 폼, "새 타입"·"복제" 링크, 투자비 `<textarea name=csv>`. 에셋 절은 자리만(`<section id="assets">`, C가 채움).

- [ ] **Step 1: 실패 테스트**
  ```csharp
  [Fact] public async Task Admin_Types_Needs_Login_And_Saves_With_Header_Token()
  {   // GET /admin/types → 401, 본문에 "/admin/login"
      // POST /admin/types (X-ASH-Admin-Token, form name=T_A kind=Standard speedLoadedStraight=1 … unitPrice=1200) → 200
      // store.Get("T_A").Defaults.SpeedLoadedStraight == 1; store.Prices()["T_A"].UnitPrice == 1200m
      // POST name="한글" → 400, 본문에 "AutoMod"
      // POST /admin/types/prices csv에 오류 행 → 400, 본문에 "3행"
  }
  ```
- [ ] **Step 2: 실행** `dotnet test tests/ASH.Server.Tests --filter CatalogTests` → FAIL.
- [ ] **Step 3: 구현** 위 라우트·페이지. 폼 숫자 파싱은 `double.TryParse(…, InvariantCulture)`; 실패 항목은 400과 함께 빨간 문구.
- [ ] **Step 4: 실행** → PASS.
- [ ] **Step 5: 커밋**
  ```
  feat(server): /admin/types page to edit catalog types and prices

  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  ```

---

### Task 7: 결론·비교에 투자비 (Server)

**Files:** `src/ASH.Server/JobView.cs`, `src/ASH.Server/JobPages.cs`(`Summary` 카드 1개, `Job` 선택 인자), `src/ASH.Server/AnalysisPages.cs`(`Metrics`에 필드, `Compare` 열 1개, `Detail` 선택 인자), `src/ASH.Server/Program.cs`(두 라우트에 `CatalogStore` 인자 전달), 테스트 `tests/ASH.Server.Tests/CatalogTests.cs`.

**Interfaces:**
- Produces `public sealed record InvestmentView(decimal Total, string Currency, IReadOnlyList<string> Unpriced);`
- Produces `public static InvestmentView JobView.Investment(ProjectInput input, IReadOnlyDictionary<string, int> decided, IReadOnlyDictionary<string, PriceRow> prices)` — 그룹마다 `decided[g.Group] × prices[g.Type].UnitPrice`; 타입이 `prices`에 없으면 `Unpriced`에 타입 이름(중복 제거). `Currency`는 첫 단가의 것(없으면 `KRW`). 숨김 타입도 단가가 있으면 센다.
- Changes `JobPages.Job(…, IReadOnlyDictionary<string, PriceRow>? prices = null)`: `Summary`에 네 번째 카드 `<div class="card"><div class="sub">투자비(원)</div><div class="big">{Total:N0}</div><div class="sub">단가 없음: T_X</div></div>`(단가가 전혀 없으면 카드의 big은 "-"). `prices == null`이면 카드 없음.
- Changes `AnalysisPages.Metrics(int? Total, double Util, double Stop, JobView View, InvestmentView? Investment = null)`, `MetricsOf(JobStore js, JobInfo j, IReadOnlyDictionary<string, PriceRow>? prices = null)`, `Detail(…, IReadOnlyDictionary<string, PriceRow>? prices = null)`; `Compare` 표의 `교착(회)` 옆에 `투자비(원)` 열. A가 비교 탭으로 옮길 때 `Metrics.Investment`를 그대로 쓰면 된다.

- [ ] **Step 1: 실패 테스트**
  ```csharp
  [Fact] public void Investment_Lists_Unpriced_Types_And_Sums_Known_Only()
  {   // input: G1→T_A, G2→T_B, G3→T_A; decided {G1:2,G2:3,G3:1}; prices {T_A:1000}
      // → Total == 3000m, Unpriced == ["T_B"], Currency == "KRW"
  }
  [Fact] public async Task Summary_Shows_Investment_Card()
  {   // JobEnv에 완료 작업(JobTests의 헬퍼로 Decided 있는 job.json + project\input.xlsx) + store.SavePrices("…T_AGV,500,KRW,") → GET /jobs/{id} 본문에 "투자비(원)"
  }
  ```
- [ ] **Step 2: 실행** → FAIL.
- [ ] **Step 3: 구현** 위 서명. `Program.cs`의 `/jobs/{id}`·`/analyses/{id}` 람다에 `CatalogStore cs` 인자를 더해 `cs.Prices()` 전달(두 줄).
- [ ] **Step 4: 실행** → PASS. 전체 PASS.
- [ ] **Step 5: 커밋**
  ```
  feat(server): investment from catalog prices on result and compare pages

  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  ```

---

### Task 8: 카탈로그 읽기와 `AGVEXCEL` 연동 (Core + 플러그인)

**Files:** `src/ASH.Core/Runner/JobServerClient.cs`, 새 `src/ASH.Core/Catalog/CatalogFetch.cs`, `src/ASH.Plugin/Commands/ModelCommands.cs`, `docs/plugin-manual-test.md`, 테스트 새 `tests/ASH.Tests/Runner/CatalogFetchTests.cs`.

**Interfaces:**
- Produces `public async Task<CatalogTypes> JobServerClient.GetCatalogTypesAsync(CancellationToken ct = default)` (`api/catalog/types`, `ReadOk`), `public static CatalogTypes JobServerClient.ParseCatalog(string json)`.
- Produces
  ```csharp
  public static class CatalogFetch
  {
      public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
      /// 보이는 타입 목록, 또는 null(주소 없음·연결 실패·3초 초과·non-200·JSON 오류·빈 목록 — 모두 같은 폴백)
      public static IReadOnlyList<CatalogType>? TryGet(string? serverUrl, HttpMessageHandler? handler = null);
  }
  ```
  내부는 `ServerCommands.Run`과 같은 `Task.Run + WhenAny(Timeout + 2 s)`(ZWCAD UI 컨텍스트 교착 회피). `UserSettings.TryNormalizeUrl` 실패도 `null`.
- Changes `ModelCommands.AgvExcel`: `mode != "O"`일 때 `var catalog = CatalogFetch.TryGet(UserSettings.Load().ServerUrl);` → `ExcelTemplate.Write(path, layout, catalog)` / `Update(path, layout, catalog)`(Excel COM 경로도 `Update(wb, layout, log, catalog)`). 메시지: 성공 `카탈로그 타입 {n}개로 AGV_Types를 채웠습니다` / 실패 `서버 카탈로그를 읽지 못해 T_AGV 한 행으로 만듭니다(AGVSERVER로 주소 확인)`. U 뒤에는 `ExcelReader.Read(path, out _)`로 다시 읽어 `InputValidator.CatalogDiff`를 돌리고 경고를 한 줄씩 `ed.WriteMessage`(읽기 실패는 무시 — 양식만 갱신된 상태일 수 있음).
- `AGV_Groups.Type` 드롭다운은 이미 `AGV_Types` 시트 참조라 카탈로그로 채우면 자동으로 카탈로그 이름(변경 없음).

- [ ] **Step 1: 실패 테스트**(가짜 `HttpMessageHandler`: 상태·본문·지연을 지정)
  ```csharp
  [Fact] public void TryGet_Returns_Visible_Types_On_200()
  {   // 200 {"types":[{"name":"T_A",…}]} → 1건, Name "T_A"
  }
  [Fact] public void TryGet_Falls_Back_On_Timeout_404_Empty_And_BadJson()
  {   // 지연 5 s → null (Stopwatch < 6 s); 404 → null; {"types":[]} → null; "not json" → null; serverUrl "" → null
  }
  ```
- [ ] **Step 2: 실행** `dotnet test tests/ASH.Tests --filter CatalogFetchTests` → FAIL.
- [ ] **Step 3: 구현** 위 서명. 그 다음 플러그인 연동.
- [ ] **Step 4: 실행** → PASS. `dotnet build src/ASH.Plugin/ASH.Plugin.csproj -c Release` 성공.
- [ ] **Step 5: 수동 시험 항목** `docs/plugin-manual-test.md`에 "AGVEXCEL 카탈로그" 3줄: 서버 켜짐(W → 타입 n행), 서버 주소 비움(W → T_AGV), 기존 파일에 U(값 유지·누락 타입 추가·경고 출력). 이 PC에서 ZWCAD로 1회 확인하고 결과를 같은 파일에 날짜와 함께 적는다.
- [ ] **Step 6: 커밋**
  ```
  feat(plugin): AGVEXCEL fills AGV_Types from the server catalog with a 3 s fallback

  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  ```

---

### Task 9A: 곡선 속도 모델 반영 — 관문 B-G1 **통과** 때만 (Core + Server)

**Files:** 새 `src/ASH.Core/Model/CurveSpeed.cs`, `src/ASH.Core/Export/ExportOptions.cs`(`VehicleGroupSpec`), `src/ASH.Core/Export/LayoutExporter.cs`, `src/ASH.Core/Model/ModelBuilder.cs`(`VehicleGroups`), `src/ASH.Core/Model/InputSignature.cs`, `src/ASH.Server/JobPages.cs`(`InputTab` 1행), 테스트 `tests/ASH.Tests/Export/LayoutExporterTests.cs`, `tests/ASH.Tests/Model/ModelBuilderTests.cs`.

**Interfaces:**
- Produces `public static class CurveSpeed { public const bool Applied = true; public const string Note = "반영(AutoMod 차량 crvvel)"; }`
- Changes `VehicleGroupSpec(…, double SpeedEmpty = 0, double SpeedCurve = 0, double SpeedEmptyCurve = 0, …)`(뒤쪽 선택 인자; 0 = 직선과 같음).
- `LayoutExporter`: `load Default`의 `crvvel`·`rcrvvel`는 `SpeedCurve > 0`이면 그 값, 아니면 `Speed`; `load Empty`의 같은 두 줄은 `SpeedEmptyCurve > 0`이면 그 값(`0 N` 명시), 아니면 공차 직선 규칙 그대로. 기존 정규식 두 줄을 `vel|sprvel|rvel|rsprvel|crabvel`과 `crvvel|rcrvvel` 둘로 나눈다. `GPATHTYPE … vel 1 0`(경로 상한 없음)은 그대로.
- `ModelBuilder.VehicleGroups`: `SpeedCurve: t.SpeedLoadedCurve ?? 0, SpeedEmptyCurve: t.SpeedEmptyCurve ?? 0`.
- `InputSignature.Compute`: 타입 줄에 곡선 값이 **있을 때만** 뒤에 덧붙인다(`L("type", …, t.Width)` 그대로, 곡선이 null이 아니면 `L("type", …, t.Width, t.SpeedLoadedCurve, t.SpeedEmptyCurve)`) — 옛 모델의 서명 유지.
- `JobPages.InputTab`: `<tr><th>곡선 속도</th><td>{CurveSpeed.Note}</td></tr>` + 타입별 곡선 값 표(있는 것만).

- [ ] **Step 1: 실패 테스트**
  ```csharp
  [Fact] public void Exporter_Writes_Curve_Speeds_On_crvvel_Lines_Only()
  {   // Speed 1, SpeedCurve 0.5, SpeedEmpty 1.5, SpeedEmptyCurve 0.7 (Meters) →
      // Default: "vel\t0 1 Meters", "crvvel\t0 0.5 Meters", "rcrvvel\t0 0.5"; Empty: "vel\t0 1.5", "crvvel\t0 0.7"
      // SpeedCurve 0 → crvvel == vel
  }
  [Fact] public void Signature_Changes_With_Curve_Speed_Only_When_Set()
  {   // 곡선 null인 입력 두 개 → 같은 서명; 하나에 SpeedLoadedCurve 0.5 → 다른 서명
  }
  ```
- [ ] **Step 2: 실행** `dotnet test tests/ASH.Tests --filter "LayoutExporterTests|ModelBuilderTests"` → FAIL.
- [ ] **Step 3: 구현** 위대로.
- [ ] **Step 4: 실행** → PASS. 전체 PASS. 이 PC에서 `AGVMODEL`로 모델 하나 빌드해 `agv.asy`의 `crvvel` 줄 확인(수동, 결과를 `docs/gates.md` B-G1 절에 한 줄).
- [ ] **Step 5: 커밋**
  ```
  feat(core): curve speeds written to the vehicles' crvvel lines and into the input signature

  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  ```

---

### Task 9B: 곡선 속도 "모델 미반영" 경고 — 관문 B-G1 **실패** 때만 (Core + Server)

**Files:** 새 `src/ASH.Core/Model/CurveSpeed.cs`, `src/ASH.Core/Input/InputValidator.cs`, `src/ASH.Server/JobPages.cs`(`InputTab` 1행), 테스트 `tests/ASH.Tests/Input/InputValidatorTests.cs`.

**Interfaces:**
- Produces `public static class CurveSpeed { public const bool Applied = false; public const string Note = "양식에만 받음 · 모델 미반영(AutoMod가 곡선 속도를 받지 않음, 관문 B-G1)"; }`
- `InputValidator.Validate`: 타입에 곡선 값이 있고 직선과 다르면 `InputWarning` `타입 'T_X': 곡선 속도 {v}: 모델 미반영(값을 바꿔도 결과에 영향 없음)`(기존 `Ignored` 문구 재사용). 곡선 == 직선이면 경고 없음.
- `InputSignature`·`LayoutExporter`·`VehicleGroupSpec`는 **손대지 않는다**(값이 반영되지 않으므로 서명에 넣지 않음, 스펙 B.4).
- `JobPages.InputTab`: `<tr><th>곡선 속도</th><td>{CurveSpeed.Note}</td></tr>`.

- [ ] **Step 1: 실패 테스트**
  ```csharp
  [Fact] public void Validate_Warns_Curve_Speed_Not_Modelled()
  {   // SpeedLoadedCurve 0.5 (직선 1) → InputWarning에 "미반영"; SpeedLoadedCurve 1 → 경고 없음
  }
  ```
- [ ] **Step 2: 실행** `dotnet test tests/ASH.Tests --filter InputValidatorTests` → FAIL.
- [ ] **Step 3: 구현** 위대로.
- [ ] **Step 4: 실행** → PASS. 전체 PASS.
- [ ] **Step 5: 커밋**
  ```
  feat(core): curve speed accepted in the template with a "not modelled" warning

  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  ```

---

## 자기 검토 메모

- B.2 데이터 → Task 3·5. B.3 화면 → Task 6(에셋 절은 자리만, C). B.4 양식·AGVEXCEL·관문·서명 → Task 1·2·4·8·9A/9B. B.5 투자비 → Task 7. H의 카탈로그 폴백·U 경고·투자비 타입 출처 → Task 8·3·7. G의 `.bak` 덮어쓰기 → Task 5.
- 스펙 B.4의 `LogicGenerator set velocity` 대안은 Task 1 Step 3에서 근거와 함께 뺐다(구간 진입 이벤트 없음 → 스펙이 허용한 "경고" 경로).
- `Program.cs` 접촉은 Task 5(2줄)·Task 7(2줄)뿐이며 A의 첫 커밋 뒤에 한다.
