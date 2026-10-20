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

## CP·팔레트 (Task 13)

| 항목 | 결과 | 날짜 | ZWCAD 버전 | 메모 |
|---|---|---|---|---|
| T13-1 `AGVPALETTE` → "ASH" 팔레트가 뜨고 그룹 5개(레이아웃, 경로, CP, 교통제어, 검증·내보내기). 두 번 실행해도 팔레트 하나 | | | | |
| T13-2 팔레트에서 TYPE `load`, GROUP `G1` → `AGVCP`로 경로 위 3번 클릭 → `L001`, `L002`, `L003`이 경로 위(투영점)에 삽입, GROUP=G1 | | | | |
| T13-3 TYPE을 `traffic`으로 바꾸면 접두어 칸이 `T`로 바뀌고 다음 이름 `T001` | | | | |
| T13-4 경로에서 0.05 m보다 먼 곳 클릭 → "경로에서 너무 멉니다", 명령은 계속 | | | | |
| T13-5 상세 모드 체크 → 클릭마다 NAME/TYPE/GROUP/STATION/CAP 5번 질문, Enter는 기본값. 직접 입력한 이름은 다음 자동 이름과 겹치지 않음 | | | | |
| T13-6 `AGVCP` 도중 클릭한 CP가 Enter 전에도 화면에 보임(`FlushGraphics`), Esc로 끝내도 이미 넣은 CP는 남음 | | | | |
| T13-7 팔레트 각 버튼이 해당 명령을 실행(명령 실행 중에 누르면 어떻게 되는지도 메모) | | | | |
| T13-8 팔레트 입력란 한글 IME/포커스: 팔레트에 입력 중 도면 단축키가 가로채지 않음 | | | | |

## 교통제어·검증·내보내기 (Task 14)

설정 파일: 도면 폴더의 `ash.settings.tsv`. 한 줄에 `키<탭>값`, 소수점은 `.`. 없으면 기본값, 모르는 키는 무시. 저장 안 된 도면에서는 명령이 "도면을 먼저 저장하세요"로 멈춘다.

| 키 | 기본값 | 뜻 |
|---|---|---|
| SafetyGap | 0.5 | 안전 거리(m) |
| CpSpacing | 10 | CP 간격(m, 현재 간격 CP 생성은 꺼짐) |
| ClaimDist | (빈칸=SafetyGap) | 점유 거리 |
| BlockReach | (빈칸=VehicleLength+SafetyGap) | block 길이 |
| VehicleLength / VehicleWidth | 2.0 / 1.0 | 차량 크기(m) |
| Units | Meters | asy 단위 |
| Scale | 1.0 | 도면→모델 배율 |
| OriginX / OriginY | 0 / 0 | 원점 |
| ModelName | model | `<도면 폴더>\<ModelName>.arc`에 내보냄 |
| Groups | `G1:DefVehicle:5:1.0:2.0:1.0` | `Group:TypeName:MaxCount:Speed:Length:Width`를 `;`로 이어 씀. 그룹마다 standby/buffer CP가 1개 이상 있어야 내보내기가 됨 |

| 항목 | 결과 | 날짜 | ZWCAD 버전 | 메모 |
|---|---|---|---|---|
| T14-1 십자 경로 4개(`AGVSPLIT` 완료) + standby CP 1개(GROUP G1)인 도면을 저장 → `AGVTRAFFIC` → block `B001` 사각형 1개와 traffic CP 2개(`T001`, `T002`)가 진입 경로에만 생김 | | | | |
| T14-2 `AGVCHECK` → `[코드] 메시지 (x, y)` 줄 출력, 끝 4개 DanglingEnd(예상 4건), 첫 항목으로 확대(폭 10 m), "검사 결과 4건" | | | | |
| T14-3 `AGVEXPORT` → "오류 4건이 있습니다. 계속할까요?" Y → 메모장(기본 편집기)에 `agv.asy`가 열림: `GPATH` 4줄, `CPOINT` 3줄, `NAMELST name nl_G1 item S001`, 같은 폴더에 `traffic.tsv` 2행 | | | | |
| T14-4 명령줄 요약 `경로 +4 −0 ~0, CP +3 −0 ~0`. 다시 `AGVEXPORT` → `+0 −0 ~0`, `agv.asy.bak.<시각>` 백업 생김 | | | | |
| T14-5 이 `.arc`를 AM 편집기에서 열어 경로·CP가 보임 | | | | |
| T14-6 block이 겹치는 배치 → `AGVBLOCKAUTO`가 "B001, B002 합칠까요? [Y/N]"를 묻고 Y면 사각형 하나 | | | | |
| T14-7 교차를 분할하지 않은 도면 → `AGVCHECK`가 UnsplitIntersection 보고 후 "지금 분할할까요?" Y → 분할·재검사 | | | | |
| T14-8 standby CP 없는 그룹으로 `AGVEXPORT` → "내보내기 중단: …" 메시지, 파일 안 바뀜 | | | | |
| T14-9 `ash.settings.tsv`에 숫자 아닌 값 → "설정 파일 오류" 메시지 | | | | |
| T14-10 `AGVCHECK` 확대: `GetCurrentView`/`SetCurrentView`가 평면 뷰에서 정확히 첫 항목 중심으로 감(회전 UCS/뷰는 미지원) | | | | |
| T14-11 `DWGTITLED`/`DWGPREFIX`로 도면 폴더를 찾음(새 도면은 저장 요구) | | | | |
