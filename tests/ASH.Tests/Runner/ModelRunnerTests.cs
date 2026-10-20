using System.Threading;
using ASH.Core.Model;
using ASH.Core.Runner;
using ASH.Tests.Model;

namespace ASH.Tests.Runner;

public class ModelRunnerTests
{
    static RunScenario Sc(int seed = 3, int g1 = 2) => new(seed, new Dictionary<string, int> { ["G1"] = g1 });

    static RunRequest Req(string dir, string? exe = null, string args = "-w", int timeoutMs = 5000)
    {
        Directory.CreateDirectory(Path.Combine(dir, "m.arc"));
        return new RunRequest(dir, "m", LogicGeneratorTests.Sample(), Sc(), TimeSpan.FromMilliseconds(timeoutMs), null) { ExeOverride = exe, ExeArgs = args };
    }
    // killed child may still hold the cwd for a moment
    static void Rm(string d)
    {
        for (var i = 0; i < 10; i++)
        {
            try { Directory.Delete(d, true); return; }
            catch (IOException) { Thread.Sleep(200); }
            catch (UnauthorizedAccessException) { Thread.Sleep(200); }
        }
    }
    static string Tmp() => Path.Combine(Path.GetTempPath(), "ash_mr_" + Guid.NewGuid().ToString("N"));
    static string Fx(string n) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "runner", n);
    // fake model: writes a valid result.tsv (fixture) and a report
    static string OkArgs => $"/c copy \"{Fx("result.tsv")}\" result.tsv >nul & echo x> m.report";

    [Fact]
    public void RunId_Covers_All_Groups_In_Row_Order_With_MinCount_Default_And_Dbg()
    {
        var two = LogicGeneratorTests.TwoGroups();
        Assert.Equal("n1-2_s3", ModelRunner.RunId(two, new RunScenario(3, new Dictionary<string, int> { ["G2"] = 2 }))); // G1 missing -> MinCount 1
        Assert.Equal("n3-1_s4_dbg", ModelRunner.RunId(two, new RunScenario(4, new Dictionary<string, int> { ["G1"] = 3, ["G2"] = 1 }, true)));
        Assert.Equal("G1=1;G2=2", ModelRunner.ScenarioText(two, new RunScenario(3, new Dictionary<string, int> { ["G2"] = 2 })));
    }

    [Fact]
    public void Missing_Exe_Records_Failure()
    {
        var d = Tmp(); var o = ModelRunner.Run(Req(d));
        Assert.False(o.Success); Assert.Contains("exe not found", o.Error);
        Rm(d);
    }

    [Fact]
    public void Runner_Kills_On_Timeout_And_Records_Failure()
    {
        var d = Tmp();
        var o = ModelRunner.Run(Req(d, "cmd.exe", "/c ping -n 30 127.0.0.1", 500));
        Assert.False(o.Success); Assert.Equal("timeout", o.Error); Assert.Null(o.ExitCode);
        Assert.True(o.Duration < TimeSpan.FromSeconds(5), o.Duration.ToString());
        Assert.True(File.Exists(Path.Combine(d, "m.arc", "params.txt"))); // params were swapped in before launch
        Rm(d);
    }

    [Fact]
    public void Exit_Zero_Without_Result_Is_Failure_And_Result_Is_Moved_When_Present()
    {
        var d = Tmp();
        var o = ModelRunner.Run(Req(d, "cmd.exe", "/c exit 0"));
        Assert.False(o.Success); Assert.Equal("result.tsv missing", o.Error); Assert.Equal(0, o.ExitCode);
        var o2 = ModelRunner.Run(Req(d, "cmd.exe", OkArgs));
        Assert.True(o2.Success, o2.Error);
        Assert.Equal(Path.Combine(o2.RunDir, "result.tsv"), o2.ResultTsvPath); Assert.Equal(Path.Combine(o2.RunDir, "m.report"), o2.ReportPath);
        Assert.True(File.Exists(o2.ResultTsvPath) && File.Exists(o2.ReportPath));
        Assert.False(File.Exists(Path.Combine(d, "result.tsv")));
        Rm(d);
    }

    [Fact]
    public void Missing_Report_Or_Invalid_Result_Is_Failure()
    {
        var d = Tmp();
        var o = ModelRunner.Run(Req(d, "cmd.exe", $"/c copy \"{Fx("result.tsv")}\" result.tsv >nul"));
        Assert.False(o.Success); Assert.Equal("m.report missing", o.Error);
        var o2 = ModelRunner.Run(Req(d, "cmd.exe", "/c echo kind> result.tsv& echo x> m.report")); // no clock row
        Assert.False(o2.Success); Assert.Contains("result.tsv:", o2.Error);
        Rm(d);
    }

    [Fact]
    public void Rerun_Does_Not_Inherit_Stale_Files()
    {
        var d = Tmp();
        var req = Req(d, "cmd.exe", OkArgs);
        var o = ModelRunner.Run(req);
        Assert.True(o.Success, o.Error);
        File.WriteAllText(Path.Combine(o.RunDir, "m.message"), "old");
        File.WriteAllText(Path.Combine(d, "m.report"), "stale"); // left in the work dir by an earlier crashed run
        var o2 = ModelRunner.Run(req.WithExe("cmd.exe", "/c exit 0"));
        Assert.False(o2.Success); // stale result.tsv/report from run 1 must not make a dead run pass
        Assert.False(File.Exists(Path.Combine(o2.RunDir, "m.message")));
        Assert.False(File.Exists(Path.Combine(d, "m.report")));
        Rm(d);
    }

    [Fact]
    public void PrepareWorkSlot_Copies_Exe_Arc_Dir()
    {
        var d = Tmp(); Directory.CreateDirectory(Path.Combine(d, "m.arc")); Directory.CreateDirectory(Path.Combine(d, "m.dir", "sub"));
        File.WriteAllText(Path.Combine(d, "m.exe"), "exe"); File.WriteAllText(Path.Combine(d, "m.arc", "a.txt"), "a");
        File.WriteAllText(Path.Combine(d, "m.dir", "sub", "b.bin"), "b");
        var w = ModelRunner.PrepareWorkSlot(d, "m", 2);
        Assert.Equal(Path.Combine(d, "work", "w2"), w);
        Assert.Equal("exe", File.ReadAllText(Path.Combine(w, "m.exe")));
        Assert.Equal("a", File.ReadAllText(Path.Combine(w, "m.arc", "a.txt")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(w, "m.dir", "sub", "b.bin")));
        File.WriteAllText(Path.Combine(d, "m.arc", "a.txt"), "a2"); File.SetLastWriteTimeUtc(Path.Combine(d, "m.arc", "a.txt"), DateTime.UtcNow.AddMinutes(1));
        ModelRunner.PrepareWorkSlot(d, "m", 2);
        Assert.Equal("a2", File.ReadAllText(Path.Combine(w, "m.arc", "a.txt"))); // older copy refreshed
        Rm(d);
    }

    [Fact]
    public void Run_Waits_For_A_Busy_WorkDir_Then_Fails_After_The_Limit()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), ModelRunner.MutexWait);
        var d = Tmp(); Directory.CreateDirectory(Path.Combine(d, "work"));
        var req = Req(d, "cmd.exe", "/c exit 0");
        var name = ModelRunner.MutexName(d);
        using var held = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var holder = new Thread(() => { using var m = new Mutex(false, name); m.WaitOne(); held.Set(); release.Wait(); m.ReleaseMutex(); });
        holder.Start(); held.Wait();
        var keep = ModelRunner.MutexWait;
        try
        {
            ModelRunner.MutexWait = TimeSpan.FromMilliseconds(300);
            var o = ModelRunner.Run(req);
            Assert.False(o.Success); Assert.StartsWith("work dir busy", o.Error);
            // a holder that lets go within the wait no longer fails the run
            ModelRunner.MutexWait = TimeSpan.FromSeconds(30);
            System.Threading.Tasks.Task.Delay(1500).ContinueWith(_ => release.Set());
            Assert.DoesNotContain("busy", ModelRunner.Run(req).Error ?? "");
        }
        finally { ModelRunner.MutexWait = keep; release.Set(); holder.Join(); Rm(d); }
    }
}

static class RunRequestExt
{
    public static RunRequest WithExe(this RunRequest r, string exe, string args) => r with { ExeOverride = exe, ExeArgs = args };
}
