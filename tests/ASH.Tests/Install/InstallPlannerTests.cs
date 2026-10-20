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
}
