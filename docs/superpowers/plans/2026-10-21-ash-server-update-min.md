# ASH 1.6차 구현 계획: 서버 최소 기능(배포 버전 관리) + 플러그인 업데이트 확인 + net10 자가 포함 정렬

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 사내 서버(`ASH.Server`)가 서명된 ASH 배포 정보와 설치 파일을 제공하고, 플러그인이 ZWCAD 시작 시 새 버전을 알리고 `AGVUPDATE`로 검증된 설치 파일을 받게 한다. 실행형 산출물(`ash-run`, 서버)은 net10 win-x64 자가 포함 단일 파일로 배포한다.

**Architecture:** 배포 정보는 Gantt_Scheduler와 같은 방식: 매니페스트 JSON(버전·최소 지원·파일·SHA-256·크기) + 분리 서명(ECDSA P-256, SHA-256, DER, 매니페스트 바이트 그대로). 개인키는 릴리스 PC에만, 공개키(`update-key.pub`, PEM 여러 개 허용)는 플러그인·서버에 리소스로 들어간다. 서버는 저장·제공·업로드 검증만 하고 판단은 클라이언트(플러그인)가 한다. 서명 검증 코드는 net48(플러그인)과 net10(서버·도구·테스트)에서 같이 쓰는 공유 소스로 둔다.

**Tech Stack:** C#, ASP.NET Core minimal API (net10, `Microsoft.Extensions.Hosting.WindowsServices`), `Microsoft.AspNetCore.Mvc.Testing`(테스트), netstandard2.0 Core(`System.Net.Http`, `DataContractJsonSerializer` — 새 패키지 없음), net48 플러그인.

**Spec:** `docs/superpowers/specs/2026-10-21-ash-server-design.md` §17(버전·업데이트), `docs/superpowers/specs/2026-10-20-ash-plugin-design.md` §14.1. 참고 구현: Leepy0/Gantt_Scheduler `tools/release/pack.py`, `src/GanttScheduler/Services/UpdateKey.cs`, `.github/workflows/release.yml`; Leepy0/AGV_FOTA_GATEWAY `src/Gateway.Server/Gateway.Server.csproj`(RID 지정 시 자가 포함 단일 파일).

## Global Constraints

- 버전 하나: `Directory.Build.props` `<Version>`(현재 0.1.0)을 전 프로젝트가 공유. 매니페스트 `version`도 이 값.
- 실행형 프로젝트(`ASH.Runner`, `ASH.Server`, `tools/AshRelease`)는 `net10.0`. FOTA 방식: csproj에 `<SelfContained Condition="'$(RuntimeIdentifier)' != ''">true</SelfContained>` + `<PublishSingleFile …>true</PublishSingleFile>`; 릴리스는 `-r win-x64`. Core는 netstandard2.0, 플러그인·설치 프로그램은 net48 그대로.
- 매니페스트 파일 이름 `ASH.json`, 서명 `ASH.json.sig`. 필드(DataContract, camelCase 키): `product`("ASH"), `version`, `minSupported`, `formatVersion`(int, 현재 1), `rid`("win-x64"), `file`("ASH-Setup-<ver>.exe"), `sha256`(소문자 hex), `size`(long), `published`(UTC ISO8601 `yyyy-MM-ddTHH:mm:ssZ`), `notes`(string). 서명은 파일 바이트 그대로에 대한 것 — 서버·클라이언트는 매니페스트를 다시 직렬화하지 않고 받은 바이트로 검증한다.
- 서명: ECDSA P-256 + SHA-256, 서명 형식 DER(Gantt·openssl과 동일). 공개키 파일 `update-key.pub`(PEM `-----BEGIN PUBLIC KEY-----` SPKI, 여러 개 → 하나라도 맞으면 통과). 공개키가 빌드에 없으면 서버는 업로드를 받되 경고를 기록하고, 플러그인은 서명 없이 SHA-256만 확인하고 "서명 키 미설정" 경고를 낸다.
- 버전 비교: SemVer `MAJOR.MINOR.PATCH[-pre]`; pre-release는 같은 번호의 정식보다 낮다.
- 서버 기본 포트 5080(appsettings `Urls`), 데이터 폴더 `%ProgramData%\ASH.Server\releases\`(설정 `Releases:Dir`). 관리 업로드는 헤더 `X-ASH-Admin-Token`(appsettings `Admin:Token`, 비어 있으면 업로드 기능 꺼짐).
- 플러그인은 서버 접속 실패·시간 초과(3 s)로 절대 ZWCAD를 멈추거나 오류 창을 띄우지 않는다. 확인은 백그라운드, 결과 표시는 UI 스레드(`Application.Idle` 1회).
- 외부 호스트 의존 금지: 런타임에 사내 서버 외 접속 없음.
- 커밋 메시지는 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`로 끝난다.

## Review Focus

1. 서버가 꺼져 있거나 주소가 틀린 PC에서 ZWCAD 시작: 3 s 안에 조용히 포기, 명령줄에 1줄 "업데이트 확인 실패(서버 응답 없음)"만. → Task 5 `Startup_Check_Timeout_Does_Not_Throw`(Core 측 단위 테스트) + 수동 U1.
2. 매니페스트·설치 파일이 중간에 바뀜(해시 불일치, 서명 불일치): 설치 파일을 쓰지 않고 지운다. → Task 2·3 테스트.
3. 현재 버전이 최소 지원 미만: 경고(서버 기능 없음이므로 차단 대상 없음), 새 버전 안내. → Task 2 `Evaluate_Below_Minimum`.
4. 업로드 토큰 없음/틀림, 서명 틀림: 401/400으로 거절, 기존 최신 버전 유지. → Task 4 테스트.
5. 업로드 도중 실패: 반쯤 쓴 파일이 "최신"으로 노출되지 않는다(임시 파일 → 검증 → 원자적 이동 → 매니페스트 교체 순). → Task 4 테스트.

---

### Task 1: net10 자가 포함 정렬

**Files:** Modify `src/ASH.Runner/ASH.Runner.csproj`(FOTA 조건부 SelfContained/PublishSingleFile), `scripts/build-release.ps1`(`ash-run`을 `-r win-x64` 자가 포함 단일 파일로 publish → `dist/ASH-Runner-<ver>-win-x64.zip`), `docs/superpowers/specs/2026-10-21-ash-server-design.md`(서버·워커 net8 → net10, 자가 포함 단일 파일 명시), README(Runner 설치: zip 풀고 `ash-run.exe` 실행, 런타임 불필요).
- [ ] build-release 실행 → zip 안 `ash-run.exe` 하나(+pdb 제외)와 `ash-run.exe --help` 실행 확인. 커밋 `build: self-contained single-file ash-run, net10 server target`.

### Task 2: Core 배포 정보 모델·버전 판단·클라이언트

**Files:** Create `src/ASH.Core/Update/ReleaseManifest.cs`(DataContract 레코드형 클래스 + `static ReleaseManifest Parse(byte[] json)` / `byte[] ToJson()`), `Update/SemVersion.cs`(`TryParse`, `CompareTo`), `Update/UpdateCheck.cs`(`static UpdateStatus Evaluate(string current, ReleaseManifest m)` → `enum UpdateState { UpToDate, Available, BelowMinimum, ServerOlder }` + 메시지), `Update/UpdateClient.cs`(`UpdateClient(Uri server, TimeSpan timeout, HttpMessageHandler? handler = null)`; `Task<FetchedManifest> GetManifestAsync(CancellationToken)` → `FetchedManifest(byte[] Json, byte[]? Sig, ReleaseManifest Manifest)`; `Task<string> DownloadVerifiedAsync(ReleaseManifest m, string destDir, CancellationToken)` — `GET /download/<file>`를 `<dest>\<file>.part`로 받아 SHA-256·크기 확인 후 이름 변경, 불일치 시 지우고 `InvalidDataException`). 엔드포인트: `GET /api/ash/version` → JSON 본문 = 매니페스트 바이트 그대로, `GET /api/ash/version.sig` → 서명 바이트(없으면 404).
- Tests `tests/ASH.Tests/Update/*`: SemVer 순서(0.1.0 < 0.1.1 < 0.2.0-rc1 < 0.2.0), Evaluate 4상태, 매니페스트 왕복(필드·camelCase), 클라이언트는 가짜 `HttpMessageHandler`로: 정상, 404 sig, 해시 불일치(파일 삭제됨), 시간 초과 → `TaskCanceledException`/`HttpRequestException`을 호출자가 잡을 수 있게 그대로.
- [ ] 커밋 `feat(core): release manifest, semver, update check, update client`.

### Task 3: 서명(공유 소스) + 릴리스 도구

**Files:** Create `src/Shared/ManifestSignature.cs`(namespace `ASH.Shared`; `ManifestSignature(string pemWithOneOrMoreKeys)`; `bool Verify(byte[] data, byte[] derSignature)`; `string KeyId` = SPKI SHA-256 앞 16 hex; net48·net10 둘 다 컴파일: SPKI DER에서 P-256 공개점(마지막 65바이트 `04||X||Y`)을 꺼내 `ECParameters`로 `ECDsa.Create(ECParameters)`(net48은 `ECDsaCng`도 가능), DER 서명 → IEEE P1363(r‖s 각 32바이트) 변환 후 `VerifyData(data, sig, SHA256)`), `tools/AshRelease/AshRelease.csproj`(net10 console, `<Compile Include="..\..\src\Shared\ManifestSignature.cs" Link=… />`): 명령 `keygen --out <dir>`(`ash-signing.pem` 개인키 PKCS#8, `update-key.pub` 공개키), `manifest --file <setup.exe> --version <v> --min <v> --notes <file> --out ASH.json`, `sign --key <pem> --in ASH.json`(→ `ASH.json.sig`, DER), `verify --pub update-key.pub --in ASH.json`.
- `ASH.Core`는 공유 소스를 쓰지 않는다(netstandard2.0에 `ECParameters`가 없을 수 있음 — 구현자가 확인; 있으면 Core에 넣어도 됨, 원장에 기록). 플러그인·서버·테스트·도구는 `<Compile Include … Link>`로 공유.
- 공개키 위치: 저장소 루트 `update-key.pub`(없으면 생성 안 됨 → 서명 확인 생략 경로). 플러그인·서버 csproj는 존재할 때만 `EmbeddedResource`.
- `scripts/build-release.ps1`: Setup 생성 뒤 `AshRelease manifest` → `ASH_SIGNING_KEY` 환경 변수(개인키 PEM 파일 경로)가 있으면 `sign`, `-RequireSignature` 스위치면 키 없을 때 실패 → `dist/ASH.json`(+`.sig`).
- Tests: openssl과 같은 형식의 서명을 net10 `ECDsa`로 만들어(DER) net48·net10 모두에서 `ManifestSignature.Verify` 통과; 바이트 1개 변경 시 실패; 키 2개 중 두 번째로 서명 → 통과; 잘못된 PEM → `ArgumentException`. 도구 keygen→manifest→sign→verify 왕복 테스트(net10).
- [ ] 커밋 `feat(release): manifest signing tool and shared verifier`.

### Task 4: `ASH.Server` (최소)

**Files:** Create `src/ASH.Server/ASH.Server.csproj`(net10 Web, WindowsServices, FOTA 자가 포함 조건, 공유 소스 링크, `update-key.pub` 조건부 리소스), `Program.cs`(`UseWindowsService()`; `--install`/`--uninstall`은 `sc.exe create ASH.Server binPath= "<exe>" start= auto` / `sc.exe delete`, 관리자 권한 필요 안내), `ReleaseStore.cs`(폴더 `Releases:Dir`; `current/ASH.json`·`ASH.json.sig`·설치 파일; 업로드는 `incoming/<guid>/`에 쓰고 검증(매니페스트 파싱, 파일 이름·크기·SHA-256 일치, 공개키 있으면 서명 필수) → 설치 파일을 `files/`로 이동 → `current/` 매니페스트를 임시 파일 후 `File.Replace`로 교체; 이전 매니페스트는 `history/<version>/`에 보관), 엔드포인트: `GET /healthz`, `GET /api/ash/version`, `GET /api/ash/version.sig`, `GET /download/{file}`(파일 이름은 `files/` 안의 것만, 경로 조작 거부), `POST /api/admin/releases`(multipart: `manifest`, `signature?`, `setup`; 토큰 필요), `GET /`(간단한 HTML 상태 페이지: 현재 버전, 최소 지원, 게시 시각, 키 지문, 업로드 가능 여부 — 한국어). `appsettings.json` 기본값.
- `tests/ASH.Server.Tests/ASH.Server.Tests.csproj`(net10, `Microsoft.AspNetCore.Mvc.Testing`)를 `ASH.sln`에 추가: 업로드 정상/토큰 없음 401/서명 틀림 400/해시 틀림 400(기존 버전 유지)/`../` 다운로드 거부/업로드 중 예외 시 `current` 불변/sig 없는 매니페스트(키 없는 서버) 허용 + 경고.
- `scripts/build-release.ps1`: `ASH.Server` win-x64 자가 포함 단일 파일 publish → `dist/ASH-Server-<ver>-win-x64.zip`(exe + appsettings.json + README-server.txt). `scripts/publish-release.ps1 -Server http://host:5080 -Token <t>`: `dist/ASH.json`·`.sig`·Setup을 업로드.
- [ ] 커밋 `feat(server): minimal ASH.Server for release distribution`.

### Task 5: 플러그인 업데이트 확인·`AGVUPDATE`

**Files:** Modify `ASH.Core.Settings.ProjectSettings`(`ServerUrl` 기본 ""), 그러나 서버 주소는 도면이 아니라 PC 단위 설정이 맞으므로 **`%APPDATA%\ASH\ash.user.tsv`**(키 `ServerUrl`) 새 파일 — `ASH.Core.Settings.UserSettings.Load/Save` + 테스트; 설치 프로그램에 `/server:<url>` 옵션을 추가해 설치 시 기록(무인 배포용). Plugin: `Update/UpdateNotifier.cs`(`PluginEntry.Initialize`에서 `ServerUrl` 있으면 `Task.Run` → `UpdateClient` 3 s → 결과를 `Application.Idle` 1회 핸들러로 명령줄에 1줄 + `PaletteStatus.Set`; 예외 전부 삼킴), `Commands/UpdateCommands.cs`(`AGVUPDATE`: 서버 주소 없으면 입력 받아 저장; 매니페스트+서명 받기 → 플러그인 리소스 `update-key.pub`로 검증(없으면 경고) → `UpdateCheck.Evaluate(현재 어셈블리 버전)` → Available이면 `%TEMP%\ASH-update\`로 `DownloadVerifiedAsync` → "ZWCAD를 종료한 뒤 <경로>를 실행하세요" + 폴더 열기; `AGVSERVER`: 서버 주소 보기/바꾸기), 팔레트 상단·리본 "레이아웃" 패널에 "업데이트" 버튼, `docs/plugin-manual-test.md` U1–U6.
- 플러그인 버전 = `typeof(PluginEntry).Assembly.GetName().Version` 대신 `AssemblyInformationalVersion`(= `<Version>`).
- [ ] 빌드 0/0, 테스트, 커밋 `feat(plugin): update check on startup and AGVUPDATE`.

### Task 6: 끝까지 확인 + 문서

- 통합 테스트(net10, `ASH.Server.Tests`): `WebApplicationFactory`로 서버 기동 → 도구로 만든 서명 매니페스트·가짜 Setup 업로드 → Core `UpdateClient`가 받아 `ManifestSignature`로 검증·다운로드·해시 확인 → `UpdateCheck` = Available.
- 실제 기동(이 PC): `dist/ASH-Server-…zip`을 풀어 `ASH.Server.exe --urls http://localhost:5080` 실행 → `publish-release.ps1` 업로드 → `curl /api/ash/version` 확인 → 프로세스 종료. 결과를 보고서에.
- 문서: `docs/server-install.md`(서버 PC 설치: zip 풀기 → `appsettings.json` 토큰·포트 → `ASH.Server.exe --install` → 방화벽 5080 허용 → 상태 페이지 확인 → 릴리스 PC에서 `publish-release.ps1`), README 갱신, 서명 키 관리(개인키는 릴리스 PC 밖으로 내보내지 않음, 키 교체는 새 공개키를 먼저 추가한 버전을 옛 키로 서명해 배포).
- [ ] 커밋 `docs: server install and release signing`.

## 범위 밖(서버 본작업으로)

서버 자기 업데이트(Gantt `UpdateService/UpdateApplier` 이식), 워커·대기열·DB·결과 화면, `manifest.json` 업로드 묶음, 공용 로직·에셋 동기화, 최소 지원 미만 시 서버 기능 차단.
