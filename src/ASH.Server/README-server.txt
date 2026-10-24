ASH.Server - ASH 배포 서버 (최소판)
자세한 절차(백업, 롤백, 문제 해결)는 docs\server-install.md 를 본다.

!! 무결성 확인(0.15.0부터): 서명 없이 PC 가 github.com 과 대조한다. 플러그인 AGVUPDATE 와 ash-run 자동 업데이트는 이 서버의
   ASH.json / ASH.Runner.json 을 https://github.com/Leepy0/ASH/releases/download/v<버전>/<같은 파일명> 과 바이트 단위로 비교하고,
   같을 때만 파일을 받는다. 사용자 PC 는 github.com(리디렉션되는 release-assets.githubusercontent.com / objects.githubusercontent.com 포함)에
   HTTPS 로 접속할 수 있어야 한다. 0.14.x 이하(옛 서명 키 내장) PC·서버·ash-run 은 자동 업데이트로 넘어오지 못하니 0.15.0 을
   한 번 수동 설치한다(ASH-Setup 실행, 서버 zip 교체, ash-run zip 교체).

가장 쉬운 방법: 이 폴더를 C:\Program Files\ASH.Server 에 풀고 install-service.bat 을 더블클릭한다(관리자 권한을 묻는다).
  폴더 권한 잠금, 서비스 등록(자동 시작, 죽으면 재시작), 방화벽 규칙(appsettings.json 의 포트), 시작, 응답 확인까지 한 번에 한다.
  제거는 uninstall-service.bat (배포 파일과 appsettings.json 은 남긴다). 서비스 등록 후에는 새 버전을 GitHub 에서 가져오고 서버도 스스로 업데이트한다.
  아래는 같은 일을 손으로 하는 방법이다. 명령은 관리자 명령 프롬프트(cmd) 기준이다.

1. 이 폴더(ASH.Server.exe, appsettings.json)를 C:\Program Files\ASH.Server 에 푼다. .NET 설치는 필요 없다.
2. appsettings.json 에서
   - GitHub       새 릴리스 자동 가져오기(AGV_FOTA_GATEWAY 와 같은 방식). 기본값 그대로 두면 된다.
                    BaseUrl https://github.com/Leepy0/ASH 의 releases/latest/download/ASH.json 을 CheckIntervalMin(기본 60분)마다 확인.
                    Proxy 비우면 PC 설정, none 이면 직접 연결, 또는 http://proxy.corp:8080 (서비스는 사용자 프록시 설정을 안 씀).
                    가져온 릴리스도 크기·SHA-256 을 확인한다(GitHub 에서 HTTPS 로 받은 것은 그대로 믿는다). 관리자 암호는 필요 없다.
                    Dns 비우면 PC 의 DNS. 사내 DNS 가 github.com 을 못 찾으면 DNS 서버 IP 를 적는다(예: "8.8.8.8, 1.1.1.1").
                    GitHub 주소를 그 DNS 에서 먼저 찾고, 답이 없으면 PC 의 DNS 로 다시 찾는다(UDP 53).
                    UpdateServer true(기본) 면 서버 프로그램 자신도 새 버전으로 자동 교체한다(아래 "서버 자동 업데이트").
   - Admin:Password  관리자 암호. 4~20자, 영문·숫자·특수문자 혼합 가능(공백·한글 안 됨). 비우면 기본값 ash!23.
                    다른 사람이 닿는 서버는 반드시 바꾼다. 규칙에 맞지 않으면 관리자 기능이 꺼진다.
                    비워 두면 수동 업로드만 꺼지고 GitHub 가져오기는 동작한다.
   - Urls         기본 http://0.0.0.0:8082
   - Releases:Dir 배포 파일 보관 폴더(기본 %ProgramData%/ASH.Server/releases)
   - Jobs         서버 실행(ASH의 "서버로 보내기"로 올린 모델을 빌드하고 대수 탐색). 서버 PC 에 AutoMod 가 있어야 한다.
                    AmHome AutoMod 설치 폴더(기본 C:\AutoMod\12_6, bin\am264.exe 가 있어야 함)
                    Parallel 동시에 돌리는 모델 실행 수(기본 2). 코어 수와 AutoMod 라이선스에 맞춘다.
                    DialogButton 체험판처럼 실행 때 대화상자가 뜨는 설치면 누를 버튼 이름(예: "Student Version"). 정식판은 비운다.
                    RunTimeoutMin 실행 하나의 시간 제한(기본 30분), BuildTimeoutMin 빌드 시간 제한(기본 10분),
                    JobTimeoutHours 작업 하나의 시간 제한(기본 24시간). 넘으면 끊고 다음 작업으로 넘어간다.
                    Dir 작업 폴더(기본 %ProgramData%/ASH.Server/jobs).
                    KeepRunsDays 끝난 작업의 실행 폴더를 이 일수 뒤 작업 폴더의 runs.zip 하나로 압축해 보관하고 원본 폴더와 빌드 출력을 지운다(기본 30, 0 = 그대로 둠).
                      runs.zip 받기, 요약(summary.tsv, 결과 화면, Excel 보고서), 올린 원본(upload.zip), 로그는 그대로다. 한 시간마다 확인한다.
                    Enabled false 면 작업을 받지 않는다. WorkerEnabled false 면 받기만 하고 실행하지 않는다.
                    주의: 올린 모델의 로직(user_ext.m 등)이 서버에서 AutoMod 로 컴파일·실행된다. 사내망에서만 연다.
                    작업 대기열은 http://서버:8082/admin/jobs, 결과는 홈(/)·분석(/analyses)에서 본다. 취소는 보낸 PC(같은 IP)나 관리자 암호로만 된다.
                    상세 탭의 실행 목록에서 "받기"를 누르면 그 실행의 모델(빌드 출력이 남아 있으면 <모델>.exe·.dir 포함)과
                      params.txt, run.bat 을 zip 으로 받는다. AutoMod 가 있는 PC 에서 풀고 run.bat 을 누르면 그 실행을 창으로 본다.
                      run.bat 의 AutoMod 폴더 기본값은 이 서버의 AmHome 이다.
   - Film         웹 컷 편집기에서 내보낸 영상을 서버가 작업 사이에 만든다(컷마다 AutoView, ffmpeg 로 자막·이어 붙이기, MP4).
                    FfmpegPath 기본 ffmpeg(PATH 에서 찾음). libx264 와 drawtext 가 들어 있는 최신 빌드를 서버 PATH 에 두거나 전체 경로를 적는다.
                      확인: ffmpeg -hide_banner -encoders | findstr libx264   /   ffmpeg -hide_banner -filters | findstr drawtext
                    AviewExe 비우면 AmHome\bin 안의 aview64.exe(12.6 이상), 없으면 aview.exe(12.4). AutoView 는 모델 실행과 같은 AutoMod 라이선스를 쓴다.
                    CutTimeoutMin 컷 하나의 AutoView 제한(기본 20분). 넘으면 그 컷만 실패한다.
                    최초 1회 확인: 영상을 한 편 만들어 서비스 세션에서 AutoView 가 그리는지 본다. "AutoView가 MP4를 만들지 않았습니다" 로 실패하면
                      서비스를 로그온 가능한 계정으로 바꾸고 그 계정이 서버 PC 에 로그온해 두거나, 서비스 대신 작업 스케줄러에
                      "사용자가 로그온한 경우에만 실행" 으로 ASH.Server.exe 를 등록한다. 자세한 내용은 docs\server-install.md 5절 "영상 촬영".
   를 확인한다. 환경 변수 Admin__Password 로 관리자 암호를 줄 수도 있다.
   설치 뒤에는 Admin:Password(관리자 암호)로 브라우저 http://서버:8082/admin/settings 에서 위 값을 바꿀 수 있다(아래 "웹 설정").
   설치 폴더 권한을 관리자로 제한한다(관리자 암호 보호):
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
   수동 업로드의 관리자 암호는 평문 HTTP 로 전달된다. 신뢰할 수 있는 사내망에서만 업로드하거나, 앞단에 HTTPS 프록시를 둔다.
6. 브라우저로 http://서버:8082/admin/settings 를 열어 서버 상태를 확인한다. 시작 10초쯤 뒤 "마지막 확인 — 가져옴: x.y.z" 와
   현재 버전이 보이면 끝이다. "실패: …" 면 서버 PC 의 github.com 접속과 GitHub:Proxy 를 확인한다.
7. 새 버전 배포: GitHub 에 정식 릴리스(ASH.json, ASH-Setup-x.y.z.exe 포함)를 만들면 서버가 60분 안에 가져간다.
   Directory.Build.props 의 <Version> 을 올려 main 에 push 하면 GitHub Actions 가 자동으로 만든다(docs\server-install.md).
   바로 반영하려면 sc stop ASH.Server && sc start ASH.Server.
   서버가 GitHub 에 닿지 않을 때만 수동 업로드:  $env:ASH_ADMIN_TOKEN='<관리자 암호>'; scripts\publish-release.ps1 -Server http://서버:8082
   (수동 업로드도 서명 없이 받지만 PC 가 github.com 과 대조하므로 GitHub 릴리스와 다른 파일은 설치되지 않는다.)

로그
- 이벤트 뷰어 > Windows 로그 > 응용 프로그램, 원본(Source) ASH.Server. (appsettings.json 의 Logging:EventLog 로 수준 조정)
- LocalService 는 이벤트 원본을 만들 수 없다. 관리자 권한으로 한 번만 만든다:
     New-EventLog -LogName Application -Source ASH.Server
  (또는 처음 한 번 LocalSystem 으로 서비스를 시작해 원본이 생기게 한 뒤 LocalService 로 바꾼다.)

서명 없는 업로드 (선택: 서명 키 사용)
- 기본은 서명이 없다. 서버는 서명 없는 ASH.json, Runner, 서버 자동 업데이트를 모두 받는다(Update:AllowUnsigned 기본값 true).
  false 로 두면 서명 없는 업로드를 거절하므로 키가 없는 빌드에서는 쓰지 말 것. 설정 페이지에는
  "무결성 확인: 없음 — PC가 github.com 릴리스와 대조해 확인"이 표시된다.
- 서명을 다시 쓰려면 공개키 update-key.pub 를 저장소 루트에 두고 ASH_SIGNING_KEY(개인키 PEM)로 빌드한다(예전 서명 방식).
  공개키만 있고 개인키가 없으면 build-release.ps1 이 중단한다. 개인키는 커밋하지 않고 암호화한 오프라인 사본을 2개 보관한다.
  잃어버리면 새 키를 자동 업데이트로 보낼 수 없어 모든 PC 에 ASH-Setup 을 직접 다시 설치해야 한다.
- Update:PublicKeyPem 설정은 시험/개발용 덮어쓰기다. 서명을 쓰는 운영에서는 설정하지 말 것
  (설정하면 내장 공개키를 대체하고 경고 로그가 남는다. 빈 값은 개발 환경에서만 "키 없음"으로 쓰인다).

웹 주소 (상단 메뉴 홈 · 분석 · 설정)
- /                          홈: 내 것만 | 전체 작업·분석, 대기·실행 상태(열려 있으면 10초마다 갱신)
- /analyses                  분석 목록
- /analyses/{id}?tab=        분석 하나: status(현황) · conclusion(결론) · compare(비교) · films(영상)
- /admin/jobs                전체 작업 대기열(옛 /jobs 는 여기로 301). 작업 하나는 /jobs/{id}
- /api/home                  홈이 쓰는 JSON(작업·분석·대기 상태)
- /api/analyses/{id}/status  분석 현황 JSON(실행 중 진행·중간 결과)
- /api/films/{id}            영상 한 건의 진행 JSON
- /films/{id}/files/all.zip  그 영상의 컷 파일 전부(zip)

웹 설정 (/admin/settings, 상단 메뉴 "설정")
- Admin:Password가 규칙(4~20자)에 맞아야 열린다. 처음에 관리자 암호를 한 번 입력하면 브라우저에 HttpOnly·SameSite=Strict 쿠키(서버만 아는 무작위
  세션 ID, 8시간, 서버를 다시 시작하면 로그아웃)가 남는다. 암호 자체는 쿠키나 페이지에 들어가지 않는다. 다른 사이트에서 온 POST 는
  Origin 검사로 거절한다. 스크립트는 X-ASH-Admin-Token 헤더로 같은 주소에 POST 해도 된다.
- 바꿀 수 있는 값: Jobs(Enabled, WorkerEnabled, AmHome, Parallel, DialogButton, RunTimeoutMin, BuildTimeoutMin, JobTimeoutHours,
  KeepRunsDays, Dir), Film(FfmpegPath, AviewExe, CutTimeoutMin), GitHub(Enabled, BaseUrl, CheckIntervalMin, UpdateServer, Proxy, Dns), Urls, Releases:Dir, Update:AllowUnsigned.
  Admin:Password, Update:PublicKeyPem, Logging 은 웹에서 바꾸지 않는다(파일을 직접 고친다).
- 바로 적용(저장하면 다음 작업·다음 정리부터): Jobs:Enabled, AmHome, Parallel, DialogButton, RunTimeoutMin, BuildTimeoutMin,
  JobTimeoutHours, KeepRunsDays, Film:CutTimeoutMin, Film:FfmpegPath, Film:AviewExe.  재시작 필요: 나머지 전부(Jobs:WorkerEnabled, Jobs:Dir, GitHub:*, Urls, Releases:Dir, Update:AllowUnsigned).
- 경로 항목(AmHome, Dir, FfmpegPath, AviewExe, Releases:Dir) 옆의 "찾아보기…" 는 서버 PC 의 드라이브·폴더를 보여 주는 대화상자다(관리자 로그인 필요, 이름만 읽음).
- 저장은 값을 검사(정수 범위, 켬/끔, 주소 형식)한 뒤 appsettings.json 을 임시 파일로 쓰고 바꿔 넣는다. 이전 파일은
  appsettings.json.yyyyMMdd-HHmmss.bak 으로 남는다. 모르는 키는 그대로 두지만 파일 안의 // 주석은 없어진다(.bak 에는 남음).
  AmHome 에 AutoMod 빌드 exe 가 없으면 경고만 하고 저장한다.
- "서비스 다시 시작"은 자동 업데이트와 같은 update\apply-update.bat 을 파일 교체 없이 돌려 sc stop / sc start 한다(update\apply.log).
  작업이 실행 중이면 확인란을 체크해야 하고, 그 작업은 실패로 남는다. 콘솔로 실행 중이면 버튼이 없다(직접 다시 실행).
  서비스 계정이 설치 폴더에 쓸 수 없으면(LocalService) 저장이 실패하고 파일은 그대로다.

분석 삭제 (관리자, /admin/analyses/<id>/delete)
- 분석 페이지 맨 아래 "관리자: 분석 삭제…" 링크가 확인 페이지로 간다(로그인 필요, /admin/settings 와 같은 관리자 암호). 분석 이름을 입력해야 지워진다.
- 분석의 작업은 남고 "분석에 넣지 않은 작업"이 된다(안·버전·선정 해제, 버전 메모 유지). 분석·안·컷 목록·이 분석의 영상 기록과 파일은 지워지며 되돌릴 수 없다.
- 이 분석의 영상이 대기 중이거나 만드는 중이면 409 로 거절한다(먼저 취소). 작업이 없는 분석은 누구나 분석 페이지에서 지울 수 있다.

운영
- 백업: Releases:Dir 폴더 전체와 appsettings.json. 복원: 서비스를 멈추고 복사해 넣은 뒤(폴더 권한 확인) sc start ASH.Server.
- 서버 자동 업데이트(FOTA Gateway 와 같은 방식): ASH.Server.json 과 서버 zip 을 GitHub 에서 받아 크기·SHA-256 을 확인한 뒤
  update\apply-update.bat 이 서비스를 멈추고 exe 를 바꿔 다시 시작한다. appsettings.json 과 배포 파일은 그대로 둔다.
  60초 안에 새 버전이 응답하지 않으면 이전 exe 로 자동 되돌리고(실패한 exe 는 ASH.Server.exe.bad) 그 버전은 다시 시도하지 않는다.
  결과: 설정 페이지 "지난 업데이트" 줄, update\apply.log. 이전 exe 는 ASH.Server.exe.old 로 남는다.
  LocalService 계정으로 바꾸면 설치 폴더에 쓸 수 없어 자동 교체가 실패한다(이전 버전 유지).
  0.2.0 이하 서버는 이 기능이 없어 한 번은 손으로 바꾼다: sc stop ASH.Server, ASH.Server.exe 교체, sc start ASH.Server.
- Runner(ash-run) 배포: GitHub 릴리스의 ASH.Runner.json 과 ASH-Runner-x.y.z-win-x64.zip 을 크기·SHA-256 확인 후 runner\ 폴더에 보관하고,
  분석 PC 의 ash-run 이 작업 시작 전에 /api/ash/runner 로 받은 매니페스트를 github.com 릴리스와 대조한 뒤(같을 때만) 스스로 교체한다.
  github.com 에 접속할 수 없거나 내용이 다르면 "update skipped: ..." 한 줄을 내고 지금 버전으로 작업을 계속한다. 설정 페이지 "Runner(ash-run)" 줄에서 확인한다.
- 서명 키를 다시 쓰는 경우에만: 서버 exe 를 교체한 뒤 설정 페이지의 서명 키 ID 가 교체 전과 같은지 확인한다.
- 롤백: publish-release.ps1 -Force 로 낮은 버전을 올려도 이미 새 버전을 쓰는 PC 는 내려가지 않는다(서버 버전이 더 낮다는 안내만 나온다).
  되돌리려면 옛 코드를 더 높은 버전 번호로 다시 빌드해 게시한다.
- files\ 폴더는 업로드마다 커진다. 지울 때는 현재 버전과 롤백 대상 설치 파일을 남긴다.
- 로컬 관리자 권한이 없는 사용자 PC 는 IT 가 배포한다:  ASH-Setup-x.y.z.exe /silent /server:http://서버:8082
