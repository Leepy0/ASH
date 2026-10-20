# ASH — AGV Simul Helper

ZWCAD 플러그인과 실행기로 AutoMod AGV 수량 산출 업무를 자동화한다.

- 설계서: docs/superpowers/specs/2026-10-20-ash-plugin-design.md
- 1차 구현 계획: docs/superpowers/plans/2026-10-20-ash-core-and-layout.md

## 설치

1. `ASH-Setup-x.y.z.exe`를 받는다(빌드: `scripts/build-release.ps1` → `dist/`).
2. 실행한다(관리자 권한을 요청한다). 찾은 ZWCAD가 모두 선택된 상태로 나온다. 못 찾으면 "직접 지정…"으로 `ZWCAD.exe`가 있는 폴더를 고른다. "설치"를 누른다.
3. ZWCAD를 완전히 종료한 뒤 다시 시작하세요. 리본에 `ASH` 탭이 생기고 `AGV*` 명령을 쓸 수 있다.
4. 탭이 안 보이면 명령줄에 AGVRIBBON 또는 AGVPALETTE.
5. 첫 사용: 도면 저장 → AGVINIT. mm 도면이면 ash.settings.tsv의 Scale을 0.001로.

- 지원 버전: ZWCAD 2026만(`Directory.Build.props`의 `AshZwcadVersion`). 다른 버전은 목록에 "(미지원: ASH는 ZWCAD 2026용)"으로 선택 해제된 채 나오고, 선택해도 건너뛴다(`/silent`는 종료 코드 2).
- ZWCAD가 실행 중이면 설치·제거를 하지 않는다("ZWCAD를 닫고 다시 실행하세요.", `/silent`는 종료 코드 1).
- exe는 서명되지 않았다. Windows SmartScreen이 막으면 "추가 정보 → 실행".
- 제거에도 이 exe가 필요하다(아직 "프로그램 추가/제거" 항목 없음). exe를 지우지 말고 보관한다.

제거: `ASH-Setup-x.y.z.exe /uninstall` (창에서 "제거"). 무인 설치: `/silent`, 폴더 추가: `/dir:"D:\CAD\ZWCAD 2026"`. 로그: `%TEMP%\ASH-install.log`. 종료 코드: 0 성공, 1 실패(ZWCAD 실행 중 포함), 2 경고(자동 로드 등록 못 함 — ZWCAD를 한 번 실행한 뒤 다시 설치, 미지원 버전 건너뜀, SYSTEM 계정 등 사용자 하이브를 알 수 없음).
DLL은 `<ZWCAD 폴더>\ASH\`에 들어가고(쓰기 실패 시 `%ProgramData%\ASH\<버전>\`), 자동 로드는 `HKCU`·`HKLM\Software\ZWSoft\ZWCAD\<버전>\<언어>\Applications\ASH`에 등록된다.

서버 설치·릴리스 절차·서명 키 관리: [docs/server-install.md](docs/server-install.md)

업데이트: 플러그인은 시작할 때(3초 제한, 백그라운드) 사내 서버(`ASH.Server`)에 새 버전이 있는지 확인해 명령줄에 한 줄만 알린다. 서버 주소는 PC별 `%APPDATA%\ASH\ash.user.tsv`의 `ServerUrl`(없으면 확인하지 않음)이며 `AGVSERVER`로 바꾸고, 설치할 때 `/server:http://server:5080`으로 미리 넣을 수 있다. `AGVUPDATE`는 설치 파일을 `%TEMP%\ASH-update\`에 받아(SHA-256·서명 확인) 폴더를 열어 준다. ZWCAD를 완전히 종료한 뒤 그 파일을 실행한다.

## ash-run (수량 탐색 실행기)

플러그인으로 모델을 만든 뒤(`<Project>\<Model>.exe`, `<Model>.arc\`), 콘솔에서 그룹별 최소 AGV 대수를 탐색한다. `input.xlsx`는 프로젝트 폴더에 둔다. 설치: `dist/ASH-Runner-x.y.z-win-x64.zip`을 풀고 `ash-run.exe`를 실행한다(.NET 런타임 설치 불필요, win-x64 자가 포함 단일 파일).

```
ash-run C:\Projects\Plant1 --model plant1 --parallel 2 --dialog-button "Student Version" --timeout-min 30 --initial-reps 1
```

- 옵션: `--parallel`(동시 실행 수, 기본 2), `--initial-reps`(탐색 중 반복 수, 기본 1), `--impact`(영향표 작성, 기본 끔), `--append`(기존 `runs\` 유지), `--debug`(모델 로직 로그 켬).
- `--dialog-button`: 기본은 AutoMod 대화상자를 누르지 않는다(정식 라이선스 서버가 학생판으로 빠지지 않게). **체험판(Student Version) 사용자는 `--dialog-button "Student Version"`을 꼭 준다.** 없으면 Authorization 창에서 실행이 멈춰 시간 초과로 실패한다.
- 결과: `runs\summary.tsv`(실행마다 그룹별 KPI), `runs\impact.tsv`(`--impact`일 때, 그룹 +1일 때 다른 그룹 KPI 변화), `runs\<RunId>\`(원본 결과·보고서). 시작할 때 기존 `runs\`는 `runs.<yyyyMMdd_HHmmss>\`로 옮긴다(`--append`면 그대로 이어 씀). 콘솔에 실행마다 한 줄, 끝에 그룹별 결정 대수와 실행 수·소요 시간을 표로 출력한다. Ctrl+C는 진행 중인 실행을 마치고 새 실행을 시작하지 않는다.
- 겹치는 경로·block을 쓰는 그룹은 `<Model>.arc\agv.asy`·`blocks.tsv`·`traffic.tsv`로 묶어 함께 판정한다(없으면 그룹별 단독 탐색 + 경고).
- 모델을 만들 때 `<Model>.arc\input.sig`(그룹·타입·자재 순서·Routes·대기 CP·SimLength의 지문)를 쓴다. 지금 `input.xlsx`와 다르면 "모델 재생성 필요"로 종료 코드 1. T/T·임계값·재고·Warmup·반복 수 등은 실행마다 `params.txt`로 넘어가므로 바꿔도 다시 만들 필요 없다.
- 판정(대수마다, 반복 평균): 공정 정지 0 → 최소 재고 ≥ 안전재고 → 가동률 ≤ UtilMax → 소비량 ≥ 이론 소비량의 95%(생산 달성; 공급 횟수는 보고용, 초기 재고를 쓰는 동안은 공급이 적어도 된다) → 교착 0. 교착 = 한 구간 이동이 Settings `DeadlockSec`(기본 600초) 초과.
- 가동률은 AutoMod `.report`의 차량 상태 비율에서 계산하며 **warmup 구간을 포함**한다(로직에서 통계를 초기화할 방법이 없음).
- 탐색 실행 수(묶음 하나, 그룹 k개, 범위 R = Max−Min+1, 초기 반복 1): 기준 1 + k·⌈log2 R⌉ + 이후 패스마다 k(한 대씩만 줄여 봄) + 확정 반복 + 확인. 최대 대수에서 실패한 그룹은 1패스 뒤 건너뛴다. `InitStockMode Fixed`는 시드 영향이 없어 반복 1회로 실행한다.
- 종료 코드: 0 성공, 1 오류(인자·모델 없음, 모델 재생성 필요, 기준 실행이 모두 실패 — 이때 최근 `<Model>.message` 앞 5줄을 출력), 2 성공했으나 경고(판정 실패·실행 실패 포함).
- `--am-home <AutoMod 폴더>`를 주면 모델 실행 프로세스의 PATH 앞에 `<폴더>\bin`이 붙는다(생략하면 PATH 그대로).
- `summary.tsv`의 `ok`/`reason`은 그 실행 하나의 한 그룹만 단일 반복으로 판정한 값이다. 탐색의 최종 판정은 반복 평균이라 다를 수 있다.
