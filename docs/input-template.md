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
| AGV_Types | Type, Group, SpeedLoaded(m/s), SpeedEmpty(m/s), Accel(m/s²), Decel(m/s²), Capacity(용기 수), LoadTime(초), UnloadTime(초), MTBF(초, 0=없음), MTTR(초), Length(m), Width(m), Color |
| AGV_Groups | Group, Type(그룹당 하나), MinCount(≥1), MaxCount(≥MinCount), StandbyCPs(쉼표 구분), Dispatch(Nearest / FIFO) |
| Materials | Material, Station(선택·메모), Group, TaktTime(초, >0), UnitsPerCycle(원수), UnitsPerContainer(≥1), CallThreshold(용기), UnloadThreshold(용기), InitStock(용기, 선택), SafetyStock(용기, 선택), EmptyReturn(Y/N) |
| Routes | Material, SupplyFromCP, SupplyToCP, BufferCP(선택), ReturnFromCP(선택), ReturnToCP(선택), ExtraTime(초) |

## 도면 값으로 채운 양식

`AGVEXCEL`이 새 파일을 만들 때는 도면의 CP를 읽어 채운다(기존 `input.xlsx`는 건드리지 않는다).

- AGV_Groups / AGV_Types: CP의 GROUP 값마다 한 행(타입명은 `T_<그룹>`), StandbyCPs는 그 그룹의 standby CP.
- Routes / Materials: load CP마다 자재 `M001`, `M002`...를 만들고, 같은 순서의 unload / buffer / rtnload / rtnunload CP를 짝지어 넣는다. 짝은 추측이므로 확인한다.
- 속도, T/T 같은 숫자는 도면에서 알 수 없어 예시 값 그대로이다. 반드시 고친다.
- 도면에 해당 CP가 없으면 그 시트는 회색 예시 행이 들어간다.

Stations·Ext 시트는 양식에서 뺐다(모델에 아무 영향이 없었다). 예전 파일에 남아 있어도 읽는다. 사용자 확장 훅은 `<Model>.arc/user_ext.m`의 `F_ext_on_call`, `F_ext_select_vehicle`, `F_ext_before_unload`, `F_ext_on_snap`을 직접 고친다.

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
