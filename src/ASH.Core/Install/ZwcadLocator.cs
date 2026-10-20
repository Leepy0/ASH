using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ASH.Core.Install;

public static class ZwcadLocator
{
    const string Exe = "ZWCAD.exe";
    const string UninstallRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<ZwcadInstall> Find(IRegistryReader reg, IFileSystem fs, string programFiles, string programFilesX86)
    {
        var result = new List<ZwcadInstall>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool IsZwcadDir(string dir) => dir.Length > 0 && fs.DirectoryExists(dir) && fs.FileExists(dir + "\\" + Exe);

        void Add(string version, string lang, string dir)
        {
            dir = dir.Trim().Trim('"').TrimEnd('\\');
            if (!IsZwcadDir(dir) || !seen.Add(dir)) return;
            var key = version.Length == 0 ? @"Software\ZWSoft\ZWCAD"
                    : lang.Length == 0 ? @"Software\ZWSoft\ZWCAD\" + version
                    : @"Software\ZWSoft\ZWCAD\" + version + "\\" + lang;
            result.Add(new ZwcadInstall(version, lang, dir, key));
        }

        // 1) ZWCAD's own keys: HKLM\...\ZWCAD\<ver>\<lang> string values pointing at the install dir
        foreach (var root in new[] { @"SOFTWARE\ZWSoft\ZWCAD", @"SOFTWARE\WOW6432Node\ZWSoft\ZWCAD" })
            foreach (var ver in reg.SubKeys("HKLM", root))
                foreach (var lang in reg.SubKeys("HKLM", root + "\\" + ver))
                    foreach (var (_, value) in reg.Values("HKLM", root + "\\" + ver + "\\" + lang))
                        if (value is string s && IsZwcadDir(s.Trim().TrimEnd('\\')))
                            Add(ver, lang, s);

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
                Add(Year(dir), "", dir);
            }

        // 3) Folder scan
        foreach (var pf in new[] { programFiles, programFilesX86 })
            foreach (var d in fs.EnumerateDirectories(pf + @"\ZWSOFT", "ZWCAD *"))
                Add(Year(d), "", d);

        return result;
    }

    static string Year(string dir)
    {
        var m = Regex.Match(dir.Trim().Trim('"').TrimEnd('\\'), @"(\d{4})$");
        return m.Success ? m.Groups[1].Value : "";
    }
}
