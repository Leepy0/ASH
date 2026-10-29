# 입력 양식 (input.xlsx)

"양식 생성"으로 만든 파일의 시트·열 요약이다. 모든 시트에서 1행은 헤더, 2행은 설명/단위, 3행부터 데이터이다. 생성된 3행은 예시 값(`M001`, `*_EX`)이므로 지우거나 덮어쓴다.

> **기본 열과 고급 열:** Materials와 AGV_Types는 자주 쓰는 기본 열이 앞에 있고, 나머지 고급 열은 뒤에 Excel 그룹(윤곽)으로 묶여 접혀 있다. 열 머리글 위의 **+** 를 누르면 펼쳐진다(− 로 다시 접음). 접힌 열도 읽으므로 값이 들어 있으면 그대로 쓰인다. 열은 머리글 이름으로 찾으므로 순서가 달라도 된다. 아래 표에서 고급 열은 *기울임*.

> **색 범례:** 파란색 = 헤더(1행), 회색 기울임 = 예시 행(3행), **연녹색(`E2EFDA`) = 입력이 필요 없는 칸**(자동 계산 수식 열, 메모·설명 열). 연녹색 칸은 건드리지 않는다. 수식 열(`CallThresholdTotal`, `UnloadThresholdTotal`)은 값을 덮어쓰면 수식이 사라지고, Settings의 `Description`은 모델에 쓰이지 않는 메모이다. 연녹색은 3행~1000행에 칠해지고, "양식 갱신"은 예전 파일에도 같은 색을 칠한다(Excel로만 쓸 수 있는 암호화 파일은 Excel이 같은 색을 칠한다).

> **정렬:** 모든 시트의 모든 칸(헤더·설명·데이터)은 가운데 정렬이다. 열 전체에 걸어 두므로 나중에 입력하는 칸도 같고, "양식 갱신"이 옛 파일과 새로 붙인 열·행에도 적용한다.

> **주의:** 3행의 예시 행은 회색 기울임으로 표시된다. 반드시 실제 값으로 바꾸거나 삭제해야 한다(그대로 두면 예시 자재 M001이 모델에 들어간다).

## Settings (키/값)

| Key | 기본값 | 단위·설명 |
|---|---|---|
| Units | Meters | 쓰지 않음. 모델 단위는 `ash.settings.tsv`의 Units이고, 다르면 모델 생성이 경고한다. 이 양식의 길이·속도는 단위와 상관없이 m, m/s |
| SimLength | 8 | 시간(h). 내부에서 초로 변환 |
| Warmup | 1 | 시간(h). 통계 제외 구간 |
| Replications | 3 | 대수마다 반복 횟수 |
| SeedBase | 1 | 시드 시작값 |
| UtilMax | 0.75 | AGV 가동률 상한 |
| SafetyStock | 2 | 용기. 자재별 값이 비었을 때 기본 |
| InitStockMode | Uniform | Fixed / Uniform / Staggered |
| InitStockSpread | 10 | Uniform: ±%, Staggered: 오프셋(용기) |
| TrafficMode | Block | Block / CPOnly |
| WorkDefinition | LoadToUnload | LoadToUnload / CallToReturn |

## 데이터 시트

| 시트 | 열 (단위) |
|---|---|
| AGV_Types | Type, SpeedLoaded(m/s), SpeedEmpty(m/s), Capacity(용기 수), LoadTime(초), UnloadTime(초), Length(m), Width(m), Kind(목록: Standard / Conveyor / Ant / Magnet), *Category(분류 메모), SpeedLoadedCurve(m/s, 빈칸=직선), SpeedEmptyCurve(m/s, 빈칸=직선), Accel(m/s²), Decel(m/s²), MTBF(초, 0=없음), MTTR(초), Color, Asset(에셋 이름, 비워 둔다)* |
| AGV_Groups | Group, Type(AGV_Types 목록에서 선택, 여러 그룹이 같은 타입을 써도 된다), MinCount(≥1), MaxCount(≥MinCount), StandbyCPs(쉼표 구분), Dispatch(Nearest / FIFO), FinishCP(선택) |
| Materials | Material, Group(운반 그룹, AGV_Groups 목록), TaktTime(초, >0), UnitsPerCycle(소요 원수), UnitsPerContainer(용기당 자재 수량, ≥1), CallThreshold(호출 임계, 용기), Form(목록: Box / Pallet / Trolley), SupplyFromCP(load CP), SupplyToCP(unload CP), ReturnFromCP(rtnload CP, 선택), ReturnToCP(rtnunload CP, 선택), *Station(공정명, 메모), CallThresholdQty(호출 임계, 자재) → CallThresholdTotal(합계, 자동), UnloadThreshold(언로딩 임계, 용기) + UnloadThresholdQty(언로딩 임계, 자재) → UnloadThresholdTotal(합계, 자동), InitStock / SafetyStock(용기, `-` 또는 빈칸 = Settings 기본값), LoadLength / LoadWidth / LoadHeight(m, `-` 또는 빈칸 = 형태별 기본값), Flow(목록: Supply / Output, 빈칸 = Supply), Use(목록: Y / N, 빈칸 = Y. 아래 "자재 사용 여부"), BufferCP(buffer CP, 선택), ExtraTime(초), BufferWaitSec(버퍼 대기 허용, 초, 빈칸 = 0, ≥0)* |
| *(예전 양식) Routes* | Material, SupplyFromCP, SupplyToCP, BufferCP, ReturnFromCP, ReturnToCP, ExtraTime, BufferWaitSec. 지금 양식은 이 열들이 Materials의 자재 행에 있다(자재당 경로 1개). 아래 "예전 양식의 Routes 시트" |
| Conveyors (선택) | Conveyor(CAD 컨베이어 이름), Material(소비 자재), ProductTakt(제품 투입 간격, 초, 빈칸 = 60), Speed(m/s, 빈칸 = 0.3), Capacity(동시에 올라갈 제품 수, 빈칸 = 길이로 자동), ConsumeAt(소비 지점, 폴리선 시작에서 m, 빈칸 = 끝). 아래 "컨베이어" |

## 도면 값으로 채운 양식

`AGVEXCEL`이 새 파일을 만들 때는 도면의 CP를 읽어 채운다.

- AGV_Types: 타입 `T_AGV` 한 행. 모든 그룹이 이 타입을 쓴다. 다른 차량이 필요하면 타입 행을 추가하고 그룹의 Type을 바꾼다.
- AGV_Groups: CP의 GROUP 값마다 한 행, StandbyCPs는 그 그룹의 standby CP.
- Materials: load CP마다 자재 `M001`, `M002`...를 만들고, 같은 순서의 unload / buffer / rtnload / rtnunload CP를 같은 행의 경로 열에 짝지어 넣는다. 짝은 추측이므로 확인한다.
- `AGVCPROUTE`로 놓은 CP는 따로 다룬다. 아래 "루트 순서 배치로 만든 행".
- 속도, T/T 같은 숫자는 도면에서 알 수 없어 예시 값 그대로이다. 반드시 고친다. 호출 임계는 경로 길이로 제안 값을 넣는다(아래 "호출 임계 제안").
- 도면에 해당 CP가 없으면 그 시트는 회색 예시 행이 들어간다.
- 끝에 빈 필수 칸 요약 한 줄이 명령줄에 나온다(아래 "입력 필요 요약").

`input.xlsx`가 이미 있으면 `AGVEXCEL`이 묻는다.

- O(기본): 열기만 한다.
- U 갱신: 입력한 값은 그대로 두고 도면에서 새로 생긴 것만 뒤에 붙인다. 새 그룹(타입은 AGV_Types의 첫 타입), 기존 그룹의 새 standby CP, 아직 Materials의 SupplyFromCP에 없는 load CP(새 자재 이름 `M###`의 한 행, 경로 포함). 빠진 시트·열·Settings 키를 더하고, 예전 양식의 AGV_Types.Group 열은 지운다. 예전 양식의 Routes 시트는 Materials에 합친다(아래). 열 순서는 바꾸지 않고, 나란히 있는 고급 열끼리 그룹으로 묶어 접는다(이미 묶인 열은 그대로). 빈 호출 임계에는 제안 값을 넣는다. 바뀐 내용은 명령줄에 한 줄씩 나오고, 마지막 줄은 입력 필요 요약이다. 도면에서 지운 CP·그룹은 지우지 않는다(검증 오류로 알려 준다).
- W 덮어쓰기: 한 번 더 확인한 뒤 새 양식으로 바꾼다.

U·W 모두 원래 파일을 `input.bak.<날짜_시각>.xlsx`로 먼저 복사한다. 갱신은 2행 설명도 현재 양식으로 바꾼다.

사내 문서 암호화(NASCA)로 파일을 직접 고칠 수 없거나 Excel이 파일을 열고 있으면, 같은 갱신을 Excel을 통해 한다(Excel이 연 파일이면 그 창에서 고쳐 저장, 아니면 보이지 않는 Excel이 열어 고치고 저장). Excel이 저장하므로 암호화는 유지된다. Excel에서 셀을 편집 중이면 실패하니 편집을 마치고 다시 한다. Routes 시트 삭제(확인 창 없이)와 고급 열 묶기도 Excel이 한다.

### 예전 양식의 Routes 시트

예전 양식은 경로(SupplyFromCP ~ BufferWaitSec)가 Routes 시트에 따로 있었다. 지금은 Materials의 자재 행에 있다.

- 읽기: Materials에 `SupplyFromCP` 머리글이 있으면 경로를 자재 행에서, 없으면 예전처럼 Routes 시트에서 읽는다. 둘 다 없으면 오류.
- U 갱신이 합친다: Materials 끝에 경로 열(1·2행 머리글)을 붙이고, Routes 행의 값을 자재 이름으로 찾은 Materials 행에 옮긴 뒤 Routes 시트를 지운다. 명령줄: `Routes 시트를 Materials에 합침 N행`. 원본은 `input.bak.*.xlsx`로 남는다.
- 합치면 읽은 입력이 같다(자재·경로 값, 입력 서명). 경로 순서는 Materials 행 순서가 되므로, Routes 행을 자재와 다른 순서로 두었던 파일은 입력 서명이 달라져 `AGVMODEL`로 모델을 다시 만들라는 안내가 나올 수 있다.
- 합치지 않는 경우(Routes 시트를 그대로 두고 이유를 한 줄로 알림): 한 자재에 Routes 행이 둘 이상(모델은 자재당 경로 1개만 쓴다. 검증 오류로도 나옴), 자재 이름이 빈 Routes 행, Materials에 없는 자재의 Routes 행, Materials에 같은 자재 이름이 둘. 고친 뒤 다시 U 갱신하면 합친다. 그동안 새 load CP의 행은 예전처럼 Materials와 Routes 두 시트에 붙는다.
- 서버의 "input.xlsx 받기"·"자료 받기"도 예전 작업의 셀 스냅샷을 같은 방법으로 새 양식으로 되살린다.

### 호출 임계 제안

`AGVEXCEL`(W·U)은 호출 임계(CallThreshold)가 빈 행에 제안 값을 넣는다. W와 U가 새로 붙인 행은 예시 값(4) 대신 제안 값이 들어간다. 입력한 값은 바꾸지 않는다(호출 임계(자재) 열에 값이 있으면 빈칸으로 보지 않는다).

- 값 = 공급 한 사이클 동안 쓰는 용기 수(올림) + 1. 사이클 = 도면 경로 길이(load → buffer → unload, m) ÷ 적재 속도 + 로딩 + 언로딩 + 추가 시간(공급 상한 검증과 같은 식), 용기 하나가 버티는 시간 = T/T × 용기당 자재 수량 ÷ 소요 원수.
- 검증 경고를 피하도록 안전재고(자재 값, 없으면 Settings) + 1 이상, 1회 소비량(소요 원수 ÷ 용기당 수량, 올림) 이상으로 올린다. 예: 80 m, 1 m/s, 로딩·언로딩 20초 → 120초, 용기 600초 → 1 + 1 = 2, 안전재고 2라서 3.
- 그룹의 타입 속도, T/T, 수량, 도면 경로(길이)를 알아야 넣는다. 하나라도 모르면 칸을 그대로 둔다. 길이는 `ash.settings.tsv`의 Scale로 m로 바꾼다.
- 명령줄: `Materials: M001 호출 임계 3 (제안)`. 제안이므로 운영에 맞게 고친다.

### 입력 필요 요약

`AGVEXCEL`(W·U)의 마지막 줄은 이름이 있는 행의 빈 필수 칸 수이다. 예: `입력 필요: Materials T/T 3칸, 호출 임계 1칸 · AGV_Groups 최대 대수 1칸`. 없으면 `입력 필요 없음`.

- Materials: 운반 그룹, T/T, 소요 원수, 용기당 자재 수량, 호출 임계(자재 열로 채워도 됨), load CP, unload CP. Use가 N인 행은 세지 않는다. 컨베이어가 소비하는 자재의 T/T·소요 원수는 Conveyors 값을 쓰므로 세지 않는다.
- AGV_Groups: AGV 타입, 최소 대수, 최대 대수, 대기 CP.
- AGV_Types: 적재 속도, 공차 속도, 용기 수, 로딩, 언로딩.

## 목록 선택(데이터 유효성 검사)

다음 열은 셀의 드롭다운에서 고르고, 목록에 없는 값은 Excel이 받지 않는다. 양식 생성과 갱신 때마다 다시 설정한다. (CP 이름 열은 아래 "CP 이름 드롭다운".)

| 시트.열 | 목록 |
|---|---|
| AGV_Types.Kind | Standard / Conveyor / Ant / Magnet |
| AGV_Groups.Type | AGV_Types의 Type |
| AGV_Groups.Dispatch | Nearest / FIFO |
| Materials.Group | AGV_Groups의 Group |
| Materials.Form | Box / Pallet / Trolley |
| Materials.Use | Y / N |
| Conveyors.Material | Materials의 Material |

### CP 이름 드롭다운

Materials의 SupplyFromCP / SupplyToCP / BufferCP / ReturnFromCP / ReturnToCP와 AGV_Groups의 StandbyCPs / FinishCP는 도면의 CP 이름 목록에서 고른다. 목록은 type별로 걸러진다.

| 열 | 도면 CP type |
|---|---|
| SupplyFromCP | load |
| SupplyToCP | unload |
| BufferCP | buffer |
| ReturnFromCP | rtnload |
| ReturnToCP | rtnunload |
| StandbyCPs | standby |
| FinishCP | finish |

- 목록은 숨김 시트 `_lists`의 C열부터(load, unload, buffer, rtnload, rtnunload, standby, finish 순) 있고, A열은 에셋 이름이 쓴다. `AGVEXCEL`(W·U)이 도면을 읽어 다시 채운다. 도면에서 CP를 지우면 다음 갱신 때 목록에서도 빠진다.
- 위 표의 다른 목록과 달리 막지 않고 경고만 한다. 도면에 아직 없는 CP 이름을 미리 적으려면 경고에서 "예"를 누른다(`AGVEXCELDIFF`가 알려 준다).
- StandbyCPs는 `SB1,SB2`처럼 쉼표로 여러 개를 적을 수 있어서, 이 열에 쉼표 목록이 한 줄이라도 있으면 드롭다운을 걸지 않는다(기존 드롭다운은 갱신 때 지운다). 대기 CP가 하나뿐인 그룹만 있을 때 드롭다운이 있다.
- 사내 문서 암호화로 Excel을 통해 갱신할 때도 같은 목록과 드롭다운을 Excel이 만든다.

## 루트 순서 배치로 만든 행

`AGVCPROUTE`가 놓은 CP는 블록에 같은 루트 번호가 붙어 도면에 남는다(XData `ASH_ROUTE`). `AGVEXCEL`은 이 CP들을 낱개 CP의 순서 짝짓기에서 빼고, 루트 하나씩 행을 만든다.

- 루트의 load CP마다 Materials 한 행(경로 포함). 자재 이름은 낱개 CP 행과 같이 `M###`(비어 있는 다음 번호).
- 그룹은 루트의 그룹. SupplyToCP, ReturnFromCP, ReturnToCP는 루트에서 같은 순서의 CP이고, 모자라면 마지막 CP(load 2개, unload 1개면 둘 다 그 unload로). BufferCP는 비운다.
- AGV_Groups: StandbyCPs에 루트의 standby, FinishCP에 루트의 finish. FinishCP는 비어 있을 때만 채운다.
- 이미 있는 load·unload 짝은 건너뛴다(두 번 실행해도 늘지 않음). T/T와 임계값은 예시 값이므로 고친다.

## 낱개로 놓은 CP의 짝짓기

그룹마다 load, unload, buffer, rtnload, rtnunload를 이름 순(`L2`가 `L10` 앞)으로 세우고 같은 순번끼리 한 루트로 묶는다. 루트 수는 load, unload, rtnload, rtnunload 중 가장 많은 것에 맞추고, 모자란 쪽은 마지막 CP를 다시 쓴다. 그 종류가 그룹에 없으면 비운다. buffer는 같은 순번이 없으면 비운다.

- 예: 그룹 1에 `S1, L1, U1, U2, RL1, RL2, RU1` → `L1-U1-RL1-RU1`, `L1-U2-RL2-RU1` 두 루트(자재 `M001`, `M002`).
- 갱신(`AGVEXCEL` U)은 시트에 없는 load·unload 짝만 더한다. 사용자가 unload를 고친 행도 그 load의 행 하나로 센다.
- 무엇을 더했는지는 명령줄에 한 줄씩 나온다: `Materials: 경로 G1 (대기 S001 → 로딩 L001,L002 → …)의 자재 M002 추가`(합치지 않은 예전 양식은 `Materials·Routes:`).

## 검증 표시 (빨강·주황 칸)

`AGVEXCEL`(U·W)이 끝날 때와 `AGVMODEL`이 입력을 검증한 뒤, 문제가 있는 칸을 칠하고 메모를 단다.

- 오류: 빨강(FF9B9B), 경고: 주황(FFD27F). 칸의 메모는 `ASH 검증(오류): …` 또는 `ASH 검증(경고): …`로 시작한다.
- 칸은 문제 메시지가 가리키는 행과, 메시지에 열 이름(예: `SupplyToCP`)이 있으면 그 열이다. 열을 모르면 행의 첫 칸. 한 칸에 문제가 여럿이면 메모에 모두 적는다. 행이 없는 문제(Settings 값 등)는 명령줄에만 나온다.
- 다음 실행 때 이전 표시(`ASH 검증`으로 시작하는 메모와 그 칸의 색)를 먼저 지우고 새로 칠한다. 고친 칸은 색이 사라진다. 직접 단 메모는 지우지 않는다.
- 색만 지우므로 회색 예시 행에 표시가 겹쳤다면 색이 없어진다.
- 암호화된 파일이나 Excel이 열고 있는 파일은 Excel을 통해 같은 표시를 한다.

## 엑셀·도면 비교 (AGVEXCELDIFF)

팔레트 검증·내보내기 탭 "엑셀·도면 비교" 또는 `AGVEXCELDIFF`. 도면과 `input.xlsx`를 견주어 문제 목록(표)에 낸다. 행을 더블클릭하면 해당 CP로 이동한다(도면에 없는 CP는 위치가 없다).

| 종류 | 뜻 | 등급 |
|---|---|---|
| 도면에 없음 | Excel의 경로 열(Materials, 예전 양식은 Routes) / AGV_Groups / Stations가 쓰는 CP 이름이 도면에 없음 | 오류 |
| 타입 불일치 | CP type이 열과 다름(예: load CP를 SupplyToCP에 씀) | 오류 |
| 미사용 CP | traffic이 아닌 도면 CP를 어느 행도 쓰지 않음 | 경고 |
| 그룹 불일치 | CP의 GROUP이 자재(경로 열) 또는 그룹(StandbyCPs·FinishCP)의 그룹과 다름. CP의 GROUP이 비어 있으면 검사하지 않음 | 경고 |

## 자재 사용 여부 (Materials.Use)

- `Use`가 `N`인 자재는 모델·검증·결과에서 빼고, 같은 행의 경로도 함께 빠진다(예전 양식은 그 자재의 Routes 행을 뺀다).
- 값: `Y` / `N`(대소문자 무시), `예` / `아니오`, `1` / `0`. 빈칸은 사용. 그 밖의 값은 오류이고 시트·행·열을 알려 준다. 꺼진 행의 다른 칸은 읽지 않는다(비워도, 틀려도 된다).
- 검증은 "미사용 자재 N개 제외: A, B" 정보 메시지를 한 건 낸다. 모든 자재가 N이면 오류이다. 꺼진 자재만 쓰던 CP는 "어떤 자재도 쓰지 않는 CP" 경고가 나올 수 있다(의도된 것). 꺼진 자재의 Route가 가리키는 CP는 도면 기준으로 모델에 남는다.
- 엑셀에서 읽는 단계에서 거르므로 모델 생성, 입력 서명(`input.sig`), KPI, 서버 재실행, `input.cells.tsv` 스냅샷을 읽는 서버가 모두 같은 목록을 본다. Use를 바꾸면 서명이 바뀌므로 AGVMODEL로 모델을 다시 만든다. 서버 작업 화면의 입력 요약에는 자재 표 아래에 "미사용 자재: A, B"가 나온다.
- 예전 양식에는 Use 열이 없고, 그때는 모두 사용한다. AGVEXCEL 갱신(U)이 열을 붙인다.

## 컨베이어 (Conveyors 시트, 선택)

CAD의 컨베이어(`AGV_CONV` 폴리선, `AGVCONV`로 이름을 붙인 것)가 자재를 소비하는 방식을 정한다. 시트는 비어 있어도 되고, 없어도 읽는다. 1행 헤더, 2행 설명, 3행부터 데이터이며 열은 머리글 이름으로 찾는다. 예시 행은 없다.

- 한 행 = 컨베이어 하나. `Material`은 그 컨베이어가 소비하는 자재(Materials 목록에서 고른다). 한 자재를 컨베이어 둘이 소비할 수 없다.
- 이 자재는 Materials의 `TaktTime`을 무시하고 `ProductTakt`를 쓴다(T/T 칸은 비워도 된다). 제품 1개당 자재 수량은 그 자재의 `UnitsPerCycle`(소요 원수)이다(예전 양식의 `UnitsPerProduct` 열은 읽지 않는다). 검증이 자재마다 정보 메시지로 알린다. 그래서 기대 소비량, 결품, 공급 상한 검증은 같은 식 그대로 컨베이어 값으로 계산된다.
- Conveyors에 없는 자재는 지금처럼 Materials의 TaktTime으로 시간 소비한다.
- 검증: 컨베이어 이름 중복, 이름·자재 비어 있음, Materials에 없는 자재(Use가 N인 자재의 컨베이어 행은 함께 빠진다), 자재 하나에 컨베이어 둘, `ProductTakt`·`Speed`·소비 자재의 소요 원수 ≤ 0, `Capacity` < 1(정수여야 함), `ConsumeAt` < 0은 오류이다. CAD에 그 이름의 컨베이어가 있는지는 여기서 보지 않는다.

## 생산 출하(Flow = Output)

빈 용기를 라인에 가져다주고 풀 용기를 창고나 다음 공정으로 가져가는 흐름은 Materials의 Flow를 `Output`으로 하고 경로 열을 이렇게 넣는다.
- SupplyFromCP = 빈 용기 보관 위치, SupplyToCP = 라인(빈 용기 하차), ReturnFromCP = 라인(풀 용기 상차), ReturnToCP = 창고·다음 공정(풀 용기 하차).
- TaktTime = 용기 하나가 차는 시간, 호출 임계 = 라인에 남은 빈 용기 수.
- 계산은 공급과 같다(빈 용기를 "소비"하는 것으로 본다). 다른 점은 그림과 표시뿐이다: 공급 구간에 빈 용기, 회수 구간에 풀 용기 그림이 나오고, 결과 표의 자재 이름에 "(반출)"이 붙는다. 반출 자재의 공급 횟수 = 빈 용기 전달·풀 용기 반출 횟수, 결품 = 빈 용기가 떨어져 생산이 멈춘 시간.
- ReturnFromCP/ReturnToCP가 없으면 풀 용기를 가져가지 않으므로 경고가 나온다.
- 풀 용기가 라인 옆에 쌓이는 한도(출하 버퍼)와 빈 용기·풀 용기를 따로 나르는 운영은 반영하지 않는다. 교환(빈 용기 내려놓고 풀 용기 싣기) 운영에 맞는다.
- Output으로 바꾸면 입력 서명이 달라지므로 AGVMODEL로 모델을 다시 만든다(그림이 로직에 들어감). 예전 양식은 AGVEXCEL 갱신(U)으로 Flow 열이 생긴다.

## 임계값과 빈 용기 회수

- 호출·언로딩 임계는 용기 열과 자재 열을 더한 값이다. 임계(용기) + 임계(자재) ÷ 용기당 자재 수량. 예: 용기 1, 자재 5, 용기당 10 → 1.5 용기. 한쪽은 비워도 되지만 둘 다 비우면 오류이다. 합계는 바로 옆 "임계 합계(용기, 자동)" 열에 Excel 수식으로 보인다(참고용, 읽지 않음, 3~1000행).
- 숫자 칸의 `-`는 빈칸과 같다(기본값 사용).
- 빈 용기 회수 여부는 따로 입력하지 않는다. 경로에 rtnload(ReturnFromCP) 또는 rtnunload(ReturnToCP)가 있으면 회수한다. 예전 양식의 EmptyReturn 열은 읽지 않고, 갱신(U)하면 지워진다.

## 타입과 그룹

타입(AGV_Types)은 차량의 제원, 그룹(AGV_Groups)은 그 제원의 차량 무리(대수·대기 CP)이다. 그룹은 Type 하나를 고르고, 같은 타입을 여러 그룹이 쓸 수 있다. AutoMod 모델에서는 그룹마다 차량 타입이 하나씩 생기고 이름은 그룹명이다.

Materials의 Group은 그 자재를 어느 그룹 차량이 나르는지 정한다. 경로 열은 어디서 어디로 가는지만 정하므로 그룹이 둘 이상이면 Group 없이는 담당 차량을 정할 수 없어 남겨 두었다.

Stations·Ext 시트는 양식에서 뺐다(모델에 아무 영향이 없었다). 예전 파일에 남아 있어도 읽는다. 사용자 확장 훅은 `<Model>.arc/user_ext.m`의 `F_ext_on_call`, `F_ext_select_vehicle`, `F_ext_before_unload`, `F_ext_on_snap`을 직접 고친다.

## 형상(Kind)과 자재 형태(Form)

- AGV_Types의 Accel / Decel(m/s²)은 모델의 차량 가속·감속으로 들어간다. 양식 기본값은 0.3이고, 0이면 AutoMod 기본값을 쓴다. 예전 양식은 0이므로 고쳐야 반영된다.
- Materials의 Form: Box, Pallet, Trolley. 비우면 Box.
- AGV_Types의 SpeedLoadedCurve / SpeedEmptyCurve(m/s)는 곡선 구간 속도이다. 비우거나 `-`이면 직선 속도와 같다. Category는 분류용 메모이고 계산에 쓰이지 않으며, Asset은 에셋 이름만 담는 열로 지금은 비워 둔다.
- AGV_Types의 Kind: Standard(표준, 모두 적재), Conveyor(벨트 C/V 상차형: Box, Pallet), Ant(대차 리프팅: Trolley), Magnet(전자석 견인: Trolley). 비우면 Standard.
- 자재의 Form을 그 그룹의 AGV Kind가 싣지 못하면 경고한다. 알 수 없는 값은 오류이다.
- Kind는 AutoMod의 차량 그림도 고른다. 차량은 AGV_Types의 Length(진행 방향)와 Width(가로)로 크기가 정해진 상자들로 만들어지며, 그림 전체를 늘리지 않는다. 바퀴·롤러·리프트판 같은 부품은 차량이 커져도 같은 크기이고 롤러는 개수만 늘어난다. 범퍼 감지 구역과 적재 연결부는 데모 그림 그대로이다.
- 차량은 전후진 모두 가능하므로 앞뒤 대칭이고, 양쪽 끝면에 삼색등(빨강·노랑·초록, 상태 표시등 그림)이 붙는다. 바퀴 4개는 바닥선에 놓인다.
- Standard: 높이 600 mm, 양쪽 범퍼. Conveyor: 높이 600 mm(롤러 윗면 기준), 롤러 간격 100 mm. Magnet: 본체 높이 600 mm, 전자석 모듈은 후방에만 있다. Ant: 차체 높이 150 mm, 바퀴는 하판 아래(바닥에서 60 mm 구간)에 있고 상판(30 mm)이 맨 위에 있다.
- 적재물을 얹는 기준: Standard, Conveyor는 600 mm 윗면, Ant는 상판 윗면(150 mm)이라 대차가 상판 위에 올라탄다. Magnet은 바닥이며 대차는 차량 뒤쪽 전자석 바로 뒤에 캐스터를 바닥에 두고 선다(대차 길이 1 m 기준으로 거리를 잡음).
- 적재물 그림은 길이를 진행 방향(y), 폭을 가로(x)로 그린다.
- 새 형상은 `VehicleKinds.All`에 한 줄 추가하고 `VehicleGeometry.Build`에 그 형상의 부품을 추가한다(없으면 Standard처럼 그려진다).
- 적재물: 자재마다 Form에 따라 상자, 빈 상자, 파레트, 대차 그림이 만들어지고 크기는 LoadLength(길이) / LoadWidth(폭) / LoadHeight(높이)로 정한다. 비우면 형태별 기본값(Box 0.6×0.4×0.3, Pallet 1.1×1.1×0.15, Trolley 1.0×0.7×0.25 m). 상자는 공급할 때 꽉 찬 상자, 빈 용기 회수 구간에서는 빈 용기 그림(빈 상자, 연회색 파레트·대차)으로 바뀐다.

## 검증 규칙

- 그룹의 Type은 AGV_Types에, 자재의 Group은 AGV_Groups에 있어야 한다. 예전 양식은 Routes의 Material이 Materials에 있어야 하고 자재당 1행이다.
- 경로 열·StandbyCPs의 CP 이름은 레이아웃에 있어야 한다. 경로 문제는 경로가 있는 칸(지금 양식은 Materials의 그 자재 행, 예전 양식은 Routes 행)에 표시된다.
- TaktTime > 0, UnitsPerContainer ≥ 1, MinCount ≥ 1, MaxCount ≥ MinCount.
- 타입·그룹·자재 이름은 중복될 수 없다.
- InitStockMode / TrafficMode / WorkDefinition / Dispatch는 허용 값만 쓸 수 있고, Replications ≥ 1, Warmup < SimLength이다.
- 필수 이름(자재·그룹·타입·CP 열)이 비어 있으면 오류이다.
- 오류 위치는 `시트!행` 형식으로 표시된다. 숫자 형식 오류는 시트·행·열을 알려 준다.
- Settings의 알 수 없거나 중복된 키는 경고로 알려 준다.
- AGV 타입: Capacity ≥ 1, SpeedEmpty·MTBF·MTTR ≥ 0. MTBF와 MTTR 중 하나만 넣으면 고장이 반영되지 않는다는 경고.

## 암호화된 파일

사내 문서 암호화(NASCA)가 저장 때 `input.xlsx`를 암호화하면 플러그인이 직접 읽지 못한다. 이때 `AGVMODEL`은 Excel로 셀을 읽고(Excel이 이미 열고 있으면 그 통합 문서를 그대로), 읽은 셀을 프로젝트 폴더의 `input.cells.tsv`(텍스트)로 저장한다. `ash-run`은 `input.xlsx`를 못 읽으면 이 파일을 쓴다. 스냅샷은 마지막 `AGVMODEL` 시점의 값이므로, `input.xlsx`를 고쳤으면 `AGVMODEL`을 다시 실행한다.

## 공급 로직 (0.13.0)

- 호출: 자재마다 T/T마다 재고를 보고, (재고 + 이미 호출한 수량)이 CallThreshold 이하이면 부족한 만큼 한꺼번에 여러 건 호출한다.
- 탑승: 호출된 용기(load)는 차량이 있든 없든 그룹의 첫 대기 CP(StandbyCPs의 첫 번째)로 들어가 먼저 온 순서대로 차량에 탄다.
- 이동: 상차 CP → (BufferCP) → 하차 CP → (회수: ReturnFrom 도착 → 추가 시간(ExtraTime) → 상차 → ReturnTo 하차) → 대기 CP. 회수 구간이 없으면 추가 시간은 하차 뒤. 모든 작업은 대기 CP에 돌아와서 끝난다.
- 가동 시간: 대기 CP에서 차량에 탄 순간(다른 곳에서 데리러 온 빈 주행 포함)부터 FinishCP 도착까지, FinishCP가 없으면 대기 CP 도착까지. 대기 CP 앞에 줄 서는 시간을 빼려면 FinishCP(CP 타입 finish)를 대기 레인 입구에 둔다.
- 언로딩 임계 대기: 차량은 재고가 언로딩 임계(UnloadThreshold) 이하가 될 때까지 기다렸다가 내린다. BufferCP가 있으면 버퍼에서 기다린 뒤 언로딩 CP로 가서 내리고, 없으면 언로딩 CP에 도착해서 그 자리에서 기다린다(0.36.0부터. 그 전에는 버퍼가 없으면 임계를 무시하고 바로 내렸다). 회수 로딩(ReturnFromCP) 없이 ReturnToCP만 있으면 언로딩 직후 빈 용기 모양으로 바뀌어 ReturnToCP까지 실려 간다.
- 가동 시간에서 빼는 대기: 위 언로딩 임계 대기(버퍼 또는 언로딩 CP) 시간 중 Materials의 BufferWaitSec(버퍼 대기 허용, 초)을 넘는 부분. 한 번 기다릴 때마다 BufferWaitSec까지는 가동으로 치고 넘는 시간만 뺀다. 예: 허용 60초에 150초 기다리면 60초는 가동, 90초는 비가동. 빈칸·0이면 대기 전부를 뺀다(2026-10-24 동작). 허용 시간은 모델 로직에 들어가므로 바꾸면 AGVMODEL로 모델을 다시 만들어야 한다(0 → 값으로 바꾸면 입력 서명이 달라져 알려 준다). 입력에 정한 시간(LoadTime, UnloadTime, ExtraTime)과 주행 중 정체·block 대기는 그대로 가동이다. 가동률 = 가동 시간 ÷ (시뮬레이션 시간 − 워밍업). 이 정의(0.24.0)는 AGVMODEL로 다시 만든 모델에만 적용된다.
- 가동률 상한(UtilMax) 초과는 실패가 아니라 경고(주황 ⚠/▲ "경고: 가동률 초과")로 보인다. 대수 탐색은 그대로 상한을 넘지 않는 대수를 찾는다. 실패(빨강)는 결품·생산 미달·교착·결과 없음뿐이다.
- 시간 내역 막대(결론 탭 그룹 표, 상세 탭 차량별 표, Excel 차량 시트): 측정 시간(워밍업 이후)을 적재 주행 · 공차 주행 · 상하차(LoadTime·UnloadTime·ExtraTime) · 버퍼 대기(허용, 가동) · 고장(작업 중 수리) · 버퍼 대기(허용 초과, 비가동) · 유휴로 나눈다. 앞의 다섯 개 합이 가동 시간이라 그 비율이 가동률과 같다. 주행에는 정체·block 대기가 들어가고, 공차 주행은 가동 시간에서 나머지를 뺀 값(호출 차량의 이동, 귀환)이다. 대기 중에 난 고장은 유휴에 들어간다(고장 시간 열은 전체). 그룹 값은 권장 대수 차량의 합. 0.25.0부터이고 AGVMODEL로 다시 만든 모델에만 나온다. 그 전 모델의 결과는 "-".
- 모델을 다시 만들어야 하는 것: 가동률 정의와 자재별 공급 횟수·이동 거리(0.24.0), 시간 내역(0.25.0)은 모델 로직이 기록하므로 `AGVMODEL`로 다시 만든 모델에서만 나온다. 서버로는 다시 `AGVSEND`한다. 서버의 "대수 바꿔 다시 돌리기"는 그때 올린 모델을 그대로 쓰므로 옛 모델이면 새 출력이 없다. 입력이 그대로면 `input.sig`가 같아서 `ash-run`이 "모델 재생성 필요"를 알리지 않는다(서명은 입력만 본다). 알려 주는 것은 BufferWaitSec를 0에서 값으로 바꾼 것처럼 서명이 달라질 때뿐이므로, 새 출력이 필요하면 직접 `AGVMODEL`을 다시 실행한다.
- 기록(result.tsv): 차량별 가동 시간·주행 거리(AutoMod total distance traveled)·작업 수, 자재별 리드타임(호출→상차 도착, 호출→하차 완료)·재고 평균·표준편차·최소·최대(AutoMod TABLE), 자재별 공급 작업 수·그 작업의 주행 거리(material 행 value8·value7, 예전 모델은 0 → 화면에 "-"). 평균 재고 − 2σ가 안전재고보다 작으면 경고.
- 대기 CP 앞 레인이 "최대 대수 × (차량 길이 + 0.5 m)"보다 짧으면(분기점이나 block까지) 모델 생성 때 경고한다.
- 시작 위치: 시뮬레이션 시작 때 그룹의 차량은 겹치지 않게 한 대씩 따로 선다. 1번 차량은 StandbyCPs의 첫 CP, 다음 차량은 나머지 StandbyCPs, 그래도 남는 차량은 첫 대기 CP 뒤 대기 레인을 따라 "차량 길이 + 0.5 m" 간격으로 자동으로 만든 시작 전용 CP(`SP_<그룹>_<번호>`)에 선다. 시작 전용 CP는 agv.asy에만 있고(작업·대기·검색 목록의 대상이 아니며 결과 화면 도면에도 나오지 않는다), 그 차량은 곧 대기 CP 뒤로 줄을 선다. 그러려면 대기 레인이 분기점·block 없이 최대 대수 × (차량 길이 + 0.5 m) 이상이어야 하고, 모자라면 "띄워 세울 자리가 N대분뿐" 경고가 나온다(남는 차량은 AutoMod가 시작 CP가 비는 순간 그 위에 겹쳐 넣는다).

## 고장·공차 속도·용량·감지 거리 (0.14.0)

- MTBF/MTTR(초): 차량마다 평균 MTBF 간격(지수분포, 달력 시간)으로 고장 나고, 평균 MTTR(지수분포) 동안 그 자리에 서 있다. 주행 중 고장은 그 구간이 끝나는 CP에서, 대기 중 고장은 다음 작업에 탈 때 남은 수리 시간만큼 반영한다. 둘 다 0보다 커야 쓰인다.
- 반복 실행(시드)마다 고장 시점이 달라진다. 수리 시간은 차량별 결과(고장 시간)에 따로 나오고, 작업 중 수리는 가동 시간에 포함된다.
- SpeedEmpty: 빈 차 주행(상차하러 가는 길, 하차 뒤, 대기 위치 복귀, 데리러 오는 길, 귀가)을 이 속도로 달린다. 0이거나 적재 속도와 같으면 적재 속도.
- Capacity: 한 번 호출 = 한 번 주행이고, 차량은 Capacity개 용기를 꽉 채워 싣고 간다(재고가 Capacity씩 늘어남). 호출 판단은 재고 + 이미 호출한 수량 기준.
- 감지 거리(AutoMod 차량 `brakedist` / `stopdist`, 적재(Default)·공차(Empty) 두 상태 모두. Default 줄 `0 b`가 값이고, Empty 줄 `1 a b`는 Default × a + b라서 `1 0`으로 써 Default와 같은 거리를 쓴다. 또 로직의 차량 초기화가 `set theVehicle defined stop distance to 1.6404 ft`(0.5 m)로 정지 거리를 지정해, 시뮬레이션은 정지 거리 0.5 m 기준으로 감속 거리를 스스로 계산한다. 차량끼리 서로 감지해야 이 거리가 쓰이므로 시스템 정의(AGVSDEF)의 ALTERNATE 줄에 `ignore`를 쓰지 않는다(0.26.6까지는 `ignore`가 있어 실행 중 정지·감속 거리가 0이었다. AutoMod 실행 중 값을 찍어 0.5 m로 바뀐 것을 확인)): 앞 차와의 직선거리가 `brakedist` 이하이면 감속을 시작하고 `stopdist`에서 선다(`brakedist` = 속도²/(2×감속) + `stopdist`). 속도²/(2×감속) + 0.5 m와 0.5 m로 쓰며 속도는 적재·공차 중 큰 값이다. 감속(Decel)이 0이면 양식 기본 감속 1 ft/s²(0.3048 m/s²)로 계산한다. 어떤 경우에도 `brakedist` ≥ `stopdist` ≥ 0.5 m(모델 단위로 환산)라서 0 m로 겹쳐 서지 않는다.
- 병목 비교는 구역(block) 기준이다. 결과 화면·Excel의 block 지도와 "대기가 긴 block" 표로 본다(그룹별로 나누지 않음).

## block 그림 (0.25.0)

- AutoMod 모델의 block은 채운 상자가 아니라 외곽선으로 그린다(셀 스타일 줄의 fill 0). 아래의 경로·CP가 비쳐 보인다. 위치와 크기는 도면의 block 사각형 그대로다.
- 0.25.0 전에 만든 모델은 채운 상자이므로 `AGVMODEL`로 다시 만들어야 바뀐다.
