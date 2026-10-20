using System.Collections.Generic;
using System.Linq;
using ASH.Core.Install;
using Xunit;

namespace ASH.Tests.Install;

public class StepExecutorTests
{
    static ZwcadInstall I => new("2026", "ko_KR", @"C:\ZWCAD2026", @"Software\ZWSoft\ZWCAD\2026\ko_KR");
    const string Key = @"Software\ZWSoft\ZWCAD\2026\ko_KR\Applications\ASH";

    [Fact] public void Plan_Uses_Override_Install_Dir()
    {
        var s = InstallPlanner.Plan(new[] { I }, @"D:\src", false, false, null, @"C:\ProgramData\ASH\2026\");
        Assert.Contains(s, x => x.Kind == "CopyFile" && x.Target == @"C:\ProgramData\ASH\2026\ASH.Plugin.dll");
        Assert.Contains(s, x => x.Kind == "RegString" && x.Target.EndsWith(@"\LOADER") && x.Value == @"C:\ProgramData\ASH\2026\ASH.Plugin.dll");
        Assert.DoesNotContain(s, x => x.Target.StartsWith(@"C:\ZWCAD2026"));
        var u = InstallPlanner.Plan(new[] { I }, "", false, true, null, @"C:\ProgramData\ASH\2026");
        Assert.Contains(u, x => x.Kind == "DeleteDir" && x.Target == @"C:\ProgramData\ASH\2026");
    }

    [Fact] public void Run_Splits_Registry_Targets_And_Copies()
    {
        var ops = new FakeOps(); var log = new List<string>();
        var r = new StepExecutor(ops, ops, log.Add).Run(InstallPlanner.Plan(new[] { I }, @"D:\src", true, false));
        Assert.Null(r);
        Assert.Contains(@"copy C:\ZWCAD2026\ASH\ASH.Plugin.dll", ops.Log);
        Assert.Contains(@"unblock C:\ZWCAD2026\ASH\ASH.Core.dll", ops.Log);
        Assert.Contains($@"sz HKCU|{Key}|LOADER=C:\ZWCAD2026\ASH\ASH.Plugin.dll", ops.Log);
        Assert.Contains($"dw HKLM|{Key}|LOADCTRLS=2", ops.Log);
        Assert.Contains($"dw HKCU|{Key}|MANAGED=1", ops.Log);
        Assert.NotEmpty(log);
    }

    [Fact] public void Install_Falls_Back_When_Zwcad_Dir_Denied()
    {
        var ops = new FakeOps(); ops.Denied.Add(@"C:\ZWCAD2026\");
        var log = new List<string>();
        Assert.True(new StepExecutor(ops, ops, log.Add).Install(I, @"D:\src", false, new FakeRegistry(), @"C:\ProgramData\ASH\2026"));
        Assert.Contains(@"copy C:\ProgramData\ASH\2026\ASH.Plugin.dll", ops.Log);
        Assert.Contains($@"sz HKCU|{Key}|LOADER=C:\ProgramData\ASH\2026\ASH.Plugin.dll", ops.Log);
        Assert.DoesNotContain(ops.Log, l => l.Contains(@"=C:\ZWCAD2026"));
        Assert.Contains(log, l => l.Contains("쓰기 실패"));
    }

    [Fact] public void Run_Stops_At_First_Failure()
    {
        var ops = new FakeOps(); ops.Denied.Add(@"C:\");
        var r = new StepExecutor(ops, ops, _ => { }).Run(InstallPlanner.Plan(new[] { I }, @"D:\src", false, false));
        Assert.NotNull(r);
        Assert.Equal("CopyFile", r!.Value.Step.Kind);
        Assert.Empty(ops.Log);
        Assert.False(new StepExecutor(ops, ops, _ => { }).Install(I, @"D:\src", false, new FakeRegistry(), @"C:\ProgramData\ASH\2026"));
    }

    [Fact] public void Uninstall_Deletes_Keys_Ash_Dir_And_Fallback_Dir()
    {
        var ops = new FakeOps();
        Assert.True(new StepExecutor(ops, ops, _ => { }).Uninstall(new[] { I }, false, new FakeRegistry(), t => @"C:\ProgramData\ASH\" + t.Version));
        Assert.Equal(new[] { $"delkey HKCU|{Key}", @"rmdir C:\ZWCAD2026\ASH", @"rmdir C:\ProgramData\ASH\2026" }, ops.Log);
    }
}
