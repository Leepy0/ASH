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
        Assert.True(r.Ok);
        Assert.Empty(r.Warnings);
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
        var r = new StepExecutor(ops, ops, log.Add).Install(I, @"D:\src", false, WithProfile(), @"C:\ProgramData\ASH\2026");
        Assert.True(r.Ok);
        Assert.Equal(@"C:\ProgramData\ASH\2026", r.FallbackDir);
        Assert.Contains(@"copy C:\ProgramData\ASH\2026\ASH.Plugin.dll", ops.Log);
        Assert.Contains($@"sz HKCU|{Key}|LOADER=C:\ProgramData\ASH\2026\ASH.Plugin.dll", ops.Log);
        Assert.DoesNotContain(ops.Log, l => l.Contains(@"=C:\ZWCAD2026"));
        Assert.Contains(log, l => l.Contains("쓰기 실패"));
    }

    [Fact] public void Run_Stops_At_First_Failure()
    {
        var ops = new FakeOps(); ops.Denied.Add(@"C:\");
        var r = new StepExecutor(ops, ops, _ => { }).Run(InstallPlanner.Plan(new[] { I }, @"D:\src", false, false));
        Assert.False(r.Ok);
        Assert.Equal("CopyFile", r.Failed!.Value.Step.Kind);
        Assert.Empty(ops.Log);
        var i = new StepExecutor(ops, ops, _ => { }).Install(I, @"D:\src", false, new FakeRegistry(), @"C:\ProgramData\ASH\2026");
        Assert.False(i.Ok);
        Assert.Equal(1, RunResult.ExitCode(new[] { new RunResult(), i }));
    }

    [Fact] public void Uninstall_Deletes_Keys_Ash_Dir_And_Fallback_Dir()
    {
        var ops = new FakeOps();
        Assert.True(new StepExecutor(ops, ops, _ => { }).Uninstall(new[] { I }, false, new FakeRegistry(), t => @"C:\ProgramData\ASH\" + t.Version).Ok);
        Assert.Equal(new[] { $"delkey HKCU|{Key}", @"rmdir C:\ZWCAD2026\ASH", @"rmdir C:\ProgramData\ASH\2026" }, ops.Log);
    }

    static FakeRegistry WithProfile()
    {
        var reg = new FakeRegistry();
        reg.Add(@"HKCU\Software\ZWSoft\ZWCAD\2026\ko_KR", "x", "y");
        return reg;
    }

    [Fact] public void Install_Copies_Before_Any_Registry_Step()
    {
        var ops = new FakeOps();
        Assert.True(new StepExecutor(ops, ops, _ => { }).Install(I, @"D:\src", true, WithProfile(), @"C:\ProgramData\ASH\2026").Ok);
        bool File(string l) => l.StartsWith("copy ") || l.StartsWith("unblock ");
        Assert.Equal(2 * InstallPlanner.Files.Length, ops.Log.Count(File)); // a copy and an unblock per file
        Assert.Equal(8, ops.Log.Count(l => l.StartsWith("sz ") || l.StartsWith("dw ")));
        Assert.True(ops.Log.FindIndex(l => !File(l)) > ops.Log.FindLastIndex(File), string.Join(" | ", ops.Log));
    }

    [Fact] public void Install_Unsupported_Version_Warns_And_Touches_Nothing()
    {
        var ops = new FakeOps();
        var r = new StepExecutor(ops, ops, _ => { }).Install(I with { Version = "2024" }, @"D:\src", true, WithProfile(), @"C:\ProgramData\ASH\2024", "2026");
        Assert.True(r.Ok);
        Assert.Empty(ops.Log);
        Assert.Contains("미지원", Assert.Single(r.Warnings));
        Assert.Equal(2, RunResult.ExitCode(new[] { r }));
    }

    [Fact] public void Run_Returns_Warnings()
    {
        var noLang = new ZwcadInstall("2024", "", @"C:\ZWCAD2024", @"Software\ZWSoft\ZWCAD\2024");
        var ops = new FakeOps();
        var r = new StepExecutor(ops, ops, _ => { }).Install(noLang, @"D:\src", false, new FakeRegistry(), @"C:\ProgramData\ASH\2024");
        Assert.True(r.Ok);
        Assert.Single(r.Warnings);
        Assert.Contains("2024", r.Warnings[0]);
        Assert.Equal(2, RunResult.ExitCode(new[] { new RunResult(), r }));
        Assert.Equal(0, RunResult.ExitCode(new[] { new RunResult() }));
    }

    [Fact] public void Uninstall_Continues_Past_DeleteDir_Failure()
    {
        var ops = new FakeOps(); ops.Denied.Add(@"C:\ZWCAD2026\ASH");
        var r = new StepExecutor(ops, ops, _ => { }).Uninstall(new[] { I }, false, new FakeRegistry(), t => @"C:\ProgramData\ASH\" + t.Version);
        Assert.False(r.Ok);
        Assert.Single(r.Failures);
        Assert.Contains(@"rmdir C:\ProgramData\ASH\2026", ops.Log);
    }
}
