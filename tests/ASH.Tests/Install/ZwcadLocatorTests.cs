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

    static ZwcadInstall One(FakeRegistry reg, string dir) =>
        Assert.Single(ZwcadLocator.Find(reg, new FakeFs(new[] { dir }, new[] { dir + @"\ZWCAD.exe" }), Pf, Pf86));

    [Fact] public void Locator_Takes_Year_From_Middle_Of_Name()
    {
        const string d = @"C:\Program Files\ZWSOFT\ZWCAD 2026 Pro";
        Assert.Equal("2026", One(new FakeRegistry(), d).Version);
    }

    [Fact] public void Locator_Keeps_Two_Languages_For_Same_Dir()
    {
        var reg = new FakeRegistry();
        reg.Add(@"HKLM\SOFTWARE\ZWSoft\ZWCAD\2026\ko_KR", "InstallPath", D26);
        reg.Add(@"HKLM\SOFTWARE\ZWSoft\ZWCAD\2026\en_US", "InstallPath", D26.ToUpperInvariant());
        var fs = new FakeFs(new[] { D26 }, new[] { D26 + @"\ZWCAD.exe" });
        var r = ZwcadLocator.Find(reg, fs, Pf, Pf86);
        Assert.Equal(new[] { "en_US", "ko_KR" }, r.Select(i => i.Language).OrderBy(x => x).ToArray());
    }

    [Fact] public void Locator_Accepts_Quoted_Exe_Path_Value_And_Wow6432Key()
    {
        var reg = new FakeRegistry();
        reg.Add(@"HKLM\SOFTWARE\WOW6432Node\ZWSoft\ZWCAD\2026\ko_KR", "Exe", "\"" + D26 + @"\ZWCAD.exe" + "\"");
        var i = One(reg, D26);
        Assert.Equal(D26, i.InstallDir);
        Assert.Equal("ko_KR", i.Language);
    }

    [Fact] public void Locator_Handles_Quoted_UninstallString_And_Trailing_Slash_InstallLocation()
    {
        var reg2 = new FakeRegistry();
        reg2.Add(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\B", "DisplayName", "ZWCAD 2026");
        reg2.Add(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\B", "InstallLocation", D26 + @"\");
        Assert.Equal(D26, One(reg2, D26).InstallDir);

        var reg3 = new FakeRegistry();
        reg3.Add(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\C", "DisplayName", "ZWCAD 2026");
        reg3.Add(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\C", "UninstallString", "\"" + D26 + @"\uninst.exe" + "\" /x");
        Assert.Equal(D26, One(reg3, D26).InstallDir);
    }

    [Fact] public void Locator_Dedupes_Case_Insensitively_Across_Registry_And_Folder()
    {
        var reg = new FakeRegistry();
        reg.Add(@"HKLM\SOFTWARE\ZWSoft\ZWCAD\2026\ko_KR", "InstallPath", D26.ToLowerInvariant());
        var fs = new FakeFs(new[] { D26, D26.ToLowerInvariant() }, new[] { D26 + @"\ZWCAD.exe" });
        Assert.Single(ZwcadLocator.Find(reg, fs, Pf, Pf86));
    }

    const string Mfg26 = @"C:\Program Files\ZWSOFT\ZWCAD MFG 2026";

    [Fact] public void Locator_Finds_Mfg_From_Its_ZWCADM_Registry_Key()
    {
        var reg = new FakeRegistry();
        reg.Add(@"HKLM\SOFTWARE\ZWSOFT\ZWCADM\2026\ko-KR", "InstallPath", Mfg26);
        var i = One(reg, Mfg26);
        Assert.True(i.IsMfg);
        Assert.Equal("ZWCAD MFG", i.Product);
        Assert.Equal("2026", i.Version);
        Assert.Equal("ko-KR", i.Language);
        Assert.Equal(@"Software\ZWSoft\ZWCADM\2026\ko-KR", i.ProfileKeyPath);
    }

    [Fact] public void Locator_Accepts_Zwcadm_Exe_For_Mfg()
    {
        var reg = new FakeRegistry();
        reg.Add(@"HKLM\SOFTWARE\ZWSOFT\ZWCADM\2026\ko-KR", "Exe", Mfg26 + @"\ZWCADM.exe");
        var fs = new FakeFs(new[] { Mfg26 }, new[] { Mfg26 + @"\ZWCADM.exe" });
        var i = Assert.Single(ZwcadLocator.Find(reg, fs, Pf, Pf86));
        Assert.Equal(Mfg26, i.InstallDir);
        Assert.True(i.IsMfg);
    }

    [Fact] public void Locator_Folder_And_Uninstall_Hits_Say_Mfg_By_Name()
    {
        var byFolder = One(new FakeRegistry(), Mfg26);
        Assert.True(byFolder.IsMfg);
        Assert.Equal(@"Software\ZWSoft\ZWCADM\2026", byFolder.ProfileKeyPath);

        var reg = new FakeRegistry();
        reg.Add(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\M", "DisplayName", "ZWCAD MFG 2026");
        reg.Add(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\M", "InstallLocation", Mfg26);
        Assert.True(One(reg, Mfg26).IsMfg);
        Assert.False(One(new FakeRegistry(), D26).IsMfg); // plain ZWCAD stays ZWCAD
    }

    [Fact] public void Locator_Keeps_Zwcad_And_Mfg_Side_By_Side()
    {
        var reg = new FakeRegistry();
        reg.Add(@"HKLM\SOFTWARE\ZWSoft\ZWCAD\2026\ko-KR", "InstallPath", D26);
        reg.Add(@"HKLM\SOFTWARE\ZWSOFT\ZWCADM\2026\ko-KR", "InstallPath", Mfg26);
        var fs = new FakeFs(new[] { D26, Mfg26 }, new[] { D26 + @"\ZWCAD.exe", Mfg26 + @"\ZWCAD.exe" });
        var r = ZwcadLocator.Find(reg, fs, Pf, Pf86);
        Assert.Equal(2, r.Count);
        Assert.Single(r, x => x.IsMfg && x.InstallDir == Mfg26);
        Assert.Single(r, x => !x.IsMfg && x.InstallDir == D26);
    }
}
