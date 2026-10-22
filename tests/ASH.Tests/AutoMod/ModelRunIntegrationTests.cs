using System.Globalization;
using ASH.Core.Asy;
using ASH.Core.AutoMod;
using ASH.Core.Export;
using ASH.Core.Geometry;
using ASH.Core.Input;
using ASH.Core.Model;
using ASH.Core.Runner;
using ASH.Tests.Layout;
using ASH.Tests.Model;

namespace ASH.Tests.AutoMod;

public class ModelRunIntegrationTests
{
    static string? AmHome => Environment.GetEnvironmentVariable("ASH_AM_HOME");
    const string Model = "ashrun";

    sealed class Run(string work, RunOutcome result, List<string[]> rows, string tsv)
    {
        public string Work => work; public RunOutcome Result => result; public string ResultTsvPath => Path.Combine(result.RunDir, "result.tsv"); public string ReportPath => Path.Combine(result.RunDir, Model + ".report"); public List<string[]> Rows => rows; public string Tsv => tsv;
    }

    /// <summary>One-way square loop (4 paths, 10 m sides) with S001 standby, L001 load, B001 buffer, U001 unload, one block;
    /// a second group adds S002 standby (G2) and L002 load.</summary>
    static Run BuildAndRun(ProjectInput input, IReadOnlyDictionary<string, int> active, int seed = 1, IReadOnlyDictionary<string, VehicleTemplateOverride>? vehOverrides = null)
    {
        var l = L.Of(L.Line("p1", 0, 0, 10, 0), L.Line("p2", 10, 0, 10, 10), L.Line("p3", 10, 10, 0, 10), L.Line("p4", 0, 10, 0, 0));
        l.Cps.Add(L.Cp("S001", 5, 0, "standby", "G1"));
        l.Cps.Add(L.Cp("L001", 10, 5, "load"));
        l.Cps.Add(L.Cp("B001", 6, 10, "buffer"));
        l.Cps.Add(L.Cp("U001", 2, 10, "unload"));
        if (input.Groups.Count > 1) { l.Cps.Add(L.Cp("S002", 0, 5, "standby", "G2")); l.Cps.Add(L.Cp("L002", 10, 8, "load")); }
        var work = Path.Combine(Path.GetTempPath(), "ash_run_" + Guid.NewGuid().ToString("N"));
        var specs = ModelBuilder.VehicleGroups(input);
        l.Blocks.Add(new ASH.Core.Layout.LayoutBlock { Name = "B_x1", Cap = 1, Rect = new Rect2(new Point2(8, -1), new Point2(11, 2)) }); // corner p1/p2
        var res = ModelBuilder.Build(new ModelBuildRequest(work, Model, input, l, new ExportOptions(Groups: specs), VehicleOverrides: vehOverrides), AmHome!, TimeSpan.FromMinutes(5));
        Assert.DoesNotContain(res.Findings, f => f.Code == ASH.Core.Layout.FindingCode.InputError);
        var b = res.Build!;
        Assert.True(b.Success, work + "\n" + b.MakeErrText + b.Am2ErrText);
        var o = ModelRunner.Run(new RunRequest(work, Model, input, new RunScenario(seed, active), TimeSpan.FromMinutes(2), "Student Version"));
        var msgPath = Path.Combine(o.RunDir, Model + ".message");
        var msg = File.Exists(msgPath) ? File.ReadAllText(msgPath) : "(no .message)";
        Assert.True(o.Success, work + "\n" + o.Error + "\n" + msg);
        var tsv = File.ReadAllText(Path.Combine(o.RunDir, "result.tsv"));
        Console.WriteLine($"{work} run {o.Duration.TotalSeconds:0.0} s, build dialogs [{string.Join("|", b.ClosedDialogTitles)}]\n{tsv}");
        return new Run(work, o, tsv.Split('\n').Where(x => x.Length > 0).Select(x => x.TrimEnd('\r').Split('\t')).ToList(), tsv);
    }

    static Run BuildAndRun(ProjectInput input, int activeG1) => BuildAndRun(input, new Dictionary<string, int> { ["G1"] = activeG1 });

    static void Cleanup(string dir) { if (Environment.GetEnvironmentVariable("ASH_KEEP_TEMP") != "1") Directory.Delete(dir, true); }

    static double V(string[] row, int i) => double.Parse(row[2 + i], CultureInfo.InvariantCulture);
    static string[] Mat(Run r, string name) => r.Rows.Single(x => x[0] == "material" && x[1] == name);

    // every vehicle kind and every material form must be accepted by AutoMod's build (geometry syntax, `set load type`) and run to the end
    [SkippableTheory]
    [InlineData("Standard", "Box", true, 1.2, 0.8)]
    [InlineData("Conveyor", "Pallet", false, 1.6, 1.0)]
    [InlineData("Ant", "Trolley", true, 1.0, 0.7)]
    [InlineData("Magnet", "Trolley", false, 2.0, 0.9)]
    public void Kinds_And_Forms_Build_And_Run(string kind, string form, bool emptyReturn, double length, double width)
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var input = LogicGeneratorTests.Sample();
        input.AgvTypes[0] = input.AgvTypes[0] with { Kind = kind, Length = length, Width = width, Accel = 0.3, Decel = 0.3 };
        for (int i = 0; i < input.Materials.Count; i++)
            input.Materials[i] = input.Materials[i] with { Form = form, EmptyReturn = emptyReturn, LoadLength = i == 0 ? 0.9 : null };
        if (emptyReturn) for (int i = 0; i < input.Routes.Count; i++) input.Routes[i] = input.Routes[i] with { ReturnFromCP = "U001", ReturnToCP = "L001" };
        var run = BuildAndRun(input, new Dictionary<string, int> { ["G1"] = 1 });
        var asy = File.ReadAllText(Path.Combine(run.Work, Model + ".arc", "agv.asy"));
        Assert.Contains("Base3_Blue", asy);
        Assert.Contains("set load type to L_m1", File.ReadAllText(Path.Combine(run.Work, Model + ".arc", Model + "~.asy")));
        Cleanup(run.Work);
    }

    [SkippableFact]
    public void Run_With_VehSeg_Override_From_Demo_Block()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var demo = File.ReadAllLines(Path.Combine(AmHome!, "demos", "amoddemos", "demos", "agv", "agvdemo.arc", "agv.asy"));
        var start = Array.FindIndex(demo, x => x.StartsWith("AGVVEHSEG name DefSegment"));
        Assert.True(start >= 0, "demo DefSegment block");
        var block = demo.Skip(start).TakeWhile((x, i) => i == 0 || !(x.Length > 0 && char.IsUpper(x[0]))).ToList(); // geometry lines start with digits, the next top-level keyword is upper-case
        var text = string.Join("\n", block).Replace("DefSegment", "FromServer") + "\n";
        var run = BuildAndRun(LogicGeneratorTests.Sample(), new Dictionary<string, int> { ["G1"] = 1 }, 1,
            new Dictionary<string, VehicleTemplateOverride> { ["G1"] = new(VehSegText: text) });
        Assert.Contains("AGVVEHSEG name Seg_G1 ", File.ReadAllText(Path.Combine(run.Work, Model + ".arc", "agv.asy")));
        Cleanup(run.Work);
    }

    [SkippableFact]
    public void Run_Writes_Result_Tsv_With_Material_Rows()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var run = BuildAndRun(LogicGeneratorTests.Sample(), 1);
        Assert.Equal("kind\tname\tgroup\tvalue1\tvalue2\tvalue3\tvalue4\tvalue5\tvalue6\tvalue7\tvalue8", run.Tsv.Split('\n')[0].TrimEnd('\r'));
        var mats = run.Rows.Where(x => x[0] == "material").ToList();
        Assert.Equal(new[] { "M1", "M2" }, mats.Select(x => x[1]));
        Assert.All(mats, x => { Assert.Equal(11, x.Length); Assert.True(V(x, 3) > 0, "supplies"); Assert.True(V(x, 4) > 0, "expected"); });
        var blk = run.Rows.Single(x => x[0] == "block" && x[1] == "B_x1");
        Assert.True(V(blk, 1) > 0, "block passes"); // first real check of the 1x1 block template geometry
        Assert.Equal(3600, V(run.Rows.Single(x => x[0] == "clock"), 2), 3);
        Assert.Equal(0, V(run.Rows.Single(x => x[0] == "deadlock"), 1));
        Assert.DoesNotContain(run.Rows, x => x[0] == "blockgroup");
        var veh = run.Rows.Where(x => x[0] == "vehicle").ToList();
        Assert.Equal(3, veh.Count);
        Assert.All(veh, x => { Assert.Equal(11, x.Length); Assert.Equal(0, V(x, 6)); }); // no MTBF: no repair time
        Assert.DoesNotContain("ASH:", File.ReadAllText(Path.Combine(run.Result.RunDir, Model + ".message"))); // Debug=0: no logic prints
        Cleanup(run.Work); // kept on failure for inspection
    }

    [SkippableFact]
    public void Breakdowns_EmptySpeed_And_Capacity_Run_And_Differ_By_Seed()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var p = LogicGeneratorTests.Sample();
        p.AgvTypes[0] = p.AgvTypes[0] with { SpeedEmpty = 1.5, Decel = 0.3, Capacity = 2, MTBF = 600, MTTR = 60 };
        double Down(Run r) => r.Rows.Where(x => x[0] == "vehicle").Sum(x => V(x, 6));
        var a = BuildAndRun(p, new Dictionary<string, int> { ["G1"] = 2 }, seed: 1);
        var b = BuildAndRun(p, new Dictionary<string, int> { ["G1"] = 2 }, seed: 2);
        Assert.True(Down(a) > 0, "repair time with MTBF 600 s:\n" + a.Tsv);
        Assert.NotEqual(Down(a), Down(b));                                      // each seed gets its own breakdowns
        Assert.All(a.Rows.Where(x => x[0] == "material"), x => Assert.True(V(x, 3) > 0, "supplies"));
        Assert.Equal(0, V(a.Rows.Single(x => x[0] == "deadlock"), 1));
        Cleanup(a.Work); Cleanup(b.Work);
    }

    [SkippableFact]
    public void Run_Debug_Flag_Turns_On_Logic_Prints()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var input = LogicGeneratorTests.Sample();
        var run = BuildAndRun(input, 1);
        var o = ModelRunner.Run(new RunRequest(run.Work, Model, input, new RunScenario(1, new Dictionary<string, int> { ["G1"] = 1 }, Debug: true), TimeSpan.FromMinutes(2), "Student Version"));
        Assert.True(o.Success, o.Error);
        var msg = File.ReadAllText(Path.Combine(o.RunDir, Model + ".message"));
        Assert.Contains("ASH: params ok", msg);
        Assert.Contains("ASH: leg m=1 to", msg);
        Assert.Contains("ASH: snap written", msg);
        Cleanup(run.Work);
    }

    [SkippableFact]
    public void Run_With_Events_Writes_Events_Tsv()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var input = LogicGeneratorTests.Sample();
        var run = BuildAndRun(input, 1);
        Assert.False(File.Exists(Path.Combine(run.Result.RunDir, "events.tsv"))); // EVENTS 0: no file
        var o = ModelRunner.Run(new RunRequest(run.Work, Model, input, new RunScenario(1, new Dictionary<string, int> { ["G1"] = 1 }, Events: true), TimeSpan.FromMinutes(2), "Student Version"));
        Assert.True(o.Success, o.Error);
        Assert.EndsWith("_ev", o.RunId);
        Assert.Equal(run.Tsv, File.ReadAllText(Path.Combine(o.RunDir, "result.tsv"))); // same KPIs with or without events
        var ev = EventLog.Read(Path.Combine(o.RunDir, "events.tsv"));
        Assert.Contains(ev, e => e.Kind == "deliver" && e.Material == "M1" && e.Cp == "U001" && e.Value > 0);
        Assert.Contains(ev, e => e.Kind == "call");
        Assert.Contains(ev, e => e.Kind == "pick" && e.Cp == "L001");
        Assert.Contains(ev, e => e.Kind == "wait" && e.Material == "M1");
        Assert.All(ev, e => Assert.True(e.T >= 360));
        Cleanup(run.Work);
    }

    // a 0.19 model on a 0.18 server (or ash-run): params.txt ends "DEBUG n / END" with no EVENTS line, and the model must still finish
    [SkippableFact]
    public void Run_Accepts_Params_With_And_Without_Events_Line()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var input = LogicGeneratorTests.Sample();
        var run = BuildAndRun(input, 1);
        var text = ParamsFile.Render(input, new RunScenario(1, new Dictionary<string, int> { ["G1"] = 1 }, Events: true));
        var old = string.Join("\n", text.Split('\n').Where(x => !x.StartsWith("EVENTS ")));
        Assert.Contains("DEBUG 0\nEND", old);
        foreach (var (p, events) in new[] { (old, false), (text, true) })
        {
            foreach (var n in new[] { "result.tsv", "events.tsv" }) File.Delete(Path.Combine(run.Work, n));
            File.WriteAllText(Path.Combine(run.Work, Model + ".arc", "params.txt"), p);
            using (var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(run.Work, Model + ".exe"), "-w") { WorkingDirectory = run.Work, UseShellExecute = false })!)
            using (DialogCloser.Start(proc.Id, new[] { "Authorization" }, _ => { }, "Student Version"))
            {
                var exited = proc.WaitForExit(120_000);
                if (!exited) { proc.Kill(); proc.WaitForExit(5000); }
                Assert.True(exited, "model hung with params:\n" + p);
                Assert.Equal(0, proc.ExitCode);
            }
            Assert.Equal(run.Tsv, File.ReadAllText(Path.Combine(run.Work, "result.tsv")));
            Assert.Equal(events, File.Exists(Path.Combine(run.Work, "events.tsv")));
        }
        Cleanup(run.Work);
    }

    [SkippableFact]
    public void Run_Two_Scenarios_Concurrently_In_Separate_Work_Slots()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var input = LogicGeneratorTests.Sample();
        var run = BuildAndRun(input, 1);
        var tasks = new[] { 1, 2 }.Select(slot => System.Threading.Tasks.Task.Run(() =>
        {
            var w = ModelRunner.PrepareWorkSlot(run.Work, Model, slot);
            return ModelRunner.Run(new RunRequest(run.Work, Model, input, new RunScenario(10 + slot, new Dictionary<string, int> { ["G1"] = 1 }),
                TimeSpan.FromMinutes(3), "Student Version", WorkDir: w));
        })).ToArray();
        System.Threading.Tasks.Task.WaitAll(tasks);
        foreach (var t in tasks) Assert.True(t.Result.Success, t.Result.Error);
        Assert.NotEqual(tasks[0].Result.RunDir, tasks[1].Result.RunDir);
        Assert.All(tasks, t => Assert.True(File.Exists(t.Result.ResultTsvPath) && File.Exists(t.Result.ReportPath)));
        Cleanup(run.Work);
    }

    [SkippableFact]
    public void Run_Parks_Excess_Vehicles()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var run = BuildAndRun(LogicGeneratorTests.Sample(), 1);
        var g = run.Rows.Single(x => x[0] == "group");
        Assert.Equal("G1", g[1]);
        Assert.Equal(1, V(g, 1)); // numveh 3, 2 parked
        Cleanup(run.Work);
    }

    [SkippableFact]
    public void Run_Starved_Material_Reports_Stop_Time()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var run = BuildAndRun(LogicGeneratorTests.Sample(takt: 5), 1);
        Assert.Contains(run.Rows, x => x[0] == "material" && V(x, 5) > 0);
        Cleanup(run.Work);
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
        Cleanup(run.Work);
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
        var report = File.ReadAllText(run.ReportPath);
        foreach (var type in new[] { "G1", "G2" }) // AGVS types are named after the groups; first data row of each type in "Vehicle Statistics": delivering %, trips, ...
        {
            var m = System.Text.RegularExpressions.Regex.Match(report, @"\n" + type + @":\r?\n\s+[\d.]+\s+(\d+)");
            Assert.True(m.Success && int.Parse(m.Groups[1].Value) > 0, type + " made no deliveries");
        }
        Cleanup(run.Work);
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
        Assert.Equal(File.ReadAllBytes(a.ResultTsvPath), File.ReadAllBytes(b.ResultTsvPath));
        Cleanup(a.Work); Cleanup(b.Work);
    }

    [SkippableFact]
    public void Run_Result_Flows_Through_Parser_Kpi_And_Judge()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var input = LogicGeneratorTests.Sample();
        var run = BuildAndRun(input, 1);
        Assert.Equal("n1_s1", run.Result.RunId);
        Assert.True(File.Exists(run.ReportPath) && File.Exists(Path.Combine(run.Result.RunDir, Model + ".message")));
        var kpi = Kpi.FromRun(ResultParser.Parse(run.ResultTsvPath), ResultParser.ParseReport(run.ReportPath), input).Single();
        Assert.InRange(kpi.Utilization, 0.0001, 1);
        Assert.True(kpi.Supplies > 0 && kpi.Expected > 0 && kpi.Active == 1);
        var v = Judge.Evaluate(new[] { kpi }, input.Settings);
        Assert.False(string.IsNullOrEmpty(v.Reason) && !v.Ok);
        Console.WriteLine($"verdict {v.Ok} '{v.Reason}' util {kpi.Utilization}");
        Cleanup(run.Work);
    }

    [SkippableFact]
    public void Run_Fixed_High_Init_Stock_Passes_On_Consumption_Despite_Few_Supplies()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var input = LogicGeneratorTests.Sample();
        for (int i = 0; i < input.Materials.Count; i++) input.Materials[i] = input.Materials[i] with { InitStock = 8 }; // Call 4: first call after 40 takts
        var run = BuildAndRun(input, 1);
        var verdicts = new List<(int Active, RunKpi Kpi, Verdict V)>();
        for (int n = 1; n <= 3; n++)
        {
            var o = n == 1 ? run.Result : ModelRunner.Run(new RunRequest(run.Work, Model, input, new RunScenario(1, new Dictionary<string, int> { ["G1"] = n }), TimeSpan.FromMinutes(2), "Student Version"));
            Assert.True(o.Success, o.Error);
            var k = Kpi.FromRun(ResultParser.Parse(o.ResultTsvPath!), ResultParser.ParseReport(o.ReportPath!), input).Single();
            var v = Judge.Evaluate(new[] { k }, input.Settings);
            Console.WriteLine($"n{n}: supplies {k.Supplies} consumed {k.Consumed} expected {k.Expected} stop {k.StopTimeSec} stock {k.MinStockRatio} util {k.Utilization} -> {v.Ok} {v.Reason}");
            verdicts.Add((n, k, v));
            if (v.Ok) break;
        }
        var ok = Assert.Single(verdicts, x => x.V.Ok);
        Assert.True(ok.Kpi.Supplies < 0.95 * ok.Kpi.Expected, "old supplies rule would have failed this run");
        Assert.Equal(0, ok.Kpi.StopTimeSec);
        Cleanup(run.Work);
    }
}
