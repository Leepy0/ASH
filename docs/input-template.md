# 입력 양식 (input.xlsx)

"양식 생성"으로 만든 파일의 시트·열 요약이다. 모든 시트에서 1행은 헤더, 2행은 설명/단위, 3행부터 데이터이다. 생성된 3행은 예시 값(`M001`, `*_EX`)이므로 지우거나 덮어쓴다.

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
| Materials | Material, Station, Group, TaktTime(초, >0), UnitsPerCycle(원수), UnitsPerContainer(≥1), CallThreshold(용기), UnloadThreshold(용기), InitStock(용기, 선택), SafetyStock(용기, 선택), EmptyReturn(Y/N) |
| Routes | Material, SupplyFromCP, SupplyToCP, BufferCP(선택), ReturnFromCP(선택), ReturnToCP(선택), ExtraTime(초) |
| Stations | Station, CPName, BufferCapacity(용기 수) |
| Ext | Name, Enabled(Y/N), Description |

## 검증 규칙

- 그룹의 Type은 AGV_Types에, 자재의 Group은 AGV_Groups에, Routes의 Material은 Materials에 있어야 한다.
- Routes·Stations·StandbyCPs의 CP 이름은 레이아웃에 있어야 한다.
- TaktTime > 0, UnitsPerContainer ≥ 1, MinCount ≥ 1, MaxCount ≥ MinCount.
- 타입·그룹·자재·공정 이름은 중복될 수 없다.
- 오류 위치는 `시트!행` 형식으로 표시된다. 숫자 형식 오류는 시트·행·열을 알려 준다.
