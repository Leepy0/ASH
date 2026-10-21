using System.Linq;
using ASH.Core.Install;
using Xunit;

namespace ASH.Tests.Install;

public class InstallPlannerTests
{
    static ZwcadInstall I => new("2026", "ko_KR", @"C:\ZWCAD2026", @"Software\ZWSoft\ZWCAD\2026\ko_KR");
    const string Loader = @"HKCU\Software\ZWSoft\ZWCAD\2026\ko_KR\Applications\ASH\LOADER";

    [Fact] public void Plan_Copies_Unblocks_And_Registers_Hkcu_And_Hklm_When_Elevated()
    {
        var s = InstallPlanner.Plan(new[] { I }, @"D:\src", elevated: true, uninstall: false);
        Assert.Contains(s, x => x.Kind == "CopyFile" && x.Target == @"C:\ZWCAD2026\ASH\ASH.Plugin.dll" && x.Value == @"D:\src\ASH.Plugin.dll");
        Assert.Contains(s, x => x.Kind == "CopyFile" && x.Target.EndsWith("ASH.Core.dll"));
        Assert.Contains(s, x => x.Kind == "Unblock" && x.Target.EndsWith("ASH.Plugin.dll"));
        Assert.Contains(s, x => x.Kind == "RegString" && x.Target == Loader && x.Value == @"C:\ZWCAD2026\ASH\ASH.Plugin.dll");
        Assert.Contains(s, x => x.Kind == "RegDword" && x.Target.EndsWith(@"\LOADCTRLS") && x.Value == "2");
        Assert.Contains(s, x => x.Kind == "RegDword" && x.Target.EndsWith(@"\MANAGED") && x.Value == "1");
        Assert.Contains(s, x => x.Target.StartsWith(@"HKLM\Software\ZWSoft\ZWCAD\2026\ko_KR\Applications\ASH"));
        Assert.True(s.FindLastIndex(x => x.Kind == "CopyFile") < s.FindIndex(x => x.Kind == "Unblock"));
        Assert.True(s.FindLastIndex(x => x.Kind == "Unblock") < s.FindIndex(x => x.Kind.StartsWith("Reg")));
    }

    [Fact] public void Plan_Copies_Plugin_Core_And_Their_Dependencies()
    {
        var s = InstallPlanner.Plan(new[] { I }, @"D:\src", elevated: true, uninstall: false);
        foreach (var f in new[] { "ASH.Plugin.dll", "ASH.Core.dll", "ClosedXML.dll", "DocumentFormat.OpenXml.dll", "System.Memory.dll" })
            Assert.Contains(s, x => x.Kind == "CopyFile" && x.Target == @"C:\ZWCAD2026\ASH\" + f && x.Value == @"D:\src\" + f);
        Assert.Equal(InstallPlanner.Files.Length, s.Count(x => x.Kind == "CopyFile"));
        Assert.Equal(InstallPlanner.Files.Length, s.Count(x => x.Kind == "Unblock"));
    }

    [Fact] public void Plan_Registers_Mfg_Under_Its_Own_ZWCADM_Key()
    {
        var mfg = new ZwcadInstall("2026", "ko-KR", @"C:\ZWCADM2026", @"Software\ZWSoft\ZWCADM\2026\ko-KR");
        var s = InstallPlanner.Plan(new[] { mfg }, @"D:\src", elevated: true, uninstall: false);
        Assert.Contains(s, x => x.Kind == "RegString" && x.Target == @"HKCU\Software\ZWSoft\ZWCADM\2026\ko-KR\Applications\ASH\LOADER" && x.Value == @"C:\ZWCADM2026\ASH\ASH.Plugin.dll");
        Assert.Contains(s, x => x.Kind == "RegDword" && x.Target == @"HKLM\Software\ZWSoft\ZWCADM\2026\ko-KR\Applications\ASH\LOADCTRLS" && x.Value == "2");
        Assert.DoesNotContain(s, x => x.Kind.StartsWith("Reg") && x.Target.Contains(@"\ZWCAD\"));
        var u = InstallPlanner.Plan(new[] { mfg }, @"D:\src", true, true);
        Assert.Contains(u, x => x.Kind == "DeleteKey" && x.Target == @"HKCU\Software\ZWSoft\ZWCADM\2026\ko-KR\Applications\ASH");
    }

    [Fact] public void Plan_Skips_Unsupported_Version()
    {
        var old = new ZwcadInstall("2024", "ko_KR", @"C:\ZWCAD2024", @"Software\ZWSoft\ZWCAD\2024\ko_KR");
        var s = InstallPlanner.Plan(new[] { I, old }, @"D:\src", true, false, supportedVersion: "2026");
        var w = Assert.Single(s, x => x.Kind == "Warn");
        Assert.Contains("2024", w.Value);
        Assert.DoesNotContain(s, x => x.Kind != "Warn" && x.Target.Contains("2024"));
        Assert.Contains(s, x => x.Kind == "RegString" && x.Target == Loader);
        Assert.Contains(s, x => x.Kind == "CopyFile" && x.Target.StartsWith(@"C:\ZWCAD2026\"));
    }

    [Fact] public void Planner_Uses_Original_ProfileKeyPath()
    {
        var reg = new FakeRegistry();
        reg.Add(@"HKLM\SOFTWARE\ZWSoft\ZWCAD\2026.0\ko_KR", "InstallPath", @"C:\ZWCAD2026");
        reg.Add(@"HKCU\Software\ZWSoft\ZWCAD\2026.0\ko_KR", "x", "y");
        var fs = new FakeFs(new[] { @"C:\ZWCAD2026" }, new[] { @"C:\ZWCAD2026\ZWCAD.exe" });
        var t = Assert.Single(ZwcadLocator.Find(reg, fs, @"C:\pf", @"C:\pf86"));
        Assert.Equal("2026", t.Version);
        Assert.Equal(@"Software\ZWSoft\ZWCAD\2026.0\ko_KR", t.ProfileKeyPath);
        var s = InstallPlanner.Plan(new[] { t }, @"D:\src", true, false, reg, supportedVersion: "2026");
        Assert.Contains(s, x => x.Kind == "RegString" && x.Target == @"HKCU\Software\ZWSoft\ZWCAD\2026.0\ko_KR\Applications\ASH\LOADER");
        Assert.Contains(s, x => x.Kind == "RegString" && x.Target == @"HKLM\Software\ZWSoft\ZWCAD\2026.0\ko_KR\Applications\ASH\LOADER");
        Assert.DoesNotContain(s, x => x.Target.Contains(@"ZWCAD\2026\"));
        Assert.DoesNotContain(s, x => x.Kind == "Warn");
    }

    [Fact] public void Planner_Skips_Hkcu_When_Profile_Missing()
    {
        var s = InstallPlanner.Plan(new[] { I }, @"D:\src", true, false, new FakeRegistry());
        Assert.DoesNotContain(s, x => x.Target.StartsWith("HKCU"));
        Assert.Contains(s, x => x.Kind == "RegString" && x.Target == @"HKLM\Software\ZWSoft\ZWCAD\2026\ko_KR\Applications\ASH\LOADER");
        Assert.Contains(s, x => x.Kind == "Warn" && x.Value!.Contains("ZWCAD를 한 번 실행한 뒤 다시 설치하세요"));
        Assert.Contains(s, x => x.Kind == "CopyFile");
    }

    [Fact] public void Planner_Skips_Hklm_When_Not_Elevated()
    {
        var s = InstallPlanner.Plan(new[] { I }, @"D:\src", false, false);
        Assert.DoesNotContain(s, x => x.Target.StartsWith("HKLM"));
    }

    [Fact] public void Planner_Reinstall_Overwrites_Same_Key()
    {
        var s = InstallPlanner.Plan(new[] { I, I }, @"D:\src", true, false);
        Assert.Equal(1, s.Count(x => x.Kind == "RegString" && x.Target == Loader));
    }

    [Fact] public void Uninstall_Deletes_Keys_And_Folder()
    {
        var s = InstallPlanner.Plan(new[] { I }, @"D:\src", true, true);
        Assert.Contains(s, x => x.Kind == "DeleteKey" && x.Target == @"HKCU\Software\ZWSoft\ZWCAD\2026\ko_KR\Applications\ASH");
        Assert.Contains(s, x => x.Kind == "DeleteDir" && x.Target == @"C:\ZWCAD2026\ASH");
        Assert.True(s.FindLastIndex(x => x.Kind == "DeleteKey") < s.FindIndex(x => x.Kind == "DeleteDir"));
    }

    [Fact] public void Empty_Language_Registers_Under_Existing_Hkcu_Subkeys_Or_Warns()
    {
        var noLang = new ZwcadInstall("2024", "", @"C:\ZWCAD2024", @"Software\ZWSoft\ZWCAD\2024");
        var reg = new FakeRegistry();
        reg.Add(@"HKCU\Software\ZWSoft\ZWCAD\2024\zh_CN", "x", "y");
        var s = InstallPlanner.Plan(new[] { noLang }, @"D:\src", false, false, reg);
        Assert.Contains(s, x => x.Kind == "RegString" && x.Target == @"HKCU\Software\ZWSoft\ZWCAD\2024\zh_CN\Applications\ASH\LOADER");
        Assert.DoesNotContain(s, x => x.Kind == "Warn");

        var w = InstallPlanner.Plan(new[] { noLang }, @"D:\src", false, false);
        Assert.Contains(w, x => x.Kind == "Warn" && !string.IsNullOrEmpty(x.Value));
        Assert.Contains(w, x => x.Kind == "CopyFile");
        Assert.DoesNotContain(w, x => x.Kind.StartsWith("Reg"));
    }

    [Fact] public void Planner_Warns_And_Skips_Registry_On_Empty_Version()
    {
        var noVer = new ZwcadInstall("", "", @"D:\CAD\ZWCAD", @"Software\ZWSoft\ZWCAD");
        var reg = new FakeRegistry();
        reg.Add(@"HKCU\Software\ZWSoft\ZWCAD\2024\zh_CN", "x", "y");
        var s = InstallPlanner.Plan(new[] { noVer }, @"D:\src", true, false, reg);
        Assert.Contains(s, x => x.Kind == "Warn" && x.Value!.Contains(@"D:\CAD\ZWCAD"));
        Assert.Contains(s, x => x.Kind == "CopyFile");
        Assert.DoesNotContain(s, x => x.Kind.StartsWith("Reg"));
    }

    [Fact] public void Planner_Registers_Each_Language_For_Same_Dir_But_Copies_Once()
    {
        var a = I; var b = I with { Language = "en_US", ProfileKeyPath = @"Software\ZWSoft\ZWCAD\2026\en_US" };
        var s = InstallPlanner.Plan(new[] { a, b }, @"D:\src", false, false);
        Assert.Equal(2, s.Count(x => x.Kind == "RegString" && x.Target.EndsWith(@"\LOADER")));
        Assert.Equal(1, s.Count(x => x.Kind == "CopyFile" && x.Target.EndsWith("ASH.Plugin.dll")));
    }

    [Fact] public void Uninstall_EmptyLanguage_Deletes_All_Language_Keys()
    {
        var noLang = new ZwcadInstall("2024", "", @"C:\ZWCAD2024", @"Software\ZWSoft\ZWCAD\2024");
        var reg = new FakeRegistry();
        reg.Add(@"HKCU\Software\ZWSoft\ZWCAD\2024\zh_CN", "x", "y");
        reg.Add(@"HKCU\Software\ZWSoft\ZWCAD\2024\en_US", "x", "y");
        var s = InstallPlanner.Plan(new[] { noLang }, @"D:\src", false, true, reg);
        Assert.Equal(2, s.Count(x => x.Kind == "DeleteKey"));
        Assert.Contains(s, x => x.Kind == "DeleteKey" && x.Target == @"HKCU\Software\ZWSoft\ZWCAD\2024\en_US\Applications\ASH");
    }

    [Fact] public void EmptyLanguage_Elevated_Uses_Hklm_And_Wow_But_Warns_When_Hkcu_Empty()
    {
        var noLang = new ZwcadInstall("2024", "", @"C:\ZWCAD2024", @"Software\ZWSoft\ZWCAD\2024");
        var reg = new FakeRegistry();
        reg.Add(@"HKLM\SOFTWARE\WOW6432Node\ZWSoft\ZWCAD\2024\zh_CN", "x", "y");
        var s = InstallPlanner.Plan(new[] { noLang }, @"D:\src", true, false, reg);
        Assert.Contains(s, x => x.Kind == "RegString" && x.Target == @"HKLM\Software\ZWSoft\ZWCAD\2024\zh_CN\Applications\ASH\LOADER");
        Assert.DoesNotContain(s, x => x.Kind == "RegString" && x.Target.StartsWith("HKCU"));
        Assert.Contains(s, x => x.Kind == "Warn");
    }
}
