ASH.Server - ASH 배포 서버 (최소판)

1. 이 폴더(ASH.Server.exe, appsettings.json)를 서버 PC의 원하는 위치에 푼다. .NET 설치는 필요 없다.
2. appsettings.json 에서
   - Admin:Token  업로드용 토큰. 16자 이상의 길고 무작위인 문자열. 비어 있거나 16자 미만이면 업로드가 꺼진다.
   - Urls         기본 http://0.0.0.0:5080
   - Releases:Dir 배포 파일 보관 폴더(기본 %ProgramData%/ASH.Server/releases)
   를 확인한다. appsettings.json 에는 토큰이 들어 있으므로 폴더 접근 권한을 관리자(와 서비스 계정)로 제한한다
   (예: icacls "<폴더>" /inheritance:r /grant Administrators:(OI)(CI)F SYSTEM:(OI)(CI)F).
   환경 변수 Admin__Token 으로 줄 수도 있다.
3. 관리자 권한 명령 프롬프트에서  ASH.Server.exe --install  (Windows 서비스 등록, 자동 시작)
   등록 후 출력되는 BINARY_PATH_NAME 이 따옴표로 감싸져 있는지 확인한다.
   서비스 시작: sc start ASH.Server     제거: ASH.Server.exe --uninstall
   확인만 하려면 서비스 등록 없이 ASH.Server.exe 를 직접 실행해도 된다(설정은 exe 와 같은 폴더의 appsettings.json).
   서비스는 기본적으로 LocalSystem 으로 돈다. 권한을 줄이려면 LocalService 로 바꾼다:
     sc config ASH.Server obj= "NT AUTHORITY\LocalService"
     icacls "%ProgramData%\ASH.Server" /grant "NT AUTHORITY\LocalService:(OI)(CI)M"
   (appsettings.json 도 LocalService 가 읽을 수 있어야 한다. Releases:Dir 를 바꿨다면 그 폴더에 같은 권한을 준다.)
4. 방화벽에서 TCP 5080 을 허용한다. 사내망에서만 열 것.
   토큰은 평문 HTTP 로 전달된다. 신뢰할 수 있는 사내망에서만 업로드하거나, 앞단에 HTTPS 프록시를 둔다.
5. 브라우저로 http://서버:5080/ 를 열어 상태 페이지를 확인한다.
6. 릴리스 PC에서:  $env:ASH_ADMIN_TOKEN='<토큰>'; scripts\publish-release.ps1 -Server http://서버:5080

서명 키
- 운영 서버는 서명된 배포만 받아야 한다. 운영 전에 AshRelease keygen 으로 키를 만들고 공개키 update-key.pub 를 저장소 루트에 둔 뒤
  다시 빌드한다(서버 exe 에 공개키가 내장되어, 서명 없거나 틀린 배포는 거절된다). 개인키는 릴리스 PC 밖으로 내보내지 않는다.
- 공개키가 없으면 서명 확인 없이 업로드를 받고 상태 페이지에 "서명 키 없음(서명 확인 안 함)"이 표시된다.
- Update:PublicKeyPem 설정은 시험/개발용 덮어쓰기다. 운영에서는 설정하지 말 것
  (설정하면 내장 공개키를 대체하고 경고 로그가 남는다. 빈 값은 개발 환경에서만 "키 없음"으로 쓰인다).
