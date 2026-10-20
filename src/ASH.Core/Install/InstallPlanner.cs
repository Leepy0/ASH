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

    /// <param name="reg">Used only to enumerate language sub-keys for installs whose Language is empty.</param>
    public static List<InstallStep> Plan(IEnumerable<ZwcadInstall> targets, string dllSourceDir, bool elevated, bool uninstall, IRegistryReader? reg = null)
    {
        var list = targets.Distinct().ToList();
        var dirs = list.Select(t => t.InstallDir.TrimEnd('\\')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var hives = elevated ? new[] { "HKCU", "HKLM" } : new[] { "HKCU" };
        var regs = new List<InstallStep>();
        var warns = new List<InstallStep>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in list)
        {
            var loader = t.InstallDir.TrimEnd('\\') + @"\ASH\ASH.Plugin.dll";
            var any = false;
            foreach (var hive in hives)
            {
                var langs = t.Language.Length > 0 ? new[] { t.Language }
                          : reg?.SubKeys(hive, @"Software\ZWSoft\ZWCAD\" + t.Version) ?? new string[0];
                foreach (var lang in langs)
                {
                    any = true;
                    var key = hive + @"\Software\ZWSoft\ZWCAD\" + t.Version + "\\" + lang + @"\Applications\ASH";
                    if (!seenKeys.Add(key)) continue;
                    if (uninstall) regs.Add(new InstallStep("DeleteKey", key, null));
                    else
                    {
                        regs.Add(new InstallStep("RegString", key + @"\LOADER", loader));
                        regs.Add(new InstallStep("RegDword", key + @"\LOADCTRLS", "2"));
                        regs.Add(new InstallStep("RegDword", key + @"\MANAGED", "1"));
                        regs.Add(new InstallStep("RegString", key + @"\DESCRIPTION", Description));
                    }
                }
            }
            if (!any && !uninstall)
                warns.Add(new InstallStep("Warn", t.InstallDir, $"ZWCAD {t.Version}: no profile under HKCU\\Software\\ZWSoft\\ZWCAD\\{t.Version}; run ZWCAD once, then reinstall to register autoload."));
        }

        if (uninstall)
            return regs.Concat(dirs.Select(d => new InstallStep("DeleteDir", d + @"\ASH", null))).ToList();

        var dests = dirs.SelectMany(d => Files.Select(f => (dest: d + @"\ASH\" + f, src: dllSourceDir.TrimEnd('\\') + "\\" + f))).ToList();
        return dests.Select(x => new InstallStep("CopyFile", x.dest, x.src))
            .Concat(dests.Select(x => new InstallStep("Unblock", x.dest, null)))
            .Concat(regs).Concat(warns).ToList();
    }
}
