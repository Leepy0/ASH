# 분석 서버 관문 확인 (설계서 11장)

이 PC의 AutoMod 12.6(내부 14.5)은 **체험판**이다. 라이선스 관련 결과는 USB 키(Sentinel HASP HL)가 꽂힌 분석 서버에서 다시 확인해야 한다.

## 절차 (서버에서)

1. `C:\AutoMod\12_6\demos\amoddemos\demos\agv\agvdemo.arc`를 `%TEMP%\gate\agvdemo.arc`로 복사한다.
2. `cd %TEMP%\gate` 후 `"C:\AutoMod\12_6\bin\am264.exe" -bq agvdemo`를 실행한다. 뜨는 대화상자 제목과 내용을 기록한다. `agvdemo.dir\agvdemo64.dll`과 `agvdemo.exe`가 생겼는지 확인한다.
3. `agvdemo.exe -w`를 실행한다. Authorization 대화상자가 뜨는지, 뜨지 않고 `agvdemo.report`가 채워지는지 기록한다. (관문 1)
4. 3을 두 창에서 동시에 실행해 두 번째가 라이선스 오류 없이 돌아가는지 기록한다. (관문 2)
5. 2에서 "Entity Limit Status" 대화상자의 숫자(현재/최대)와 문구를 그대로 기록한다. (관문 3)
6. AM 편집기에서 Process 시스템에 block 하나(`Btest`, cap 1)를 만들어 저장한 뒤 `<model>~.asy`에서 `Btest`가 들어간 줄을 복사해 둔다. (관문 4)

## 결과

| 관문 | 날짜 | 결과 | 원문 / 근거 | 영향 |
|---|---|---|---|---|
| 1 창 없는 실행(`-w`) 인증 | 2026-10-20 | **체험판에서 통과** | `agvdemo.exe -w` 실행 시 "Authorization" 창(버튼: Cancel / Help / Student Version / License Server / Password Version, 라벨 Runtime)이 뜸. 자식 버튼 "Student Version"에 `BM_CLICK`을 보내면 창 없이 8시간 시뮬이 CPU 2.6초에 완료, `agvdemo.report`(18 KB)·`.message`("Simulation complete") 생성. 설치 매뉴얼: 편집기와 런타임 인증 코드가 따로이며 Runtime 코드를 한 번 입력하면 이후 묻지 않음 | Runner는 완전 자동 모드 가능. 대화상자 처리기는 버튼 이름을 설정값으로(체험판 `Student Version`, 서버는 `License Server` 또는 미출현). 서버에서는 Runtime 인증 코드를 1회 입력해 두면 창이 안 뜸(확인 필요) |
| 2 동시 실행 | 2026-10-20 | **가능** (사용자 확인) | — | Runner 동시 실행 수 설정값 > 1 허용 |
| 3 엔티티 제한 200 | 2026-10-20 | **체험판 제약** (사용자 확인) | 이 PC는 체험판. 서버의 정식 라이선스에는 해당 없음 | 모델 분할 불필요 |
| 4 Process block 문법 | 2026-10-20 | **데모에서 추출** | `BLOCK name B_intersection 4 cap 1` 다음 줄에 `dis N picpos begx … endx … scx … scy … scz …`(표시 N개) + `UserDef template Feet` 그래픽 블록. 출처 `demos\amoddemos\demos\Vehicles\vehicles.arc\vehicles~.asy:342`, `gswa\examp14_1\…\examp141~.asy:126` | 모듈 C가 `BLOCK name <NAME> <표시수> cap <CAP>` + `dis` 줄로 생성 |
| 5 원호 angle 부호 | 2026-10-20 | **해결** | 데모 path16 끝점 계산: 음수 = 시계 방향 | Core 테스트로 고정됨 |

## 관문 4 보충: 차량이 block을 claim하는 방법

데모 43개의 `.asy`와 `code.m`에는 `claim <block>` 줄도, `CPOINT … claim` 속성도 없다(데모는 block을 정의만 하고 쓰지 않거나 로드 로직에서 쓴다). 차량(Path Mover)이 CP에서 block을 claim/release하는 정규 방법은 AutoMod 매뉴얼의 Path Mover 장(control point의 block 연결)을 따른다. 매뉴얼 확인 결과는 아래에 추가한다.

매뉴얼(`automod manual.pdf`, "Claiming blocks") 확인 결과 (2026-10-20):

> Typically, blocks are used as a collision control feature between path mover vehicles … To use a block for collision control, place the block graphically. Loads or vehicles automatically claim and release the block when they move in or out of the block's boundaries.

- **claim도 release도 자동이다.** 사각형을 그래픽으로 놓기만 하면 차량이 경계에 들어갈 때 claim, 나갈 때 release한다. CP 속성이나 로직의 `claim`/`release`는 필요 없다.
- 로직의 `claim <block>` / `release <block>`는 Process의 load가 쓰는 것이고, vehicle procedure의 function 안에서는 block을 claim할 수 없다(매뉴얼 "Note When you are using the claim action in a function, you cannot claim blocks").
- 차량은 경로상의 block을 미리 보고 claim이 안 되면 감속·정지한다(매뉴얼 9690, 9737행: "a vehicle might not be able to claim a control point or block in its route").
- **설계에 미치는 영향(2차 계획서에서 결정):** 진입 traffic CP는 claim에 필요하지 않다. 남는 역할은 (a) 대기 위치를 명시적으로 고정하는 것, (b) `traffic.tsv`로 통계·보고서에서 block과 경로를 짝짓는 것. CP 없이 block만 두는 "CP 최소" 모드를 기본으로 할지 사용자가 정한다. 현재 Core/Plugin은 CP를 만들지만 AM 동작과 충돌하지는 않는다.
- block의 CP 연결 속성은 AM에 없다. 설계서 6.1의 "1순위: CP 속성 claim 목록"은 폐기한다.


## 관문 4 후속 결정 (2026-10-20, 사용자)

block은 사각형뿐이라 비직각 교차에서는 경계 기준 대기 위치가 거칠다. Path Mover 설정에서 block claim 트리거를 "경계 진입"이 아니라 "CP 목적지로 이동 시 경로상의 block 선점"으로 바꿀 수 있고, 이것을 기본으로 한다. 진입 traffic CP는 그대로 유지한다(대기 위치 = claim 시점). 해당 설정의 `.asy` 키워드는 2차 계획(모듈 C)에서 AM 편집기 저장본으로 추출한다.

## 관문 4 추가: "Claim Block" 설정의 `.asy` 위치 (2026-10-20)

User's Guide(`amvol1and2.pdf`, Path Mover Systems, p.526) 확인. Path Mover 시스템 기본값 "Claim Block"에 두 옵션이 있다.
- **When claiming next control point** — 다음 CP를 claim할 때 그 CP까지 경로상의 block을 **모두 먼저** claim한다. 하나라도 못 잡으면 아무것도 잡지 않고 현재 CP에서 대기. AutoMod 8.7 이하 기본값.
- **When approaching block** — block 경계 앞 감속 지점에서 claim. 9.0 이상 기본값.
"Release Block": On vehicle center(8.7 기본) / On vehicle trailing edge(9.0 기본). "Can't Claim Next Point/Block": Come to complete stop / Resume speed as soon as claimed(기본).

이 설정은 AGVS `.asy`의 **`AGVSDEF` 줄 끝 토큰**에 저장된다(데모 43개 비교):
- `ALTERNATE NONE ignore OldBlockClaim OldBlockRelease` — OldBlockClaim = "When claiming next control point", OldBlockRelease = "On vehicle center" (추정: 8.7 기본값을 Old로 표기)
- `ALTERNATE NONE ignore ResumeSpeedWhenClaimed` — 토큰 없음 = approaching block / trailing edge; ResumeSpeedWhenClaimed = 재가속 옵션; `ignore`는 Runtime Warning Messages=Off로 추정.

**사용자가 원하는 "CP 목적지 이동 시 경로상 block 선점" = `OldBlockClaim`.** 모듈 C는 `AGVSDEF … ALTERNATE NONE ignore OldBlockClaim ResumeSpeedWhenClaimed`로 생성한다(OldBlockRelease는 trailing edge가 더 안전하므로 넣지 않음). 토큰 의미는 추정이므로 2차 시작 때 AM 편집기에서 옵션을 바꿔 저장한 파일과 대조해 확정한다.

## 관문 B-G1 곡선 속도 (2026-10-23)

질문: 차량 타입 줄 `crvvel`/`rcrvvel`(`agvs_veh.txt` 11행)이 호(arc) 구간에서 실제로 속도를 제한하는가.

- 바꾼 줄: `%TEMP%\gateB\slow\agvdemo.arc\agv.asy`의 `load Default` 블록 `crvvel 0 3 Feet Seconds`, `rcrvvel 0 3 Feet Seconds` → `0 0.3`. `base`는 원본. 둘 다 snap 8 h.
- 실행: 각 폴더에서 `am264.exe -bq agvdemo`("Entity Limit Status" OK 클릭) → `agvdemo.exe -w`(이번에는 Authorization 창이 뜨지 않음). `.message` 둘 다 "Simulation complete".

| 항목 | base (crvvel 3) | slow (crvvel 0.3) |
|---|---|---|
| path15 호(4.71 ft) 평균 시간 | 1.95 s | 15.69 s |
| path20 호(11.0 ft) 평균 시간 | 4.02 s | 36.95 s |
| path12 직선 평균 시간 | 18.56 s | 20.04 s |
| Tthroughput 평균(/h) | 127.75 | 72.38 |
| 배송 횟수 / 평균 시간 | 1025 / 31.39 s | 581 / 50.50 s |
| Delivering 비율 | 0.223 | 0.204 |

판정: **통과.** 호 통과 시간이 호 길이 ÷ 0.3 ft/s와 맞고 직선은 그대로다. Task 9A(차량 타입의 곡선 속도를 `agvs_veh.txt`에 반영) 실행.
