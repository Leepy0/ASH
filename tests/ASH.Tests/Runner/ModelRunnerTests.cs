using ASH.Core.Model;
using ASH.Core.Runner;
using ASH.Tests.Model;

namespace ASH.Tests.Runner;

public class ModelRunnerTests
{
    static RunRequest Req(string dir, string? exe = null, string args = "-w", int timeoutMs = 5000)
    {
        Directory.CreateDirectory(Path.Combine(dir, "m.arc"));
        return new RunRequest(dir, "m", LogicGeneratorTests.Sample(), new RunScenario(3, new Dictionary<string, int> { ["G1"] = 2 }),
            TimeSpan.FromMilliseconds(timeoutMs), null, exe, args);
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

    [Fact]
    public void RunId_Joins_Counts_And_Seed() =>
        Assert.Equal("n1-2_s3", ModelRunner.RunId(new RunScenario(3, new Dictionary<string, int> { ["G2"] = 2, ["G1"] = 1 })));

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
        Assert.False(o.Success); Assert.Equal("timeout", o.Error);
        Assert.True(o.Duration < TimeSpan.FromSeconds(5), o.Duration.ToString());
        Assert.True(File.Exists(Path.Combine(d, "m.arc", "params.txt"))); // params were swapped in before launch
        Rm(d);
    }

    [Fact]
    public void Exit_Zero_Without_Result_Is_Failure_And_Result_Is_Moved_When_Present()
    {
        var d = Tmp();
        var o = ModelRunner.Run(Req(d, "cmd.exe", "/c exit 0"));
        Assert.False(o.Success); Assert.Equal("result.tsv missing", o.Error);
        var o2 = ModelRunner.Run(Req(d, "cmd.exe", "/c echo kind> result.tsv& echo x> m.report"));
        Assert.True(o2.Success, o2.Error);
        Assert.True(File.Exists(Path.Combine(o2.RunDir, "result.tsv")) && File.Exists(Path.Combine(o2.RunDir, "m.report")));
        Assert.False(File.Exists(Path.Combine(d, "result.tsv")));
        Rm(d);
    }
}
