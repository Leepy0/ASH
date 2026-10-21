using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
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
        var running = false;
        foreach (var name in new[] { "ZWCAD", "ZWCADM" }) // name match is case-insensitive
        {
            var p = Process.GetProcessesByName(name);
            running |= p.Length > 0;
            foreach (var x in p) x.Dispose();
        }
        return running;
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
        if (u == null ? silent || system : system && u.Value.Sid == me.User) return NoUserHive(system);
        if (u == null || u.Value.Sid == me.User) { Log("등록 대상 사용자: " + me.Name); return (null, false); }
        var sid = u.Value.Sid.Value;
        if (!Win32Registry.UserHiveLoaded(sid))
        {
            if (system) return NoUserHive(system);
            Warn($"{u.Value.Account} 사용자 하이브가 로드되지 않아 현재 계정({me.Name})의 HKCU에 등록합니다.");
            return (null, false);
        }
        Log($"등록 대상 사용자: {u.Value.Account} (HKEY_USERS\\{sid}, 실행 계정 {me.Name})");
        return (sid, false);
    }

    // under SYSTEM (IT deployment) HKLM already covers every user, so skipping HKCU is expected and not a warning (exit code stays 0)
    (string?, bool) NoUserHive(bool system)
    {
        const string w = "사용자 하이브를 알 수 없어 HKCU 등록을 건너뜀";
        if (system) Log(w);
        else { _session.Warnings.Add(w); Log("경고: " + w); }
        return (null, true);
    }

    /// <summary>A warning that makes the exit code 2 and shows in the window's warning list.</summary>
    public void Warn(string w)
    {
        _session.Warnings.Add(w);
        Log("경고: " + w);
    }

    /// <summary>ServerUrl saved for the install target user, else the PC-wide one ("" when none).</summary>
    public string ExistingServerUrl() =>
        UserSettings.Load(_userAppData == null ? UserSettings.MachinePath : UserSettings.PathIn(_userAppData), UserSettings.MachinePath).ServerUrl;

    /// <summary>
    /// Writes ServerUrl to %ProgramData%\ASH\ash.machine.tsv (always: covers SYSTEM deployments and every user of the PC)
    /// and, when a target user profile is known, to that user's %APPDATA%\ASH\ash.user.tsv (the user the HKCU registration is for, not necessarily the elevated account).
    /// A warning (exit code 2) only when nothing was written, or when the machine file fails although we are elevated.
    /// </summary>
    public void SaveServerUrl(string url)
    {
        var machine = UserSettings.MachinePath;
        bool machineOk = false, userOk = false;
        if (!Elevated) Log("관리자 권한이 아님: PC 공용 서버 주소 파일은 쓰지 않음"); // a non-admin must not create the folder (its ACL has to be locked)
        else
            try
            {
                var dir = Path.GetDirectoryName(machine)!;
                var acl = RealFileSystem.LockedAcl(); // Administrators/SYSTEM full, Users read
                if (Directory.Exists(dir)) ReclaimDir(dir, acl); else Directory.CreateDirectory(dir, acl);
                // temp file + move: a file a standard user pre-created at the target (and still owns) is replaced, never written through
                var tmp = machine + ".tmp";
                File.Delete(tmp);
                new UserSettings(url).Save(tmp);
                File.Delete(machine);
                File.Move(tmp, machine);
                machineOk = true;
                Log($"서버 주소 저장(PC 공용): {url} → {machine}");
            }
            catch (Exception ex) { Log($"PC 공용 서버 주소 파일을 쓰지 못했습니다 ({machine}): {ex.Message}"); }
        if (_userAppData != null)
        {
            var path = UserSettings.PathIn(_userAppData);
            try
            {
                (UserSettings.Load(path) with { ServerUrl = url }).Save(path);
                userOk = true;
                Log($"서버 주소 저장: {url} → {path}");
            }
            catch (Exception ex) { Warn($"서버 주소를 저장하지 못했습니다 ({path}): {ex.Message}"); }
        }
        else Log("사용자 폴더를 알 수 없어 사용자 파일은 쓰지 않음");
        if (!machineOk && !userOk) Warn($"서버 주소를 저장하지 못했습니다 (PC 공용 파일 {machine}, 사용자 파일 없음/실패)");
        else if (!machineOk && Elevated) Warn($"서버 주소를 PC 공용 파일에 저장하지 못했습니다 ({machine})");
    }

    // A folder a standard user pre-created before the first elevated install: take ownership back (owner can always rewrite the ACL) and lock it.
    // SIDs only, no localized account names.
    static void ReclaimDir(string dir, DirectorySecurity locked)
    {
        var di = new DirectoryInfo(dir);
        if ((di.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("재해석 지점(junction/symlink)이라 사용하지 않음: " + dir);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var owner = di.GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier));
        if (!admins.Equals(owner) && !new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Equals(owner)) locked.SetOwner(admins);
        di.SetAccessControl(locked);
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
            Log($"-- {t.Product} {t.Version} {t.Language} {t.InstallDir}");
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

    // %TEMP%\ASH-install\<guid>\<every file of InstallPlanner.Files>
    string Extract()
    {
        if (_src != null) return _src;
        var dir = Path.Combine(Path.GetTempPath(), "ASH-install", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var name in ASH.Core.Install.InstallPlanner.Files)
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
