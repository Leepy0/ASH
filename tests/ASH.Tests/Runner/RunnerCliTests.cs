#if NET10_0_OR_GREATER
using ASH.Runner;

namespace ASH.Tests.Runner;

public class RunnerCliTests
{
    [Fact]
    public void Defaults()
    {
        var a = RunnerArgs.Parse(new[] { @"C:\p", "--model", "m" }, out var err)!;
        Assert.Null(err);
        Assert.Equal((@"C:\p", "m", 2, "Student Version", 30, 1, true, false), (a.ProjectDir, a.Model, a.Parallel, a.DialogButton, a.TimeoutMin, a.InitialReps, a.Impact, a.Debug));
    }

    [Fact]
    public void All_Options_And_Order_Independence()
    {
        var a = RunnerArgs.Parse(new[] { "--model", "m", "--parallel", "4", "--timeout-min", "5", "--initial-reps", "2", "--no-impact", "--debug",
            "--am-home", @"C:\AutoMod\12_6", "--dialog-button", "", @"C:\p" }, out _)!;
        Assert.Equal((@"C:\p", 4, 5, 2, false, true, @"C:\AutoMod\12_6", null), (a.ProjectDir, a.Parallel, a.TimeoutMin, a.InitialReps, a.Impact, a.Debug, a.AmHome, a.DialogButton));
    }

    [Theory]
    [InlineData("--model", "m")]                                   // no project dir
    [InlineData(@"C:\p")]                                          // no model
    [InlineData(@"C:\p", "--model", "m", "--parallel", "0")]       // below minimum
    [InlineData(@"C:\p", "--model", "m", "--parallel", "x")]
    [InlineData(@"C:\p", "--model")]                               // missing value
    [InlineData(@"C:\p", "--model", "m", "--bogus")]
    [InlineData(@"C:\p", "extra", "--model", "m")]
    public void Bad_Args_Are_Rejected(params string[] args)
    {
        Assert.Null(RunnerArgs.Parse(args, out var err));
        Assert.False(string.IsNullOrEmpty(err));
    }

    [Fact]
    public async Task RunAsync_Returns_1_On_Bad_Args_Or_Missing_Exe()
    {
        var sw = new StringWriter();
        Assert.Equal(1, await Program.RunAsync(new[] { "--model" }, output: sw));
        var dir = Path.Combine(Path.GetTempPath(), "ash_cli_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Assert.Equal(1, await Program.RunAsync(new[] { dir, "--model", "m" }, output: sw));
        Assert.Contains("m.exe", sw.ToString());
        Directory.Delete(dir, true);
    }
}
#endif
