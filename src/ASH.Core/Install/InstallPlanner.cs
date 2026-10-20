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
    /// Enumerates language sub-keys for installs whose Language is empty, and checks that a known-language HKCU profile
    /// key exists before registering there. Callers (installer exe) must pass it, for uninstall too, otherwise
    /// empty-language installs get no registry steps (uninstall would leave keys behind).
    /// </param>
    /// <param name="installDirOverride">
    /// Replaces "&lt;InstallDir&gt;\ASH" as the DLL folder (and LOADER path) for every target; meant for a single-install
    /// re-plan when the ZWCAD folder is not writable (fallback %ProgramData%\ASH\&lt;ver&gt;).
    /// </param>
    /// <param name="supportedVersion">ZWCAD year the plugin is built for; a target with another known year gets a Warn and nothing else.</param>
    public static List<InstallStep> Plan(IEnumerable<ZwcadInstall> targets, string dllSourceDir, bool elevated, bool uninstall,
                                         IRegistryReader? reg = null, string? installDirOverride = null, string? supportedVersion = null)
    {
        var warns = new List<InstallStep>();
        var list = new List<ZwcadInstall>();
        foreach (var t in targets.Distinct())
        {
            if (supportedVersion != null && t.Version.Length > 0 && t.Version != supportedVersion)
                warns.Add(new InstallStep("Warn", t.InstallDir, $"미지원 버전 건너뜀: ZWCAD {t.Version} (ASH는 ZWCAD {supportedVersion}용) {t.InstallDir}"));
            else list.Add(t);
        }
        string AshDir(ZwcadInstall t) => installDirOverride?.TrimEnd('\\') ?? t.InstallDir.TrimEnd('\\') + @"\ASH";
        var dirs = list.Select(AshDir).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var regs = new List<InstallStep>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const string RunOnce = "ZWCAD를 한 번 실행한 뒤 다시 설치하세요.";

        // profile: "Software\ZWSoft\ZWCAD\<real version key>\<lang>"
        void Register(string hive, ZwcadInstall t, string profile)
        {
            var key = hive + "\\" + profile + @"\Applications\ASH";
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
                // a missing profile means ZWCAD never ran for this user; a key created now could be replaced on first start
                if (uninstall || reg == null || reg.KeyExists("HKCU", t.ProfileKeyPath)) Register("HKCU", t, t.ProfileKeyPath);
                else warns.Add(new InstallStep("Warn", t.InstallDir, $"ZWCAD {t.Version}: HKCU\\{t.ProfileKeyPath} 없음, HKCU 등록을 건너뜀. {RunOnce}"));
                if (elevated) Register("HKLM", t, t.ProfileKeyPath);
                continue;
            }
            // language unknown: HKCU profile keys are authoritative; HKLM (and WOW6432Node) only widen when elevated
            var below = t.ProfileKeyPath.Substring(@"Software\".Length); // "ZWSoft\ZWCAD\<ver>"
            var hkcu = reg?.SubKeys("HKCU", t.ProfileKeyPath) ?? new string[0];
            foreach (var l in hkcu) Register("HKCU", t, t.ProfileKeyPath + "\\" + l);
            if (elevated && reg != null)
                foreach (var l in hkcu.Concat(reg.SubKeys("HKLM", @"SOFTWARE\" + below))
                                       .Concat(reg.SubKeys("HKLM", @"SOFTWARE\WOW6432Node\" + below)).Distinct(StringComparer.OrdinalIgnoreCase))
                    Register("HKLM", t, t.ProfileKeyPath + "\\" + l);
            if (hkcu.Length == 0 && !uninstall)
                warns.Add(new InstallStep("Warn", t.InstallDir, $"ZWCAD {t.Version}: HKCU\\{t.ProfileKeyPath} 아래 프로필 없음, HKCU 자동 로드 등록을 건너뜀. {RunOnce}"));
        }

        if (uninstall)
            return regs.Concat(dirs.Select(d => new InstallStep("DeleteDir", d, null))).ToList();

        var dests = dirs.SelectMany(d => Files.Select(f => (dest: d + "\\" + f, src: dllSourceDir.TrimEnd('\\') + "\\" + f))).ToList();
        return dests.Select(x => new InstallStep("CopyFile", x.dest, x.src))
            .Concat(dests.Select(x => new InstallStep("Unblock", x.dest, null)))
            .Concat(regs).Concat(warns).ToList();
    }
}
