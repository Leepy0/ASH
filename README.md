# ASH — AGV Simul Helper

ZWCAD 플러그인과 실행기로 AutoMod AGV 수량 산출 업무를 자동화한다.

- 설계서: docs/superpowers/specs/2026-10-20-ash-plugin-design.md
- 1차 구현 계획: docs/superpowers/plans/2026-10-20-ash-core-and-layout.md

## 설치

1. `ASH-Setup-x.y.z.exe`를 받는다(빌드: `scripts/build-release.ps1` → `dist/`).
2. 실행한다(관리자 권한을 요청한다). 찾은 ZWCAD가 모두 선택된 상태로 나온다. 못 찾으면 "직접 지정…"으로 `ZWCAD.exe`가 있는 폴더를 고른다. "설치"를 누른다.
3. ZWCAD를 다시 시작하면 리본에 `ASH` 탭이 생기고 `AGV*` 명령을 쓸 수 있다.

제거: `ASH-Setup-x.y.z.exe /uninstall` (창에서 "제거"). 무인 설치: `/silent`, 폴더 추가: `/dir:"D:\CAD\ZWCAD 2026"`. 로그: `%TEMP%\ASH-install.log`.
DLL은 `<ZWCAD 폴더>\ASH\`에 들어가고(쓰기 실패 시 `%ProgramData%\ASH\<버전>\`), 자동 로드는 `HKCU`·`HKLM\Software\ZWSoft\ZWCAD\<버전>\<언어>\Applications\ASH`에 등록된다.
