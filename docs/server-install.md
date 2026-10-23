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
   - `Admin:Token` — **선택.** 수동 업로드(`publish-release.ps1`)를 쓸 때만 넣는다. 16자 이상의 긴 무작위 문자열. 비워 두면 수동 업로드만 꺼지고 GitHub 가져오기는 그대로 동작한다.
   - `Urls` — 기본 `http://0.0.0.0:8082`. 포트를 바꾸면 아래 방화벽·`/server:` 주소도 같이 바꾼다.
   - `Releases:Dir` — 배포 파일 보관 폴더(기본 `%ProgramData%/ASH.Server/releases`). **3번에서 권한을 먼저 잠근 폴더**여야 한다.
   - 설정 파일(토큰을 넣었다면 토큰 포함)을 보호하도록 설치 폴더 권한도 관리자로 제한한다(`Program Files` 기본값은 일반 사용자 읽기 가능).
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
6. 다른 PC의 브라우저로 `http://<서버>:8082/` 를 열어 상태 페이지를 확인한다. 서비스 시작 직후에는 "게시된 버전이 없습니다."가 정상이고, 10초쯤 뒤 새로 고치면 "마지막 확인 … 가져옴: x.y.z"와 현재 버전이 보인다. "실패: …"가 보이면 서버 PC에서 github.com 접속과 `GitHub:Proxy`를 확인한다. "무결성 확인: 없음 — PC가 github.com 릴리스와 대조해 확인"이 보이는 것이 정상이다.

수동 업로드의 토큰은 평문 HTTP로 전달된다. 수동 업로드는 신뢰할 수 있는 사내망에서만 하거나 앞단에 HTTPS 프록시를 둔다.

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
- 서버가 GitHub에 닿지 않는 곳이면 수동 업로드를 쓴다(서버 `Admin:Token` 필요):
  ```powershell
  $env:ASH_ADMIN_TOKEN = '<서버 appsettings.json 의 Admin:Token>'      # 명령줄·기록에 남기지 않으려고 환경 변수로
  scripts\publish-release.ps1 -Server http://<서버>:8082
  ```
  서명 없이 받지만, PC는 서버의 매니페스트를 github.com의 `v<버전>` 릴리스와 대조하므로 GitHub 릴리스와 같은 파일을 올려야 설치된다.

- 버전은 `Directory.Build.props`의 `<Version>`이다(올리지 않으면 서버가 같은/낮은 버전을 거절한다).
- 서명은 하지 않는다(선택 기능, 아래 "선택: 서명 키 사용"). `-AllowUnsigned`/`-RequireSignature`는 이제 의미 없는 옵션으로, 경고만 낸다. `update-key.pub`가 있는데 `ASH_SIGNING_KEY`가 없으면 빌드가 중단된다.
- 빌드는 항상 처음부터 다시 컴파일하고(`--no-incremental`) `dotnet test ASH.sln`(서버 테스트 포함)을 통과해야 패키징한다.
- `-MinSupported <버전>`(없으면 `ASH_MIN_SUPPORTED`, 그것도 없으면 `0.0.0`)은 그 버전 **미만** PC에 "최소 지원 미만" 안내를 띄운다. 기본값은 "아무도 미만이 아님"이다 — 이번 버전으로 자동 설정하지 않는다. 이번 버전보다 높게는 줄 수 없다(서버도 거절).
- 게시 뒤 상태 페이지(`http://<서버>:8082/`)에서 버전, "무결성 확인" 줄, "마지막 확인" 결과를 확인한다.

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

홈(`http://<서버>:8082/`)은 위에서부터 지금 실행 중인 작업·대기 건수·배포 버전·서버 버전 카드, 최근 작업 5건(전체는 `/jobs`), 배포(현재 버전, 설치 파일 받기, Runner, 이전 버전), 서버 상태(자동 업데이트, GitHub 가져오기, 무결성 확인, 수동 업로드, 서버 시각) 순서다. 상단 메뉴는 홈 · 작업 · 설정.

### 웹 설정과 서비스 재시작 (`/admin/settings`)

`appsettings.json`의 값을 브라우저에서 바꾼다. `Admin:Token`(16자 이상)이 비어 있으면 페이지가 꺼져 있다.

- **로그인:** 토큰을 한 번 입력하면 서버가 무작위 세션 ID를 만들어 `HttpOnly`, `SameSite=Strict`, `Path=/admin` 쿠키로 준다(8시간, 서버 메모리에만 있어 재시작하면 다시 로그인). 토큰 자체는 브라우저에 저장되지 않고 페이지에 다시 나오지 않는다. 틀리면 1초 늦게 거절한다.
- **위조 방지:** 저장·재시작·로그인 POST는 `Origin`(없으면 `Referer`)이 이 서버 주소일 때만 받는다. 스크립트는 `X-ASH-Admin-Token` 헤더로 같은 주소에 POST할 수 있다(`/api/admin/releases`와 같은 토큰).
- **바꿀 수 있는 값과 적용 시점:**

| 바로 적용(다음 작업·다음 정리부터) | 서비스 재시작 필요 |
|---|---|
| `Jobs:Enabled`, `Jobs:AmHome`, `Jobs:Parallel`, `Jobs:DialogButton`, `Jobs:RunTimeoutMin`, `Jobs:BuildTimeoutMin`, `Jobs:JobTimeoutHours`, `Jobs:KeepRunsDays`, `Film:CutTimeoutMin` | `Jobs:WorkerEnabled`, `Jobs:Dir`, `Film:FfmpegPath`, `Film:AviewExe`, `GitHub:Enabled`, `GitHub:BaseUrl`, `GitHub:CheckIntervalMin`, `GitHub:UpdateServer`, `GitHub:Proxy`, `GitHub:Dns`, `Urls`, `Releases:Dir`, `Update:AllowUnsigned` |

  `Admin:Token`, `Update:PublicKeyPem`, `Logging`은 웹에서 바꾸지 않는다(서버 PC에서 파일을 직접 고친다).
- **저장:** 모든 값을 먼저 검사한다(정수 범위, 켬/끔, `Urls`·`GitHub:BaseUrl`·`GitHub:Proxy`·`GitHub:Dns` 형식). 하나라도 틀리면 아무것도 쓰지 않는다. 통과하면 파일을 다시 읽어 바뀐 키만 고치고(모르는 키는 그대로) 임시 파일로 쓴 뒤 `File.Replace`로 바꿔 넣으며, 이전 파일은 `appsettings.json.yyyyMMdd-HHmmss.bak`으로 남는다. 파일 안의 `//` 주석은 저장 때 없어진다(.bak에는 남음). `Jobs:AmHome`에 AutoMod 빌드 exe가 없으면 경고만 한다.
- **재시작:** "서비스 다시 시작"은 자동 업데이트의 `update\apply-update.bat`을 파일 교체 없이(stage `-`) 실행해 `sc stop` → 이 프로세스가 끝나기를 기다림 → `sc start`를 한다(기록은 `update\apply.log`). 서비스는 자기 자신을 동기적으로 다시 시작할 수 없어서 분리된 배치가 한다. 실행 중인 작업이 있으면 확인란을 체크해야 하며 그 작업은 "서버가 다시 시작되어 중단되었습니다"로 실패 처리된다. 콘솔로 실행 중이거나 등록된 서비스의 exe가 다르면 버튼 대신 이유가 나온다.
- 서비스 계정이 설치 폴더에 쓸 수 없으면(LocalService) 저장이 실패하고 파일은 바뀌지 않는다.

### 관리자의 분석 삭제 (`/admin/analyses/{id}/delete`)

분석 페이지 맨 아래 "관리자: 분석 삭제…" 링크(누구에게나 보임)가 이 확인 페이지로 간다. 페이지가 로그인을 요구한다(`/admin/settings`와 같은 토큰·쿠키, 로그인 뒤 이 페이지로 돌아온다). 보낸 작업이 없는 분석은 예전처럼 누구나 분석 페이지에서 바로 지울 수 있고, 작업이 있는 분석은 관리자만 지운다.

- **결과:** 분석의 작업은 **지워지지 않고** "분석에 넣지 않은 작업"이 된다(안·버전·선정은 해제, 버전 메모는 유지). 분석과 안, 컷 목록(`analyses\<id>\cuts.json`), 이 분석의 영상 기록과 영상 파일(`films\<id>\`)은 지워지고 되돌릴 수 없다.
- **확인:** 분석 이름을 그대로 입력해야 지워진다(다르면 400, 아무것도 지우지 않음). POST는 `Origin` 검사와 관리자 로그인(또는 `X-ASH-Admin-Token` 헤더)이 필요하다.
- **영상이 대기 중이거나 만드는 중이면** 409로 거절한다. 영상 페이지에서 취소한 뒤 다시 한다.

### 서버 자동 업데이트 (AGV_FOTA_GATEWAY와 같은 방식)

서버는 GitHub 확인 때마다(`CheckIntervalMin`) 자기 버전도 확인해 새 버전이면 스스로 바꾼다.

1. 최신 릴리스의 `ASH.Server.json`을 HTTPS로 읽는다(GitHub에서 받은 것은 그대로 믿는다. 서명은 요구하지 않는다).
2. `ASH-Server-<버전>-win-x64.zip`을 받아 크기·SHA-256을 매니페스트의 값과 대조하고, 설치 폴더의 `update\v<버전>\`에 푼다.
3. `update\apply-update.bat`이 서비스를 멈추고, 지금 exe를 `ASH.Server.exe.prev`로 바꾼 뒤 새 파일을 복사한다. **`appsettings.json`은 덮어쓰지 않는다.** `Releases:Dir`의 배포 파일도 그대로다.
4. 서비스를 다시 시작하고 60초 안에 `http://127.0.0.1:<포트>/healthz/version`이 새 버전을 돌려주면 끝. 이전 exe는 `ASH.Server.exe.old`로 남는다.
5. 응답이 없으면 이전 exe로 **자동 되돌리고**(실패한 exe는 `ASH.Server.exe.bad`) 다시 시작한다. 그 버전은 `update\skip-auto.txt`에 적혀 다시 시도하지 않는다. 더 높은 버전을 릴리스하면 다시 시도한다.

결과는 상태 페이지의 "서버 프로그램 … 자동 업데이트"와 "지난 업데이트" 줄, `update\apply.log`, 이벤트 로그에 남는다.

자동 교체는 이 조건에서만 한다(아니면 상태 페이지에 이유가 나온다): `ASH.Server.exe --install`로 등록한 Windows 서비스로 실행 중, 배포용 단일 exe(zip의 것), `GitHub:Enabled`와 `GitHub:UpdateServer`가 true.

### Runner(ash-run) 배포

서버는 분석 PC의 `ash-run`이 스스로 업데이트하도록 Runner를 보관하고 내려 준다.

1. GitHub 확인 때마다 최신 릴리스의 `ASH.Runner.json`을 읽는다. 서버에 있는 Runner보다 높으면 `ASH-Runner-<버전>-win-x64.zip`을 받아, 크기·SHA-256을 확인한 뒤 `Releases:Dir\runner\`에 둔다(최신 2개만 보관).
2. 분석 PC의 `ash-run`은 작업을 시작하기 전에 `/api/ash/runner`(매니페스트)를 읽고 **github.com의 `https://github.com/Leepy0/ASH/releases/download/v<버전>/ASH.Runner.json`과 바이트 단위로 대조**한 뒤(같을 때만), 새 버전이면 `/download/ASH-Runner-<버전>-win-x64.zip`을 받아 스스로 교체한다(README의 "ash-run" 절). 서버가 Runner를 못 가져와도 지금 Runner는 계속 쓸 수 있고, 분석 PC가 github.com에 접속할 수 없거나 내용이 다르면 `update skipped: ...` 한 줄을 내고 지금 버전으로 계속한다.
3. 서버 상태 페이지의 "Runner(ash-run)" 줄에 보관 중인 버전과 마지막 확인 결과가 나온다.
4. 분석 PC에는 서버 주소가 필요하다: 설치 프로그램의 `/server:` 로 넣었다면 그 설정을 읽고, 아니면 `ash-run --update --server http://<서버>:8082` 또는 환경 변수 `ASH_SERVER`.
5. 릴리스에 `ASH.Runner.json`, `ASH-Runner-<버전>-win-x64.zip`이 있어야 한다(`build-release.ps1`이 만든다. `gh release create`에 같이 올린다).

**0.2.0 이하 서버는 자동 업데이트 기능이 없어 한 번은 손으로 바꾼다.** 새 `ASH-Server-<버전>-win-x64.zip`을 받아:

```bat
sc stop ASH.Server
rem ASH.Server.exe 만 교체한다(appsettings.json 과 Releases:Dir 의 배포 파일은 그대로 둔다)
sc start ASH.Server
```

교체한 뒤(자동이든 수동이든) 상태 페이지에서 새 버전이 보이는지 확인한다(서명 키를 다시 쓰는 경우에는 **서명 키 ID가 교체 전과 같은지**도 본다. 다르면 새 exe가 다른 `update-key.pub`로 빌드된 것이니 되돌린다). 손으로 되돌릴 때는 서비스를 멈추고 `ASH.Server.exe.old`를 `ASH.Server.exe`로 바꾼 뒤 시작한다.

### 영상 촬영 (ffmpeg·AutoView, 0.19.0)

웹의 컷 편집기(`/analyses/{id}/cuts`)에서 내보낸 영상은 서버가 작업 사이에 만든다(컷마다 AutoView 한 번 → ffmpeg로 자막·이어 붙이기 → MP4 yuv420p faststart). 서버 PC에 아래가 필요하다. 없으면 영상만 실패하고 작업 실행은 영향이 없다.

1. **ffmpeg** — libx264와 drawtext(libfreetype)가 들어 있는 최신 빌드(gyan.dev의 full/essentials 등). 서버 서비스 계정의 `PATH`에 두거나 `Film:FfmpegPath`에 전체 경로를 적는다. AutoView도 MP4 변환에 PATH의 ffmpeg를 쓰므로, 전체 경로를 적으면 서버가 그 폴더를 AutoView 실행 환경의 PATH에 붙여 준다. 확인: `ffmpeg -hide_banner -encoders | findstr libx264`, `ffmpeg -hide_banner -filters | findstr drawtext` (둘 다 한 줄 이상 나와야 한다).
2. **AutoView** — AutoMod 폴더의 `bin` 안에서 찾는다: AutoMod 12.6 이상은 `aview64.exe`, 12.4는 `aview.exe`(둘 다 있으면 `aview64.exe`). 다른 곳에 있으면 `Film:AviewExe`에 전체 경로를 적는다. AutoView 라이선스가 모델 실행과 같은 라이선스를 쓰므로 서버 PC에서 AutoMod 라이선스가 AutoView도 허용하는지 확인한다.
3. **서비스 세션 렌더 확인(최초 1회)** — AutoView가 서비스(세션 0, 로그온 없음)에서 화면 없이 그려지는지는 PC마다 다를 수 있다. 영상을 한 편 만들어 완료되는지 본다. 컷이 "AutoView가 MP4를 만들지 않았습니다"로 실패하면 AutoView가 서비스에서 그리지 못하는 것이니 둘 중 하나를 쓴다. (a) 서비스를 로그온 가능한 계정으로 바꾸고(`sc config ASH.Server obj= ".\<계정>" password= "<암호>"`) 서버 PC에 그 계정이 로그온해 둔다. (b) 서비스 대신 작업 스케줄러에 `ASH.Server.exe`를 "사용자가 로그온한 경우에만 실행"으로 등록한다(자동 업데이트·복구 설정은 서비스일 때만 동작).
4. 컷 하나가 `Film:CutTimeoutMin`(기본 20분)을 넘으면 AutoView를 끊고 그 컷만 실패한다. 영상 페이지 `/films/{id}`에 컷별 상태와 `film.log`가 나온다. F0(화면 배율 기준값)는 영상마다 자동 보정 컷으로 정한다.

| 설정 | 기본 | 뜻 | 적용 |
|---|---|---|---|
| `Film:FfmpegPath` | `ffmpeg` | ffmpeg 실행 파일(PATH에서 찾거나 전체 경로) | 바로 |
| `Film:AviewExe` | (빈칸 = `<Jobs:AmHome>\bin`에서 `aview64.exe`, 없으면 `aview.exe`) | AutoView 실행 파일 | 바로 |
| `Film:CutTimeoutMin` | 20 | 컷 하나의 AutoView 시간 제한(분, 1~600) | 바로 |

세 값 모두 웹 설정 페이지의 "영상 촬영" 구역에서 바꾼다.

### 백업과 복원

- 백업 대상: **`Releases:Dir` 폴더 전체**(`current\`, `files\`, `history\`)와 **`appsettings.json`**(토큰·설정). 서버 exe는 zip에서 다시 얻을 수 있으니 필수가 아니다. 서명 키를 쓰는 경우 개인키는 서버에 없다(2절 "선택: 서명 키 사용").
- 복원: 서비스를 멈추고 두 가지를 제자리에 복사한 뒤(`Releases:Dir`는 3절의 권한을 다시 확인) `sc start ASH.Server`. 상태 페이지에서 현재 버전·이전 버전 목록을 확인한다.

### `files\` 정리

업로드할 때마다 `Releases:Dir\files\`에 `ASH-Setup-<버전>.exe`가 쌓인다. 디스크가 부족하면 오래된 파일을 직접 지우되 **현재 버전과 롤백 대상(바로 이전 버전 등)은 남긴다.** `history\`의 매니페스트가 가리키는 설치 파일이 없으면 그 버전은 내려받을 수 없다.

## 6. 문제 해결

| 증상 | 원인·조치 |
|---|---|
| `AGVUPDATE`에 "업데이트 중단(github.com에 접속할 수 없어 배포 정보를 대조하지 못했습니다)" | 사용자 PC가 github.com에 HTTPS로 접속하지 못함(방화벽·프록시·DNS). 브라우저로 `https://github.com/Leepy0/ASH/releases`가 열리는지 보고, release-assets.githubusercontent.com / objects.githubusercontent.com도 허용한다. PC의 프록시 설정(로그인 계정 자격 증명)을 쓰므로 그 계정으로 확인한다. |
| "서버의 ASH.json이(가) github.com v…과 다릅니다" | 서버에 GitHub 릴리스와 다른 파일이 게시됨(수동 업로드 등). 서버가 GitHub 릴리스로 다시 받게 하거나(서비스 재시작) 새 버전을 릴리스한다. 이 상태에서는 설치되지 않는다. |
| 업로드가 400 "minSupported(…)가 version(…)보다 높습니다" | `-MinSupported`/`ASH_MIN_SUPPORTED`가 이번 버전보다 높음. |
| 상태 페이지 "지난 업데이트: x.y.z이(가) 60초 안에 응답하지 않아 … 자동 되돌림" | 새 서버 exe가 서비스로 뜨지 않음. `update\apply.log`와 이벤트 로그를 보고, 고친 판을 더 높은 버전으로 릴리스한다(같은 버전은 다시 시도하지 않음). |
| 상태 페이지 "지난 업데이트: x.y.z 파일 교체 실패" | 설치 폴더에 쓸 수 없음(LocalService 계정, 백신 잠금 등). `update\apply.log` 확인. 원인을 고친 뒤 더 높은 버전으로 릴리스하거나 수동 교체한다. |
| 상태 페이지 "마지막 확인 — 실패: …" (이름 확인 오류, "No such host is known" 등) | 서버 PC의 DNS가 github.com을 찾지 못함. `nslookup github.com 8.8.8.8`처럼 쓸 수 있는 DNS를 찾아 `GitHub:Dns`에 적고 서비스를 재시작한다. |
| 상태 페이지 "Runner(ash-run) … 거절" | 받은 zip이 `ASH.Runner.json`의 크기·SHA-256과 다름. 릴리스를 다시 만들어 새 버전으로 낸다. |
| 분석 PC에서 `update skipped: …` | 서버 주소 없음/응답 없음, github.com 접속 불가 또는 서버의 `ASH.Runner.json`이 github.com과 다름, 폴더에 쓸 수 없음 등. 문장 그대로 원인이 나온다. 작업은 지금 버전으로 계속된다. `ash-run --update --server <주소>`로 원인을 다시 확인한다. |
| 상태 페이지 "마지막 확인 — 실패: …" | 서버 PC에서 github.com에 닿지 않음. 브라우저로 `https://github.com/Leepy0/ASH/releases/latest/download/ASH.json`이 받아지는지 보고, 콘솔에서는 되는데 서비스에서만 실패하면 `GitHub:Proxy`를 적는다. 이벤트 로그에 같은 내용이 남는다. |
| 상태 페이지 "마지막 확인 — GitHub에 ASH.json이(가) 있는 릴리스가 없습니다" | 최신 정식 릴리스에 `ASH.json`이 빠졌거나 초안·시험판만 있음. |
| 업로드가 403 "업로드가 꺼져 있습니다" | `Admin:Token`이 비었거나 16자 미만. 서비스 재시작 필요. |
| 업로드가 401 | 토큰이 다름(`$env:ASH_ADMIN_TOKEN` 확인). |
| (서명 키를 다시 쓸 때만) 경고 "서명 키 미설정", 업로드 400 "서명 키 없음"/서명 거절, `build-release.ps1` 중단, "업데이트 확인 실패(서명 확인 실패)", 상태 페이지 "거절: 서명이 맞지 않습니다" | `update-key.pub`와 `ASH_SIGNING_KEY`(개인키)가 짝이 맞는지, 서버·플러그인·ash-run이 그 공개키로 빌드된 판인지 확인한다. 키 교체 중이면 4절. |
| 영상 컷이 "AutoView가 MP4를 만들지 않았습니다"로 실패 | 서버 PATH에 ffmpeg가 없거나 AutoView가 서비스 세션에서 그리지 못함. 5절 "영상 촬영"의 확인 명령으로 ffmpeg를 보고, 안 되면 로그온 계정으로 실행한다. `/films/{id}`의 `film.log`와 작업 폴더의 `aviewerr.dat`를 본다. |
| 다른 PC에서 접속이 안 되고 서버 PC 자신에서는 됨 | 방화벽 규칙이 Public 프로필 NIC에 적용되지 않음. 1절 5번의 함정 참고. |
| 서비스 로그가 이벤트 뷰어에 없음 | LocalService는 이벤트 원본을 못 만든다. 1절 "로그 위치" 참고. |
| "업데이트 확인 실패(서버 응답 없음)" | 서비스가 꺼졌거나(`sc query ASH.Server`) 방화벽·주소·포트 오류. 사용자 PC에서 `curl http://<서버>:8082/healthz`가 `ok`인지 본다. 3초 안에 답이 없으면 이 메시지가 나온다. |
| "업데이트 확인 실패(서버 인증서 오류 — 사내 인증서 신뢰 여부 확인)" | https 주소인데 사용자 PC가 서버 인증서(사내 CA)를 신뢰하지 않음. 사내 루트 인증서를 PC에 배포하거나 http 주소 사용. |
| 프록시 | 서버(`ASH.Server`) 접속은 시스템 프록시를 쓰지 않고(`UseProxy=false`) 직접 한다. 서버가 프록시 뒤에만 있다면 사내 DNS/라우팅으로 직접 닿게 해야 한다. github.com 대조는 반대로 PC의 프록시 설정(로그인 계정 자격 증명)을 쓴다. |
| "다운로드 시간 초과(5분)" | 설치 파일(약 0.5 MB~)을 5분 안에 못 받음. 네트워크 확인 후 `AGVUPDATE` 다시. |
| "다운로드 실패(…이전 설치 프로그램이 실행 중이면 닫고…)" | 같은 이름의 `ASH-Setup`이 실행 중. 닫고 다시. |
