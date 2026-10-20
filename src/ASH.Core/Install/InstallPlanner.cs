using System;
using System.Collections.Generic;
using System.Linq;

namespace ASH.Core.Install;

/// <param name="Kind">CopyFile (Value=source) | Unblock | RegString | RegDword (Value=decimal) | DeleteKey | DeleteDir | Warn (Value=message)</param>
public sealed record InstallStep(string Kind, string Target, string? Value);

public static class InstallPlanner
{
    static readonly string[] Files = { "ASH.Plugin.dll", "ASH.Core.dll" };
    const string Description = "ASH - AGV Simul Helper";

    /// <param name="reg">
    /// Enumerates language sub-keys for installs whose Language is empty. Callers (installer exe) must pass it,
    /// for uninstall too, otherwise empty-language installs get no registry steps (uninstall would leave keys behind).
    /// </param>
    /// <param name="installDirOverride">
    /// Replaces "&lt;InstallDir&gt;\ASH" as the DLL folder (and LOADER path) for every target; meant for a single-install
    /// re-plan when the ZWCAD folder is not writable (fallback %ProgramData%\ASH\&lt;ver&gt;).
    /// </param>
    public static List<InstallStep> Plan(IEnumerable<ZwcadInstall> targets, string dllSourceDir, bool elevated, bool uninstall,
                                         IRegistryReader? reg = null, string? installDirOverride = null)
    {
        var list = targets.Distinct().ToList();
        string AshDir(ZwcadInstall t) => installDirOverride?.TrimEnd('\\') ?? t.InstallDir.TrimEnd('\\') + @"\ASH";
        var dirs = list.Select(AshDir).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var regs = new List<InstallStep>();
        var warns = new List<InstallStep>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Register(string hive, ZwcadInstall t, string lang)
        {
            var key = hive + @"\Software\ZWSoft\ZWCAD\" + t.Version + "\\" + lang + @"\Applications\ASH";
            if (!seenKeys.Add(key)) return;
            if (uninstall) { regs.Add(new InstallStep("DeleteKey", key, null)); return; }
            var loader = AshDir(t) + @"\ASH.Plugin.dll";
            regs.Add(new InstallStep("RegString", key + @"\LOADER", loader));
            regs.Add(new InstallStep("RegDword", key + @"\LOADCTRLS", "2"));
            regs.Add(new InstallStep("RegDword", key + @"\MANAGED", "1"));
            regs.Add(new InstallStep("RegString", key + @"\DESCRIPTION", Description));
        }

        foreach (var t in list)
        {
            if (t.Version.Length == 0)
            {
                if (!uninstall) warns.Add(new InstallStep("Warn", t.InstallDir, $"버전을 알 수 없어 자동 로드 등록을 건너뜀: {t.InstallDir}"));
                continue;
            }
            if (t.Language.Length > 0)
            {
                Register("HKCU", t, t.Language);
                if (elevated) Register("HKLM", t, t.Language);
                continue;
            }
            // language unknown: HKCU profile keys are authoritative; HKLM (and WOW6432Node) only widen when elevated
            var profile = @"ZWSoft\ZWCAD\" + t.Version;
            var hkcu = reg?.SubKeys("HKCU", @"Software\" + profile) ?? new string[0];
            foreach (var l in hkcu) Register("HKCU", t, l);
            if (elevated && reg != null)
                foreach (var l in hkcu.Concat(reg.SubKeys("HKLM", @"SOFTWARE\" + profile))
                                       .Concat(reg.SubKeys("HKLM", @"SOFTWARE\WOW6432Node\" + profile)).Distinct(StringComparer.OrdinalIgnoreCase))
                    Register("HKLM", t, l);
            if (hkcu.Length == 0 && !uninstall)
                warns.Add(new InstallStep("Warn", t.InstallDir, $"ZWCAD {t.Version}: no profile under HKCU\\Software\\ZWSoft\\ZWCAD\\{t.Version}; run ZWCAD once, then reinstall to register autoload."));
        }

        if (uninstall)
            return regs.Concat(dirs.Select(d => new InstallStep("DeleteDir", d, null))).ToList();

        var dests = dirs.SelectMany(d => Files.Select(f => (dest: d + "\\" + f, src: dllSourceDir.TrimEnd('\\') + "\\" + f))).ToList();
        return dests.Select(x => new InstallStep("CopyFile", x.dest, x.src))
            .Concat(dests.Select(x => new InstallStep("Unblock", x.dest, null)))
            .Concat(regs).Concat(warns).ToList();
    }
}
