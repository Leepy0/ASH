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
- 최신 버전: 창이 열리면 백그라운드로(10초 제한) `https://github.com/Leepy0/ASH/releases/latest/download/ASH.json`을 읽어 "설치 파일 버전 x / GitHub 최신 y"를 보인다("최신 버전 확인"으로 다시). y가 더 새 버전이면 "최신 설치 파일 받아 설치"가 `ASH-Setup-y.exe`를 `%TEMP%\ASH-update\<임의 폴더>\`에 받아(진행 표시, "취소" 가능) 매니페스트의 SHA-256·크기를 확인한 뒤 같은 인자 + `/noupdatecheck`로 실행하고 이 창을 닫는다. ZWCAD를 켤 필요가 없고 사내 서버도 쓰지 않는다(api.github.com 불필요, github.com HTTPS가 신뢰 기준, PC의 프록시 설정 사용). 확인·다운로드가 실패하면(오프라인, 프록시, 사내 인증서) 한 줄만 보이고 이 exe의 버전을 그대로 설치할 수 있다. `/silent`와 `/noupdatecheck`는 확인하지 않는다.

제거: `ASH-Setup-x.y.z.exe /uninstall` (창에서 "제거"). 무인 설치: `/silent`, 폴더 추가: `/dir:"D:\CAD\ZWCAD 2026"`. 로그: `%TEMP%\ASH-install.log`. 종료 코드: 0 성공, 1 실패(ZWCAD 실행 중 포함), 2 경고(자동 로드 등록 못 함 — ZWCAD를 한 번 실행한 뒤 다시 설치, 미지원 버전 건너뜀, SYSTEM 계정 등 사용자 하이브를 알 수 없음).
DLL은 `<ZWCAD 폴더>\ASH\`에 들어가고(쓰기 실패 시 `%ProgramData%\ASH\<버전>\`), 자동 로드는 `HKCU`·`HKLM\Software\ZWSoft\ZWCAD\<버전>\<언어>\Applications\ASH`에 등록된다.

서버 설치·릴리스 절차: [docs/server-install.md](docs/server-install.md). 서버 웹은 홈(`/`, 내 작업·전체), 분석(`/analyses/{id}`의 현황·결론·비교·영상), 설정(`/admin/settings`)이며 전체 대기열은 `/admin/jobs`다.

업데이트: 플러그인은 시작할 때(3초 제한, 백그라운드) 사내 서버(`ASH.Server`)에 새 버전이 있는지 확인해 명령줄에 한 줄만 알린다. 서버 주소는 PC별 `%APPDATA%\ASH\ash.user.tsv`의 `ServerUrl`(비어 있으면 PC 공용 `%ProgramData%\ASH\ash.machine.tsv`, 둘 다 없으면 확인하지 않음)이며 `AGVSERVER`로 바꾸고, 설치할 때 `/server:http://server:8082`으로 미리 넣을 수 있다. `AGVUPDATE`는 서버에서 받은 `ASH.json`을 `https://github.com/Leepy0/ASH/releases/download/v<버전>/ASH.json`과 바이트 단위로 비교하고, 같을 때만 설치 파일을 `%TEMP%\ASH-update\`에 받아(매니페스트의 SHA-256·크기 확인) 폴더를 열어 준다. ZWCAD를 완전히 종료한 뒤 그 파일을 실행한다. 시작할 때의 새 버전 알림은 대조하지 않는다(알림만, 설치는 `AGVUPDATE`에서 대조). github.com은 HTTPS라 사내망에서 바꿔치기할 수 없다. PC는 github.com(및 리디렉션되는 release-assets.githubusercontent.com / objects.githubusercontent.com)에 HTTPS로 접속할 수 있어야 하고, PC의 프록시 설정(로그인 계정 자격 증명)을 쓴다. 접속할 수 없거나 내용이 다르면 "업데이트 중단(github.com에 접속할 수 없어 배포 정보를 대조하지 못했습니다)" 같은 줄을 출력하고 설치하지 않는다. 0.15.0부터 이 방식이다. 0.14.x 이하(옛 서명 키 내장) PC는 자동 업데이트로 넘어오지 못하므로 0.15.0의 `ASH-Setup`을 한 번 직접 실행한다.

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
- **자동 업데이트:** 작업을 시작하기 전에 서버(`ASH.Server`)에 새 `ash-run`이 있는지 확인하고(3초 제한), 있으면 서버에서 받은 `ASH.Runner.json`을 `https://github.com/Leepy0/ASH/releases/download/v<버전>/ASH.Runner.json`과 바이트 단위로 비교하고, 같을 때만 zip을 받아 매니페스트의 SHA-256·크기를 확인한 뒤 `ash-run.exe`를 새 파일로 바꾸고 같은 인자로 다시 실행한다. 그래서 한 작업이 두 버전에 걸치지 않는다. 이전 파일은 `ash-run.exe.old`로 남고, 새 파일이 시작되지 않으면 이전 파일로 되돌린다. 서버 주소는 `--server <주소>`, 환경 변수 `ASH_SERVER`, 설정 파일(`%APPDATA%\ASH\ash.user.tsv` 또는 `%ProgramData%\ASH\ash.machine.tsv`의 `ServerUrl`) 순서로 찾고, 없으면 업데이트하지 않는다. `--no-update`는 확인을 건너뛰고, `ash-run --update [--server 주소]`는 지금 확인하고 바꾼 뒤 끝낸다. `ash-run --version`은 버전을 출력한다. 서버가 응답하지 않거나 폴더에 쓸 수 없거나 github.com에 접속할 수 없거나 내용이 다르면 `update skipped: ...` 한 줄을 알리고 지금 버전으로 계속한다. 분석 PC는 github.com(및 release-assets.githubusercontent.com / objects.githubusercontent.com)에 HTTPS로 접속할 수 있어야 하고 PC의 프록시 설정(로그인 계정 자격 증명)을 쓴다. 0.14.x 이하 `ash-run`은 자동 업데이트로 넘어오지 못하므로 0.15.0 zip으로 한 번 교체한다. `ash-run`은 시작할 때 자기 파일을 `%TEMP%\.netsh-run\`에 풀어 쓴다(새 버전의 첫 실행만 2~3초, 이후 즉시). 다른 `ash-run`이 실행 중이어도 파일을 안전하게 교체하기 위해서이고, 쓰지 않는 옛 버전 폴더는 지워도 된다. 문제가 있으면 환경 변수 `ASH_RUNNER_DEBUG=1`로 실행해 자세한 오류와 잠금 과정을 본다.
- `--am-home <AutoMod 폴더>`를 주면 모델 실행 프로세스의 PATH 앞에 `<폴더>\bin`이 붙는다(생략하면 PATH 그대로).
- `summary.tsv`의 `ok`/`reason`은 그 실행 하나의 한 그룹만 단일 반복으로 판정한 값이다. 탐색의 최종 판정은 반복 평균이라 다를 수 있다.
