#if NET10_0_OR_GREATER
using System.Diagnostics;
using ASH.Core.Export;
using ASH.Core.Geometry;
using ASH.Core.Model;
using ASH.Core.Runner;
using ASH.Runner;
using ASH.Tests.Layout;
using ASH.Tests.Model;

namespace ASH.Tests.Runner;

public class RunnerEndToEndTests
{
    static string? AmHome => Environment.GetEnvironmentVariable("ASH_AM_HOME");
    const string Model = "ashrun";

    [Fact]
    public void Failed_Runs_Land_In_Summary_As_Not_Ok()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash_e2e_f_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, Model + ".exe"), "x");     // never started: the runner is faked
        var a = new RunnerArgs(dir, Model, Parallel: 2);
        var input = LogicGeneratorTests.Sample();
        var mx = new ModelExecutor(a, input, r => new RunOutcome(ModelRunner.RunId(r.Input, r.Scenario), false, dir, TimeSpan.FromSeconds(1), "boom"));
        var kpis = mx.Run(1, new RunScenario(1, new Dictionary<string, int> { ["G1"] = 2 }));
        Assert.True(double.IsNaN(kpis.Single().Utilization));
        var row = Assert.Single(SummaryTsv.ReadAll(Path.Combine(dir, "runs", "summary.tsv")));
        Assert.False(row.Ok); Assert.Equal("boom", row.Reason); Assert.Equal("G1=2", row.Scenario);
        Assert.Equal(1, mx.Failures);
        Assert.True(Directory.Exists(Path.Combine(dir, "work", "w1")) && Directory.Exists(Path.Combine(dir, "work", "w2")));
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Ok_Exit_With_Unparsable_Result_Counts_As_Failure()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash_e2e_t_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var tsv = Path.Combine(dir, "result.tsv");
        File.WriteAllText(tsv, "kind\tname\tgroup\tvalue1\nmaterial\tM1\tG1\tnot-a-number\n"); // garbage
        var mx = new ModelExecutor(new RunnerArgs(dir, Model, Parallel: 1), LogicGeneratorTests.Sample(),
            r => new RunOutcome(ModelRunner.RunId(r.Input, r.Scenario), true, dir, TimeSpan.FromSeconds(1), null, 0, null, tsv, null));
        mx.Run(1, new RunScenario(1, new Dictionary<string, int> { ["G1"] = 1 }));
        Assert.Equal(1, mx.Failures);
        var row = Assert.Single(SummaryTsv.ReadAll(Path.Combine(dir, "runs", "summary.tsv")));
        Assert.False(row.Ok);
        Assert.DoesNotContain("NaN", row.Reason);
        Assert.False(string.IsNullOrEmpty(row.Reason));
        Directory.Delete(dir, true);
    }

    [SkippableFact] public Task AshRun_Searches_A_Real_Model_In_Two_Slots() => Search(1);

    // two replications: the replication batch runs in both slots at once
    [SkippableFact] public Task AshRun_Fans_Replications_Out_To_Both_Slots() => Search(2);

    static async Task Search(int replications)
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var l = L.Of(L.Line("p1", 0, 0, 10, 0), L.Line("p2", 10, 0, 10, 10), L.Line("p3", 10, 10, 0, 10), L.Line("p4", 0, 10, 0, 0));
        l.Cps.Add(L.Cp("S001", 5, 0, "standby", "G1"));
        l.Cps.Add(L.Cp("L001", 10, 5, "load"));
        l.Cps.Add(L.Cp("B001", 6, 10, "buffer"));
        l.Cps.Add(L.Cp("U001", 2, 10, "unload"));
        l.Blocks.Add(new ASH.Core.Layout.LayoutBlock { Name = "B_x1", Cap = 1, Rect = new Rect2(new Point2(8, -1), new Point2(11, 2)) });
        var input = LogicGeneratorTests.Sample();                                  // G1 Min 1 Max 3, SimLength 1 h
        input.Settings = input.Settings with { Replications = replications };
        var work = Path.Combine(Path.GetTempPath(), "ash_e2e_" + Guid.NewGuid().ToString("N"));
        var specs = new[] { new VehicleGroupSpec("G1", "Tug", 3, 1.0, 1, 1) };
        var res = ModelBuilder.Build(new ModelBuildRequest(work, Model, input, l, new ExportOptions(Groups: specs)), AmHome!, TimeSpan.FromMinutes(5));
        Assert.True(res.Build!.Success, work + "\n" + res.Build.MakeErrText + res.Build.Am2ErrText);

        var sw = Stopwatch.StartNew();
        var output = new StringWriter();
        var code = await Program.RunAsync(new[] { work, "--model", Model, "--parallel", "2", "--initial-reps", "1", "--timeout-min", "5" }, input, output);
        sw.Stop();
        Console.WriteLine(output.ToString());
        Console.WriteLine($"ash-run e2e elapsed {sw.Elapsed.TotalSeconds:0.0} s, exit {code}");

        Assert.True(code == 0 || code == 2, output.ToString());
        var rows = SummaryTsv.ReadAll(Path.Combine(work, "runs", "summary.tsv"));
        Assert.True(rows.Count >= 2 * replications, "summary rows " + rows.Count);
        if (replications > 1) Assert.True(Directory.Exists(Path.Combine(work, "work", "w1")) && Directory.Exists(Path.Combine(work, "work", "w2")));
        Assert.True(File.Exists(Path.Combine(work, "runs", "impact.tsv")));
        var decided = int.Parse(output.ToString().Split('\n').Single(x => x.StartsWith("G1 ")).Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
        Assert.InRange(decided, 1, 3);
        if (Environment.GetEnvironmentVariable("ASH_KEEP_TEMP") != "1") Directory.Delete(work, true);
    }
}
#endif
