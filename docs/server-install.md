# ASH 서버 설치와 릴리스 배포

> **무결성 확인(0.15.0부터): 서명 없이 PC가 github.com과 대조한다.**
> - 기본은 서명 없음이다. 저장소 루트의 `update-key.pub`는 없고, 빌드에 공개키가 내장되지 않으며, GitHub Secret `ASH_SIGNING_KEY_PEM`도 쓰지 않는다.
> - 플러그인 `AGVUPDATE`와 `ash-run` 자동 업데이트는 사내 서버에서 받은 매니페스트(`ASH.json`/`ASH.Runner.json`)를 `https://github.com/Leepy0/ASH/releases/download/v<버전>/<같은 파일명>`과 바이트 단위로 비교한다. 같을 때만 설치 파일/zip을 받고 매니페스트의 SHA-256·크기로 확인한다. github.com은 HTTPS라 사내망에서 바꿔치기할 수 없다.
> - 사용자 PC는 github.com(및 리디렉션되는 release-assets.githubusercontent.com / objects.githubusercontent.com)에 HTTPS로 접속할 수 있어야 한다. PC의 프록시 설정(로그인 계정 자격 증명)을 쓴다. 접속할 수 없거나 내용이 다르면 업데이트를 거절한다.
> - 0.14.x 이하(옛 서명 키 내장) PC·서버·ash-run은 자동 업데이트로 넘어오지 못한다. 0.15.0을 한 번 수동 설치한다(`ASH-Setup` 실행, 서버 zip 교체, `ash-run` zip 교체).
> - 서명은 선택 기능으로 남아 있다(2절 "선택: 서명 키 사용").

구성: **서버 PC**(`ASH.Server`, GitHub 릴리스를 스스로 가져와 보관·제공) / **릴리스 PC**(빌드·GitHub 릴리스 생성, 기본은 GitHub Actions) / **사용자 PC**(ZWCAD + ASH 플러그인, 시작할 때 서버에 새 버전 확인).

## 1. 서버 PC 설치

`ASH.Server`는 .NET 설치가 필요 없는 단일 exe다(`dist/ASH-Server-<버전>-win-x64.zip`). 아래 명령은 **관리자 명령 프롬프트(cmd)** 기준이다(PowerShell이면 `%ProgramData%`를 `$env:ProgramData`로, `sc`를 `sc.exe`로 바꾼다).

**가장 쉬운 방법:** zip을 `C:\Program Files\ASH.Server`에 풀고 `install-service.bat`을 더블클릭한다(관리자 권한을 묻는다). 아래 3~5번(폴더 권한 잠금, 서비스 등록과 복구 설정, 방화벽 규칙)과 시작, 응답 확인을 한 번에 한다. 포트는 `appsettings.json`의 `Urls`에서 읽는다. `Releases:Dir`을 기본값에서 바꿨다면 그 폴더의 권한은 직접 잠근다. 제거는 `uninstall-service.bat`(배포 파일과 설정은 남긴다). 아래는 같은 일을 손으로 하는 방법이다.

1. zip을 **`C:\Program Files\ASH.Server`** 에 푼다. `ASH.Server.exe`, `appsettings.json`, `README-server.txt`, `install-service.bat`, `uninstall-service.bat`이 나온다.
2. `appsettings.json`을 고친다.
   - `GitHub` — 새 릴리스 자동 가져오기(AGV_FOTA_GATEWAY와 같은 방식). 기본값 그대로 두면 된다.
     - `BaseUrl` 기본 `https://github.com/Leepy0/ASH`. 서버는 `<BaseUrl>/releases/latest/download/ASH.json`만 본다(api.github.com은 쓰지 않음). https만 받는다.
     - `CheckIntervalMin` 기본 60(분). 0이면 서비스 시작 때 한 번만 확인. 시작 10초 뒤 첫 확인.
     - `Proxy` 비우면 PC 프록시 설정, `none`이면 직접 연결, 또는 `http://proxy.corp:8080`. 서비스(LocalSystem)는 로그인 사용자의 프록시 설정을 쓰지 않으므로, 콘솔 실행으로는 되는데 서비스로는 실패하면 여기에 적는다.
     - `Dns` 비우면 PC의 DNS를 쓴다. 사내 DNS가 github.com을 못 찾으면 쓸 수 있는 DNS 서버 IP를 적는다(예: `"8.8.8.8, 1.1.1.1"` 또는 사내 외부용 DNS). GitHub 주소(다운로드가 넘겨지는 release-assets.githubusercontent.com 포함)를 그 DNS에서 먼저 찾고, 답이 없으면 PC의 DNS로 다시 찾는다. UDP 53번 포트로 나간다. 프록시를 쓰면 프록시 이름도 같은 순서로 찾는다.
     - `UpdateServer` 기본 true. 서버 프로그램 자신도 자동으로 새 버전으로 바꾼다(5절 "서버 자동 업데이트"). false면 서버 exe는 수동 교체.
     - `Enabled` false면 끈다(수동 업로드만 사용, 서버 자동 업데이트도 꺼짐).
     - 가져온 릴리스도 업로드와 똑같이 크기·SHA-256을 확인하고(GitHub에서 HTTPS로 받은 것은 그대로 믿는다. 서명은 요구하지 않는다), 서버 버전보다 높을 때만 게시한다.
   - `Admin:Password` — 관리자 암호(웹 설정·작업 대기열·분석 삭제·타입 관리·수동 업로드). 4~20자, 영문·숫자·특수문자를 섞어 쓸 수 있고 공백·한글은 안 됨. 비워 두면 기본값 `ash!23`. **다른 사람이 닿는 서버는 반드시 바꾼다.** 규칙에 맞지 않으면 관리자 기능이 모두 꺼진다. 0.22 이하의 `Admin:Token`은 읽지 않는다.
   - `Urls` — 기본 `http://0.0.0.0:8082`. 포트를 바꾸면 아래 방화벽·`/server:` 주소도 같이 바꾼다. 주소를 여러 개 쓰려면 `;`로 잇는다(https는 5절 "완료·실패 알림").
   - `Releases:Dir` — 배포 파일 보관 폴더(기본 `%ProgramData%/ASH.Server/releases`). **3번에서 권한을 먼저 잠근 폴더**여야 한다.
   - 설정 파일(관리자 암호 포함)을 보호하도록 설치 폴더 권한도 관리자로 제한한다(`Program Files` 기본값은 일반 사용자 읽기 가능).
     ```bat
     icacls "C:\Program Files\ASH.Server" /inheritance:r /grant:r "Administrators:(OI)(CI)F" "SYSTEM:(OI)(CI)F"
     ```
3. **배포 폴더를 먼저 만들고 권한을 잠근 뒤에** 서비스를 등록한다. 순서를 지킬 것 — 서비스가 폴더를 먼저 만들면 `%ProgramData%`의 상속 권한(일반 사용자가 읽고 새 파일을 만들 수 있음)이 붙어, 로컬 사용자가 배포 파일을 바꿔치기할 수 있다.
   ```bat
   mkdir "%ProgramData%\ASH.Server"
   icacls "%ProgramData%\ASH.Server" /inheritance:r /grant:r "Administrators:(OI)(CI)F" "SYSTEM:(OI)(CI)F"
   cd /d "C:\Program Files\ASH.Server"
   ASH.Server.exe --install
   sc qc ASH.Server
   sc start ASH.Server
   ```
   - 윈도우 표시 언어가 한국어 등이라 `Administrators`/`SYSTEM` 이름이 통하지 않으면 SID로 준다: `icacls "%ProgramData%\ASH.Server" /inheritance:r /grant:r "*S-1-5-32-544:(OI)(CI)F" "*S-1-5-18:(OI)(CI)F"` (`*S-1-5-32-544` = Administrators, `*S-1-5-18` = SYSTEM). 설치 폴더 명령도 같다.
   - `sc qc`의 `BINARY_PATH_NAME`이 `"C:\Program Files\ASH.Server\ASH.Server.exe"`처럼 따옴표로 감싸졌는지 확인한다(경로에 공백이 있다).
   - `Releases:Dir`을 다른 곳으로 바꿨다면 그 폴더에 같은 `mkdir`/`icacls`를 한다.
   - 서비스는 기본적으로 LocalSystem으로 돈다. LocalService로 바꾸면 설치 폴더에 쓸 수 없어 **서버 자동 업데이트가 교체 단계에서 실패한다**(이전 버전 유지, `update\apply.log`에 기록). 그래도 권한을 줄이려고 LocalService로 바꾼다면 `sc config ASH.Server obj= "NT AUTHORITY\LocalService"` 뒤에 배포 폴더에 수정 권한, 설치 폴더에 읽기 권한을 준다.
     ```bat
     icacls "%ProgramData%\ASH.Server" /grant "NT AUTHORITY\LocalService:(OI)(CI)M"
     icacls "C:\Program Files\ASH.Server" /grant "NT AUTHORITY\LocalService:(OI)(CI)RX"
     ```
   - 제거: `ASH.Server.exe --uninstall`. 서비스 없이 확인만 하려면 `ASH.Server.exe`를 직접 실행해도 된다.
4. **서비스 복구(실패 시 자동 재시작)**
   ```bat
   sc failure ASH.Server reset= 86400 actions= restart/60000
   ```
   프로세스가 죽으면 60초 뒤 다시 시작하고, 실패 횟수는 하루(86400초) 뒤에 초기화된다.
5. **방화벽** 인바운드 TCP 8082 허용(사내망 범위만).
   ```powershell
   New-NetFirewallRule -DisplayName "ASH.Server" -Direction Inbound -Protocol TCP -LocalPort 8082 -Action Allow -Profile Domain,Private
   ```
   **함정:** 서버 PC의 네트워크가 도메인에 연결되지 않으면 윈도우가 그 NIC를 **Public**으로 분류하기도 한다. 그러면 위 규칙은 적용되지 않아 다른 PC에서 접속이 안 된다(서버 PC 자신에서는 열린다). `Get-NetConnectionProfile`로 `NetworkCategory`를 확인하고, Public이면 NIC를 Private로 바꾼다(`Set-NetConnectionProfile -InterfaceAlias <이름> -NetworkCategory Private`). 규칙에 `-Profile Public`을 그냥 추가하지 말 것 — 카페·공유기 같은 신뢰할 수 없는 네트워크에도 포트가 열린다. 꼭 필요하면 `-RemoteAddress 10.0.0.0/8`처럼 사내 대역으로 제한한다.
6. 다른 PC의 브라우저로 `http://<서버>:8082/admin/settings` 를 열어 서버 상태를 확인한다. 서비스 시작 직후에는 "게시된 버전이 없습니다."가 정상이고, 10초쯤 뒤 설정(`/admin/settings`)을 새로 고치면 "마지막 확인 … 가져옴: x.y.z"와 현재 버전이 보인다. "실패: …"가 보이면 서버 PC에서 github.com 접속과 `GitHub:Proxy`를 확인한다. "무결성 확인: 없음 — PC가 github.com 릴리스와 대조해 확인"이 보이는 것이 정상이다.

수동 업로드의 관리자 암호는 평문 HTTP로 전달된다. 수동 업로드는 신뢰할 수 있는 사내망에서만 하거나 앞단에 HTTPS 프록시를 둔다.

### 로그 위치

서비스 로그는 **이벤트 뷰어 > Windows 로그 > 응용 프로그램**, 원본(Source) **ASH.Server**에 남는다(`appsettings.json`의 `Logging:EventLog:LogLevel:Default` = Information). 업로드 거절·가져오기 오류가 여기에 찍힌다.

LocalService 계정은 이벤트 원본을 **만들 수 없어** 로그가 조용히 사라진다. 한 번만 관리자 권한으로 원본을 만들어 둔다.

```powershell
New-EventLog -LogName Application -Source ASH.Server
```

(또는 처음 한 번 LocalSystem으로 서비스를 시작해 원본이 생기게 한 뒤 LocalService로 바꾼다.) 콘솔로 직접 실행하면 로그가 콘솔에 나온다.

### 서명 없는 업로드 (`Update:AllowUnsigned`)

기본값은 `true`다. 서버는 서명 없는 `ASH.json`, Runner, 서버 자동 업데이트를 모두 받는다. 관리자 수동 업로드(`publish-release.ps1`)도 서명 없이 받지만, PC가 github.com과 대조하므로 GitHub 릴리스와 다른 파일은 설치되지 않는다. `"Update": { "AllowUnsigned": false }`로 두면 서명 없는 업로드를 거절한다. 키가 없는 빌드에서는 쓰지 말 것.

## 2. 릴리스 절차 (릴리스 PC)

### GitHub Actions로 릴리스 (기본)

`.github/workflows/release.yml`이 Windows 러너에서 `build-release.ps1`(빌드·테스트)을 돌리고 정식 릴리스(태그 포함)를 만든다. 서명이 없어 Secret이 필요 없고 누가 push해도 된다.

- 릴리스: `Directory.Build.props`의 `<Version>`을 올려 `main`에 push한다. 그 버전의 릴리스가 없으면 `v<버전>` 태그와 정식 릴리스를 만들고, 있으면 건너뛴다. `v*` 태그를 직접 push하거나(태그와 버전이 같아야 함) Actions 탭에서 Run workflow로 실행해도 된다.
- 릴리스 자산은 7개다: `ASH-Setup-<v>.exe`, `ASH.json`, `ASH.Server.json`, `ASH.Runner.json`, `ASH-Plugin-<v>.zip`, `ASH-Runner-<v>-win-x64.zip`, `ASH-Server-<v>-win-x64.zip`(`.sig` 없음).
- 릴리스 노트는 직전 태그 이후 커밋 제목(chore·ci 제외)이다.

### 빌드와 게시 (릴리스 PC에서 수동)

```powershell
scripts\build-release.ps1 -MinSupported 0.1.0 -Notes notes.txt     # dist\ 에 Setup, Plugin, Runner, Server zip, ASH.json, ASH.Server.json, ASH.Runner.json
gh release create v<버전> dist\ASH-Setup-<버전>.exe dist\ASH.json dist\ASH.Server.json dist\ASH.Runner.json dist\ASH-Plugin-<버전>.zip dist\ASH-Runner-<버전>-win-x64.zip dist\ASH-Server-<버전>-win-x64.zip --title "v<버전>" --notes-file notes.txt
```

- 정식 릴리스(초안·시험판 아님)에 `ASH.json`, `ASH-Setup-<버전>.exe`가 반드시 있어야 서버가 가져간다. 서버 자동 업데이트에는 `ASH.Server.json`, `ASH-Server-<버전>-win-x64.zip`, ash-run 배포에는 `ASH.Runner.json`, `ASH-Runner-<버전>-win-x64.zip`도 필요하다(`build-release.ps1`이 함께 만든다). 태그는 `v<버전>`(매니페스트의 version과 같게). 위 7개 자산이 전부 올라가야 한다.
- 서버는 `CheckIntervalMin`(기본 60분) 안에 가져간다. 바로 반영하려면 서버에서 `sc stop ASH.Server && sc start ASH.Server`.
- 서버가 GitHub에 닿지 않는 곳이면 수동 업로드를 쓴다(서버 `Admin:Password` 필요):
  ```powershell
  $env:ASH_ADMIN_TOKEN = '<서버 appsettings.json 의 Admin:Password>'      # 명령줄·기록에 남기지 않으려고 환경 변수로
  scripts\publish-release.ps1 -Server http://<서버>:8082
  ```
  서명 없이 받지만, PC는 서버의 매니페스트를 github.com의 `v<버전>` 릴리스와 대조하므로 GitHub 릴리스와 같은 파일을 올려야 설치된다.

- 버전은 `Directory.Build.props`의 `<Version>`이다(올리지 않으면 서버가 같은/낮은 버전을 거절한다).
- 서명은 하지 않는다(선택 기능, 아래 "선택: 서명 키 사용"). `-AllowUnsigned`/`-RequireSignature`는 이제 의미 없는 옵션으로, 경고만 낸다. `update-key.pub`가 있는데 `ASH_SIGNING_KEY`가 없으면 빌드가 중단된다.
- 빌드는 항상 처음부터 다시 컴파일하고(`--no-incremental`) `dotnet test ASH.sln`(서버 테스트 포함)을 통과해야 패키징한다.
- `-MinSupported <버전>`(없으면 `ASH_MIN_SUPPORTED`, 그것도 없으면 `0.0.0`)은 그 버전 **미만** PC에 "최소 지원 미만" 안내를 띄운다. 기본값은 "아무도 미만이 아님"이다 — 이번 버전으로 자동 설정하지 않는다. 이번 버전보다 높게는 줄 수 없다(서버도 거절).
- 게시 뒤 설정 페이지(`http://<서버>:8082/admin/settings`)에서 버전, "무결성 확인" 줄, "마지막 확인" 결과를 확인한다.

### 되돌리기(롤백)의 뜻

`publish-release.ps1 -Force`로 **더 낮은 버전**을 게시해도 이미 새 버전을 쓰는 PC는 내려가지 않는다. 클라이언트는 서버 버전이 자기보다 낮으면 "서버의 배포 버전이 현재보다 낮습니다"만 알리고(ServerOlder) 아무것도 설치하지 않는다. `-Force`는 서버가 낮은 버전을 받게 해 줄 뿐이다.

실제로 되돌리려면 **옛 코드를 다시 빌드하되 더 높은 버전 번호**를 붙여 게시한다(예: 0.3.0이 문제라 0.2.x 코드로 돌아가려면 그 코드를 0.3.1로 빌드해 정상 게시). 그러면 PC들이 평범한 업데이트로 받는다.

### 선택: 서명 키 사용

기본은 서명이 없다. 예전 서명 방식으로 되돌리려면 `AshRelease keygen --out <저장소 밖 안전한 폴더>`로 키를 만들고 공개키 `update-key.pub`를 저장소 루트에 커밋한 뒤, `ASH_SIGNING_KEY`(개인키 PEM 경로)로 `build-release.ps1`를 실행한다. 공개키가 빌드에 내장되고 매니페스트에 `.sig`가 붙으며(릴리스에도 올린다), 공개키만 있고 개인키가 없으면 빌드가 중단된다. GitHub Actions에서는 서명하지 않는다.

- 개인키(`ash-signing.pem`)는 저장소에 커밋하지 않고 릴리스 PC 밖으로 내보내지 않으며, 암호화한 오프라인 사본 2개를 서로 다른 곳에 보관한다. 잃어버리면 모든 PC에 `ASH-Setup`을 직접 다시 설치해야 한다.
- 키 교체는 4절. 이 절의 내용은 서명을 다시 쓸 때만 해당한다.

## 3. 사용자 PC

- 설치 때 서버 주소를 넣는다: `ASH-Setup-x.y.z.exe /server:http://<서버>:8082` (창 모드는 "서버 주소" 칸). 설치가 실패하면 주소는 저장하지 않는다.
- 주소는 두 곳에 저장된다. **PC 공용** `%ProgramData%\ASH\ash.machine.tsv`(관리자·SYSTEM만 쓰기, 일반 사용자는 읽기)에는 항상 쓰고, 대상 사용자를 알 수 있으면 `%APPDATA%\ASH\ash.user.tsv`(`ServerUrl`)에도 쓴다. 플러그인은 사용자 파일의 `ServerUrl`이 비어 있지 않으면 그것을, 아니면 PC 공용 값을 쓴다.
- 이미 설치한 PC는 ZWCAD 명령 `AGVSERVER`로 주소를 넣거나 바꾼다(사용자 파일에 저장).
- 주소가 있으면 ZWCAD 시작 때(3초 제한) 새 버전을 확인해 명령줄에 한 줄 알리고, `AGVUPDATE`가 서버의 `ASH.json`을 github.com의 같은 버전 릴리스와 바이트 단위로 대조하고, 같을 때만 설치 파일을 `%TEMP%\ASH-update\`에 받아 SHA-256·크기를 확인한 뒤 탐색기로 연다. ZWCAD를 완전히 종료한 뒤 그 파일을 실행한다. 시작 때의 새 버전 알림은 대조하지 않는다(알림만, 설치는 `AGVUPDATE`에서 대조).
- 업데이트를 하려면 PC가 github.com(및 리디렉션되는 release-assets.githubusercontent.com / objects.githubusercontent.com)에 HTTPS로 접속할 수 있어야 한다. PC의 프록시 설정(로그인 계정 자격 증명)을 쓴다. 접속할 수 없거나 내용이 다르면 "업데이트 중단(github.com에 접속할 수 없어 배포 정보를 대조하지 못했습니다)" 같은 줄을 출력하고 설치하지 않는다.
- 서버 없이도: `ASH-Setup` 창은 열릴 때 github.com의 최신 릴리스(`releases/latest/download/ASH.json`)를 직접 확인하고, 더 새 버전이면 "최신 설치 파일 받아 설치"로 그 `ASH-Setup`을 받아(SHA-256·크기 확인) 실행한다. ZWCAD를 켜서 `AGVUPDATE`를 할 필요가 없다. 실패하면 한 줄 안내만 하고 그 exe의 버전으로 설치한다(README "설치" 절).
- 주소는 `http(s)://호스트[:포트][/경로]` 형식만 받는다(사용자 정보 `user@`, `?query`, `#fragment` 불가).

### 로컬 관리자 권한이 없는 사용자

설치는 관리자 권한이 필요하다. 권한이 없는 사용자 PC는 **IT가 배포**한다(SYSTEM으로 실행하는 배포 도구, 로그온 스크립트 등).

```bat
ASH-Setup-x.y.z.exe /silent /server:http://<서버>:8082
```

SYSTEM으로 실행해도 로그온한 대화형 사용자가 있고 그 사용자의 레지스트리 하이브가 로드되어 있으면 그 사용자의 HKCU 등록과 사용자 파일(`ash.user.tsv`)을 쓴다. 로그온한 사용자를 알 수 없으면(아무도 로그온하지 않았거나 하이브가 없음) HKCU 등록과 사용자 파일은 건너뛰고 HKLM 등록과 PC 공용 `ash.machine.tsv`만 쓴다. 이 경우에도 종료 코드는 0이다(PC 공용 파일을 썼다면). 사용자 파일에 주소가 없는 사용자는 PC 공용 주소를 쓴다.

## 4. 서명 키 교체 (서명을 다시 쓸 때만)

서명 키를 쓰는 경우에만 해당한다. 새 공개키가 들어간 버전을 **옛 키로 서명해** 먼저 배포한 뒤에야 새 키로 서명할 수 있다.

1. `keygen`으로 새 키를 만든다(새 폴더).
2. 저장소 루트 `update-key.pub`에 **옛 공개키와 새 공개키를 모두** 넣는다(PEM 블록 두 개를 이어 붙임. 여러 개 중 하나만 맞아도 통과).
3. 옛 개인키(`ASH_SIGNING_KEY`)로 서명해 이 버전을 빌드·배포한다. 사용자 PC가 이 버전으로 업데이트하고, **서버도 이 판으로 교체**한다(서버 exe가 새 공개키를 알아야 새 키 서명을 받는다).
4. 대부분의 PC가 업데이트한 뒤, 새 개인키로 서명한 릴리스를 낸다. 더 안전하게 하려면 그 다음 버전에서 `update-key.pub`의 옛 키를 뺀다.
5. 옛 개인키가 유출됐다면 새 키 판은 옛 키로 서명할 수 없으므로 모든 PC에 `ASH-Setup`을 직접 실행하게 해야 한다.

## 5. 서버 운영

왼쪽 사이드바는 홈 · 분석 · 영상 · 보관함 · 관리다(평소 아이콘만, 마우스를 올리면 펼쳐진다). 관리 위의 종 버튼이 알림이다.

- **홈**(`http://<서버>:8082/`): 네 구획 진행 | 결론 확인 | 영상 | 보고서. 내 것만 | 전체 전환. 열어 둔 채로 10초마다 갱신하고(대기→실행 전환은 자동 새로 고침, 60초마다 전체를 다시 읽음) 서버와 끊기면 "연결 끊김"을 띄운다.
- **분석**(`/analyses`, `/analyses/{id}`): 목록 + 탭 결론 · 비교 · 영상 · 버전(`?tab=conclusion|compare|films|status`). 영상 탭에서 완성 영상을 보고 `all.zip`으로 컷 파일을 한 번에 받는다.
- **보고서**(`/analyses/{id}/report`, `/analyses/{id}/report.pptx`): 아래 "보고서".
- **영상**(`/films`): 영상 목록. 영상 하나는 `/films/{id}`, 컷 편집기(영상 만들기)는 `/analyses/{id}/cuts`.
- **보관함**(`/files`): 보낸 파일을 찾아 받는 화면(검색, `continue.zip`).
- **관리**(`/admin/settings`): 서버 상태(자동 업데이트, GitHub 가져오기, 무결성 확인, 수동 업로드, 서버 시각)와 배포(현재 버전, 설치 파일, Runner, 이전 버전)도 여기 있다. 전체 작업 대기열은 `/admin/jobs`(옛 `/jobs`는 301).
- JSON: `/api/home`, `/api/analyses/{id}/status`, `/api/films`, `/api/films/{id}`, `/api/me/events`(알림).

### 웹 설정과 서비스 재시작 (`/admin/settings`)

`appsettings.json`의 값을 브라우저에서 바꾼다. `Admin:Password`가 규칙(4~20자, 영문·숫자·특수문자를 섞어 쓸 수 있고 공백·한글은 안 됨, 비우면 기본값 `ash!23`)에 맞지 않으면 페이지가 꺼져 있다.

- **탭:** 작업 현황(실행·대기 카드, 최근 작업) · 기준 정보(`/admin/types`, 타입·투자비·에셋) · 서버 설정(`?tab=server`, 배포·서버 상태, 설정 저장, 서비스 다시 시작). 오른쪽 끝에 로그아웃.
- **타입 일괄 입력:** 기준 정보 탭의 "엑셀 양식 받기"(`/admin/types/template.xlsx`, 지금 타입이 들어 있음)를 고치거나 줄을 더한 뒤, 머리글 줄까지 복사해 "엑셀에서 붙여넣기"에 붙여넣는다. 이름이 같으면 고치고 없으면 만든다. 머리글에 없는 열은 기존 값을 유지한다. 한 줄이라도 틀리면 아무것도 저장하지 않는다. 단가 칸이 비면 단가는 그대로다.
- **로그인:** 관리자 암호를 한 번 입력하면 서버가 무작위 세션 ID를 만들어 `HttpOnly`, `SameSite=Strict`, `Path=/admin` 쿠키로 준다(8시간, 서버 메모리에만 있어 재시작하면 다시 로그인). 토큰 자체는 브라우저에 저장되지 않고 페이지에 다시 나오지 않는다. 틀리면 1초 늦게 거절한다.
- **위조 방지:** 저장·재시작·로그인 POST는 `Origin`(없으면 `Referer`)이 이 서버 주소일 때만 받는다. 스크립트는 `X-ASH-Admin-Token` 헤더로 같은 주소에 POST할 수 있다(`/api/admin/releases`와 같은 토큰).
- **바꿀 수 있는 값과 적용 시점:**

| 바로 적용(다음 작업·다음 정리부터) | 서비스 재시작 필요 |
|---|---|
| `Jobs:Enabled`, `Jobs:AmHome`, `Jobs:Parallel`, `Jobs:DialogButton`, `Jobs:RunTimeoutMin`, `Jobs:BuildTimeoutMin`, `Jobs:JobTimeoutHours`, `Jobs:KeepRunsDays`, `Film:CutTimeoutMin`, `Film:CalibrationTimeoutMin`, `Film:DialogTitles` | `Jobs:WorkerEnabled`, `Jobs:Dir`, `Film:FfmpegPath`, `Film:AviewExe`, `GitHub:Enabled`, `GitHub:BaseUrl`, `GitHub:CheckIntervalMin`, `GitHub:UpdateServer`, `GitHub:Proxy`, `GitHub:Dns`, `Urls`, `Releases:Dir`, `Update:AllowUnsigned` |

  `Admin:Password`, `Update:PublicKeyPem`, `Logging`은 웹에서 바꾸지 않는다(서버 PC에서 파일을 직접 고친다).
- **찾아보기:** 경로 설정(`Jobs:AmHome`, `Jobs:Dir`, `Film:FfmpegPath`, `Film:AviewExe`, `Releases:Dir`) 옆의 "찾아보기…"는 브라우저가 아니라 **서버 PC**의 드라이브·폴더(파일을 고르는 항목은 파일도)를 보여 주는 대화상자다. 폴더를 열어 "이 폴더 선택"(파일은 클릭)하면 입력칸이 채워지고, 저장은 따로 눌러야 한다. 목록은 이름만 읽는 관리자 전용 `GET /admin/fs?path=&files=1`(설정 화면과 같은 로그인)이 주며 파일 내용은 읽지 않는다. 열 수 없는 폴더는 안내만 나온다.
- **실행 원본 압축:** `Jobs:KeepRunsDays`(기본 30일, 0 = 그대로 둠)가 지나면 끝난 작업의 `runs\<실행>` 폴더들을 작업 폴더의 `runs.zip` 하나로 묶어(임시 이름으로 쓰고 옮김, 읽어 검증한 뒤에야) 원본 폴더를 지운다. 빌드 출력(`<모델>.dir`, `.exe`)과 작업 슬롯은 다시 만들 수 있어 지운다. `runs.zip` 받기는 그 압축 파일을 그대로 주고, 결과 화면·Excel 보고서는 미리 저장한 `blocks.tsv`/`stats.tsv`로 그대로 열린다. 압축 전에 `runs.zip`을 받으면 그때 만들어 작업 폴더에 남기고, 압축할 때 같은 이름으로 바꿔 넣는다. 압축한 뒤 파일이 없으면 410이고, 작업이 끝나기 전에는 받을 수 없다(409). 확인은 서버를 켠 1분 뒤부터 한 시간마다 하며, 영상(`films\`)과 장면 후보 파일은 건드리지 않는다.
- **저장:** 모든 값을 먼저 검사한다(정수 범위, 켬/끔, `Urls`·`GitHub:BaseUrl`·`GitHub:Proxy`·`GitHub:Dns` 형식). 하나라도 틀리면 아무것도 쓰지 않는다. 통과하면 파일을 다시 읽어 바뀐 키만 고치고(모르는 키는 그대로) 임시 파일로 쓴 뒤 `File.Replace`로 바꿔 넣으며, 이전 파일은 `appsettings.json.yyyyMMdd-HHmmss.bak`으로 남는다. 파일 안의 `//` 주석은 저장 때 없어진다(.bak에는 남음). `Jobs:AmHome`에 AutoMod 빌드 exe가 없으면 경고만 한다.
- **재시작:** "서비스 다시 시작"은 자동 업데이트의 `update\apply-update.bat`을 파일 교체 없이(stage `-`) 실행해 `sc stop` → 이 프로세스가 끝나기를 기다림 → `sc start`를 한다(기록은 `update\apply.log`). 서비스는 자기 자신을 동기적으로 다시 시작할 수 없어서 분리된 배치가 한다. 실행 중인 작업이 있으면 확인란을 체크해야 하며 그 작업은 "서버가 다시 시작되어 중단되었습니다"로 실패 처리된다. 콘솔로 실행 중이거나 등록된 서비스의 exe가 다르면 버튼 대신 이유가 나온다.
- 서비스 계정이 설치 폴더에 쓸 수 없으면(LocalService) 저장이 실패하고 파일은 바뀌지 않는다.

### 관리자의 분석 삭제 (`/admin/analyses/{id}/delete`)

분석 페이지 맨 아래 "관리자: 분석 삭제…" 링크(누구에게나 보임)가 이 확인 페이지로 간다. 페이지가 로그인을 요구한다(`/admin/settings`와 같은 관리자 암호·쿠키, 로그인 뒤 이 페이지로 돌아온다). 보낸 작업이 없는 분석은 예전처럼 누구나 분석 페이지에서 바로 지울 수 있고, 작업이 있는 분석은 관리자만 지운다.

- **결과:** 분석의 작업은 **지워지지 않고** "분석에 넣지 않은 작업"이 된다(안·버전·선정은 해제, 버전 메모는 유지). 분석과 안, 컷 목록(`analyses\<id>\cuts.json`)과 컷 미리보기(`analyses\<id>\previews\`, 모델 복사본 포함), 이 분석의 영상 기록과 영상 파일(`films\<id>\`)은 지워지고 되돌릴 수 없다.
- **확인:** 분석 이름을 그대로 입력해야 지워진다(다르면 400, 아무것도 지우지 않음). POST는 `Origin` 검사와 관리자 로그인(또는 `X-ASH-Admin-Token` 헤더)이 필요하다.
- **영상이 대기 중이거나 만드는 중이면** 409로 거절한다. 영상 페이지에서 취소한 뒤 다시 한다.

### 서버 자동 업데이트 (AGV_FOTA_GATEWAY와 같은 방식)

서버는 GitHub 확인 때마다(`CheckIntervalMin`) 자기 버전도 확인해 새 버전이면 스스로 바꾼다.

1. 최신 릴리스의 `ASH.Server.json`을 HTTPS로 읽는다(GitHub에서 받은 것은 그대로 믿는다. 서명은 요구하지 않는다).
2. `ASH-Server-<버전>-win-x64.zip`을 받아 크기·SHA-256을 매니페스트의 값과 대조하고, 설치 폴더의 `update\v<버전>\`에 푼다.
3. `update\apply-update.bat`이 서비스를 멈추고, 지금 exe를 `ASH.Server.exe.prev`로 바꾼 뒤 새 파일을 복사한다. **`appsettings.json`은 덮어쓰지 않는다.** `Releases:Dir`의 배포 파일도 그대로다.
4. 서비스를 다시 시작하고 60초 안에 `http://127.0.0.1:<포트>/healthz/version`이 새 버전을 돌려주면 끝. 이전 exe는 `ASH.Server.exe.old`로 남는다.
5. 응답이 없으면 이전 exe로 **자동 되돌리고**(실패한 exe는 `ASH.Server.exe.bad`) 다시 시작한다. 그 버전은 `update\skip-auto.txt`에 적혀 다시 시도하지 않는다. 더 높은 버전을 릴리스하면 다시 시도한다.

결과는 설정 페이지의 "서버 프로그램 … 자동 업데이트"와 "지난 업데이트" 줄, `update\apply.log`, 이벤트 로그에 남는다.

자동 교체는 이 조건에서만 한다(아니면 설정 페이지에 이유가 나온다): `ASH.Server.exe --install`로 등록한 Windows 서비스로 실행 중, 배포용 단일 exe(zip의 것), `GitHub:Enabled`와 `GitHub:UpdateServer`가 true.

### Runner(ash-run) 배포

서버는 분석 PC의 `ash-run`이 스스로 업데이트하도록 Runner를 보관하고 내려 준다.

1. GitHub 확인 때마다 최신 릴리스의 `ASH.Runner.json`을 읽는다. 서버에 있는 Runner보다 높으면 `ASH-Runner-<버전>-win-x64.zip`을 받아, 크기·SHA-256을 확인한 뒤 `Releases:Dir\runner\`에 둔다(최신 2개만 보관).
2. 분석 PC의 `ash-run`은 작업을 시작하기 전에 `/api/ash/runner`(매니페스트)를 읽고 **github.com의 `https://github.com/Leepy0/ASH/releases/download/v<버전>/ASH.Runner.json`과 바이트 단위로 대조**한 뒤(같을 때만), 새 버전이면 `/download/ASH-Runner-<버전>-win-x64.zip`을 받아 스스로 교체한다(README의 "ash-run" 절). 서버가 Runner를 못 가져와도 지금 Runner는 계속 쓸 수 있고, 분석 PC가 github.com에 접속할 수 없거나 내용이 다르면 `update skipped: ...` 한 줄을 내고 지금 버전으로 계속한다.
3. 서버 설정 페이지의 "Runner(ash-run)" 줄에 보관 중인 버전과 마지막 확인 결과가 나온다.
4. 분석 PC에는 서버 주소가 필요하다: 설치 프로그램의 `/server:` 로 넣었다면 그 설정을 읽고, 아니면 `ash-run --update --server http://<서버>:8082` 또는 환경 변수 `ASH_SERVER`.
5. 릴리스에 `ASH.Runner.json`, `ASH-Runner-<버전>-win-x64.zip`이 있어야 한다(`build-release.ps1`이 만든다. `gh release create`에 같이 올린다).

**0.2.0 이하 서버는 자동 업데이트 기능이 없어 한 번은 손으로 바꾼다.** 새 `ASH-Server-<버전>-win-x64.zip`을 받아:

```bat
sc stop ASH.Server
rem ASH.Server.exe 만 교체한다(appsettings.json 과 Releases:Dir 의 배포 파일은 그대로 둔다)
sc start ASH.Server
```

교체한 뒤(자동이든 수동이든) 설정 페이지에서 새 버전이 보이는지 확인한다(서명 키를 다시 쓰는 경우에는 **서명 키 ID가 교체 전과 같은지**도 본다. 다르면 새 exe가 다른 `update-key.pub`로 빌드된 것이니 되돌린다). 손으로 되돌릴 때는 서비스를 멈추고 `ASH.Server.exe.old`를 `ASH.Server.exe`로 바꾼 뒤 시작한다.

### 에셋: 차량 그림 (0.22.0)

`/admin/types`의 에셋 절에서 타입마다 3D 파일(STL 또는 OBJ)을 올리면 서버가 부품별 상자로 단순화해 AutoMod `.cel`과 미리보기를 만든다. 올릴 수 있는 크기는 `Catalog:AssetMaxMb`(기본 50)다. STEP(`.stp`/`.step`)은 서버에 변환기가 없으니 CAD에서 STL로 내보내 올린다. 플러그인의 `AGVEXCEL`이 만드는 입력 파일에는 `AGV_Types`의 Asset 열 드롭다운용 숨김 `_lists` 시트가 생기며, Asset을 비우면 기본 차량 그림(Length×Width 상자)을 쓴다. 결과 화면 입력 탭의 AGV 타입 표에 타입별 에셋 이름이 보인다. 확인 항목은 `docs/plugin-manual-test.md`의 AS1~AS7.

### 영상 촬영 (ffmpeg·AutoView, 0.19.0)

웹의 컷 편집기(`/analyses/{id}/cuts`)에서 내보낸 영상은 서버가 작업 사이에 만든다(컷마다 AutoView 한 번 → ffmpeg로 자막·이어 붙이기 → MP4 yuv420p faststart). 서버 PC에 아래가 필요하다. 없으면 영상만 실패하고 작업 실행은 영향이 없다.

1. **ffmpeg** — libx264와 drawtext(libfreetype)가 들어 있는 최신 빌드(gyan.dev의 full/essentials 등). 서버 서비스 계정의 `PATH`에 두거나 `Film:FfmpegPath`에 전체 경로를 적는다. AutoView도 MP4 변환에 PATH의 ffmpeg를 쓰므로, 전체 경로를 적으면 서버가 그 폴더를 AutoView 실행 환경의 PATH에 붙여 준다. 확인: `ffmpeg -hide_banner -encoders | findstr libx264`, `ffmpeg -hide_banner -filters | findstr drawtext` (둘 다 한 줄 이상 나와야 한다).
2. **AutoView** — AutoMod 폴더의 `bin` 안에서 찾는다: AutoMod 12.6 이상은 `aview64.exe`, 12.4는 `aview.exe`(둘 다 있으면 `aview64.exe`). 다른 곳에 있으면 `Film:AviewExe`에 전체 경로를 적는다. AutoView 라이선스가 모델 실행과 같은 라이선스를 쓰므로 서버 PC에서 AutoMod 라이선스가 AutoView도 허용하는지 확인한다.
3. **로그온 세션 대리 실행기(필수, 0.26.0)** — 서비스는 계정과 상관없이 세션 0(화면 없음)에서 돌아 AutoView가 그리지 못한다(진단 `서버 세션 0`, `보이는 창 없음`, 프레임 JPG 0개). 그래서 서비스는 그대로 두고 AutoView 렌더만 로그온한 사용자 세션의 `ASH.Server.exe --film-helper`가 한다. 영상용 계정으로 서버 PC에 로그온해 **관리자 권한 명령 프롬프트**에서 한 번 등록한다:
   ```
   "C:\Program Files\ASH.Server\ASH.Server.exe" --install-film-helper
   ```
   - 다른 계정을 쓰려면 끝에 `도메인\계정`을 붙인다. 등록은 ① 그 계정에 `Jobs:Dir` 수정 권한(icacls, AutoView가 서비스가 만든 작업 폴더에 써야 함)과 설치 폴더 읽기·실행 권한(관리자 전용으로 잠근 폴더의 exe를 실행해야 함. 그래서 이 계정은 `appsettings.json`의 관리자 암호도 읽을 수 있으니 영상 전용 계정을 쓴다) ② 작업 스케줄러 `ASH.FilmHelper`(로그온할 때, 사용자가 로그온한 경우에만, 보통 권한) ③ 지금 바로 시작을 한다. 해제: `--uninstall-film-helper`.
   - 서비스와 대리 실행기는 `Jobs:Dirilm-helper\`의 파일로 주고받는다(`heartbeat.txt` 2초마다, 요청 `req-*.json`, 결과 `res-*.json`, 기록 `helper.log`). 대리 실행기는 AutoView·AutoMod·ffmpeg 경로를 `appsettings.json`에서 직접 읽고 `Jobs:Dir` 안의 폴더만 다룬다.
   - 대리 실행기가 없으면 서비스가 `schtasks /Run`으로 한 번 깨우고 30초 기다린다. 그래도 없으면 컷이 "로그온 세션의 AutoView 대리 실행기가 돌고 있지 않습니다"로 바로 실패한다(2분 동안 다시 기다리지 않음).
   - 서버 자동 업데이트로 exe가 바뀌면 대리 실행기는 하던 렌더를 마치고 끝나고, 다음 렌더 때 서비스가 새 exe로 다시 깨운다.
   - 그 계정은 로그온을 유지한다(재부팅 뒤 자동 로그온이 필요하면 전용 계정에만 설정). 원격 데스크톱 연결을 끊거나 화면을 잠그면 그리지 못할 수 있으니, 원격으로 들어갔다면 로그오프하지 말고 콘솔로 돌려놓고 나온다.
   - 서버를 서비스가 아니라 로그온한 콘솔에서 실행하면(세션 1 이상) 대리 실행기 없이 직접 그린다.
4. 컷 하나가 `Film:CutTimeoutMin`(기본 20분)을 넘으면 AutoView를 끊고 그 컷만 실패한다. 영상 페이지 `/films/{id}`에 컷별 상태와 `film.log`가 나온다. F0(화면 배율 기준값)는 영상마다 자동 보정 컷(1프레임 렌더 2번)으로 정한다. 보정 렌더 한 번의 제한은 `Film:CalibrationTimeoutMin`(기본 3분)이다. 넘으면 영상을 멈추지 않고 추정값으로 계속하며, 영상 페이지에 "주의: 화면 배율 보정 실패, 추정값 사용 — <진단>" 경고가 붙는다. AutoView가 정말 멈춘 상태면 바로 다음 컷도 시간 초과가 되고, 연속 2회 시간 초과 규칙으로 영상이 멈춘다("AutoView가 응답하지 않아 중단했습니다").
5. **대화상자 자동 클릭** — AutoView가 띄우는 창 가운데 제목에 `Film:DialogTitles`(쉼표 구분, 제목의 일부, 대소문자 무시)가 든 창의 OK(한글 Windows는 `확인`)를 누른다. 취소는 누르지 않는다. 기본값은 `Attention,비디오 압축,Video Compression`이다. AutoView 12.4는 동영상을 만들 때 `Attention!`("…Your AVI file will be generated…") 다음에 Windows의 `비디오 압축`(영문 Windows: `Video Compression`, 압축 프로그램 Microsoft Video 1) 창을 띄운다. 0.19.1까지는 `Attention`만 눌러 이 둘째 창에서 멈춘 것으로 보인다("화면 배율 보정 중 시간 제한 초과", 서버 화면 사진으로 확인). 다른 창에 걸리면 아래 진단의 `창 "제목"`을 보고 그 제목을 더한다(재시작 없이 다음 렌더부터 적용).
6. **AutoView 14 미만(12.6 x32 `aview.exe` 12.6.1.12, 12.4)** — 이 버전들의 카메라 스크립트 문법에는 `format mp4`와 `upvector`가 없다(12.6.1.12 실행 파일 안의 문법표로 확인: 동영상 형식은 AVI·MPEG·custom, 시점은 `pos`·`relative to`·`scale`만). 서버는 AutoView 파일 버전이 14 미만이면(버전을 못 읽으면 이름이 `aview.exe`일 때) `format avi`·`filename "<컷>.avi"`로 쓰고 `upvector`를 빼며, 바로 위에서 보는 시점은 카메라를 높이의 1/1000만큼 비켜 화면 위쪽 방향을 정한다(기울기 0.06°). 회전(roll)은 이 버전에서 줄 수 없다. 오류 `camera.des line N syntax error: mp4`/`upvector`는 0.26.1까지의 이 문제다. 이어서 12.4 설명: 12.4는 MP4가 아니라 AVI를 만든다. 서버는 `<컷>.mp4`가 없으면 `<컷>.avi`, 그것도 없으면 그 실행 동안 작업 폴더에 새로 생긴 AVI(12.4 창에는 `model.avi`로 나옴)를 받아 ffmpeg로 MP4(H.264 yuv420p)로 바꾼다. `film.log`에 "AutoView가 AVI를 만들었습니다"가 남는다. **이 AVI 경로는 12.4가 없는 개발 PC에서 확인하지 못했다**(14.5의 AVI 출력은 빈 화면이었다). 12.4 서버에서 영상을 한 편 만들어 화면이 제대로 나오는지, 크기가 요청한 해상도(예: 1280×720)인지 확인한다. 12.4가 영상 크기 설정을 무시하고 640×480으로 만들면 컷의 화면 범위와 F0 보정이 어긋날 수 있다. 12.4의 `aview.exe`가 `-n`(스크립트 끝나면 종료) 옵션을 받는지도 확인되지 않았다. 바로 끝나면 진단의 `출력 "…"`(AutoView의 표준 출력)과 `aviewout.dat`를 본다.

**진단 읽는 법.** AutoView 실행이 시간 제한에 걸리거나 동영상 없이 끝나면 오류 끝에 `진단:` 한 줄이 붙는다(영상 페이지의 컷 오류·경고와 `film.log`). `film.log` 첫머리에는 영상마다 `AutoView: <경로> (버전 x)`가 남는다. 예:

```
시간 제한(3분)을 넘어 AutoView를 끊었습니다(대화상자에 걸렸거나 너무 긴 컷). 창 제목이 보이면 설정 Film:DialogTitles에 더합니다.
진단: AutoView C:\AutoMod\12_4\bin\aview.exe (버전 12.4.0.0) · 창 "비디오 압축" [#32770] 버튼 확인/취소/구성(F).../정보(A)... (aview.exe) / "AutoView - m" [...] (aview.exe)
· 서버 세션 1 · camera.des 있음 · aviewerr.dat 없음 · aviewout.dat "…Camera script duration is 1 seconds" · cal_on.mp4 없음 · avi 없음
· convert.bat 없음 · 프레임 JPG 0개 · ffmpeg C:\ffmpeg\bin\ffmpeg.exe · 누른 창 Attention!
```
(실제로는 한 줄. 버전 숫자는 예시다.)

| 항목 | 뜻과 조치 |
|---|---|
| `AutoView <경로> (버전 …)` | 실제로 실행한 AutoView. 12.4면 `aview.exe`. 버전이 "정보 없음"이면 다른 파일이다. |
| `창 "제목" [클래스] 버튼 … 내용 "…" (exe)` | 끊을 때 AutoView(와 자식 프로세스)에 떠 있던 창. 대화상자 제목을 `Film:DialogTitles`에 더한다. `보이는 창 없음`이면 창 없이 멈춘 것(서비스 세션 등). |
| `서버 세션 0(서비스: 화면 없음)` | 대리 실행기를 거치지 않고 서비스가 직접 그렸다. 3번의 `--install-film-helper`로 등록한다. 대리 실행기를 거친 렌더는 그 계정의 세션 번호(1 이상)가 나온다. |
| `aviewerr.dat "…"` | 카메라 스크립트 오류(그 버전의 AutoView가 모르는 문장 등). |
| `aviewout.dat "…"` | AutoView의 스크립트 해석 기록 끝부분. |
| `<컷>.mp4 있음/없음`, `avi …` | AutoView가 만든 동영상. |
| `convert.bat`, `프레임 JPG N개` | 14.x의 MP4 변환 단계(프레임 JPG → convert.bat → ffmpeg). JPG는 있는데 MP4가 없으면 변환(ffmpeg·PATH)이 실패한 것. |
| `ffmpeg <경로>` / `ffmpeg 없음` | AutoView 실행 환경의 PATH에서 찾은 ffmpeg. |
| `누른 창 …` | 이번 실행에서 서버가 OK를 누른 창. |
| `출력 "…"` | AutoView 자신의 표준 출력·오류(사용법 안내 등). |

| 설정 | 기본 | 뜻 | 적용 |
|---|---|---|---|
| `Film:FfmpegPath` | `ffmpeg` | ffmpeg 실행 파일(PATH에서 찾거나 전체 경로) | 바로 |
| `Film:AviewExe` | (빈칸 = `<Jobs:AmHome>\bin`에서 `aview64.exe`, 없으면 `aview.exe`) | AutoView 실행 파일 | 바로 |
| `Film:CutTimeoutMin` | 20 | 컷 하나의 AutoView 시간 제한(분, 1~600) | 바로 |
| `Film:CalibrationTimeoutMin` | 3 | 화면 배율 보정 렌더 한 번의 시간 제한(분, 1~60). 넘으면 추정값으로 계속 | 바로 |
| `Film:DialogTitles` | `Attention,비디오 압축,Video Compression` | OK(확인)를 누를 AutoView 창 제목(쉼표 구분, 제목의 일부, 2글자 이상) | 바로 |

모두 웹 설정 페이지의 "영상 촬영" 구역에서 바꾼다.

### 컷 미리보기 (0.25.0)

컷 편집기의 "미리보기"는 컷의 첫 장면 1장(1280×720)을 AutoView로 그린다. 화면에 있는 값 그대로 그리지만 서버에 저장된 컷이어야 한다(`POST /api/analyses/{id}/cuts/{cutId}/preview`, 같은 사이트에서 온 요청만).

- **대기열을 같이 쓴다.** 작업·영상과 같은 일꾼 하나가 하므로 AutoMod와 AutoView가 동시에 돌지 않는다. 일꾼은 작업 하나를 끝낼 때마다 영상 일을 하나(장면 후보 찾기, 없으면 컷 미리보기, 없으면 영상 한 편) 하고 다시 작업으로 돌아간다. 큰 작업이 돌고 있으면 미리보기는 그 뒤에 한다(화면에 앞선 건수가 나온다). 미리보기가 돌면 그만큼 작업 대기열이 늦어진다. `Jobs:WorkerEnabled`가 꺼져 있으면 409다.
- 처음 한 번은 모델을 돌려 짧은 구간(컷 시각 앞 2초~뒤 3초)만 기록한다. 같은 버전·대수·구간이면 모델을 다시 만들지 않으므로 시점만 바꿔 다시 그리는 것은 AutoView 시간만 든다.
- **디스크:** 분석마다 `Jobs:Dir\analyses\<id>\previews\`에 컷별 `<컷>.json`·`<컷>.jpg`와 `work\`(가장 최근 미리보기의 모델 복사본, 빌드 결과, 짧은 기록)가 하나 남는다. `work\` 크기는 모델에 따라 다르다. `KeepRunsDays` 정리 대상이 아니고 분석을 지울 때 함께 지워진다.
- AutoView를 못 찾으면 503 "AutoView 없음"이다(`Film:AviewExe`, `Jobs:AmHome`). 시간 제한은 영상 컷과 같은 `Film:CutTimeoutMin`이다. 서버가 다시 시작되면 돌던 미리보기는 실패로 남는다("다시 누르세요").

### 보고서 (0.25.0)

분석 화면의 "보고서 받기"(`/analyses/{id}/report`)가 인쇄용 화면을 연다. 서버 설정은 필요 없다.

- 버전은 `?job=`으로 고른다. 없으면 분석의 선정 버전, 선정이 없으면 가장 최근 완료 버전이다. 결과가 나온 버전이 없으면 화면이 그렇게 알린다. `?map=all`은 병목 지도에 배치 전체를 보인다.
- **PDF로 저장**은 브라우저의 인쇄다(A4 가로, 구획마다 새 쪽: 표지·안별 결론, 그룹별, 자재별, 병목 지도, 영상). 서버가 PDF를 만들지 않는다.
- **PPT**(`/analyses/{id}/report.pptx?job=`)는 요청 때 서버가 만든다. 16:9, 파워포인트 차트(데이터 편집 가능)와 도형이다. 영상 썸네일의 링크는 요청을 받은 주소(`scheme://host`)로 만든다. 그래서 사용자가 연 주소(서버 이름 또는 IP, http 또는 https)가 그대로 링크에 들어간다. 서버 PC에 PowerPoint는 필요 없다.
- **Excel**은 `/jobs/{id}/files/report.xlsx`다(작업이 끝난 뒤 처음 요청할 때 만든다).

### 대수 바꿔 다시 돌리기 (0.25.0)

작업(버전) 화면 결론 탭의 "대수 바꿔 다시 돌리기"는 `POST /jobs/{id}/rerun`(폼 `n.<그룹>` = 대수)이다. 탐색 없이 그 대수로 확인 실행만 하는 새 작업을 만들어 같은 분석·안의 다음 버전으로 넣는다.

- **권한은 취소와 같다.** 그 작업을 보낸 PC(같은 IP)이거나 관리자 암호(폼 `token` 필드 또는 `X-ASH-Admin-Token` 헤더)가 맞아야 한다. 아니면 403이다. 화면의 상자는 보낸 PC에서만 보인다. 관리자는 헤더나 `token`으로 직접 POST한다. 다른 사이트에서 온 요청은 거절한다.
- 완료되어 결론 대수가 있는 작업만 된다(409). `Jobs:Enabled`가 꺼져 있으면 403이다. 대수는 그룹의 시험 범위 안의 정수여야 하고(아니면 400), 하나도 안 바뀌면 400이다.
- 새 작업은 원래 작업이 올린 `upload.zip`을 복사해 만든다(`runs/`는 뺀다). 작업 폴더에 모델 패키지 크기만큼 디스크가 더 든다. `upload.zip`이 없으면 410이다. 실행 원본이 `runs.zip`으로 압축된 뒤에도 된다.
- **모델은 그때 올린 그대로다.** 시간 내역 같은 새 출력이 없는 옛 모델은 다시 돌려도 없다. `AGVMODEL`로 다시 만들어 `AGVSEND`한다.

### 완료·실패 알림 (0.25.0)

내 작업·영상이 끝나거나(완료·실패·취소) 실패하면 알린다. 모든 화면 사이드바의 종 버튼이 상태를 보이고, 사용자가 누를 때만 브라우저에 알림 허용을 묻는다.

- **어떻게 알아내나:** 열려 있는 ASH 화면이 30초마다(탭이 다시 보일 때도) `GET /api/me/events?since=<마지막으로 본 서버 시각>`을 부른다. 서버는 그 뒤에 끝난 "내" 작업·영상을 돌려준다(최대 20건, 하루보다 오래된 것은 되풀이하지 않음). "내" = 홈의 "내 것"과 같은 규칙(같은 IP 또는 같은 보낸 사람 이름, 로그인 없음). 영상은 내 작업이 들어 있는 분석의 것이다. 마지막으로 본 시각은 브라우저(`localStorage`)에 있어 새로 고쳐도 되풀이하지 않고, 처음 연 화면은 그때부터 센다.
- **http 인트라넷 주소(`http://서버:8082`)에서는 시스템 알림이 안 된다.** 브라우저가 보안 연결이 아닌 사이트의 알림을 막는다(ASH는 `window.isSecureContext`와 `Notification`만 본다). 대신 열려 있는 ASH 화면 안에 토스트로 알리고(10초), 그 탭이 가려져 있으면 탭 제목에 "(n)"을 붙인다. 종 버튼은 "화면 안 알림"으로 보인다. 서버 설정은 필요 없다.
- **시스템 알림을 쓰려면 둘 중 하나를 한다.**
  1. **서버를 https로 연다.** 사용자 PC가 신뢰하는 인증서(사내 CA 등)가 필요하다. `appsettings.json`에 https 주소와 `Kestrel:Certificates:Default:Path`/`Password`를 넣고 서비스를 다시 시작한다.
     ```json
     "Urls": "http://0.0.0.0:8082;https://0.0.0.0:8443",
     "Kestrel": { "Certificates": { "Default": { "Path": "C:\\ASH\\server.pfx", "Password": "<암호>" } } }
     ```
     사용자는 인증서 이름과 같은 주소 `https://서버:8443/`로 접속한다. 방화벽에 8443도 연다. 주의:
     - `install-service.bat`은 `Urls`의 **첫 포트**만 방화벽에 열고 응답을 확인한다. http 주소를 첫째로 두고 https 포트는 손으로 연다.
     - `Urls`에 https만 두면 서버 자동 업데이트가 새 버전 응답 확인(`/healthz/version`, http 전용)을 건너뛴다(`update\apply.log`에 "no http address, health check skipped"). 새 버전이 안 떠도 자동으로 되돌리지 못한다.
     - 인증서 파일과 암호는 `appsettings.json`처럼 관리자만 읽게 잠근다(1절). `Kestrel`은 웹 설정에 없고 파일에서 직접 고친다.
     - ZWCAD가 여는 작업 페이지도 https이려면 플러그인 서버 주소(`AGVSERVER`)를 https로 바꾼다. 인증서를 신뢰하지 않으면 플러그인이 "서버 인증서 오류"를 낸다(6절).
  2. **PC의 Chrome/Edge 기업 정책**으로 그 http 주소를 보안 연결로 취급하게 한다. 정책 `OverrideSecurityRestrictionsOnInsecureOrigin`의 목록에 `http://서버:8082`를 넣어(그룹 정책 또는 레지스트리) 배포하고 브라우저를 다시 시작한다. 서버는 그대로 http다. 이 정책은 브라우저 동작이라 이 저장소에서 시험하지 않았다. 적용한 PC에서 종 버튼이 "알림 켜기"로 바뀌는지 확인한다(`docs/plugin-manual-test.md`의 NT 항목).
- **허용한 뒤:** 종 버튼이 "알림 켜짐"이 된다. 브라우저에서 차단했다면 "알림 차단됨"이고, 주소창의 사이트 설정에서 알림을 허용으로 바꾼다.
- **한계:** ASH 화면이 한 곳이라도 열려 있어야 알린다. 모든 탭을 닫은 뒤에도 알리는 진짜 푸시는 브라우저 제조사의 외부 푸시 서비스를 거쳐야 해서 지원하지 않는다. 사내 서버만으로는 할 수 없다.

### 백업과 복원

- 백업 대상: **`Releases:Dir` 폴더 전체**(`current\`, `files\`, `history\`)와 **`appsettings.json`**(관리자 암호·설정). 서버 exe는 zip에서 다시 얻을 수 있으니 필수가 아니다. 서명 키를 쓰는 경우 개인키는 서버에 없다(2절 "선택: 서명 키 사용").
- 복원: 서비스를 멈추고 두 가지를 제자리에 복사한 뒤(`Releases:Dir`는 3절의 권한을 다시 확인) `sc start ASH.Server`. 설정 페이지에서 현재 버전·이전 버전 목록을 확인한다.

### `files\` 정리

업로드할 때마다 `Releases:Dir\files\`에 `ASH-Setup-<버전>.exe`가 쌓인다. 디스크가 부족하면 오래된 파일을 직접 지우되 **현재 버전과 롤백 대상(바로 이전 버전 등)은 남긴다.** `history\`의 매니페스트가 가리키는 설치 파일이 없으면 그 버전은 내려받을 수 없다.

## 6. 문제 해결

| 증상 | 원인·조치 |
|---|---|
| `AGVUPDATE`에 "업데이트 중단(github.com에 접속할 수 없어 배포 정보를 대조하지 못했습니다)" | 사용자 PC가 github.com에 HTTPS로 접속하지 못함(방화벽·프록시·DNS). 브라우저로 `https://github.com/Leepy0/ASH/releases`가 열리는지 보고, release-assets.githubusercontent.com / objects.githubusercontent.com도 허용한다. PC의 프록시 설정(로그인 계정 자격 증명)을 쓰므로 그 계정으로 확인한다. |
| "서버의 ASH.json이(가) github.com v…과 다릅니다" | 서버에 GitHub 릴리스와 다른 파일이 게시됨(수동 업로드 등). 서버가 GitHub 릴리스로 다시 받게 하거나(서비스 재시작) 새 버전을 릴리스한다. 이 상태에서는 설치되지 않는다. |
| 업로드가 400 "minSupported(…)가 version(…)보다 높습니다" | `-MinSupported`/`ASH_MIN_SUPPORTED`가 이번 버전보다 높음. |
| 설정 페이지 "지난 업데이트: x.y.z이(가) 60초 안에 응답하지 않아 … 자동 되돌림" | 새 서버 exe가 서비스로 뜨지 않음. `update\apply.log`와 이벤트 로그를 보고, 고친 판을 더 높은 버전으로 릴리스한다(같은 버전은 다시 시도하지 않음). |
| 설정 페이지 "지난 업데이트: x.y.z 파일 교체 실패" | 설치 폴더에 쓸 수 없음(LocalService 계정, 백신 잠금 등). `update\apply.log` 확인. 원인을 고친 뒤 더 높은 버전으로 릴리스하거나 수동 교체한다. |
| 설정 페이지 "마지막 확인 — 실패: …" (이름 확인 오류, "No such host is known" 등) | 서버 PC의 DNS가 github.com을 찾지 못함. `nslookup github.com 8.8.8.8`처럼 쓸 수 있는 DNS를 찾아 `GitHub:Dns`에 적고 서비스를 재시작한다. |
| 설정 페이지 "Runner(ash-run) … 거절" | 받은 zip이 `ASH.Runner.json`의 크기·SHA-256과 다름. 릴리스를 다시 만들어 새 버전으로 낸다. |
| 분석 PC에서 `update skipped: …` | 서버 주소 없음/응답 없음, github.com 접속 불가 또는 서버의 `ASH.Runner.json`이 github.com과 다름, 폴더에 쓸 수 없음 등. 문장 그대로 원인이 나온다. 작업은 지금 버전으로 계속된다. `ash-run --update --server <주소>`로 원인을 다시 확인한다. |
| 설정 페이지 "마지막 확인 — 실패: …" | 서버 PC에서 github.com에 닿지 않음. 브라우저로 `https://github.com/Leepy0/ASH/releases/latest/download/ASH.json`이 받아지는지 보고, 콘솔에서는 되는데 서비스에서만 실패하면 `GitHub:Proxy`를 적는다. 이벤트 로그에 같은 내용이 남는다. |
| 설정 페이지 "마지막 확인 — GitHub에 ASH.json이(가) 있는 릴리스가 없습니다" | 최신 정식 릴리스에 `ASH.json`이 빠졌거나 초안·시험판만 있음. |
| 업로드가 403 "업로드가 꺼져 있습니다" | `Admin:Password`가 4~20자 규칙에 맞지 않음(공백·한글 포함 등). 고친 뒤 서비스 재시작. |
| 업로드가 401 | 관리자 암호가 다름(`$env:ASH_ADMIN_TOKEN` 확인). |
| (서명 키를 다시 쓸 때만) 경고 "서명 키 미설정", 업로드 400 "서명 키 없음"/서명 거절, `build-release.ps1` 중단, "업데이트 확인 실패(서명 확인 실패)", 설정 페이지 "거절: 서명이 맞지 않습니다" | `update-key.pub`와 `ASH_SIGNING_KEY`(개인키)가 짝이 맞는지, 서버·플러그인·ash-run이 그 공개키로 빌드된 판인지 확인한다. 키 교체 중이면 4절. |
| 영상 컷이 "AutoView가 동영상(MP4/AVI)을 만들지 않았습니다"로 실패 | 서버 PATH에 ffmpeg가 없거나 AutoView가 서비스 세션에서 그리지 못함, 또는 이전 AutoView(12.4)가 다른 방식으로 만듦. 오류 끝의 `진단:`(5절 "영상 촬영"의 "진단 읽는 법")에서 AutoView 버전·창·파일·ffmpeg를 본다. |
| 영상이 "시간 제한…"이나 "화면 배율 보정 실패, 추정값 사용"으로 끝남 | AutoView가 대화상자에 걸렸을 가능성이 크다. `진단:`의 `창 "제목"`을 `Film:DialogTitles`에 더하고 다시 찍는다. |
| 컷 미리보기가 503 "AutoView 없음" | 서버에서 AutoView를 못 찾음. `Film:AviewExe` 또는 `Jobs:AmHome`의 `bin\aview64.exe`/`aview.exe`를 확인한다(5절 "영상 촬영"). |
| 종 버튼이 "화면 안 알림"이고 시스템 알림이 안 뜸 | 접속 주소가 http라서 정상이다. 시스템 알림이 필요하면 5절 "완료·실패 알림"의 https 또는 기업 정책. |
| 대수 다시 돌리기가 403 / 410 | 403: 보낸 PC가 아니고 관리자 암호도 없음(또는 `Jobs:Enabled` 꺼짐). 410: 그 작업의 `upload.zip`이 없음. |
| 다른 PC에서 접속이 안 되고 서버 PC 자신에서는 됨 | 방화벽 규칙이 Public 프로필 NIC에 적용되지 않음. 1절 5번의 함정 참고. |
| 서비스 로그가 이벤트 뷰어에 없음 | LocalService는 이벤트 원본을 못 만든다. 1절 "로그 위치" 참고. |
| "업데이트 확인 실패(서버 응답 없음)" | 서비스가 꺼졌거나(`sc query ASH.Server`) 방화벽·주소·포트 오류. 사용자 PC에서 `curl http://<서버>:8082/healthz`가 `ok`인지 본다. 3초 안에 답이 없으면 이 메시지가 나온다. |
| "업데이트 확인 실패(서버 인증서 오류 — 사내 인증서 신뢰 여부 확인)" | https 주소인데 사용자 PC가 서버 인증서(사내 CA)를 신뢰하지 않음. 사내 루트 인증서를 PC에 배포하거나 http 주소 사용. |
| 프록시 | 서버(`ASH.Server`) 접속은 시스템 프록시를 쓰지 않고(`UseProxy=false`) 직접 한다. 서버가 프록시 뒤에만 있다면 사내 DNS/라우팅으로 직접 닿게 해야 한다. github.com 대조는 반대로 PC의 프록시 설정(로그인 계정 자격 증명)을 쓴다. |
| "다운로드 시간 초과(5분)" | 설치 파일(약 0.5 MB~)을 5분 안에 못 받음. 네트워크 확인 후 `AGVUPDATE` 다시. |
| "다운로드 실패(…이전 설치 프로그램이 실행 중이면 닫고…)" | 같은 이름의 `ASH-Setup`이 실행 중. 닫고 다시. |
