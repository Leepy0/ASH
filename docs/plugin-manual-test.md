# ASH 플러그인 수동 테스트 기록표

ZWCAD가 설치된 PC에서 실행한다. 각 항목에 결과(OK/NG), 날짜, ZWCAD 버전, 메모를 적는다.
빌드 PC에는 ZWCAD가 없어서 아래 항목은 모두 실행 시 미검증 상태다.

## 기본 흐름 (Task 11/12 브리프)

| 항목 | 결과 | 날짜 | ZWCAD 버전 | 메모 |
|---|---|---|---|---|
| T11-1 `NETLOAD`로 `ASH.Plugin.dll` 로드, 오류 없음 | | | | |
| T11-2 `AGVINIT` → 레이어 패널에 `AGV_PATH`, `AGV_PATH_SPUR`, `AGV_DIR`, `AGV_CP`, `AGV_BLOCK`, `AGV_STATION` 6개(색 3,2,8,1,5,6) | | | | |
| T11-3 `AGV_CP` 블록 정의에 어트리뷰트 5개(NAME TYPE GROUP STATION CAP) | | | | |
| T11-4 `AGVINIT` 재실행 → "새로 만든 항목 0개", 중복 없음 | | | | |
| T12-1 십자 모양 폴리라인 2개를 `AGVPATH`로 그림 → XData name `P001`, `P002`, 화살표 표시 | | | | |
| T12-2 `AGVSPLIT` Enter → 경로 4개, XData name `P001_1`, `P001_2`, `P002_1`, `P002_2`, 화살표가 원래 방향 | | | | |
| T12-3 `AGVREVERSE`로 하나 뒤집음 → 그 경로 화살표 반대 | | | | |
| T12-4 `AGVFILLET` 반경 1 → 두 직선 사이 원호 경로 `<a>_f` 삽입, 양 끝 잘림 | | | | |
| T12-5 `AGVOFFSET`, `AGVARRAY`, `AGVJOIN` 기본 동작 | | | | |

## 실행 시 미검증 항목 (리뷰 체크리스트)

| 항목 | 결과 | 날짜 | ZWCAD 버전 | 메모 |
|---|---|---|---|---|
| R1 `AGVPATH`: `CommandEnded`의 `GlobalCommandName`이 `"PLINE"`으로 온다 | | | | |
| R2 `AGVPATH`: 폴리라인 하나 그릴 때 `ObjectAppended`가 한 번만 잡힌다(경로 1개, 이름 1개) | | | | |
| R3 `AGVPATH`: `CommandEnded` 처리기 안에서 `LockDocument` + 트랜잭션 쓰기가 오류 없이 된다 | | | | |
| R4 `AGVPATH`: PLINE 도중 Esc(`CommandCancelled`) → 이미 만들어진 폴리라인이 태그된다(또는 없으면 아무 일 없음) | | | | |
| R5 `AGVPATH`: PLINE 도중 `U`(되돌리기) 후 끝냄 → 지워진 객체로 오류 없음 | | | | |
| R6 `PLINETYPE=0`(구형 2D 폴리라인)에서 `AGVPATH` 동작 확인(현재 `Polyline`만 잡음) | | | | |
| R7 CLAYER를 색이 0번 아닌 레이어로 둔 채 `AGVINIT`(새 도면) → `AGV_CP`/`AGV_DIR` 기호 색이 각각 AGV_CP/AGV_DIR 레이어 색을 따른다 | | | | |
| R8 `AGVINIT` 없이 새 도면에서 각 명령 실행 → 레이어/블록이 자동 생성되고 오류 없음 | | | | |
| R9 MIRROR한 Arc/Polyline(Normal.Z<0): 화살표 방향과 `AGVSPLIT` 결과 형상이 원본과 일치 | | | | |
| R10 dir=1(뒤집힌) 경로가 `AGVSPLIT`/`AGVJOIN`/`AGVFILLET` 뒤에도 진행 방향 유지 | | | | |
| R11 bulge>1(반원 초과) 원호 폴리라인을 `AGVSPLIT` 등으로 다시 쓰면 형상 동일(180° 초과 원호는 둘로 나뉨) | | | | |
| R12 CP 어트리뷰트 위치·표시: NAME만 보이고 나머지 숨김, `AGV_CP` 레이어 동결 시 어트리뷰트도 같이 숨음 | | | | |
| R13 `SelectionFilter` 레이어 목록 `AGV_PATH,AGV_PATH_SPUR`(쉼표)로 두 레이어 모두 선택됨 | | | | |
| R14 `GetKeywords` 프롬프트: `[...]` 표시가 중복 없이 나오고 Enter가 기본값으로 동작 | | | | |
| R15 일반(비동적) 블록에서 `DynamicBlockTableRecord`가 자기 정의를 돌려줌(CP/스테이션 인식) | | | | |
| R16 화살표 크기 0.6: mm 단위 도면과 m 단위 도면에서 보이는 크기 확인 | | | | |
| R17 `AGVSPLIT` P 모드: 경로에서 먼 곳을 찍으면 거부 메시지, 경로 위를 찍으면 이름 충돌 없이 분할 | | | | |
| R18 `AGVOFFSET` 4가지 조합(좌/우 × 반대 방향 Y/N) 위치와 화살표 방향 | | | | |
