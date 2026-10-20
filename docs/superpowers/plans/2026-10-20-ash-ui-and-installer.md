# ASH 1.5차 구현 계획: 팔레트·리본 UI 리디자인 + 설치 프로그램

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ZWCAD 안에서 ASH를 탭으로 나뉜 세련된 팔레트와 상단 리본 탭으로 쓰게 하고, 설치 프로그램 하나로 ZWCAD 설치 폴더를 찾아 DLL을 넣고 시작 시 자동 로드되게 한다.

**Architecture:** UI는 WinForms 팔레트(`PaletteSet`, `ZwSoft.ZwCAD.Windows`)를 `TabControl` + 평면(flat) 버튼으로 다시 그리고, 리본은 `ZdWindows.dll`의 `ZwSoft.Windows` API(`ComponentManager.Ribbon`, `RibbonTab/RibbonPanel/RibbonPanelSource/RibbonButton`)로 `PluginEntry.Initialize`에서 지연 생성한다. 설치 프로그램은 `ASH.Installer`(net48 WinForms 단일 exe, 관리자 권한)이며, 탐색·등록 계획 계산은 Core의 `ASH.Core.Install`(레지스트리·파일시스템 인터페이스로 추상화)에 두어 테스트한다.

**Tech Stack:** C#, net48 WinForms(Plugin, Installer), `ZWCAD.NetApi` 20.26.0(`ZdWindows.dll` 리본 API 포함), netstandard2.0 Core, xunit.

**Spec:** `docs/superpowers/specs/2026-10-20-ash-plugin-design.md` §6.2 명령 목록, §9 사용자 흐름. 이 계획의 UI·설치 요구는 2026-10-20 사용자 지시: "탭으로 나누고 상단 리본 메뉴 추가", "설치가 제일 중요: CAD 설치 위치 자동 탐색, DLL 자동 복사, NETLOAD 자동화".

## Global Constraints

- 명령 이름·동작은 바꾸지 않는다. 팔레트와 리본은 같은 명령을 `SendStringToExecute("<CMD> ", true, false, true)`로 호출하고, `CMDNAMES`가 비어 있지 않으면 "실행 중인 명령을 먼저 끝내세요."를 출력하고 보내지 않는다(기존 규칙).
- 리본 API: `ZwSoft.Windows.ComponentManager.Ribbon`, `RibbonTab{Title,Id}`, `RibbonPanel{Source}`, `RibbonPanelSource{Title,Items}`, `RibbonButton{Text,ShowText,ShowImage,Size=RibbonItemSize.Large|Standard,Orientation,CommandParameter,CommandHandler:ICommand,ToolTip,Image,LargeImage}`, `RibbonRowPanel`, `RibbonRowBreak`, `RibbonSeparator`. 어셈블리 참조 `ZdWindows`(패키지에 포함), WPF 참조 `PresentationCore/PresentationFramework/WindowsBase/System.Xaml`(`<UseWPF>true</UseWPF>`). 이 조합은 프로브로 컴파일 확인됨(2026-10-20).
- 리본 탭 Id `ASH_TAB`, 제목 `ASH`. 패널: 레이아웃 / 경로 / CP / 교통제어 / 검증·내보내기. 리본은 `ComponentManager.ItemInitialized`(리본이 아직 없을 때)와 작업공간 전환(`ComponentManager.Ribbon` 재생성 시) 모두에서 멱등하게 다시 만든다. `Initialize()`에서는 이벤트 등록만 하고 무거운 일은 하지 않는다(demand-load 시점에 ZWCAD가 완전히 초기화되지 않을 수 있음).
- 팔레트 GUID `6f1a6c2e-5b7d-4e7a-9c3b-a5b000000001` 유지. 탭: 경로 / CP / 교통제어 / 검증·내보내기(레이아웃 초기화는 상단 공통 버튼). `CpSettings.Current` 바인딩 규칙 유지.
- 디자인: 평면 스타일(`FlatStyle.Flat`, 테두리 없음, hover 색), 기본 글꼴 `Segoe UI 9.5`, 강조색 `#2F6FED`, 배경 `#F7F8FA`, 버튼 높이 36, 아이콘 20px(코드로 그린 단색 글리프, 이미지 파일 없음), 섹션 제목은 11px 회색 대문자 스타일. 팔레트 최소 폭 260.
- 설치 프로그램: `ASH.Installer.exe`(net48, `requireAdministrator` 매니페스트, 단일 exe에 `ASH.Plugin.dll`·`ASH.Core.dll` EmbeddedResource). 탐색 순서 (1) `HKLM\SOFTWARE\ZWSoft\ZWCAD\<ver>\<lang>` 및 `WOW6432Node` 하위의 문자열 값 중 존재하는 폴더에 `ZWCAD.exe`가 있는 것, (2) `HKLM\...\Uninstall\*`에서 `DisplayName`에 `ZWCAD` 포함 → `InstallLocation` 또는 `UninstallString`의 폴더, (3) `%ProgramFiles%\ZWSOFT\ZWCAD *`, `%ProgramFiles(x86)%\ZWSOFT\ZWCAD *` 폴더 스캔. 중복 제거 후 버전·언어·경로 목록.
- 설치 위치 `<ZWCAD 설치 폴더>\ASH\`(쓰기 실패 시 `%ProgramData%\ASH\<ver>\`로 대체하고 알림). 복사 후 `Zone.Identifier` 대체 데이터 스트림 삭제(차단 해제).
- 자동 로드 등록: 각 ZWCAD 프로필 키 `HKCU\Software\ZWSoft\ZWCAD\<ver>\<lang>`(존재하는 모든 `<lang>`)와 `HKLM`(같은 경로) 아래 `Applications\ASH`에 `LOADER`(REG_SZ, dll 전체 경로), `LOADCTRLS`(REG_DWORD 2 = 시작 시 로드), `MANAGED`(REG_DWORD 1), `DESCRIPTION`(REG_SZ "ASH - AGV Simul Helper"). 출처: ZWSOFT 기술 KB(중망CAD 자동 로드)와 사례 블로그(LOADCTRLS/LOADER/MANAGED). 실제 동작은 ZWCAD PC에서 확인(수동 테스트 I1).
- 제거: `ASH.Installer.exe /uninstall` → 키 삭제, 폴더 삭제. 무인: `/silent`(찾은 모든 ZWCAD에 설치). 로그 `%TEMP%\ASH-install.log`.
- Core의 설치 계획 로직은 외부 패키지 없이: `IRegistryReader`/`IRegistryWriter`/`IFileSystem` 인터페이스 + exe 쪽 `Microsoft.Win32` 구현. 테스트는 가짜 구현으로.
- 커밋 메시지는 `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`로 끝난다.

## Review Focus

1. ZWCAD가 2개 버전 설치된 PC: 설치 프로그램은 둘 다 찾아 체크박스로 보여주고 기본 모두 선택이어야 한다. → Task 1 `Locator_Finds_Two_Versions_From_Registry_And_Folders`.
2. `HKLM` 쓰기 권한이 없을 때(관리자 아님): `HKCU`만 등록하고 성공으로 보고하되 "모든 사용자 등록은 건너뜀"을 로그에 남긴다. → Task 1 `Planner_Skips_Hklm_When_Not_Elevated`.
3. 리본이 아직 생성되지 않은 시점(demand-load)에 `ComponentManager.Ribbon == null`: 예외 없이 `ItemInitialized`에서 재시도해야 한다. → Task 3 코드 리뷰 항목(런타임 미검증, 수동 R24).
4. 팔레트 탭 전환 중 `CpSettings` 값 보존: 탭을 바꿔도 TYPE/GROUP 입력값이 유지되어야 한다(컨트롤은 한 번만 생성). → Task 2 수동 T15-3.
5. 재설치: 이미 `Applications\ASH` 키와 폴더가 있으면 덮어쓰고 중복 키를 만들지 않는다. → Task 1 `Planner_Reinstall_Overwrites_Same_Key`.

---

### Task 1: 설치 계획 로직 (Core, 테스트 가능)

**Files:**
- Create: `src/ASH.Core/Install/IRegistry.cs` (`IRegistryReader { string[] SubKeys(string hive, string path); string? GetString(string hive, string path, string name); IEnumerable<(string Name, object Value)> Values(string hive, string path); }`, `IRegistryWriter { void SetString(...); void SetDword(...); void DeleteKey(...); }`), `IFileSystem.cs` (`DirectoryExists`, `FileExists`, `EnumerateDirectories(string parent, string pattern)`)
- Create: `src/ASH.Core/Install/ZwcadInstall.cs` (`sealed record ZwcadInstall(string Version /*"2026"*/, string Language /*"ko_KR"|"zh_CN"|"en_US"…*/, string InstallDir, string ProfileKeyPath /*Software\ZWSoft\ZWCAD\2026\ko_KR*/)`)
- Create: `src/ASH.Core/Install/ZwcadLocator.cs` (`static List<ZwcadInstall> Find(IRegistryReader reg, IFileSystem fs, string programFiles, string programFilesX86)`)
- Create: `src/ASH.Core/Install/InstallPlanner.cs` (`sealed record InstallStep(string Kind /*CopyFile|Unblock|RegString|RegDword|DeleteKey*/, string Target, string? Value)`, `static List<InstallStep> Plan(IEnumerable<ZwcadInstall> targets, string dllSourceDir, bool elevated, bool uninstall)`)
- Test: `tests/ASH.Tests/Install/ZwcadLocatorTests.cs`, `tests/ASH.Tests/Install/InstallPlannerTests.cs`

- [ ] **Step 1: 테스트**

```csharp
public class ZwcadLocatorTests
{
    [Fact] public void Locator_Finds_Two_Versions_From_Registry_And_Folders()
    {
        var reg = new FakeRegistry();
        reg.Add(@"HKLM\SOFTWARE\ZWSoft\ZWCAD\2026\ko_KR", "InstallPath", @"C:\Program Files\ZWSOFT\ZWCAD 2026");
        var fs = new FakeFs(dirs: new[]{ @"C:\Program Files\ZWSOFT\ZWCAD 2026", @"C:\Program Files\ZWSOFT\ZWCAD 2024" },
                            files: new[]{ @"C:\Program Files\ZWSOFT\ZWCAD 2026\ZWCAD.exe", @"C:\Program Files\ZWSOFT\ZWCAD 2024\ZWCAD.exe" });
        var r = ZwcadLocator.Find(reg, fs, @"C:\Program Files", @"C:\Program Files (x86)");
        Assert.Equal(2, r.Count);
        Assert.Contains(r, i => i.Version == "2026" && i.Language == "ko_KR" && i.InstallDir.EndsWith("ZWCAD 2026"));
        Assert.Contains(r, i => i.Version == "2024" && i.InstallDir.EndsWith("ZWCAD 2024"));   // 폴더 스캔만으로 찾은 경우 Language는 "" 
    }
    [Fact] public void Locator_Dedupes_Same_Dir_From_Registry_And_Uninstall() { /* 같은 폴더가 세 경로에서 나와도 1개 */ }
    [Fact] public void Locator_Ignores_Dir_Without_Exe() { /* ZWCAD.exe 없는 폴더 제외 */ }
}
public class InstallPlannerTests
{
    static ZwcadInstall I => new("2026","ko_KR",@"C:\ZWCAD2026",@"Software\ZWSoft\ZWCAD\2026\ko_KR");
    [Fact] public void Plan_Copies_Unblocks_And_Registers_Hkcu_And_Hklm_When_Elevated()
    {
        var s = InstallPlanner.Plan(new[]{I}, @"D:\src", elevated: true, uninstall: false);
        Assert.Contains(s, x => x.Kind=="CopyFile" && x.Target==@"C:\ZWCAD2026\ASH\ASH.Plugin.dll" && x.Value==@"D:\src\ASH.Plugin.dll");
        Assert.Contains(s, x => x.Kind=="CopyFile" && x.Target.EndsWith("ASH.Core.dll"));
        Assert.Contains(s, x => x.Kind=="Unblock" && x.Target.EndsWith("ASH.Plugin.dll"));
        Assert.Contains(s, x => x.Kind=="RegString" && x.Target==@"HKCU\Software\ZWSoft\ZWCAD\2026\ko_KR\Applications\ASH\LOADER" && x.Value==@"C:\ZWCAD2026\ASH\ASH.Plugin.dll");
        Assert.Contains(s, x => x.Kind=="RegDword" && x.Target.EndsWith(@"\LOADCTRLS") && x.Value=="2");
        Assert.Contains(s, x => x.Kind=="RegDword" && x.Target.EndsWith(@"\MANAGED") && x.Value=="1");
        Assert.Contains(s, x => x.Target.StartsWith(@"HKLM\Software\ZWSoft\ZWCAD\2026\ko_KR\Applications\ASH"));
    }
    [Fact] public void Planner_Skips_Hklm_When_Not_Elevated() { var s = InstallPlanner.Plan(new[]{I}, @"D:\src", false, false); Assert.DoesNotContain(s, x => x.Target.StartsWith("HKLM")); }
    [Fact] public void Planner_Reinstall_Overwrites_Same_Key() { var s = InstallPlanner.Plan(new[]{I,I}, @"D:\src", true, false); Assert.Equal(1, s.Count(x => x.Target.EndsWith(@"HKCU\Software\ZWSoft\ZWCAD\2026\ko_KR\Applications\ASH\LOADER".Substring(4)) && x.Target.StartsWith("HKCU"))); }
    [Fact] public void Uninstall_Deletes_Keys_And_Folder() { var s = InstallPlanner.Plan(new[]{I}, @"D:\src", true, true); Assert.Contains(s, x => x.Kind=="DeleteKey" && x.Target==@"HKCU\Software\ZWSoft\ZWCAD\2026\ko_KR\Applications\ASH"); Assert.Contains(s, x => x.Kind=="DeleteDir" && x.Target==@"C:\ZWCAD2026\ASH"); }
}
```
`FakeRegistry`/`FakeFs`는 `tests/ASH.Tests/Install/Fakes.cs`. Language가 없는(폴더 스캔) 설치는 등록 시 `HKCU\Software\ZWSoft\ZWCAD\<ver>` 아래 존재하는 모든 언어 하위 키에 등록한다(없으면 경고 step `Kind=="Warn"`).

- [ ] **Step 2: 실패 확인 → 구현 → 통과** — `dotnet test tests/ASH.Tests --filter FullyQualifiedName~Install`
- [ ] **Step 3: 커밋** `feat(core): ZWCAD locator and install planner`

### Task 2: 팔레트 리디자인 (탭 + 평면 스타일)

**Files:**
- Modify: `src/ASH.Plugin/Ui/AgvPalette.cs` (PaletteSet 생성만 남김), Create: `Ui/AgvPaletteControl.cs`, `Ui/Theme.cs`(색·글꼴·`FlatButton` 팩토리·글리프 그리기), `Ui/Glyphs.cs`(`static Bitmap Draw(GlyphKind kind, int px, Color color)`: path/reverse/offset/array/split/join/fillet/cp/block/traffic/check/export/init 13종)
- Modify: `docs/plugin-manual-test.md` (T15 팔레트 항목)

**Interfaces:**
- Produces: `static class Theme { Color Accent, Bg, Panel, Text, Muted, Hover; Font Base, Small; Button Make(string text, GlyphKind glyph, string cmd); Label Section(string text); }`, `enum GlyphKind { Init, Path, Reverse, Offset, Array, Split, Join, Fillet, Cp, Block, Traffic, Check, Export }`. `AgvPaletteControl` 레이아웃: 상단 헤더(제품명 + "양식 초기화" 버튼), `TabControl`(탭 4개), 하단 상태 줄(마지막 명령 결과 1줄, `PathCommands.Run`의 결과 메시지를 `PaletteStatus.Set(string)`으로 받음).
- 버튼 클릭 → 기존 `CMDNAMES` 가드 + `SendStringToExecute`. 탭 "CP"에는 기존 입력 컨트롤(상세 모드, TYPE 콤보 DropDownList, GROUP, STATION, 접두어, CAP)과 "CP 배치", "traffic CP 자동 생성". 탭 "교통제어"에는 block 자동 / 교통제어 자동 / Settings 열기(`ash.settings.tsv`를 메모장으로). 탭 "검증·내보내기"에 검증, 내보내기, 마지막 검사 결과 n건 표시.

- [ ] **Step 1: 구현** (WinForms, 디자이너 파일 없음, `DoubleBuffered`, `TabControl` 은 `Appearance.Normal` + `DrawMode.OwnerDrawFixed`로 평면 탭 그리기)
- [ ] **Step 2: 미리보기 캡처** — `scratchpad`의 `palettehost`처럼 WinForms 호스트에서 `DrawToBitmap`으로 `docs/images/palette-preview.png` 갱신 (스크립트 `scripts/capture-palette.ps1`는 저장소에 넣되 ZWCAD 없이 돌아감)
- [ ] **Step 3: 빌드 0/0, 전체 테스트, 커밋** `feat(plugin): tabbed flat palette`

### Task 3: 리본 탭

**Files:**
- Create: `src/ASH.Plugin/Ui/AgvRibbon.cs` (`static class AgvRibbon { static void EnsureBuilt(); static void Remove(); }`), Modify: `PluginEntry.cs` (`Initialize`: `ComponentManager.ItemInitialized += …; if (ComponentManager.Ribbon != null) EnsureBuilt();` `Terminate`: `Remove()`), Modify: `ASH.Plugin.csproj` (`<UseWPF>true</UseWPF>`), Modify: `docs/plugin-manual-test.md` (R24~R27)
- Produces: 탭 `ASH`, 패널 5개. 큰 버튼(세로, 32px 아이콘 = `Glyphs.Draw(kind, 32, Accent)`를 `BitmapSource`로 변환) 1개 + 작은 버튼 행(`RibbonRowPanel`)으로 구성. `ICommand` 구현 `RibbonCmd`는 `CMDNAMES` 가드 후 `SendStringToExecute`. `EnsureBuilt`는 `ribbon.Tabs`에 Id `ASH_TAB`가 이미 있으면 아무것도 하지 않는다. 리본 재생성 감지: `ComponentManager.ItemInitialized`에서 `EnsureBuilt()` 재호출.

- [ ] **Step 1: 구현, 빌드 0/0, 커밋** `feat(plugin): ASH ribbon tab`

### Task 4: 설치 프로그램 exe

**Files:**
- Create: `src/ASH.Installer/ASH.Installer.csproj` (net48 WinExe, `UseWindowsForms`, `ApplicationManifest` with `requireAdministrator`, `ProjectReference` ASH.Core, EmbeddedResource `ASH.Plugin.dll`/`ASH.Core.dll` from `../ASH.Plugin/bin/$(Configuration)/net48/` with a `BeforeBuild` dependency on ASH.Plugin via `ProjectReference` `ReferenceOutputAssembly=false`), `Program.cs`, `MainForm.cs`(찾은 ZWCAD 목록 체크박스, 설치/제거 버튼, 로그 텍스트, 완료 시 "ZWCAD를 다시 시작하면 ASH 탭이 보입니다"), `Win32Registry.cs`(`IRegistryReader/Writer` 구현, `RegistryView.Registry64`와 `Registry32` 둘 다), `RealFileSystem.cs`, `Unblock.cs`(`DeleteFile(path + ":Zone.Identifier")` P/Invoke), `StepRunner.cs`(계획 실행 + 로그 + 실패 시 롤백 없이 중단·보고)
- Create: `scripts/build-release.ps1` (Release 빌드 → `dist/ASH-Setup-<version>.exe` 복사, 버전은 `Directory.Build.props`의 `<Version>`), `app.manifest`
- Modify: `ASH.sln`, `README.md`(설치 3줄: 받기 → 실행 → ZWCAD 재시작), `docs/plugin-manual-test.md`(I1~I6: 설치, 재시작 후 자동 로드, 두 버전, 제거, 비관리자, 재설치)

- [ ] **Step 1: 구현, `dotnet build ASH.sln -c Release` 0/0, `scripts/build-release.ps1` 실행해 `dist/ASH-Setup-0.1.0.exe` 생성 확인, 이 PC(ZWCAD 없음)에서 exe 실행 시 "ZWCAD 설치를 찾지 못했습니다" + 수동 경로 입력 가능 확인(스크린샷 `docs/images/installer-preview.png`), 커밋** `feat(installer): ZWCAD auto-detect installer with autoload registration`

### Task 5: 통합 확인·문서

- [ ] 전체 테스트(양쪽 TFM) 통과, `docs/plugin-manual-test.md`의 새 항목(T15, R24~R27, I1~I6) 정리, `README.md` 갱신, 커밋 `docs: install and UI manual tests`.
