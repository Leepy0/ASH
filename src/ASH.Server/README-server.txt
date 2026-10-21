ASH.Server - ASH 배포 서버 (최소판)
자세한 절차(키 보관, 백업, 롤백, 문제 해결)는 docs\server-install.md 를 본다.

!! 보안: 처음 시험 배포부터 서명해야 한다. 서명 키 없이 빌드한 서버는 업로드를 모두 거절한다
   (Update:AllowUnsigned=true 인 개발 PC만 예외). 서명 없는 빌드는 개발용이다.

아래 명령은 관리자 명령 프롬프트(cmd) 기준이다.

1. 이 폴더(ASH.Server.exe, appsettings.json)를 C:\Program Files\ASH.Server 에 푼다. .NET 설치는 필요 없다.
2. appsettings.json 에서
   - GitHub       새 릴리스 자동 가져오기(AGV_FOTA_GATEWAY 와 같은 방식). 기본값 그대로 두면 된다.
                    BaseUrl https://github.com/Leepy0/ASH 의 releases/latest/download/ASH.json 을 CheckIntervalMin(기본 60분)마다 확인.
                    Proxy 비우면 PC 설정, none 이면 직접 연결, 또는 http://proxy.corp:8080 (서비스는 사용자 프록시 설정을 안 씀).
                    가져온 릴리스도 서명·크기·SHA-256 을 확인한다. 토큰은 필요 없다.
                    UpdateServer true(기본) 면 서버 프로그램 자신도 새 버전으로 자동 교체한다(아래 "서버 자동 업데이트").
   - Admin:Token  선택. 수동 업로드(publish-release.ps1)를 쓸 때만. 16자 이상의 길고 무작위인 문자열.
                    비워 두면 수동 업로드만 꺼지고 GitHub 가져오기는 동작한다.
   - Urls         기본 http://0.0.0.0:8082
   - Releases:Dir 배포 파일 보관 폴더(기본 %ProgramData%/ASH.Server/releases)
   를 확인한다. 환경 변수 Admin__Token 으로 토큰을 줄 수도 있다.
   설치 폴더 권한을 관리자로 제한한다(토큰을 넣었다면 토큰 보호):
     icacls "C:\Program Files\ASH.Server" /inheritance:r /grant:r "Administrators:(OI)(CI)F" "SYSTEM:(OI)(CI)F"
3. 배포 폴더(Releases:Dir)를 먼저 만들고 권한을 잠근 다음에 서비스를 등록한다(순서 필수. 서비스가 먼저 만들면
   %ProgramData% 의 상속 권한이 붙어 로컬 사용자가 배포 파일을 바꿀 수 있다):
     mkdir "%ProgramData%\ASH.Server"
     icacls "%ProgramData%\ASH.Server" /inheritance:r /grant:r "Administrators:(OI)(CI)F" "SYSTEM:(OI)(CI)F"
     cd /d "C:\Program Files\ASH.Server"
     ASH.Server.exe --install
   Releases:Dir 를 바꿨다면 그 폴더에 같은 mkdir/icacls 를 한다.
   윈도우 표시 언어 때문에 Administrators/SYSTEM 이름이 통하지 않으면 SID 로 준다:
     icacls "%ProgramData%\ASH.Server" /inheritance:r /grant:r "*S-1-5-32-544:(OI)(CI)F" "*S-1-5-18:(OI)(CI)F"
   (*S-1-5-32-544 = Administrators, *S-1-5-18 = SYSTEM. 설치 폴더 명령도 같다.)
   등록 후 출력되는 BINARY_PATH_NAME 이 따옴표로 감싸져 있는지 확인한다:  sc qc ASH.Server
   서비스 시작: sc start ASH.Server     제거: ASH.Server.exe --uninstall
   확인만 하려면 서비스 등록 없이 ASH.Server.exe 를 직접 실행해도 된다(설정은 exe 와 같은 폴더의 appsettings.json).
   서비스는 기본적으로 LocalSystem 으로 돈다. 권한을 줄이려면 LocalService 로 바꾼다:
     sc config ASH.Server obj= "NT AUTHORITY\LocalService"
     icacls "%ProgramData%\ASH.Server" /grant "NT AUTHORITY\LocalService:(OI)(CI)M"       (배포 파일 폴더)
     icacls "C:\Program Files\ASH.Server" /grant "NT AUTHORITY\LocalService:(OI)(CI)RX"   (exe 와 appsettings.json 폴더)
   (Releases:Dir 를 바꿨다면 그 폴더에도 LocalService 수정 권한을 준다.)
4. 서비스 복구(죽으면 60초 뒤 자동 재시작):
     sc failure ASH.Server reset= 86400 actions= restart/60000
5. 방화벽에서 TCP 8082 을 허용한다. 사내망에서만 열 것.
     New-NetFirewallRule -DisplayName "ASH.Server" -Direction Inbound -Protocol TCP -LocalPort 8082 -Action Allow -Profile Domain,Private   (PowerShell)
   함정: 서버 PC 의 네트워크가 Public 으로 분류돼 있으면 위 규칙이 적용되지 않아 다른 PC 에서 접속이 안 된다
   (서버 자신에서는 열린다). Get-NetConnectionProfile 로 확인하고 Set-NetConnectionProfile ... -NetworkCategory Private 로 바꾼다.
   규칙에 Public 을 그냥 추가하지 말 것.
   수동 업로드의 토큰은 평문 HTTP 로 전달된다. 신뢰할 수 있는 사내망에서만 업로드하거나, 앞단에 HTTPS 프록시를 둔다.
6. 브라우저로 http://서버:8082/ 를 열어 상태 페이지를 확인한다. 시작 10초쯤 뒤 "마지막 확인 — 가져옴: x.y.z" 와
   현재 버전이 보이면 끝이다. "실패: …" 면 서버 PC 의 github.com 접속과 GitHub:Proxy 를 확인한다.
7. 새 버전 배포: GitHub 에 정식 릴리스(ASH.json, ASH.json.sig, ASH-Setup-x.y.z.exe 포함)를 만들면 서버가 60분 안에 가져간다.
   바로 반영하려면 sc stop ASH.Server && sc start ASH.Server.
   서버가 GitHub 에 닿지 않을 때만 수동 업로드:  $env:ASH_ADMIN_TOKEN='<토큰>'; scripts\publish-release.ps1 -Server http://서버:8082

로그
- 이벤트 뷰어 > Windows 로그 > 응용 프로그램, 원본(Source) ASH.Server. (appsettings.json 의 Logging:EventLog 로 수준 조정)
- LocalService 는 이벤트 원본을 만들 수 없다. 관리자 권한으로 한 번만 만든다:
     New-EventLog -LogName Application -Source ASH.Server
  (또는 처음 한 번 LocalSystem 으로 서비스를 시작해 원본이 생기게 한 뒤 LocalService 로 바꾼다.)

서명 키
- 운영 서버는 서명된 배포만 받아야 한다. 운영 전에 AshRelease keygen 으로 키를 만들고 공개키 update-key.pub 를 저장소 루트에 둔 뒤
  다시 빌드한다(서버 exe 에 공개키가 내장되어, 서명 없거나 틀린 배포는 거절된다).
- 개인키는 릴리스 PC 에서 한 번만 만들고, 커밋하지 않으며, 암호화한 오프라인 사본을 2개 보관한다.
  잃어버리면 새 키를 자동 업데이트로 보낼 수 없어 모든 PC 에 ASH-Setup 을 직접 다시 설치해야 한다.
- 공개키가 없는 서버는 업로드를 거절한다(400 "서명 키 없음 — Update:AllowUnsigned=true로만 허용").
  개발 PC 에서만 appsettings.json 에 "Update": { "AllowUnsigned": true } 를 넣어 서명 없는 업로드를 받을 수 있고,
  이때 상태 페이지에 "서명 키 없음(서명 확인 안 함) — 개발용"이 표시된다.
- Update:PublicKeyPem 설정은 시험/개발용 덮어쓰기다. 운영에서는 설정하지 말 것
  (설정하면 내장 공개키를 대체하고 경고 로그가 남는다. 빈 값은 개발 환경에서만 "키 없음"으로 쓰인다).

운영
- 백업: Releases:Dir 폴더 전체와 appsettings.json. 복원: 서비스를 멈추고 복사해 넣은 뒤(폴더 권한 확인) sc start ASH.Server.
- 서버 자동 업데이트(FOTA Gateway 와 같은 방식): 서명된 ASH.Server.json 과 서버 zip 을 GitHub 에서 받아 확인한 뒤
  update\apply-update.bat 이 서비스를 멈추고 exe 를 바꿔 다시 시작한다. appsettings.json 과 배포 파일은 그대로 둔다.
  60초 안에 새 버전이 응답하지 않으면 이전 exe 로 자동 되돌리고(실패한 exe 는 ASH.Server.exe.bad) 그 버전은 다시 시도하지 않는다.
  결과: 상태 페이지 "지난 업데이트" 줄, update\apply.log. 이전 exe 는 ASH.Server.exe.old 로 남는다.
  LocalService 계정으로 바꾸면 설치 폴더에 쓸 수 없어 자동 교체가 실패한다(이전 버전 유지).
  0.2.0 이하 서버는 이 기능이 없어 한 번은 손으로 바꾼다: sc stop ASH.Server, ASH.Server.exe 교체, sc start ASH.Server.
- 서버 exe 를 교체한 뒤에는 상태 페이지의 서명 키 ID 가 교체 전과 같은지 확인한다.
- 롤백: publish-release.ps1 -Force 로 낮은 버전을 올려도 이미 새 버전을 쓰는 PC 는 내려가지 않는다(서버 버전이 더 낮다는 안내만 나온다).
  되돌리려면 옛 코드를 더 높은 버전 번호로 다시 빌드해 게시한다.
- files\ 폴더는 업로드마다 커진다. 지울 때는 현재 버전과 롤백 대상 설치 파일을 남긴다.
- 로컬 관리자 권한이 없는 사용자 PC 는 IT 가 배포한다:  ASH-Setup-x.y.z.exe /silent /server:http://서버:8082
