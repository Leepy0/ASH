using System.Globalization;
using ASH.Core.Asy;
using ASH.Core.AutoMod;
using ASH.Core.Export;
using ASH.Core.Geometry;
using ASH.Core.Input;
using ASH.Core.Model;
using ASH.Tests.Layout;
using ASH.Tests.Model;

namespace ASH.Tests.AutoMod;

public class ModelRunIntegrationTests
{
    static string? AmHome => Environment.GetEnvironmentVariable("ASH_AM_HOME");
    const string Model = "ashrun";

    sealed class Run(string work, AmRunResult result, List<string[]> rows, string tsv)
    {
        public string Work => work; public AmRunResult Result => result; public List<string[]> Rows => rows; public string Tsv => tsv;
    }

    /// <summary>One-way square loop (4 paths, 10 m sides) with S001 standby, L001 load, B001 buffer, U001 unload, one block;
    /// a second group adds S002 standby (G2) and L002 load.</summary>
    static Run BuildAndRun(ProjectInput input, IReadOnlyDictionary<string, int> active, int seed = 1)
    {
        var l = L.Of(L.Line("p1", 0, 0, 10, 0), L.Line("p2", 10, 0, 10, 10), L.Line("p3", 10, 10, 0, 10), L.Line("p4", 0, 10, 0, 0));
        l.Cps.Add(L.Cp("S001", 5, 0, "standby", "G1"));
        l.Cps.Add(L.Cp("L001", 10, 5, "load"));
        l.Cps.Add(L.Cp("B001", 6, 10, "buffer"));
        l.Cps.Add(L.Cp("U001", 2, 10, "unload"));
        if (input.Groups.Count > 1) { l.Cps.Add(L.Cp("S002", 0, 5, "standby", "G2")); l.Cps.Add(L.Cp("L002", 10, 8, "load")); }
        var work = Path.Combine(Path.GetTempPath(), "ash_run_" + Guid.NewGuid().ToString("N"));
        var arc = Path.Combine(work, Model + ".arc");
        var specs = input.Groups.Select(g =>
        {
            var t = input.AgvTypes.Single(x => x.Type == g.Type);
            return new VehicleGroupSpec(g.Group, g.Type, g.MaxCount, t.SpeedLoaded, t.Length, t.Width);
        }).ToArray();
        ExportRunner.Run(l, new ExportOptions(Groups: specs), arc);
        var info = new LayoutInfo(l.Cps.Select(c => c.Name).ToList(), new[] { "B_x1" }, new Dictionary<string, string>());
        var agv = Path.Combine(arc, "agv.asy");
        var sys = AsyReader.ReadFile(agv);
        LogicGenerator.AddDispatchLists(sys, input, info);
        AsyWriter.WriteFile(sys, agv);
        // Task 5 moves this token into ExportOptions.ClaimBlockOnNextCp
        File.WriteAllText(agv, File.ReadAllText(agv).Replace("ALTERNATE NONE ignore ResumeSpeedWhenClaimed", "ALTERNATE NONE ignore OldBlockClaim ResumeSpeedWhenClaimed"));

        var pm = LogicGenerator.BuildProcessModel(input, info, "Meters");
        pm.Blocks.Add(new BlockEntity("B_x1", 1, new Rect2(new Point2(8, -1), new Point2(11, 2)))); // corner p1/p2
        ProcessAsyWriter.WriteFile(pm, Path.Combine(arc, Model + "~.asy"));
        File.WriteAllText(Path.Combine(arc, "model.amo"), string.Join("\n",
            "VERSION 12.6.1.19", "UNITS Meters Seconds", "RNSET 1",
            "DRAWPOS minx -5 maxx 15 miny -5 maxy 15 minz -10 maxz 10 trx 0 try 0",
            "MOVESYS name agv", "\tSYSPOS endx 1", $"PROCSYS name {Model}~", "\tSYSPOS endx 1",
            "CONTROL snaplen " + (input.Settings.SimLengthSec / 3600).ToString(CultureInfo.InvariantCulture) + " Hours counts 1 autorep", ""));
        ParamsFile.Write(input, new RunScenario(seed, active), Path.Combine(arc, "params.txt"));

        var b = AmBuilder.Build(AmHome!, work, Model, TimeSpan.FromMinutes(5));
        Assert.True(b.Success, work + "\n" + b.MakeErrText + b.Am2ErrText);
        var r = AmRunner.RunHeadless(Path.Combine(work, Model + ".exe"), work, TimeSpan.FromMinutes(2), "Student Version");
        var msg = File.Exists(r.MessagePath) ? File.ReadAllText(r.MessagePath) : "(no .message)";
        Assert.False(r.TimedOut, work + "\n" + msg);
        Assert.True(File.Exists(r.ResultTsvPath), work + "\n" + msg);
        var tsv = File.ReadAllText(r.ResultTsvPath);
        Console.WriteLine($"{work} run {r.Duration.TotalSeconds:0.0} s, build dialogs [{string.Join("|", b.ClosedDialogTitles)}], run dialogs [{string.Join("|", r.ClickedDialogTitles)}]\n{tsv}");
        return new Run(work, r, tsv.Split('\n').Where(x => x.Length > 0).Select(x => x.TrimEnd('\r').Split('\t')).ToList(), tsv);
    }

    static Run BuildAndRun(ProjectInput input, int activeG1) => BuildAndRun(input, new Dictionary<string, int> { ["G1"] = activeG1 });

    static double V(string[] row, int i) => double.Parse(row[2 + i], CultureInfo.InvariantCulture);
    static string[] Mat(Run r, string name) => r.Rows.Single(x => x[0] == "material" && x[1] == name);

    [SkippableFact]
    public void Run_Writes_Result_Tsv_With_Material_Rows()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var run = BuildAndRun(LogicGeneratorTests.Sample(), 1);
        Assert.Equal("kind\tname\tgroup\tvalue1\tvalue2\tvalue3\tvalue4\tvalue5\tvalue6\tvalue7\tvalue8", run.Tsv.Split('\n')[0].TrimEnd('\r'));
        var mats = run.Rows.Where(x => x[0] == "material").ToList();
        Assert.Equal(new[] { "M1", "M2" }, mats.Select(x => x[1]));
        Assert.All(mats, x => { Assert.Equal(11, x.Length); Assert.True(V(x, 3) > 0, "supplies"); Assert.True(V(x, 4) > 0, "expected"); });
        Assert.Contains(run.Rows, x => x[0] == "block" && x[1] == "B_x1");
        Assert.Equal(3600, V(run.Rows.Single(x => x[0] == "clock"), 2), 3);
        Assert.Equal(0, V(run.Rows.Single(x => x[0] == "deadlock"), 1));
        Directory.Delete(run.Work, true); // kept on failure for inspection
    }

    [SkippableFact]
    public void Run_Parks_Excess_Vehicles()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var run = BuildAndRun(LogicGeneratorTests.Sample(), 1);
        var g = run.Rows.Single(x => x[0] == "group");
        Assert.Equal("G1", g[1]);
        Assert.Equal(1, V(g, 1)); // numveh 3, 2 parked
        Directory.Delete(run.Work, true);
    }

    [SkippableFact]
    public void Run_Starved_Material_Reports_Stop_Time()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var run = BuildAndRun(LogicGeneratorTests.Sample(takt: 5), 1);
        Assert.Contains(run.Rows, x => x[0] == "material" && V(x, 5) > 0);
        Directory.Delete(run.Work, true);
    }

    [SkippableFact]
    public void Run_Recovers_From_Zero_Stock_And_Multi_Container_Need()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var p = LogicGeneratorTests.Sample();
        p.Materials[0] = p.Materials[0] with { InitStock = 0 };                              // starts stopped, must call anyway
        // need 2 containers per cycle, Call 0 below that need, stock 1: only the below-need call can start supply
        p.Materials[1] = p.Materials[1] with { UnitsPerCycle = 2, UnitsPerContainer = 1, CallThreshold = 0, InitStock = 1 };
        var run = BuildAndRun(p, 1);
        var span = p.Settings.SimLengthSec - p.Settings.WarmupSec;
        foreach (var m in new[] { "M1", "M2" })
        {
            var row = Mat(run, m);
            Assert.True(V(row, 3) >= 2, m + " supplies");            // called again after the first delivery
            Assert.True(V(row, 5) < span * 0.95, m + " stop time");  // not a permanent stop
        }
        Directory.Delete(run.Work, true);
    }

    [SkippableFact]
    public void Run_Two_Groups_Serve_Their_Own_Materials()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var run = BuildAndRun(LogicGeneratorTests.TwoGroups(), new Dictionary<string, int> { ["G1"] = 1, ["G2"] = 1 });
        Assert.Equal(1, V(run.Rows.Single(x => x[0] == "group" && x[1] == "G1"), 1)); // numveh 3, 2 parked
        Assert.Equal(1, V(run.Rows.Single(x => x[0] == "group" && x[1] == "G2"), 1)); // numveh 2, 1 parked
        Assert.True(V(Mat(run, "M1"), 3) > 0);
        Assert.True(V(Mat(run, "M2"), 3) > 0);
        var report = File.ReadAllText(run.Result.ReportPath);
        foreach (var type in new[] { "Tug", "Tug2" }) // first data row of each type in "Vehicle Statistics": delivering %, trips, ...
        {
            var m = System.Text.RegularExpressions.Regex.Match(report, @"\n" + type + @":\r?\n\s+[\d.]+\s+(\d+)");
            Assert.True(m.Success && int.Parse(m.Groups[1].Value) > 0, type + " made no deliveries");
        }
        Directory.Delete(run.Work, true);
    }

    [SkippableFact]
    public void Run_Same_Seed_Is_Deterministic()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        static ProjectInput Uniform()
        {
            var p = LogicGeneratorTests.Sample();
            p.Settings = p.Settings with { InitStockMode = "Uniform" };
            return p;
        }
        var active = new Dictionary<string, int> { ["G1"] = 2 };
        // the seed reaches the model through params.txt init stocks; the logic itself draws no random numbers
        static string[] Body(string p) => p.Split('\n').Where(x => !x.StartsWith("SEED ")).ToArray();
        Assert.NotEqual(Body(ParamsFile.Render(Uniform(), new RunScenario(1, active))), Body(ParamsFile.Render(Uniform(), new RunScenario(2, active))));
        var a = BuildAndRun(Uniform(), active);
        var b = BuildAndRun(Uniform(), active);
        Assert.Equal(File.ReadAllBytes(a.Result.ResultTsvPath), File.ReadAllBytes(b.Result.ResultTsvPath));
        Directory.Delete(a.Work, true); Directory.Delete(b.Work, true);
    }
}
