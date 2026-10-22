# AutoView 실무 가이드 (AutoMod 14.5 / aview64) — ASH 영상 컷 기능 설계용

작성: 2026-10-22. 문서 근거와 이 PC 실험 결과를 함께 적었다.

출처 표기:
- **[H:경로]**: AutoView 도움말(CHM 풀어 둔 것) `C:\Users\ioi\AppData\Local\Temp\amh_autoview\Topics\경로`
- **[UG p.N]**: `C:\AutoMod\12_6\help\manuals\amvol1and2.pdf` (Applied AutoMod **14.5** User's Guide, 16장 AutoView, 인쇄 쪽 번호)
- **[RN]**: `relnotes.pdf` (14.x 릴리스 노트), **[RT]**: `runtime.pdf`
- **[BIN:파일]**: 설치된 실행 파일 안의 문자열. `aview64.exe`에는 카메라 스크립트 문법(yacc 문법표)이 그대로 들어 있다. `xvt_h3d64.dll`에는 MP4 변환 명령이 있다. 추출본은 `scratchpad\autoview\aview64_strings.txt`, `xvt_strings.txt`.
- **[실험]**: 이 PC에서 직접 확인한 것. 파일은 `scratchpad\autoview\exp\` 아래에 있다.
- **(추정)**: 읽거나 관찰한 것이 아니라 추론한 것.

> 버전 주의: 폴더 이름은 `C:\AutoMod\12_6`이지만 설치본은 **AutoMod/AutoView 14.5.0.4**다(`aview64.exe` 파일 버전, `aviewout.dat`의 "AutoView 14.5 Build: 14.5.0.4"). 매뉴얼도 14.5판이다. ASH가 쓰는 `VERSION 12.6.1.19` 모델도 문제없이 빌드·실행됐다.

---

## 1. 개념: 기록 → 애니메이션 → 동영상

1. **기록(시뮬레이션 중)**: 모델 실행 중에 AutoView 출력이 켜진 snap 구간의 움직임이 `<모델>.avw\` 폴더에 기록된다 [UG p.672–673][H:Procedure\Generating_animation_description_files.htm]. 그래픽을 끈 배치 실행(`-w`)에서도 기록된다 [UG p.673 Tip][실험].
2. **애니메이션(aview64)**: AutoView가 기록 파일과 **카메라 설명 파일** `<모델>.avw\camera.des`를 읽는다. 이 파일은 "언제(시뮬레이션 시각) 어떤 시점으로 볼지"를 적은 스크립트다. 애니메이션은 스크립트의 **첫 time 문에서 시작해 마지막 time 문에서 끝난다** [H:Reference\time_stamp.htm][UG p.677].
3. **동영상**: 스크립트 앞부분에 `define movie spec … end movie spec`을 두고, `record movie` / `pause movie` / `generate movie`로 녹화 구간을 정한다 [H:Reference\record_movie.htm 등]. 형식은 avi, **mp4**, custom이다 [BIN:aview64 문법 `FORMAT MP4|AVI|CUSTOM`][UG p.688].

모델은 다시 돌리지 않는다. 한 번 기록한 `.avw`로 카메라 스크립트만 바꿔 가며 컷을 여러 개 만들 수 있다.

---

## 2. 기록 켜기

### 2.1 model.amo의 CONTROL 줄 (ASH 방식)
- Run Control 대화상자의 snap마다 있는 "AutoView" 체크가 `model.amo`의 `CONTROL … autoview` 토큰이다. 데모 `agvdemo.arc\model.amo` 원문은 `CONTROL snaplen 8 Hours counts 1 autorep autoview`이다. ASH의 `ModelAmoWriter.Render(autoView: true)`가 같은 토큰을 붙인다 [UG p.239 "AutoView: Writes the snap's animation to an AutoView file"].
- **일부 구간만 기록하기 [실험]**: CONTROL 줄을 두 개 쓰면 된다. 첫 줄에는 autoview를 빼고, 둘째 줄에만 넣는다.
  ```
  CONTROL snaplen 0.1 Hours counts 1
  CONTROL snaplen 0.1 Hours counts 1 autorep autoview
  ```
  결과(`exp\agvdemo.message`): `360.00: AutoView output is on.` → `720.00: AutoView output is off.` 기록은 360–720 s만 됐다. 기록이 없는 snap도 애니메이션 시간축에는 포함되지만 움직임은 보이지 않는다 [UG p.673 Note][H:Reference\faq_no_action.htm]. 그런 구간은 `jump to time`으로 건너뛴다.
  - 주의(추정): ASH가 지금 쓰는 `.report`는 autorep이 붙은 snap 끝에 써진다. CONTROL 줄을 나눌 때 autorep 위치와 통계 리셋 여부가 바뀌지 않게 해야 한다.
- 실행: `-w` 배치 실행에서 대화상자 없이 기록됐다 [실험]. 빌드(`am264 -bq`)에서는 체험판 "Entity Limit Status" 창이 떴다. 이것은 체험판 전용이다.
- 라이선스: 문서에는 "기록 파일을 만들려면 AutoView 라이선스가 필요하다"고 되어 있다 [H:Overview\Getting_started_with_AutoView.htm][UG p.672]. 하지만 **이 Student/체험판 PC에서도 `.avw` 생성과 MP4 생성이 모두 됐고, 라이선스 창은 한 번도 뜨지 않았다** [실험].
- `-r <이름>`(named run) 실행에서는 AutoView 파일도 `.nrd` 폴더로 간다 [RT "r (redirect output files) … Also redirects AutoView files"].

### 2.2 `.avw` 폴더 내용과 크기
| 파일 | 내용 | 생성 시점 |
|---|---|---|
| `<모델>.mdf` | 움직임 기록(텍스트). 헤더는 `distance units Micrometers`, `time units Seconds`이고, `time t object … pos/vel/acc …` 형식의 이벤트가 이어진다 | 시뮬레이션 [실험] |
| `<모델>.avf\` | 시스템 정의 사본(`agv.asy`, `conv.asy`, `<모델>~.asy`, `model.amo`) | 시뮬레이션 [실험] |
| `<모델>.vdf` | 저장된 뷰(TOP/BOTTOM/FRONT/BACK/LEFT/RIGHT 기본 6개 + 사용자 뷰) | **aview64로 처음 열 때** [실험][H:Procedure\Using_saved_views.htm] |
| `camera.des` | 카메라 스크립트. 직접 써도 된다 | GUI의 Model > Edit Script 또는 직접 작성 [H:Popup\Camera_description_file.htm] |

- 도움말에는 `.odf/.sdf/.pdf`가 따로 생긴다고 적혀 있지만, 14.5에서는 그 대신 `.avf\` 폴더가 생겼다 [실험].
- 크기 [실험]: agvdemo(AGV 10대 + 컨베이어)를 6분(360 s) 기록하니 `agvdemo.mdf` = **107,746 B**(약 18 KB/분)였다. 8시간을 통째로 기록하면 이 모델 기준 약 8.6 MB(추정, 선형 가정)다. 매뉴얼은 "snap은 짧게, 대개 10분 이하"를 권하고, 1분을 기록해 보고 크기를 가늠하라고 한다 [UG p.673].
- 모델 이름은 대소문자를 구분한다. 차량은 `agv:DefVehicle(n)`, 차량 그래픽은 `agv:DefSegment(n)`, 로드는 `temp N`, 큐는 `Qpallet(n)`처럼 mdf에 적힌 이름 그대로 쓴다 [실험].

---

## 3. 카메라 설명 파일(camera.des) 문법

전체 문법은 `aview64.exe` 안의 yacc 규칙에서 뽑았다 [BIN:aview64]. 도움말 그림과 대조했다 [H:Overview\Camera_description_file_syntax.htm 및 Reference\*.htm, 문법 그림 `image\S_AV*.gif`].

### 3.1 구조
```
camera description            ← 첫 줄(필수). "camera description file"도 가능
<기본 단위 문>                 ← 첫 time 문 앞
<표시 문>(turn, solid …)       ← 첫 time 문 앞이면 시작 설정, 뒤면 그 시각에 변경
define movie spec … end movie spec   ← 첫 time 문 앞
time <t>                      ← 시각
  <뷰 문 / 녹화 문 / 표시 문 …>
time <t2>
  …
```
- 주석: `/* … */`, `// …` [H:Procedure\Defining_comments_in_the_camera_description_file.htm]
- 키워드는 줄여 쓸 수 있고(밑줄 친 부분만 필수), 단위는 대소문자·단복수를 구분하지 않는다 [H:Overview\Reading_syntax_diagrams.htm][단위 그림].
- 시스템·엔티티·뷰 이름은 대소문자를 구분한다 [H:Popup\Obj_systemname.htm][H:Procedure\Using_saved_views.htm].

### 3.2 기본 단위 문 (Default)
```
distance feet | meter | millimeter | inch | …   (DIS unit)
time seconds | minutes | hours | …
velocity …  acceleration …  angle degree|radian|cycle  angular velocity …  angular acceleration …
```
[H:Overview\Default_syntax.htm][BIN:`default_dis : DIS unit_optional dis_unit` …]
- **중요 [실험]**: 이 버전에서는 뷰 좌표(`pos`)에 `distance feet`가 **적용되지 않았다**. `pos … feet`처럼 단위를 붙여도 마찬가지였다. 좌표는 **내부 단위 마이크로미터(µm)**로 읽혔다. 근거:
  - `.vdf`의 기본 뷰 숫자가 µm다(헤더는 `distance unit foot`인데 값은 6,320,924 = 20.7 ft).
  - feet 값(27.35, −10.05, 100)을 넣으면 카메라가 원점 근처 µm 위치로 갔다(t4/t5/t7/t9).
  - µm 값(8336280, −3063240)을 넣으면 정확히 그 지점이 화면 중앙에 왔다(t11/t13).
  
  → **뷰 좌표는 µm로 쓴다**(1 ft = 304,800 µm, 1 m = 1,000,000 µm). 매뉴얼 예시(`pos 0, 0, 3` = 3 ft)와는 다르다. 서버 모델에서도 mdf 헤더(`distance units Micrometers`)를 확인할 것.
- 시각(time)은 `time seconds` 기준이 그대로 적용됐다 [실험].

### 3.3 시간 문 (Time)
| 문 | 의미 | 출처 |
|---|---|---|
| `time <n> [단위]` | 시뮬레이션 시각 n에 아래 문장 실행. 값은 증가해야 함 | [H:Reference\time_stamp.htm] |
| `time relative <n>` | 앞 시각 + n | [H:Reference\relative_time_stamp.htm] |
| `jump to time <n>` | 그 시각으로 즉시 이동(앞뒤 모두 가능). 기록 안 된 구간 건너뛰기용 | [H:Reference\time_jump.htm][실험 s1] |
| `time scale <x>` | 재생 속도 배수. 앞 시각 문 없이도 쓸 수 있음 | [H:Reference\time_scale.htm] |
| `wait for <n>` | n만큼 화면 정지. **동영상에서는 무시됨**("Found 'wait for' in script. Ignored when making movie.") | [H:Reference\wait.htm][BIN:aview64] |
| `pause` | 'p' 키를 누를 때까지 정지. **무인 실행 금지** | [H:Reference\pause.htm] |
| `repeat` | 처음부터 무한 반복. **무인 실행 금지** | [H:Reference\repeat.htm] |

- 시각이 줄어들면 경고를 내고 점프로 처리한다: "Decreasing Time Stamp. Treating like a jump to: …" [BIN][실험].

### 3.4 표시 문 (Display)
| 문 | 의미 |
|---|---|
| `perspective` / `orthogonal` | 원근 / 평행투영(2D 평면도에 적합) [H:Reference\perspective.htm, orthogonal.htm] |
| `solid` / `wire` | 면 / 와이어프레임 [H:Reference\solid.htm, wire.htm] |
| `grid on` / `grid off` | 그리드 [H:Reference\grid_on_off.htm] |
| `window size full screen` | 창을 전체 화면으로. 동영상 크기는 movie spec이 정함 [H:Reference\window_size.htm] |
| `turn system <SYS> picture <PIC> on\|off\|invisible\|standard\|template` | 그림 요소 켜기/끄기 [H:Reference\turn_picture.htm] |
| `window color <색>` | 배경색(도움말에 없음, 문법표에 있음) [BIN `WINDOW COLOR color_value`][실험: `window color black` 동작] |
| `playsoundfile "<파일>"` | 소리 재생(문법표만, 미검증) [BIN] |

- PICTURE 이름: AGVS는 `Text Direction Paths Points Transfers Vehicles`, 컨베이어는 `Text Direction Loads Sections Stations Photoeyes Transfers`, 프로세스는 `Text Blocks Labels Resources Queues`이고 기본 스크립트에는 `Path_Names Queue_Names …`도 있다. 시스템 전체는 `System` [H:Popup\picture*.htm][UG p.678–679].
- **[실험]** `turn system agvdemo picture Labels off`(라벨 문구 숨김), `turn system agv picture Paths off`는 동작했다. 프로세스 시스템 이름을 `agvdemo~`(물결표 포함)로 쓰면 **구문 오류**가 났다. ASH 모델도 프로세스 시스템이 `<모델>~`이므로 `turn system <모델> …`로 쓴다(추정, agvdemo와 같은 구조).
- 빌드할 때 꺼 둔 요소는 AutoView에서 다시 켤 수 없다. 모델에서는 다 켜 두고 스크립트에서 끈다 [H:Reference\faq_visible_entities.htm].

### 3.5 뷰 문 (View)
```
view <저장뷰이름>                                   ← .vdf의 뷰(TOP 등) [H:Reference\saved_view_syntax.htm]
view focus relative to <REF> [pos x, y, z]
     camera relative to <REF> [pos x, y, z] [upvector x, y, z] scale <s>
pan [to] [next] [view]                             ← 이 시각부터 다음 view 문의 시각까지 부드럽게 이동
message "<문자열>"                                  ← AutoView 정보줄에만 표시, 동영상에는 안 나옴
```
- REF(참조): `world` | `object <SYS:이름(n)>` [`at tcf <TCF>`] | `temp <로드ID>` | `path <SYS:경로>` | `system …` | `container …` [H:Reference\ref.htm][BIN `reference : WORLD | system_ref | object_ref [AT tcf_id] | blob_id | temp_id | container_id | path_id`]
- `focus` = 바라보는 점, `camera` = 카메라 위치 [H:Reference\view_definition.htm].
- `scale` > 1이면 확대, 0–1이면 축소 [H:Reference\scale_number.htm]. 도움말은 "camera scale does not result in zooming"이라고 쓰지만, **실험에서는 scale이 확대 배율 그대로 동작했다**(scale 2 → 정확히 2배, t10/t13).
- `upvector`는 화면 위쪽 방향이다. 시선과 평행하면 안 된다. 바로 위에서 내려다보는 평면도는 `upvector 0, 1, 0`(+Y가 화면 위) [H:Reference\view_file.htm][BIN `UPVECTOR vector`].
- `pan`은 고정(world) 뷰 사이에서 잘 동작한다. 움직이는 물체 기준 뷰로 pan하면 예측하기 어렵다고 도움말에 적혀 있다 [H:Reference\pan.htm]. 이동은 부드러운 가감속으로 보인다(문자열 `EaseInEaseOut`, `TransitionQuaternion`, 추정).
- 저장 뷰는 aview64가 모델을 처음 열 때 `.vdf`에 생긴다. 아직 없는데 `view TOP`을 쓰면 첫 실행에서 "Undefined: TOP" 오류가 난다 [실험]. **ASH는 저장 뷰 대신 위의 인라인 `view focus … camera …`를 쓰는 것이 안전하다.**

### 3.6 동영상 문 (Movie spec / Movie)
```
define movie spec
  format mp4 | avi | custom
  filename "<경로\이름>"        ← 확장자 자동. 경로 포함 1024자 미만
  width <px>   height <px>      ← 기본값: AutoView 창 크기
  quality <0-100>
  framespersec <n>              ← 기본 30
  keyframefreq <n>              ← 기본 20
  saveframedir "<폴더\>"         ← 따옴표 필수. 끝에 \ 또는 /. MP4 기본값 "C:/autoviewtemp/"
  saveframes true|false         ← 프레임 JPG를 남길지
  append true|false             ← 같은 이름 파일에 이어 붙이기
end movie spec
record movie    pause movie    generate movie
```
[H:Reference\AVI_movie_specification.htm, MP4_movie_specification.htm, record_movie.htm, pause_movie.htm, generate_movie.htm][BIN `movie_init_statement`]
- `generate movie`가 없으면 애니메이션이 끝날 때 만든다 [H:Reference\generate_movie.htm].
- **스크립트 하나에 동영상은 하나만** 만들 수 있다: "Only one MOVIE may be created in a camera script." [BIN]
- 도움말에 없는 단축형도 있다(미검증): `mp4 <파일> <가로> <세로> <폴더> true|false`, `record mp4`, `pause mp4`, `generate mp4` [BIN].
- saveframedir 폴더가 이미 있으면 "제거하겠다"는 경고 창이 뜬다(문자열, 추정 동작) [BIN:xvt]. 잡마다 새 폴더를 쓴다.

---

## 4. 뷰 좌표 계산법 (모델 좌표의 사각형 → 뷰 값)

검증한 방식은 **평행투영(orthogonal) 평면도**다.

```
orthogonal
...
view focus relative to world pos CX, CY, 0 camera relative to world pos CX, CY, Z upvector 0, 1, 0 scale S
```
- `CX, CY` = 사각형 중심(µm). `Z` = 모든 물체보다 높은 값(예: 30,480,000 µm = 100 ft). 평행투영에서는 화면 크기에 영향이 없다 [실험 t11–t13].
- **보이는 범위는 `scale`로만 정해진다.** 동영상의 짧은 변 방향으로 보이는 길이는 다음과 같다.
  `보이는 길이(짧은 변) = F0 / S`, 긴 변 = 짧은 변 × (긴 변 px / 짧은 변 px)
  - F0는 **장면마다 정해지는 값**이다(보이는 시스템과 도면에 따라 달라짐, 10.4). F0를 제대로 정하면 10장 공식의 오차는 1% 미만이다. agvdemo에서 측정한 F0는 **264–277 ft**였다(t13: 720p에서 2.73 px/ft, t14: 800×800·scale 2에서 6.0 px/ft, t15: 1280×400·scale 2, m1: scale 6). 10장의 정밀 측정값은 **267 ft**다.
  - ~~F0 ≈ DRAWPOS 3차원 대각선~~은 **틀렸다**(우연히 비슷했을 뿐). `.avw`에 복사된 model.amo의 DRAWPOS는 0이다. **F0는 화면에 보이는 장면 전체(정적 도면 포함)의 3차원 경계 상자 대각선 × 약 1.17–1.21**이고, 시스템을 끄거나 도면을 더하면 바뀐다 [실험, 10.4].
  - 출력 크기(1280×720, 800×800, 1280×400)를 바꿔도 짧은 변 기준 F0는 같았다. 화면 중앙은 항상 정확히 (CX, CY)였다 [실험].
- 사각형 w×h(모델 단위)를 W×H px(W ≥ H)에 여백 m(예: 0.1)을 두고 담으려면:
  ```
  필요 짧은 변 길이 L = max(h, w·H/W) · (1+m)
  S = F0 / L
  CX = (xmin+xmax)/2 · (µm 환산), CY = (ymin+ymax)/2 · (µm 환산)
  ```
  예(m1 실험): 사각형 A = x −10…45, y −35…5 ft(55×40), 1280×720 → L = max(40, 30.9)·1.1 = 44 ft → S = 266/44 ≈ 6.05. CX = 17.5 ft = 5,334,000 µm, CY = −15 ft = −4,572,000 µm.
- 정확도는 ±5% 정도였다. 첫 사용 전에 모델마다 **보정 컷 1장**을 권한다: scale 1, 1프레임을 렌더해 아는 두 점의 픽셀 거리로 F0를 잰다.
- 원근(perspective): 카메라 높이를 100 ft에서 200 ft로 바꿔도 화면 배율은 거의 같았다. 원근 왜곡 정도만 달라졌다(t11/t12). 즉 원근에서도 확대는 scale로 한다. 10장에서 확인했다: 초점(focus) 평면의 배율이 F0/S로 고정되고, 카메라 거리 D는 원근 왜곡만 바꾼다. 기울인 시점의 공식과 실측은 **10장**에 있다.
- 기본 TOP 뷰(.vdf)는 모델 전체가 들어오게 만들어진다. 카메라 = 중심 + Z 549 ft, scale 1 [실험 .vdf].

---

## 5. 배속·프레임·영상 크기

### 5.1 동영상 모드의 시간 규칙 [실험, 문서와 다름]
| time scale | fps | 시뮬레이션 구간 | 프레임 수 |
|---|---|---|---|
| 0.6667 | 15 | 20 s | 60 (t1) |
| 1 | 30 | 20 s | 40 (t2) |
| 2 | 15 | 20 s | 20 (t3) |
| 1.3333 | 15 | 355 s | 534 (m1) → 35.6 s 동영상 = 9.97배 |

→ **프레임 1장 = 시뮬레이션 0.5 × time scale 초.** fps와는 무관하다. 0.5는 AutoView의 기본 "display step"으로 보인다(키 `d`/`D`/`u`로 바꾸는 값, 문자열 `u)pdate display step`, 추정).
```
배속(시뮬레이션 초 / 영상 초) = 0.5 × time_scale × fps
time_scale = 배속 / (0.5 × fps)        예) 10배, 15 fps → 1.3333 / 10배, 30 fps → 0.6667
영상 길이(초) = 녹화한 시뮬레이션 구간 / 배속
```
- 문서 공식("time scale ÷ fps", [H:Procedure\Equalizing_the_movie_and_animation_length.htm][UG p.689])으로 계산하면 이 버전에서는 **2배 느린** 영상이 나온다.
- `pause movie` 구간도 애니메이션 시간은 흐른다. `aviewout.dat`의 "Camera script duration"은 (마지막 time − 첫 time) / time scale이다 [실험].
- `-c` / `-s`(실시간 동기) 옵션은 화면 재생용이다. 동영상 생성은 실시간보다 빨랐다: 534프레임 720p를 28.4 s에 만들었다(약 19 fps, 이 PC) [실험]. 동영상 모드에서 -c/-s가 영향을 주는지는 미검증이다.

### 5.2 크기·화질
- MP4 [실험][BIN:xvt]: 각 프레임을 `saveframedir\amov<N>.jpg`(width×height로 오프스크린 렌더, 720p에서 약 45 KB/장)로 저장한다. 그다음 작업 폴더(cwd)에 `convert.bat`을 쓰고 실행한다:
  `ffmpeg -framerate <fps> -i <dir>\amov%d.jpg -s WxH -force_key_frames expr:gte(t,n_forced*<keyframefreq>) -y -crf <C> <파일>.mp4`
  - 측정된 quality → crf: 100→1, 80→10, 50→25, 20→40. 즉 **crf = max(1, (100−quality)/2)**.
  - 결과: h264, yuvj420p, 지정한 fps. 1280×720·quality 80·15 fps·35.6 s = **1.56 MB**(m1_cuts.mp4).
  - `append true`이면 `concatfileslist.txt`와 `ffmpeg -f concat -safe 0 … -c copy`로 이어 붙인다 [BIN:xvt](미검증).
  - **ffmpeg가 PATH에 있어야 한다** [H:Reference\Downloading_the_FFmpeg_framework.htm](지원판 4.2.x [RN]). 이 PC는 WinGet ffmpeg로 동작했다.
- AVI [실험]: 확인 창 다음에 Windows **"비디오 압축"(VfW 압축 프로그램 선택) 대화상자**가 매번 뜬다(`AVIOut64.dll`의 `AS_CompressOptsDlg`). 기본 코덱은 Microsoft Video 1(msvideo1)이다. AutoView **창 화면을 캡처**하는데(창이 1280×720으로 바뀜, `AS_AddFrame_hWnd`), 결과는 **빈 회색 화면**이었다(a1_avi_f30.jpg). 릴리스 노트의 알려진 문제와 같다: "hardware acceleration → 유효하지 않은 AVI" [RN 382688][UG p.688]. **→ AVI는 쓰지 않는다. MP4만 쓴다.**

---

## 6. 동영상 만들기

### 6.1 대화형(GUI)
aview64 → Model > Open → `<모델>.avw\<모델>.mdf` → Model > Edit Script로 camera.des 편집 → 저장하면 자동 재생된다. 동영상 spec이 있으면 확인 창이 뜨고 OK를 누른다 [UG p.674–677, 688–689]. 저장 뷰는 화면을 맞춘 뒤 `l`(소문자 L)을 눌러 만든다. scale 대신 Translate Z를 쓰라고 권한다 [H:Procedure\Using_saved_views.htm].

### 6.2 명령줄
```
cd <모델 폴더>          ← <모델>.avw 가 있는 폴더
aview64 -n <모델>       ← usage: "aview [-Lnsc] Model-Name" [BIN]
```
- `-n`: 스크립트가 끝나면 종료한다(실험: exit 0) [H:Overview\AutoView_command_line_options.htm].
- `-c`: 실시간 동기 해제. `-s`: 실시간 동기(기본값).
- `-L`: 문서에 없다(런타임의 "solids off"와 같을 것으로 추정 [RT]).
- 스크립트는 항상 `<모델>.avw\camera.des`에서 읽는다. 다른 파일 이름을 넘기는 옵션은 없다 [BIN/실험].
- 출력: `aviewout.dat`(파싱 로그, "Camera script duration is N seconds"), `aviewerr.dat`(스크립트 오류 줄 번호). 둘 다 cwd에 생긴다 [실험]. 스크립트 오류가 나도 종료 코드는 0이었다. **aviewerr.dat를 반드시 검사한다.**

### 6.3 무인 실행 — 가능하지만 대화상자 자동 클릭이 필수
이 PC에서 사용자 클릭 없이 끝까지 돌았다. 단 다음 창을 프로그램으로 눌러야 했다(`tools\run_aview.ps1`가 Win32 `BM_CLICK`으로 "OK"를 누름. ASH `DialogCloser`와 같은 방식).

| 창 제목 | 내용 | 언제 |
|---|---|---|
| `Attention!` | "The specified grid has over 1M lines…" | 모델 로드 시(모델/도면 한계 의존). 스크립트의 `grid off`로는 막히지 않음 |
| `Attention!` | "Grid or Minor line spacing value too small…" | 위와 같음 |
| `Attention!` | "This camera script specifies that a movie should be generated… Press OK to continue…" **OK/Cancel** | **동영상이 있는 모든 실행** |
| `비디오 압축` | VfW 코덱 선택(버튼 `확인`) | AVI만 |

추가 위험과 조건:
- **NoDefaultCurrentDirectoryInExePath 환경변수**: 이 변수가 설정돼 있으면 AutoView 내부의 `convert.bat` 실행이 "'convert.bat'은(는) … 아닙니다"로 실패하고 MP4가 만들어지지 않는다. 오류도 내지 않는다. Claude Code 셸이 이 변수를 켜 두어서 실제로 겪었다 [실험]. 워커 프로세스 환경에서 이 변수를 제거한다.
- **작업 폴더 충돌**: `convert.bat`, `aviewout.dat`, `aviewerr.dat`는 cwd에, 스크립트는 고정 이름 `camera.des`에 생긴다. → 잡마다 별도 폴더(.avw 사본)와 별도 cwd를 쓴다(추정).
- **창 상태**: MP4는 오프스크린 렌더라 **창을 최소화해도 정상 프레임이 나왔다**(h1_minimized) [실험]. 도움말의 "다른 창이 덮으면 녹화된다" 경고 [H:Procedure\Creating_a_movie.htm]는 AVI/custom(창 캡처)에 해당하는 것으로 보인다(추정).
- **서비스/세션 0(로그인 없는 실행)**: 미검증. HOOPS 렌더러(OpenGL 계열 추정)가 대화형 데스크톱 없이 동작할지 알 수 없다. 서버에서 작업 스케줄러 "사용자의 로그온 여부에 관계없이 실행"으로 실제 확인해야 한다(검은 프레임·멈춤 여부). 대화상자 자동 클릭은 같은 세션 안에서만 된다.
- 시간 제한과 강제 종료를 둔다(`pause`, `repeat`, 미처리 대화상자에 대비).

### 6.4 여러 컷 만들기와 이어 붙이기
- 방법 A(권장): **컷 1개 = aview 실행 1회 = MP4 1개**. 스크립트당 동영상은 하나뿐이고 [BIN], 컷별로 파일을 다시 쓸 수 있어 관리가 쉽다. 실행 한 번에 2–5 s 정도 든다(이 모델 기준, 로드 약 2 s 포함).
- 방법 B: 실행 한 번에 `record movie` → `pause movie` → `jump to time` → `record movie` …로 여러 구간을 **파일 하나**에 담는다 [실험 s1_segments: 400–420 s, 600–620 s 두 구간 → 64프레임]. 컷 사이 경계 효과(전환·자막)는 넣을 수 없다.
- 이어 붙이기 [실험]: 같은 크기·fps·코덱이면 재인코딩 없이 붙는다.
  `ffmpeg -f concat -safe 0 -i list.txt -c copy out.mp4` (list.txt: `file 'a.mp4'` 줄들) → m1_cuts(534) + s1_segments(64) = 598프레임, 39.87 s.
- 자막/제목은 AutoView `message`가 동영상에 나오지 않으므로 [H:Reference\message.htm] ffmpeg `drawtext`로 나중에 넣는다(제안).

---

## 7. 실험 결과 (이 PC)

모든 파일 위치: `C:\Users\ioi\AppData\Local\Temp\claude\C--Users-ioi\f1b6fa66-4240-52ee-87fb-e5a0bce80e7e\scratchpad\autoview\`

| 단계 | 결과 |
|---|---|
| 모델 | 데모 `agvdemo`를 `exp\agvdemo.arc`로 복사. CONTROL을 0.1 h(기록 안 함) + 0.1 h(autoview)로 수정 |
| 빌드 | `am264 -bq agvdemo` → "Entity Limit Status"(체험판, 124/200) OK 클릭 후 성공 |
| 실행 | `agvdemo.exe -w` → 대화상자 없음. `.avw` 생성(mdf 107,746 B, 360–720 s) |
| aview 첫 실행 | `.vdf` 생성. 저장 뷰 `TOP` 미정의 오류(첫 회만) |
| MP4 기본(t1) | `exp\t1_top.mp4` 1280×720, 15 fps, 60프레임(ts 0.6667) — 처음에는 NoDefaultCurrentDirectoryInExePath 때문에 convert.bat 실패, 수동 실행으로 생성 |
| 배속 측정(t2, t3) | 프레임 = 구간/(0.5·ts) 확인 |
| 좌표·배율 보정(t4–t15) | 좌표는 µm, scale = 확대, F0 ≈ 266 ft |
| **본 실험 m1** | `exp\m1_cuts.mp4`: 360–560 s 사각형 A 평면도(10배, 20 s) → 560–580 s pan(2 s) → 580–640 s 사각형 B → 640–715 s 차량 추적 시도. 1280×720, 15 fps, 534프레임, 35.6 s, 1,562,353 B, 생성 28.4 s. 키프레임 모음 `exp\m1_contact.jpg` |
| 차량 추적(f1–f3, g1–g4) | `view focus relative to object agv:DefVehicle(1)`(및 `…$txf`, `DefSegment(1)`)은 오류 없이 파싱됐지만 **차량이 화면 중앙에 오지 않았다**. 시점이 엉뚱한 곳으로 튀었다(`exp\f_contact.jpg`, `exp\g_contact.jpg`). 카메라를 차량 기준(`camera relative to object …`)으로 두면 회전·비스듬한 화면이 됐다 |
| 여러 구간(s1) | record/pause/jump/record → 한 파일, 64프레임. `saveframes false`면 임시 폴더 삭제 |
| 최소화 창(h1) | MP4 정상 |
| AVI(a1) | 비디오 압축 대화상자 → msvideo1, 62프레임, **빈 화면** |
| 화질(q100/q50/q20) | crf 1/25/40, 크기 74/35/17 KB(1초 미만 클립) |
| 표시(l2) | Labels off, Paths off, window color black 동작. `agvdemo~`는 구문 오류 |
| 이어 붙이기 | `exp\concat_test.mp4` 598프레임 |

도구 스크립트: `tools\run_aview.ps1`(aview 실행 + 대화상자 자동 OK + 시간 제한), `tools\render.ps1`(camera.des 생성 + 실행 + ffprobe), `tools\dlg.ps1`/`w32.ps1`(창 목록·버튼 클릭), `tools\h2t.py`, `tools\pdf2t.py`, `tools\strs.py`.
실험이 끝난 뒤 aview64/agvdemo/ffmpeg 프로세스는 남아 있지 않다.

---

## 8. 알 수 없는 점 / 서버에서 확인할 것

1. **세션 0(서비스, 로그온 없음)에서 MP4 렌더가 되는지**: 프레임이 정상인지, 검은 화면인지, 멈추는지. 대화상자 자동 클릭이 그 세션에서 동작하는지.
2. 라이선스판 서버에서 aview64가 라이선스·인증 창을 띄우는지. 이 PC에서는 없었다.
3. ASH 모델에서도 "grid over 1M lines" 경고가 뜨는지(DRAWPOS·그리드 간격 의존). 뜨지 않게 할 방법(모델의 그리드 설정)이 있는지.
4. 확인 창("This camera script specifies that a movie…")을 끄는 설정이 있는지. 문자열 `AutoMovieEnabled`, `ASIOPTIONS`, `AMOVIE_SAVE`가 보이지만 의미는 미확인. 없으면 자동 클릭으로 간다.
5. 좌표 단위가 서버 모델에서도 µm인지(mdf 헤더). F0 보정 컷(10.4)이 ASH 모델에서도 같은 방식으로 동작하는지. 첫 추정식(1.19 × 3D 대각선)이 얼마나 맞는지.
6. display step 0.5 s가 고정인지(다른 PC나 설정에서 바뀌면 배속 공식이 바뀜). 컷마다 `aviewout.dat`의 duration과 ffprobe 프레임 수로 검증하는 것을 권한다.
7. 물체 기준 추적(`relative to object` / `temp` / `at tcf`)이 다른 TCF 이름이나 다른 차량 유형에서는 동작하는지. 이 실험에서는 실패했다.
8. 8시간 전체 기록 시 mdf 크기와 aview 로드 시간(이 모델 6분 = 5,905 update 레코드, 로드 약 2 s).
9. `append true`(MP4 이어 붙이기), `mp4 …` 단축 문법, `-c`/`-s`가 동영상에 주는 영향은 미검증.
10. 서버의 ffmpeg 버전(지원판 4.2.x [RN])과 PATH. 워커 환경에 `NoDefaultCurrentDirectoryInExePath`가 없는지.

---

## 9. 컷 4종 레시피

공통 머리말(1280×720, 15 fps, 배속 k). `time scale = k / 7.5`:
```
camera description
distance feet
time seconds
angle degree
time scale <k/7.5>
orthogonal
solid
grid off
turn system <모델> picture Labels off      // 필요 시
define movie spec
format mp4
filename "<job폴더>\cut01"
width 1280
height 720
quality 80
framespersec 15
keyframefreq 30
saveframedir "<job폴더>\frames01\"
saveframes false
end movie spec
```
`V(r) = view focus relative to world pos CX, CY, 0 camera relative to world pos CX, CY, 30480000 upvector 0, 1, 0 scale S` (4장 공식, 좌표 µm)

**(1) 고정 사각형 r, 구간 [t0, t1], 배속 k**
```
time t0
V(r)
record movie
time t1
generate movie
```
영상 길이 = (t1−t0)/k 초. 전제: 그 구간이 AutoView 기록 snap 안에 있어야 한다.

**(2) A에서 N초 → M초 동안 이동 → B에서 L초 (영상 초 기준, 배속 k)**
```
time t0
V(A)
record movie
time t0 + N·k
pan to next view
time t0 + (N+M)·k
V(B)
time t0 + (N+M+L)·k
generate movie
```
(m1 실험 구조와 같다. pan은 위치와 scale을 함께 보간한다.)

**(3) 전체 레이아웃 / 병목 구간**
- 전체: r = DRAWPOS 사각형(또는 `view TOP`. 단 .vdf가 먼저 있어야 함). 레시피 (1)과 같다.
- 결과에서 고른 구간 여러 개:
  - 컷마다 레시피 (1)로 따로 실행하고 ffmpeg concat으로 잇는다(권장).
  - 또는 실행 한 번에 `record movie … pause movie … jump to time t_k … V(r_k) … record movie … generate movie`.
- 기록: 해당 구간이 전부 autoview snap에 들어가야 한다. 전체 실행을 기록하거나, 결과를 보고 구간 주변만 기록하도록 CONTROL 줄을 나눠 재실행한다.

**(4) 근접 추적 a → b (차량·로드 따라가기)**
- 문서상 방법은 `view focus relative to object agv:DefVehicle(n) camera relative to object … pos …`, 로드는 `temp <ID>`다 [H:Reference\ref.htm, view_definition.htm]. 하지만 **이 PC 실험에서는 추적이 맞지 않았다.** 쓰려면 서버에서 재검증해야 한다.
- 대안(추정, 미검증): 대상의 궤적 (t, x, y)를 얻는다. ASH 로직이 일정 간격으로 차량 좌표를 파일에 쓰게 하는 방법이 가장 쉽다. mdf는 경로 기준 위치라 경로 기하(.asy)와 합성해야 한다. 그다음 1–2 시뮬레이션 초 간격의 키프레임 world 뷰를 `pan to next view`로 잇는다.
  ```
  time t_i
  V(차량 위치 i, 작은 사각형)
  time t_i + ε
  pan to next view
  time t_{i+1}
  V(차량 위치 i+1)
  ...
  ```
  고정 world 뷰 사이의 pan은 동작을 확인했다(m1).

---

## 10. 기울인 시점 (2026-10-23 실험)

실험 파일: `scratchpad\autoview\exp2\`(모델 = agvdemo + 11장의 정적 도면 `drw`). 도구: `tools\tilt.ps1`(뷰 계산 + 렌더), `tools\measure.py`(픽셀 측정), `tools\build_run.ps1`(빌드 + 기록). 모든 컷은 1280×720, 시각 400 s, 2프레임이다.
측정 대상은 도면으로 넣은 사각형 A(x −10…45, y −35…5 ft, 55×40 ft)다. 바닥 테두리(z=0)는 빨강, 높이 10 ft 상자 테두리(윗면 + 세로 모서리)는 노랑이다. 도면을 켠 프레임과 `turn system drw picture System off`로 끈 프레임의 차이에서 도면 픽셀만 뽑았다. 그래서 모델의 다른 빨간 물체(작업자 머리 등)가 섞이지 않는다.

### 10.1 기호와 카메라 위치 [실험]
| 기호 | 뜻 |
|---|---|
| r | 담을 사각형 x∈[xmin,xmax], y∈[ymin,ymax]. 중심 (cx, cy), w = xmax−xmin, h = ymax−ymin (모델 단위) |
| Hz | 화면에 함께 담을 높이(차량·랙 높이 등). 바닥 선만이면 0 |
| θ | **기울기**: 수직에서 잰 각. 0 = 바로 위에서 본 평면도. (고도각 α = 90°−θ) |
| φ | **시선 방위**: 0 = 북쪽(+Y)을 보며 +Y가 화면 위, 90 = 동쪽(+X)을 보며 +X가 화면 위 |
| D | 초점에서 카메라까지 거리(모델 단위) |
| W×H | 영상 픽셀(W ≥ H) |

```
시선 d     = ( sinθ·sinφ,  sinθ·cosφ, −cosθ)
화면 오른쪽 = ( cosφ,       −sinφ,      0   )
화면 위     = ( cosθ·sinφ,  cosθ·cosφ,  sinθ)
focus  F   = (cx, cy, Hz/2)
camera     = F − D·d = (cx − D·sinθ·sinφ,  cy − D·sinθ·cosφ,  Hz/2 + D·cosθ)
upvector   = θ = 0 이면 (sinφ, cosφ, 0),  θ > 0 이면 (0, 0, 1)      ← 둘 다 확인
```
- 스크립트에는 µm로 쓴다(4장: 1 ft = 304,800 µm, 1 m = 1,000,000 µm).
- focus를 높이의 절반(Hz/2)에 두면 높이가 있는 내용이 화면 위아래로 고르게 놓인다.
- 원근(perspective)에서는 **초점 평면의 배율이 F0/S로 고정**되고, 카메라가 가까운 점은 D / (그 점의 깊이)배로 커진다(핀홀). D는 원근 왜곡 정도만 바꾼다. 시험한 D는 60/100/200 ft다. 가장 가까운 60 ft는 대상(55×40×10 ft)의 가장 긴 변보다 조금 큰 정도이고, 이때도 공식이 맞았다. 그보다 가까운 D는 미검증이다. 평행(orthogonal)에서는 D가 화면에 영향을 주지 않는다. 다만 장면 앞쪽이 카메라 뒤로 가지 않도록 D를 장면보다 크게 둔다(추정).

### 10.2 scale 공식 [실험]
사각형의 8개 모서리 P(4개 모서리 × z ∈ {0, Hz})마다 다음을 계산한다.
```
v = P − F
f = 1                       (orthogonal)
f = D / (D + v·d)           (perspective)
X = f·(v·화면오른쪽),  Y = f·(v·화면위)
ax = max|X|,  ay = max|Y|
L  = max(2·ay, 2·ax·H/W) · (1 + m)           m = 여백 비율(예: 0.1)
S  = F0 / L                                   F0 = 그 장면의 기준 시야(10.4)
```
- 평행투영 닫힌식: `2·ax = |w·cosφ| + |h·sinφ|`, `2·ay = (|w·sinφ| + |h·cosφ|)·cosθ + Hz·sinθ`.
  → 화면 세로 방향 크기는 **h·cosθ + Hz·sinθ**로 줄어든다(φ=0). 고도각 α로 쓰면 h·sinα + Hz·cosα. θ=0이면 4장 공식과 같다.
- 확인용 픽셀 변환: 1 모델 단위 = `H·S/F0` px. 점의 화면 좌표 = (W/2 + H·S/F0·X, H/2 − H·S/F0·Y).
- 원근에서는 가까운(화면 아래쪽) 변이 커지므로 위의 대칭 공식이 아래쪽 여백을 m에 맞추고 위쪽은 더 남긴다. focus를 옮겨 가운데로 맞추는 방법은 검증하지 않았다.

예(평행투영, 사각형 A, Hz = 10 ft, θ = 45°, φ = 0, D = 100 ft, m = 0.1, F0 = 267 ft. focus z = Hz/2 = 5 ft = 1,524,000 µm):
`2·ax = 55`, `2·ay = 40·0.7071 + 10·0.7071 = 35.36`, `L = max(35.36, 55·720/1280 = 30.94)·1.1 = 38.89 ft`, `S = 267/38.89 = 6.865`
```
orthogonal
view focus relative to world pos 5334000, -4572000, 1524000 camera relative to world pos 5334000, -26124615, 23076615 upvector 0, 0, 1 scale 6.8654
```
원근 같은 시점(공식이 S = 6.1372를 줌): `perspective` + 같은 view 줄에 `scale 6.1372`.
φ = 90°, θ = 60°(평행): `view focus relative to world pos 5334000, -4572000, 1524000 camera relative to world pos -21062454, -4572000, 16764000 upvector 0, 0, 1 scale 6.7125`

### 10.3 실측 표 [실험]
**(가) 투영 검증** — scale 4 고정, focus z = 0, D = 100 ft, 예측은 F0 = 268 ft로 계산. 측정 = 빨강+노랑 도면 픽셀의 경계 상자(px).

| 투영 | θ | φ | 예측 가로×세로 | 측정 가로×세로 | k = 측정/예측 (가로, 세로) |
|---|---|---|---|---|---|
| 평행 | 0 | 0 | 592×430 | 593×431 | 1.003, 1.003 |
| 평행 | 30 | 0 | 592×426 | 593×428 | 1.003, 1.005 |
| 평행 | 45 | 0 | 592×380 | 593×383 | 1.003, 1.008 |
| 평행 | 60 | 0 | 592×308 | 593×310 | 1.003, 1.007 |
| 평행 | 0 | 90 | 430×592 | 431×593 | 1.003, 1.003 |
| 평행 | 30 | 90 | 430×566 | 431×568 | 1.003, 1.004 |
| 평행 | 45 | 90 | 430×494 | 431×497 | 1.003, 1.006 |
| 평행 | 60 | 90 | 430×389 | 431×390 | 1.003, 1.004 |
| 원근 | 0 | 0 | 656×478 | 659×479 | 1.003, 1.003 |
| 원근 | 30 | 0 | 726×444 | 729×445 | 1.003, 1.003 |
| 원근 | 45 | 0 | 750×390 | 753×392 | 1.004, 1.005 |
| 원근 | 60 | 0 | 760×309 | 765×311 | 1.005, 1.008 |
| 원근 | 0 | 90 | 478×656 | 479×659 | 1.003, 1.003 |
| 원근 | 30 | 90 | 554×592 | 557×593 | 1.005, 1.003 |
| 원근 | 45 | 90 | 584×513 | 587×515 | 1.003, 1.004 |
| 원근 | 60 | 90 | 604×397 | 605×399 | 1.002, 1.006 |
| 원근 D=200 | 45 | 0 | 662×384 | 663×386 | 1.003, 1.006 |
| 원근 D=60 | 45 | 0 | 914×403 | 917×404 | 1.003, 1.003 |

- 위치도 맞았다. 예: 원근 θ45 φ0의 바닥 사각형은 예측 x 296–984, y 227–537, 측정 x 293–984, y 226–539(가까운 변이 넓은 사다리꼴).
- k가 모두 1.002–1.008이다. 선 두께 3 px가 경계 상자를 2–3 px 넓히는 것과 F0 = 267(268 대신)로 설명된다. → **공식은 정확하다. 보정 계수 k(θ) = 1.00**.

**(나) 공식으로 맞춘 컷의 여백** — `-Fit`, F0 = 267, m = 0.1, Hz = 10 ft, focus z = 5 ft. 목표 여백은 세로가 꽉 찰 때 위·아래 각 32.7 px(720·(1−1/1.1)/2), 가로가 꽉 찰 때 좌·우 각 58.2 px다.

| 투영 | θ | φ | D | S | 여백 좌/우/위/아래 (px) |
|---|---|---|---|---|---|
| 평행 | 0 | 0 | – | 6.068 | 189/191/33/31 |
| 평행 | 30 | 0 | – | 6.123 | 185/187/33/31 |
| 평행 | 45 | 0 | – | 6.865 | 130/132/33/31 |
| 평행 | 60 | 0 | – | 7.846 | **58/60**/57/55 |
| 평행 | 0 | 90 | – | 4.413 | 400/402/33/31 |
| 평행 | 30 | 90 | – | 4.612 | 390/392/33/31 |
| 평행 | 45 | 90 | – | 5.281 | 354/356/33/31 |
| 평행 | 60 | 90 | – | 6.713 | 277/279/33/31 |
| 원근 | 0 | 0 | 100 | 5.765 | 189/191/33/31 |
| 원근 | 30 | 0 | 100 | 5.776 | 139/141/68/31 |
| 원근 | 45 | 0 | 100 | 6.137 | 87/89/96/31 |
| 원근 | 60 | 0 | 100 | 6.291 | **58/60**/149/73 |
| 원근 | 0 | 90 | 100 | 4.193 | 400/402/33/31 |
| 원근 | 30 | 90 | 100 | 4.177 | 363/365/90/31 |
| 원근 | 45 | 90 | 100 | 4.441 | 328/330/123/31 |
| 원근 | 60 | 90 | 100 | 5.282 | 252/254/148/31 |
| 원근 | 45 | 0 | 200 | 6.501 | 110/112/66/31 |
| 원근 | 45 | 0 | 60 | 5.534 | **58/60**/136/38 |
| 원근 | 60 | 0 | 60 | 5.254 | **58/59**/197/89 |

- 모든 컷에서 네 모서리와 높이 10 ft 상자가 화면 안에 들어왔고, 꽉 차는 쪽 여백이 목표와 2 px 이내였다. 굵은 숫자는 가로가 꽉 찬 경우다.
- 원근은 위쪽(먼 쪽) 여백이 더 남는다(10.2 마지막 항목).
- 참고(사각형 A, φ=0, 평행): 평면도 대비 scale 배율 S(θ)/S(0)는 θ=30° 1.009, 45° 1.131, 60° 1.293이다. 모양·Hz에 따라 달라지므로 표 대신 10.2 공식을 쓴다.
- 프레임 모음: `exp2\tilt_contact.jpg`(12컷). 개별 프레임: `exp2\k_*_frames\amov0.jpg`, 원시 측정 `exp2\r_*_frames`.

### 10.4 F0(기준 시야)는 장면 크기로 정해진다 [실험]
F0는 aview64가 모델을 열 때 **보이는 장면 전체의 경계**로 정한다(문자열 `SetBaseFieldWidth` [BIN]). 4장의 "DRAWPOS 대각선"은 우연이었다.

| 장면 | 보이는 범위(가로×세로×높이) | 3D 대각선 | 측정 F0 | F0/대각선 |
|---|---|---|---|---|
| agvdemo 전체(+작은 도면) | 148.8×162.1×8.5 ft(평면·정면 렌더로 잼) | 220.2 | 267 | 1.21 |
| 작은 도면만(다른 시스템 `turn … off`) | 55×40×10 | 68.7 | 80.2 | 1.17 |
| 모델을 감싸는 큰 도면 300×260 ft + 모델 | 300×260×10 | 397.1 | 472.1 | 1.19 |
| 큰 도면만 | 300×260×10 | 397.1 | 472.1 | 1.19 |

- 스크립트 앞부분(첫 time 문 앞)의 `turn system … off`가 F0를 바꾼다(작은 도면만 켜자 3.3배 확대됨). 기존 경계 **안**의 정적 시스템 하나를 끄고 켜는 것은 영향이 없었다(10.3 측정, 아래 보정 컷 시험). 첫 time 문 뒤의 `turn`이 F0를 바꾸는지는 미검증이다.
- **도면이 모델보다 크면 F0가 커진다**(267 → 472). ASH는 도면을 넣은 모델로 F0를 다시 정해야 한다.
- "보이는 장면"에 들어가는 것: `turn … off` 하지 않은 모든 시스템의 그래픽이다. 경로·차량·컨베이어·큐·자원·**라벨 글자**·정적 도면이 모두 들어간다. 위 첫 줄의 범위에도 라벨 글자가 포함됐다. 그리드는 모든 실험에서 `grid off`였으므로 영향은 미검증이다. 움직이는 로드·차량이 나중에 경계 밖으로 나가도 F0는 그대로인 것으로 보인다(F0는 모델을 열 때 한 번 정함. m1 컷 360–715 s 동안 배율 일정, 추정).

**F0 정하는 법 — 기본: 보정 컷 [실험]**
1. ASH가 보정용 정적 시스템 `ash_cal`을 하나 더 쓴다(시험 때 이름은 `cal`). 내용은 장면 중심에 놓은 사각형 테두리 하나(변 Lc ≈ 장면 가로·세로의 절반, 11.2 형식, 색 무관)다. 장면 경계 안에 있으므로 끄고 켜도 F0가 변하지 않는다(시험: 큰 도면 장면에서 `ash_cal`을 꺼도 큰 도면 폭이 457 px로 같았음).
2. 실제 컷과 **같은 머리말(같은 `turn … off` 줄)**로 두 번 실행한다. 한 번은 그대로, 한 번은 `turn system ash_cal picture System off`를 더한다. 본문은 다음과 같다(1프레임이면 됨. CX, CY = 사각형 중심, µm).
   ```
   orthogonal
   time T
   view focus relative to world pos CX, CY, 0 camera relative to world pos CX, CY, 30480000 upvector 0, 1, 0 scale 1
   record movie
   time T+1
   generate movie
   ```
3. 두 `amov0.jpg`의 차이 픽셀(RGB 차이 합 > 90)의 경계 상자 가로 p(px)를 잰다. 그러면 **`F0 = H × Lc / p`**다(H = 영상 세로 px, Lc = 사각형 가로, 모델 단위). 세로로 재려면 사각형 세로 길이를 쓴다.
4. 이후 그 장면(같은 `turn` 설정)의 모든 컷은 이 F0를 쓴다. 실제 컷에는 `turn system ash_cal picture System off`를 넣는다.
- 시험 결과(`exp4\cal2_*`): 큰 도면 장면, Lc = 150×130 ft, p = 229 / 201 px → F0 = 471.6 / 465.7. 큰 도면 외곽선(300 ft ↔ 457 px)으로 잰 값은 472.6이었다. 선 두께 때문에 정밀도는 약 ±1.5%이고, p가 클수록 정확하다. Lc = 55 ft(p = 86 px)로 재면 460(−3%)이었다. 그래서 큰 사각형을 쓴다.
- 이 오차는 여백 m = 0.1 안에 들어간다. 10.3의 공식 오차는 F0가 맞으면 1% 미만이다.

**첫 추정(보정 전, 추정)**: `F0 ≈ 1.19 × (보이는 장면 경계 상자의 3D 대각선)`.
근거는 모델 1개(agvdemo)의 장면 4가지뿐이고, 계수가 1.17–1.21로 흔들렸다. 장면 범위도 렌더 프레임에서 잰 값이다(라벨 글자 포함). 잘림을 피하려면 계수 1.15를 쓴다. F0를 작게 잡으면 S가 작아져 여백이 늘어날 뿐이다. 보정 컷을 대신할 수는 없다.

---

## 11. 도면 배경(정적 그래픽) (2026-10-23 실험)

**결론: 된다.** AutoMod **정적 시스템(Static system)** 에 HOOPS 메타파일(HMF) 선을 넣으면 빌드·실행·AutoView MP4에 그대로 나온다 [실험: `exp2\d3_top_A.mp4`. MP4를 디코드한 프레임에도 도면 픽셀이 있음]. DXF를 직접 읽지는 않는다. ASH가 DXF 선을 HMF `Polyline`으로 바꿔 `.asy`를 쓰는 방식이다.

### 11.1 근거
- 매뉴얼: 정적 시스템은 움직이지 않는 그래픽(벽, 바닥, CAD 도면)을 담는다. "CAD 도면을 정적 시스템으로 읽어 들여 그 선 위에 모델을 만들 수 있다" [UG p.632–633 "Reading a graphic in a static system"].
- 14.x는 DWG/DXF/SKP/HSF/HMF/IGES/IV/WRL을 읽는다 [RN 414244, 416261][BIN:xvt "AutoCAD DWG (*.dwg, *.dxf)"]. 다른 도구로 만든 DXF는 64비트판을 죽게 할 수 있다는 알려진 문제가 있다 [RN 443348]. GUI의 Read 경로는 시험하지 않았다(대화형 편집기 필요).
- 설치된 데모 `demos\activexdemos\unload\unload.arc\truck.asy`에는 읽어 들인 HSF 그래픽이 **코드 `511` + 본문 HMF 텍스트**로 저장돼 있다. 정적 시스템 예: `demos\…\Molding\molding.arc\lsys1.asy`, `activexdemos\unload\unload.arc\building.asy`. 아래 형식은 이 저장 형식을 그대로 따른 것이다.

### 11.2 파일 형식 [실험]
**`<모델>.arc\drw.asy`** (CRLF로 썼고 동작함. 시험한 파일 `exp3\agvdemo.arc\drw.asy`(Meters판)에서 노란 상자 부분만 뺀 것)
```
SYSTYPE Static
UNITS Meters Seconds
SYSDEF
FLAGS
	System Inherit
STATICDEF UserId 9
 IND 0 display picpos endx 1
	template Meters
511 81
-3.048 13.716 -10.668 1.524 0 0
-1 -1 0 1 1 drw.dxf
;; HMF V1.25 TEXT
(Segment "" (
  (Line_Weight 3)
  (Segment "floor" ((Color_By_Value "Line" "RGB" 1 0 0) (Polyline ((-3.048 -10.668 0) (13.716 -10.668 0) (13.716 1.524 0) (-3.048 1.524 0) (-3.048 -10.668 0)))))
))
#HMF END
end
```
**`<모델>.arc\model.amo`**: 시스템 목록에 두 줄을 넣는다.
```
STATIC name drw
	SYSPOS endx 1
```
- 순서 [실험]: (a) `MOVESYS …` 뒤, `PROCSYS` 줄 **앞**(데모 assembly와 같은 순서)과 (b) `PROCSYS` 블록 **뒤**, 첫 `CONTROL` 줄 앞, 두 위치만 시험했고 둘 다 동작했다(exp2, exp4). 그 밖의 위치는 미검증이다. → **(a) 위치를 쓴다.**
- 이름: `STATIC name <이름>`과 파일 `<이름>.asy`가 짝이다. 같은 .arc 안의 다른 시스템 이름(ASH 모델이면 `agv` 등 이동 시스템과 `<모델>~` 프로세스 시스템)과 파일 이름이 겹치면 안 된다. 겹칠 때의 동작은 시험하지 않았다. ASH는 겹치지 않는 접두어를 쓴다(예: `ash_drw`, `ash_cal`). 시험한 이름은 `drw`, `drwbig`, `cal`(소문자·영숫자)이다. 밑줄이 든 이름은 설치 데모의 정적 시스템 `stat_bluehold`(Palletizer)에 있다.
- 도면 여러 장 [실험]: (1) 정적 시스템을 여러 개 둔다(`drwbig` + `cal` 두 시스템 동시 동작, exp4). 컷마다 따로 끌 수 있다. (2) 한 시스템 안에 `IND 0`, `IND 1` …로 여러 그래픽을 둔다(IND 0 = HMF, IND 1 = 셀 상자로 동작, exp2 d3). 끄기는 시스템 단위다. → 따로 켜고 끌 도면은 시스템을 나눈다.

줄별로 그대로 쓸 부분과 바뀌는 부분:
| 줄 | 그대로 | 바꿀 수 있음 |
|---|---|---|
| `SYSTYPE Static`, `SYSDEF`, `FLAGS`, `\tSystem Inherit` | 전부 | – |
| `UNITS <단위> Seconds` | `UNITS`, `Seconds` | 단위(Feet/Meters/Millimeters 확인) |
| `STATICDEF UserId 9` | `STATICDEF UserId` | 숫자: 데모는 8·9, 시험은 9와 3 모두 동작(저장해도 그대로 유지). 두 시스템이 같은 9여도 동작. 의미 미확인 |
| ` IND 0 display picpos endx 1` | ` IND`, `display picpos endx 1` | IND 번호(시스템 안 그래픽 순번 0, 1, …). `picpos`에 `begx/begy`(위치 이동)를 더할 수 있겠지만 미검증 → `endx 1`만 쓰고 좌표를 HMF에 직접 넣는다 |
| `\ttemplate <단위>` | `template` | 단위(HMF 좌표 단위가 됨) |
| `511 81` | **둘 다 그대로**(아래) | – |
| 경계 줄 | – | 아무 값(다시 계산됨) |
| `-1 -1 0 1 1 <이름>` | `-1 -1 0 1 1` | 이름(표시용) |
| `;; HMF V1.25 TEXT` … `#HMF END`, `end` | 머리·끝 줄 | HMF 본문 |

- **`511 81`의 81** [실험]: 줄 수나 바이트 수가 아니다(본문 7줄로 동작. 데모 truck.asy는 본문 9만 줄에도 81). 다른 셀 도형(`310 5`, `700 21`)처럼 **플래그 값**으로 보인다(추정). 17과 80을 쓰면 빌드는 되고 저장본(.avf)에서 81로 바뀌었다. **1을 쓰면 am264 빌드가 죽었다**(종료 코드 −1073741819, exp4). → 항상 `81`을 쓴다. 다른 값은 미검증이다.

규칙:
- **HMF 좌표는 `template` 단위**다. `template Feet` / `Meters` / `Millimeters` 세 가지 모두 같은 위치에 그려졌다(g_unit_*). ASH 모델 단위(Meters 또는 Millimeters)를 그대로 쓰면 된다. 처음에 µm로 썼더니 304,800배 커졌다. 그 결과 F0가 폭증해 모델이 화면에서 사라졌다(d1).
- 좌표는 월드 좌표 그대로다(`picpos endx 1` = 원점·회전 없음).
- `511 81` 다음 줄(경계 x최소 x최대 y최소 y최대 z최소 z최대)은 AutoMod가 HMF에서 **다시 계산해 저장한다**. 틀린 값(±1000)을 써도 결과가 같았다(g_bbox_wrong). 그래도 실제 범위를 쓰는 것을 권한다.
- 이름 줄 `-1 -1 0 1 1 <이름>`의 이름은 표시용이다.
- HMF 문법(확인한 것만): `(Segment "<이름>" ( … ))` 중첩, `(Polyline ((x y z) (x y z) …))`, `(Color_By_Value "Line" "RGB" r g b)`(0–1), `(Line_Weight n)`(화면 px 두께로 보임). 원·호·글자(`Circle`, `Circular_Arc`, `Text`)는 시험하지 않았다. → DXF의 ARC/CIRCLE은 선분으로 나눠 Polyline으로 쓴다.
- z: 바닥 선은 z = 0으로 그렸다. 평면도에서 높이가 있는 회색 상자(큐) 위로도 선이 보였다(d3). agvdemo에는 바닥판 그래픽이 없다. 그래서 **모델의 바닥판(z = 0 근처 면)이 z = 0 도면 선을 가리는지는 미검증**이다. 가리면 도면을 z를 조금 올려(예: +0.01 m) 그리는 것을 먼저 시험한다(추정).
- 셀 형식 도형(`310` 상자 등, 데모 .asy와 같은 형식)도 같은 정적 시스템의 `IND 1`로 넣어 보였다(d3의 파란 기둥).
- 체험판 엔티티 수(124/200)는 정적 시스템을 넣어도 그대로였다.

### 11.3 빌드·실행·AutoView [실험]
- `am264 -bq agvdemo` → `agvdemo.exe -w` → `.avw\agvdemo.avf\drw.asy`에 사본이 생기고 AutoView가 그린다.
- **주의**: `<모델>.dir`이 남아 있으면 `am264 -bq`가 바뀐 정적 `.asy`를 다시 읽지 않았다(옛 `drw.sys` 사용). 파일 시각이 더 새로워도 마찬가지였다. → **정적 `.asy`를 바꾼 뒤에는 빌드할 때마다 `<모델>.dir` 폴더를 먼저 지운다**(`tools\build_run.ps1`가 매번 지움. 지운 뒤 빌드는 이 PC에서 3–4 s).
- 카메라 스크립트에서 끄고 켜기: `turn system drw picture System off`(동작 확인). 컷마다 도면 표시를 고를 수 있다. 단 도면이 장면 경계를 정하고 있으면 끄는 순간 F0가 바뀐다(10.4).
- 도면 때문에 장면이 커지면 F0가 커진다. 10.4의 방법으로 다시 정한다.

### 11.4 Task 8용 한 줄 요약
1. DXF의 LINE/LWPOLYLINE/POLYLINE(ARC·CIRCLE은 선분화)을 모델 단위로 바꾼다(ASH Scale 적용).
2. `(Polyline ((x y 0) …))`로 모아 위 형식의 `ash_drw.asy`(`511 81`)를 쓴다.
3. model.amo의 PROCSYS 줄 앞에 `STATIC name ash_drw` / `\tSYSPOS endx 1`을 더한다.
4. `<모델>.dir`을 지우고 빌드한다.
5. F0는 10.4의 보정 컷(`ash_cal`)으로 정한다.

미검증: GUI의 DXF Read 결과가 같은 `511` 형식인지(추정: 데모 HSF와 같음). 선 수천 개짜리 도면의 빌드·로드 시간. 글자·원 HMF 요소. 서버(라이선스판)에서의 동작.
