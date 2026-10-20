#if NET10_0_OR_GREATER
using System.Diagnostics;
using ASH.Core.Export;
using ASH.Core.Geometry;
using ASH.Core.Input;
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

    static string NewDir(string tag)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash_e2e_" + tag + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>A project whose exe cannot start: every run fails fast without AutoMod.</summary>
    static string BrokenProject(ProjectInput input, string tag)
    {
        var dir = NewDir(tag);
        File.WriteAllText(Path.Combine(dir, Model + ".exe"), "x");
        Directory.CreateDirectory(Path.Combine(dir, Model + ".arc"));
        InputSignature.Write(Path.Combine(dir, Model + ".arc"), input);
        return dir;
    }

    [Fact]
    public void Failed_Runs_Land_In_Summary_As_Not_Ok_And_Report_Progress()
    {
        var dir = NewDir("f");
        File.WriteAllText(Path.Combine(dir, Model + ".exe"), "x");     // never started: the runner is faked
        var input = LogicGeneratorTests.Sample();
        var seen = new List<RunOutcome>();
        var summary = Path.Combine(dir, "elsewhere", "s.tsv");
        var mx = new ModelExecutor(new ExecutorOptions(dir, Model, 2, SummaryPath: summary, Progress: new Collect(seen)), input,
            r => new RunOutcome(ModelRunner.RunId(r.Input, r.Scenario), false, dir, TimeSpan.FromSeconds(1), "boom"));
        var kpis = mx.Run(1, new RunScenario(1, new Dictionary<string, int> { ["G1"] = 2 }));
        Assert.True(double.IsNaN(kpis.Single().Utilization));
        var row = Assert.Single(SummaryTsv.ReadAll(summary));
        Assert.False(row.Ok); Assert.Equal("boom", row.Reason); Assert.Equal("G1=2", row.Scenario);
        Assert.Equal(1, mx.Failures); Assert.True(mx.Failed("n2_s1"));
        Assert.Equal("n2_s1", Assert.Single(seen).RunId);
        Assert.True(Directory.Exists(Path.Combine(dir, "work", "w1")) && Directory.Exists(Path.Combine(dir, "work", "w2")));
        Directory.Delete(dir, true);
    }

    sealed class Collect(List<RunOutcome> into) : IProgress<RunOutcome> { public void Report(RunOutcome v) { lock (into) into.Add(v); } }

    [Fact]
    public void Ok_Exit_With_Unparsable_Result_Counts_As_Failure()
    {
        var dir = NewDir("t");
        var tsv = Path.Combine(dir, "result.tsv");
        File.WriteAllText(tsv, "kind\tname\tgroup\tvalue1\nmaterial\tM1\tG1\tnot-a-number\n"); // garbage
        var seen = new List<RunOutcome>();
        var mx = new ModelExecutor(new ExecutorOptions(dir, Model, 1, Progress: new Collect(seen)), LogicGeneratorTests.Sample(),
            r => new RunOutcome(ModelRunner.RunId(r.Input, r.Scenario), true, dir, TimeSpan.FromSeconds(1), null, 0, null, tsv, null));
        mx.Run(1, new RunScenario(1, new Dictionary<string, int> { ["G1"] = 1 }));
        Assert.Equal(1, mx.Failures);
        var row = Assert.Single(SummaryTsv.ReadAll(Path.Combine(dir, "runs", "summary.tsv")));
        Assert.False(row.Ok);
        Assert.DoesNotContain("NaN", row.Reason);
        Assert.False(string.IsNullOrEmpty(row.Reason));
        Assert.False(Assert.Single(seen).Success);   // progress sees the parse failure too
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task AshRun_Refuses_A_Model_Built_From_Other_Input()
    {
        var input = LogicGeneratorTests.Sample();
        var dir = BrokenProject(input, "sig");
        var changed = LogicGeneratorTests.Sample();
        changed.Routes[0] = changed.Routes[0] with { BufferCP = null };
        var o = new StringWriter();
        Assert.Equal(1, await Program.RunAsync(new[] { dir, "--model", Model }, changed, o));
        Assert.Contains("모델 재생성 필요", o.ToString());
        var takt = LogicGeneratorTests.Sample(takt: 30);               // run-time values (params.txt) need no rebuild
        Assert.DoesNotContain("모델 재생성 필요", await Run(dir, takt));
        Directory.Delete(dir, true);
    }

    static async Task<string> Run(string dir, ProjectInput input, params string[] extra)
    {
        var o = new StringWriter();
        await Program.RunAsync(new[] { dir, "--model", Model, "--parallel", "1" }.Concat(extra).ToArray(), input, o);
        return o.ToString();
    }

    [Fact]
    public async Task AshRun_Exits_1_With_Model_Message_When_Every_Baseline_Run_Fails()
    {
        var input = LogicGeneratorTests.Sample();
        var dir = BrokenProject(input, "fail");
        var old = Path.Combine(dir, "runs", "old");
        Directory.CreateDirectory(old);
        File.WriteAllLines(Path.Combine(old, Model + ".message"), Enumerable.Range(1, 7).Select(i => "line " + i));
        var o = new StringWriter();
        Assert.Equal(1, await Program.RunAsync(new[] { dir, "--model", Model, "--parallel", "1", "--append" }, input, o));
        var text = o.ToString();
        Assert.Contains("every baseline run failed", text);
        Assert.Contains("  line 5", text); Assert.DoesNotContain("line 6", text);
        Assert.DoesNotContain("decided", text);
        Directory.Delete(dir, true);
    }

    // "runs.*" alone would also match runs\ (Windows pattern quirk)
    static string[] Rotated(string dir) => Directory.GetDirectories(dir).Where(x => Path.GetFileName(x).StartsWith("runs.")).ToArray();

    [Fact]
    public async Task AshRun_Rotates_Previous_Runs_Unless_Append()
    {
        var input = LogicGeneratorTests.Sample();
        var dir = BrokenProject(input, "rot");
        Directory.CreateDirectory(Path.Combine(dir, "runs"));
        File.WriteAllText(Path.Combine(dir, "runs", "marker"), "");
        var text = await Run(dir, input);
        Assert.Contains("previous runs moved to", text);
        var moved = Assert.Single(Rotated(dir));
        Assert.Matches(@"runs\.\d{8}_\d{6}$", moved);
        Assert.True(File.Exists(Path.Combine(moved, "marker")));
        Assert.False(File.Exists(Path.Combine(dir, "runs", "marker")));
        await Run(dir, input, "--append");
        Assert.Single(Rotated(dir));     // --append keeps runs\
        Directory.Delete(dir, true);
    }

    [SkippableFact] public Task AshRun_Searches_A_Real_Model_In_Two_Slots() => Search(1);

    // two replications: the replication batch runs in both slots at once
    [SkippableFact] public Task AshRun_Fans_Replications_Out_To_Both_Slots() => Search(2);

    static async Task Search(int replications)
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        // 80 m one-way loop (20 m sides). The 40 m loop of the run tests never passes at Max 3 (3 vehicles jam: 2 deadlocks, stock 0),
        // and UnloadThreshold 3 < Call 4 holds every vehicle at the buffer until stock <= 3 (utilization 83% at 1, 76% at 2).
        // With Unload 5 the buffer wait never triggers: 1..3 vehicles all pass, so the search must come down from Max.
        var l = L.Of(L.Line("p1", 0, 0, 20, 0), L.Line("p2", 20, 0, 20, 20), L.Line("p3", 20, 20, 0, 20), L.Line("p4", 0, 20, 0, 0));
        l.Cps.Add(L.Cp("S001", 10, 0, "standby", "G1"));
        l.Cps.Add(L.Cp("L001", 20, 10, "load"));
        l.Cps.Add(L.Cp("B001", 12, 20, "buffer"));
        l.Cps.Add(L.Cp("U001", 4, 20, "unload"));
        l.Blocks.Add(new ASH.Core.Layout.LayoutBlock { Name = "B_x1", Cap = 1, Rect = new Rect2(new Point2(18, -1), new Point2(21, 2)) });
        var input = LogicGeneratorTests.Sample();                                  // G1 Min 1 Max 3, SimLength 1 h, Call 4
        for (int i = 0; i < input.Materials.Count; i++) input.Materials[i] = input.Materials[i] with { UnloadThreshold = 5 };
        input.Settings = input.Settings with { Replications = replications, InitStockMode = replications > 1 ? "Uniform" : "Fixed" };
        var work = Path.Combine(Path.GetTempPath(), "ash_e2e_" + Guid.NewGuid().ToString("N"));
        var specs = new[] { new VehicleGroupSpec("G1", "Tug", 3, 1.0, 1, 1) };
        var res = ModelBuilder.Build(new ModelBuildRequest(work, Model, input, l, new ExportOptions(Groups: specs)), AmHome!, TimeSpan.FromMinutes(5));
        Assert.True(res.Build!.Success, work + "\n" + res.Build.MakeErrText + res.Build.Am2ErrText);

        var sw = Stopwatch.StartNew();
        var output = new StringWriter();
        var code = await Program.RunAsync(new[] { work, "--model", Model, "--parallel", "2", "--initial-reps", "1", "--timeout-min", "5",
            "--dialog-button", "Student Version", "--impact" }, input, output);
        sw.Stop();
        Console.WriteLine(output.ToString());
        Console.WriteLine($"ash-run e2e elapsed {sw.Elapsed.TotalSeconds:0.0} s, exit {code}");

        Assert.True(code == 0 || code == 2, output.ToString());
        var rows = SummaryTsv.ReadAll(Path.Combine(work, "runs", "summary.tsv"));
        Assert.True(rows.Count >= 2 * replications, "summary rows " + rows.Count);
        if (replications > 1) Assert.True(Directory.Exists(Path.Combine(work, "work", "w1")) && Directory.Exists(Path.Combine(work, "work", "w2")));
        Assert.True(File.Exists(Path.Combine(work, "runs", "impact.tsv")));
        var decided = int.Parse(output.ToString().Split('\n').Single(x => x.StartsWith("G1 ")).Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
        Assert.InRange(decided, 1, 2);   // decided below Max 3: the first real "Max 미만" check
        Assert.Contains(rows, r => r.Ok && r.Active == decided);
        if (Environment.GetEnvironmentVariable("ASH_KEEP_TEMP") != "1") Directory.Delete(work, true);
    }
}
#endif
