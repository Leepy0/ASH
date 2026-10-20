using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using ASH.Core.Install;
using ASH.Core.Settings;

namespace ASH.Installer;

/// <summary>Win32 glue around Core's StepExecutor: extracts the embedded DLLs, logs to %TEMP%\ASH-install.log and the UI.</summary>
sealed class StepRunner : IDisposable
{
    public static readonly string LogPath = Path.Combine(Path.GetTempPath(), "ASH-install.log");
    static readonly string FallbackRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ASH");
    readonly Action<string> _ui;
    readonly Win32Registry _reg;
    readonly StepExecutor _exec;
    readonly RunResult _session = new(); // warnings not tied to one plan (user hive)
    readonly List<RunResult> _results = new();
    readonly string? _userAppData; // the install target user's %APPDATA%; null = unknown, nothing is written there
    string? _src;

    /// <param name="silent">/silent: an unresolvable interactive user means no HKCU registration (no guessing whose hive).</param>
    public StepRunner(Action<string> ui, bool silent = false)
    {
        _ui = ui;
        var (sid, noHkcu) = ResolveUserSid(silent);
        _reg = new Win32Registry(sid, noHkcu);
        _userAppData = UserDataPaths.AppData(sid, noHkcu, _reg, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        _exec = new StepExecutor(new RealFileSystem(FallbackRoot), _reg, Log);
    }

    public static bool Elevated => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>A running ZWCAD locks the DLLs and may rewrite its profile keys on exit.</summary>
    public static bool ZwcadRunning()
    {
        var p = Process.GetProcessesByName("ZWCAD"); // name match is case-insensitive
        foreach (var x in p) x.Dispose();
        return p.Length > 0;
    }

    public const string CloseZwcad = "ZWCAD를 닫고 다시 실행하세요.";

    /// <summary>Plan Warn messages (autoload not registered), across every install run so far.</summary>
    public List<string> AutoloadWarnings => _results.SelectMany(r => r.Warnings).ToList();
    /// <summary>Warnings outside plans, e.g. the user's hive was not loaded so HKCU was used.</summary>
    public List<string> SessionWarnings => _session.Warnings;
    public List<string> FallbackDirs => _results.Where(r => r.FallbackDir != null).Select(r => r.FallbackDir!).ToList();
    public int ExitCode => RunResult.ExitCode(_results.Append(_session));

    static string FallbackDir(ZwcadInstall t) => Path.Combine(FallbackRoot, t.Version.Length > 0 ? t.Version : "unknown");

    // HKCU of the elevated token is the admin's hive when another account was used at the UAC prompt
    // SYSTEM's own HKCU (S-1-5-18) is never the CAD user's, so it is skipped rather than written
    (string? Sid, bool NoHkcu) ResolveUserSid(bool silent)
    {
        var me = WindowsIdentity.GetCurrent();
        bool system = me.User?.IsWellKnown(WellKnownSidType.LocalSystemSid) == true;
        var u = InteractiveUser.Find();
        if (u == null ? silent || system : system && u.Value.Sid == me.User) return NoUserHive();
        if (u == null || u.Value.Sid == me.User) { Log("등록 대상 사용자: " + me.Name); return (null, false); }
        var sid = u.Value.Sid.Value;
        if (!Win32Registry.UserHiveLoaded(sid))
        {
            if (system) return NoUserHive();
            Warn($"{u.Value.Account} 사용자 하이브가 로드되지 않아 현재 계정({me.Name})의 HKCU에 등록합니다.");
            return (null, false);
        }
        Log($"등록 대상 사용자: {u.Value.Account} (HKEY_USERS\\{sid}, 실행 계정 {me.Name})");
        return (sid, false);
    }

    (string?, bool) NoUserHive()
    {
        const string w = "사용자 하이브를 알 수 없어 HKCU 등록을 건너뜀";
        _session.Warnings.Add(w);
        Log("경고: " + w);
        return (null, true);
    }

    /// <summary>A warning that makes the exit code 2 and shows in the window's warning list.</summary>
    public void Warn(string w)
    {
        _session.Warnings.Add(w);
        Log("경고: " + w);
    }

    /// <summary>ServerUrl saved for the install target user ("" when none or unknown).</summary>
    public string ExistingServerUrl() => _userAppData == null ? "" : UserSettings.Load(UserSettings.PathIn(_userAppData)).ServerUrl;

    /// <summary>Writes ServerUrl to the target user's %APPDATA%\ASH\ash.user.tsv (the user the HKCU registration is for, not necessarily the elevated account).</summary>
    public void SaveServerUrl(string url)
    {
        if (_userAppData == null) { Warn("사용자 폴더를 알 수 없어 서버 주소를 저장하지 못했습니다"); return; }
        var path = UserSettings.PathIn(_userAppData);
        try
        {
            (UserSettings.Load(path) with { ServerUrl = url }).Save(path);
            Log($"서버 주소 저장: {url} → {path}");
        }
        catch (Exception ex) { Warn($"서버 주소를 저장하지 못했습니다 ({path}): {ex.Message}"); }
    }

    public void Log(string line)
    {
        try { File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + line + Environment.NewLine, Encoding.UTF8); }
        catch { /* log file is best effort */ }
        _ui(line);
    }

    public void Install(IList<ZwcadInstall> targets)
    {
        Log($"=== 설치 시작 ({targets.Count}개, 관리자={Elevated}) ===");
        if (!Elevated) Log("관리자 권한이 아님: 모든 사용자(HKLM) 등록은 건너뜀, 현재 사용자(HKCU)만 등록");
        var src = Extract();
        foreach (var t in targets)
        {
            Log($"-- ZWCAD {t.Version} {t.Language} {t.InstallDir}");
            var r = _exec.Install(t, src, Elevated, _reg, FallbackDir(t), Program.SupportedVersion);
            _results.Add(r);
            if (!r.Ok) Log("이 ZWCAD는 설치 실패. ZWCAD가 실행 중이면 닫고 다시 시도하세요. 로그: " + LogPath);
        }
        Log(ExitCode switch { 0 => "=== 설치 완료 ===", 2 => "=== 설치 완료 (경고 있음) ===", _ => "=== 설치 중 오류 있음 ===" });
    }

    public void Uninstall(IList<ZwcadInstall> targets)
    {
        Log($"=== 제거 시작 ({targets.Count}개) ===");
        var r = _exec.Uninstall(targets, Elevated, _reg, FallbackDir);
        _results.Add(r);
        if (r.Ok) Log("=== 제거 완료 ===");
        else Log("=== 제거 중 오류 " + r.Failures.Count + "건 (ZWCAD가 실행 중이면 닫고 다시 시도): "
                 + string.Join(", ", r.Failures.Select(f => f.Step.Target)) + " ===");
    }

    // %TEMP%\ASH-install\<guid>\ASH.Plugin.dll + ASH.Core.dll
    string Extract()
    {
        if (_src != null) return _src;
        var dir = Path.Combine(Path.GetTempPath(), "ASH-install", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var name in new[] { "ASH.Plugin.dll", "ASH.Core.dll" })
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new InvalidOperationException("내장 리소스 없음: " + name);
            using var f = File.Create(Path.Combine(dir, name));
            s.CopyTo(f);
        }
        return _src = dir;
    }

    public void Dispose()
    {
        try { if (_src != null) Directory.Delete(_src, true); } catch { /* temp cleanup is best effort */ }
    }
}
