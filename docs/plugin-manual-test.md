# ASH 플러그인 수동 테스트 기록표

ZWCAD가 설치된 PC에서 실행한다. 각 항목에 결과(OK/NG), 날짜, ZWCAD 버전, 메모를 적는다.
빌드 PC에는 ZWCAD가 없어서 아래 항목은 모두 실행 시 미검증 상태다.

## 설치

1. 빌드 PC에서 `dotnet build ASH.sln -c Release`.
2. `src/ASH.Plugin/bin/Release/net48/ASH.Plugin.dll`과 같은 폴더의 `ASH.Core.dll`을 ZWCAD PC의 한 폴더에 복사한다.
3. 복사한 두 DLL을 각각 오른쪽 클릭 → 속성 → "차단 해제" 체크 → 확인(인터넷/메일로 받은 파일은 차단돼서 `NETLOAD`가 실패한다).
4. ZWCAD에서 `NETLOAD` → `ASH.Plugin.dll` 선택.
5. 기록표에 ZWCAD 버전을 적는다. API 패키지는 `ZWCAD.NetApi` 20.26.0(ZWCAD 2026)이다. ZWCAD 2024에서는 `src/ASH.Plugin/ASH.Plugin.csproj`의 PackageReference를 `ZWCAD.NET` 24.0.0으로 바꿔 다시 빌드한다.

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

설정 파일: 도면 폴더의 `ash.settings.tsv`. 한 줄에 `키<탭>값`, 소수점은 `.`. 없으면 교통제어·내보내기 명령이 기본값으로 새로 만들고 경로를 한 번 출력한다. 모르는 키는 무시. SafetyGap, VehicleLength, VehicleWidth, Scale은 0보다 커야 한다. 저장 안 된 도면에서는 명령이 "도면을 먼저 저장하세요"로 멈춘다.

| 키 | 기본값 | 뜻 |
|---|---|---|
| SafetyGap | 0.5 | 안전 거리(m) |
| CpSpacing | 10 | CP 간격(m, 현재 간격 CP 생성은 꺼짐) |
| ClaimDist | (빈칸=SafetyGap) | 점유 거리 |
| BlockReach | (빈칸=VehicleLength+SafetyGap) | block 길이 |
| VehicleLength / VehicleWidth | 2.0 / 1.0 | 차량 크기(m) |
| Units | Meters | asy 단위 |
| Scale | 1.0 | 도면 단위→m 배율. mm 도면은 `0.001`. 설정의 길이 값(m)은 모두 도면 단위로 바꿔(÷Scale) 쓰고, 기호 크기·허용 오차도 같이 바뀐다 |
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

## 리뷰 수정 후 미검증 항목 (Task 13/14 fix)

| 항목 | 결과 | 날짜 | ZWCAD 버전 | 메모 |
|---|---|---|---|---|
| R19 다른 명령이 입력 대기 중(예: 상세 모드 `AGVCP`의 NAME 질문)일 때 팔레트 버튼 클릭 → "실행 중인 명령을 먼저 끝내세요." 출력, 명령 이름이 입력값으로 들어가지 않음. 대기 중인 명령이 없을 때는 버튼이 정상 실행(`CMDNAMES`가 비어 있음) | | | | |
| R20 `AGVTRAFFIC`/`AGVBLOCKAUTO`에서 "합칠까요?" 질문에 Esc → block·CP 모두 생기지 않고 메시지 없이 끝남 | | | | |
| R21 팔레트를 도킹한 뒤 ZWCAD 재시작 → 같은 위치에 뜸(고정 GUID) | | | | |
| R22 팔레트 CAP 칸에 `abc` 입력 후 다른 칸으로 이동 → `1`로 바뀜 | | | | |
| R23 상세 모드에서 이미 있는 이름 입력 → "이미 있는 이름입니다." 후 다시 질문 | | | | |

## 단위 배율 (final fix)

길이 설정(SafetyGap, VehicleLength, VehicleWidth, BlockReach, ClaimDist, CpSpacing)은 m 단위다. 플러그인은 `u = 1/Scale`(1 m당 도면 단위)를 곱해 쓴다. 허용 오차 0.001 m, CP 허용 거리 0.05 m, `AGVCHECK` 확대 폭 10 m, 필렛 기본 반경 1 m도 같이 바뀐다. 블록 정의는 m 기준 크기 그대로 두고, 삽입하는 참조(CP, block 태그, 화살표)의 축척을 `u`로 한다.

| 항목 | 결과 | 날짜 | ZWCAD 버전 | 메모 |
|---|---|---|---|---|
| U1 `INSUNITS`=4(mm) 도면, `Scale` 1 → 교통제어/내보내기 명령이 "도면 단위가 mm인데 Scale=1 입니다. ash.settings.tsv의 Scale을 0.001로 설정하세요." 경고를 내고 계속 진행 | | | | |
| U2 mm 도면, `Scale` 0.001 → `AGVSPLIT`/`AGVCP` 뒤 화살표(0.6 m), CP 원(0.3 m), 글자(0.15 m)가 m 도면과 같은 실제 크기(도면상 600, 300, 150) | | | | |
| U3 mm 도면, `Scale` 0.001 → `AGVTRAFFIC` block 크기·traffic CP 위치가 같은 배치의 m 도면과 1000배 비율로 일치 | | | | |
| U4 mm 도면, `Scale` 0.001 → `AGVCP`가 경로에서 50 (=0.05 m) 이내 클릭만 받음, 메시지 "허용 0.05 m" | | | | |
| U5 mm 도면, `Scale` 0.001 → `AGVEXPORT`의 `traffic.tsv` boundary_dist가 m 값(예: 6500 → 6.5) | | | | |
| U6 `ash.settings.tsv` 없는 저장된 도면에서 `AGVTRAFFIC` → 기본 설정 파일이 만들어지고 경로가 한 번 출력됨, 다시 실행하면 출력 없음 | | | | |
| U7 `Scale`을 0이나 음수로 → "설정 파일 오류 … 0보다 커야 합니다" | | | | |
| U8 기존 AGV_CP 블록 정의에서 어트리뷰트 하나(예: STATION)를 지운 도면 → `AGVINIT`이 빠진 태그 이름을 경고 | | | | |
| U9 block 사각형 두 개가 겹치거나 맞붙은 도면 → 각 사각형이 자기 중심의 태그 이름을 읽음(`AGVCHECK`에 DuplicateBlockName 없음) | | | | |
| U10 이름 없는(`?`로 시작) 경로, 중복 이름 경로가 있는 도면 → `AGVCHECK`가 "경로 이름을 정리할까요?" Y → 첫 경로는 그대로, 나머지는 `P###`, 화살표 방향·지선 여부 유지, 재검사 결과에 이름 오류 없음 | | | | |
| U11 `AGVFILLET`을 같은 경로 쌍 근처에서 두 번 → 두 번째 원호 이름이 `<a>_f_1` | | | | |
| U12 상세 모드 `AGVCP`에서 TYPE에 `foo`, GROUP에 `a b` 입력 → 다시 질문 | | | | |
| U13 standby CP 없는 그룹으로 `AGVEXPORT` → "내보내기 중단: 그룹 G1에 standby/buffer CP가 없습니다." | | | | |

## 탭 팔레트 (1.5 Task 2)

미리보기: `scripts/capture-palette.ps1` → `docs/images/palette-preview.png` (ZWCAD 없이 렌더링, 실제 팔레트 도킹 모양은 아래에서 확인).

| 항목 | 결과 | 날짜 | ZWCAD 버전 | 메모 |
|---|---|---|---|---|
| T15-1 `AGVPALETTE` → 상단 "ASH / AGV Simul Helper" + "양식 초기화", 탭 4개(경로 / CP / 교통제어 / 검증·내보내기), 하단 상태 줄 "준비". 폭을 260까지 줄여도 탭 스크롤 화살표 없음 | | | | |
| T15-2 평면 스타일: 버튼 테두리 없음, 마우스 올리면 연파랑(#E8EEFB), 선택 탭 파란 글자 + 밑줄, 아이콘 20px가 글자 왼쪽 | | | | |
| T15-3 CP 탭에서 TYPE `load`, GROUP `G2` 입력 → 경로 탭으로 갔다가 돌아옴 → 값 그대로, `AGVCP`가 `L###`/G2로 배치 (Review Focus 4) | | | | |
| T15-4 각 탭 버튼이 해당 명령 실행(경로 7, CP 2, 교통제어 2, 검증·내보내기 2, 상단 `AGVINIT`). 명령 실행 중 클릭 → "실행 중인 명령을 먼저 끝내세요." | | | | |
| T15-5 명령 끝난 뒤 상태 줄: 성공 "`<CMD>` 완료", 변경 없음 "`<CMD>`: 변경 없음", Esc "`<CMD>` 취소", 예외 "`<CMD>` 오류: …". `AGVCHECK` 후 검증 탭 "마지막 검사 결과 n건" | | | | |
| T15-6 교통제어 탭 "Settings 열기"(톱니 아이콘): 저장된 도면 → 도면 폴더 `ash.settings.tsv`가 메모장으로 열림(없으면 기본값으로 만든 뒤). 새 도면 → "도면을 먼저 저장하세요." | | | | |

## 리본 탭 (1.5 Task 3)

| 항목 | 결과 | 날짜 | ZWCAD 버전 | 메모 |
|---|---|---|---|---|
| R24 시작 시 자동 로드와 `NETLOAD`(demand-load) 둘 다: 리본에 `ASH` 탭, 패널 5개(레이아웃 / 경로 / CP / 교통제어 / 검증·내보내기), 각 패널 큰 버튼 1개 + 작은 버튼 줄(3개마다 줄바꿈), 파란 아이콘 배경 투명(검은 사각형 아님). 로드 중 예외 메시지 없음 (Review Focus 3) | | | | |
| R25 작업공간 전환(리본 ↔ 클래식 ↔ 리본, `WSCURRENT` 변경 시 재생성) 뒤에도 `ASH` 탭이 다시 있고 하나뿐(중복 없음) | | | | |
| R26 `ASH` 탭이 사라진 상태(예: 리본 재설정)에서 `AGVRIBBON` → 탭이 다시 생기고 앞으로 옴. 리본이 없는 화면에서는 "리본이 아직 없습니다…" 메시지 | | | | |
| R27 명령 실행 중(예: `AGVCP` 클릭 대기) 리본 버튼 클릭 → "실행 중인 명령을 먼저 끝내세요.", 명령 이름이 입력값으로 들어가지 않음. 대기 중이 아니면 각 버튼이 해당 명령 실행 | | | | |
