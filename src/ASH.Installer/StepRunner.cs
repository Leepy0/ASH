using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using ASH.Core.Install;

namespace ASH.Installer;

/// <summary>Win32 glue around Core's StepExecutor: extracts the embedded DLLs, logs to %TEMP%\ASH-install.log and the UI.</summary>
sealed class StepRunner : IDisposable
{
    static readonly string LogPath = Path.Combine(Path.GetTempPath(), "ASH-install.log");
    readonly Action<string> _ui;
    readonly Win32Registry _reg = new();
    readonly StepExecutor _exec;
    string? _src;

    public StepRunner(Action<string> ui)
    {
        _ui = ui;
        var fs = new RealFileSystem();
        _exec = new StepExecutor(fs, _reg, Log);
    }

    public static bool Elevated => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    static string FallbackDir(ZwcadInstall t) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ASH", t.Version.Length > 0 ? t.Version : "unknown");

    public void Log(string line)
    {
        try { File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + line + Environment.NewLine); } catch { /* log file is best effort */ }
        _ui(line);
    }

    public bool Install(IList<ZwcadInstall> targets)
    {
        Log($"=== 설치 시작 ({targets.Count}개, 관리자={Elevated}) ===");
        if (!Elevated) Log("관리자 권한이 아님: 모든 사용자(HKLM) 등록은 건너뜀, 현재 사용자(HKCU)만 등록");
        var src = Extract();
        bool ok = true;
        foreach (var t in targets)
        {
            Log($"-- ZWCAD {t.Version} {t.Language} {t.InstallDir}");
            if (!_exec.Install(t, src, Elevated, _reg, FallbackDir(t)))
            {
                ok = false;
                Log("이 ZWCAD는 설치 실패. ZWCAD가 실행 중이면 닫고 다시 시도하세요. 로그: " + LogPath);
            }
        }
        Log(ok ? "=== 설치 완료 ===" : "=== 설치 중 오류 있음 ===");
        return ok;
    }

    public bool Uninstall(IList<ZwcadInstall> targets)
    {
        Log($"=== 제거 시작 ({targets.Count}개) ===");
        var ok = _exec.Uninstall(targets, Elevated, _reg, FallbackDir);
        Log(ok ? "=== 제거 완료 ===" : "=== 제거 중 오류 있음 (ZWCAD가 실행 중이면 닫고 다시 시도) ===");
        return ok;
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
