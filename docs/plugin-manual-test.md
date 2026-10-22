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
| T11-2 `AGVINIT` → 레이어 패널에 `AGV_PATH`, `AGV_PATH_SPUR`, `AGV_PATH_TWO`, `AGV_PATH_CRAB`, `AGV_DIR`, `AGV_CP`, `AGV_BLOCK`, `AGV_STATION` 8개(색 3,2,4,30,8,1,5,6), 블록 `AGV_DIR`와 `AGV_DIR2` | | | | |
| T12-1b `AGVPATH` → 종류 선택 "[단방향(O)/양방향(T)/지선(S)/크랩(C)] <단방향>"에서 각각 그린 뒤 레이어가 `AGV_PATH`/`_TWO`/`_SPUR`/`_CRAB`이고 명령줄에 "경로 P00x 추가 (양방향)"처럼 종류가 나옴. 양방향은 양쪽 화살표, 나머지는 한쪽 화살표 | | | | |
| T12-1c `AGVPTYPE`(리본 "경로 종류", 팔레트 "경로 종류 변경") → 경로 선택 후 종류 지정 → 레이어·화살표가 바뀌고, `AGVEXPORT` 뒤 `agv.asy`의 `GPATH`에 `two`/`spur`/`crab`이 해당 경로에만 붙음(지선에 `crab`이 자동으로 붙지 않음) | | | | |
| T12-1d 방향표가 너무 작지 않음: 기본 2 m. `ash.settings.tsv`에 `ArrowSize<탭>5`를 넣고 아무 편집 명령 실행 → 5 m로 바뀜. 짧은 원호 조각에서는 조각 길이의 80%로 줄어 겹치지 않음 | | | | |
| T11-3 `AGV_CP` 블록 정의에 어트리뷰트 4개(NAME TYPE GROUP CAP). STATION이 들어 있던 예전 도면의 블록은 그대로 쓸 수 있음 | | | | |
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
| R13 `SelectionFilter` 레이어 목록 `AGV_PATH,AGV_PATH_TWO,AGV_PATH_SPUR,AGV_PATH_CRAB`(쉼표)로 네 레이어 모두 선택됨 | | | | |
| R14 `GetKeywords` 프롬프트: `[...]` 표시가 중복 없이 나오고 Enter가 기본값으로 동작 | | | | |
| R15 일반(비동적) 블록에서 `DynamicBlockTableRecord`가 자기 정의를 돌려줌(CP/스테이션 인식) | | | | |
| R16 화살표 크기 0.6: mm 단위 도면과 m 단위 도면에서 보이는 크기 확인 | | | | |
| R17 `AGVSPLIT` P 모드: 경로에서 먼 곳을 찍으면 거부 메시지, 경로 위를 찍으면 이름 충돌 없이 분할 | | | | |
| R18 `AGVOFFSET` 4가지 조합(좌/우 × 반대 방향 Y/N) 위치와 화살표 방향 | | | | |

## CP·팔레트 (Task 13)

| 항목 | 결과 | 날짜 | ZWCAD 버전 | 메모 |
|---|---|---|---|---|
| T13-1 `AGVPALETTE` → "ASH" 팔레트가 뜨고 그룹 5개(레이아웃, 경로, CP, 교통제어, 검증·내보내기). 두 번 실행해도 팔레트 하나 | | | | |
| T13-2 팔레트 GROUP `G1`, CP 탭 `load` 버튼 → 경로 위 3번 클릭 → `L001`, `L002`, `L003`이 경로 위(투영점)에 삽입, GROUP=G1 | | | | |
| T13-3 `traffic` 버튼을 누르면 접두어 칸이 `T`로 바뀌고 다음 이름 `T001` | | | | |
| T13-4 경로에서 0.05 m보다 먼 곳 클릭 → "경로에서 너무 멉니다", 명령은 계속 | | | | |
| T13-5 상세 모드 체크 → 클릭마다 NAME/TYPE/GROUP/CAP 4번 질문, Enter는 기본값. 직접 입력한 이름은 다음 자동 이름과 겹치지 않음 | | | | |
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
| BlockReach | (빈칸=자동) | 교차점에서 block 끝까지 각 가지 길이(m). 빈칸이면 가지마다 그 자리에 선 차량이 다른 경로 차량과 SafetyGap 이상 떨어지는 거리까지만 잡는다(직각 교차는 VehicleWidth/2+SafetyGap). 그 끝에서 다음 꺾임점까지 차량 한 대(VehicleLength+SafetyGap)도 못 서면 그 짧은 구간을 통째로 넣는다(진입 CP가 block 경계에 붙어 AutoMod에서 교착) |
| VehicleLength / VehicleWidth | 2.0 / 1.0 | 차량 크기(m) |
| Units | Meters | asy 단위 |
| Scale | 1.0 | 도면 단위→m 배율. mm 도면은 `0.001`. 설정의 길이 값(m)은 모두 도면 단위로 바꿔(÷Scale) 쓰고, 기호 크기·허용 오차도 같이 바뀐다 |
| OriginX / OriginY | 0 / 0 | 원점 |
| ModelName | model | `<도면 폴더>\<ModelName>.arc`에 내보냄 |
| Groups | `G1:DefVehicle:5:1.0:2.0:1.0` | `Group:TypeName:MaxCount:Speed:Length:Width`를 `;`로 이어 씀. 그룹마다 standby/buffer CP가 1개 이상 있어야 내보내기가 됨 |

| 항목 | 결과 | 날짜 | ZWCAD 버전 | 메모 |
|---|---|---|---|---|
| T14-1 십자 경로 4개(`AGVSPLIT` 완료) + standby CP 1개(GROUP G1)인 도면을 저장 → `AGVTRAFFIC` → block `B001` 사각형 1개와 traffic CP 2개(`T001`, `T002`)가 진입 경로에만 생김. 기본 설정(폭 1.0, SafetyGap 0.5)이면 사각형은 교차점 중심 2 m × 2 m(예전 7 m × 7 m) | | | | |
| T14-2 `AGVCHECK` → `[코드] 메시지 (x, y)` 줄 출력, 끝 4개 DanglingEnd(예상 4건), 첫 항목으로 확대(폭 10 m), "검사 결과 4건". 같은 도면을 양방향 경로로 그리면 끝이 이어지지 않아도 DanglingEnd가 나오지 않음(단방향만 보고) | | | | |
| T14-3 `AGVEXPORT` → "오류 4건이 있습니다. 계속할까요?" Y → 명령줄에 `asy:` 경로만 출력되고 파일은 열리지 않음. `agv.asy` 내용: `GPATH` 4줄, `CPOINT` 3줄, `NAMELST name nl_G1 item S001`, 같은 폴더에 `traffic.tsv` 2행 | | | | |
| T14-4 명령줄 요약 `경로 +4 −0 ~0, CP +3 −0 ~0`. 다시 `AGVEXPORT` → `+0 −0 ~0`, `agv.asy.bak.<시각>` 백업 생김 | | | | |
| T14-5 이 `.arc`를 AM 편집기에서 열어 경로·CP가 보임 | | | | |
| T14-6 교차점 두 개 사이가 차량 한 대(VehicleLength+2×SafetyGap) 자리보다 좁은 배치(예: 차량 2 m, 교차점 3 m 간격) → `AGVBLOCKAUTO`가 "B001, B002 가까워 합칠까요? [Y/N] <Y>"를 묻고 Y면 사각형 하나. 합친 사각형 안에 load/standby 같은 정차 CP가 들어가면 질문 끝에 "(정차 CP … 교착 위험)"이 붙고 기본값이 N | | | | |
| T14-6b 교차점 사이에 차량 한 대가 설 자리가 있는 배치(예: 6 m 간격) → 질문 없이 교차점마다 작은 block 하나씩 | | | | |
| T14-6c 직접 그린 `AGV_BLOCK` 레이어 닫힌 사각형 몇 개(태그 없는 것, 이름이 뒤섞인 것 포함) → `AGVBLOCKNUM` → 원점(0,0)에서 사각형 중심까지 거리가 가까운 순으로 `B001`부터 다시 붙음(거리가 같으면 y 작은 것, 다음 x 작은 것). 태그 없던 사각형은 중심에 태그가 생기고 명령줄에 "태그 없던 n개에 태그 추가". `AGVCHECK`에 DuplicateBlockName 없음. Ctrl+Z 한 번으로 전부 되돌아감 | | | | |
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
| U8 기존 AGV_CP 블록 정의에서 어트리뷰트 하나(예: CAP)를 지운 도면 → `AGVINIT`이 빠진 태그 이름을 경고 | | | | |
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
| T15-3 CP 탭에서 GROUP `G2` 입력 → 경로 탭으로 갔다가 돌아옴 → 값 그대로, `load` 버튼이 `L###`/G2로 배치 (Review Focus 4) | | | | |
| T15-4 각 탭 버튼이 해당 명령 실행(경로 7, CP TYPE별 8개는 모두 `AGVCP`, 교통제어 3(`AGVBLOCKAUTO`, `AGVBLOCKNUM`, `AGVTRAFFIC`), 검증·내보내기 2, 상단 `AGVINIT`). 명령 실행 중 클릭 → "실행 중인 명령을 먼저 끝내세요." | | | | |
| T15-5 명령 끝난 뒤 상태 줄: 성공 "`<CMD>` 완료", 변경 없음 "`<CMD>`: 변경 없음", Esc "`<CMD>` 취소", 예외 "`<CMD>` 오류: …". `AGVCHECK` 후 검증 탭 "마지막 검사 결과 n건" | | | | |
| T15-6 검증·내보내기 탭 "설정 열기"(톱니 아이콘): 저장된 도면 → 설정 창이 열림(T15-7, 파일이 없으면 기본값으로 만든 뒤). 새 도면 → "도면을 먼저 저장하세요." | | | | |
| T15-7 설정 창: `ash.settings.tsv`의 항목마다 한글 이름·입력칸·안내(파일 키 포함), 맨 아래 "서버 주소"(AGVSERVER와 같은 값). Groups는 읽기 전용. 값을 바꾸고 확인 → 파일에 저장, 명령줄 "설정을 저장했습니다", 메모장으로 열어 같은 값. 취소/Esc → 두 파일 모두 그대로 | | | | |
| T15-8 설정 창 검사: 안전 거리에 `abc`·`0`, 원점 Y에 `1,5`, 모델 이름에 `a b`, 서버 주소에 `server:8082`(http 없음) → 각각 경고 창, 해당 칸으로 커서 이동, 아무 파일도 안 바뀜. 서버 주소를 비우고 확인 → `ash.user.tsv`의 ServerUrl이 빈칸 | | | | |
| T15-9 설정 창 "텍스트로 열기" → 창이 닫히고 메모장으로 `ash.settings.tsv`가 열림. 파일에 숫자 아닌 값이 있으면 "설정 열기"가 창 대신 "설정 파일 오류 … 메모장으로 엽니다" | | | | |

## 모델 생성 (Phase 2 Task 6)

AutoMod 12.6이 있는 PC 필요(T16-2, T16-4), T16-5는 AutoMod가 없는 PC와 서버. 검증·내보내기 탭 "Excel 양식"(`AGVEXCEL`), "모델 생성"(`AGVMODEL`, 예전 "내보내기" 버튼을 합침), "빌드·실행"(`AGVRUN`, T16-7~T16-10).

| 항목 | 결과 | 날짜 | ZWCAD 버전 | 메모 |
|---|---|---|---|---|
| T16-1 저장된 도면에서 `AGVEXCEL`: `input.xlsx`가 없으면 도면 폴더에 양식을 만들고 Excel로 열림, 있으면 O/U/W를 묻고 Enter(O)는 열기만, U는 `input.bak.*.xlsx` 백업 뒤 입력 유지·새 그룹/standby/load CP만 추가(명령줄에 변경 목록), W는 한 번 더 확인 후 새 양식. Excel에서 열어 둔 채 U/W → 닫으라는 안내. 새 도면 → "도면을 먼저 저장하세요." | | | | |
| T16-1b `AGVEXCEL` 질문의 `[O/U/W]`를 명령줄 목록(클릭)에서 고르면 직접 입력한 것과 같이 동작(“유효하지 않은 입력” 없음). 사내 암호화된 `input.xlsx`(Excel로 저장한 것)에서 U → "Excel로 갱신합니다…" 뒤 변경 목록, Excel에서 열어 보면 입력값 유지·새 행 추가·드롭다운 있음, 파일은 여전히 암호화. Excel에서 그 파일을 열어 둔 채 U → 열린 창에서 바뀌고 저장됨 | | | | |
| T16-2 양식을 채우고 `AGVMODEL`: 검사 결과(오류 시 계속 여부 질문) 뒤 상태 줄 "AGVMODEL 빌드 중…" → 명령줄에 arc 폴더 경로와 "빌드 성공", 상태 줄 "AGVMODEL 완료". `input.xlsx`가 없으면 T16-6 | | | | |
| T16-3 Excel에 잘못된 값(예: 없는 CP 이름, 음수 속도): 명령줄에 Excel 경고·오류가 나열되고 빌드 중단, 상태 줄 "AGVMODEL 오류" | | | | |
| T16-4 AutoMod 빌드 실패 상황(AutoMod는 있으나 모델 오류): 명령줄에 MakeErr/Am2Err 앞 20줄 또는 실행 실패 메시지, 상태 줄 "AGVMODEL 오류" | | | | |
| T16-5 AutoMod가 없는 PC(또는 `AmHome`이 없는 폴더)에서 `AGVMODEL`: "이 PC에는 AutoMod가 없어 모델 파일만 만들었습니다…" → 서버 대기 건수와 "서버로 보낼까요? [Y/N]" → Y면 전송·작업 페이지 열림, 서버가 자기 Jobs:AmHome으로 빌드 | | | | |
| T16-5 상태 줄·팔레트: 빌드 중 "AGVMODEL 빌드 중…", 끝나면 완료/오류. 리본 패널 "검증·내보내기"에도 두 버튼 | | | | |
| T16-6 "내보내기" 버튼이 없고 "모델 생성" 하나: AutoMod가 있는 PC에서 한 번 누르면 `<모델>.arc`와 `<모델>.dir`이 함께 생김. 명령줄에 `경로 +n −n ~n, CP …` 요약과 asy·traffic 경로, 두 번째부터 "백업: …agv.asy.bak.<시각>", 끝에 "빌드 성공: …\<모델>.dir". `input.xlsx`가 없으면 "경로·CP만 내보냅니다" 뒤 예전 `AGVEXPORT`와 같은 결과(상태 줄 "AGVEXPORT 완료"). `AGVEXPORT`를 직접 입력해도 그대로 동작 | | | | |
| T16-7 AutoMod가 있는 PC, "모델 생성" 직후 "빌드·실행"(▷, `AGVRUN`) → 다시 빌드하지 않고 곧바로 AutoMod 시뮬레이션 창("<모델> - AutoMod … Simulation", Status·Messages 창)이 뜸. 실행 중에도 ZWCAD를 계속 쓸 수 있음. 상태 줄 "AutoMod에서 <모델>을(를) 엽니다…" | | | | |
| T16-8 `<모델>.dir`을 지우거나 "모델 생성"을 AutoMod 없이(AmHome을 틀리게) 한 뒤 AmHome을 고치고 "빌드·실행" → "AutoMod 빌드 중…" 뒤 창이 뜸. 빌드 오류면 MakeErr/Am2Err 앞 20줄, 상태 줄 "AGVRUN 빌드 실패", 창 안 뜸 | | | | |
| T16-9 AutoMod가 없는 PC에서 "빌드·실행" → "AutoMod를 찾지 못했습니다: …am264.exe" + "'서버로 보내기'(AGVSEND)로 …" 안내, 아무것도 실행 안 함. `<모델>.arc`가 없으면 "먼저 '모델 생성'" 안내 | | | | |
| T16-10 AutoMod 창을 연 채 "모델 생성" → "모델 생성 중단: …"(`<모델>.dir` 잠김), 이어서 창을 연 채 "빌드·실행" → "AGVRUN 오류: … 실행 중이면 닫고 다시 하세요", 창을 닫고 다시 "빌드·실행" → 빌드 후 실행. 어느 쪽도 ZWCAD가 멈추거나 튕기지 않음 | | | | |

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
| S1 서버 PC에서 zip을 풀고(`appsettings.json`은 그대로) 관리자 PowerShell에서 `ASH.Server.exe --install` → 출력에 "[SC] CreateService 성공", `sc.exe qc ASH.Server`의 `BINARY_PATH_NAME`이 `"…\ASH.Server.exe"`처럼 따옴표로 감싸짐(경로에 공백이 있어도). `sc.exe start ASH.Server` → `RUNNING`, 서버 PC 재부팅 뒤에도 자동 시작 | | | | |
| S2 방화벽 TCP 8082 허용 후 **다른 PC**의 브라우저로 `http://<서버>:8082/` → 상태 페이지("게시된 버전이 없습니다."), `curl http://<서버>:8082/healthz` → `ok`. 허용 전에는 접속 실패, 허용 뒤 성공 | | | | |
| S3 서비스 시작 10초쯤 뒤 상태 페이지 → "마지막 확인 — 가져옴: x.y.z"와 현재 버전·게시 시각·서명 키 ID 표시(키 내장 빌드면 "서명 키 없음" 문구가 없음). GitHub에 더 높은 버전을 릴리스한 뒤 서비스를 재시작하면 그 버전으로 바뀜. (수동 업로드 확인 시) `Admin:Token`을 넣고 `publish-release.ps1` → "200 OK", 토큰이 틀리면 401, 비었거나 16자 미만이면 403 | | | | |
| S4 사용자 PC에서 `ASH-Setup-<현재보다 낮은 버전>.exe /server:http://<서버>:8082` 설치 → ZWCAD 시작 후 3초 안에 "ASH <새 버전> 사용 가능 — AGVUPDATE로 받기"(릴리스를 `-MinSupported`/`ASH_MIN_SUPPORTED` 없이 빌드했다면 기본값이 `0.0.0`이라 "최소 지원 미만" 안내는 **뜨지 않는 것이 정상**. 그 안내를 보려면 PC 버전보다 높은 `-MinSupported <버전>`으로 빌드·게시해 U5와 맞춰 확인) → `AGVUPDATE` → 다운로드 완료·탐색기 열림. 서명 키 내장 빌드에서는 "서명 키 미설정" 경고가 없음. 서버 서비스를 중지(`sc.exe stop ASH.Server`)한 뒤 ZWCAD 시작 → "업데이트 확인 실패(서버 응답 없음)" 한 줄(멈춤 없음) | | | | |
| S5 서버 자동 업데이트: 서버보다 높은 버전을 GitHub에 릴리스한 뒤 서비스 재시작(또는 `CheckIntervalMin` 대기) → 약 10초 뒤 상태 페이지 "서버 프로그램: <새 버전>", "지난 업데이트: 서버 <새 버전>(으)로 자동 업데이트됨". 설치 폴더에 `ASH.Server.exe.old`, `update\apply.log`에 "healthy". `appsettings.json` 값(포트·토큰)과 게시된 버전·이전 버전 목록이 그대로이고 S4 업데이트 확인이 계속 동작. 서버 PC 재부팅 뒤에도 유지 | | | | |

## Runner 자동 업데이트 (분석 PC)

| 항목 | 날짜 | 결과 | 비고 |
|---|---|---|---|
| A1 서버가 GitHub에서 새 릴리스를 가져온 뒤 상태 페이지 "Runner(ash-run): x.y.z" 표시. 서버 `Releases:Dir\runner\files`에 zip이 있음 | | | |
| A2 분석 PC에서 낮은 버전 `ash-run.exe --version` 확인 후 `ash-run --update --server http://<서버>:8082` → "ash-run a -> b", `ash-run --version`이 새 버전, 같은 폴더에 `ash-run.exe.old` | | | |
| A3 다시 `ash-run --update` → "up to date". 서버를 끄고 실행 → "update skipped: server ... does not answer", 작업은 계속됨(3초 안) | | | |
| A4 작업 실행(`ash-run <프로젝트> --model m`)이 업데이트 뒤 새 버전으로 이어져 끝까지 돌고 종료 코드가 같음. `--no-update`면 확인하지 않음 | | | |
| A5 `ash-run`을 두 창에서 동시에 시작 → 한쪽만 받고 다른 쪽은 기다렸다가 새 파일로 실행. `ash-run.exe.old`는 하나 | | | |
| A6 `ash-run.exe.old`를 `ash-run.exe`로 되돌려 롤백 가능(ZWCAD 없이) | | | |

## 명령 정리 (0.7.0)

| 항목 | 날짜 | 결과 | 비고 |
|---|---|---|---|
| C1 새 도면(mm)에서 아무 AGV 명령 실행 → `ash.settings.tsv`가 `Scale 0.001`로 생김, 명령줄에 "Scale 0.001: 도면 단위 기준". 단위가 m인 도면은 `Scale 1` | | | |
| C2 `AGVPATH` 종류 선택 후 선을 연속으로 그리고 Enter로 종료 → 폴리선 하나가 `P00x`로 태그됨. **다시 Enter** → "경로 종류 [...]" 질문부터 다시 시작(그냥 PLINE이 아님) | | | |
| C3 C2의 폴리선(꺾인 선)에서 `AGVFILLET` → 경로 선택 없이 시작하면 "필렛할 경로 선택(모서리 가까이를 클릭)", 모서리 가까이를 클릭 → 반경 → 그 모서리만 둥글어짐. 경로를 먼저 선택하고 `AGVFILLET` → 반경만 묻고 모든 모서리가 둥글어짐, 짧아서 못 한 모서리는 좌표가 안내됨 | | | |
| C4 이어진 별도 경로 두 개를 선택(미리 선택)하고 `AGVFILLET`/`AGVJOIN` → 순서 질문 없이 실행 | | | |
| C5 `AGVOFFSET` → 기준점 → 변위(점을 찍거나 숫자 입력) → 방향 뒤집기 질문, 총 세 번. 경로를 먼저 선택해 두면 경로 선택 질문도 없음. 찍은 점 쪽(숫자 입력이면 기준점 쪽)에 새 경로가 생김 | | | |
| C6 경로를 선택한 채 `AGVREVERSE`, `AGVPTYPE`(종류만 질문), `AGVSPLIT`(선택한 경로를 바로 교차 분할), `AGVARRAY` → 선택 질문 없이 실행. 리본·팔레트 버튼으로 실행해도 같음 | | | |
| C7 CP 탭에 "traffic CP 자동 생성" 버튼이 없음. 리본 CP 패널에는 "CP 배치"(마지막으로 고른 TYPE)만 있음. 교통제어 탭의 "교통제어 자동 생성"은 그대로 | | | |
| C8 CP 탭에 TYPE별 버튼 8개(2열, 아이콘이 TYPE 색), 마우스를 올리면 이름 규칙(T###, L### ...) 툴팁. `load` 버튼 → 접두어 L, CAP이 INF로 바뀌고 안내 글줄이 초록색 "load · 이름 L### · CAP INF · 레이어 AGV_CP_LOAD". `traffic` 버튼 → T, 1. 같은 TYPE 버튼을 다시 누르면 고친 접두어·CAP 유지 | | | |
| C9 `load` 버튼으로 배치 → 명령줄 "CP L001 (load, CAP INF) 삽입", 레이어 `AGV_CP_LOAD`, 초록색. `AGVINIT` 뒤 레이어 패널에 `AGV_CP_*` 7개 | | | |
| C10 내보내기 → `agv.asy`에 무제한 CP가 `..._capinf` CP 종류로 나오고 `cap 2147483647`. traffic CP는 기존대로 cap 1 | | | |
| C11 "Settings 열기"는 이제 "검증·내보내기" 탭의 "설정" 구역에 있음(교통제어 탭에는 없음) | | | |
| C12 `AGVARROW` → 현재 길이를 보여 주고 값을 입력하면 설정 파일에 저장되고 방향표가 바로 바뀜. Enter만 누르면 그대로 다시 그림. 경로 탭의 "방향표 크기·갱신" 버튼, 리본 "경로" 패널의 "방향표 크기" | | | |

## 서울 시간 · STATION 삭제 · 필렛 반경 (0.7.1)

| 항목 | 날짜 | 결과 | 비고 |
|---|---|---|---|
| D1 서버 상태 페이지: "게시 시각(서울)"이 UTC+9로 보임(예: `...T00:57:15Z` → `09:57:15`), "마지막 확인(서울)", "서버 시각(서울)"과 PC 표준 시간대가 보임. PC 시간대를 바꿔도 서울 시각은 그대로 | | | |
| D2 서버 PC의 "서버 시각(서울)"이 실제 시계와 같음. 다르면 PC의 날짜 및 시간(또는 시계 자체)이 틀린 것 | | | |
| D3 `%APPDATA%\ASH\plugin.log`, 설치 로그, `agv.asy.bak.<시각>`, `runs.<시각>`의 시각이 서울 시간 | | | |
| D4 CP 탭에 STATION 칸이 없음. 상세 모드 질문이 NAME, TYPE, GROUP, CAP. 새 CP 블록에 STATION 속성이 없음 | | | |
| D5 `AGVFILLET` 처음 실행 → 반경 기본값 1200(mm 도면). 다른 값(예: 800)을 입력해 실행 → 다음 실행의 기본값이 800. ZWCAD를 다시 시작해도 800. `ash.user.tsv`에 `FilletRadiusM 0.8` | | | |
| D6 도면 단위가 m인 도면에서 `AGVFILLET` → 기본값이 같은 길이(m 단위로 0.8)로 보임 | | | |

## 경로 분할 · 검증 표 · 객체 찾기 · Excel 양식 (0.8.0)

| 항목 | 날짜 | 결과 | 비고 |
|---|---|---|---|
| E1 `AGVPATH`로 선분을 여러 개 이어 그리고 Enter → 직선 하나마다 `P00x` 경로가 따로 생김(합쳐진 폴리선 아님). 이전 도면의 폴리선 경로는 그대로이고 `AGVFILLET`도 동작 | | | |
| E2 `AGVFILLET`, `AGVARROW` 입력줄이 "반경 <1200>"처럼 기본값을 한 번만 보여 줌. 완료 문구는 "작업 완료 (...)" | | | |
| E3 "검증·내보내기" 탭 → 검증 실행 → 문제 있는 항목만 표(문제, 대상, 내용)로 나옴. 문제가 없으면 표가 비어 있음. 행을 더블클릭하면 그 위치로 화면 이동(배율 그대로) | | | |
| E4 "찾기" 탭 → 범주(경로 전체, 경로 · 단방향/양방향/지선/크랩, CP 전체, CP · TYPE별, block)를 바꾸면 목록이 걸러지고 이름·정보 입력으로 추가로 거를 수 있음 | | | |
| E5 찾기 목록 행을 더블클릭 → 객체가 선택되고 화면이 그 객체 중심으로 pan. 줌 배율은 변하지 않음. "목록 새로고침"으로 도면 변경 반영 | | | |
| E6 "Excel 양식" 버튼 → `TypeInitializer`/`TableLoader` 예외 없이 양식이 저장됨. 열어서 읽기(`AGVMODEL`)도 됨. 실패하면 `plugin.log`의 "assembly ..." 줄 확인 | | | |
| E7 Setup: 설치 마지막 화면에 "설치 후 ZWCAD 실행" 체크박스. 체크 후 설치 → ZWCAD가 (관리자 권한 없이) 실행됨. 해제하면 실행 안 함 | | | |

## Excel 읽기 오류 진단 (0.8.2)

| 항목 | 날짜 | 결과 | 비고 |
|---|---|---|---|
| F1 `input.xlsx`의 숫자 칸에 글자를 넣고 `AGVMODEL` → "Excel 읽기 오류: Materials 시트 3행 'TaktTime' 열: ..."처럼 시트·행·열이 나옴 | | | |
| F2 `input.xlsx`를 0바이트 파일로 바꾸고 `AGVMODEL` → 원인(예외 종류와 메시지)과 "파일이 비어 있습니다" 안내가 나옴 | | | |
| F3 `input.xlsx`를 메모장으로 열어 아무 글이나 저장 → "텍스트(CSV·HTML 등)로 저장된 파일" 안내 | | | |
| F4 Excel로 연 채 `AGVMODEL` → "다른 프로그램이 사용 중" 안내(원인 메시지 포함). 같은 내용이 `%APPDATA%\ASH\plugin.log`에도 남음 | | | |
| F5 `AGVEXCEL`로 새 양식을 만든 뒤 폴더에 `input.xlsx.tmp`가 남지 않음 | | | |

## 암호화(NASCA)된 input.xlsx (0.8.3)

| 항목 | 날짜 | 결과 | 비고 |
|---|---|---|---|
| G1 Excel로 `input.xlsx`를 저장해 NASCA 암호화가 걸린 상태에서 `AGVMODEL` → "xlsx를 직접 읽지 못해 ... Excel로 읽습니다…" 뒤에 모델 생성이 진행됨 | | | |
| G2 Excel에서 `input.xlsx`를 연 채(저장 안 한 수정 포함) `AGVMODEL` → 열려 있는 통합 문서를 읽고 "저장하지 않은 변경까지 포함" 안내 | | | |
| G3 `AGVMODEL` 뒤 프로젝트 폴더에 `input.cells.tsv`가 생김(텍스트, 메모장으로 열림) | | | |
| G4 `ash-run`을 암호화된 `input.xlsx`로 실행 → 경고 "마지막 모델 생성 때의 셀 스냅샷을 씁니다"와 함께 실행됨 | | | |
| G5 Excel이 없는 PC에서 암호화된 파일로 `AGVMODEL` → "Excel이 설치되어 있지 않습니다" 오류와 파일 점검 결과 | | | |
| G6 `AGVMODEL`이 끝난 뒤 작업 관리자에 숨은 EXCEL.EXE가 새로 남지 않음(이 PC처럼 Excel이 종료 뒤 다시 뜨는 환경은 제외) | | | |

## 가속·감속, 자재 형태, AGV 형상, AutoMod 경로 (0.9.0)

| 항목 | 날짜 | 결과 | 비고 |
|---|---|---|---|
| H1 새 `input.xlsx`의 AGV_Types에 Accel/Decel 0.3, Kind Standard가 있고 Materials에 Form Box가 있음 | | | |
| H2 `AGVMODEL` 후 `agv.asy`의 `accel`/`decel` 줄(`load Default`)이 `0.3 Meters Seconds Seconds`. AM에서 차량이 0.3 m/s²로 출발·정지 | | | |
| H3 Kind를 Ant로 두고 자재 Form을 Box로 → 경고 "싣지 못합니다". Trolley면 경고 없음 | | | |
| H4 Form에 `Crate` 입력 → "Materials 시트 ... 알 수 없습니다 (Box / Pallet / Trolley)" 오류 | | | |
| H5 ash.settings.tsv의 AmHome을 없는 폴더로 두고 `AGVMODEL` → 오류 없이 "이 PC에는 AutoMod가 없어 모델 파일만 만들었습니다", `<모델>.arc`는 만들어지고 서버 전송 여부를 물음. 보낸 패키지에 ash.settings.tsv 없음 | | | |
| H6 AmHome이 맞을 때 빌드가 끝나면 `<모델>.dir` 폴더가 AutoMod에 의해 생김 | | | |
| H7 모델 생성 중 예상 밖 오류가 나면 명령줄에 `오류: <예외 종류>: <메시지>`가 나오고 plugin.log에 전체 내용이 남음 | | | |

## 차량·적재물 그림 (0.10.0)

| 항목 | 날짜 | 결과 | 비고 |
|---|---|---|---|
| I1 `AGVMODEL` 후 AutoMod에서 모델을 열면 차량이 Kind별 모양(Standard 범퍼, Conveyor 롤러 데크, Ant 리프트판, Magnet 뒤쪽 전자석)으로 보임 | | | |
| I2 차량이 진행하는 방향(앞)이 그림의 앞(Standard의 범퍼 쪽, Magnet은 반대쪽이 뒤)과 같음. 반대로 보이면 알려 주세요 | | | |
| I3 AGV_Types의 Length를 1.2에서 2.4로 바꾸고 다시 `AGVMODEL` → 차량이 진행 방향으로 길어지고 롤러 수만 늘며 일그러지지 않음. Width도 같음 | | | |
| I4 Materials의 Form을 Box / Pallet / Trolley로 바꿔 보면 AGV 위 적재물 그림이 상자 / 파레트 / 대차로 달라짐 | | | |
| I5 LoadLength를 0.9로 바꾸면 그 자재의 적재물만 길어지고 모양이 일그러지지 않음 | | | |
| I6 Form Box, EmptyReturn Y, Routes에 ReturnFromCP / ReturnToCP가 있는 자재: 공급 때 꽉 찬 상자, 돌아올 때 윗면이 열린 빈 상자 | | | |
| I7 적재물이 AGV 위에 놓이는 높이가 어색하지 않음(너무 뜨거나 파묻히지 않음). 어색하면 높이를 알려 주세요 | | | |

## 대칭 차량, 삼색등, 높이 (0.10.1)

| 항목 | 날짜 | 결과 | 비고 |
|---|---|---|---|
| J1 차량이 앞뒤 대칭이고 양쪽 끝면에 빨강·노랑·초록 삼색등이 보임. 후진해도 모양이 같음 | | | |
| J2 Standard와 Conveyor의 윗면 높이가 600 mm(0.6 m)임 | | | |
| J3 Ant는 차체 150 mm이고 바퀴가 하판 아래에 있으며, 대차가 상판 위에 올라탐(캐스터가 상판에 닿음) | | | |
| J4 Magnet 본체는 600 mm, 대차는 뒤쪽 전자석 바로 뒤에서 캐스터가 바닥에 닿음. 후진하면 대차가 앞에서 밀림 | | | |
| J5 Standard, Conveyor 위의 상자, 파레트가 600 mm 윗면에 놓임. 적재물의 긴 쪽이 진행 방향과 나란함 | | | |

## 서버로 보내기 (0.11.0)

| 항목 | 날짜 | 결과 | 비고 |
|---|---|---|---|
| K1 `AGVMODEL`을 아직 안 한 도면에서 `AGVSEND` → "보낼 파일이 없습니다: ..."와 함께 먼저 모델 생성(AGVMODEL)을 하라는 안내가 나오고 멈춤 | | | |
| K2 서버 주소가 비어 있을 때 `AGVSEND` → AGVSERVER로 설정하라는 안내와 설정 파일 경로가 나오고 멈춤 | | | |
| K3 서버 주소가 맞으면 "서버 대기 N건"(예상 시작이 있으면 서울 시간으로, 보통 걸리는 분)이 나오고 "서버로 보낼까요? [Y/N] <Y>"를 물음. N이면 아무것도 보내지 않음 | | | |
| K4 서버가 작업을 실행하지 않도록 꺼져 있으면 그 사실을 알리고 묻지 않고 멈춤 | | | |
| K5 Y → 전송 완료 메시지에 작업 번호가 나오고 브라우저가 그 작업 페이지로 열림. 임시 zip이 `%TEMP%`에 남지 않음 | | | |
| K6 서버 쪽에서 거절(예: 잘못된 모델 이름)하면 "서버가 받지 않았습니다(HTTP 상태): 이유"가 나옴 | | | |
| K7 서버를 끄고 `AGVSEND` → "서버에 연결하지 못했습니다: ..."가 나오고 ZWCAD가 멈추지 않음. plugin.log에 기록됨 | | | |
| K8 팔레트 검증·내보내기 탭의 "서버로 보내기" 버튼과 리본 검증·내보내기 패널의 같은 버튼이 `AGVSEND`를 실행함 | | | |

## 서버 작업 대기열과 결과 화면 (0.11.0)

| 항목 | 날짜 | 결과 | 비고 |
|---|---|---|---|
| L1 서버 PC appsettings.json의 Jobs:AmHome이 AutoMod 설치 폴더를 가리킴(체험판이면 Jobs:DialogButton "Student Version") | | | |
| L2 `AGVSEND` 후 `http://서버:8082/jobs`에 작업이 "대기" → "실행 중(빌드 / 대수 탐색)" → "완료"로 바뀌고 실행 수가 늘어남 | | | |
| L3 결론 탭: 권장 총 대수, 시험 범위, 그룹별 권장 대수·가동률 막대, block 점유율·대기 지도 2개 | | | |
| L4 상세 탭: 그룹별 대수-가동률 곡선(권장 대수 초록선, 상한 빨간 점선), 영향표, 실행 목록 | | | |
| L5 입력·추적 탭에 올린 파일과 SHA-256, 다운로드 탭에서 summary.tsv / runs.zip / job.log 받기 | | | |
| L6 두 작업을 연달아 보내면 두 번째는 "앞에 1건", 첫 작업이 끝나면 자동 시작 | | | |
| L7 다른 PC에서 취소 버튼 → 거절(403). 보낸 PC에서 취소 → 대기 중이면 바로 "취소됨" | | | |
| L8 실행 중 서버 서비스를 재시작하면 그 작업이 "실패: 서버가 다시 시작되어 중단" | | | |
| L9 AutoMod가 없는 서버로 보내면 "서버에 AutoMod가 없습니다: ...am264.exe" 실패 | | | |

## 결과 올리기, Excel 보고서, 보관 기간 (0.12.0)

| 항목 | 날짜 | 결과 | 비고 |
|---|---|---|---|
| M1 ash-run 0.12.0으로 대수 탐색 후 프로젝트 폴더 `runs\search.tsv`가 생김 | | | |
| M2 `AGVRESULTUP`("결과 올리기") → Y → 브라우저에 "내 PC에서 실행한 결과" 작업이 바로 "완료"로 보이고 권장 대수·block 지도가 나옴. 서버 대기열은 늘지 않음 | | | |
| M3 ash-run을 안 돌린 폴더에서 `AGVRESULTUP` → "올릴 결과가 없습니다: runs/summary.tsv, runs/search.tsv" | | | |
| M4 결과 화면의 "Excel 보고서 받기" → report.xlsx를 Excel로 열면 시트 6개(요약, 대수별 가동률, 실행 목록, 영향표, block, 입력), 가동률이 % 숫자 | | | |
| M5 Jobs:KeepRunsDays를 1로 두고 하루 뒤 → runs.zip 링크가 "정리되었습니다"로 바뀌고 결론 탭과 Excel 보고서는 그대로 | | | |
| M6 큰 모델(.arc 수백 MB)로 `AGVSEND` → ZWCAD 메모리가 크게 늘지 않고 전송 완료 | | | |

## 공급 로직 재구성 (0.13.0)

| 항목 | 날짜 | 결과 | 비고 |
|---|---|---|---|
| N1 AGV_Groups에 FinishCP 열이 있고 비워 두어도 모델이 만들어짐. CP 타입 목록에 finish(접두어 F)가 있음 | | | |
| N2 AutoMod에서 차량이 하차 후 항상 대기 CP로 돌아왔다가 다음 작업으로 출발함 | | | |
| N3 호출이 몰리면 대기 CP에 용기가 여러 개 쌓이고, 돌아온 차량이 먼저 온 것부터 싣고 나감 | | | |
| N4 result.tsv에 vehicle 행(가동 시간, 주행 거리, 작업 수)과 matstat 행(리드타임, 재고 통계)이 있음 | | | |
| N5 대기 CP 앞 레인을 짧게 그리고 AGVMODEL → "줄 설 공간이 … m뿐입니다" 경고 | | | |
| N6 차량 제자리 회전이 90°에 약 7초 | | | |

## 고장·공차 속도·용량 (0.14.0)

| 항목 | 날짜 | 결과 | 비고 |
|---|---|---|---|
| Q1 AGV_Types에 MTBF 3600, MTTR 300을 넣고 모델 생성 → "v2 미반영" 경고가 없음 | | | |
| Q2 AutoMod에서 차량이 가끔 CP에 멈춰 서 있다가(수리) 다시 출발함 | | | |
| Q3 SpeedEmpty를 적재 속도보다 크게 → 빈 차가 더 빨리 달림 | | | |
| Q4 Capacity 2 → 한 대가 용기 2개를 한 번에 공급(재고가 2씩 늘어남) | | | |
| Q5 앞 차 뒤에서 멈출 때 간격이 약 0.5 m, 감속이 일찍 시작됨 | | | |
| Q6 서버 결과 결론 탭에 "차량" 표와 "자재별 리드타임·재고" 표, 상세 탭에 차량별 표(고장 시간 열) | | | |
| Q7 report.xlsx에 "차량", "자재 통계" 시트 | | | |

## 팔레트 TYPE별 CP·포커스·블록 채번·내보내기

| 항목 | 날짜 | 결과 | 비고 |
|---|---|---|---|
| V1 팔레트 버튼(예: `경로 그리기`, CP `load`) 클릭 후 마우스를 팔레트 위에 둔 채 Enter → 실행 중인 명령이 입력을 받음(기본값·종료). "실행 중인 명령을 먼저 끝내세요."가 나오지 않음. 클릭 직후 커서가 도면 쪽으로 포커스 이동 | | | |
| V2 CP 명령 대기 중 다른 TYPE 버튼 클릭 → "실행 중인 명령을 먼저 끝내세요.", TYPE·접두어·안내 글줄이 바뀌지 않음 | | | |
| V3 교통제어 탭 "블록 채번"(# 아이콘) → `AGVBLOCKNUM` 실행 | | | |
| V4 `AGVEXPORT` 완료 → 명령줄에 asy·traffic 경로, `.asy`가 연결 프로그램으로 열리지 않음 | | | |
