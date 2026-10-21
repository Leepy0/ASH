# ASH 서버 설치와 릴리스 배포

> **보안 주의: 처음 시험 배포부터 서명해야 합니다. 서명 키를 만들고 `update-key.pub`를 커밋하세요.**
> - 서명 없는 빌드는 **개발용**입니다(`scripts\build-release.ps1 -AllowUnsigned`). 키가 내장되지 않은 플러그인·서버는 서버가 주는 `ASH.json`의 SHA-256만 대조하므로, 서버나 사내망 경로가 뚫리면 가짜 설치 파일을 받게 됩니다.
> - `build-release.ps1`는 서명 키(`ASH_SIGNING_KEY`)나 `update-key.pub`가 없으면 기본적으로 **중단**합니다. 서버도 공개키가 없으면 업로드를 거절합니다(`Update:AllowUnsigned=true`일 때만 예외).
> - 이미 키 없이 배포한 PC는 새 설치 파일을 한 번 직접 실행해야 키가 들어갑니다(키 없는 판의 자동 업데이트로는 키를 신뢰할 수 없음).

구성: **서버 PC**(`ASH.Server`, GitHub 릴리스를 스스로 가져와 보관·제공) / **릴리스 PC**(빌드·서명·GitHub 릴리스 생성) / **사용자 PC**(ZWCAD + ASH 플러그인, 시작할 때 서버에 새 버전 확인).

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
     - 가져온 릴리스도 업로드와 똑같이 서명·크기·SHA-256을 확인하고, 서버 버전보다 높을 때만 게시한다.
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
6. 다른 PC의 브라우저로 `http://<서버>:8082/` 를 열어 상태 페이지를 확인한다. 서비스 시작 직후에는 "게시된 버전이 없습니다."가 정상이고, 10초쯤 뒤 새로 고치면 "마지막 확인 … 가져옴: x.y.z"와 현재 버전이 보인다. "실패: …"가 보이면 서버 PC에서 github.com 접속과 `GitHub:Proxy`를 확인한다. "서명 키" 줄에 키 ID가 아니라 "서명 키 없음"이 보이면 서버 exe가 `update-key.pub` 없이 빌드된 것이다(업로드는 거절된다). 2절의 키를 만들고 서버를 다시 빌드·교체한다.

수동 업로드의 토큰은 평문 HTTP로 전달된다. 수동 업로드는 신뢰할 수 있는 사내망에서만 하거나 앞단에 HTTPS 프록시를 둔다.

### 로그 위치

서비스 로그는 **이벤트 뷰어 > Windows 로그 > 응용 프로그램**, 원본(Source) **ASH.Server**에 남는다(`appsettings.json`의 `Logging:EventLog:LogLevel:Default` = Information). 업로드 거절·서명 오류·키 상태가 여기에 찍힌다.

LocalService 계정은 이벤트 원본을 **만들 수 없어** 로그가 조용히 사라진다. 한 번만 관리자 권한으로 원본을 만들어 둔다.

```powershell
New-EventLog -LogName Application -Source ASH.Server
```

(또는 처음 한 번 LocalSystem으로 서비스를 시작해 원본이 생기게 한 뒤 LocalService로 바꾼다.) 콘솔로 직접 실행하면 로그가 콘솔에 나온다.

### 서명 없는 서버(개발용)

공개키(`update-key.pub`)가 내장되지 않은 서버는 업로드를 **거절**한다(400 "서명 키 없음 — Update:AllowUnsigned=true로만 허용"). 개발 PC에서만 `appsettings.json`에 `"Update": { "AllowUnsigned": true }`를 넣어 서명 없는 업로드를 받을 수 있다. 이 모드는 상태 페이지에 "서명 키 없음(서명 확인 안 함) — 개발용"으로 표시되고 업로드 응답에도 경고가 붙는다. 운영 서버에는 쓰지 않는다.

## 2. 릴리스 절차 (릴리스 PC)

### 서명 키 만들기 (최초 1회)

```powershell
dotnet tools\AshRelease\bin\Release\net10.0\AshRelease.dll keygen --out D:\secure\ash-keys   # 저장소 밖의 안전한 폴더
copy D:\secure\ash-keys\update-key.pub <저장소 루트>\update-key.pub
git add update-key.pub; git commit -m "chore: update signing public key"
```

- `update-key.pub`(공개키)는 저장소 루트에 **커밋**한다. 빌드 때 플러그인과 서버 exe에 내장된다. 빌드 스크립트는 내장 여부를 확인하고(`AshRelease check-resource`) 빠졌으면 중단한다.
- 키를 만든 뒤에는 플러그인과 서버를 **다시 빌드해 배포**해야 내장된다.

### 개인키 보관 (중요)

- `ash-signing.pem`(개인키)은 **릴리스 PC에서 한 번만 만든다.** 저장소에 **절대 커밋하지 않고** 릴리스 PC 밖으로 내보내지 않는다.
- **암호화한 오프라인 사본 2개**를 서로 다른 곳(예: 암호화 USB 두 개를 금고와 다른 사무실)에 보관한다. 사본이 하나뿐이면 디스크 고장 한 번에 끝난다.
- **잃어버리면 모든 PC를 손으로 다시 설치해야 한다.** 사용자 PC의 플러그인은 자기 안에 내장된 공개키로만 업데이트를 믿는다. 새 키를 넣은 판을 자동 업데이트로 보내려면 그 판을 **옛 키로 서명**해야 하는데(4절), 옛 개인키가 없으면 그럴 수 없다. 남은 방법은 새 키가 든 `ASH-Setup`을 PC마다 직접 실행하는 것뿐이다.
- 유출이 의심되면 4절의 키 교체 절차(옛 키로 서명할 수 있을 때)를 즉시 진행한다.

### 빌드와 게시

```powershell
$env:ASH_SIGNING_KEY = 'D:\secure\ash-keys\ash-signing.pem'
scripts\build-release.ps1 -MinSupported 0.1.0 -Notes notes.txt     # dist\ 에 Setup, Plugin, Runner, Server zip, ASH.json, ASH.json.sig
gh release create v<버전> dist\ASH-Setup-<버전>.exe dist\ASH.json dist\ASH.json.sig dist\ASH.Server.json dist\ASH.Server.json.sig dist\ASH.Runner.json dist\ASH.Runner.json.sig dist\ASH-Plugin-<버전>.zip dist\ASH-Runner-<버전>-win-x64.zip dist\ASH-Server-<버전>-win-x64.zip --title "v<버전>" --notes-file notes.txt
```

- 정식 릴리스(초안·시험판 아님)에 `ASH.json`, `ASH.json.sig`, `ASH-Setup-<버전>.exe`가 반드시 있어야 서버가 가져간다. 서버 자동 업데이트에는 `ASH.Server.json`, `ASH.Server.json.sig`, `ASH-Server-<버전>-win-x64.zip`도 필요하다(`build-release.ps1`이 함께 만든다). 태그는 `v<버전>`(매니페스트의 version과 같게).
- 서버는 `CheckIntervalMin`(기본 60분) 안에 가져간다. 바로 반영하려면 서버에서 `sc stop ASH.Server && sc start ASH.Server`.
- 서버가 GitHub에 닿지 않는 곳이면 수동 업로드를 쓴다(서버 `Admin:Token` 필요):
  ```powershell
  $env:ASH_ADMIN_TOKEN = '<서버 appsettings.json 의 Admin:Token>'      # 명령줄·기록에 남기지 않으려고 환경 변수로
  scripts\publish-release.ps1 -Server http://<서버>:8082
  ```

- 버전은 `Directory.Build.props`의 `<Version>`이다(올리지 않으면 서버가 같은/낮은 버전을 거절한다).
- 서명은 **기본으로 필수**다. `ASH_SIGNING_KEY`(개인키 PEM 경로)나 저장소 루트 `update-key.pub`가 없으면 빌드가 중단된다. 개발 중에만 `-AllowUnsigned`로 서명 없이 빌드한다. `update-key.pub`가 있는데 서명 키가 없으면 `-AllowUnsigned`를 줘도 중단한다(키가 든 플러그인은 서명 없는 매니페스트로 업데이트할 수 없다). `-RequireSignature`는 더 이상 필요 없는 옵션으로, 남겨 두었지만 경고만 낸다.
- 빌드는 항상 처음부터 다시 컴파일하고(`--no-incremental`) `dotnet test ASH.sln`(서버 테스트 포함)을 통과해야 패키징한다.
- `-MinSupported <버전>`(없으면 `ASH_MIN_SUPPORTED`, 그것도 없으면 `0.0.0`)은 그 버전 **미만** PC에 "최소 지원 미만" 안내를 띄운다. 기본값은 "아무도 미만이 아님"이다 — 이번 버전으로 자동 설정하지 않는다. 이번 버전보다 높게는 줄 수 없다(서버도 거절).
- 게시 뒤 상태 페이지(`http://<서버>:8082/`)에서 버전, 서명 키 ID, "마지막 확인" 결과를 확인한다.

### 되돌리기(롤백)의 뜻

`publish-release.ps1 -Force`로 **더 낮은 버전**을 게시해도 이미 새 버전을 쓰는 PC는 내려가지 않는다. 클라이언트는 서버 버전이 자기보다 낮으면 "서버의 배포 버전이 현재보다 낮습니다"만 알리고(ServerOlder) 아무것도 설치하지 않는다. `-Force`는 서버가 낮은 버전을 받게 해 줄 뿐이다.

실제로 되돌리려면 **옛 코드를 다시 빌드하되 더 높은 버전 번호**를 붙여 게시한다(예: 0.3.0이 문제라 0.2.x 코드로 돌아가려면 그 코드를 0.3.1로 빌드해 정상 게시). 그러면 PC들이 평범한 업데이트로 받는다.

## 3. 사용자 PC

- 설치 때 서버 주소를 넣는다: `ASH-Setup-x.y.z.exe /server:http://<서버>:8082` (창 모드는 "서버 주소" 칸). 설치가 실패하면 주소는 저장하지 않는다.
- 주소는 두 곳에 저장된다. **PC 공용** `%ProgramData%\ASH\ash.machine.tsv`(관리자·SYSTEM만 쓰기, 일반 사용자는 읽기)에는 항상 쓰고, 대상 사용자를 알 수 있으면 `%APPDATA%\ASH\ash.user.tsv`(`ServerUrl`)에도 쓴다. 플러그인은 사용자 파일의 `ServerUrl`이 비어 있지 않으면 그것을, 아니면 PC 공용 값을 쓴다.
- 이미 설치한 PC는 ZWCAD 명령 `AGVSERVER`로 주소를 넣거나 바꾼다(사용자 파일에 저장).
- 주소가 있으면 ZWCAD 시작 때(3초 제한) 새 버전을 확인해 명령줄에 한 줄 알리고, `AGVUPDATE`가 설치 파일을 `%TEMP%\ASH-update\`에 받아 서명·SHA-256을 확인한 뒤 탐색기로 연다. ZWCAD를 완전히 종료한 뒤 그 파일을 실행한다.
- 주소는 `http(s)://호스트[:포트][/경로]` 형식만 받는다(사용자 정보 `user@`, `?query`, `#fragment` 불가).

### 로컬 관리자 권한이 없는 사용자

설치는 관리자 권한이 필요하다. 권한이 없는 사용자 PC는 **IT가 배포**한다(SYSTEM으로 실행하는 배포 도구, 로그온 스크립트 등).

```bat
ASH-Setup-x.y.z.exe /silent /server:http://<서버>:8082
```

SYSTEM으로 실행해도 로그온한 대화형 사용자가 있고 그 사용자의 레지스트리 하이브가 로드되어 있으면 그 사용자의 HKCU 등록과 사용자 파일(`ash.user.tsv`)을 쓴다. 로그온한 사용자를 알 수 없으면(아무도 로그온하지 않았거나 하이브가 없음) HKCU 등록과 사용자 파일은 건너뛰고 HKLM 등록과 PC 공용 `ash.machine.tsv`만 쓴다. 이 경우에도 종료 코드는 0이다(PC 공용 파일을 썼다면). 사용자 파일에 주소가 없는 사용자는 PC 공용 주소를 쓴다.

## 4. 서명 키 교체

새 공개키가 들어간 버전을 **옛 키로 서명해** 먼저 배포한 뒤에야 새 키로 서명할 수 있다.

1. `keygen`으로 새 키를 만든다(새 폴더).
2. 저장소 루트 `update-key.pub`에 **옛 공개키와 새 공개키를 모두** 넣는다(PEM 블록 두 개를 이어 붙임. 여러 개 중 하나만 맞아도 통과).
3. 옛 개인키(`ASH_SIGNING_KEY`)로 서명해 이 버전을 빌드·배포한다. 사용자 PC가 이 버전으로 업데이트하고, **서버도 이 판으로 교체**한다(서버 exe가 새 공개키를 알아야 새 키 서명을 받는다).
4. 대부분의 PC가 업데이트한 뒤, 새 개인키로 서명한 릴리스를 낸다. 더 안전하게 하려면 그 다음 버전에서 `update-key.pub`의 옛 키를 뺀다.
5. 옛 개인키가 유출됐다면 새 키 판은 옛 키로 서명할 수 없으므로 모든 PC에 `ASH-Setup`을 직접 실행하게 해야 한다.

## 5. 서버 운영

### 서버 자동 업데이트 (AGV_FOTA_GATEWAY와 같은 방식)

서버는 GitHub 확인 때마다(`CheckIntervalMin`) 자기 버전도 확인해 새 버전이면 스스로 바꾼다.

1. 최신 릴리스의 `ASH.Server.json`을 읽고, 태그의 `ASH.Server.json.sig`를 서버에 내장된 공개키로 확인한다(서명이 없거나 틀리면 거절).
2. `ASH-Server-<버전>-win-x64.zip`을 받아 크기·SHA-256을 서명된 값과 대조하고, 설치 폴더의 `update\v<버전>\`에 푼다.
3. `update\apply-update.bat`이 서비스를 멈추고, 지금 exe를 `ASH.Server.exe.prev`로 바꾼 뒤 새 파일을 복사한다. **`appsettings.json`은 덮어쓰지 않는다.** `Releases:Dir`의 배포 파일도 그대로다.
4. 서비스를 다시 시작하고 60초 안에 `http://127.0.0.1:<포트>/healthz/version`이 새 버전을 돌려주면 끝. 이전 exe는 `ASH.Server.exe.old`로 남는다.
5. 응답이 없으면 이전 exe로 **자동 되돌리고**(실패한 exe는 `ASH.Server.exe.bad`) 다시 시작한다. 그 버전은 `update\skip-auto.txt`에 적혀 다시 시도하지 않는다. 더 높은 버전을 릴리스하면 다시 시도한다.

결과는 상태 페이지의 "서버 프로그램 … 자동 업데이트"와 "지난 업데이트" 줄, `update\apply.log`, 이벤트 로그에 남는다.

자동 교체는 이 조건에서만 한다(아니면 상태 페이지에 이유가 나온다): `ASH.Server.exe --install`로 등록한 Windows 서비스로 실행 중, 배포용 단일 exe(zip의 것), 서명 공개키가 내장된 빌드, `GitHub:Enabled`와 `GitHub:UpdateServer`가 true.

### Runner(ash-run) 배포

서버는 분석 PC의 `ash-run`이 스스로 업데이트하도록 서명된 Runner를 보관하고 내려 준다.

1. GitHub 확인 때마다 최신 릴리스의 `ASH.Runner.json`을 읽는다. 서버에 있는 Runner보다 높으면 태그의 `ASH.Runner.json.sig`와 `ASH-Runner-<버전>-win-x64.zip`을 받아, 서명·크기·SHA-256을 확인한 뒤 `Releases:Dir\runner\`에 둔다(최신 2개만 보관).
2. 분석 PC의 `ash-run`은 작업을 시작하기 전에 `/api/ash/runner`(+`.sig`)를 읽고 **자기 안의 공개키로 서명을 다시 확인**한 뒤, 새 버전이면 `/download/ASH-Runner-<버전>-win-x64.zip`을 받아 스스로 교체한다(README의 "ash-run" 절). 서버가 Runner를 못 가져와도 지금 Runner는 계속 쓸 수 있다.
3. 서버 상태 페이지의 "Runner(ash-run)" 줄에 보관 중인 버전과 마지막 확인 결과가 나온다.
4. 분석 PC에는 서버 주소가 필요하다: 설치 프로그램의 `/server:` 로 넣었다면 그 설정을 읽고, 아니면 `ash-run --update --server http://<서버>:8082` 또는 환경 변수 `ASH_SERVER`.
5. 릴리스에 `ASH.Runner.json`, `ASH.Runner.json.sig`, `ASH-Runner-<버전>-win-x64.zip`이 있어야 한다(`build-release.ps1`이 만든다. `gh release create`에 같이 올린다).

**0.2.0 이하 서버는 자동 업데이트 기능이 없어 한 번은 손으로 바꾼다.** 새 `ASH-Server-<버전>-win-x64.zip`을 받아:

```bat
sc stop ASH.Server
rem ASH.Server.exe 만 교체한다(appsettings.json 과 Releases:Dir 의 배포 파일은 그대로 둔다)
sc start ASH.Server
```

교체한 뒤(자동이든 수동이든) 상태 페이지의 **서명 키 ID가 교체 전과 같은지** 확인한다. 다르면 새 exe가 다른 `update-key.pub`로 빌드된 것이다(옛 키로 서명한 배포가 거절되니 되돌린다). 손으로 되돌릴 때는 서비스를 멈추고 `ASH.Server.exe.old`를 `ASH.Server.exe`로 바꾼 뒤 시작한다.

### 백업과 복원

- 백업 대상: **`Releases:Dir` 폴더 전체**(`current\`, `files\`, `history\`)와 **`appsettings.json`**(토큰·설정). 서버 exe는 zip에서 다시 얻을 수 있으니 필수가 아니다. 개인키는 서버에 없다(2절의 별도 보관).
- 복원: 서비스를 멈추고 두 가지를 제자리에 복사한 뒤(`Releases:Dir`는 3절의 권한을 다시 확인) `sc start ASH.Server`. 상태 페이지에서 현재 버전·이전 버전 목록·키 ID를 확인한다.

### `files\` 정리

업로드할 때마다 `Releases:Dir\files\`에 `ASH-Setup-<버전>.exe`가 쌓인다. 디스크가 부족하면 오래된 파일을 직접 지우되 **현재 버전과 롤백 대상(바로 이전 버전 등)은 남긴다.** `history\`의 매니페스트가 가리키는 설치 파일이 없으면 그 버전은 내려받을 수 없다.

## 6. 문제 해결

| 증상 | 원인·조치 |
|---|---|
| `AGVUPDATE`에 "경고: 서명 키 미설정 — SHA-256만 확인" | 플러그인을 `update-key.pub` 없이 빌드함(`-AllowUnsigned` 개발 빌드). 2절의 키를 만들고 커밋한 뒤 다시 빌드·설치. |
| 업로드가 400 "서명 키 없음 — Update:AllowUnsigned=true로만 허용" | 서버 exe를 `update-key.pub` 없이 빌드함. 키 커밋 후 서버 zip을 다시 빌드해 교체(개발 PC만 `Update:AllowUnsigned=true`). |
| 업로드가 400 "minSupported(…)가 version(…)보다 높습니다" | `-MinSupported`/`ASH_MIN_SUPPORTED`가 이번 버전보다 높음. |
| 상태 페이지 "지난 업데이트: x.y.z이(가) 60초 안에 응답하지 않아 … 자동 되돌림" | 새 서버 exe가 서비스로 뜨지 않음. `update\apply.log`와 이벤트 로그를 보고, 고친 판을 더 높은 버전으로 릴리스한다(같은 버전은 다시 시도하지 않음). |
| 상태 페이지 "지난 업데이트: x.y.z 파일 교체 실패" | 설치 폴더에 쓸 수 없음(LocalService 계정, 백신 잠금 등). `update\apply.log` 확인. 원인을 고친 뒤 더 높은 버전으로 릴리스하거나 수동 교체한다. |
| 상태 페이지 "마지막 확인 — 실패: …" (이름 확인 오류, "No such host is known" 등) | 서버 PC의 DNS가 github.com을 찾지 못함. `nslookup github.com 8.8.8.8`처럼 쓸 수 있는 DNS를 찾아 `GitHub:Dns`에 적고 서비스를 재시작한다. |
| 상태 페이지 "Runner(ash-run) … 거절" | GitHub 릴리스의 `ASH.Runner.json.sig`가 서버 내장 공개키와 다른 키로 서명됐거나 zip이 배포 정보와 다름. 올바른 키로 다시 빌드해 새 버전으로 릴리스한다. |
| 분석 PC에서 `update skipped: …` | 서버 주소 없음/응답 없음, 폴더에 쓸 수 없음 등. 문장 그대로 원인이 나온다. 작업은 지금 버전으로 계속된다. `ash-run --update --server <주소>`로 원인을 다시 확인한다. |
| 상태 페이지 "마지막 확인 — 실패: …" | 서버 PC에서 github.com에 닿지 않음. 브라우저로 `https://github.com/Leepy0/ASH/releases/latest/download/ASH.json`이 받아지는지 보고, 콘솔에서는 되는데 서비스에서만 실패하면 `GitHub:Proxy`를 적는다. 이벤트 로그에 같은 내용이 남는다. |
| 상태 페이지 "마지막 확인 — 거절: 서명이 맞지 않습니다" | GitHub 릴리스의 `ASH.json.sig`가 서버 내장 공개키와 다른 키로 서명됨, 또는 `ASH.json`을 손으로 고침. 올바른 키로 다시 빌드해 새 버전으로 릴리스한다. |
| 상태 페이지 "마지막 확인 — GitHub에 ASH.json이(가) 있는 릴리스가 없습니다" | 최신 정식 릴리스에 `ASH.json`이 빠졌거나 초안·시험판만 있음. |
| 업로드가 403 "업로드가 꺼져 있습니다" | `Admin:Token`이 비었거나 16자 미만. 서비스 재시작 필요. |
| 업로드가 401 | 토큰이 다름(`$env:ASH_ADMIN_TOKEN` 확인). |
| 업로드가 거절됨(서명) | `ASH_SIGNING_KEY`의 키가 서버 내장 공개키와 다름. 올바른 키로 `build-release.ps1`를 다시 실행. |
| `build-release.ps1`가 "Signing is required" 로 중단 | `ASH_SIGNING_KEY`나 `update-key.pub` 없음. 2절의 키 준비 후 다시, 개발 빌드라면 `-AllowUnsigned`. |
| 다른 PC에서 접속이 안 되고 서버 PC 자신에서는 됨 | 방화벽 규칙이 Public 프로필 NIC에 적용되지 않음. 1절 5번의 함정 참고. |
| 서비스 로그가 이벤트 뷰어에 없음 | LocalService는 이벤트 원본을 못 만든다. 1절 "로그 위치" 참고. |
| "업데이트 확인 실패(서버 응답 없음)" | 서비스가 꺼졌거나(`sc query ASH.Server`) 방화벽·주소·포트 오류. 사용자 PC에서 `curl http://<서버>:8082/healthz`가 `ok`인지 본다. 3초 안에 답이 없으면 이 메시지가 나온다. |
| "업데이트 확인 실패(서버 인증서 오류 — 사내 인증서 신뢰 여부 확인)" | https 주소인데 사용자 PC가 서버 인증서(사내 CA)를 신뢰하지 않음. 사내 루트 인증서를 PC에 배포하거나 http 주소 사용. |
| "업데이트 확인 실패(서명 확인 실패)" | 서버의 `ASH.json.sig`가 내장 공개키와 안 맞음(키 교체 중이거나 다른 키로 서명). 4절 확인. |
| 프록시 | 업데이트 클라이언트는 시스템 프록시를 쓰지 않고(`UseProxy=false`) 서버에 직접 접속한다. 서버가 프록시 뒤에만 있다면 사내 DNS/라우팅으로 직접 닿게 해야 한다. |
| "다운로드 시간 초과(5분)" | 설치 파일(약 0.5 MB~)을 5분 안에 못 받음. 네트워크 확인 후 `AGVUPDATE` 다시. |
| "다운로드 실패(…이전 설치 프로그램이 실행 중이면 닫고…)" | 같은 이름의 `ASH-Setup`이 실행 중. 닫고 다시. |
