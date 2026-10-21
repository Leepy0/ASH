using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ASH.Core.Install;

public static class ZwcadLocator
{
    /// <summary>ZWCAD MFG is ZWCADM in the registry; its exe name is not confirmed, so ZWCAD.exe and ZWCADM.exe are both accepted.</summary>
    static readonly string[] Exes = { "ZWCAD.exe", "ZWCADM.exe" };
    public const string Root = @"Software\ZWSoft\ZWCAD";
    public const string MfgRoot = @"Software\ZWSoft\ZWCADM";

    /// <summary>Profile root for a product: MFG or plain ZWCAD.</summary>
    public static string ProfileRoot(bool mfg) => mfg ? MfgRoot : Root;

    /// <summary>"MFG"/"Mechanical" in a name or folder, or ZWCADM.exe in the folder.</summary>
    public static bool LooksMfg(string text) =>
        text.IndexOf("MFG", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Mechanical", StringComparison.OrdinalIgnoreCase) >= 0
        || text.IndexOf("ZWCADM", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>The ZWCAD exe in dir (null when none).</summary>
    public static string? FindExe(IFileSystem fs, string dir) => Exes.Select(e => dir + "\\" + e).FirstOrDefault(fs.FileExists);
    const string UninstallRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<ZwcadInstall> Find(IRegistryReader reg, IFileSystem fs, string programFiles, string programFilesX86)
    {
        var result = new List<ZwcadInstall>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string version, string lang, string dir, string? profileKey = null, bool mfg = false)
        {
            dir = dir.Trim().Trim('"');
            foreach (var e in Exes)
                if (dir.EndsWith("\\" + e, StringComparison.OrdinalIgnoreCase)) { dir = dir.Substring(0, dir.Length - e.Length - 1); break; }
            dir = dir.TrimEnd('\\');
            if (dir.Length == 0 || !fs.DirectoryExists(dir)) return;
            var exe = FindExe(fs, dir);
            if (exe == null) return;
            // ZWCADM.exe alone means MFG; a hit from a name/folder that says MFG as well
            mfg |= profileKey == null && exe.EndsWith("ZWCADM.exe", StringComparison.OrdinalIgnoreCase) && !fs.FileExists(dir + "\\ZWCAD.exe");
            var root = ProfileRoot(mfg);
            // a language-less hit (folder/uninstall) is redundant once the dir is known with any language
            if (lang.Length == 0 ? result.Any(r => string.Equals(r.InstallDir, dir, StringComparison.OrdinalIgnoreCase))
                                 : !seen.Add(dir + "|" + lang)) return;
            // profileKey keeps the real key name (e.g. "2026.0"); Version is only the year for display/matching
            var key = profileKey ?? (version.Length == 0 ? root
                    : lang.Length == 0 ? root + "\\" + version
                    : root + "\\" + version + "\\" + lang);
            result.Add(new ZwcadInstall(version, lang, dir, key));
        }

        // 1) ZWCAD's own keys: HKLM\...\ZWCAD\<ver>\<lang> (MFG: ...\ZWCADM\<ver>\<lang>) string values pointing at the install dir (or the exe)
        foreach (var mfg in new[] { false, true })
            foreach (var root in new[] { @"SOFTWARE\ZWSoft\" + (mfg ? "ZWCADM" : "ZWCAD"), @"SOFTWARE\WOW6432Node\ZWSoft\" + (mfg ? "ZWCADM" : "ZWCAD") })
                foreach (var ver in reg.SubKeys("HKLM", root))
                    foreach (var lang in reg.SubKeys("HKLM", root + "\\" + ver))
                        foreach (var (_, value) in reg.Values("HKLM", root + "\\" + ver + "\\" + lang))
                            if (value is string s) Add(Year(ver), lang, s, ProfileRoot(mfg) + "\\" + ver + "\\" + lang, mfg);

        // 2) Uninstall entries
        foreach (var root in new[] { UninstallRoot, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
            foreach (var k in reg.SubKeys("HKLM", root))
            {
                var p = root + "\\" + k;
                var name = reg.GetString("HKLM", p, "DisplayName");
                if (name == null || name.IndexOf("ZWCAD", StringComparison.OrdinalIgnoreCase) < 0) continue;
                string dir = reg.GetString("HKLM", p, "InstallLocation") ?? "";
                if (string.IsNullOrWhiteSpace(dir))
                {
                    var u = (reg.GetString("HKLM", p, "UninstallString") ?? "").Trim();
                    if (u.Length == 0) continue;
                    // shortcut: handles quoted or "...\x.exe args" forms only; upgrade if MSI "MsiExec /X{guid}" entries matter
                    var m = Regex.Match(u, "^\"([^\"]+)\"|^(.+?\\.exe)", RegexOptions.IgnoreCase);
                    var exe = m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : u;
                    dir = exe.Substring(0, Math.Max(0, exe.LastIndexOf('\\')));
                }
                var y = Year(name);
                Add(y.Length > 0 ? y : Year(LastSegment(dir)), "", dir, null, LooksMfg(name));
            }

        // 3) Folder scan
        foreach (var pf in new[] { programFiles, programFilesX86 })
            foreach (var d in fs.EnumerateDirectories(pf + @"\ZWSOFT", "ZWCAD *"))
                Add(Year(LastSegment(d)), "", d, null, LooksMfg(LastSegment(d)));

        return result;
    }

    static string LastSegment(string dir)
    {
        dir = dir.Trim().Trim('"').TrimEnd('\\');
        return dir.Substring(dir.LastIndexOf('\\') + 1);
    }

    // first 20xx year anywhere in the text ("ZWCAD 2026 Pro" -> "2026")
    static string Year(string text)
    {
        var m = Regex.Match(text, @"(?<!\d)(20\d{2})(?!\d)");
        return m.Success ? m.Groups[1].Value : "";
    }
}
