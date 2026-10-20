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

## ash-run (수량 탐색 실행기)

플러그인으로 모델을 만든 뒤(`<Project>\<Model>.exe`, `<Model>.arc\`), 콘솔에서 그룹별 최소 AGV 대수를 탐색한다. `input.xlsx`는 프로젝트 폴더에 둔다. .NET 10 런타임 필요(`dist/ASH-Runner-x.y.z.zip`).

```
ash-run C:\Projects\Plant1 --model plant1 --parallel 2 --dialog-button "Student Version" --timeout-min 30 --initial-reps 1
```

- 옵션: `--parallel`(동시 실행 수, 기본 2), `--initial-reps`(탐색 중 반복 수, 기본 1), `--no-impact`(영향표 생략), `--debug`(모델 로직 로그 켬), `--dialog-button ""`(AutoMod 대화상자 자동 클릭 끔).
- 결과: `runs\summary.tsv`(실행마다 그룹별 KPI 누적), `runs\impact.tsv`(그룹 +1일 때 다른 그룹 KPI 변화), `runs\<RunId>\`(원본 결과·보고서). 콘솔에 그룹별 결정 대수와 실행 수·소요 시간을 표로 출력한다.
- 겹치는 경로·block을 쓰는 그룹은 `<Model>.arc\agv.asy`·`blocks.tsv`·`traffic.tsv`로 묶어 함께 판정한다(없으면 그룹별 단독 탐색 + 경고).
- 종료 코드: 0 성공, 1 오류(인자·모델 없음 등), 2 성공했으나 경고(판정 실패·실행 실패 포함).
- `--am-home <AutoMod 폴더>`를 주면 모델 실행 프로세스의 PATH 앞에 `<폴더>\bin`이 붙는다(생략하면 PATH 그대로).
- `summary.tsv`의 `ok`/`reason`은 그 실행 하나의 한 그룹만 단일 반복으로 판정한 값이다. 탐색의 최종 판정은 반복 평균이라 다를 수 있다.
