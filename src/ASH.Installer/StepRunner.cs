using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using ASH.Core.Install;

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
    string? _src;

    public StepRunner(Action<string> ui)
    {
        _ui = ui;
        _reg = new Win32Registry(ResolveUserSid());
        _exec = new StepExecutor(new RealFileSystem(FallbackRoot), _reg, Log);
    }

    public static bool Elevated => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>Plan Warn messages (autoload not registered), across every install run so far.</summary>
    public List<string> AutoloadWarnings => _results.SelectMany(r => r.Warnings).ToList();
    /// <summary>Warnings outside plans, e.g. the user's hive was not loaded so HKCU was used.</summary>
    public List<string> SessionWarnings => _session.Warnings;
    public List<string> FallbackDirs => _results.Where(r => r.FallbackDir != null).Select(r => r.FallbackDir!).ToList();
    public int ExitCode => RunResult.ExitCode(_results.Append(_session));

    static string FallbackDir(ZwcadInstall t) => Path.Combine(FallbackRoot, t.Version.Length > 0 ? t.Version : "unknown");

    // HKCU of the elevated token is the admin's hive when another account was used at the UAC prompt
    string? ResolveUserSid()
    {
        var me = WindowsIdentity.GetCurrent();
        var u = InteractiveUser.Find();
        if (u == null || u.Value.Sid == me.User) { Log("등록 대상 사용자: " + me.Name); return null; }
        var sid = u.Value.Sid.Value;
        if (!Win32Registry.UserHiveLoaded(sid))
        {
            var w = $"{u.Value.Account} 사용자 하이브가 로드되지 않아 현재 계정({me.Name})의 HKCU에 등록합니다.";
            _session.Warnings.Add(w);
            Log("경고: " + w);
            return null;
        }
        Log($"등록 대상 사용자: {u.Value.Account} (HKEY_USERS\\{sid}, 실행 계정 {me.Name})");
        return sid;
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
            var r = _exec.Install(t, src, Elevated, _reg, FallbackDir(t));
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
