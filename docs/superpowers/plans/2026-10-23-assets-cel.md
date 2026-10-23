# 에셋(.cel) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 관리자가 `/admin/types`에서 타입별로 STL/OBJ를 올리면 서버가 축 정렬 상자 묶음의 경량 `.cel`과 SVG 미리보기(위·옆·사선)를 만들어 보관하고, 플러그인이 모델 생성(AGVMODEL) 때 그 `.cel`을 모델 폴더에 받아 차량 그림으로 쓴다(서버·에셋이 없으면 지금의 `VehicleGeometry` 그림).

**Architecture:**
- 조사 결과(이 PC `C:\AutoMod\12_6\demos\graphics\cell\*.cel` 24개, `agvs_vehseg.txt`): AutoMod `.asy`는 `.cel` 파일을 **경로로 참조하지 않고** `template Feet` 뒤에 셀 내용을 **인라인**한다(`agvk.cel`의 `10000/7000` 머리가 `.asy`에서는 `700`으로 바뀌어 들어감). 그래서 "차량 그래픽으로 연결" = 에셋의 상자 목록을 `VehicleGeometry`와 같은 자리(Geom1 자식)에 끼운 `AGVVEHSEG` 텍스트를 만들어 기존 `VehicleTemplateOverride.VehSegText` 훅으로 넣는다. 익스포터는 손대지 않는다.
- Core(`src/ASH.Core/Cel/`): `MeshReader`(STL/OBJ → 부품별 삼각형) → `BoxSimplifier`(부품별 AABB, 축·원점 정렬, 필터) → `Box` 목록(미터, 그림 좌표계: x 가로, y 진행축(앞 = −y), z 바닥 0) → `CelWriter`(독립 `.cel` = 10000/7000/700/310, 차량 세그먼트 텍스트) / `CelReader`(310만 읽음, 플러그인용) / `SvgPreview`.
- Server: `AssetStore`(`<Jobs:Dir>\catalog\assets\<id>\`), `Routes.Assets.cs`(업로드·재변환·삭제·목록·파일), `AssetPages.Section`(B의 `/admin/types` 안 에셋 절).
- Plugin: AGVEXCEL `Asset` 드롭다운, AGVMODEL에서 `.cel` 내려받아 `ModelBuildRequest.VehicleOverrides`로 전달.
- 단위: 상자는 미터로 저장(`asset.json`), `.cel`·세그먼트 텍스트는 피트(템플릿 `template Feet`와 같음, `VehicleGeometry.FtPerM`).

**Tech Stack:** C# — Core netstandard2.0(라이브러리 추가 없음, 파서 직접 구현), Server .NET 10 minimal API, Plugin net48(ZWCAD), xunit(`tests/ASH.Tests`, `tests/ASH.Server.Tests`의 `JobEnv`).

**Spec:** `C:\Users\ioi\Documents\ASH\docs\superpowers\specs\2026-10-23-ash-web-and-catalog-design.md` (0, C, E, G, H, B.2/B.3). 보조: `2026-10-21-ash-server-design.md` 3장 표 `.cel` 행·6.2·G3, `docs/gates.md`.

## Global Constraints

- 코드 금지: NuGet·외부 호스트 의존성 추가 없음(사내망). 페이지에 외부 자원 없음.
- 좌표계는 `VehicleGeometry` 주석을 따른다: 그림 x = 가로, y = 진행축, 차량은 **−y로 달린다**(앞 = −y), 바닥 z = 템플릿 기준 −2.5 ft(`FloorRel`). 310 크기 줄은 `y-extent x-extent top-y top-x height 0 0`(y 먼저). 플래그 5 = 이름+위치·회전·스케일 줄 있음, 자기 색.
- 상자(310)만 쓴다(C.4). 원기둥은 관문 C-G1 결과를 기록만 한다.
- 에셋은 버전 이력 없음: 재변환·덮어쓰기 때 이전 `asset.cel`/`asset.json`을 `.bak`로.
- 서버 코드는 `Program.cs`에 라우트를 넣지 않고 `Routes.Assets.cs`의 `MapAssets()`만 `Program.cs`에 1줄 추가. 관리자 판정은 B가 공용으로 빼둔 `AdminAuth.IsAdmin(HttpContext, ReleaseStore, AdminSessions)`를 쓴다(없으면 Task 9에서 `Program.cs`의 `HeaderAdmin`/`CookieAdmin` 두 줄을 그 이름의 static 클래스로 옮긴다).
- B가 주는 것(가정): `src/ASH.Server/Catalog/TypeStore.cs` — `AgvTypeDef Find(string name)`, `IReadOnlyList<AgvTypeDef> All()`, `void AddAsset(string typeName, string assetId)`, `void RemoveAsset(string typeName, string assetId)`; `AgvTypeDef.Assets : List<string>`; `/admin/types` 렌더 함수 `CatalogPages.Types(...)`; `ExcelTemplate` `AGV_Types` 열에 `Asset` 이름이 들어 있음; 플러그인 `CatalogClient.GetTypesAsync(url, ct)`(`GET /api/catalog/types`, 3초 타임아웃·실패 시 null). `AddAsset`/`RemoveAsset`이 없으면 C가 `TypeStore`에 그 두 메서드만 더한다.
- 커밋 메시지: `feat(core): …` / `feat(server): …` / `feat(plugin): …` / `docs: …`, 끝에 빈 줄 + `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- 테스트 픽스처는 코드로 만든다(`tests/ASH.Tests/Fixtures`에 작은 ASCII STL·OBJ 문자열 상수; 바이너리 STL은 테스트에서 바이트로 생성). 데모 `.cel`을 저장소에 복사하지 않는다(관문 기록만).

## Review Focus

1. **바이너리 STL 오판**(Task 4): 바이너리 STL도 80바이트 머리가 `solid`로 시작할 수 있다. 판정은 머리 문자열이 아니라 `길이 == 84 + 50 × n`으로.
2. **진행 방향 부호**(Task 5): 그림 좌표계에서 앞은 **−y**. `Heading=+X`인 메시의 +X 끝 부자재가 변환 뒤 `y < 0`에 있어야 한다. 원점 `Front`는 앞 끝(최소 y)을 0에 둔다.
3. **700 자식 수 불일치**(Task 6): 그룹(700)의 자식 수 줄이 실제와 다르면 AM이 조용히 깨진다. 테스트가 `Geom1` 자식 수 줄 == 상자 수 + 1(`Lift_Joint`; `Base3_Blue`는 에셋에 넣지 않음)을 센다.
4. **업로드 상한 우회**(Task 9): `FormOptions.MultipartBodyLengthLimit`는 전역 512 MB. 에셋 라우트는 `IHttpMaxRequestBodySizeFeature.MaxRequestBodySize`를 50 MB로 내리고, 파트 길이도 다시 검사한다.
5. **플러그인 폴백이 CAD를 멈추거나 조용히 빠뜨림**(Task 11): 서버 호출은 3초 타임아웃, 실패·404·이름 불일치는 **경고 한 줄**("에셋 X 없음 → 기본 그림")과 기본 그림. 예외가 AGVMODEL을 중단시키면 안 된다.

---

### Task 1: 관문 C-G1 — `.cel` 310 필드 확정, 원기둥(320/350) 해독

**Files:** `docs/superpowers/specs/2026-10-23-ash-web-and-catalog-design.md`("관문 결과" 절), `docs/gates.md`(표에 C-G1 행)

**Interfaces:** 없음(코드 없음)

- [ ] **Step 1: 데모 통계·표본 확인.** `C:\AutoMod\12_6\demos\graphics\cell\*.cel`에서 타입 코드 빈도(310 408, 700 230, 351 101, 320 62, 350 38, 7000 22, 10000 21, 321 11 …)와 `auto.cel`의 `320 5 … 0.3788 0.3788 0.3788 0 0`(바퀴 drum, 회전 `90 0 0`), `jet.cel`의 `350 5 … 10 120 0 0` / `5 11 0 0`을 적는다. 310은 이미 `VehicleGeometry` 주석대로 AutoMod 12.6에서 확인됨(`y-extent x-extent top-y top-x height 0 0`, 플래그 1/4/16) — 그대로 **확정**으로 기록.
- [ ] **Step 2: 원기둥 시험 파일.** `%TEMP%\gate\cyl.cel`을 손으로 만든다: `10000 5` 머리 → `7000 21` → `700 17` 자식 3 → 310 상자 1개(기준자, 1×1×1) + `320 5`(`0.5 0.5 2 0 0`, 회전 0) + `350 5`(`0.5 2 0 0`). AM 편집기(`C:\AutoMod\12_6\bin\ace64.exe` 또는 모델 편집기의 그래픽 가져오기)에서 열어 각 도형의 형태·반지름·높이·축 방향을 기준 상자와 비교한다. 열리지 않으면 값을 바꿔 2회만 더 시도하고 "미해독"으로 둔다.
- [ ] **Step 3: 기록.** 스펙 "관문 결과"에 `C-G1` 항목: 310 확정 근거, 320/350의 필드 해석(또는 미해독), 7000/10000 머리(이름 줄·위치·회전·스케일·자식 수 1). `docs/gates.md` 표에 같은 요약 1행. 커밋 `docs: C-G1 .cel 필드 관문 결과`.
- [ ] **Step 4: 영향.** 결과와 무관하게 이번 릴리스는 상자만(C.4). 원기둥 해독 성공은 "다음 릴리스에서 `BoxSimplifier`에 원기둥 판별 추가 가능"으로만 적는다. Task 4~12는 모두 진행.

### Task 2: 관문 C-G2 — 입력 형식(STEP 가능 여부)

**Files:** 스펙 "관문 결과", `docs/gates.md`

**Interfaces:** 없음

- [ ] **Step 1: 오프라인 STEP 라이브러리 유무.** `third_party\`(없음), `%USERPROFILE%\.nuget\packages`에 step/opencascade/assimp 계열 없음(2026-10-23 확인). 순수 .NET STEP 파서(IxMilia.Step)는 엔티티만 읽고 B-rep 테셀레이션이 없어 상자화에 못 쓴다. AM의 `A3DLIBS.dll`에 `.step` 문자열은 있으나 명령줄 자동화 지점이 없다(서버 설계 3장).
- [ ] **Step 2: 기록.** "관문 결과"에 `C-G2: STL(ASCII·binary)·OBJ(v/f)만. STEP은 사용자가 CAD에서 STL로 내보내 올린다(이유: 오프라인 설치 가능한 테셀레이션 커널 없음)`. 업로드 화면 도움말 문구에도 같은 안내. 커밋 `docs: C-G2 입력 형식 관문 결과`.
- [ ] **Step 3: 영향.** Task 4는 STL/OBJ만. 관문이 뒤집히면(라이브러리 발견) Task 4 뒤에 "STEP → 삼각형" 리더를 `MeshReader.Read`의 확장자 분기 하나로 더한다.

### Task 3: 관문 C-G3 — 생성 `.cel`이 이 PC의 AutoMod에서 빌드·실행

**Files:** 스펙 "관문 결과", `docs/gates.md`, 임시 `%TEMP%\gate\`

**Interfaces:** 없음(기존 `LayoutExporter` + `VehicleTemplateOverride(VehSegText)` 사용)

- [ ] **Step 1: 세그먼트 인라인 경로(기본안).** `tests/ASH.Tests/AutoMod`의 빌드 통합 테스트가 쓰는 기준 모델(또는 `VehicleGeometry` 데모 모델)을 `%TEMP%\gate\g3`에 만든다. `agvs_vehseg.txt`를 복사해 `Geom1` 자식을 손으로 쓴 310 상자 5개(차체 1 + 바퀴 4, 피트)로 바꾸고 자식 수 줄을 6(상자 5 + Lift_Joint)으로 고친 텍스트를 `ExportOptions.VehicleOverrides["T_AGV"] = new VehicleTemplateOverride(VehSegText: …)`로 넣어 `.asy`를 만든다. `"C:\AutoMod\12_6\bin\am264.exe" -bq <model>` 빌드, `<model>.exe -w`(체험판은 "Student Version" 버튼, `docs/gates.md` 관문 1) 실행, 편집기에서 차량 그림·적재물 위치(CONTAINER z = 차체 높이)를 본다.
- [ ] **Step 2: 독립 `.cel` 파일 경로(확인만).** 같은 상자들을 `10000 5 / 7000 21 / 700 17` 머리로 감싼 `%TEMP%\gate\g3\box_agv.cel`을 AM 편집기 "그래픽 가져오기"로 열어 형상·크기가 Step 1과 같은지 본다. `.asy`에 파일 경로로 참조하는 문법이 있는지 매뉴얼(`automod manual.pdf`, "cell file", "picture file")을 한 번 검색한다. 없으면 "인라인만"으로 확정(이 계획의 기본안).
- [ ] **Step 3: 기록.** "관문 결과"에 `C-G3: 인라인 세그먼트로 빌드·실행 통과 / 독립 .cel 편집기 열림 여부 / 파일 참조 문법 유무`. `docs/gates.md` 1행. 커밋 `docs: C-G3 .cel 모델 반영 관문 결과`.
- [ ] **Step 4: 영향.** 통과 → Task 6의 `VehSeg`와 Task 11 그대로. 인라인이 실패하면(AM이 임의 상자 수를 거부) Task 6·11을 멈추고 사용자에게 보고(0.22.0에서 C 제외, 스펙 H).

### Task 4: `MeshReader` — STL(ASCII·binary)·OBJ(v/f) 파서

**Files:** `src/ASH.Core/Cel/MeshReader.cs`, `tests/ASH.Tests/Cel/MeshReaderTests.cs`, `tests/ASH.Tests/Fixtures/MeshFixtures.cs`

**Interfaces:**
```csharp
namespace ASH.Core.Cel;
public readonly struct Vec3 { public readonly double X, Y, Z; public Vec3(double x, double y, double z); }
public sealed record Triangle(Vec3 A, Vec3 B, Vec3 C);
/// <summary>A part: one `solid` of an ASCII STL, one `o`/`g` of an OBJ, or the whole file.</summary>
public sealed record MeshPart(string Name, IReadOnlyList<Triangle> Triangles);
public static class MeshReader
{
    /// <summary>By extension (.stl/.obj, case-insensitive); FormatException with the line number for a bad file, NotSupportedException for another extension.</summary>
    public static IReadOnlyList<MeshPart> Read(string path);
    public static IReadOnlyList<MeshPart> ReadStl(byte[] bytes);          // binary when bytes.Length == 84 + 50 * n (n from offset 80), else ASCII
    public static IReadOnlyList<MeshPart> ReadObj(TextReader reader);     // v, f (3+ vertices → fan), o/g start a part; vt/vn/mtl ignored; negative indices allowed
}
```

- [ ] **Step 1: 실패 테스트.** `MeshFixtures.StlAscii2Solids`(`solid body` 삼각형 2개 + `solid wheel` 삼각형 1개), `MeshFixtures.ObjQuad`(`o box` v 4개, f 1개 사각형), 바이너리 STL은 테스트에서 `BinaryWriter`로 80바이트 머리 `"solid fake"` + n=2로 생성.
  ```csharp
  [Fact] public void Stl_Ascii_Splits_Solids() { var p = MeshReader.ReadStl(Encoding.ASCII.GetBytes(MeshFixtures.StlAscii2Solids)); Assert.Equal(new[]{"body","wheel"}, p.Select(x=>x.Name)); Assert.Equal(2, p[0].Triangles.Count); }
  [Fact] public void Stl_Binary_With_Solid_Header_Is_Binary() { var p = MeshReader.ReadStl(BinaryStl(2)); Assert.Single(p); Assert.Equal(2, p[0].Triangles.Count); }
  [Fact] public void Obj_Quad_Fans_To_Two_Triangles() { var p = MeshReader.ReadObj(new StringReader(MeshFixtures.ObjQuad)); Assert.Equal("box", p[0].Name); Assert.Equal(2, p[0].Triangles.Count); }
  [Fact] public void Obj_Bad_Face_Index_Reports_Line() { var e = Assert.Throws<FormatException>(() => MeshReader.ReadObj(new StringReader("v 0 0 0\nf 1 2 3\n"))); Assert.Contains("2", e.Message); }
  ```
- [ ] **Step 2: 실행 FAIL** `dotnet test tests/ASH.Tests --filter MeshReaderTests`.
- [ ] **Step 3: 구현.** 위 시그니처 그대로. ASCII STL은 `vertex` 토큰 3개씩, `solid <name>`마다 부품(이름 없으면 `part1`…). 바이너리 판정은 길이식만. OBJ는 `InvariantCulture` 파싱.
- [ ] **Step 4: 실행 PASS.** 커밋 `feat(core): MeshReader — STL(ASCII/binary)·OBJ(v/f) 부품별 삼각형 읽기`.

### Task 5: `BoxSimplifier` — 부품별 AABB, 방향·원점·필터

**Files:** `src/ASH.Core/Cel/BoxSimplifier.cs`, `tests/ASH.Tests/Cel/BoxSimplifierTests.cs`

**Interfaces:**
```csharp
namespace ASH.Core.Cel;
public enum Heading { PlusX, PlusY, MinusX, MinusY }   // which source axis points forward
public enum OriginAt { Front, Center, Back }           // where y = 0 sits along travel
/// <summary>MinPartPct: parts whose longest side is under this % of the whole length are dropped. SourceUnitToM: 0.001 for mm files (default), 1 for m, 0.0254 for in.
/// Grid: when the file has one part, split its bounding box into Grid x Grid cells (xy) and box each occupied cell (1 = off).</summary>
public sealed record SimplifyOptions(double MinPartPct = 2, int MaxBoxes = 200, Heading Heading = Heading.PlusX, OriginAt Origin = OriginAt.Center,
    int RotateDeg = 0, double SourceUnitToM = 0.001, int Grid = 1);
/// <summary>Metres, picture frame: x across, y along travel (front = -y), Z0 = bottom above the floor; Dx, Dy, H > 0.</summary>
public sealed record Box(string Name, double X, double Y, double Z0, double Dx, double Dy, double H, int Color = 9);
public sealed record SimplifyResult(IReadOnlyList<Box> Boxes, int DroppedSmall, int DroppedOverMax, double LengthM, double WidthM, double HeightM);
public static class BoxSimplifier
{
    /// <summary>ArgumentException when no triangle survives.</summary>
    public static SimplifyResult Simplify(IReadOnlyList<MeshPart> parts, SimplifyOptions o);
}
```

- [ ] **Step 1: 실패 테스트.** 부품 2개를 mm로 만든다: `body` AABB x 0..2000, y −500..500, z 0..600; `lamp` x 1950..2000, y −50..50, z 300..340(앞쪽 끝에 있는 작은 부품); `dust` 10 mm 조각.
  ```csharp
  [Fact] public void PlusX_Heading_Puts_Front_At_Negative_Y() { var r = BoxSimplifier.Simplify(Parts(), new SimplifyOptions(Heading: Heading.PlusX, Origin: OriginAt.Center, MinPartPct: 1)); var lamp = r.Boxes.Single(b => b.Name == "lamp"); Assert.True(lamp.Y < -0.9, lamp.Y.ToString()); Assert.Equal(2.0, r.LengthM, 6); Assert.Equal(1.0, r.WidthM, 6); }
  [Fact] public void Front_Origin_Starts_At_Zero() { var r = BoxSimplifier.Simplify(Parts(), new SimplifyOptions(Origin: OriginAt.Front)); Assert.Equal(0, r.Boxes.Min(b => b.Y - b.Dy / 2), 6); }
  [Fact] public void Floor_Is_Zero_And_Small_Parts_Dropped() { var r = BoxSimplifier.Simplify(Parts(), new SimplifyOptions()); Assert.Equal(0, r.Boxes.Min(b => b.Z0), 6); Assert.DoesNotContain(r.Boxes, b => b.Name == "dust"); Assert.Equal(1, r.DroppedSmall); }
  [Fact] public void MaxBoxes_Keeps_Largest() { var r = BoxSimplifier.Simplify(Parts(), new SimplifyOptions(MaxBoxes: 1, MinPartPct: 0)); Assert.Equal("body", r.Boxes.Single().Name); Assert.Equal(2, r.DroppedOverMax); }
  [Fact] public void Grid_Splits_Single_Part() { var r = BoxSimplifier.Simplify(new[]{ Parts()[0] }, new SimplifyOptions(Grid: 2, MinPartPct: 0)); Assert.Equal(4, r.Boxes.Count); }
  ```
- [ ] **Step 2: 실행 FAIL.**
- [ ] **Step 3: 구현.** 순서: 단위 환산 → 축 재배열(Heading: 원본 전진축 → 그림 −y, 원본 위 z 유지, 오른손 좌표 유지) → `RotateDeg`(z축, 90° 단위) → 부품별 AABB → 바닥 평행이동(전체 최소 z → 0) → 원점(Front: 최소 y → 0, Back: 최대 y → 0, Center: 중앙) → 작은 부품 제거(`MinPartPct`, 전체 길이 = 진행축 길이) → 부피 큰 순 `MaxBoxes`. 이름은 부품 이름을 `[A-Za-z0-9_]`로 정리하고 중복은 `_2`.
- [ ] **Step 4: 실행 PASS.** 커밋 `feat(core): BoxSimplifier — 부품별 축 정렬 상자, 방향·원점·크기 필터`.

### Task 6: `CelWriter`/`CelReader` — 독립 `.cel`과 차량 세그먼트 텍스트

**Files:** `src/ASH.Core/Cel/CelWriter.cs`(두 클래스), `src/ASH.Core/Export/VehicleGeometry.cs`(조립부 추출), `tests/ASH.Tests/Cel/CelWriterTests.cs`

**Interfaces:**
```csharp
namespace ASH.Core.Cel;
public static class CelWriter
{
    /// <summary>A stand-alone cell file (10000 / 7000 / 700 group / 310 boxes), feet, LF line ends; the AutoMod editor opens it.</summary>
    public static string Cel(string cellName, IReadOnlyList<Box> boxes);
    /// <summary>The AGVVEHSEG text: the template (agvs_vehseg.txt) with Geom1's children replaced by the boxes (feet), the lift joint on the top of the
    /// highest box and the CONTAINER there (loadTopM = max(Z0 + H)); same layout as VehicleGeometry.Build.</summary>
    public static string VehSeg(IReadOnlyList<string> template, IReadOnlyList<Box> boxes);
}
public static class CelReader
{
    /// <summary>The 310 boxes of a cell written by CelWriter (feet → metres); other types are skipped. FormatException on a short block.</summary>
    public static IReadOnlyList<Box> ReadBoxes(string celText);
}
```
`VehicleGeometry`: 상자 목록을 템플릿에 끼우는 꼬리 부분(`res` 조립, Lift_Joint, Top, Link1, CONTAINER)을 `internal static List<string> Assemble(IReadOnlyList<string> template, IReadOnlyList<IEnumerable<string>> parts, double topM, double containerXM)`로 빼고 `Build`와 `CelWriter.VehSeg`가 같이 쓴다. `Prim`은 `internal`로.

- [ ] **Step 1: 실패 테스트.**
  ```csharp
  static Box[] Two() => new[]{ new Box("body",0,0,0,0.8,1.2,0.5), new Box("wheel",0.4,-0.4,0,0.05,0.1,0.1,0) };
  [Fact] public void Cel_Has_Header_And_Child_Count() { var l = CelWriter.Cel("agv_cell", Two()).Split('\n'); Assert.Equal("10000 5", l[0]); Assert.Contains(" agv_cell", l[1]); Assert.Equal("7000 21", l[6]); Assert.Equal("700 17", l[12]); Assert.Equal("2", l[14]); Assert.Equal(2, l.Count(x => x == "310 5")); }
  [Fact] public void Cel_Size_Line_Is_Y_First_In_Feet() { var l = CelWriter.Cel("c", Two()).Split('\n'); var size = l[Array.IndexOf(l, "310 5") + 5].Split(' '); Assert.Equal(1.2 * 3.280839895, double.Parse(size[0], CultureInfo.InvariantCulture), 4); Assert.Equal(0.8 * 3.280839895, double.Parse(size[1], CultureInfo.InvariantCulture), 4); }
  [Fact] public void VehSeg_Child_Count_And_Container_Top() { var l = CelWriter.VehSeg(Template(), Two()).Split('\n'); int g = Array.FindIndex(l, x => x.EndsWith(" Geom1")); Assert.Equal("3", l[g + 1]); /* 2 boxes + Lift_Joint */ Assert.Contains(l, x => x.Contains("trz " + F(0.5 * 3.280839895))); Assert.StartsWith("AGVVEHSEG name DefSegment", l[0]); }
  [Fact] public void Reader_Round_Trips_Metres() { var b = CelReader.ReadBoxes(CelWriter.Cel("c", Two())); Assert.Equal(2, b.Count); Assert.Equal(1.2, b[0].Dy, 6); Assert.Equal(-0.4, b[1].Y, 6); Assert.Equal(0, b[1].Color); }
  [Fact] public void VehicleGeometry_Build_Unchanged() { /* GeometryTests의 기존 단언이 그대로 통과(리팩터 보호) */ }
  ```
- [ ] **Step 2: 실행 FAIL** (`--filter "CelWriterTests|GeometryTests"`).
- [ ] **Step 3: 구현.** `Cel`: 머리 3단은 `cube.cel`/`agvk.cel` 모양(이름 줄 `9 9 0 1 0 <name>`, 위치·회전·스케일 줄, 자식 수 1·1·n). 상자는 `VehicleGeometry.Prim`(플래그 5, 색 = `Box.Color`). `VehSeg`: `Assemble` 호출, `FloorRel` 보정은 `Assemble` 안에서. 숫자 `0.######` Invariant.
- [ ] **Step 4: 실행 PASS.** `GeometryTests`·`LayoutExporterTests` 전체 PASS 확인. 커밋 `feat(core): CelWriter/CelReader — 상자 목록의 독립 .cel과 차량 세그먼트 텍스트`.

### Task 7: `SvgPreview` — 위·옆·사선 SVG

**Files:** `src/ASH.Core/Cel/SvgPreview.cs`, `tests/ASH.Tests/Cel/SvgPreviewTests.cs`

**Interfaces:**
```csharp
namespace ASH.Core.Cel;
public static class SvgPreview
{
    /// <summary>One SVG (width x height px) with three panels: top (x→right, front(-y)→up), side (y→right, z→up), isometric (cabinet projection 30°);
    /// boxes drawn far-to-near, fill from the AutoMod colour index, 1 px outline; a 1 m scale bar and an arrow marking the front. No external resources.</summary>
    public static string Render(IReadOnlyList<Box> boxes, int width = 900, int height = 300);
}
```

- [ ] **Step 1: 실패 테스트.**
  ```csharp
  [Fact] public void Three_Panels_And_All_Boxes() { var s = SvgPreview.Render(Two()); Assert.StartsWith("<svg", s); Assert.Equal(3, Regex.Matches(s, "<g class=\"panel\"").Count); Assert.Equal(3 * 2, Regex.Matches(s, "<rect class=\"box\"").Count + Regex.Matches(s, "<path class=\"box\"").Count); Assert.DoesNotContain("http://", s.Replace("http://www.w3.org/2000/svg", "")); }
  [Fact] public void Front_Is_Up_In_Top_View() { /* lamp at y=-0.5 gets a smaller svg y than body centre in the first panel */ }
  [Fact] public void Empty_List_Still_Renders() { Assert.Contains("상자 없음", SvgPreview.Render(Array.Empty<Box>())); }
  ```
- [ ] **Step 2: FAIL → Step 3: 구현**(색 표는 `VehicleGeometry`의 AutoMod 표준색 0~15 → hex 고정 배열; 텍스트는 한국어 라벨 "위·옆·사선") **→ Step 4: PASS.** 커밋 `feat(core): SvgPreview — 상자 목록의 위·옆·사선 SVG`.

### Task 8: `AssetConverter`(Core) + `AssetStore`(Server)

**Files:** `src/ASH.Core/Cel/AssetConverter.cs`, `src/ASH.Server/Catalog/AssetStore.cs`, `tests/ASH.Tests/Cel/AssetConverterTests.cs`, `tests/ASH.Server.Tests/AssetStoreTests.cs`

**Interfaces:**
```csharp
namespace ASH.Core.Cel;
public sealed record ConvertResult(IReadOnlyList<Box> Boxes, string Cel, string Svg, SimplifyResult Stats);
public static class AssetConverter
{
    /// <summary>MeshReader → BoxSimplifier → CelWriter.Cel(cellName) → SvgPreview. Exceptions pass through (FormatException, NotSupportedException, ArgumentException).</summary>
    public static ConvertResult Convert(string sourcePath, string cellName, SimplifyOptions o);
}
```
```csharp
namespace ASH.Server.Catalog;
public sealed class AssetInfo
{
    public string Id { get; set; } = "";          // "s-" + 8 hex
    public string TypeName { get; set; } = "";
    public string Name { get; set; } = "";        // unique within the type, [A-Za-z0-9_] (Excel Asset column value)
    public string Note { get; set; } = "";
    public int ConveyorDir { get; set; }          // 0/90/180/270, memo only
    public int Boxes { get; set; }
    public string Source { get; set; } = "";      // original file name
    public SimplifyOptions Options { get; set; } = new();
    public double LengthM { get; set; } public double WidthM { get; set; } public double HeightM { get; set; }
    public DateTime CreatedUtc { get; set; } public DateTime UpdatedUtc { get; set; } public string UpdatedBy { get; set; } = "";
}
/// <summary>&lt;Jobs:Dir&gt;\catalog\assets\&lt;id&gt;\{source.&lt;ext&gt;, asset.cel, preview.svg, asset.json}; in memory like JobStore; a folder with a bad asset.json is logged and skipped.</summary>
public sealed class AssetStore
{
    public AssetStore(string jobsDir, ILogger<AssetStore> log);
    public string Dir { get; }
    public IReadOnlyList<AssetInfo> All();
    public IReadOnlyList<AssetInfo> ForType(string typeName);
    public AssetInfo? Find(string id);
    public AssetInfo? FindByName(string typeName, string name);
    /// <summary>Copies the upload to source.&lt;ext&gt;, converts, writes the three files. ArgumentException for a bad name or a duplicate (type, name); conversion exceptions pass through and nothing is left behind.</summary>
    public AssetInfo Create(string typeName, string name, string note, int conveyorDir, string uploadPath, string originalFileName, SimplifyOptions o, string by);
    /// <summary>Re-runs the conversion on the kept source with new options/name/note; previous asset.cel/asset.json go to .bak.</summary>
    public AssetInfo Reconvert(string id, string name, string note, int conveyorDir, SimplifyOptions o, string by);
    public bool Delete(string id);              // whole folder
    public string Path(string id, string file); // "asset.cel" | "preview.svg" | "asset.json"
}
```

- [ ] **Step 1: 실패 테스트.** Core: `AssetConverterTests.Stl_To_Cel_And_Svg`(픽스처 STL 임시 파일 → `Boxes.Count == 2`, `Cel` 첫 줄 `10000 5`, `Svg` `<svg`로 시작). Server: 임시 폴더에 `new AssetStore(dir, NullLogger)`:
  ```csharp
  [Fact] public void Create_Writes_Four_Files_And_Reloads() { var a = st.Create("T_AGV","agv_a","",0, stlPath,"agv.stl", new SimplifyOptions(), "admin"); foreach (var f in new[]{"source.stl","asset.cel","preview.svg","asset.json"}) Assert.True(File.Exists(Path.Combine(st.Dir, a.Id, f)), f); var st2 = new AssetStore(dir, log); Assert.Equal("agv_a", st2.Find(a.Id)!.Name); Assert.Single(st2.ForType("T_AGV")); }
  [Fact] public void Duplicate_Name_In_Type_Rejected() { st.Create("T_AGV","agv_a",…); Assert.Throws<ArgumentException>(() => st.Create("T_AGV","agv_a",…)); st.Create("T_AMR","agv_a",…); /* other type ok */ }
  [Fact] public void Bad_Name_Rejected() { Assert.Throws<ArgumentException>(() => st.Create("T_AGV","agv a",…)); }
  [Fact] public void Reconvert_Keeps_Bak() { var a = st.Create(…); st.Reconvert(a.Id, "agv_b", "", 90, new SimplifyOptions(Origin: OriginAt.Front), "admin"); Assert.True(File.Exists(Path.Combine(st.Dir, a.Id, "asset.cel.bak"))); Assert.Equal("agv_b", st.Find(a.Id)!.Name); }
  [Fact] public void Failed_Convert_Leaves_No_Folder() { Assert.ThrowsAny<Exception>(() => st.Create("T_AGV","x","",0, badObjPath,"x.obj", new(), "a")); Assert.Empty(Directory.GetDirectories(st.Dir)); }
  ```
- [ ] **Step 2: FAIL → Step 3: 구현**(`Program.cs`에 `AddSingleton(sp => new AssetStore(sp.GetRequiredService<JobStore>().Dir, …))` 1줄; 이름 검사 정규식 `^[A-Za-z0-9_]{1,40}$`; 임시 폴더에 만든 뒤 `Directory.Move`로 원자화) **→ Step 4: PASS.** 커밋 `feat(core): AssetConverter` / `feat(server): AssetStore — catalog\assets 보관`.

### Task 9: `Routes.Assets.cs` — 업로드·재변환·삭제·API·관리자 절

**Files:** `src/ASH.Server/Routes.Assets.cs`, `src/ASH.Server/Catalog/AssetPages.cs`, `src/ASH.Server/Program.cs`(`app.MapAssets();` 1줄), B의 `/admin/types` 렌더 호출부(1줄: `AssetPages.Section(types, assetStore)` 삽입), `tests/ASH.Server.Tests/AssetRouteTests.cs`

**Interfaces:**
```csharp
namespace ASH.Server;
public static class AssetRoutes
{
    public const long AssetUploadLimit = 50L * 1024 * 1024;   // Catalog:AssetMaxMb overrides
    public static void MapAssets(this WebApplication app);
}
// GET  /api/catalog/assets?type=T            → {assets:[{id,typeName,name,note,conveyorDir,boxes,source,lengthM,widthM,heightM,createdUtc}]}
// GET  /api/catalog/assets/{id}/asset.cel     → text/plain; 404 Msg
// GET  /api/catalog/assets/{id}/preview.svg   → image/svg+xml
// POST /admin/assets            (admin, multipart: type,name,note,conveyorDir,file + minPartPct,maxBoxes,heading,origin,rotateDeg,sourceUnit,grid) → 303 /admin/types#asset-{id}; 400 Msg on error
// POST /admin/assets/{id}/reconvert (admin, form, same option fields)            → 303 /admin/types#asset-{id}
// POST /admin/assets/{id}/delete    (admin)                                      → 303 /admin/types ; also TypeStore.RemoveAsset
public static class AssetPages
{
    /// <summary>The 에셋 section of /admin/types: per type a table (name, boxes, size, source, conveyor dir, note, updated) with the inline preview.svg,
    /// a reconvert form per asset, one upload form (type select, file, options with the defaults, help text "STEP은 CAD에서 STL로 내보내 올리세요").</summary>
    public static string Section(IReadOnlyList<Catalog.AgvTypeDef> types, AssetStore assets);
}
```

- [ ] **Step 1: 실패 테스트**(`JobEnv(adminToken: "…")`, 헤더 토큰으로 관리자):
  ```csharp
  [Fact] public async Task Upload_Then_Cel_And_List() { var r = await PostAsset(env, "T_AGV", "agv_a", stlBytes, "agv.stl"); Assert.Equal(HttpStatusCode.SeeOther, r.StatusCode); var id = IdFrom(r.Headers.Location!); var cel = await env.Http.GetStringAsync($"/api/catalog/assets/{id}/asset.cel"); Assert.StartsWith("10000 5", cel); var list = await env.Http.GetStringAsync("/api/catalog/assets?type=T_AGV"); Assert.Contains("\"agv_a\"", list); Assert.Contains(id, env.Types.Find("T_AGV")!.Assets); }
  [Fact] public async Task Upload_Without_Admin_Is_401() { … Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode); }
  [Fact] public async Task Over_Limit_Is_413() { /* env with Catalog:AssetMaxMb=0.001, 2 KB file */ Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.StatusCode); }
  [Fact] public async Task Bad_Extension_Is_400() { var r = await PostAsset(env, "T_AGV", "x", new byte[10], "x.step"); Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode); Assert.Contains("STL", await r.Content.ReadAsStringAsync()); }
  [Fact] public async Task Delete_Removes_Files_And_Type_Link() { … Assert.Equal(HttpStatusCode.NotFound, (await env.Http.GetAsync($"/api/catalog/assets/{id}/asset.cel")).StatusCode); }
  [Fact] public async Task Admin_Types_Page_Shows_Section() { var html = await env.Http.GetStringAsync("/admin/types"); Assert.Contains("id=\"assets\"", html); Assert.Contains("<svg", html); }
  ```
  `JobEnv`에 `Types`(TypeStore) 접근자 1줄 추가(B가 이미 넣었으면 생략).
- [ ] **Step 2: FAIL → Step 3: 구현.** 업로드는 `IHttpMaxRequestBodySizeFeature.MaxRequestBodySize = limit` + `file.Length > limit → 413`. 업로드 파일은 임시 경로에 저장 뒤 `AssetStore.Create`, 성공 시 `TypeStore.AddAsset`. 확장자 `.stl/.obj`만. 옵션 파싱 실패는 400 한국어 문구. 관리자 판정은 `AdminAuth.IsAdmin`(Global Constraints).
- [ ] **Step 4: PASS.** 커밋 `feat(server): 에셋 라우트·관리자 절 — 업로드·재변환·삭제·asset.cel API`.

### Task 10: Core 입력 — `AgvType.Asset` 읽기·양식 드롭다운

**Files:** `src/ASH.Core/Input/ProjectInput.cs`, `src/ASH.Core/Input/ExcelReader.cs`, `src/ASH.Core/Input/ExcelTemplate.cs`, `tests/ASH.Tests/Input/AssetColumnTests.cs`

**Interfaces:**
```csharp
// ProjectInput.cs — 선택 인자를 끝에 더한다(B의 Curve 속도 인자 뒤). 기존 호출 그대로.
public sealed record AgvType(string Type, double SpeedLoaded, double SpeedEmpty, double Accel, double Decel, int Capacity, double LoadTime, double UnloadTime,
    double MTBF, double MTTR, double Length, double Width, string Color, int Row = 0, string Kind = "", /* B의 열들 */ string Asset = "");
// ExcelReader: r.StrIfColumn("Asset") → Asset (열이 없으면 "")
// ExcelTemplate: 기존 데이터 검증 목록에 ("AGV_Types", "Asset", <comma list>)를 넣는 진입점
public static class ExcelTemplate // 기존 클래스에 속성 1개 추가
{
    /// <summary>The comma list of the Asset drop-down (names across all types; empty = no drop-down). Set by the plugin from the catalog before Create/Update.</summary>
    public static string AssetListFormula { get; set; } = "";
}
```

- [ ] **Step 1: 실패 테스트.** `AssetColumnTests.Reader_Reads_Asset_When_Column_Exists`(워크북에 `Asset` 열 추가 → `AgvTypes[0].Asset == "agv_a"`), `Reader_Without_Column_Is_Empty`, `Template_Adds_Asset_Validation_When_List_Set`(`AssetListFormula = "agv_a,agv_b"`로 `Create` → 시트 `AGV_Types` `Asset` 열 데이터 검증 목록 포함; 비어 있으면 검증 없음).
- [ ] **Step 2: FAIL → Step 3: 구현**(E장: `Asset` 열 이름은 B가 넣음. B가 아직 안 넣었으면 열 배열에 끝 항목 `"Asset"`/`"에셋(목록에서 선택, 빈칸=기본 그림)"`/`""` 추가) **→ Step 4: PASS.** 커밋 `feat(core): AGV_Types.Asset 읽기와 양식 드롭다운`.

### Task 11: 플러그인 — AGVEXCEL 드롭다운, AGVMODEL에서 `.cel` 받아 차량 그림으로

**Files:** `src/ASH.Plugin/Commands/ModelCommands.cs`, `src/ASH.Plugin/Commands/AssetClient.cs`, `tests/ASH.Tests/Cel/AssetOverrideTests.cs`(Core 쪽 순수 함수 테스트), `src/ASH.Core/Cel/AssetOverrides.cs`

**Interfaces:**
```csharp
// Core (netstandard2.0, 플러그인·테스트가 같이 씀)
namespace ASH.Core.Cel;
public static class AssetOverrides
{
    /// <summary>For each AgvType with Asset != "": fetch(typeName, assetName) → cel text or null; null/exception → one warning line and no override for that type.
    /// Returns VehSegText overrides keyed by type name (template = agvs_vehseg.txt).</summary>
    public static Dictionary<string, ASH.Core.Export.VehicleTemplateOverride> Build(IReadOnlyList<ASH.Core.Input.AgvType> types, IReadOnlyList<string> template,
        Func<string, string, string?> fetchCel, ICollection<string> warnings);
}
```
```csharp
// Plugin
namespace ASH.Plugin.Commands;
internal static class AssetClient
{
    /// <summary>GET /api/catalog/assets?type=… then /api/catalog/assets/{id}/asset.cel; 3 s per call; saves to &lt;model&gt;.arc\assets\&lt;name&gt;.cel and returns its text; null when the server is unset, unreachable, or the name is missing.</summary>
    public static string? Fetch(string serverUrl, string arcDir, string typeName, string assetName, out string? why);
    /// <summary>Asset names of all types from GET /api/catalog/types (B's CatalogClient), "" when unreachable.</summary>
    public static string AssetListFormula(string serverUrl);
}
```

- [ ] **Step 1: 실패 테스트(Core).**
  ```csharp
  [Fact] public void Asset_Type_Gets_Override_Others_Not() { var t = new[]{ Type("T_A", "agv_a"), Type("T_B", "") }; var w = new List<string>(); var o = AssetOverrides.Build(t, Template(), (ty, n) => CelWriter.Cel("c", Two()), w); Assert.Single(o); Assert.StartsWith("AGVVEHSEG name DefSegment", o["T_A"].VehSegText); Assert.Empty(w); }
  [Fact] public void Missing_Asset_Warns_And_Falls_Back() { var w = new List<string>(); var o = AssetOverrides.Build(new[]{ Type("T_A","nope") }, Template(), (_, _) => null, w); Assert.Empty(o); Assert.Contains(w, x => x.Contains("nope") && x.Contains("기본 그림")); }
  [Fact] public void Fetch_Exception_Is_A_Warning_Not_A_Throw() { var w = new List<string>(); AssetOverrides.Build(new[]{ Type("T_A","a") }, Template(), (_, _) => throw new IOException("x"), w); Assert.Single(w); }
  ```
- [ ] **Step 2: FAIL → Step 3: 구현.** Core `Build`: `CelReader.ReadBoxes` → `CelWriter.VehSeg`. 플러그인: `ModelCommands.AgvExcel`에서 `ExcelTemplate.AssetListFormula = AssetClient.AssetListFormula(url)`(B의 카탈로그 읽기 바로 뒤); `AgvModel`(line ~118 부근)에서 `input = ExcelReader.Read(...)` 뒤 `var ov = AssetOverrides.Build(input.AgvTypes, template, (ty, n) => AssetClient.Fetch(url, arcDir, ty, n, out _), warnings)`를 `ModelBuildRequest(VehicleOverrides: ov.Count > 0 ? ov : null)`로. 경고는 기존 메시지 출력(`Say`)으로 한 줄씩. `AssetClient`는 `ServerCommands`의 `Run(task, timeout)` 패턴과 `JobServerClient`의 HttpClient를 재사용. `.cel`은 `<Model>.arc\assets\`에 두어 AGVSEND 패키지에 함께 올라간다(서버는 `.arc`를 그대로 빌드).
- [ ] **Step 4: PASS**(Core 테스트) + 플러그인 빌드 `dotnet build src/ASH.Plugin -c Release`. 수동 확인: 서버 끈 채 AGVMODEL → 경고 없이 기본 그림(Asset 빈칸), Asset 지정 + 서버 없음 → 경고 1줄 + 기본 그림. 커밋 `feat(plugin): AGVEXCEL Asset 드롭다운, AGVMODEL에서 에셋 .cel 받아 차량 그림으로`.

### Task 12: 결과 화면 입력 탭에 에셋 이름

**Files:** `src/ASH.Server/JobPages.cs`(입력 탭 AGV 타입 표), `tests/ASH.Server.Tests/JobPagesAssetTests.cs`

**Interfaces:** 없음(렌더 1곳). `JobView.Input.AgvTypes[i].Asset`을 읽는다(Task 10).

- [ ] **Step 1: 실패 테스트.** `input.cells.tsv`에 `Asset` 열 `agv_a`가 있는 패키지로 `/jobs/{id}?tab=input` → `Assert.Contains("<th>에셋</th>", html)`, `Assert.Contains("agv_a", html)`; 열이 없는 옛 패키지 → 셀 `"기본 그림"`.
- [ ] **Step 2: FAIL → Step 3: 구현**(타입 표에 열 1개; 빈 값은 "기본 그림") **→ Step 4: PASS.** 커밋 `feat(server): 결과 입력 탭에 에셋 이름`.

### Task 13: 마무리 — 관문 결과 반영·릴리스 메모

**Files:** 스펙 "관문 결과", `docs/gates.md`, `CHANGELOG`/릴리스 메모(저장소 관례 파일)

- [ ] **Step 1:** 스펙 "관문 결과"의 C-G1~G3 항목에 구현 뒤 확인한 사실(실제 업로드 STL 1건으로 만든 에셋이 이 PC에서 빌드·실행됨)을 한 줄씩 보강.
- [ ] **Step 2:** 전체 테스트 `dotnet test` PASS. 0.22.0 메모에 "에셋: STL/OBJ → .cel, 미리보기; STEP은 STL로 내보내 사용" 추가. 커밋 `docs: 에셋(.cel) 관문 결과·릴리스 메모`.
