# 도면 경량화(AGVLITE) 재설계 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 컨베이어·다관절 로봇·터치 판넬 같은 복잡한 3D 블록이 겹겹이 쌓인 100~300 MB 공장 도면을, 시뮬레이션 배경과 경로 작도에 맞는 5 MB 이하의 평면 R12 DXF로 만든다. 높이(Z)가 있는 것은 모두 평면으로 투영하고, 눈에 보이지 않는 작은 요소와 겹친 선은 지우고, 무거운 블록은 외곽선으로 바꾼다.

**Architecture:** 플러그인(`src/ASH.Plugin`)은 ZWCAD에서 **블록 정의를 한 번씩만** 읽어 3D 기하로 넘기고, 인스턴스는 (정의, 3D 행렬, 레이어)만 기록한다. 정리(평탄화 → 퇴화 제거 → 중복·겹침 제거 → 체인 결합·단순화 → 작은 요소 제거 → 블록 외곽선)는 전부 Core(`src/ASH.Core/Lite`)의 순수 함수로 두어 ZWCAD 없이 테스트한다. 예상 크기는 정의별 정리 결과 × 인스턴스 수로 계산하므로 레이어·옵션을 바꿔도 즉시 갱신된다. 쓰기는 스트리밍이며 인스턴스 사이의 중복은 쓰는 순간 지운다.

**Tech Stack:** C# (Core netstandard2.0, 플러그인 net48 WinForms, ZWCAD.NetApi 20.26.0), xunit. 빌드 PC에 ZWCAD가 없으므로 플러그인 쪽은 수동 테스트 기록표로 검증한다.

**Spec:** `docs/superpowers/specs/2026-10-20-ash-plugin-design.md` §5 (이 계획으로 §5를 고쳐 쓴다, Task 9)

---

## 0. 현재 구현 진단 (0.20.0, `c792e00`·`28f1b77`)

지금 있는 것: 모델 공간 읽기(블록 8단계 중첩 펼침, 작업 예산 2,000,000), 문자·해치·치수·이미지 항상 제거, 외부참조·3D 범위 상자·꺼진 레이어 옵션, 레이어 선택 대화상자와 레이어별 예상 크기, 원점 이동, R12 ASCII 쓰기, 보고서 TSV. 곡선 근사 수학(`CurveApprox`)과 R12 레이어 이름 규칙, 보고서, 원점 이동, 레이어 대화상자는 그대로 쓴다.

요구사항(작은 요소 삭제, 높이 평탄화, 시뮬레이션용 정리) 기준으로 모자란 점:

| # | 문제 | 어디 | 영향 |
|---|---|---|---|
| D1 | **평탄화가 2D 아핀 지름길.** `ToAffine`이 3D 행렬의 z 행·열을 버린다. X축으로 90° 돌려 세운 서브블록(로봇 팔, 판넬 면)은 선 하나로 붕괴하거나 엉뚱한 위치에 찍힌다 | `LiteCommands.Extractor.ToAffine` | 3D로 만든 설비 블록이 깨진다 |
| D2 | **수직 모서리가 0길이 LINE으로 남는다.** `Line`은 Z만 버리므로 같은 XY의 수직선이 점이 된다. 수직 평면의 원·호는 선분 위에 눌린 폴리라인이 된다 | `Add(Line)`, `Sample` | 쓸모없는 엔티티가 수만 개 |
| D3 | **중복·겹침 제거가 없다.** 상자의 윗면·아랫면 모서리, 층층이 쌓인 같은 블록, 레이어만 다른 같은 선이 전부 따로 저장된다 | 없음 | 3D 도면에서 크기의 절반 이상이 중복 |
| D4 | **작은 요소 제거가 없다.** 볼트 구멍, 모따기, 로봇 내부 선, 판넬 버튼이 그대로 남는다 | 없음 | 요구사항 미충족 |
| D5 | **3D 솔리드·메시는 "제거" 또는 "범위 상자"뿐.** 컨베이어·로봇 솔리드의 모양이 사라지거나 사각형만 남는다 | `Add(Solid3d…)` | 배경으로 못 쓴다 |
| D6 | **인스턴스마다 전부 메모리에 펼친다.** 2,000,000개 예산을 넘으면 잘리고(`Truncated`), 블록 단위로 무엇이 무거운지 볼 수단이 없다 | `Walk`, `LiteSource.Items` | 300 MB 도면에서 일부 누락·메모리 급증 |
| D7 | **좌표가 항상 소수 4자리.** mm 도면에서 0.1 µm 정밀도라 바이트를 낭비한다 | `LiteDxfWriter.Num` | 크기 20~30 % 손해 |
| D8 | **진행 표시·취소가 없다.** 큰 도면에서 몇 분간 멈춘 것처럼 보인다 | `Run` | 사용성 |
| D9 | **설정이 저장되지 않는다.** 도면이 개정될 때마다 레이어 체크를 다시 한다 | 없음 | 재현성 |
| D10 | **폴리라인 단순화·체인 결합이 없다.** 스플라인·타원을 잘게 샘플한 점, 다른 CAD에서 들어와 수백 개 짧은 선으로 쪼개진 곡선이 그대로다 | 없음 | 크기, 그리고 D4를 안전하게 할 전제 |
| D11 | **실제 도면 측정이 없다.** 테스트는 합성 데이터뿐 | `tests/ASH.Tests/Lite` | 기본값 근거 없음 |

---

## 1. 목표와 판정 기준

1. **크기:** 기본 설정으로 100~300 MB DWG → 5 MB 이하 DXF (10 MB 초과는 지금처럼 경고 + 끄기 후보 제안).
2. **모양:** 벽·기둥·통로·설비 외곽이 남고, 설비 안의 작은 선·구멍·겹친 선은 없다. 높이가 있는 요소는 모두 평면 투영이다(수직선·수직 원은 사라지거나 선분으로).
3. **시간:** 300 MB 도면 읽기+정리가 수 분 이내, 명령줄에 진행률, Esc로 취소.
4. **재현성:** 같은 도면·같은 설정 → 바이트까지 같은 DXF. 설정은 `lite\<name>_lite.settings.tsv`에 남아 다음 실행의 기본값이 된다.
5. **호환:** 결과 DXF를 ZWCAD가 열고, `DxfLite`(서버 컷 편집기·영상 배경)가 읽고, AutoMod 정적 그래픽으로 들어간다. `AGVCONVERT`·`AGVCHECK`·`AGVSEND`는 영향 없음.

---

## 2. 정리 파이프라인 (설계)

모든 임계값은 mm로 받고 INSUNITS로 도면 단위로 바꾼다(`UnitScale.FromInsUnits`). 순서가 중요하다: **E(체인 결합) 뒤에 F(작은 요소 제거)** 를 두어야 짧은 선 수백 개로 쪼개진 긴 곡선이 살아남는다.

| 단계 | 내용 | 기본값 | 실행 위치 |
|---|---|---|---|
| A. 읽기 | 블록 정의 1회 읽기 → 로컬 **3D** 기하(선 끝점 3D, 원·호는 중심·법선·반지름, 폴리라인은 법선·고도·벌지, 곡선은 3D 샘플점). 인스턴스 = (정의, `Matrix3d` 전체, 레이어, 외부참조/동결 플래그). MINSERT는 행·열마다 인스턴스. 두께(Thickness)·고도(Elevation)는 무시 | — | 플러그인 |
| B. 평탄화 | 인스턴스 행렬(3×4)을 3D 점에 적용한 뒤 Z를 버린다. 법선이 ±Z와 평행인 원·호·벌지는 정확히(회전·균등 배율·대칭), 기울어진 것은 투영 타원을 샘플, 수직 평면의 것은 선분으로 | 곡선 허용 오차 10 mm | Core |
| C. 퇴화 제거 | 길이 < 0.01 mm 선, 꼭짓점이 전부 같은 폴리라인, 반지름 0 원·호, 폴리라인의 연속 중복 꼭짓점 | — | Core |
| D. 중복·겹침 제거 | (1) 끝점을 격자(0.1 mm)로 반올림한 정규화 키(끝점 정렬)로 정확 중복 제거, 원·호도 같은 방식. (2) 같은 직선 위(방향각·법선 오프셋을 격자로 묶음)의 선분들을 1차원 구간 합집합으로 병합 | 격자 0.1 mm | Core(정의 안) + 쓰기 시(인스턴스 사이) |
| E. 체인 결합·단순화 | 끝점이 격자에서 만나는 선분을 폴리라인으로 잇고(분기점에서는 끊음), 같은 직선 위 꼭짓점 제거, Douglas–Peucker(허용 오차 = 곡선 허용 오차) | 10 mm | Core |
| F. 작은 요소 제거 | 엔티티(결합 뒤)의 범위 상자 대각선 < MinSize → 제거. 이유별 개수·바이트 기록 | MinSize 50 mm (0이면 끔) | Core |
| G. 3D 객체 | 모드: 제거 / **외곽(투영점의 볼록 껍질)** / 모서리 투영(폭발한 모든 모서리 → B~F) / 범위 상자 | 외곽 | 플러그인(폭발) + Core |
| H. 블록 단순화 | 정의별 모드: 그대로 / 외곽선(정리 후 투영 기하의 볼록 껍질) / 사각형 / 제거. 기본 규칙: 정리 뒤 인스턴스당 선 수 > BlockLimit → 외곽선. 대화상자의 블록 표에서 정의마다 바꿀 수 있다 | BlockLimit 500 | Core |
| I. 쓰기 | 선택 레이어만, 원점 이동, 단위별 정밀도(0.01 mm 상당: mm 도면 소수 2자리, m 도면 5자리), 인스턴스 사이 전역 중복 제거(64-bit 키 해시 집합, 레이어 무시 옵션 기본 켬), 결정적 순서(레이어 → 원본 순서) | — | Core |

정의 단위로 B~F를 돌릴 때 임계값은 그 정의의 **가장 큰 인스턴스 배율**로 로컬 단위로 바꾼다(작다고 지운 것이 어떤 인스턴스에서도 크지 않도록 보수적으로). 비균등 배율 인스턴스는 지금처럼 원·호를 현으로 바꾼다(`CurveApprox.Transform`).

예상 크기 = Σ(레이어, 플래그) 버킷 = 정의별 정리 결과 바이트 × 인스턴스 수. 전역 중복 제거는 쓰기 때만 하므로 예상치는 **상한**이고, 보고서가 실제 값을 적는다.

---

## Global Constraints

- Core는 netstandard2.0(플러그인 net48과 공유): System.Text.Json 금지, LINQ·`System.Memory`는 가능. 플러그인 UI 문구는 한국어, 수치에는 단위.
- 원본 도면은 절대 바꾸지 않는다(읽기 전용 트랜잭션). 출력은 `lite\<name>_lite.dxf`, `lite\<name>_lite.report.tsv`, `lite\<name>_lite.settings.tsv`.
- R12 DXF는 LINE/POLYLINE/CIRCLE/ARC만, ASCII, 레이어 이름 31자 규칙(`LiteLayers.R12Names`) 유지. `DxfLite`가 그대로 읽을 수 있어야 한다.
- 결정적 결과: 같은 입력·설정이면 같은 바이트. 해시 집합 순회에 의존하지 않는다.
- 메모리: 인스턴스별 펼친 엔티티를 전부 들고 있지 않는다. 쓰기 때 해시 키(16바이트/선)만 든다.
- 작업 예산·중첩 한도는 유지하되(런어웨이 방어), 정의 단위 읽기로 300 MB 도면에서 예산에 닿지 않아야 한다.
- 플러그인 코드는 빌드 PC에서 실행할 수 없다: 모든 판단 로직은 Core에 두고 테스트한다. 플러그인은 읽기·폭발·진행 표시만.

## Review Focus

1. **쪼개진 곡선이 살아남는가:** 반지름 5 m 원호를 5 mm 선분 3,000개로 폭발한 입력 → 체인 결합 뒤 폴리라인 1개, MinSize 50 mm에서 지워지지 않음(Task 4 테스트).
2. **3D 블록이 바르게 눕는가:** X축 90° 회전 서브블록 안의 사각형 → 평면에서 선분 1개(수직면)로, Y축 회전 없는 것은 그대로(Task 2 테스트). 로봇 블록 실물 검증은 Task 9.
3. **중복 제거가 다른 레이어·다른 Z의 같은 선을 잡는가:** Z = 0과 Z = 1200의 같은 상자 → 선 4개(Task 3 테스트).
4. **예상 크기와 실제 크기의 차이:** 전역 중복 제거 전 예상치가 실제보다 작으면 안 된다(상한 보장, Task 6 테스트: 무작위 블록 배치에서 예상 ≥ 실제).
5. **설정 파일의 하위 호환:** 설정 파일이 없거나 모르는 키가 있어도 기본값으로 실행(Task 6 테스트).

---

## File Structure

Core (`src/ASH.Core/Lite/`)
- `LiteModel.cs` — 유지 + `Affine3`(3×4 행렬, `Project`로 `Affine2`·Z 성분 분리), 3D 로컬 기하 `Geo3`(`Line3`, `Circle3`, `Arc3`, `Poly3`, `Points3`).
- `Flatten.cs` — 신규. `Geo3` × `Affine3` → `LiteEntity`(B).
- `Clean.cs` — 신규. 퇴화 제거(C), 격자 키·정확 중복·겹침 병합(D), 전역 중복 키(`SegKey`).
- `Chain.cs` — 신규. 체인 결합·직선 꼭짓점 제거·Douglas–Peucker(E).
- `SmallFilter.cs` — 신규. 범위 상자 기준 제거와 이유별 집계(F).
- `Hull.cs` — 신규. 볼록 껍질(모노톤 체인)·범위 사각형(G·H).
- `LiteBlocks.cs` — 신규. `BlockDef`(정의별 정리 결과·통계), `BlockUse`(인스턴스), `BlockMode`.
- `LiteSettings.cs` — 신규. 임계값·모드·레이어 선택·블록별 모드, `lite\<name>_lite.settings.tsv` 읽기/쓰기.
- `LitePipeline.cs` — 신규. 정의 하나에 B~H를 순서대로 적용, `LiteStats`(이유별 제거 개수·바이트).
- `LitePlan.cs` — 수정. 정의 × 인스턴스에서 버킷 계산, 블록 표, `Write`는 스트리밍·전역 중복 제거.
- `LiteDxfWriter.cs` — 수정. 단위별 정밀도, 엔티티 단위 쓰기 유지(`Bytes`가 곧 추정).
- `LiteReport.cs` — 수정. 블록 표, 단계별 제거 이유 표, 설정 요약, 소요 시간.
- `CurveApprox.cs`, `LiteLayers.cs` — 유지.

Plugin (`src/ASH.Plugin/`)
- `Commands/LiteCommands.cs` — Extractor 재작성: 정의 1회 읽기(3D), 솔리드·서피스·메시 폭발, 진행률·`UserBreak` 취소, 설정 파일 적용, 명령 흐름.
- `Ui/LiteDialog.cs` — 재구성: 레이어 표 + 블록 표 + 정리 설정, 백그라운드 재계산.

Tests (`tests/ASH.Tests/Lite/`)
- `FlattenTests.cs`, `CleanTests.cs`, `ChainTests.cs`, `SmallFilterTests.cs`, `HullTests.cs`, `BlocksTests.cs`, `SettingsTests.cs`, 기존 `LiteTests.cs`(쓰기·보고서·추정) 갱신.

Docs
- `docs/superpowers/specs/2026-10-20-ash-plugin-design.md` §5 교체, `README.md`에 "도면 경량화" 절, `docs/plugin-manual-test.md`에 "도면 경량화 재설계" 표.

---

### Task 1: 기준 측정 — 지금 코드로 실제 도면 재기 (사용자 PC)

**Files:**
- Modify: `src/ASH.Core/Lite/LiteReport.cs`, `src/ASH.Plugin/Commands/LiteCommands.cs` (진단용 블록 표)
- Create: `docs/lite-baseline.md`

- [ ] **Step 1:** Extractor가 정의마다 (이름, 인스턴스 수, 펼친 선·곡선 수, 3D 수)를 세어 `LiteSource.Blocks`에 넣고, 보고서에 "[블록 상위 30]" 표(인스턴스 × 선 수 내림차순)를 추가한다. 읽기·쓰기 소요 시간도 보고서에 적는다.
- [ ] **Step 2:** 대표 도면 3~5개(100~300 MB, 컨베이어·로봇·판넬이 들어 있는 것)로 AGVLITE를 돌려 `docs/lite-baseline.md`에 표로 적는다: DWG 크기, 읽기 시간, 결과 DXF 크기, 엔티티 수, 잘림 여부, 상위 블록 10개와 그 비중, 레이어 상위 5개.
- [ ] **Step 3:** 결과 DXF를 ZWCAD에서 열어 무엇이 깨지는지(D1·D2) 화면을 `docs/images/lite-baseline-*.png`로 남긴다. 이 표가 Task 9의 before/after 기준이다.

### Task 2: Core — 3D 평탄화와 퇴화 제거 (B, C)

**Files:**
- Modify: `src/ASH.Core/Lite/LiteModel.cs`
- Create: `src/ASH.Core/Lite/Flatten.cs`, `src/ASH.Core/Lite/Clean.cs`(퇴화 부분), `tests/ASH.Tests/Lite/FlattenTests.cs`

- [ ] **Step 1 (테스트 먼저):** X축 90° 회전 행렬 아래의 로컬 XY 사각형 → 선분 1개; Z축 회전 + 이동 아래의 원 → 원(중심·반지름 정확); 법선 (1,0,0)인 원 → 선분; 기울어진(법선 (0, sin30°, cos30°)) 원 → 타원 점열이 허용 오차 안; Z만 다른 선 → 0길이로 제거; 벌지 폴리라인이 대칭 행렬에서 방향 뒤집힘(기존 `CurveApprox` 동작 유지).
- [ ] **Step 2:** `Affine3`(12개 값, `Matrix3d.ToArray()` 행 우선에서 생성, `Then`, `Ap(x,y,z)`, 평면 부분 `Affine2`와 Z 열 분리)과 `Geo3` 레코드를 `LiteModel.cs`에 더한다.
- [ ] **Step 3:** `Flatten.To2D(Geo3, Affine3, tol, layer)`: 점은 3D 변환 후 Z 버림. 원·호·벌지는 변환된 법선이 ±Z이고 평면 부분이 유사 변환이면 정확, 평면 부분이 비균등이면 현, 법선이 기울었으면 `CurveApprox.Ellipse`로 투영 타원 샘플, 법선이 수평이면 두 끝점 선분(원은 지름 선분).
- [ ] **Step 4:** `Clean.Degenerate(...)`: 임계 0.01 mm(도면 단위 환산). 연속 중복 꼭짓점 제거, 2점 미만 폴리라인 제거, 반지름 0 원·호 제거. 제거 이유 "퇴화"로 집계.
- [ ] **Step 5:** 테스트 통과, 기존 `LiteTests` 통과.

### Task 3: Core — 중복·겹침 제거 (D)

**Files:**
- Modify: `src/ASH.Core/Lite/Clean.cs`
- Create: `tests/ASH.Tests/Lite/CleanTests.cs`

- [ ] **Step 1 (테스트 먼저):** 같은 선 10개(끝점 순서 뒤바뀜 섞음) → 1개; 0.05 mm 차이 나는 선 → 1개(격자 0.1 mm), 0.3 mm 차이 → 2개; 같은 직선 위 [0,10]·[5,20]·[20,30] → [0,30] 1개, [0,10]·[11,20] → 2개; Z = 0·1200의 같은 상자(평탄화 뒤) → 4개; 같은 원 3개 → 1개; 폴리라인은 꼭짓점 열 전체가 같을 때만 중복(역순 포함); 결과 순서가 입력 순서를 따른다(결정성).
- [ ] **Step 2:** `SegKey.Of(x1,y1,x2,y2, grid)`: 끝점을 격자로 반올림해 사전순 정렬, 64-bit 해시(두 점의 long 좌표 조합, 충돌 시 정확 비교용 구조체도 제공). 원·호·폴리라인 키도 같은 방식.
- [ ] **Step 3:** `Clean.Dedup(entities, grid)`: 첫 등장만 남긴다. `Clean.MergeCollinear(lines, grid, angleGrid)`: 방향각을 0.01°·법선 오프셋을 격자로 묶어 같은 직선 그룹을 만들고, 1차원 투영 구간을 정렬·합집합으로 병합(간격 ≤ 격자이면 잇는다). 제거 이유 "중복"·"겹침" 집계.
- [ ] **Step 4:** 100만 선 무작위 입력에서 Dedup+Merge 2초 이내(Release 기준, 느슨한 상한) 테스트.

### Task 4: Core — 체인 결합·단순화·작은 요소 제거 (E, F)

**Files:**
- Create: `src/ASH.Core/Lite/Chain.cs`, `src/ASH.Core/Lite/SmallFilter.cs`, `tests/ASH.Tests/Lite/ChainTests.cs`, `tests/ASH.Tests/Lite/SmallFilterTests.cs`

- [ ] **Step 1 (테스트 먼저):** 5 m 원호를 5 mm 선분 3,000개로 쪼갠 입력 → 폴리라인 1개, 꼭짓점 수 ≤ `CurveApprox.Steps` 상당(허용 오차 10 mm), 최대 편차 ≤ 10 mm; T자 분기에서는 3개로 끊김; 닫힌 사각형 4선 → 닫힌 폴리라인 1개; 일직선 위 꼭짓점 제거; 20 mm 원 → 제거(MinSize 50), 60 mm 원 → 유지; 10 mm 선분 100개가 체인으로 1 m → 유지; MinSize 0 → 아무것도 안 지움; 집계가 이유별로 맞음.
- [ ] **Step 2:** `Chain.Join(lines+polylines, grid)`: 끝점 격자 키 → 인접 목록, 차수 2인 꼭짓점만 통과해 체인 생성(분기·끝에서 종료), 같은 레이어만 연결. 원래 폴리라인도 끝점이 맞으면 잇는다(벌지 보존).
- [ ] **Step 3:** `Chain.Simplify(polyline, tol)`: 벌지 없는 구간에 Douglas–Peucker, 벌지 꼭짓점은 고정. 닫힌 폴리라인은 가장 먼 점에서 둘로 나눠 적용.
- [ ] **Step 4:** `SmallFilter.Apply(entities, minSize)`: `LiteBox` 대각선 < minSize → 제거, 이유 "작은 요소"로 개수·바이트 집계(바이트는 `LiteDxfWriter.Bytes`).
- [ ] **Step 5:** `LitePipeline.Run(defGeo, settings, scaleMax) → (entities, stats)`: C → D → E → F 순서로 호출. 테스트: 순서를 바꾸면(F 먼저) 쪼개진 원호가 사라지는 회귀 테스트를 두어 순서를 고정한다.

### Task 5: Core — 블록 모델·외곽선·예상 크기 (G, H, 추정)

**Files:**
- Create: `src/ASH.Core/Lite/Hull.cs`, `src/ASH.Core/Lite/LiteBlocks.cs`, `tests/ASH.Tests/Lite/HullTests.cs`, `tests/ASH.Tests/Lite/BlocksTests.cs`
- Modify: `src/ASH.Core/Lite/LitePlan.cs`, `src/ASH.Core/Lite/LitePipeline.cs`

- [ ] **Step 1 (테스트 먼저):** 볼록 껍질: 정사각형+내부 점 → 4점, 일직선 점 → 선분(2점), 점 1개 → 제거; 블록 모드 외곽선 → 닫힌 폴리라인 1개, 사각형 → 범위 상자, 제거 → 0개; 정의 하나(선 1,000개)를 인스턴스 50개로 → 예상 바이트 = 50 × 정의 바이트(±헤더), 기본 규칙 BlockLimit 500이면 외곽선으로 바뀌어 50 × 폴리라인 1개; 레이어 0 내용은 인스턴스 레이어를, 다른 레이어는 자기 레이어를 쓰고 동결 플래그가 상속된다(기존 동작 유지).
- [ ] **Step 2:** `Hull.Convex(points)`(Andrew 모노톤 체인), `Hull.Box(points)`.
- [ ] **Step 3:** `BlockDef`: 정의 ID, 이름, 로컬 `Geo3` 목록, 자식 인스턴스 목록, 정리 결과(`LiteEntity` 목록, 레이어별 바이트), `Stats`, 인스턴스당 선 수. `BlockUse`: 정의, `Affine3`, 레이어, 플래그. 모델 공간도 하나의 `BlockDef`(이름 `*MODEL_SPACE`).
- [ ] **Step 4:** `LitePlan` 재작성: 정의 그래프를 깊이 우선으로 돌며 각 정의의 정리 결과를 한 번 계산(가장 큰 누적 배율로), 인스턴스마다 (해석된 레이어, 플래그) 버킷에 개수·바이트 더하기. 중첩 블록의 모드 "외곽선"은 자식까지 펼친 투영 기하에서 껍질을 구한다. `Layers`, `Layer()`, `Total()`, `Biggest()`, `DefaultOn()` API는 유지(대화상자 호환). 블록 표 `Blocks` 추가(이름, 인스턴스 수, 정리 전·후 선 수, 바이트, 현재 모드, 기본 규칙).
- [ ] **Step 5:** `Select`/`Write`가 정의를 인스턴스 행렬로 펼치면서 스트리밍으로 쓴다(인스턴스 펼친 목록을 만들지 않음).

### Task 6: Core — 쓰기 정밀도·전역 중복 제거·설정 파일·보고서 (I)

**Files:**
- Modify: `src/ASH.Core/Lite/LiteDxfWriter.cs`, `src/ASH.Core/Lite/LitePlan.cs`, `src/ASH.Core/Lite/LiteReport.cs`
- Create: `src/ASH.Core/Lite/LiteSettings.cs`, `tests/ASH.Tests/Lite/SettingsTests.cs`; `tests/ASH.Tests/Lite/LiteTests.cs` 갱신

- [ ] **Step 1 (테스트 먼저):** mm 도면(INSUNITS 4)은 소수 2자리, m 도면(6)은 5자리, inch(1)는 3자리; `Bytes`가 `Write` 출력과 바이트 단위로 같음(기존 테스트 유지); 같은 정의를 같은 자리에 두 번 놓으면 두 번째는 쓰지 않음(전역 중복), 레이어가 다르면 "레이어 무시" 옵션에 따라; 무작위 블록 배치 50가지에서 예상 ≥ 실제; 설정 파일 왕복(모든 키), 없는 파일·모르는 키·깨진 값 → 기본값; 보고서에 단계별 제거 표·블록 표·설정·소요 시간이 있음; 출력이 `DxfLite.Read`로 읽힘.
- [ ] **Step 2:** `LiteDxfWriter.Precision(insUnits)`와 `Write(..., precision)`; `Num`을 포맷 인자로.
- [ ] **Step 3:** `LitePlan.Write`: `HashSet<long>`(SegKey)로 전역 중복 제거, 결정적 순서(선택 레이어 이름순 → 정의 → 원본 순서). `LiteResult`에 전역 중복 제거 수 추가.
- [ ] **Step 4:** `LiteSettings`: `MinSizeMm`, `ToleranceMm`, `GridMm`, `Solid3dMode`, `BlockLimit`, `IncludeXref`, `SkipHidden`, `DedupAcrossLayers`, `Layers`(체크 목록), `BlockModes`(이름→모드). TSV 키/값, UTF-8 BOM, 모르는 키 보존.
- [ ] **Step 5:** `LiteReport`: "[정리 단계]" 표(단계, 제거 개수, 줄인 바이트), "[블록]" 표, "[설정]" 표, 읽기·정리·쓰기 소요 시간. 기존 절은 유지.

### Task 7: 플러그인 — Extractor 재작성·3D 폭발·진행·취소 (A, G)

**Files:**
- Modify: `src/ASH.Plugin/Commands/LiteCommands.cs`

- [ ] **Step 1:** `Read(BlockTableRecord)`가 `Geo3`를 만든다: `Line` 끝점 3D, `Circle`/`Arc` 중심·법선·반지름·각도, `Polyline`/`Polyline2d` 법선·고도·벌지(두께 무시), `Polyline3d` 3D 점, `Spline`/`Ellipse`/비단순 폴리라인은 `GetPointAtParameter`로 3D 적응 샘플(`CurveApprox.Adaptive`를 3D 거리로 확장하거나 투영 뒤 샘플: 투영 뒤 샘플을 택한다 — 인스턴스 행렬을 모르므로 정의 로컬에서 허용 오차 / 최대 배율로), `Mline`/`Region` 폭발 유지.
- [ ] **Step 2:** `Solid3d`/`Body`/`Surface`: `Explode` 재귀(Solid3d → Region·Surface → 곡선). 폭발이 실패하면 `GeometricExtents` 상자로 후퇴하고 보고서에 "폭발 실패 N개". 모드가 "외곽"이면 폭발 곡선의 점만 모아 Core 껍질로. `SubDMesh`·`PolyFaceMesh`·`PolygonMesh`: 꼭짓점·면에서 모서리 3D 선. 솔리드 하나당 상한(MaxPoints) 적용.
- [ ] **Step 3:** 인스턴스 기록: `BlockReference.BlockTransform` 전체를 `Affine3`로, MINSERT는 행·열 오프셋을 블록 로컬 변환으로 합성(기존 지름길 제거).
- [ ] **Step 4:** 진행·취소: 정의 100개마다 `ed.WriteMessage`로 "읽는 중 n/m 정의, 선 k개", `HostApplicationServices.Current.UserBreak()`로 Esc 확인 → 취소 시 깨끗이 종료. 정리(Core)는 `Task.Run`으로 돌리고 ZWCAD 쓰레드는 기다리며 메시지만(문서 잠금은 읽기 끝난 뒤 해제).
- [ ] **Step 5:** 설정 파일: 있으면 읽어 대화상자 초기값으로, "만들기" 뒤 저장. 명령 흐름·열기 질문·오류 처리는 기존과 같다. 수동 테스트 항목을 Task 9 표에 적는다.

### Task 8: 플러그인 — 대화상자 재구성

**Files:**
- Modify: `src/ASH.Plugin/Ui/LiteDialog.cs`

- [ ] **Step 1:** 상단: 도면 → 출력, 읽은 정의 수·인스턴스 수·3D 수, 문제(잘림·실패·폭발 실패). 탭 또는 두 절: **레이어 표**(기존 열 + "정리 후") / **블록 표**(이름, 인스턴스, 선/인스턴스, 예상 MB, 모드 콤보: 그대로·외곽선·사각형·제거, 기본 규칙 표시). 블록 표는 바이트 내림차순, 상위 200개 + "나머지 n개".
- [ ] **Step 2:** 정리 설정 절: 작은 요소 기준(mm, 0 = 끔), 곡선 허용 오차(mm), 3D 객체 모드(제거/외곽/모서리/사각형), 블록 외곽선 기준(선/인스턴스), 외부참조 포함, 꺼진·동결 레이어 제외, 레이어 무시하고 중복 제거. 프리셋 버튼 "시뮬레이션용(기본)"·"최대 보존".
- [ ] **Step 3:** 설정이 바뀌면 `LitePlan.Recompute(settings)`를 백그라운드(`Task`)로 돌리고 끝나면 표·합계 갱신(디바운스 300 ms, 진행 중이면 "계산 중…"). 레이어·블록 모드 체크만 바뀌면 즉시 합산만(기존처럼 비용 0).
- [ ] **Step 4:** 합계 줄에 "원본 선 n → 정리 후 m (중복 a, 작은 요소 b, 블록 외곽선 c 줄임)" 요약. 10 MB 초과 경고와 끄기 후보(레이어·블록 둘 다) 유지.

### Task 9: 문서·수동 테스트·실제 도면 검증·기본값 확정

**Files:**
- Modify: `docs/superpowers/specs/2026-10-20-ash-plugin-design.md`(§5), `README.md`, `docs/plugin-manual-test.md`, `docs/lite-baseline.md`

- [ ] **Step 1:** Task 1의 도면으로 다시 측정해 `docs/lite-baseline.md`에 before/after 표(크기·시간·엔티티·단계별 제거 수)를 적는다. 목표(5 MB)에 못 미치는 도면은 어떤 블록·레이어가 남는지 적고 기본값(MinSize·BlockLimit)을 조정한다.
- [ ] **Step 2:** 결과 DXF를 ZWCAD에서 열어 경로 작도 스냅이 되는지, 컨베이어·로봇·판넬이 외곽선으로 보이는지 캡처를 남긴다. AutoMod 정적 그래픽 가져오기(한 번)와 서버 컷 편집기(`DxfLite`) 표시를 확인한다.
- [ ] **Step 3:** `docs/plugin-manual-test.md`에 "도면 경량화 재설계" 표: L1 3D 블록이 눕는다, L2 Esc 취소, L3 설정 파일이 다음 실행 초기값, L4 블록 표에서 모드 바꾸면 크기 즉시 변화, L5 10 MB 초과 경고, L6 외부참조 포함/제외, L7 폭발 실패 후퇴, L8 같은 설정 두 번 → 같은 바이트.
- [ ] **Step 4:** 스펙 §5를 이 파이프라인 표로 바꾸고, README에 사용법(명령, 설정 의미, 보고서 읽는 법)을 넣는다. `Directory.Build.props` 버전 0.21.0, `release 0.21.0` 커밋은 사용자가 결정.

---

## 사용자가 정할 것 (기본값 제안)

| 항목 | 제안 | 비고 |
|---|---|---|
| 작은 요소 기준 | 50 mm | 볼트 구멍·모따기·버튼이 지워지고 설비 외곽은 남는 크기. Task 9에서 실측으로 조정 |
| 3D 객체 기본 모드 | 외곽(볼록 껍질) | 컨베이어는 사각형, 로봇은 덩어리로. 내부 구조가 필요하면 "모서리 투영" |
| 블록 외곽선 기준 | 정리 뒤 인스턴스당 선 500개 초과 | 블록 표에서 정의마다 바꿀 수 있음 |
| 중복 제거 격자 | 0.1 mm | 끝점이 미세하게 어긋난 복사본을 같은 것으로 봄 |
| 레이어 무시 중복 제거 | 켬 | 먼저 쓰인 레이어가 남는다 |
| 문자 | 계속 제거 | 구역 이름 같은 큰 문자 유지는 후속 과제 |

## 후속 과제 (이번 범위 밖)

- 숨은선 제거(진짜 상면 윤곽): 볼록 껍질·모서리 투영으로 충분한지 Task 9 결과를 보고 결정.
- 큰 문자(구역·공정 이름) 유지 옵션.
- 경량 DXF를 서버에 바로 보내는 흐름(AGVSEND가 `lite\`를 쓰도록)은 별도 계획.
