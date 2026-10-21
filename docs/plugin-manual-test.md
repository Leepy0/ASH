# ASH 플러그인 수동 테스트 기록표

ZWCAD가 설치된 PC에서 실행한다. 각 항목에 결과(OK/NG), 날짜, ZWCAD 버전, 메모를 적는다.
빌드 PC에는 ZWCAD가 없어서 아래 항목은 모두 실행 시 미검증 상태다.

## 설치

설치 프로그램(`dist/ASH-Setup-<버전>.exe`, 아래 I1~I9)을 쓴다. 아래 1~5는 수동 `NETLOAD` 방법이다.

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

## 모델 생성 (Phase 2 Task 6)

AutoMod 12.6이 있는 PC 필요(T16-2, T16-4). 검증·내보내기 탭 "Excel 양식"(`AGVEXCEL`), "모델 생성·빌드"(`AGVMODEL`).

| 항목 | 결과 | 날짜 | ZWCAD 버전 | 메모 |
|---|---|---|---|---|
| T16-1 저장된 도면에서 `AGVEXCEL`: `input.xlsx`가 없으면 도면 폴더에 양식을 만들고 Excel로 열림, 있으면 덮어쓰지 않고 열기만. 새 도면 → "도면을 먼저 저장하세요." | | | | |
| T16-2 양식을 채우고 `AGVMODEL`: 검사 결과(오류 시 계속 여부 질문) 뒤 상태 줄 "AGVMODEL 빌드 중…" → 명령줄에 arc 폴더 경로와 "빌드 성공", 상태 줄 "AGVMODEL 완료". `input.xlsx`가 없으면 "먼저 AGVEXCEL…" 안내 | | | | |
| T16-3 Excel에 잘못된 값(예: 없는 CP 이름, 음수 속도): 명령줄에 Excel 경고·오류가 나열되고 빌드 중단, 상태 줄 "AGVMODEL 오류" | | | | |
| T16-4 AutoMod 빌드 실패 상황(`AmHome`을 없는 폴더로): 명령줄에 MakeErr/Am2Err 앞 20줄 또는 실행 실패 메시지, 상태 줄 "AGVMODEL 오류" | | | | |
| T16-5 상태 줄·팔레트: 빌드 중 "AGVMODEL 빌드 중…", 끝나면 완료/오류. 리본 패널 "검증·내보내기"에도 두 버튼 | | | | |

## 리본 탭 (1.5 Task 3)

| 항목 | 결과 | 날짜 | ZWCAD 버전 | 메모 |
|---|---|---|---|---|
| R24 시작 시 자동 로드와 `NETLOAD`(demand-load) 둘 다: 리본에 `ASH` 탭, 패널 5개(레이아웃 / 경로 / CP / 교통제어 / 검증·내보내기), 각 패널 큰 버튼 1개 + 작은 버튼 줄(3개마다 줄바꿈), 파란 아이콘 배경 투명(검은 사각형 아님). 로드 중 예외 메시지 없음 (Review Focus 3) | | | | |
| R25 작업공간 전환(리본 ↔ 클래식 ↔ 리본, `WSCURRENT` 변경 시 재생성) 뒤에도 `ASH` 탭이 다시 있고 하나뿐(중복 없음) | | | | |
| R26 `ASH` 탭이 사라진 상태(예: 리본 재설정)에서 `AGVRIBBON` → 탭이 다시 생기고 앞으로 옴. 리본이 없는 화면에서는 "리본이 아직 없습니다…" 메시지 | | | | |
| R27 명령 실행 중(예: `AGVCP` 클릭 대기) 리본 버튼 클릭 → "실행 중인 명령을 먼저 끝내세요.", 명령 이름이 입력값으로 들어가지 않음. 대기 중이 아니면 각 버튼이 해당 명령 실행 | | | | |

## 설치 프로그램 (1.5 Task 4)

미리보기: `ASH-Setup-0.1.0.exe /capture:<png>` → `docs/images/installer-preview.png` (ZWCAD 없는 빌드 PC, 탐색 없이 렌더링). 이 PC에는 `C:\Program Files\ZWSOFT`와 `HKLM\SOFTWARE\ZWSoft`가 없어 실제 실행 시에도 "ZWCAD 설치를 찾지 못했습니다"가 나온다(`/silent`는 종료 코드 1).

| 항목 | 결과 | 날짜 | ZWCAD 버전 | 메모 |
|---|---|---|---|---|
| I1 설치: `ASH-Setup-x.y.z.exe` 실행 → UAC 요청 → 목록에 "ZWCAD 2026 (ko_KR) — C:\Program Files\ZWSOFT\ZWCAD 2026"이 선택된 채 표시 → "설치" → 로그에 복사 2건·등록 4건×(HKCU, HKLM), "완료. ZWCAD를 다시 시작하면…" 메시지와 안내 3줄("ZWCAD를 완전히 종료한 뒤 다시 시작하세요." / AGVRIBBON·AGVPALETTE / AGVINIT·Scale 0.001). ZWCAD 2026만 지원(ZWCAD.NetApi 20.26.0). `<ZWCAD>\ASH\ASH.Plugin.dll`·`ASH.Core.dll` 존재, 속성에 "차단 해제" 체크박스 없음 | | | | |
| I2 재시작 후 자동 로드: ZWCAD 재시작 → `NETLOAD` 없이 리본 `ASH` 탭과 `AGVINIT` 등 명령 동작. `regedit`에서 `HKCU\Software\ZWSoft\ZWCAD\<ver>\<lang>\Applications\ASH`의 `LOADER`/`LOADCTRLS=2`/`MANAGED=1`/`DESCRIPTION` 확인. `<ver>`는 ZWCAD가 만든 실제 키 이름 그대로(예: `2026.0`이면 `2026.0` 아래, 새 `2026` 키를 만들지 않음). ZWCAD를 한 번도 실행하지 않아 `HKCU\…\<ver>\<lang>`이 없으면 HKCU 등록은 건너뛰고(HKLM은 관리자면 등록) 경고 "…ZWCAD를 한 번 실행한 뒤 다시 설치하세요.", 종료 코드 2 | | | | |
| I3 두 버전(예: 2024, 2026) 설치 PC: 둘 다 목록에 나오고 기본 모두 선택(Review Focus 1). 설치 후 두 버전 모두 자동 로드 | | | | |
| I4 제거: `ASH-Setup-x.y.z.exe /uninstall` → "제거" → 확인 → `Applications\ASH` 키(HKCU·HKLM)와 `<ZWCAD>\ASH` 폴더 삭제, ZWCAD 재시작 시 ASH 탭 없음. ZWCAD 실행 중이면 DLL 잠김으로 "제거 중 오류" 로그 | | | | |
| I5 비관리자: UAC에서 "아니요" → 실행 안 됨(관리자 필수 매니페스트). `<ZWCAD>` 폴더 쓰기 거부 시 로그 "쓰기 실패 → %ProgramData%\ASH\<ver> 사용", LOADER가 그 경로, 완료 창에 대체 폴더 안내. 그 폴더 보안: 상속 끊김, Administrators/SYSTEM 모든 권한, Users 읽기 및 실행만 | | | | |
| I6 재설치: 설치를 두 번 → 오류 없이 덮어씀, `Applications\ASH` 키 하나뿐, 값 중복 없음(Review Focus 5). ZWCAD 실행 중 재설치는 복사 실패로 중단되고 "ZWCAD가 실행 중이면 닫고 다시 시도" 로그 | | | | |
| I7 다른 관리자 계정으로 권한 상승: 표준 사용자로 로그인 → 실행 → UAC에 관리자 계정 자격 증명 입력 → 로그 첫 줄 "등록 대상 사용자: <도메인\로그인 사용자> (HKEY_USERS\<SID>, 실행 계정 <관리자>)" → 설치 후 로그인 사용자의 `HKCU\Software\ZWSoft\ZWCAD\<ver>\<lang>\Applications\ASH`에 키가 있고 관리자 프로필에는 없음. 로그인 사용자 하이브가 없으면 경고 "사용자 하이브가 로드되지 않아…" + 종료 코드 2. 제거도 같은 하이브에서 지움 | | | | |
| I8 ZWCAD 실행 중: 설치·제거 시작 전에 `ZWCAD` 프로세스를 확인 → 창 모드는 "ZWCAD를 닫고 다시 실행하세요." 창만 뜨고 아무것도 바꾸지 않음, `/silent`는 같은 문구를 로그에 남기고 종료 코드 1. (확인 뒤 ZWCAD를 켜서 DLL이 잠기면: 설치는 "실패: CopyFile … — IOException: …", 오류 창, 종료 코드 1. 제거는 키를 지우고 폴더 삭제 실패만 모아 "제거 중 오류 n건: <폴더>" 보고, 나머지 폴더 삭제는 계속) | | | | |
| I9 미지원 버전: ZWCAD 2024(또는 2026 아닌 버전)가 있는 PC → 목록에 "ZWCAD 2024 … (미지원: ASH는 ZWCAD 2026용)"이 선택 해제된 채 표시. 직접 선택해 설치해도 로그 "미지원 버전 건너뜀: ZWCAD 2024 (ASH는 ZWCAD 2026용) …"만 남고 복사·등록 없음. `/silent` → 같은 경고, 종료 코드 2. SYSTEM 계정 `/silent`(예: 배포 도구) → 경고 "사용자 하이브를 알 수 없어 HKCU 등록을 건너뜀", HKLM만 등록, 종료 코드 2 | | | | |

종료 코드(`/silent`와 창 모드 같음): 0 성공, 1 실패, 2 경고(예: ZWCAD를 한 번도 실행하지 않아 HKCU 프로필이 없어 HKCU 등록을 건너뜀, 미지원 버전 건너뜀, 사용자 하이브를 알 수 없음 → 창에는 "경고:" 아래 각 메시지).

## 업데이트 확인 (서버 업데이트 Task 5)

서버 주소는 PC별 설정 `%APPDATA%\ASH\ash.user.tsv`(`ServerUrl<탭><주소>`), 사용자 값이 비어 있으면 PC 공용 `%ProgramData%\ASH\ash.machine.tsv`(같은 형식, 설치 프로그램 `/server:`가 항상 기록). 서명 공개키는 저장소 루트 `update-key.pub`이 빌드 때 플러그인에 내장된다(없으면 서명 없이 SHA-256만 확인하고 `AGVUPDATE`가 "서명 키 미설정" 경고). 서버는 `src/ASH.Server`(기본 포트 8082), 릴리스는 `tools/AshRelease`로 만든다. 현재 버전 = 플러그인 `<Version>`.

| 항목 | 결과 | 날짜 | ZWCAD 버전 | 메모 |
|---|---|---|---|---|
| U1 서버 주소 미설정(`ash.user.tsv` 없음): ZWCAD 시작 → 명령줄·상태 줄에 업데이트 관련 메시지 없음, 네트워크 접속 없음. 서버가 꺼져 있거나 주소가 틀린 PC(`AGVSERVER`로 `http://localhost:9`): 시작 후 3초 안에 명령줄에 "업데이트 확인 실패(서버 응답 없음)" 한 줄만(오류 창·멈춤 없음, 그동안 ZWCAD 입력 가능). 주소 형식이 틀리면 "…(서버 주소 형식 오류)" | | | | |
| U2 서버에 현재보다 높은 버전을 올린 뒤 ZWCAD 시작: 명령줄과 팔레트 상태 줄에 "ASH <새 버전> 사용 가능 — AGVUPDATE로 받기" 한 줄(시작 직후 첫 유휴 때 1회). 같은 버전이면 아무 메시지 없음 | | | | |
| U3 `AGVUPDATE`(서버 주소 없는 상태): "서버 주소(예: http://server:8082)" 입력 → 형식이 틀리면 안내 후 중단, 맞으면 `ash.user.tsv`에 저장. 새 버전이 있으면 "다운로드 중…" → "다운로드 완료: %TEMP%\ASH-update\ASH-Setup-x.y.z.exe — ZWCAD를 완전히 종료한 뒤 이 파일을 실행하세요" + 탐색기가 그 파일을 선택한 채 열림. 팔레트 헤더 원형 화살표 버튼·리본 `레이아웃` 패널 "업데이트" 버튼도 같은 동작. 최신이면 "최신 버전입니다". 이전에 받은 같은 이름의 설치 프로그램이 실행 중이면 "다운로드 실패(…이전 설치 프로그램이 실행 중이면 닫고 다시 시도하세요)" | | | | |
| U4 서명 불일치: 서버의 `ASH.json.sig`를 다른 키로 만든 것(또는 `.sig` 없음)으로 바꾸고(공개키 내장 빌드) 시작 → "업데이트 확인 실패(서명 확인 실패)", `AGVUPDATE` → 같은 메시지, `%TEMP%\ASH-update\`에 파일·`.part`가 남지 않음. 내장된 `update-key.pub`이 깨진 PEM이면 "서명 키 오류…"로 중단(서명 없이 진행하지 않음). 공개키가 없는 빌드: `AGVUPDATE`가 "서명 키 미설정 — SHA-256만 확인" 경고 후 진행 | | | | |
| U5 최소 지원 버전보다 낮은 PC(서버 `minSupported` > 플러그인 버전): 시작 시 "현재 버전이 최소 지원(<min>)보다 낮습니다 — AGVUPDATE", `AGVUPDATE`로 최신 설치 파일 받기. 서버 버전이 현재보다 낮으면 "서버의 배포 버전 … 낮습니다" 한 줄 | | | | |
| U6 `AGVSERVER`: 현재 주소와 파일 경로 표시 → 새 주소 입력 시 저장("서버 주소를 저장했습니다"), Enter(빈 입력)는 변경 없음, 잘못된 형식은 거부. 팔레트·리본 `서버 설정` 버튼도 동작. 명령 실행 중 버튼 클릭 → "실행 중인 명령을 먼저 끝내세요." | | | | |
| I10 설치 프로그램 `/server:`: `ASH-Setup-x.y.z.exe /silent /server:http://server:8082` → 로그 "서버 주소 저장: … → <로그인 사용자>\AppData\Roaming\ASH\ash.user.tsv"(다른 관리자 계정으로 권한 상승해도 로그인 사용자의 `%APPDATA%`), `ServerUrl` 한 줄. 주소 형식이 틀리면 경고 "/server: … 무시"와 종료 코드 2, 기존 파일 그대로. 창 모드: "서버 주소 (선택)" 입력란(기존 파일 값이 미리 채워짐, 비우면 설정 유지), 설치 후 같은 파일에 기록. `/uninstall`은 설정 파일을 건드리지 않음. `%ProgramData%\ASH\ash.machine.tsv`에도 같은 `ServerUrl`이 기록되고(로그 "서버 주소 저장(PC 공용)"), 폴더 권한은 Administrators/SYSTEM 전체·Users 읽기(`icacls %ProgramData%\ASH`). 사용자 파일을 지우거나 비워도 플러그인이 PC 공용 주소로 확인함. SYSTEM 계정 `/silent`(예: `psexec -s`): 로그온한 사용자의 하이브가 로드돼 있으면 그 사용자의 HKCU·사용자 파일도 쓰고, 아니면 PC 공용 파일만 쓴다(종료 코드 0). 미리 일반 사용자가 `%ProgramData%\ASH`와 `ash.machine.tsv`를 만들어 둔 상태에서 설치해도 폴더 소유자가 Administrators로 바뀌고(`icacls %ProgramData%\ASH /T`/탐색기 보안 탭) 파일이 새로 만들어짐 | | | | |

## 서버 설치 (서버 업데이트 Task 6, 실제 서버 PC)

절차는 `docs/server-install.md`. 이 PC에서는 `ASH-Server-0.1.0-win-x64.zip`을 임시 폴더에 풀어 콘솔 실행 → `publish-release.ps1` 업로드 → `UpdateClient`로 확인·다운로드까지 통과했다(`.superpowers/sdd/2026-10-21-ash-server-update-min/task-6-report.md`). 아래는 실제 서버 PC(서비스 등록, 방화벽, 다른 PC에서 접속)에서만 확인할 수 있는 항목이다.

| 항목 | 결과 | 날짜 | ZWCAD 버전 | 메모 |
|---|---|---|---|---|
| S1 서버 PC에서 zip을 풀고 `appsettings.json`에 `Admin:Token`(16자 이상)을 넣은 뒤 관리자 PowerShell에서 `ASH.Server.exe --install` → 출력에 "[SC] CreateService 성공", `sc.exe qc ASH.Server`의 `BINARY_PATH_NAME`이 `"…\ASH.Server.exe"`처럼 따옴표로 감싸짐(경로에 공백이 있어도). `sc.exe start ASH.Server` → `RUNNING`, 서버 PC 재부팅 뒤에도 자동 시작 | | | | |
| S2 방화벽 TCP 8082 허용 후 **다른 PC**의 브라우저로 `http://<서버>:8082/` → 상태 페이지("게시된 버전이 없습니다."), `curl http://<서버>:8082/healthz` → `ok`. 허용 전에는 접속 실패, 허용 뒤 성공 | | | | |
| S3 릴리스 PC에서 `$env:ASH_ADMIN_TOKEN` 설정 → `scripts\publish-release.ps1 -Server http://<서버>:8082` → "200 OK / 게시됨: ASH x.y.z". 상태 페이지에 버전·게시 시각·서명 키 ID 표시(키 내장 빌드면 "서명 키 없음" 문구가 없음). 토큰을 틀리게 주면 401, `appsettings.json` 토큰이 16자 미만이면 403 | | | | |
| S4 사용자 PC에서 `ASH-Setup-<현재보다 낮은 버전>.exe /server:http://<서버>:8082` 설치 → ZWCAD 시작 후 3초 안에 "ASH <새 버전> 사용 가능 — AGVUPDATE로 받기"(릴리스를 `-MinSupported`/`ASH_MIN_SUPPORTED` 없이 빌드했다면 기본값이 `0.0.0`이라 "최소 지원 미만" 안내는 **뜨지 않는 것이 정상**. 그 안내를 보려면 PC 버전보다 높은 `-MinSupported <버전>`으로 빌드·게시해 U5와 맞춰 확인) → `AGVUPDATE` → 다운로드 완료·탐색기 열림. 서명 키 내장 빌드에서는 "서명 키 미설정" 경고가 없음. 서버 서비스를 중지(`sc.exe stop ASH.Server`)한 뒤 ZWCAD 시작 → "업데이트 확인 실패(서버 응답 없음)" 한 줄(멈춤 없음) | | | | |
| S5 서버 업데이트(수동): 서비스 중지 → `ASH.Server.exe` 교체(`appsettings.json`·`Releases:Dir` 유지) → 시작 후 상태 페이지의 현재 버전·이전 버전 목록이 그대로이고 S4 업데이트 확인이 계속 동작. 서버 PC 재부팅 뒤에도 게시된 버전 유지 | | | | |
