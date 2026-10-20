# ASH 서버 설치와 릴리스 배포

> **보안 주의: 첫 배포 전에 서명 키를 만들고 `update-key.pub`를 커밋해야 합니다 — 없으면 SHA-256만 확인합니다.**
> 서명 키 없이 빌드한 플러그인·서버는 서버가 주는 `ASH.json`의 SHA-256만 대조하므로, 서버나 사내망 경로가 뚫리면 가짜 설치 파일을 받게 됩니다. 이미 키 없이 배포한 PC는 새 설치 파일을 한 번 직접 실행해야 키가 들어갑니다(키 없는 판의 자동 업데이트로는 키를 신뢰할 수 없음).

구성: **서버 PC**(`ASH.Server`, 배포 파일 보관·제공) / **릴리스 PC**(빌드·서명·업로드) / **사용자 PC**(ZWCAD + ASH 플러그인, 시작할 때 서버에 새 버전 확인).

## 1. 서버 PC 설치

`ASH.Server`는 .NET 설치가 필요 없는 단일 exe다(`dist/ASH-Server-<버전>-win-x64.zip`).

1. zip을 서버 PC의 원하는 폴더(예: `C:\ASH.Server`)에 푼다. `ASH.Server.exe`, `appsettings.json`, `README-server.txt`가 나온다.
2. `appsettings.json`을 고친다.
   - `Admin:Token` — 업로드용 토큰. **16자 이상**의 긴 무작위 문자열(비어 있거나 짧으면 업로드가 꺼짐).
   - `Urls` — 기본 `http://0.0.0.0:5080`. 포트를 바꾸면 아래 방화벽·`/server:` 주소도 같이 바꾼다.
   - `Releases:Dir` — 배포 파일 보관 폴더(기본 `%ProgramData%/ASH.Server/releases`).
   - 토큰이 들어 있으므로 폴더 권한을 관리자로 제한한다(`README-server.txt`의 `icacls` 예시).
3. **관리자 PowerShell**에서 서비스로 등록하고 시작한다.
   ```powershell
   cd C:\ASH.Server
   .\ASH.Server.exe --install
   sc.exe qc ASH.Server        # BINARY_PATH_NAME 이 "…\ASH.Server.exe" 처럼 따옴표로 감싸졌는지 확인
   sc.exe start ASH.Server
   ```
   제거: `.\ASH.Server.exe --uninstall`. 서비스 없이 확인만 하려면 `ASH.Server.exe`를 직접 실행해도 된다.
4. 방화벽 인바운드 TCP 5080 허용(사내망 범위만).
   ```powershell
   New-NetFirewallRule -DisplayName "ASH.Server" -Direction Inbound -Protocol TCP -LocalPort 5080 -Action Allow -Profile Domain,Private
   ```
5. 다른 PC의 브라우저로 `http://<서버>:5080/` 를 열어 상태 페이지를 확인한다. 처음에는 "게시된 버전이 없습니다."가 정상이다. "서명 키" 줄이 "서명 키 없음(서명 확인 안 함)"이면 아래 2절의 키를 만든 뒤 서버를 다시 빌드·교체해야 한다.

토큰은 평문 HTTP로 전달된다. 신뢰할 수 있는 사내망에서만 업로드하거나 앞단에 HTTPS 프록시를 둔다.

## 2. 릴리스 절차 (릴리스 PC)

### 서명 키 만들기 (최초 1회)

```powershell
dotnet tools\AshRelease\bin\Release\net10.0\AshRelease.dll keygen --out D:\secure\ash-keys   # 저장소 밖의 안전한 폴더
copy D:\secure\ash-keys\update-key.pub <저장소 루트>\update-key.pub
git add update-key.pub; git commit -m "chore: update signing public key"
```

- `update-key.pub`(공개키)는 저장소 루트에 **커밋**한다. 빌드 때 플러그인과 서버 exe에 내장된다.
- `ash-signing.pem`(개인키)은 **저장소 밖**에 두고 커밋하지 않으며 릴리스 PC 밖으로 내보내지 않는다. 잃어버리면 새 키로 교체해야 한다(4절).
- 키를 만든 뒤에는 플러그인과 서버를 **다시 빌드해 배포**해야 내장된다.

### 빌드와 업로드

```powershell
$env:ASH_SIGNING_KEY = 'D:\secure\ash-keys\ash-signing.pem'
scripts\build-release.ps1 -RequireSignature -Notes notes.txt       # dist\ 에 Setup, Plugin, Runner, Server zip, ASH.json, ASH.json.sig
$env:ASH_ADMIN_TOKEN = '<서버 appsettings.json 의 Admin:Token>'      # 명령줄·기록에 남기지 않으려고 환경 변수로
scripts\publish-release.ps1 -Server http://<서버>:5080
```

- 버전은 `Directory.Build.props`의 `<Version>`이다(올리지 않으면 서버가 같은/낮은 버전을 거절한다).
- `ASH_MIN_SUPPORTED`를 주면 그 버전 미만 PC에 "최소 지원 미만" 안내가 뜬다(생략하면 이번 버전 = 이전 모든 버전이 미만).
- `-RequireSignature`는 개인키나 `update-key.pub`가 없으면 빌드를 중단한다. 운영 배포에는 항상 쓴다.
- 이전 버전으로 되돌리는 배포는 `publish-release.ps1 -Force`.
- 업로드 뒤 상태 페이지(`http://<서버>:5080/`)에서 버전과 서명 키 ID를 확인한다.

## 3. 사용자 PC

- 설치 때 서버 주소를 넣는다: `ASH-Setup-x.y.z.exe /server:http://<서버>:5080` (창 모드는 "서버 주소" 칸). 설치가 실패하면 주소는 저장하지 않는다.
- 이미 설치한 PC는 ZWCAD 명령 `AGVSERVER`로 주소를 넣거나 바꾼다. 주소는 `%APPDATA%\ASH\ash.user.tsv`(`ServerUrl`)에 저장된다.
- 주소가 있으면 ZWCAD 시작 때(3초 제한) 새 버전을 확인해 명령줄에 한 줄 알리고, `AGVUPDATE`가 설치 파일을 `%TEMP%\ASH-update\`에 받아 서명·SHA-256을 확인한 뒤 탐색기로 연다. ZWCAD를 완전히 종료한 뒤 그 파일을 실행한다.
- 주소는 `http(s)://호스트[:포트][/경로]` 형식만 받는다(사용자 정보 `user@`, `?query`, `#fragment` 불가).

## 4. 서명 키 교체

새 공개키가 들어간 버전을 **옛 키로 서명해** 먼저 배포한 뒤에야 새 키로 서명할 수 있다.

1. `keygen`으로 새 키를 만든다(새 폴더).
2. 저장소 루트 `update-key.pub`에 **옛 공개키와 새 공개키를 모두** 넣는다(PEM 블록 두 개를 이어 붙임. 여러 개 중 하나만 맞아도 통과).
3. 옛 개인키(`ASH_SIGNING_KEY`)로 서명해 이 버전을 빌드·배포한다. 사용자 PC가 이 버전으로 업데이트하고, **서버도 이 판으로 교체**한다(서버 exe가 새 공개키를 알아야 새 키 서명을 받는다).
4. 대부분의 PC가 업데이트한 뒤, 새 개인키로 서명한 릴리스를 낸다. 더 안전하게 하려면 그 다음 버전에서 `update-key.pub`의 옛 키를 뺀다.
5. 옛 개인키가 유출됐다면 새 키 판은 옛 키로 서명할 수 없으므로 모든 PC에 `ASH-Setup`을 직접 실행하게 해야 한다.

## 5. 서버 업데이트 (지금은 수동)

서버 자기 업데이트는 아직 없다. 새 `ASH-Server-<버전>-win-x64.zip`을 받아:

```powershell
sc.exe stop ASH.Server
# ASH.Server.exe 만 교체한다(appsettings.json 과 Releases:Dir 의 배포 파일은 그대로 둔다)
sc.exe start ASH.Server
```

## 6. 문제 해결

| 증상 | 원인·조치 |
|---|---|
| `AGVUPDATE`에 "경고: 서명 키 미설정 — SHA-256만 확인" | 플러그인을 `update-key.pub` 없이 빌드함. 2절의 키를 만들고 커밋한 뒤 다시 빌드·설치. |
| 상태 페이지·업로드 응답에 "서명 키 없음(서명 확인 안 함)" | 서버 exe를 `update-key.pub` 없이 빌드함. 키 커밋 후 서버 zip을 다시 빌드해 교체. |
| 업로드가 403 "업로드가 꺼져 있습니다" | `Admin:Token`이 비었거나 16자 미만. 서비스 재시작 필요. |
| 업로드가 401 | 토큰이 다름(`$env:ASH_ADMIN_TOKEN` 확인). |
| 업로드가 거절됨(서명) | `ASH_SIGNING_KEY`의 키가 서버 내장 공개키와 다름. `build-release.ps1 -RequireSignature`로 다시 빌드. |
| "업데이트 확인 실패(서버 응답 없음)" | 서비스가 꺼졌거나(`sc.exe query ASH.Server`) 방화벽·주소·포트 오류. 사용자 PC에서 `curl http://<서버>:5080/healthz`가 `ok`인지 본다. 3초 안에 답이 없으면 이 메시지가 나온다. |
| "업데이트 확인 실패(서명 확인 실패)" | 서버의 `ASH.json.sig`가 내장 공개키와 안 맞음(키 교체 중이거나 다른 키로 서명). 4절 확인. |
| 프록시 | 업데이트 클라이언트는 시스템 프록시를 쓰지 않고(`UseProxy=false`) 서버에 직접 접속한다. 서버가 프록시 뒤에만 있다면 사내 DNS/라우팅으로 직접 닿게 해야 한다. |
| "다운로드 시간 초과(5분)" | 설치 파일(약 0.5 MB~)을 5분 안에 못 받음. 네트워크 확인 후 `AGVUPDATE` 다시. |
| "다운로드 실패(…이전 설치 프로그램이 실행 중이면 닫고…)" | 같은 이름의 `ASH-Setup`이 실행 중. 닫고 다시. |
