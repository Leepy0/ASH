# 서버 모델 생성 (AGVSEND가 모델 대신 원본을 보낸다)

## 목표

생성기 수정이 서버 업데이트만으로 반영되게 한다. 플러그인은 도면·엑셀을 읽고 검증만 하고, 모델(`<Model>.arc`)은 서버가 만든다.

## 지금

- `AGVMODEL`(PC): `LoadInput`(엑셀 → `ProjectInput`, `input.cells.tsv` 스냅샷) + `DrawingReader.Read`(도면 → `Layout`) + `ash.settings.tsv`(Units, Origin, Scale, Groups) + 서버에서 받은 차량 에셋(.cel) → `ModelBuilder.Build(..., amHome: null)` → `.arc`. 입력 오류는 엑셀 칸에 표시(`MarkExcel`).
- `AGVSEND`: `.arc` + `input.cells.tsv` + `ash.settings.tsv` + drawing.dxf/dwg + 좌표 틀을 zip으로 올림(`JobPackage.Create`). `.arc` 없으면 거절.
- 서버 `/api/jobs`: `JobPackage.Extract`(필수 = `.arc` 3개 + `input.cells.tsv`) → 작업 등록 → 워커가 빌드·탐색. 다시 돌리기(`Routes.Rerun`)는 upload.zip을 복사해 다시 Extract.

## 바뀐 뒤

### 패키지 두 형식

| 형식 | 필수 파일 | 누가 |
|---|---|---|
| 모델(기존) | `<M>.arc/agv.asy`, `<M>.arc/model.amo`, `<M>.arc/<M>~.asy`, `input.cells.tsv` | 예전 플러그인, `AGVRESULTUP`(로컬 결과는 그 모델 그대로) |
| 원본(새) | `layout.tsv`, `input.cells.tsv`, `ash.settings.tsv` | 새 플러그인의 `AGVSEND` (서버가 `generates: true`일 때) |

선택 파일(drawing.dxf, drawing.dwg, 좌표 틀)은 두 형식 모두 그대로.

### 서버

- `Extract`가 두 형식 중 하나를 요구한다.
- 원본 형식이고 `.arc`가 없으면 **접수 요청 안에서** 모델을 만든다(수 초). 입력 오류는 HTTP 400과 오류 목록으로 돌려줘 플러그인 명령줄에 바로 보인다. 작업 폴더는 지운다.
- 만든 뒤에는 지금과 똑같이 `.arc`가 프로젝트 폴더에 있으므로 워커·영상·생산량 한계·결과 화면은 손대지 않는다.
- `job.log` 첫 줄들: `모델 생성: 서버 v<버전>` + 경고들. `JobInfo.ServerGenerated = true`.
- 다시 돌리기는 upload.zip에 `.arc`가 없으니 자동으로 **지금 서버 생성기로 다시 만든다**.
- 차량 에셋: 서버 `AssetStore`에서 타입·이름으로 .cel을 직접 찾는다(플러그인의 `AssetClient.Fetch`와 같은 판정: 없음/상자 0개/못 읽음 → 경고 후 기본 그림). `.arc/assets/<name>.cel`에 저장.
- `/api/queue` 응답에 `generates: true`.

### 플러그인

- `AGVSEND`: 서버가 `generates: true`면 `AGVMODEL` 선행 없이 바로
  1. 설정 로드, 서버 대기열 조회(지금처럼)
  2. 문서 문맥에서 `LoadInput`(엑셀 → 입력 + 셀 스냅샷), `DrawingReader.Read`, `CheckCommands.Report` 차단 오류 질문(AGVMODEL과 같은 문구)
  3. `ModelBuilder.Validate`로 입력 검증 → `MarkExcel` → 입력 오류가 있으면 보내지 않음
  4. `layout.tsv` 쓰기, `JobPackage.CreateSource`로 zip
  5. 이후(대화상자, DWG, 업로드, 브라우저)는 지금과 같음
- 서버가 `generates`를 모르면(예전 서버) 지금처럼 `.arc` 필수 경로.
- `AGVMODEL`은 로컬 실행(`AGVRUN`)용으로 그대로 남는다. 끝의 "서버로 보내기" 제안도 그대로(이제 원본을 보냄).
- `AGVRESULTUP`은 모델 형식 그대로.

## Core 계약 (먼저 커밋, 서버·플러그인이 이것만 쓴다)

```csharp
namespace ASH.Core.Layout;
/// layout.tsv: 첫 줄 "ash-layout\t1". 이후 한 줄 = 한 객체, 탭 구분, 숫자는 InvariantCulture "R".
public static class LayoutSnapshot
{
    public const string FileName = "layout.tsv";
    public static void Write(Layout l, string path);
    public static Layout Read(string path);           // 형식 오류 → FormatException(줄 번호 포함)
}

namespace ASH.Core.Model;
public static class ModelBuilder
{
    // Build 41~44행을 꺼낸 것. Build도 이걸 부른다.
    public static List<Finding> Validate(ProjectInput input, Layout.Layout l, double scale);
}

/// 원본 형식 폴더에서 모델 만들기: cells → ProjectInput, ash.settings.tsv → ExportOptions(AGVMODEL과 같은 규칙: 엑셀 Groups가 있으면 VehicleGroups, 없으면 settings Groups),
/// layout.tsv → Layout, 에셋은 cel(typeName, assetName) 콜백(null = 없음). ModelBuilder.Build(amHome: null).
public static class SourceBuild
{
    public static ModelBuildResult Run(string projectDir, string model, Func<string, string, string?>? cel, List<string> warnings);
}

namespace ASH.Core.Runner;
public static class JobPackage
{
    public static IReadOnlyList<string> RequiredSource { get; } // layout.tsv, input.cells.tsv, ash.settings.tsv
    public static bool IsSource(string projectDir, string model);   // 원본 파일 있고 .arc 필수 파일 없음
    public static IReadOnlyList<PackageFile> CreateSource(string projectDir, string zipPath, string? dwgPath = null);
    // Extract: Required(model) 또는 RequiredSource 중 하나가 다 있으면 통과
}
```

`layout.tsv` 줄 종류(첫 칸):

- `tol <tolerance>`
- `path <name> <id> <flags: s=spur t=two-way c=crab> <seg>...` — seg = `L x1 y1 x2 y2` 또는 `A cx cy sx sy sweep` (각 값은 탭으로 이어 붙임)
- `cp <name> <type> <group> <cap> <x> <y> <id> <routeId>`
- `block <name> <cap> <minx> <miny> <maxx> <maxy> <id> <tagId>`
- `station <name> <x> <y>`
- `asset <kind> <x> <y> <rot> <sx> <sy> <widthM> <seg>...`
- `conv <name> <graphicOnly 0/1> <widthM> <id> <consumeAtM 또는 빈칸> <seg>...`

이름·문자열 칸의 탭·줄바꿈은 공백으로 바꾼다(도면 이름에 없지만 방어). 왕복 시험: 모든 필드가 같아야 함(Segment는 record 동등).

## 일 나누기

1. **Core (Opus)**: 위 계약 전부 + 시험(왕복, 형식 오류, Validate가 Build와 같은 판정, SourceBuild가 AGVMODEL 경로와 같은 `.arc` 파일을 만드는지 — 같은 입력으로 `ModelBuilder.Build` 직접 호출 결과와 파일 비교, Extract 두 형식).
2. **서버 (Sonnet)**, Core 뒤: `/api/jobs`·`Routes.Rerun`에서 `IsSource`면 `SourceBuild.Run`(에셋 = AssetStore), 입력 오류 → 400 + 폴더 삭제, job.log 줄, `JobInfo.ServerGenerated`, `/api/queue`의 `generates`. 작업 페이지 파일/상태 탭에 "서버에서 생성" 한 줄. 시험: 원본 zip 접수 → `.arc` 생김, 오류 입력 → 400, 다시 돌리기 → 재생성.
3. **플러그인 (Sonnet)**, Core 뒤, 서버와 동시: `ServerCommands.Send`의 원본 경로, `JobServerClient` 대기열 응답의 `generates`, `ModelCommands`에서 검증·엑셀 표시를 재사용할 수 있게 꺼내기. 문서: `docs/server-install.md`, `README-server.txt`, `docs/manual/05`(보내기), `docs/plugin-manual-test.md`(시험 행 추가).

## 하지 않는 것

- 서버에서 DXF/DWG를 읽어 레이아웃을 만들기(XData·블록 속성은 플러그인 `DrawingReader`만 읽는다).
- 플러그인 `AGVMODEL` 제거.
