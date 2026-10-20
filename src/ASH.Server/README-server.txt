ASH.Server - ASH 배포 서버 (최소판)

1. 이 폴더(ASH.Server.exe, appsettings.json)를 서버 PC의 원하는 위치에 푼다. .NET 설치는 필요 없다.
2. appsettings.json 에서
   - Admin:Token  업로드용 토큰(비워 두면 업로드 불가). 길고 무작위인 문자열을 쓴다.
   - Urls         기본 http://0.0.0.0:5080
   - Releases:Dir 배포 파일 보관 폴더(기본 %ProgramData%\ASH.Server\releases)
   를 확인한다.
3. 관리자 권한 명령 프롬프트에서  ASH.Server.exe --install  (Windows 서비스 등록, 자동 시작)
   서비스 시작: sc start ASH.Server     제거: ASH.Server.exe --uninstall
   확인만 하려면 서비스 등록 없이 ASH.Server.exe 를 직접 실행해도 된다.
4. 방화벽에서 TCP 5080 을 허용한다.
5. 브라우저로 http://서버:5080/ 를 열어 상태 페이지를 확인한다.
6. 릴리스 PC에서:  scripts\publish-release.ps1 -Server http://서버:5080 -Token <토큰>
