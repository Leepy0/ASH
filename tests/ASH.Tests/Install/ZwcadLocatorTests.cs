using System.Linq;
using ASH.Core.Install;
using Xunit;

namespace ASH.Tests.Install;

public class ZwcadLocatorTests
{
    const string Pf = @"C:\Program Files";
    const string Pf86 = @"C:\Program Files (x86)";
    const string D26 = @"C:\Program Files\ZWSOFT\ZWCAD 2026";
    const string D24 = @"C:\Program Files\ZWSOFT\ZWCAD 2024";

    [Fact] public void Locator_Finds_Two_Versions_From_Registry_And_Folders()
    {
        var reg = new FakeRegistry();
        reg.Add(@"HKLM\SOFTWARE\ZWSoft\ZWCAD\2026\ko_KR", "InstallPath", D26);
        var fs = new FakeFs(dirs: new[] { D26, D24 }, files: new[] { D26 + @"\ZWCAD.exe", D24 + @"\ZWCAD.exe" });
        var r = ZwcadLocator.Find(reg, fs, Pf, Pf86);
        Assert.Equal(2, r.Count);
        Assert.Contains(r, i => i.Version == "2026" && i.Language == "ko_KR" && i.InstallDir.EndsWith("ZWCAD 2026")
                                && i.ProfileKeyPath == @"Software\ZWSoft\ZWCAD\2026\ko_KR");
        Assert.Contains(r, i => i.Version == "2024" && i.Language == "" && i.InstallDir.EndsWith("ZWCAD 2024"));
    }

    [Fact] public void Locator_Dedupes_Same_Dir_From_Registry_And_Uninstall()
    {
        var reg = new FakeRegistry();
        reg.Add(@"HKLM\SOFTWARE\ZWSoft\ZWCAD\2026\ko_KR", "InstallPath", D26);
        reg.Add(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ZW1", "DisplayName", "ZWCAD 2026");
        reg.Add(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ZW1", "InstallLocation", D26.ToUpperInvariant());
        reg.Add(@"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\ZW2", "DisplayName", "ZWCAD 2026");
        reg.Add(@"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\ZW2", "UninstallString", D26 + @"\uninst.exe /x");
        var fs = new FakeFs(new[] { D26 }, new[] { D26 + @"\ZWCAD.exe" });
        var r = ZwcadLocator.Find(reg, fs, Pf, Pf86);
        Assert.Equal("ko_KR", Assert.Single(r).Language);
    }

    [Fact] public void Locator_Ignores_Dir_Without_Exe()
    {
        var reg = new FakeRegistry();
        reg.Add(@"HKLM\SOFTWARE\ZWSoft\ZWCAD\2026\ko_KR", "InstallPath", D26);
        var fs = new FakeFs(new[] { D26, D24 }, new[] { D24 + @"\ZWCAD.exe" });
        var r = ZwcadLocator.Find(reg, fs, Pf, Pf86);
        Assert.Equal("2024", Assert.Single(r).Version);
    }
}
