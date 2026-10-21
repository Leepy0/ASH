# 입력 양식 (input.xlsx)

"양식 생성"으로 만든 파일의 시트·열 요약이다. 모든 시트에서 1행은 헤더, 2행은 설명/단위, 3행부터 데이터이다. 생성된 3행은 예시 값(`M001`, `*_EX`)이므로 지우거나 덮어쓴다.

> **주의:** 3행의 예시 행은 회색 기울임으로 표시된다. 반드시 실제 값으로 바꾸거나 삭제해야 한다(그대로 두면 예시 자재 M001이 모델에 들어간다).

## Settings (키/값)

| Key | 기본값 | 단위·설명 |
|---|---|---|
| Units | Meters | AM 거리 단위 |
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
| AGV_Types | Type, Group, SpeedLoaded(m/s), SpeedEmpty(m/s), Accel(m/s²), Decel(m/s²), Capacity(용기 수), LoadTime(초), UnloadTime(초), MTBF(초, 0=없음), MTTR(초), Length(m), Width(m), Color, Kind(선택: Standard / Conveyor / Ant / Magnet) |
| AGV_Groups | Group, Type(그룹당 하나), MinCount(≥1), MaxCount(≥MinCount), StandbyCPs(쉼표 구분), Dispatch(Nearest / FIFO), FinishCP(선택) |
| Materials | Material, Station(선택·메모), Group, TaktTime(초, >0), UnitsPerCycle(원수), UnitsPerContainer(≥1), CallThreshold(용기), UnloadThreshold(용기), InitStock(용기, 선택), SafetyStock(용기, 선택), EmptyReturn(Y/N), Form(선택: Box / Pallet / Trolley), LoadLength / LoadWidth / LoadHeight(m, 선택) |
| Routes | Material, SupplyFromCP, SupplyToCP, BufferCP(선택), ReturnFromCP(선택), ReturnToCP(선택), ExtraTime(초) |

## 도면 값으로 채운 양식

`AGVEXCEL`이 새 파일을 만들 때는 도면의 CP를 읽어 채운다(기존 `input.xlsx`는 건드리지 않는다).

- AGV_Groups / AGV_Types: CP의 GROUP 값마다 한 행(타입명은 `T_<그룹>`), StandbyCPs는 그 그룹의 standby CP.
- Routes / Materials: load CP마다 자재 `M001`, `M002`...를 만들고, 같은 순서의 unload / buffer / rtnload / rtnunload CP를 짝지어 넣는다. 짝은 추측이므로 확인한다.
- 속도, T/T 같은 숫자는 도면에서 알 수 없어 예시 값 그대로이다. 반드시 고친다.
- 도면에 해당 CP가 없으면 그 시트는 회색 예시 행이 들어간다.

Stations·Ext 시트는 양식에서 뺐다(모델에 아무 영향이 없었다). 예전 파일에 남아 있어도 읽는다. 사용자 확장 훅은 `<Model>.arc/user_ext.m`의 `F_ext_on_call`, `F_ext_select_vehicle`, `F_ext_before_unload`, `F_ext_on_snap`을 직접 고친다.

## 형상(Kind)과 자재 형태(Form)

- AGV_Types의 Accel / Decel(m/s²)은 모델의 차량 가속·감속으로 들어간다. 양식 기본값은 0.3이고, 0이면 AutoMod 기본값을 쓴다. 예전 양식은 0이므로 고쳐야 반영된다.
- Materials의 Form: Box, Pallet, Trolley. 비우면 Box.
- AGV_Types의 Kind: Standard(표준, 모두 적재), Conveyor(벨트 C/V 상차형: Box, Pallet), Ant(대차 리프팅: Trolley), Magnet(전자석 견인: Trolley). 비우면 Standard.
- 자재의 Form을 그 그룹의 AGV Kind가 싣지 못하면 경고한다. 알 수 없는 값은 오류이다.
- Kind는 AutoMod의 차량 그림도 고른다. 차량은 AGV_Types의 Length(진행 방향)와 Width(가로)로 크기가 정해진 상자들로 만들어지며, 그림 전체를 늘리지 않는다. 바퀴·롤러·리프트판 같은 부품은 차량이 커져도 같은 크기이고 롤러는 개수만 늘어난다. 범퍼 감지 구역과 적재 연결부는 데모 그림 그대로이다.
- 차량은 전후진 모두 가능하므로 앞뒤 대칭이고, 양쪽 끝면에 삼색등(빨강·노랑·초록, 상태 표시등 그림)이 붙는다. 바퀴 4개는 바닥선에 놓인다.
- Standard: 높이 600 mm, 양쪽 범퍼. Conveyor: 높이 600 mm(롤러 윗면 기준), 롤러 간격 100 mm. Magnet: 본체 높이 600 mm, 전자석 모듈은 후방에만 있다. Ant: 차체 높이 150 mm, 바퀴는 하판 아래(바닥에서 60 mm 구간)에 있고 상판(30 mm)이 맨 위에 있다.
- 적재물을 얹는 기준: Standard, Conveyor는 600 mm 윗면, Ant는 상판 윗면(150 mm)이라 대차가 상판 위에 올라탄다. Magnet은 바닥이며 대차는 차량 뒤쪽 전자석 바로 뒤에 캐스터를 바닥에 두고 선다(대차 길이 1 m 기준으로 거리를 잡음).
- 적재물 그림은 길이를 진행 방향(y), 폭을 가로(x)로 그린다.
- 새 형상은 `VehicleKinds.All`에 한 줄 추가하고 `VehicleGeometry.Build`에 그 형상의 부품을 추가한다(없으면 Standard처럼 그려진다).
- 적재물: 자재마다 Form에 따라 상자, 빈 상자, 파레트, 대차 그림이 만들어지고 크기는 LoadLength(길이) / LoadWidth(폭) / LoadHeight(높이)로 정한다. 비우면 형태별 기본값(Box 0.6×0.4×0.3, Pallet 1.1×1.1×0.15, Trolley 1.0×0.7×0.25 m). 상자는 공급할 때 꽉 찬 상자, 빈 용기 회수(EmptyReturn=Y) 구간에서는 빈 상자로 바뀐다. 파레트와 대차는 돌아올 때도 같은 그림이다.

## 검증 규칙

- 그룹의 Type은 AGV_Types에, 자재의 Group은 AGV_Groups에, Routes의 Material은 Materials에 있어야 한다.
- Routes·StandbyCPs의 CP 이름은 레이아웃에 있어야 한다.
- TaktTime > 0, UnitsPerContainer ≥ 1, MinCount ≥ 1, MaxCount ≥ MinCount.
- 타입·그룹·자재 이름은 중복될 수 없다.
- InitStockMode / TrafficMode / WorkDefinition / Dispatch는 허용 값만 쓸 수 있고, Replications ≥ 1, Warmup < SimLength이다.
- 필수 이름(자재·그룹·타입·CP 열)이 비어 있으면 오류이다. AGV 타입의 Group이 그 타입을 쓰는 그룹과 다르면 경고이다.
- 오류 위치는 `시트!행` 형식으로 표시된다. 숫자 형식 오류는 시트·행·열을 알려 준다.
- Settings의 알 수 없거나 중복된 키는 경고로 알려 준다.

## 암호화된 파일

사내 문서 암호화(NASCA)가 저장 때 `input.xlsx`를 암호화하면 플러그인이 직접 읽지 못한다. 이때 `AGVMODEL`은 Excel로 셀을 읽고(Excel이 이미 열고 있으면 그 통합 문서를 그대로), 읽은 셀을 프로젝트 폴더의 `input.cells.tsv`(텍스트)로 저장한다. `ash-run`은 `input.xlsx`를 못 읽으면 이 파일을 쓴다. 스냅샷은 마지막 `AGVMODEL` 시점의 값이므로, `input.xlsx`를 고쳤으면 `AGVMODEL`을 다시 실행한다.

## 공급 로직 (0.13.0)

- 호출: 자재마다 T/T마다 재고를 보고, (재고 + 이미 호출한 수량)이 CallThreshold 이하이면 부족한 만큼 한꺼번에 여러 건 호출한다.
- 탑승: 호출된 용기(load)는 차량이 있든 없든 그룹의 첫 대기 CP(StandbyCPs의 첫 번째)로 들어가 먼저 온 순서대로 차량에 탄다.
- 이동: 상차 CP → (BufferCP) → 하차 CP → (회수: ReturnFrom 도착 → 추가 시간(ExtraTime) → 상차 → ReturnTo 하차) → 대기 CP. 회수 구간이 없으면 추가 시간은 하차 뒤. 모든 작업은 대기 CP에 돌아와서 끝난다.
- 가동 시간: 대기 CP에서 차량에 탄 순간(다른 곳에서 데리러 온 빈 주행 포함)부터 FinishCP 도착까지, FinishCP가 없으면 대기 CP 도착까지. 대기 CP 앞에 줄 서는 시간을 빼려면 FinishCP(CP 타입 finish)를 대기 레인 입구에 둔다.
- 기록(result.tsv): 차량별 가동 시간·주행 거리(AutoMod total distance traveled)·작업 수, 자재별 리드타임(호출→상차 도착, 호출→하차 완료)·재고 평균·표준편차·최소·최대(AutoMod TABLE). 평균 재고 − 2σ가 안전재고보다 작으면 경고.
- 대기 CP 앞 레인이 "최대 대수 × (차량 길이 + 0.5 m)"보다 짧으면(분기점이나 block까지) 모델 생성 때 경고한다.
