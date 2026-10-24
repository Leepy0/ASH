# 입력 양식 (input.xlsx)

"양식 생성"으로 만든 파일의 시트·열 요약이다. 모든 시트에서 1행은 헤더, 2행은 설명/단위, 3행부터 데이터이다. 생성된 3행은 예시 값(`M001`, `*_EX`)이므로 지우거나 덮어쓴다.

> **색 범례:** 파란색 = 헤더(1행), 회색 기울임 = 예시 행(3행), **연녹색(`E2EFDA`) = 입력이 필요 없는 칸**(자동 계산 수식 열, 메모·설명 열). 연녹색 칸은 건드리지 않는다. 수식 열(`CallThresholdTotal`, `UnloadThresholdTotal`)은 값을 덮어쓰면 수식이 사라지고, Settings의 `Description`은 모델에 쓰이지 않는 메모이다. 연녹색은 3행~1000행에 칠해지고, "양식 갱신"은 예전 파일에도 같은 색을 칠한다(Excel로만 쓸 수 있는 암호화 파일은 Excel이 같은 색을 칠한다).

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
| AGV_Types | Type, Category(분류 메모), SpeedLoaded(m/s), SpeedLoadedCurve(m/s, 빈칸=직선), SpeedEmpty(m/s), SpeedEmptyCurve(m/s, 빈칸=직선), Accel(m/s²), Decel(m/s²), Capacity(용기 수), LoadTime(초), UnloadTime(초), MTBF(초, 0=없음), MTTR(초), Length(m), Width(m), Color, Kind(목록: Standard / Conveyor / Ant / Magnet), Asset(에셋 이름, 비워 둔다) |
| AGV_Groups | Group, Type(AGV_Types 목록에서 선택, 여러 그룹이 같은 타입을 써도 된다), MinCount(≥1), MaxCount(≥MinCount), StandbyCPs(쉼표 구분), Dispatch(Nearest / FIFO), FinishCP(선택) |
| Materials | Material, Station(공정명, 메모), Group(운반 그룹, AGV_Groups 목록), TaktTime(초, >0), UnitsPerCycle(소요 원수), UnitsPerContainer(용기당 자재 수량, ≥1), CallThreshold(호출 임계, 용기) + CallThresholdQty(호출 임계, 자재) → CallThresholdTotal(합계, 자동), UnloadThreshold(언로딩 임계, 용기) + UnloadThresholdQty(언로딩 임계, 자재) → UnloadThresholdTotal(합계, 자동), InitStock / SafetyStock(용기, `-` 또는 빈칸 = Settings 기본값), Form(목록: Box / Pallet / Trolley), LoadLength / LoadWidth / LoadHeight(m, `-` 또는 빈칸 = 형태별 기본값) |
| Routes | Material, SupplyFromCP, SupplyToCP, BufferCP(선택), ReturnFromCP(선택), ReturnToCP(선택), ExtraTime(초), BufferWaitSec(버퍼 대기 허용, 초, 빈칸 = 0, ≥0) |

## 도면 값으로 채운 양식

`AGVEXCEL`이 새 파일을 만들 때는 도면의 CP를 읽어 채운다.

- AGV_Types: 타입 `T_AGV` 한 행. 모든 그룹이 이 타입을 쓴다. 다른 차량이 필요하면 타입 행을 추가하고 그룹의 Type을 바꾼다.
- AGV_Groups: CP의 GROUP 값마다 한 행, StandbyCPs는 그 그룹의 standby CP.
- Routes / Materials: load CP마다 자재 `M001`, `M002`...를 만들고, 같은 순서의 unload / buffer / rtnload / rtnunload CP를 짝지어 넣는다. 짝은 추측이므로 확인한다.
- 속도, T/T 같은 숫자는 도면에서 알 수 없어 예시 값 그대로이다. 반드시 고친다.
- 도면에 해당 CP가 없으면 그 시트는 회색 예시 행이 들어간다.

`input.xlsx`가 이미 있으면 `AGVEXCEL`이 묻는다.

- O(기본): 열기만 한다.
- U 갱신: 입력한 값은 그대로 두고 도면에서 새로 생긴 것만 뒤에 붙인다. 새 그룹(타입은 AGV_Types의 첫 타입), 기존 그룹의 새 standby CP, 아직 Routes의 SupplyFromCP에 없는 load CP(새 자재 이름 `M###`와 경로 한 행). 빠진 시트·열·Settings 키를 더하고, 예전 양식의 AGV_Types.Group 열은 지운다. 바뀐 내용은 명령줄에 한 줄씩 나온다. 도면에서 지운 CP·그룹은 지우지 않는다(검증 오류로 알려 준다).
- W 덮어쓰기: 한 번 더 확인한 뒤 새 양식으로 바꾼다.

U·W 모두 원래 파일을 `input.bak.<날짜_시각>.xlsx`로 먼저 복사한다. 갱신은 2행 설명도 현재 양식으로 바꾼다.

사내 문서 암호화(NASCA)로 파일을 직접 고칠 수 없거나 Excel이 파일을 열고 있으면, 같은 갱신을 Excel을 통해 한다(Excel이 연 파일이면 그 창에서 고쳐 저장, 아니면 보이지 않는 Excel이 열어 고치고 저장). Excel이 저장하므로 암호화는 유지된다. Excel에서 셀을 편집 중이면 실패하니 편집을 마치고 다시 한다.

## 목록 선택(데이터 유효성 검사)

다음 열은 셀의 드롭다운에서 고르고, 목록에 없는 값은 Excel이 받지 않는다. 양식 생성과 갱신 때마다 다시 설정한다.

| 시트.열 | 목록 |
|---|---|
| AGV_Types.Kind | Standard / Conveyor / Ant / Magnet |
| AGV_Groups.Type | AGV_Types의 Type |
| AGV_Groups.Dispatch | Nearest / FIFO |
| Materials.Group | AGV_Groups의 Group |
| Materials.Form | Box / Pallet / Trolley |
| Routes.Material | Materials의 Material |

## 임계값과 빈 용기 회수

- 호출·언로딩 임계는 용기 열과 자재 열을 더한 값이다. 임계(용기) + 임계(자재) ÷ 용기당 자재 수량. 예: 용기 1, 자재 5, 용기당 10 → 1.5 용기. 한쪽은 비워도 되지만 둘 다 비우면 오류이다. 합계는 바로 옆 "임계 합계(용기, 자동)" 열에 Excel 수식으로 보인다(참고용, 읽지 않음, 3~1000행).
- 숫자 칸의 `-`는 빈칸과 같다(기본값 사용).
- 빈 용기 회수 여부는 따로 입력하지 않는다. Routes에 rtnload(ReturnFromCP) 또는 rtnunload(ReturnToCP)가 있으면 회수한다. 예전 양식의 EmptyReturn 열은 읽지 않고, 갱신(U)하면 지워진다.

## 타입과 그룹

타입(AGV_Types)은 차량의 제원, 그룹(AGV_Groups)은 그 제원의 차량 무리(대수·대기 CP)이다. 그룹은 Type 하나를 고르고, 같은 타입을 여러 그룹이 쓸 수 있다. AutoMod 모델에서는 그룹마다 차량 타입이 하나씩 생기고 이름은 그룹명이다.

Materials의 Group은 그 자재를 어느 그룹 차량이 나르는지 정한다. Routes는 어디서 어디로 가는지만 정하므로 그룹이 둘 이상이면 Group 없이는 담당 차량을 정할 수 없어 남겨 두었다.

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

- 그룹의 Type은 AGV_Types에, 자재의 Group은 AGV_Groups에, Routes의 Material은 Materials에 있어야 한다.
- Routes·StandbyCPs의 CP 이름은 레이아웃에 있어야 한다.
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
- 가동 시간에서 빼는 대기: BufferCP에서 재고가 언로딩 임계(UnloadThreshold) 이하로 떨어지기를 기다리는 시간 중 Routes의 BufferWaitSec(버퍼 대기 허용, 초)을 넘는 부분. 한 번 기다릴 때마다 BufferWaitSec까지는 가동으로 치고 넘는 시간만 뺀다. 예: 허용 60초에 150초 기다리면 60초는 가동, 90초는 비가동. 빈칸·0이면 대기 전부를 뺀다(2026-10-24 동작). 허용 시간은 모델 로직에 들어가므로 바꾸면 AGVMODEL로 모델을 다시 만들어야 한다(0 → 값으로 바꾸면 입력 서명이 달라져 알려 준다). 입력에 정한 시간(LoadTime, UnloadTime, ExtraTime)과 주행 중 정체·block 대기는 그대로 가동이다. 가동률 = 가동 시간 ÷ (시뮬레이션 시간 − 워밍업). 이 정의는 이 버전 이후 AGVMODEL로 다시 만든 모델에만 적용된다.
- 가동률 상한(UtilMax) 초과는 실패가 아니라 경고(주황 ⚠/▲ "경고: 가동률 초과")로 보인다. 대수 탐색은 그대로 상한을 넘지 않는 대수를 찾는다. 실패(빨강)는 결품·생산 미달·교착·결과 없음뿐이다.
- 기록(result.tsv): 차량별 가동 시간·주행 거리(AutoMod total distance traveled)·작업 수, 자재별 리드타임(호출→상차 도착, 호출→하차 완료)·재고 평균·표준편차·최소·최대(AutoMod TABLE), 자재별 공급 작업 수·그 작업의 주행 거리(material 행 value8·value7, 예전 모델은 0 → 화면에 "-"). 평균 재고 − 2σ가 안전재고보다 작으면 경고.
- 대기 CP 앞 레인이 "최대 대수 × (차량 길이 + 0.5 m)"보다 짧으면(분기점이나 block까지) 모델 생성 때 경고한다.
- 시작 위치: 시뮬레이션 시작 때 그룹의 차량은 겹치지 않게 한 대씩 따로 선다. 1번 차량은 StandbyCPs의 첫 CP, 다음 차량은 나머지 StandbyCPs, 그래도 남는 차량은 첫 대기 CP 뒤 대기 레인을 따라 "차량 길이 + 0.5 m" 간격으로 자동으로 만든 시작 전용 CP(`SP_<그룹>_<번호>`)에 선다. 시작 전용 CP는 agv.asy에만 있고(작업·대기·검색 목록의 대상이 아니며 결과 화면 도면에도 나오지 않는다), 그 차량은 곧 대기 CP 뒤로 줄을 선다. 그러려면 대기 레인이 분기점·block 없이 최대 대수 × (차량 길이 + 0.5 m) 이상이어야 하고, 모자라면 "띄워 세울 자리가 N대분뿐" 경고가 나온다(남는 차량은 AutoMod가 시작 CP가 비는 순간 그 위에 겹쳐 넣는다).

## 고장·공차 속도·용량·감지 거리 (0.14.0)

- MTBF/MTTR(초): 차량마다 평균 MTBF 간격(지수분포, 달력 시간)으로 고장 나고, 평균 MTTR(지수분포) 동안 그 자리에 서 있다. 주행 중 고장은 그 구간이 끝나는 CP에서, 대기 중 고장은 다음 작업에 탈 때 남은 수리 시간만큼 반영한다. 둘 다 0보다 커야 쓰인다.
- 반복 실행(시드)마다 고장 시점이 달라진다. 수리 시간은 차량별 결과(고장 시간)에 따로 나오고, 작업 중 수리는 가동 시간에 포함된다.
- SpeedEmpty: 빈 차 주행(상차하러 가는 길, 하차 뒤, 대기 위치 복귀, 데리러 오는 길, 귀가)을 이 속도로 달린다. 0이거나 적재 속도와 같으면 적재 속도.
- Capacity: 한 번 호출 = 한 번 주행이고, 차량은 Capacity개 용기를 꽉 채워 싣고 간다(재고가 Capacity씩 늘어남). 호출 판단은 재고 + 이미 호출한 수량 기준.
- 감지 거리(AutoMod 차량 `brakedist` / `stopdist`, 적재(Default)·공차(Empty) 두 상태 모두): 앞 차와의 직선거리가 `brakedist` 이하이면 감속을 시작하고 `stopdist`에서 선다(`brakedist` = 속도²/(2×감속) + `stopdist`). 속도²/(2×감속) + 0.5 m와 0.5 m로 쓰며 속도는 적재·공차 중 큰 값이다. 감속(Decel)이 0이면 양식 기본 감속 1 ft/s²(0.3048 m/s²)로 계산한다. 어떤 경우에도 `brakedist` ≥ `stopdist` ≥ 0.5 m(모델 단위로 환산)라서 0 m로 겹쳐 서지 않는다.
- 병목 비교는 구역(block) 기준이다. 결과 화면·Excel의 block 지도와 "대기가 긴 block" 표로 본다(그룹별로 나누지 않음).

