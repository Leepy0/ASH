# 도면 경량화(AGVLITE) 재설계 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 컨베이어·다관절 로봇·터치 판넬 같은 복잡한 3D 블록이 겹겹이 쌓인 100~300 MB 공장 도면을, **사람이 아무것도 고르지 않고** 명령 한 번으로 시뮬레이션 배경과 경로 작도에 맞는 5 MB 이하의 평면 R12 DXF로 만든다. 높이(Z)가 있는 것은 모두 평면으로 투영하고, 눈에 보이지 않는 작은 요소와 겹친 선은 지우고, 선이 빽빽한 덩어리(설비)는 외곽선으로 바꾼다. 판단은 **기하 측정값만** 쓴다. 블록 이름·레이어 이름으로는 아무것도 결정하지 않는다.

**Architecture:** 플러그인(`src/ASH.Plugin`)은 ZWCAD에서 **블록 정의를 한 번씩만** 읽어 3D 기하로 넘기고, 인스턴스는 (정의, 3D 행렬, 레이어)만 기록한다. 정리(평탄화 → 퇴화 제거 → 중복·겹침 제거 → 체인 결합·단순화 → 작은 요소 제거 → 빽빽한 덩어리 외곽선 치환)는 전부 Core(`src/ASH.Core/Lite`)의 순수 함수로 두어 ZWCAD 없이 테스트한다. 정리 강도는 단계(0~4)로 정의하고, 프로그램이 목표 크기(5 MB)에 들 때까지 단계를 올린다. 쓰기는 스트리밍이며 인스턴스 사이의 중복은 쓰는 순간 지운다. 대화상자는 기본 흐름에 없다(`AGVLITE`는 바로 만든다). 예외 조정용 `AGVLITEX`만 창을 연다.

**Tech Stack:** C# (Core netstandard2.0, 플러그인 net48 WinForms, ZWCAD.NetApi 20.26.0), xunit. 빌드 PC에 ZWCAD가 없으므로 플러그인 쪽은 수동 테스트 기록표로 검증한다.

**Spec:** `docs/superpowers/specs/2026-10-20-ash-plugin-design.md` §5 (이 계획으로 §5를 고쳐 쓴다, Task 9)

---

## 0. 현재 구현 진단 (0.20.0, `c792e00`·`28f1b77`)

지금 있는 것: 모델 공간 읽기(블록 8단계 중첩 펼침, 작업 예산 2,000,000), 문자·해치·치수·이미지 항상 제거, 외부참조·3D 범위 상자·꺼진 레이어 옵션, 레이어 선택 대화상자와 레이어별 예상 크기, 원점 이동, R12 ASCII 쓰기, 보고서 TSV. 곡선 근사 수학(`CurveApprox`), R12 레이어 이름 규칙, 보고서, 원점 이동, 쓰기는 그대로 쓴다.

요구사항(사람 개입 최소, 작은 요소 삭제, 높이 평탄화, 시뮬레이션용 정리) 기준으로 모자란 점:

| # | 문제 | 어디 | 영향 |
|---|---|---|---|
| D0 | **사람이 레이어를 고르고 옵션을 정해야 결과가 나온다.** 기본값은 레이어 **이름** 키워드(DIM, TEXT, 치수…)로 정하므로 이름이 제각각인 도면에서는 틀린다 | `LiteDialog`, `LiteLayers.KeepByDefault` | 요구사항(개입 최소) 미충족 |
| D1 | **평탄화가 2D 아핀 지름길.** `ToAffine`이 3D 행렬의 z 행·열을 버린다. X축으로 90° 돌려 세운 서브블록(로봇 팔, 판넬 면)은 선 하나로 붕괴하거나 엉뚱한 위치에 찍힌다 | `LiteCommands.Extractor.ToAffine` | 3D로 만든 설비 블록이 깨진다 |
| D2 | **수직 모서리가 0길이 LINE으로 남는다.** `Line`은 Z만 버리므로 같은 XY의 수직선이 점이 된다. 수직 평면의 원·호는 선분 위에 눌린 폴리라인이 된다 | `Add(Line)`, `Sample` | 쓸모없는 엔티티가 수만 개 |
| D3 | **중복·겹침 제거가 없다.** 상자의 윗면·아랫면 모서리, 층층이 쌓인 같은 블록, 레이어만 다른 같은 선이 전부 따로 저장된다 | 없음 | 3D 도면에서 크기의 절반 이상이 중복 |
| D4 | **작은 요소 제거가 없다.** 볼트 구멍, 모따기, 로봇 내부 선, 판넬 버튼이 그대로 남는다 | 없음 | 요구사항 미충족 |
| D5 | **3D 솔리드·메시는 "제거" 또는 "범위 상자"뿐.** 컨베이어·로봇 솔리드의 모양이 사라지거나 사각형만 남는다 | `Add(Solid3d…)` | 배경으로 못 쓴다 |
| D6 | **인스턴스마다 전부 메모리에 펼친다.** 2,000,000개 예산을 넘으면 잘린다(`Truncated`) | `Walk`, `LiteSource.Items` | 300 MB 도면에서 일부 누락·메모리 급증 |
| D7 | **좌표가 항상 소수 4자리.** mm 도면에서 0.1 µm 정밀도라 바이트를 낭비한다 | `LiteDxfWriter.Num` | 크기 20~30 % 손해 |
| D8 | **진행 표시·취소가 없다.** 큰 도면에서 몇 분간 멈춘 것처럼 보인다 | `Run` | 사용성 |
| D9 | **폴리라인 단순화·체인 결합이 없다.** 스플라인·타원을 잘게 샘플한 점, 다른 CAD에서 들어와 수백 개 짧은 선으로 쪼개진 곡선이 그대로다 | 없음 | 크기, 그리고 D4를 안전하게 할 전제 |
| D10 | **실제 도면 측정이 없다.** 테스트는 합성 데이터뿐 | `tests/ASH.Tests/Lite` | 기본값 근거 없음 |

---

## 1. 목표와 판정 기준

1. **개입 0:** `AGVLITE` 한 번이면 저장된 도면에서 결과가 나온다. 묻는 것은 "경량 도면을 열까요?" 하나(지금과 같음). 블록 이름·레이어 이름은 판단에 쓰지 않는다. 사람이 CAD에서 이미 끈 레이어(꺼짐·동결)만 그 사람의 결정으로 존중한다.
2. **크기:** 기본 설정으로 100~300 MB DWG → 5 MB 이하 DXF. 프로그램이 정리 단계를 올려 맞춘다. 4단계에서도 10 MB를 넘으면 결과는 그대로 쓰고 보고서에 남는 큰 덩어리를 적는다.
3. **모양:** 벽·기둥·통로·설비 외곽이 남고, 설비 안의 작은 선·구멍·겹친 선은 없다. 높이가 있는 요소는 모두 평면 투영이다(수직선·수직 원은 사라지거나 선분으로).
4. **시간:** 300 MB 도면 읽기+정리가 수 분 이내, 명령줄에 진행률, Esc로 취소.
5. **재현성:** 같은 도면·같은 단계 → 바이트까지 같은 DXF. 쓴 단계·임계값은 보고서와 `lite\<name>_lite.settings.tsv`에 남고, 다음 실행은 저장된 값을 먼저 써 본다(사람이 `AGVLITEX`로 바꾼 값이 있으면 그것이 유지된다).
6. **호환:** 결과 DXF를 ZWCAD가 열고, `DxfLite`(서버 컷 편집기·영상 배경)가 읽고, AutoMod 정적 그래픽으로 들어간다. `AGVCONVERT`·`AGVCHECK`·`AGVSEND`는 영향 없음.

---

## 2. 정리 파이프라인 (설계)

모든 임계값은 mm로 두고 INSUNITS로 도면 단위로 바꾼다(`UnitScale.FromInsUnits`). 순서가 중요하다: **E(체인 결합) 뒤에 F(작은 요소 제거)** 를 두어야 짧은 선 수백 개로 쪼개진 긴 곡선이 살아남는다. G(덩어리 외곽선)는 블록 경계와 무관하게 **기하의 공간 분포**로 판단하므로 블록 이름이 필요 없고, 블록으로 안 묶인 평면 도면에서도 같은 결과가 나온다.

| 단계 | 내용 | 임계값(단계별 표는 §3) | 실행 위치 |
|---|---|---|---|
| A. 읽기 | 블록 정의 1회 읽기 → 로컬 **3D** 기하(선 끝점 3D, 원·호는 중심·법선·반지름, 폴리라인은 법선·고도·벌지, 곡선은 3D 샘플점). 인스턴스 = (정의, `Matrix3d` 전체, 레이어, 외부참조/동결 플래그). MINSERT는 행·열마다 인스턴스. 두께(Thickness)·고도(Elevation)는 무시. 외부참조 내용은 항상 포함(도면의 일부다). 문자·해치·치수·이미지·OLE·점·무한선은 항상 제외 | — | 플러그인 |
| B. 평탄화 | 인스턴스 행렬(3×4)을 3D 점에 적용한 뒤 Z를 버린다. 법선이 ±Z와 평행인 원·호·벌지는 정확히(회전·균등 배율·대칭), 기울어진 것은 투영 타원을 샘플, 수직 평면의 것은 선분으로 | 곡선 허용 오차 | Core |
| C. 퇴화 제거 | 길이 < 0.01 mm 선, 꼭짓점이 전부 같은 폴리라인, 반지름 0 원·호, 폴리라인의 연속 중복 꼭짓점 | — | Core |
| D. 중복·겹침 제거 | (1) 끝점을 격자로 반올림한 정규화 키(끝점 정렬)로 정확 중복 제거(레이어 무시), 원·호·폴리라인도 같은 방식. (2) 같은 직선 위(방향각·법선 오프셋을 격자로 묶음)의 선분들을 1차원 구간 합집합으로 병합 | 격자 0.1 mm | Core(정의 안) + 쓰기 시(인스턴스 사이) |
| E. 체인 결합·단순화 | 끝점이 격자에서 만나는 선분을 폴리라인으로 잇고(분기점에서는 끊음), 같은 직선 위 꼭짓점 제거, Douglas–Peucker(허용 오차 = 곡선 허용 오차) | 곡선 허용 오차 | Core |
| F. 작은 요소 제거 | 엔티티(결합 뒤)의 범위 상자 대각선 < MinSize → 제거 | MinSize | Core |
| G. 덩어리 외곽선 | 평면 기하를 셀(Cell)로 나눠 셀마다 지나가는 선 길이를 센다. 점유 셀(길이 > 0)을 8-연결로 묶어 **덩어리**를 만들고, 덩어리마다 (범위 대각선 L, 선 밀도 ρ = 총 선 길이 / 점유 셀 면적, 선 수 n)를 잰다. **ρ > Dense 이고 L < MaxBlob** 이면 덩어리 안의 기하를 지우고 점유 셀 집합의 바깥 윤곽(marching squares → DP 단순화)으로 바꾼다. 구멍(안쪽 윤곽)은 버린다. 성긴 긴 것(벽·통로·기둥 열)은 ρ가 낮아 그대로 남고, 로봇·판넬·롤러 컨베이어는 ρ가 높아 윤곽만 남는다 | Cell 200 mm, Dense, MaxBlob | Core |
| H. 3D 객체 | 솔리드·서피스·메시는 모든 모서리를 폭발·투영해 B~G에 넣는다(별도 모드 없음). 밀도가 높으면 G가 알아서 외곽선으로 만든다. 폭발이 실패한 객체만 범위 상자 | — | 플러그인(폭발) + Core |
| I. 쓰기 | 원점 이동, 단위별 정밀도(0.01 mm 상당: mm 도면 소수 2자리, m 도면 5자리), 인스턴스 사이 전역 중복 제거(64-bit 키 해시 집합), 결정적 순서(레이어 → 정의 → 원본 순서) | — | Core |

**정의 단위 처리와 덩어리 판단의 관계.** B~G는 정의마다 한 번 돌린다(임계값은 그 정의의 가장 큰 인스턴스 배율로 로컬 단위로 환산, 작다고 지운 것이 어떤 인스턴스에서도 크지 않도록 보수적). 자식 블록은 먼저 처리되고(잎부터), 부모는 자식의 **정리 결과**(외곽선이 된 자식은 윤곽 폴리라인 하나)를 인스턴스 행렬로 놓은 뒤 자기 기하와 합쳐 G를 다시 판단한다. 그래서 "로봇 셀" 블록 안의 로봇은 윤곽이 되고, 셀의 펜스는 성기면 남으며, "생산 라인" 블록 전체가 하나의 덩어리로 뭉개지지 않는다(자식 윤곽은 선 밀도가 낮다). 모델 공간도 마지막 정의로 같은 처리를 받으므로 블록으로 안 묶인 빽빽한 영역도 잡힌다.

**예상 크기**는 Σ(레이어) 버킷 = 정의별 정리 결과 바이트 × 인스턴스 수다. 전역 중복 제거는 쓰기 때만 하므로 예상치는 **상한**이고, 보고서가 실제 값을 적는다.

---

## 3. 정리 단계와 자동 선택

사람이 숫자를 고르지 않도록 임계값을 단계로 묶는다. `AGVLITE`는 0단계부터 예상 크기(상한)를 계산해 **목표(5 MB)에 처음 드는 단계**를 쓴다. 4단계에서도 넘으면 4단계로 쓰고 경고한다. 단계 계산은 정의별 정리를 다시 도는 것이므로 읽기(A)는 한 번이다.

| 단계 | 곡선 허용 오차 | MinSize | Dense (mm 선 길이 / m²) | MaxBlob | 설명 |
|---|---|---|---|---|---|
| 0 | 5 mm | 0 | 끔 | — | 평탄화·퇴화·중복·체인만. 모양 손실 없음 |
| 1 | 10 mm | 30 mm | 끔 | — | 볼트·모따기 수준 제거 |
| 2 | 10 mm | 50 mm | 20,000 | 6 m | 로봇·판넬·소형 설비가 윤곽으로 |
| 3 | 20 mm | 100 mm | 10,000 | 30 m | 컨베이어 라인·중형 설비가 윤곽으로 |
| 4 | 30 mm | 200 mm | 5,000 | 100 m | 거의 모든 설비가 윤곽, 벽·기둥·통로만 선으로 |

Dense 값(단위 m² 안의 선 길이 mm)은 Task 1·9의 실측으로 맞춘다. 가늠: 벽 두 줄이 지나가는 1 m² 셀은 약 2,000, 롤러 50 mm 간격 컨베이어는 약 40,000, 로봇 메시는 100,000 이상.

레이어는 **고르지 않는다.** 꺼짐·동결 레이어만 제외(사람이 CAD에서 이미 내린 결정)하고, ASH 자신의 `AGV_*` 레이어(경로·CP·block: 접두사는 ASH가 정한 규약이라 이름 판단이 아니다)는 제외한다. 그 밖의 모든 레이어가 들어가며 R12 레이어 이름으로 바뀐다. 기존 `LiteLayers.RemoveKeywords` 기본 제외 규칙은 삭제한다(문자·치수·해치 엔티티 자체는 종류로 제외되므로 이름 규칙 없이도 같은 효과).

`AGVLITEX`(예외 조정): 보고서를 보고 사람이 꼭 바꿔야 할 때만 쓴다. 단계 고정(0~4), 레이어 제외 체크, 덩어리 외곽선 끄기. 선택은 `lite\<name>_lite.settings.tsv`에 저장되어 그 뒤의 `AGVLITE`가 그대로 쓴다. 블록별 선택 UI는 **만들지 않는다**(블록 수백 개를 사람이 볼 수 없고 이름으로 알 수 없음).

---

## Global Constraints

- Core는 netstandard2.0(플러그인 net48과 공유): System.Text.Json 금지, LINQ·`System.Memory`는 가능. 플러그인 UI 문구는 한국어, 수치에는 단위.
- 어떤 판단에도 블록 이름·레이어 이름을 쓰지 않는다(`AGV_` 접두사 제외와 R12 이름 변환만 예외). 테스트에서 이름을 바꿔도 결과가 같아야 한다.
- 원본 도면은 절대 바꾸지 않는다(읽기 전용 트랜잭션). 출력은 `lite\<name>_lite.dxf`, `lite\<name>_lite.report.tsv`, `lite\<name>_lite.settings.tsv`.
- R12 DXF는 LINE/POLYLINE/CIRCLE/ARC만, ASCII, 레이어 이름 31자 규칙(`LiteLayers.R12Names`) 유지. `DxfLite`가 그대로 읽을 수 있어야 한다.
- 결정적 결과: 같은 입력·단계면 같은 바이트. 해시 집합 순회에 의존하지 않는다.
- 메모리: 인스턴스별 펼친 엔티티를 전부 들고 있지 않는다. 쓰기 때 해시 키(16바이트/선)만 든다. 셀 격자는 정의의 범위만큼만 만든다(희소 사전).
- 작업 예산·중첩 한도는 유지하되(런어웨이 방어), 정의 단위 읽기로 300 MB 도면에서 예산에 닿지 않아야 한다.
- 플러그인 코드는 빌드 PC에서 실행할 수 없다: 모든 판단 로직은 Core에 두고 테스트한다. 플러그인은 읽기·폭발·진행 표시만.

## Review Focus

1. **쪼개진 곡선이 살아남는가:** 반지름 5 m 원호를 5 mm 선분 3,000개로 폭발한 입력 → 체인 결합 뒤 폴리라인 1개, MinSize 50 mm에서 지워지지 않음(Task 4 테스트).
2. **벽은 남고 설비는 윤곽이 되는가:** 100 m × 60 m 벽 두 줄 + 기둥 격자 + 2 m 로봇 메시(선 20,000개) + 20 m 롤러 컨베이어 합성 도면 → 2단계에서 로봇만, 3단계에서 컨베이어까지 윤곽, 벽·기둥은 어느 단계에서도 선 그대로(Task 5 테스트). 블록 이름을 전부 `B1, B2…`로 바꿔도 같은 결과(Task 5 테스트).
3. **3D 블록이 바르게 눕는가:** X축 90° 회전 서브블록 안의 사각형 → 평면에서 선분 1개(수직면)로, 회전 없는 것은 그대로(Task 2 테스트). 로봇 블록 실물 검증은 Task 9.
4. **중복 제거가 다른 레이어·다른 Z의 같은 선을 잡는가:** Z = 0과 Z = 1200의 같은 상자 → 선 4개(Task 3 테스트).
5. **자동 단계 선택이 목표를 맞추고 상한이 보장되는가:** 단계 k의 예상이 5 MB 이하이면 실제도 이하(Task 6 테스트: 무작위 배치 50가지에서 예상 ≥ 실제), 단계가 오를수록 예상이 단조 감소(Task 6 테스트).

---

## File Structure

Core (`src/ASH.Core/Lite/`)
- `LiteModel.cs` — 유지 + `Affine3`(3×4 행렬, 평면 부분·Z 열 분리), 3D 로컬 기하 `Geo3`(`Line3`, `Circle3`, `Arc3`, `Poly3`, `Points3`).
- `Flatten.cs` — 신규. `Geo3` × `Affine3` → `LiteEntity`(B).
- `Clean.cs` — 신규. 퇴화 제거(C), 격자 키·정확 중복·겹침 병합(D), 전역 중복 키(`SegKey`).
- `Chain.cs` — 신규. 체인 결합·직선 꼭짓점 제거·Douglas–Peucker(E).
- `SmallFilter.cs` — 신규. 범위 상자 기준 제거와 집계(F).
- `Blobs.cs` — 신규. 셀 점유·덩어리 묶기·밀도 측정·윤곽 추출(marching squares)·치환(G).
- `LiteBlocks.cs` — 신규. `BlockDef`(정의별 3D 기하·자식 인스턴스·정리 결과·통계), `BlockUse`(인스턴스).
- `LiteLevels.cs` — 신규. 단계 0~4의 임계값 표, `LiteSettings`(단계·레이어 제외·외곽선 끄기), `lite\<name>_lite.settings.tsv` 읽기/쓰기.
- `LitePipeline.cs` — 신규. 정의 하나에 B~G를 순서대로 적용, `LiteStats`(단계별 제거 개수·바이트·덩어리 수).
- `LitePlan.cs` — 수정. 정의 × 인스턴스에서 버킷 계산, 단계별 예상, 자동 단계 선택, 스트리밍 쓰기·전역 중복 제거.
- `LiteDxfWriter.cs` — 수정. 단위별 정밀도.
- `LiteReport.cs` — 수정. 쓴 단계와 임계값, 단계별 예상 크기 표, 정리 단계별 제거 표, 덩어리 상위 30(위치·크기·선 수: 사람이 도면에서 찾아볼 수 있게), 소요 시간.
- `LiteLayers.cs` — `RemoveKeywords`·`KeepByDefault` 삭제, `R12Names`·`AshPrefix`만 유지.
- `CurveApprox.cs` — 유지.

Plugin (`src/ASH.Plugin/`)
- `Commands/LiteCommands.cs` — Extractor 재작성(정의 1회 읽기 3D, 솔리드·서피스·메시 폭발, 진행률·`UserBreak` 취소), `AGVLITE`(창 없음), `AGVLITEX`(예외 조정 창).
- `Ui/LiteDialog.cs` — `AGVLITEX` 전용으로 축소: 단계 선택(단계별 예상 크기 표시), 레이어 제외 목록, 외곽선 끄기. 블록 표 없음.
- `Ui/AgvPaletteControl.cs` — "도면 경량화" 버튼은 `AGVLITE`, 작은 "조정…" 링크만 `AGVLITEX`.

Tests (`tests/ASH.Tests/Lite/`)
- `FlattenTests.cs`, `CleanTests.cs`, `ChainTests.cs`, `SmallFilterTests.cs`, `BlobsTests.cs`, `BlocksTests.cs`, `LevelsTests.cs`, 기존 `LiteTests.cs`(쓰기·보고서·추정) 갱신, `LiteLayerTests`의 키워드 테스트 삭제.

Docs
- `docs/superpowers/specs/2026-10-20-ash-plugin-design.md` §5 교체, `README.md`에 "도면 경량화" 절, `docs/plugin-manual-test.md`에 "도면 경량화 재설계" 표, `docs/lite-baseline.md`(실측).

---

### Task 1: 기준 측정 — 지금 코드로 실제 도면 재기 (사용자 PC)

**Files:**
- Modify: `src/ASH.Core/Lite/LiteReport.cs`, `src/ASH.Plugin/Commands/LiteCommands.cs` (진단용 통계)
- Create: `docs/lite-baseline.md`

- [ ] **Step 1:** Extractor가 정의마다 (인스턴스 수, 펼친 선·곡선 수, 범위 대각선, 3D 수)를 세고, 보고서에 "[블록 상위 30]" 표(인스턴스 × 선 수 내림차순, 이름은 참고용으로만)와 **셀 밀도 히스토그램**(1 m 셀 기준 선 길이 분포: 백분위 50/90/99/최대)을 추가한다. 읽기·쓰기 소요 시간도 적는다. 이 히스토그램이 §3 Dense 값의 근거다.
- [ ] **Step 2:** 대표 도면 3~5개(100~300 MB, 컨베이어·로봇·판넬이 들어 있는 것)로 AGVLITE를 돌려 `docs/lite-baseline.md`에 표로 적는다: DWG 크기, 읽기 시간, 결과 DXF 크기, 엔티티 수, 잘림 여부, 상위 블록 10개와 비중, 밀도 히스토그램.
- [ ] **Step 3:** 결과 DXF를 ZWCAD에서 열어 무엇이 깨지는지(D1·D2) 화면을 `docs/images/lite-baseline-*.png`로 남긴다. 벽·설비 부분의 1 m 셀 밀도를 몇 군데 읽어 §3 표의 Dense 가늠값을 고친다.

### Task 2: Core — 3D 평탄화와 퇴화 제거 (B, C)

**Files:**
- Modify: `src/ASH.Core/Lite/LiteModel.cs`
- Create: `src/ASH.Core/Lite/Flatten.cs`, `src/ASH.Core/Lite/Clean.cs`(퇴화 부분), `tests/ASH.Tests/Lite/FlattenTests.cs`

- [ ] **Step 1 (테스트 먼저):** X축 90° 회전 행렬 아래의 로컬 XY 사각형 → 선분 1개; Z축 회전 + 이동 아래의 원 → 원(중심·반지름 정확); 법선 (1,0,0)인 원 → 선분; 기울어진(법선 (0, sin30°, cos30°)) 원 → 타원 점열이 허용 오차 안; Z만 다른 선 → 0길이로 제거; 벌지 폴리라인이 대칭 행렬에서 방향 뒤집힘(기존 `CurveApprox` 동작 유지).
- [ ] **Step 2:** `Affine3`(12개 값, `Matrix3d.ToArray()` 행 우선에서 생성, `Then`, `Ap(x,y,z)`, 평면 부분 `Affine2`와 Z 열 분리)과 `Geo3` 레코드를 `LiteModel.cs`에 더한다.
- [ ] **Step 3:** `Flatten.To2D(Geo3, Affine3, tol, layer)`: 점은 3D 변환 후 Z 버림. 원·호·벌지는 변환된 법선이 ±Z이고 평면 부분이 유사 변환이면 정확, 비균등이면 현, 법선이 기울었으면 `CurveApprox.Ellipse`로 투영 타원 샘플, 법선이 수평이면 두 끝점 선분(원은 지름 선분).
- [ ] **Step 4:** `Clean.Degenerate(...)`: 임계 0.01 mm(도면 단위 환산). 연속 중복 꼭짓점 제거, 2점 미만 폴리라인 제거, 반지름 0 원·호 제거. "퇴화"로 집계.
- [ ] **Step 5:** 테스트 통과, 기존 `LiteTests` 통과.

### Task 3: Core — 중복·겹침 제거 (D)

**Files:**
- Modify: `src/ASH.Core/Lite/Clean.cs`
- Create: `tests/ASH.Tests/Lite/CleanTests.cs`

- [ ] **Step 1 (테스트 먼저):** 같은 선 10개(끝점 순서 뒤바뀜 섞음) → 1개; 0.05 mm 차이 → 1개(격자 0.1 mm), 0.3 mm 차이 → 2개; 같은 직선 위 [0,10]·[5,20]·[20,30] → [0,30] 1개, [0,10]·[11,20] → 2개; Z = 0·1200의 같은 상자(평탄화 뒤) → 4개; 레이어만 다른 같은 선 → 1개(먼저 온 레이어); 같은 원 3개 → 1개; 폴리라인은 꼭짓점 열 전체가 같을 때만(역순 포함); 결과 순서가 입력 순서를 따른다.
- [ ] **Step 2:** `SegKey.Of(x1,y1,x2,y2, grid)`: 끝점을 격자로 반올림해 사전순 정렬, 64-bit 해시(충돌 시 정확 비교용 구조체도 제공). 원·호·폴리라인 키도 같은 방식.
- [ ] **Step 3:** `Clean.Dedup(entities, grid)`: 첫 등장만 남긴다. `Clean.MergeCollinear(lines, grid)`: 방향각 0.01°·법선 오프셋 격자로 같은 직선 그룹을 만들고 1차원 투영 구간을 정렬·합집합(간격 ≤ 격자이면 잇는다). "중복"·"겹침" 집계.
- [ ] **Step 4:** 100만 선 무작위 입력에서 Dedup+Merge 2초 이내(Release, 느슨한 상한) 테스트.

### Task 4: Core — 체인 결합·단순화·작은 요소 제거 (E, F)

**Files:**
- Create: `src/ASH.Core/Lite/Chain.cs`, `src/ASH.Core/Lite/SmallFilter.cs`, `tests/ASH.Tests/Lite/ChainTests.cs`, `tests/ASH.Tests/Lite/SmallFilterTests.cs`

- [ ] **Step 1 (테스트 먼저):** 5 m 원호를 5 mm 선분 3,000개로 쪼갠 입력 → 폴리라인 1개, 꼭짓점 수 ≤ `CurveApprox.Steps` 상당(허용 오차 10 mm), 최대 편차 ≤ 10 mm; T자 분기에서는 3개로 끊김; 닫힌 사각형 4선 → 닫힌 폴리라인 1개; 일직선 위 꼭짓점 제거; 20 mm 원 → 제거(MinSize 50), 60 mm 원 → 유지; 10 mm 선분 100개 체인 1 m → 유지; MinSize 0 → 아무것도 안 지움; 집계가 맞음.
- [ ] **Step 2:** `Chain.Join(lines+polylines, grid)`: 끝점 격자 키 → 인접 목록, 차수 2인 꼭짓점만 통과해 체인 생성(분기·끝에서 종료), 같은 레이어만 연결. 원래 폴리라인도 끝점이 맞으면 잇는다(벌지 보존).
- [ ] **Step 3:** `Chain.Simplify(polyline, tol)`: 벌지 없는 구간에 Douglas–Peucker, 벌지 꼭짓점은 고정. 닫힌 폴리라인은 가장 먼 점에서 둘로 나눠 적용.
- [ ] **Step 4:** `SmallFilter.Apply(entities, minSize)`: `LiteBox` 대각선 < minSize → 제거, "작은 요소"로 개수·바이트 집계.
- [ ] **Step 5:** `LitePipeline.Run(defGeo, level, scaleMax)`: C → D → E → F 순서. 순서를 바꾸면(F 먼저) 쪼개진 원호가 사라지는 회귀 테스트로 순서를 고정한다.

### Task 5: Core — 덩어리 외곽선과 블록 모델 (G, 잎부터 처리)

**Files:**
- Create: `src/ASH.Core/Lite/Blobs.cs`, `src/ASH.Core/Lite/LiteBlocks.cs`, `tests/ASH.Tests/Lite/BlobsTests.cs`, `tests/ASH.Tests/Lite/BlocksTests.cs`
- Modify: `src/ASH.Core/Lite/LitePipeline.cs`, `src/ASH.Core/Lite/LitePlan.cs`

- [ ] **Step 1 (테스트 먼저):** 셀 점유: 대각선 하나가 지나는 셀 목록(Bresenham식, 선 길이를 셀마다 나눠 더함); 덩어리 묶기: 2 m 떨어진 두 정사각형 → 2개, 붙은 것 → 1개; 밀도: 1 m² 셀에 50 mm 간격 선 20개 = 20,000; 윤곽: L자 점유 셀 → L자 폴리라인 1개(볼록 껍질이 아님), 구멍은 없음; 치환: 로봇 메시(선 20,000개, 2 m) + 벽(100 m 두 줄) + 기둥 격자(0.6 m 사각형 8 m 간격) 합성 → 2단계에서 로봇만 윤곽, 벽·기둥은 그대로; 롤러 컨베이어(20 m, 50 mm 간격) → 2단계 유지, 3단계 윤곽; 잎부터: 로봇 블록 ⊂ 셀 블록(펜스 10 m 사각형) ⊂ 라인 블록 → 로봇은 윤곽, 펜스·라인은 선; 블록 이름을 `B1…Bn`으로 바꿔도 결과 바이트 동일; 모델 공간에 블록 없이 같은 기하를 넣어도 결과 동일.
- [ ] **Step 2:** `Blobs.Occupy(entities, cell)` → 희소 사전(셀 → 선 길이), `Blobs.Group(cells)`(8-연결, 유니온 파인드), `Blobs.Measure(group)`(L, ρ, n), `Blobs.Outline(group, cell)`(marching squares → 바깥 고리만 → `Chain.Simplify`).
- [ ] **Step 3:** `Blobs.Replace(entities, level)`: 조건을 만족하는 덩어리의 셀 집합에 **완전히** 들어가는 엔티티를 지우고 윤곽 폴리라인(레이어는 지운 엔티티 중 가장 많은 레이어)을 넣는다. 셀 경계에 걸친 긴 엔티티(벽이 설비를 관통)는 남긴다. "덩어리 외곽선"으로 집계(덩어리 수, 지운 선 수, 바이트). 상위 30 덩어리의 위치·크기·선 수를 `LiteStats`에 남긴다.
- [ ] **Step 4:** `BlockDef`/`BlockUse`: 정의 ID, 로컬 `Geo3`, 자식 인스턴스, 정리 결과·레이어별 바이트·통계. 모델 공간도 `BlockDef`. `LitePipeline.RunAll(defs, level)`: 깊이 우선(잎부터), 부모는 자식 정리 결과를 인스턴스 행렬로 놓은 기하 + 자기 기하로 B~G. 순환은 기존 MaxDepth로 끊는다.
- [ ] **Step 5:** `LitePlan` 재작성: `Estimate(level)` → (레이어, 플래그) 버킷(개수·바이트), `Stats`. `Layers`, `Layer()`, `Total()`, `Biggest()` API 유지. `Select`/`Write`는 정의를 인스턴스 행렬로 펼치며 스트리밍으로 쓴다.

### Task 6: Core — 자동 단계 선택·쓰기 정밀도·전역 중복 제거·설정·보고서 (§3, I)

**Files:**
- Create: `src/ASH.Core/Lite/LiteLevels.cs`, `tests/ASH.Tests/Lite/LevelsTests.cs`
- Modify: `src/ASH.Core/Lite/LiteDxfWriter.cs`, `src/ASH.Core/Lite/LitePlan.cs`, `src/ASH.Core/Lite/LiteReport.cs`, `src/ASH.Core/Lite/LiteLayers.cs`, `tests/ASH.Tests/Lite/LiteTests.cs`

- [ ] **Step 1 (테스트 먼저):** 단계 표의 값이 §3과 같고 단계가 오르면 모든 임계값이 단조; `ChooseLevel(plan, target)`이 예상이 목표 이하인 첫 단계를 고르고, 4단계도 넘으면 4; mm 도면(INSUNITS 4) 소수 2자리, m 도면(6) 5자리, inch(1) 3자리; `Bytes`가 `Write` 출력과 바이트 단위로 같음; 같은 정의를 같은 자리에 두 번 → 두 번째는 쓰지 않음; 무작위 배치 50가지에서 예상 ≥ 실제; 설정 파일 왕복(단계·제외 레이어·외곽선 끄기), 없는 파일·모르는 키·깨진 값 → 자동; 보고서에 쓴 단계·단계별 예상 표·정리 단계별 제거 표·덩어리 상위 30·소요 시간; 출력이 `DxfLite.Read`로 읽힘; `LiteLayers`에 `KeepByDefault`가 없음(컴파일 테스트).
- [ ] **Step 2:** `LiteLevels.Table`, `LiteSettings`(`Level` = 자동/0~4, `ExcludeLayers`, `NoBlobs`), TSV 읽기/쓰기(UTF-8 BOM, 모르는 키 보존).
- [ ] **Step 3:** `LiteDxfWriter.Precision(insUnits)`, `Num` 포맷 인자화. `LitePlan.Write`: `HashSet<long>`(SegKey) 전역 중복 제거, 결정적 순서. `LiteResult`에 전역 중복 수·쓴 단계.
- [ ] **Step 4:** `LiteReport`: "[단계]"(자동 선택 결과와 단계별 예상 크기), "[정리 단계]"(단계, 제거 개수, 줄인 바이트), "[덩어리 상위 30]"(중심 좌표(원본 기준)·대각선·선 밀도·지운 선 수), "[설정]", 소요 시간. 레이어 표는 유지(선택 열은 "제외" 여부만).
- [ ] **Step 5:** `LiteLayers.RemoveKeywords`·`KeepByDefault` 삭제, 관련 테스트 삭제.

### Task 7: 플러그인 — Extractor 재작성·3D 폭발·진행·취소·창 없는 `AGVLITE` (A, H)

**Files:**
- Modify: `src/ASH.Plugin/Commands/LiteCommands.cs`

- [ ] **Step 1:** `Read(BlockTableRecord)`가 `Geo3`를 만든다: `Line` 끝점 3D, `Circle`/`Arc` 중심·법선·반지름·각도, `Polyline`/`Polyline2d` 법선·고도·벌지(두께 무시), `Polyline3d` 3D 점, `Spline`/`Ellipse`/비단순 폴리라인은 `GetPointAtParameter`로 샘플(정의 로컬에서 허용 오차 / 최대 배율), `Mline`/`Region` 폭발 유지. 외부참조 내용은 항상 읽는다(미해결·언로드는 보고서에).
- [ ] **Step 2:** `Solid3d`/`Body`/`Surface`: `Explode` 재귀(Solid3d → Region·Surface → 곡선). 폭발 실패 시 `GeometricExtents` 상자로 후퇴하고 "폭발 실패 N개". `SubDMesh`·`PolyFaceMesh`·`PolygonMesh`: 꼭짓점·면에서 모서리 3D 선. 객체 하나당 상한(`CurveApprox.MaxPoints` 배수) 적용.
- [ ] **Step 3:** 인스턴스 기록: `BlockReference.BlockTransform` 전체를 `Affine3`로, MINSERT는 행·열 오프셋을 블록 로컬 변환으로 합성(기존 지름길 제거).
- [ ] **Step 4:** 진행·취소: 정의 100개마다 "읽는 중 n/m 정의, 선 k개", `HostApplicationServices.Current.UserBreak()`로 Esc 확인. 정리(Core, 단계별 예상 계산 포함)는 `Task.Run`으로 돌리고 ZWCAD 쓰레드는 "정리 중 k단계…"만 찍으며 기다린다(문서 잠금은 읽기 뒤 해제).
- [ ] **Step 5:** `AGVLITE` 흐름: 저장 확인 → 설정 파일 읽기(있으면) → 읽기 → 단계 자동 선택(또는 저장된 단계) → 쓰기 → 보고서·설정 저장 → 명령줄 요약("3단계, 엔티티 n개, x MB, 덩어리 m개 외곽선, 중복 a개·작은 요소 b개 제거") → "열까요?" 질문. 창 없음.

### Task 8: 플러그인 — `AGVLITEX` 예외 조정 창

**Files:**
- Modify: `src/ASH.Plugin/Ui/LiteDialog.cs`, `src/ASH.Plugin/Commands/LiteCommands.cs`, `src/ASH.Plugin/Ui/AgvPaletteControl.cs`

- [ ] **Step 1:** 창 내용은 세 가지뿐: 단계(자동/0~4 라디오, 각 단계의 예상 크기와 "윤곽이 되는 덩어리 수" 표시), 제외 레이어 체크 목록(기본 전부 포함, 꺼짐·동결은 회색), "덩어리 외곽선 끄기". "만들기"는 `AGVLITE`와 같은 쓰기를 한다.
- [ ] **Step 2:** 팔레트 "도면 경량화" 버튼은 `AGVLITE`, 옆의 작은 "조정…"이 `AGVLITEX`. 리본도 같게.
- [ ] **Step 3:** 저장된 설정이 자동이 아니면 `AGVLITE` 요약 첫 줄에 "저장된 조정값 사용(단계 k 고정, 레이어 n개 제외). 되돌리려면 AGVLITEX에서 자동"을 찍는다.

### Task 9: 문서·수동 테스트·실제 도면 검증·임계값 확정

**Files:**
- Modify: `docs/superpowers/specs/2026-10-20-ash-plugin-design.md`(§5), `README.md`, `docs/plugin-manual-test.md`, `docs/lite-baseline.md`

- [ ] **Step 1:** Task 1의 도면으로 다시 측정해 `docs/lite-baseline.md`에 before/after 표(크기·시간·엔티티·자동 선택 단계·정리 단계별 제거 수)를 적는다. 5 MB에 못 미치거나(단계가 너무 높아 벽까지 뭉개짐) 남는 덩어리가 있으면 §3의 Dense·MaxBlob를 고친다.
- [ ] **Step 2:** 결과 DXF를 ZWCAD에서 열어 경로 작도 스냅이 되는지, 컨베이어·로봇·판넬이 윤곽으로, 벽·기둥·통로가 선으로 보이는지 캡처를 남긴다. AutoMod 정적 그래픽 가져오기(한 번)와 서버 컷 편집기(`DxfLite`) 표시를 확인한다.
- [ ] **Step 3:** `docs/plugin-manual-test.md`에 "도면 경량화 재설계" 표: L1 `AGVLITE`가 창 없이 끝난다, L2 3D 블록이 눕는다, L3 Esc 취소, L4 같은 도면 두 번 → 같은 바이트, L5 `AGVLITEX` 단계 고정이 다음 `AGVLITE`에 유지, L6 블록 이름을 바꾼 사본에서 결과 동일, L7 폭발 실패 후퇴, L8 10 MB 초과 경고와 보고서의 덩어리 표.
- [ ] **Step 4:** 스펙 §5를 §2·§3 표로 바꾸고, README에 사용법(명령 두 개, 단계의 의미, 보고서 읽는 법)을 넣는다. `Directory.Build.props` 버전 0.21.0, `release 0.21.0` 커밋은 사용자가 결정.

---

## 설계 결정 (사람 개입 최소화를 위해 이렇게 정했다)

| 항목 | 결정 | 이유 |
|---|---|---|
| 블록 이름·레이어 이름 | 판단에 쓰지 않음 | 블록 수백 개, 이름으로 알 수 없음. `AGV_` 접두사만 ASH 규약으로 제외 |
| 설비 ↔ 건물 구분 | 셀 선 밀도와 덩어리 크기 | 설비는 빽빽하고 작다, 벽·통로는 성기고 길다. 이름·레이어와 무관 |
| 외곽선 모양 | 점유 셀 윤곽(오목 허용), 볼록 껍질 아님 | L자 설비·긴 컨베이어가 뭉개지지 않음 |
| 외부참조 | 항상 포함 | 도면의 일부. 포함 여부를 묻지 않음 |
| 3D 객체 | 항상 모서리 폭발 → 같은 파이프라인 | 별도 모드 없음. 빽빽하면 G가 윤곽으로 |
| 레이어 선택 | 없음(꺼짐·동결만 제외) | 사람이 CAD에서 이미 내린 결정만 존중 |
| 임계값 | 단계 0~4, 목표 5 MB로 자동 선택 | 숫자를 고르지 않음. `AGVLITEX`는 예외용 |
| 문자 | 계속 제거 | 구역 이름 유지는 후속 과제 |

## 후속 과제 (이번 범위 밖)

- 숨은선 제거(진짜 상면 윤곽): 점유 셀 윤곽으로 충분한지 Task 9 결과를 보고 결정.
- 큰 문자(구역·공정 이름) 유지 옵션.
- 경량 DXF를 서버에 바로 보내는 흐름(AGVSEND가 `lite\`를 쓰도록)은 별도 계획.
