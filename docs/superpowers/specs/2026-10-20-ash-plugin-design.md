# ASH (AGV Simul Helper): ZWCAD 기반 AutoMod AGV 수량 산출 플러그인 설계

작성일: 2026-10-20
상태: 초안 (사용자 승인 전)

## 1. 의도와 배경

AutoMod(이하 AM)로 공장 내 AGV 적정 대수를 산출하는 업무의 6단계(도면 준비 → 동선 작성 → 교통제어 → 로직 → 검증 → 보고서/영상) 중, 사람이 반복적으로 손으로 하는 부분을 ZWCAD 플러그인과 외부 실행기로 자동화한다.

현재 가장 큰 시간 소모는 1~3단계다.

- 공장 CAD 도면은 크고 복잡하며(3D, 이미지 포함) AM은 약 20MB 이상의 DXF를 열면 응답이 없다.
- AM 편집기에는 array, copy, trim, split 같은 CAD 명령이 없어 동선 작성이 매우 느리다. 시뮬레이션 중 동선 변경 통보가 오면 거의 처음부터 다시 그린다.
- 교통제어는 CP(제어점)와 cap 1인 block을 조합해 FIFO를 만드는데, 규모가 커질수록 CP가 폭증한다.

4단계 로직은 공용 로직을 재사용하지만, 프로젝트마다 특수 구현이 섞여 들어가고 끝난 뒤 공용판을 다시 정리하는 작업이 번거로워 사실상 방치된다. 5단계 검증은 편집기에서 AGV 대수를 손으로 바꿔 Run하고 결과를 기록해 비교한다.

AutoStat은 추가 라이선스 비용 때문에 사용하지 않는다.

## 2. 목표, 비목표, 금지 목표

### 목표

1. 공장 CAD 도면을 AM이 열 수 있는 크기와 형태로 자동 경량화한다.
2. ZWCAD 안에서 CAD 명령으로 동선(path), CP, 교통제어 block을 빠르게 그리고, 검증한 뒤 AM `.asy`로 내보낸다. 내보내기 후 수정된 `.asy`를 바로 열어 준다.
3. Excel 양식 하나에 자재·AGV·경로 정보를 입력하면, 공용 로직이 포함된 Process 시스템 `.asy`와 런타임 파라미터 파일을 생성한다. 프로젝트별 특수 구현은 별도 파일에 격리되어 공용 로직을 오염시키지 않는다.
4. 빌드된 모델을 AGV 대수·시드를 바꿔 자동 반복 실행하고, 다음 두 조건을 만족하는 그룹별 최소 AGV 대수를 찾는다.
   - 모든 자재의 재고가 사용자가 지정한 최소 안전재고 이하로 떨어지지 않는다.
   - AGV 가동률이 사용자가 지정한 상한(기본 70~80%)을 넘지 않으며, 대수가 불필요하게 남지 않는다.
5. 결과를 PPT 보고서로 자동 생성한다. 그룹별 적정 대수, 대수별 KPI 곡선, 레이아웃 위에 색으로 표시한 병목 구간, 리스크 목록을 담는다. 영상에는 배속 캡션을 넣는다.

### 비목표 (v1에서 하지 않음)

- AS/RS, Power & Free, 컨베이어, 크레인 등 AGV 이외의 이동 시스템.
- `.asy`를 다시 DWG로 되돌리는 양방향 동기화. (AM에서 고친 내용은 ZWCAD로 돌아오지 않는다. 진실의 원본은 DWG + Excel이다.)
- AutoStat 연동.
- 새 시뮬레이션 엔진 개발. AM 엔진을 그대로 쓴다.
- AM ActiveX(`AMRunX`) 연동. 이 PC에 등록되어 있지 않고, 모델 구조 변경도 불가능하다.

### 금지 목표 (이렇게 되면 실패)

- 플러그인이 생성한 공용 로직을 사용자가 프로젝트마다 손으로 고쳐야 하는 상황. 특수 구현은 반드시 확장 파일로만 들어간다.
- 레이아웃을 다시 내보냈을 때 Process 로직이 참조하는 CP·block 이름이 바뀌어 모델이 깨지는 상황. 이름은 사용자가 블록 속성으로 지정하며 자동 번호는 안정적이어야 한다.
- 결과 숫자가 어느 실행·시드에서 나왔는지 추적할 수 없는 보고서.
- AM 라이선스 약관에 어긋나는 방식(인증 우회 등)의 실행 자동화.

## 3. 확인된 제약과 사실

이번 설계 전에 실제 설치본(`C:\AutoMod\12_6`, 내부 버전 14.5.0.4 x64)에서 확인한 사실이다.

| 항목 | 확인 내용 | 영향 |
|---|---|---|
| 모델 형식 | `.arc`는 폴더. `model.amo`(시스템 목록·실행제어) + 시스템별 `.asy`(텍스트). Process `.asy` 안에 로직 소스가 `SFileBegin name code.m` 블록으로 포함됨 | 플러그인은 텍스트 생성만 하면 된다 |
| 명령줄 빌드 | `am264.exe -bq <모델>`로 가져오기+빌드+종료가 동작함(데모 모델로 확인). 결과는 `<모델>.dir\` 안의 C 소스·obj·`<모델>64.dll`과 모델 폴더의 `<모델>.exe`(`runModel64.exe` 복사본) | 빌드 자동화 가능 |
| 빌드 중 대화상자 | "Entity Limit Status"(엔티티 124/200, 50% 초과 경고)가 떠서 OK를 눌러야 진행됨 | 실행기가 이 창을 자동으로 닫아야 한다. 또한 이 설치본의 라이선스가 엔티티 200개 제한일 가능성이 있다 |
| 창 없는 실행 | `<모델>.exe -w` 실행 시 "Authorization" 대화상자(Student Version / License Server / Password Version)가 뜨고 멈춤. 사용자 선택이 필요해 테스트에서는 취소함 | 배치 실행의 관문. 7장 참조 |
| 라이선스 방식 | Sentinel HASP(`hasp.ini`, 라이선스 서버 또는 로컬 키) | 인증 자동화는 정규 설정(라이선스 서버 지정 등)으로만 한다 |
| 차량 수 변경 | `.asy`의 `AGVSVEH type X numveh N`. 런타임 수정 파일 `<모델>.dir\<모델>.mff`로도 "Number of Vehicles"를 덮어쓸 수 있으나 줄 문법 미확정. 공식 문서와 데모(`agvdemo.arc`)는 "최대 대수 정의 후 초과 차량을 큐에 영구 주차" 패턴을 사용 | 주차 패턴을 기본으로 채택 |
| 모델 출력 | 실행 폴더에 `<모델>.report`, `<모델>.print`, `<모델>.message` 생성. 로직에서 `open … for writing`으로 임의 파일 출력 가능 | 결과는 로직이 탭 구분 파일로 직접 쓴다 |
| Excel 함수 | `XLSet`/`XLGet` 동작 확인. 단, `XLClose`와 모델 종료 시 Excel 프로세스 전체가 종료됨 | 런타임 파일 I/O는 Excel 함수 대신 텍스트 파일을 사용한다 |
| 영상 | 런타임에 GIF 캡처 기능 문자열 존재. AutoView(`aview64.exe`)는 스크립트로 `RECORD MP4`, `TIME SCALE`, `PAN`, `VIEW` 등을 지원. AutoView는 별도 인증이 필요할 수 있음 | 8장 참조 |
| CAD 가져오기 | AM은 DXF/DWG를 그래픽으로만 가져온다. 경로 변환 기능은 없다 | 경량화는 플러그인이 담당 |
| 기존 플러그인 | C# .NET. 특정 레이어 선 → path(방향은 XData, 화살표 블록 표시), CP 어트리뷰트 블록(타입: standby, load, unload, rtnload, rtnunload, traffic), 특정 레이어 사각형 → AM block | **재작성한다.** 새 저장소에서 시작하고 기존 플러그인은 규약(레이어·블록·XData)의 참고 자료로만 쓴다. 기존 명령 이름 호환은 요구하지 않는다 |
| Path Mover의 교통제어 | AGVS 시스템에는 영역형 block 엔티티가 없다(데모 43개 시스템 전부 확인). 교통제어 수단은 CP의 `cap`과 Process 시스템의 block이며, block은 CP 속성(claim/release 목록)으로 연결하거나 로직에서 `claim`/`release`한다 | 사용자의 현재 방식(Process block + CP 점유)이 AM의 정규 방식이다. 설계 유지 |
| 실제 규모 | 그룹 10~20개, 자재 100~150개, CP 약 1000개 | 모델 엔티티가 수천 개가 된다. 빌드 중 본 "Entity limit 200" 경고가 라이선스 제한이면 모델이 빌드되지 않으므로 11장 3번이 최우선 관문이다 |
| 분석 PC | 분석을 돌리는 서버는 이 PC가 아니며, 인증은 로컬 USB 키(Sentinel HASP HL)다 | 이 PC에서 본 Authorization 대화상자는 키가 없어서 나온 것일 수 있다. 창 없는 실행 검증은 반드시 분석 서버에서 다시 한다 |
| 운행 모델 | 1회 운행 = 자재 1종을 한 공정에 공급(+빈 용기 회수). 밀크런은 없다 | 7.2의 단일 자재 공급 사이클로 충분하다 |

## 4. 전체 구성

세 개의 산출물로 나눈다. 모두 C#이다.

```
ASH.Core        (.NET Standard 2.0 라이브러리, 공용)
  - Asy 모델 객체와 Writer/Reader (AGVS, Process)
  - Excel 양식 스키마와 검증
  - 공용 로직 템플릿과 파라미터 파일 생성
  - 결과 파일 파서, KPI 계산, 판정 규칙

ASH.Plugin      (ZWCAD .NET 플러그인, 신규 작성)
  - 모듈 A: 도면 경량화
  - 모듈 B: 레이아웃 작성·검증·내보내기 (명령 + 팔레트 GUI)
  - 모듈 C 호출: Excel 양식 생성, 모델 생성·빌드 버튼
  - 모듈 D 결과 시각화: 병목 히트맵 레이어를 DWG에 그린다

ASH.Runner      (.NET 10 콘솔/트레이 앱, ZWCAD 밖에서 독립 실행)
  - 모듈 D: 반복 실행, 대화상자 처리, 결과 수집, 탐색, 보고서·영상 생성
  - ZWCAD가 꺼져 있어도 밤새 돌아가야 하므로 분리한다
```

프로젝트 폴더 구조(실행기와 플러그인이 공유하는 규약):

```
<Project>\
  layout.dwg              사용자가 작업하는 도면 (진실의 원본 1)
  input.xlsx              Excel 양식 (진실의 원본 2)
  lite\                   경량화된 DXF와 통계
  <Model>.arc\            플러그인이 생성. 사용자는 user_ext.m만 편집
    model.amo
    agv.asy               모듈 B 생성
    <Model>~.asy          모듈 C 생성 (Process + 공용 로직 포함)
    user_ext.m            프로젝트 특수 로직. 플러그인은 생성만 하고 덮어쓰지 않음
    params.txt            런타임 파라미터 (탭 구분). 실행기가 실행마다 교체
  <Model>.dir\            AM 빌드 산출물
  runs\                   실행별 결과 (runs\g1_n5_s3\result.tsv 등)
  report\                 PPTX, PNG, MP4
```

## 5. 모듈 A: 도면 경량화

입력: 사용자가 ZWCAD에서 연 원본 공장 도면(DWG, 외부참조·3D·이미지 포함 가능).
출력: `lite\<name>_lite.dxf`와 전후 통계(엔티티 수, 파일 크기, 제거된 종류별 개수).

처리 규칙(순서대로):

1. 외부참조(xref)와 래스터 이미지, OLE 개체 제거. 외부참조는 사용자가 선택하면 바인딩 후 처리.
2. 레이어 화이트리스트/블랙리스트. 기본값은 건물·설비·통로 관련 레이어 유지, 치수·문자·해치·주석 레이어 제거. 사용자가 팔레트에서 레이어를 체크/해제하며 결과 크기를 바로 확인한다.
3. 3D 솔리드·메시·서피스는 평면 외곽선(상면 투영)으로 치환하거나 제거. 기본은 제거이며 사용자가 선택할 수 있다.
4. 블록은 단순 선분·폴리라인·원호만 남기도록 폭발한다. 중첩 블록도 처리.
5. 스플라인·타원은 폴리라인으로 근사한다.
6. 좌표 원점 이동: 도면 범위의 왼쪽 아래를 (0,0) 근처로 옮겨 AM의 큰 좌표값 문제를 피한다. 이동량은 저장해 모듈 B의 좌표 변환에 재사용한다.
7. 단위를 Excel 설정값(Meters 등)에 맞춘다.
8. DXF R12 ASCII로 저장한다.

목표: 5MB 이하. 10MB를 넘으면 경고를 표시하고 더 제거할 후보 레이어를 제안한다.

## 6. 모듈 B: 레이아웃 작성과 내보내기

### 6.1 레이어·블록 규약

플러그인이 "양식 초기화" 명령으로 자동 생성한다. 기존 플러그인의 규약을 유지하되 이름을 아래로 정한다(기존 이름이 다르면 매핑 설정으로 호환).

| 레이어 | 내용 | AM 대응 |
|---|---|---|
| `AGV_PATH` | 폴리라인/선/원호 = 주행 경로. 방향은 XData | `GPATH … piece` |
| `AGV_CP_<TYPE>` | CP 종류별 레이어(TRAFFIC 빨강, LOAD 초록, UNLOAD 파랑, RTNLOAD 주황, RTNUNLOAD 자홍, STANDBY 노랑, BUFFER 청록). 종류를 알 수 없는 CP와 예전 도면은 `AGV_CP`. 종류의 기준은 TYPE 속성이고 레이어는 색 표시용 | `CPOINT` |
| `AGV_PATH_TWO` | 양방향 경로(색 4) | `GPATH … two piece` |
| `AGV_PATH_SPUR` | 지선(spur) 경로(색 2) | `GPATH … spur piece` |
| `AGV_PATH_CRAB` | 크랩(crab) 경로(색 30) | `GPATH … crab piece` |
| `AGV_DIR` | 방향 화살표 블록(표시용, 자동 생성·갱신) | 없음 |
| `AGV_CP` | CP 어트리뷰트 블록 | `CPOINT name X type T at <path> <dist>` |
| `AGV_BLOCK` | 교통제어 사각형 또는 폴리라인 영역 | Process 시스템의 block 엔티티(cap 1) + 로직의 claim/release |
| `AGV_STATION` | 공정/스테이션 위치 블록(자재 연결용) | 로직의 스테이션 테이블 |
| `AGV_RESULT_*` | 모듈 D가 그리는 결과 레이어(병목 히트맵) | 없음 |

CP 블록 어트리뷰트: `NAME`(사용자 지정, 필수), `TYPE`(standby / load / unload / rtnload / rtnunload / traffic / buffer), `GROUP`(AGV 그룹), `CAP`(동시 점유 수. traffic 1, 나머지 종류는 `INF` 무제한). 예전에 있던 `STATION` 속성은 쓰지 않아 없앴다(공정과 CP의 연결은 Excel `Stations` 시트의 `CPName`). 이미 `STATION`이 있는 도면의 블록 정의는 그대로 두고 읽지 않는다.

`buffer` 타입은 이번에 추가한다. 공정 앞에서 AGV가 재고가 언로딩 가능 수준으로 떨어질 때까지 대기하는 지점이다.

block 사각형 어트리뷰트: `NAME`, `CAP`(기본 1). AM의 block 교통제어는 다음과 같이 동작한다(사용자 확인). 차량이 block에 진입하기 전 CP에서 block을 claim하고, block의 경계(사각형) 밖으로 나가면 AM이 자동으로 release한다. 따라서 release용 CP는 필요 없고, 사각형 자체가 release 경계다. AM 매뉴얼("Claiming blocks") 확인 결과 claim도 자동이다. 차량이 경계에 들어가면 claim, 나가면 release하며, CP 속성이나 로직의 `claim`은 필요 없고 vehicle function 안에서는 block을 claim할 수 없다(관문 4, `docs/gates.md`).

**교통제어 기본 방식(2026-10-20 사용자 결정):** block은 사각형이라 직각 교차에서만 정확하고, 사각 교차·합류에서는 경계 진입 시점으로 대기 위치를 세밀하게 잡기 어렵다. 그래서 Path Mover 시스템 설정의 "block claim 트리거"를 **경계 진입이 아니라 'CP를 목적지로 이동할 때 경로상의 block을 미리 점유'** 로 바꾸는 것을 기본으로 한다. 이 설정에서는 traffic CP가 대기 위치이자 claim 시점이 되므로 진입 CP를 유지한다. 정확한 AGVS `.asy` 키워드는 모듈 C(2차 계획)에서 AM 편집기 저장본으로 추출한다. block 자체는 Process `.asy`의 `BLOCK name <NAME> <표시수> cap <CAP>` 엔티티로 생성하며, 사각형 좌표가 그래픽 경계가 된다.

CP 수가 1000개 규모이므로 traffic CP의 자동 생성은 기본값을 "교차·합류점 앞뒤에만"으로 두고, 등간격 배치는 사용자가 켤 때만 한다.

### 6.2 명령과 팔레트

명령은 모두 `AGV` 접두어를 쓰고, 팔레트(사이드바)의 버튼이 같은 명령을 호출한다.

경로 작성:

- `AGVPATH`: 폴리라인을 그리면 세그먼트별로 방향 XData와 화살표를 자동 부여. 그린 순서가 진행 방향.
- `AGVCONVERT`: 일반 도구로 그린 LINE·ARC·LWPOLYLINE·(맞춤 없는) 2D POLYLINE을 선택(또는 한 레이어 전체)해 `AGV_PATH` 레이어의 단방향 경로로 바꾼다. `AGVPATH`처럼 구간마다 경로 하나, 그린 방향이 진행 방향. 이어서 모든 경로를 `AGVSPLIT`처럼 교차 분할하고, 건너뛴 객체(SPLINE, 맞춤 폴리선, 잠긴 레이어, 이미 경로 등)와 방향·종류 변경 방법(`AGVREVERSE`, `AGVPTYPE`)을 명령줄에 안내한다. 한 번의 Ctrl+Z로 전부 되돌린다.
- `AGVREVERSE`: 선택 경로의 방향 반전.
- `AGVOFFSET`: 선택 경로를 간격 d로 평행 복제(양방향 통로). 방향은 반대로 설정하는 옵션.
- `AGVARRAY`: 선택 경로·CP·block을 간격과 개수로 배열 복제. CP 이름은 접미 번호 자동 증가.
- `AGVSPLIT`: 선택한 경로들(기본은 `AGV_PATH` 레이어 전체)끼리 서로 분할한다. 모든 교차점과 T자 접점(한 경로의 끝점이 다른 경로의 중간에 닿는 곳)을 찾아, 각 경로를 그 점들에서 모두 나눈다. 교차점이 n개면 그 경로는 n+1개가 된다. 각 조각은 원래 경로의 방향 XData와 타입을 그대로 가지며 이름은 원래 이름 뒤에 `_1`, `_2`, …를 붙인다. 점을 하나 찍어 나누는 수동 모드도 옵션으로 둔다. AM은 경로가 교차점에서 끊어져 있어야 분기·합류로 인식하므로, 내보내기 전 `AGVCHECK`가 분할되지 않은 교차점을 찾아내고 "지금 분할" 버튼을 제공한다.
- `AGVJOIN`: 끝점이 맞닿은 경로 병합.
- `AGVFILLET`: 두 직선 경로 사이를 지정 반경 원호로 연결(AM의 `angle` 원호 piece로 내보냄).
- ZWCAD 기본 move/copy/trim/stretch는 그대로 사용 가능해야 하며, 플러그인은 저장 전 검증에서 끊어진 연결을 찾아낸다.

CP 작성:

- `AGVCP`: 경로 위를 클릭하면 가장 가까운 경로에 투영해 CP 블록을 놓는다. 두 가지 모드가 있고 팔레트에서 전환한다.
  - **상세 모드**: 어트리뷰트 입력 창이 뜬다. load/unload처럼 개별 정보가 필요한 CP에 쓴다.
  - **연속 모드(기본)**: 팔레트에 TYPE, GROUP, 접두어를 한 번 설정해 두면, 클릭할 때마다 그 값으로 CP를 놓고 NAME만 `접두어 + 번호`로 자동 가산한다(예: `T001`, `T002`). 시작 번호는 도면에 이미 있는 같은 접두어의 최대 번호 + 1이다. Enter나 Esc로 끝낸다. 타입을 바꾸면 접두어와 CAP도 타입 기본값(T=traffic, L=load, U=unload, RL=rtnload, RU=rtnunload, S=standby, B=buffer)으로 바뀌고 번호는 접두어별로 따로 센다.
- `AGVCPAUTO`: traffic CP 자동 배치. 기준은 다음과 같고, 거리 기본값은 Settings 시트에서 읽는다.
  - **block 진입 기준(기본 켜짐)**: 도면의 각 block 사각형에 대해, 사각형으로 들어가는 각 경로마다 사각형 경계에서 `ClaimDist`(기본 = 안전 간격) 앞에 진입 CP를 하나 놓는다. 이 CP에서 block을 claim한다. 해제는 차량이 사각형 밖으로 나갈 때 AM이 자동으로 하므로 진출 CP는 만들지 않는다. 경계 앞 거리가 부족하면(이전 노드와 너무 가까우면) 가능한 가장 먼 위치에 놓고 경고한다. block이 아직 없으면 `AGVBLOCKAUTO`를 먼저 실행하라고 안내한다.
  - **간격 기준(기본 꺼짐)**: 직선·곡선 구간을 `CPSpacing`(기본 10 m) 간격으로 나누어 traffic CP를 놓는다. 긴 통로에서 차량이 줄지어 따라가도록 하고 싶을 때만 켠다.
  - **곡선 기준(기본 꺼짐)**: 원호 시작·끝에 CP를 놓는다. 곡선 속도 제어가 필요할 때 켠다.
  - 이미 같은 위치(허용 오차 내)에 CP가 있으면 새로 만들지 않는다. 이름은 연속 모드와 같은 규칙으로 `T` 접두어에 자동 번호를 붙인다.
- `AGVBLOCKAUTO`: 교통제어 block 자동 생성. 기준은 CP 자동 배치와 짝을 이룬다.
  - **노드 block(기본 켜짐)**: 경로가 만나는 모든 노드(교차·분기·합류, 즉 한 점에 경로 끝점이 3개 이상 모이는 곳)마다 사각형 하나. 노드 점을 중심으로, 노드에 닿는 모든 경로를 따라 `BlockReach`(기본 = 차량 길이 + 안전 간격)만큼 뻗은 구간을 덮는 최소 사각형에, 차량 폭의 절반 + 안전 간격을 사방으로 더한 크기다. 차량 전체가 사각형을 빠져나가야 release되므로 사각형이 차량 길이보다 작으면 교차로 안에서 뒤차가 들어온다. cap은 1. 이름은 `B` + 자동 번호.
  - **구간 block(기본 꺼짐)**: 간격 기준 CP를 켠 경우, 연속한 두 traffic CP 사이 구간마다 block을 만든다. 통로 FIFO가 된다.
  - 실행 순서는 `AGVBLOCKAUTO` → `AGVCPAUTO`다. CP 자동 배치가 block 경계를 기준으로 하기 때문이다. 팔레트의 "교통제어 자동 생성" 버튼 하나가 두 명령을 이 순서로 실행한다.
  - 가까운 노드들(사각형이 겹치는 경우)은 하나의 block으로 합칠지 사용자에게 묻는다. 합치면 교차로 묶음 하나가 cap 1이 되어 교착은 줄고 통행량은 준다.
  - 사용자는 생성된 사각형을 ZWCAD에서 그대로 옮기거나 크기를 바꿀 수 있고, 내보내기 때 사각형과 경로의 실제 교차를 다시 계산한다.

Settings 시트에 다음 값을 둔다. `SafetyGap`(기본 0.5 m), `CPSpacing`(기본 10 m), `ClaimDist`(기본 = SafetyGap), `BlockReach`(비우면 AGV_Types의 Length + SafetyGap). 그룹마다 차량 길이가 다르면 그 경로를 쓰는 그룹 중 가장 긴 차량을 기준으로 한다.

검증과 내보내기:

- `AGVCHECK`: 다음을 검사하고 문제 위치로 줌 이동한다.
  - 끝점이 다른 경로와 연결되지 않은 경로(허용 오차 1mm).
  - 서로 만나는 경로의 방향이 교차점에서 진입 전용만 있고 진출이 없는 경우(막다른 길).
  - 중복 CP 이름, 이름 없는 CP, 경로에서 허용 거리 밖에 있는 CP.
  - block이 어떤 경로와도 교차하지 않는 경우.
  - Excel의 Routes 시트가 참조하는 CP 이름이 도면에 없는 경우(Excel이 있을 때).
- `AGVEXPORT`: `agv.asy`를 생성한다. 기존 파일은 `.asy.bak.<timestamp>`로 백업한다. 생성 후 `.asy`를 기본 텍스트 편집기로 열고, 변경 요약(추가·삭제·이동된 경로/CP 수)을 표시한다. 좌표는 모듈 A의 원점 이동량과 Excel의 단위 설정을 적용한다.

### 6.3 `.asy` 매핑 규칙

- 직선 세그먼트 → `GPATH name <n> type <ptype> piece begx … begy … endx … endy … upz 1`
- 원호 세그먼트(폴리라인 bulge 또는 ARC) → `GPATH … piece cenx … ceny … begx … begy … upz 1 angle <a>`. `a`는 0.1도 단위 정수(예: 90도 → 900). 부호는 회전 방향에 따르며, 데모 파일의 부호 규칙을 샘플로 검증한다.
- 경로 종류 → AutoMod `GPATH` 키워드: 단방향(없음), 양방향 `two`, 지선 `spur`, 크랩 `crab`. 종류는 도면 XData(`path`/`two`/`spur`/`crab`, 겹치면 `spur+crab`)가 정하고 레이어는 색 표시용이다. 예전에는 지선을 항상 `spur crab`으로 내보냈지만 이제 `spur`만 내보낸다(크랩은 따로 고른다).
- 양방향 경로: 노드의 들어오는/나가는 수를 양쪽으로 세고, 교통 CP는 블록 양쪽에서 들어오는 방향마다 만든다. 방향표는 양쪽 화살표(`AGV_DIR2`).
- 방향표 길이는 `ash.settings.tsv`의 `ArrowSize`(미터, 기본 2.0)이고 짧은 조각에서는 조각 길이의 80%로 줄인다.
- 경로 타입 → `GPATHTYPE name <ptype> … vel <v> …`. 속도는 Excel의 AGV_Types에서 가져온다.
- CP → `CPOINTTYPE`(타입별 cap) + `CPOINT name <NAME> type <T> at <path> <dist>`. `dist`는 경로 시작점부터의 거리.
- 차량 → `AGVVEHSEG`(세그먼트, 그래픽 크기), `AGVSVEH type <type> numveh <max>` + `start <namelist>`. `numveh`는 Excel의 그룹별 최대 시험 대수로 쓴다(주차 패턴).
- 리스트 → `NAMELST`, `PARKLST`, `WORKLST`는 standby/buffer CP와 Excel의 그룹 정의에서 생성한다.
- `FLAGS`, `SYSDEF`, `AGVSTOL`, `UNITS`, `VERSION` 헤더는 데모 파일을 템플릿으로 쓴다. `VERSION`은 설치본에서 읽은 값(현재 `12.6.1.19`)을 넣는다.

교통제어 block은 AGVS `.asy`가 아니라 Process `.asy`의 엔티티다. 정확한 줄 문법은 샘플 모델에서 추출한다(11장).

## 7. 모듈 C: Excel 양식, 공용 로직, 모델 생성

### 7.1 Excel 양식 (`input.xlsx`)

플러그인의 "양식 생성" 버튼이 빈 양식을 만든다. 시트와 열은 다음과 같다. 모든 시트의 첫 행은 헤더이고 두 번째 행은 설명/단위이다.

**Settings**

| 키 | 기본값 | 설명 |
|---|---|---|
| Units | Meters | AM 거리 단위 |
| SimLength | 8 h | 시뮬레이션 길이 |
| Warmup | 1 h | 통계 제외 구간 |
| Replications | 3 | 대수마다 반복 횟수(시드 변경) |
| SeedBase | 1 | 시드 시작값 |
| UtilMax | 0.75 | AGV 가동률 상한 |
| SafetyStock | 2 | 최소 안전재고(용기 단위, 자재별 열이 비어 있을 때 기본) |
| InitStockMode | Uniform | Fixed / Uniform / Staggered. Uniform은 ±N%, Staggered는 자재 순번 오프셋 |
| InitStockSpread | 10 | Uniform의 N(%) 또는 Staggered의 오프셋(용기) |
| TrafficMode | Block | Block(사각형 block 사용) / CPOnly |
| WorkDefinition | LoadToUnload | 가동률 계산 시 work로 보는 구간. LoadToUnload / CallToReturn |

**AGV_Types**: Type, Group, SpeedLoaded, SpeedEmpty, Accel, Decel, Capacity(용기 수), LoadTime, UnloadTime, MTBF, MTTR, Length, Width, Color.

**AGV_Groups**: Group, Type(이 그룹이 쓰는 AGV 타입, 하나), MinCount, MaxCount(시험 범위; MaxCount가 `numveh`), StandbyCPs(쉼표 구분), Dispatch(Nearest / FIFO).

설계자 결정: v1에서는 그룹당 AGV 타입을 하나로 제한한다. 타입이 다른 차량은 다른 그룹으로 정의한다. 한 그룹 안에서 여러 타입을 섞으면 대수 탐색 공간이 타입 조합으로 늘어나므로 v2 과제로 둔다.

**Materials**: Material, Station, Group, TaktTime(초), UnitsPerCycle(T/T당 소비 원수), UnitsPerContainer, CallThreshold(용기), UnloadThreshold(용기), InitStock(용기, 비우면 Settings 규칙), SafetyStock(비우면 Settings 기본), EmptyReturn(Y/N).

**Routes**: Material, SupplyFromCP(load), SupplyToCP(unload), BufferCP(선택), ReturnFromCP(rtnload, 선택), ReturnToCP(rtnunload, 선택), ExtraTime(초, 고정 추가 시간).

**Stations**: Station, CPName, BufferCapacity(공정 앞 적치 용기 수).

**Ext**: 확장 포인트 사용 여부와 설명. 체크된 훅만 `user_ext.m`에서 호출된다.

### 7.2 공용 로직 설계

Process `.asy`의 `code.m`에 들어간다. 사용자는 이 파일을 편집하지 않는다.

- **파라미터 읽기**: `model initialization function`에서 `params.txt`(탭 구분)를 읽어 변수와 배열에 넣는다. 그룹별 가동 대수, 자재별 임계값, 초기재고, 시드 등. 실행기는 이 파일만 바꿔 재빌드 없이 시나리오를 바꾼다.
- **차량 수 제어**: 각 그룹의 `vehicle initialization`에서 순번을 매기고, `task search`에서 순번이 가동 대수를 넘는 차량은 주차 큐로 보내 `wait to be ordered`로 영구 대기시킨다(데모 패턴).
- **소비 프로세스**: 자재마다 하나. T/T마다 재고를 `UnitsPerCycle / UnitsPerContainer` 용기만큼 감소. 재고가 0이면 공정 정지 시간을 누적(정지 시간은 리스크 지표).
- **호출**: 재고가 CallThreshold 이하이고 미완료 호출이 없으면 공급 작업을 생성해 그룹 작업 리스트에 넣는다.
- **공급 사이클**: 대기 CP → load CP(로딩 시간) → (BufferCP가 있으면) buffer CP에서 재고 ≤ UnloadThreshold가 될 때까지 대기 → unload CP(언로딩 시간, 재고 증가) → EmptyReturn이면 rtnload CP → rtnunload CP → 다음 작업 또는 standby. 빈 용기가 없으면 회수 구간을 건너뛴다.
- **교통제어**: 모듈 B가 만든 "block ↔ 진입 CP" 테이블(`traffic.tsv`)로, 진입 CP에 도착하면 `claim block`. release는 차량이 block 사각형 밖으로 나갈 때 AM이 자동으로 하므로 로직에서 하지 않는다. cap 1이면 FIFO가 된다. 교착(두 차량이 서로의 block을 기다리는 상태)은 일정 시간 이상 대기 시 경고를 기록한다.
- **고장**: AGV 타입에 MTBF/MTTR이 있으면 주행 중 고장을 발생시켜 그 자리에서 MTTR만큼 멈춘다. 뒤차는 block 때문에 자연히 대기한다.
- **통계와 출력**: `model snap function`에서 Warmup 이후 값을 `result.tsv`로 쓴다.
  - 자재별: 최소 재고, 평균 재고, 공급 횟수, 기대 공급 횟수(=소비량/용기), 공정 정지 시간.
  - 그룹별·차량별: 상태별 시간 비율(idle, travel empty, travel loaded, load, unload, wait at buffer, blocked, down), 트립 수, 총 이동거리, 공급당 이동거리의 평균과 표준편차.
  - block별: 통과 횟수, 평균·최대 대기 시간, 점유율.
  - CP별: 도착 횟수, 대기 시간.
  - 교착 경고 횟수.

가동률 정의: `WorkDefinition`에 따라 work 상태의 시간 비율. LoadToUnload는 로딩 시작부터 언로딩 완료까지, CallToReturn은 호출 수락부터 standby 복귀까지를 work로 본다. blocked 시간은 별도 지표로 항상 보고한다.

### 7.3 확장 지점 (`user_ext.m`)

공용 로직은 다음 훅 함수를 호출하며, 기본 구현은 아무것도 하지 않고 `return true`다. 플러그인은 이 파일이 없을 때만 생성한다.

- `FExtOnCall(material)`: 호출 조건을 바꾸거나 막는다.
- `FExtSelectVehicle(group, job)`: 배차 규칙을 바꾼다.
- `FExtBeforeUnload(vehicle, material)`: 언로딩 조건을 바꾼다.
- `FExtOnSnap()`: 추가 통계를 기록한다.

### 7.4 모델 생성과 빌드

플러그인의 "모델 생성" 버튼이 다음을 한다.

1. Excel을 읽고 검증한다(참조 CP 존재, 그룹 일치, 숫자 범위).
2. `model.amo`, `<Model>~.asy`(Process), `params.txt`를 생성한다. `agv.asy`는 모듈 B가 이미 만들었거나 이 시점에 함께 내보낸다.
3. `am264.exe -bq <Model>`을 실행하고, 빌드 중 뜨는 "Entity Limit Status" 같은 정보 대화상자는 창 제목으로 식별해 자동으로 닫는다. 라이선스 선택 대화상자는 닫지 않고 사용자에게 보여 준다.
4. `am2err.dat`와 `<Model>.dir\make.err`를 읽어 오류를 팔레트에 표시한다.

## 8. 모듈 D: 반복 실행, 분석, 보고서, 영상

### 8.1 실행기 (`ASH.Runner`)

입력: 프로젝트 폴더. 출력: `runs\` 아래 실행별 결과, `report\` 아래 보고서.

- 실행 단위 = (그룹별 가동 대수 조합, 시드). 각 실행 전 `params.txt`를 쓰고 `<Model>.exe -w`를 실행한 뒤 `result.tsv`를 `runs\<id>\`로 옮긴다.
- 실행 시간 상한을 두고 초과 시 프로세스를 종료하고 실패로 기록한다.
- 동시 실행 수는 설정값(기본 1). 로컬 USB 키 하나는 보통 동시 실행 1개만 허용하므로, 기본 전략은 "순차 실행을 밤새 돌리는 것"이다. 실행 시간 예산: 그룹 15개 × 이진 탐색 약 4점 × 반복 3회 + 확인 실행 = 약 200회. 1회가 5분이면 약 17시간이다. 그래서 초기 탐색은 반복 1회로 좁히고 결정값 근처에서만 반복 3회를 돌리는 2단계 전략을 기본으로 한다(12장의 반복 수 조절 항목). 1회 실행 시간은 분석 서버에서 측정해 Settings의 안내값으로 기록한다.
- 창 없는 실행이 라이선스 때문에 불가능하면 "반자동 모드"로 전환한다. 실행기가 `params.txt`를 쓰고 사용자가 AM 편집기에서 Run을 누르면, 실행기는 `result.tsv` 생성을 감시해 수집하고 다음 조합을 준비한다.

### 8.2 탐색 전략

- 그룹이 하나일 때: MinCount~MaxCount에서 이진 탐색으로 "모든 자재 최소 재고 ≥ SafetyStock"을 만족하는 최소 대수를 찾고, 그 대수와 ±1에서 가동률 상한을 확인한다. 각 점은 Replications만큼 반복해 평균과 신뢰구간(95%)으로 판정한다.
- 그룹이 여럿일 때: 그룹은 자재와 경로가 분리되어 있어 대체로 독립적이다. 그룹별로 위 탐색을 하고, 공유 통로가 있어 상호작용이 있으면 마지막에 모든 그룹의 결정값으로 확인 실행을 한다. 확인 실행이 실패하면 영향을 받은 그룹을 +1 하고 다시 확인한다.
- 판정 규칙(순서대로): 재고 조건 → 가동률 상한 → 공급 횟수가 기대치의 95% 이상 → 교착 경고 0.
- 모든 결과는 `runs\summary.tsv`에 누적되고 보고서는 이 파일만 읽는다.

### 8.3 보고서 (PPTX)

경영진과 현장 양쪽이 보므로 앞부분은 결론, 뒷부분은 근거다.

1. 결론: 그룹별 적정 대수, 조건 요약(가동률 상한, 안전재고), 총 대수.
2. 대수-KPI 곡선: 그룹별로 x축 대수, y축 최소 재고·가동률·공정 정지 시간. 적정 대수에 표시.
3. 병목 히트맵: 경량화 도면 위에 경로·block을 대기 시간 기준으로 색칠한 이미지. 같은 내용을 ZWCAD의 `AGV_RESULT_*` 레이어에도 그려 사용자가 도면에서 바로 본다.
4. 자재 재고 추이: 가장 위험한 자재 상위 N개의 시간-재고 그래프.
5. 리스크 목록: 재고가 안전재고에 근접한 자재, 대기 시간이 긴 block, 공급당 이동거리 편차가 큰 경로, 교착 경고.
6. 영상 슬라이드: 전체 개요 영상과 병목 구간 영상을 삽입.
7. 부록: 실행 조건, 시드, 반복 수, 입력 Excel 요약.

### 8.4 영상

세 가지 경로가 있고 v1은 화면 녹화를 기본으로 한다.

- **화면 녹화(기본)**: 실행기가 애니메이션 모드로 모델을 실행하고 ffmpeg(gdigrab)로 창을 녹화한다. 배속은 모델이 `result.tsv`와 함께 쓰는 시계 로그(실제 시각 ↔ 시뮬 시각)로 계산해 ffmpeg `drawtext`로 캡션을 입힌다. 병목 구간 영상은 AM의 저장된 뷰 이름을 지정해 같은 방식으로 찍는다. 라이선스 추가가 필요 없고 설정이 단순하다.
- **AutoView 스크립트**: 모델 실행 제어에서 AutoView 출력을 켜 `.avw`를 만들고, 실행기가 `TIME SCALE`, `PAN`, `VIEW`, `RECORD MP4` 스크립트를 자동 생성해 `aview64.exe`로 렌더링한다. 품질은 좋지만 AutoView 인증이 필요할 수 있어 v1에서는 선택 기능으로 둔다.
- **런타임 GIF 캡처**: 런타임에 기능 문자열이 있으나 사용법 미확인. 조사 항목으로 남긴다.

## 9. 사용자 작업 흐름 (완성 후)

1. ZWCAD에서 원본 도면을 열고 `AGV 경량화`를 실행해 `lite\` DXF를 만든다. AM 모델의 정적 그래픽으로 가져온다(이 가져오기는 AM 편집기에서 한 번만 한다).
2. `AGV 양식 초기화`로 레이어·블록을 만들고 경로, CP, block을 그린다. `AGVCHECK`로 검증한다.
3. `AGV Excel 생성`으로 `input.xlsx`를 만들고 자재·AGV·경로를 입력한다.
4. `AGV 내보내기`로 `agv.asy`를 만들고 열어 확인한다. `AGV 모델 생성`으로 Process `.asy`와 `params.txt`를 만들고 빌드한다.
5. `AGV 분석 실행`으로 Runner를 띄운다. 끝나면 `report\` 폴더의 PPTX가 열리고 도면에 병목 레이어가 그려진다.
6. 동선 변경 통보가 오면 2번으로 돌아가 도면만 고치고 4~5를 다시 실행한다. Excel과 `user_ext.m`은 그대로다.

## 10. 설계자가 정한 기술 선택

사용자가 "참고만 하고 개선해도 좋다"고 했으므로 다음은 설계자의 결정이다.

- 언어와 런타임: 전부 C#. `ASH.Core`는 .NET Standard 2.0으로 만들어 ZWCAD 플러그인(.NET Framework)과 Runner(.NET 10)가 공유한다.
- Excel 읽기/쓰기: ClosedXML(Excel 설치 불필요). 런타임 파라미터는 Excel 함수 대신 탭 구분 텍스트(`params.txt`)를 쓴다. AM의 `XLClose`가 Excel 프로세스를 죽이는 문제를 피하기 위해서다.
- PPTX 생성: DocumentFormat.OpenXml. 그래프 이미지는 ScottPlot으로 PNG를 만들어 삽입한다.
- 영상: ffmpeg 실행 파일을 Runner와 함께 배포한다.
- 대화상자 처리: Win32 `EnumWindows`로 창 제목을 찾아 정보성 대화상자만 닫는다. 라이선스·오류 대화상자는 닫지 않는다.
- 외부 호스트 의존 없음: 모든 NuGet 패키지는 빌드 시점에 복원되고 실행 시 네트워크가 필요하지 않다.

## 11. 미확정 사항과 검증 관문

구현 전 또는 구현 초기에 확인해야 한다. 결과에 따라 8.1의 모드가 바뀐다.

아래 1~3은 **분석 서버(USB 키가 있는 PC)에서** 확인해야 한다. 이 PC의 결과는 참고용이다. 확인 방법은 이번에 쓴 절차와 같다. 데모 `agvdemo.arc`를 임시 폴더에 복사하고, `am264.exe -bq agvdemo`로 빌드한 뒤, `agvdemo.exe -w`를 실행해 대화상자 없이 끝나고 `agvdemo.report`가 채워지는지 본다.

1. **런타임 인증 자동화**: USB 키가 꽂힌 서버에서 `<Model>.exe -w`가 Authorization 대화상자 없이 실행되는지. 대화상자가 뜨면 라이선스 설정(`hasp.ini` 등)으로 해결 가능한지 확인한다. 안 되면 8.1의 반자동 모드가 기본이 된다.
2. **동시 실행 허용 수**: 같은 PC에서 모델 실행 파일을 2개 이상 동시에 띄울 수 있는지. 로컬 키는 1개일 가능성이 높다.
3. **엔티티 제한**: 빌드 중 "Entity limit: 200" 경고의 의미. 실제 모델은 CP 1000개, 자재 150개 규모여서 엔티티가 수천 개다. 이 제한이 라이선스 제한이면 모델 분할 또는 라이선스 확인이 선행되어야 하므로 **가장 먼저 확인한다.** 사용자가 지금까지 실제 모델을 돌려 왔다면 서버의 라이선스에는 이 제한이 없을 가능성이 높다.
4. **Process `.asy`의 block 문법과 claim/release 테이블 표현**: AM 편집기에서 block을 하나 만든 샘플 모델을 내보내 줄 문법을 추출한다.
5. **원호 `angle` 부호 규칙**: 데모 파일의 원호와 실제 그래픽을 비교해 시계/반시계 부호를 확정한다.
6. **AutoView 인증 필요 여부와 GIF 캡처 사용법**: 영상 기본 경로를 바꿀 수 있다.
7. **`.mff` 줄 문법**: 주차 패턴이 충분하면 필요 없다. 재빌드 없이 `numveh` 자체를 바꾸고 싶을 때만 조사한다.

## 12. 빌더에게 맡기는 것

- 팔레트 GUI의 구체적 배치와 아이콘.
- 기존 플러그인 코드에서 규약(레이어·블록·XData)을 참고하는 범위. 기존 명령 호환은 요구하지 않는다(3장).
- `AGVCHECK`의 허용 오차 기본값과 설정 UI.
- `result.tsv`의 정확한 열 순서(7.2의 항목을 모두 포함하면 된다).
- 보고서 템플릿의 디자인. 회사 PPT 템플릿 파일을 지정할 수 있어야 한다.
- 탐색 전략의 반복 수 조절(초기 탐색은 1회, 결정값 근처만 Replications 전부 등).
- 테스트: `agvdemo.arc`를 기준 모델로 삼아 `.asy` Writer의 출력이 AM에서 빌드되는지 확인하는 통합 테스트, Excel 스키마 검증 단위 테스트, 결과 파서 단위 테스트.
- 성능: CP 1000개, 경로 수천 개 규모의 DWG에서 `AGVCHECK`와 `AGVEXPORT`가 수 초 안에 끝나야 한다. 공간 인덱스(그리드 해시)로 끝점 연결과 CP 투영을 처리한다.

## 13. 저장소

저장소는 `Leepy0/ASH`(ASH = AGV Simul Helper). 솔루션 이름은 `ASH`, 프로젝트는 `ASH.Core`, `ASH.Plugin`, `ASH.Runner`, `ASH.Tests`. 이 설계서는 저장소의 `docs/superpowers/specs/`에 그대로 옮긴다.

## 14. 구현 중 확정된 사항 (2026-10-20 추가)

1차(Core+Plugin), 1.5차(UI·설치) 구현과 관문 확인으로 바뀌거나 확정된 내용이다. 본문과 다르면 이 절이 우선한다.

- **관문 결과** (`docs/gates.md`): (1) 창 없는 실행 — USB 키 서버에서는 Authorization 창이 뜨지 않음(사용자 확인). 체험판은 "Student Version" 버튼 클릭으로 진행. Runner는 **완전 자동 모드**. (2) 동시 실행 가능. (3) 엔티티 200 제한은 체험판 전용. (4) Process block 문법 `BLOCK name <N> <표시수> cap <C>` + `dis` 줄. claim/release는 경계 기준으로 자동.
- **교통제어 방식**: Path Mover 시스템 기본값 "Claim Block = When claiming next control point"를 기본으로 한다. AGVS `.asy`의 `AGVSDEF … ALTERNATE NONE ignore OldBlockClaim ResumeSpeedWhenClaimed`로 기록(토큰 대응은 추정, 2차 시작 시 편집기 저장본으로 확정). 진입 traffic CP는 유지(대기 위치 = claim 시점).
- **UI**: 팔레트는 탭 4개(경로/CP/교통제어/검증·내보내기) 평면 스타일; 리본 탭 `ASH` 패널 5개(`ZdWindows.dll`의 `ZwSoft.Windows` 리본 API). 계획서 `2026-10-20-ash-ui-and-installer.md`.
- **설치**: `ASH-Setup-<ver>.exe`가 ZWCAD 설치 위치를 찾아 `<ZWCAD>\ASH\`에 DLL을 넣고 `HKCU/HKLM\Software\ZWSoft\ZWCAD\<ver>\<lang>\Applications\ASH`(LOADCTRLS=2, MANAGED=1)로 자동 로드 등록. 플러그인은 ZWCAD 2026 API로 빌드.
- **단위**: Core는 단위 무관. Plugin이 `ash.settings.tsv`의 `Scale`(도면 단위→m)로 모든 길이 파라미터·허용 오차(물리 1 mm)·기호 크기를 환산. 설정은 Excel이 생기기 전까지 `ash.settings.tsv`.
- **내보내기 규칙 보강**: `ExportOptions.Groups` 필수, 그룹마다 standby/buffer CP 1개 이상 필수(없으면 오류), CP cap별 `CPOINTTYPE`(`DefaultControlPoint_cap<N>`), 숫자는 지수 표기 없이 출력.
- **2차 범위 재조정**: 모듈 C(Excel·공용 로직·모델 생성·빌드)와 모듈 D의 Plugin 무관 핵심(Runner 반복 실행·결과 파서·탐색·summary)을 한 계획으로 묶어 체험판에서 끝까지 검증한다. PPTX·히트맵·영상은 3차.

### 14.1 서버 통합과 버전·자동 업데이트 (2026-10-21, 사용자 결정)

- 분석 서버 코드(ASH.Server: ASP.NET Core net10 Windows 서비스, ASH.Worker: net10)는 **이 저장소 하나**에 둔다. 별도 저장소 `ASH_Server`는 더 쓰지 않는다. 설계서는 `docs/superpowers/specs/2026-10-21-ash-server-design.md`(13장 저장소, 16장 Core API, 17장 버전·업데이트), 시안은 `docs/images/result-summary-mock.png`, `result-detail-mock.png`. 두 프로젝트는 `ASH.sln`에 추가되고 `ASH.Core`를 참조한다. `dotnet build ASH.sln`·`dotnet test`·`scripts/build-release.ps1`는 솔루션 전체를 다루므로 추가 즉시 포함된다(서버·워커 publish 단계는 서버 계획에서 추가).
- **버전 하나**: `Directory.Build.props`의 `<Version>`(SemVer)을 플러그인·Core·Runner·서버·워커가 공유하고 릴리스마다 태그를 단다.
- **업로드 형식 버전**: "서버로 보내기" 묶음에 `manifest.json`(ASH 버전, 형식 버전 `FormatVersion`, 파일별 SHA-256). `blocks.tsv`·`traffic.tsv`·`params.txt`·`result.tsv` 형식을 바꾸면 `FormatVersion`을 올린다. 현재 형식 = 1.
- **플러그인 자동 업데이트**: 사내 서버가 최신 `ASH-Setup-x.y.z.exe`와 `/api/ash/version`(최신·최소 지원)을 제공. ASH는 ZWCAD 시작 시와 "서버로 보내기" 때 확인해 알리고, "ZWCAD 종료 후 설치"로 실행한다(설치 파일은 실행 중 ZWCAD를 거부함). 최소 지원 미만이면 서버 기능만 막는다. GitHub 접속 불필요(사내망 `api.github.com` 차단).
- **재현성**: `arc\input.sig`에 ASH 버전과 공용 로직 버전을 함께 기록한다(현재 입력 지문만 기록 → 3차에서 추가). 서버는 작업마다 Core 버전을 기록한다.
- **공용 로직 동기화**: 로컬 실행 전 서버에서 고정 버전(없으면 최신)의 공용 로직 템플릿을 받아 쓴다. 접속 불가면 Core 내장 템플릿을 쓰고 "공용 로직: 내장"을 기록한다. `LogicGenerator`가 외부 템플릿 텍스트를 받는 `LogicTemplateOverride`를 지원해야 한다(`MaterialOverrides`/`VehicleOverrides`와 같은 방식) → 3차.
- **에셋 동기화**: 고정된 차량 에셋 버전을 서버에서 받아 `VehicleOverrides`로 넘기고 로컬 캐시(`%LOCALAPPDATA%\ASH\assets\<ver>\`)에 둔다 → 3차.

### 15. CP 속성과 설정 파일 기본값 (명령 정리)

- **CP 종류**: traffic, load, unload, rtnload, rtnunload, standby, buffer. 종류마다 이름 접두어(T, L, U, RL, RU, S, B)와 레이어 색이 정해져 있고 팔레트 TYPE 목록에 색 견본과 이름 규칙(`L###`)이 보인다.
- **CAP**: traffic은 1, 나머지 종류는 무제한(`INF`, AutoMod 2147483647). 종류를 바꾸면 접두어와 CAP이 그 종류의 기본값으로 돌아간다. 내보낼 때 용량마다 `CPOINTTYPE`을 만들고 무제한은 `<이름>_capinf`.
- **STATION**: 쓰이지 않는 메모 속성이어서 없앴다(CP 블록의 속성은 NAME, TYPE, GROUP, CAP).
- **시간 표시**: 사람에게 보이는 시간과 파일 이름에 넣는 시간은 PC의 시간대와 상관없이 서울 시간(UTC+9)이다(서버 상태 페이지, 설치·플러그인 로그, 내보내기 백업 `agv.asy.bak.<시각>`, `runs.<시각>` 폴더). `ASH.json`의 `published`처럼 기계가 읽는 값은 UTC(`Z`)로 두고 보여 줄 때만 서울 시간으로 바꾼다. PC의 시계 자체가 틀리면 그대로 틀리므로 Windows 날짜 및 시간을 맞춘다. 서버 상태 페이지에 "서버 시각(서울)"과 PC 표준 시간대가 나와서 틀린 시계를 알아볼 수 있다.
- **필렛 반경**: 마지막으로 입력한 값을 PC별로(`%APPDATA%\ASH\ash.user.tsv`의 `FilletRadiusM`, 미터 단위) 기억해 다음 필렛의 기본값으로 보여 준다. 처음에는 1.2 m(mm 도면에서 1200).
- **traffic CP 자동 생성**: 교통제어 자동 생성(`AGVTRAFFIC` = block + traffic CP)과 겹쳐서 CP 탭과 리본 CP 패널에서 뺐다. 명령 `AGVCPAUTO`는 남아 있다.
- **ash.settings.tsv `Scale`**: 새 파일은 도면 `INSUNITS`로 정한다(mm 0.001, cm 0.01, m 1, inch 0.0254, ft 0.3048, 단위 없음 0.001). 파일에 `Scale`이 없으면 0.001.
- **방향표**: `AGVARROW`가 길이(`ArrowSize`, m)를 묻고 설정 파일에 저장한 뒤 모든 방향표를 바로 다시 그린다. Enter만 누르면 길이를 그대로 두고 다시 그리기만 한다(설정 파일을 직접 고친 뒤 쓴다). 경로를 편집하는 다른 명령도 끝날 때 다시 그린다.
- **미리 선택(PICKFIRST)**: `AGVREVERSE`, `AGVPTYPE`, `AGVSPLIT`, `AGVJOIN`, `AGVFILLET`, `AGVOFFSET`, `AGVARRAY`는 경로를 먼저 선택하고 실행하면 그 선택을 바로 쓴다(선택 질문을 건너뜀). 경로 두 개를 고르는 `AGVJOIN`·`AGVFILLET`은 맞닿은 순서를 스스로 찾는다.
- **AGVPATH**: `PLINE`을 명령 안에서 실행한다. 그래서 끝난 뒤 Enter는 `AGVPATH`(종류 질문부터)를 반복한다. 연속으로 그린 선은 폴리선 하나가 되고 필렛은 폴리선 안의 모서리를 둥글린다.
- **AGVFILLET**: 경로 하나를 선택하면 안의 모든 모서리, 경로 둘을 선택하면 둘이 만나는 모서리, 선택 없이 경로를 클릭하면 클릭 지점에서 가장 가까운 모서리(한 구간짜리 경로면 이어지는 경로를 더 묻는다). 반경은 마지막 값이 기본.
- **AGVOFFSET**: 기준점 → 변위(점을 찍거나 값 입력) → 새 경로 방향 뒤집기(기본 예). 쪽은 찍은 점(값을 입력했으면 기준점)이 경로 진행 방향의 왼쪽인지 오른쪽인지로 정한다.

- 경로 그리기(`AGVPATH`)는 직선 하나마다 경로 하나를 만든다. 이어진 선분이 한 폴리선이어도 분할해 태그한다. 이전에 만든 폴리선 경로는 그대로 읽고 `AGVFILLET`도 계속 지원한다.
- 숫자 입력줄의 기본값은 AutoCAD가 `<값>`으로 붙이므로 안내 문구에 같은 값을 쓰지 않는다.
- 검증 결과는 팔레트의 표(문제, 대상, 내용)에 문제 항목만 넣고, 행을 더블클릭하면 그 위치로 pan한다(배율 유지).
- 객체 찾기 탭은 범주(경로 종류, CP TYPE, block, 스테이션)와 이름·정보 글로 거르고, 더블클릭하면 객체를 선택하고 중심으로 pan한다. 팔레트는 도면을 직접 만지지 못하므로 `AGVFIND`(목록 갱신), `AGVGOTO`(이동) 명령을 거친다.
- Excel 양식은 ZWCAD 프로세스(바인딩 리다이렉트 없음)에서 돌아야 한다. ClosedXML의 시스템 글꼴 읽기(SixLabors.Fonts)를 쓰지 않는 그래픽 엔진으로 바꾸고, 플러그인 폴더의 dll을 버전과 무관하게 연결하는 `AssemblyResolve`를 `Initialize` 첫 줄에서 건다.
- Setup 마지막의 "설치 후 ZWCAD 실행"은 explorer.exe를 거쳐 관리자 권한 없이 띄운다.
