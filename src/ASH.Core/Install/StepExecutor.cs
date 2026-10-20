using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ASH.Core.Install;

/// <summary>File side effects of an install plan. Copy creates the destination folder; DeleteDir ignores a missing folder.</summary>
public interface IFileOps
{
    void Copy(string source, string dest);
    void Unblock(string path);
    void DeleteDir(string path);
}

/// <summary>Runs InstallPlanner steps in order. Stops at the first failure without rollback and reports it.</summary>
public sealed class StepExecutor
{
    readonly IFileOps _files;
    readonly IRegistryWriter _reg;
    readonly Action<string> _log;

    public StepExecutor(IFileOps files, IRegistryWriter reg, Action<string> log)
    {
        _files = files; _reg = reg; _log = log;
    }

    /// <returns>null when every step succeeded, else the failed step and its exception.</returns>
    public (InstallStep Step, Exception Error)? Run(IEnumerable<InstallStep> steps)
    {
        foreach (var s in steps)
        {
            try
            {
                switch (s.Kind)
                {
                    case "CopyFile": _files.Copy(s.Value!, s.Target); _log("복사: " + s.Target); break;
                    case "Unblock": _files.Unblock(s.Target); break;
                    case "RegString": { var (h, p, n) = Split(s.Target); _reg.SetString(h, p, n, s.Value ?? ""); _log("등록: " + s.Target + " = " + s.Value); break; }
                    case "RegDword": { var (h, p, n) = Split(s.Target); _reg.SetDword(h, p, n, int.Parse(s.Value!, CultureInfo.InvariantCulture)); _log("등록: " + s.Target + " = " + s.Value); break; }
                    case "DeleteKey": { var i = s.Target.IndexOf('\\'); _reg.DeleteKey(s.Target.Substring(0, i), s.Target.Substring(i + 1)); _log("키 삭제: " + s.Target); break; }
                    case "DeleteDir": _files.DeleteDir(s.Target); _log("폴더 삭제: " + s.Target); break;
                    case "Warn": _log("경고: " + s.Value); break;
                    default: throw new InvalidOperationException("알 수 없는 단계: " + s.Kind);
                }
            }
            catch (Exception e)
            {
                _log($"실패: {s.Kind} {s.Target} — {e.Message}");
                return (s, e);
            }
        }
        return null;
    }

    /// <summary>
    /// Installs one ZWCAD. When copying into "&lt;ZWCAD&gt;\ASH" is denied, re-plans with <paramref name="fallbackDir"/>
    /// (e.g. %ProgramData%\ASH\&lt;ver&gt;) so the DLLs and LOADER point there instead.
    /// </summary>
    public bool Install(ZwcadInstall t, string dllSourceDir, bool elevated, IRegistryReader reg, string fallbackDir)
    {
        var failed = Run(InstallPlanner.Plan(new[] { t }, dllSourceDir, elevated, false, reg));
        if (failed == null) return true;
        if (failed.Value.Step.Kind != "CopyFile" || failed.Value.Error is not UnauthorizedAccessException) return false;
        _log("쓰기 실패 → " + fallbackDir + " 사용");
        return Run(InstallPlanner.Plan(new[] { t }, dllSourceDir, elevated, false, reg, fallbackDir)) == null;
    }

    /// <summary>Removes keys, "&lt;ZWCAD&gt;\ASH" and the fallback folders of every target.</summary>
    public bool Uninstall(IList<ZwcadInstall> targets, bool elevated, IRegistryReader reg, Func<ZwcadInstall, string> fallbackDir)
    {
        var steps = InstallPlanner.Plan(targets, "", elevated, true, reg)
            .Concat(targets.Select(fallbackDir).Distinct(StringComparer.OrdinalIgnoreCase).Select(d => new InstallStep("DeleteDir", d, null)));
        return Run(steps) == null;
    }

    // "HKCU\Software\...\ASH\LOADER" -> ("HKCU", "Software\...\ASH", "LOADER")
    static (string Hive, string Path, string Name) Split(string target)
    {
        int first = target.IndexOf('\\'), last = target.LastIndexOf('\\');
        return (target.Substring(0, first), target.Substring(first + 1, last - first - 1), target.Substring(last + 1));
    }
}
