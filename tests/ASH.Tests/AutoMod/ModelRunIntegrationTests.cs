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
    static Run BuildAndRun(ProjectInput input, IReadOnlyDictionary<string, int> active, int seed = 1, IReadOnlyDictionary<string, VehicleTemplateOverride>? vehOverrides = null,
        string units = "Meters", bool autoView = false, IEnumerable<ASH.Core.Layout.LayoutAsset>? assets = null, Action<ASH.Core.Layout.Layout>? layoutExtra = null)
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
        if (assets != null) l.Assets.AddRange(assets);
        layoutExtra?.Invoke(l);
        var res = ModelBuilder.Build(new ModelBuildRequest(work, Model, input, l, new ExportOptions(Units: units, Groups: specs), VehicleOverrides: vehOverrides, AutoView: autoView), AmHome!, TimeSpan.FromMinutes(5));
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

    // 3D assets (design §8): the ash_3d static system must pass AutoMod's build (cells in the model units) and not disturb the run
    [SkippableTheory]
    [InlineData("Meters")]
    [InlineData("Millimeters")]
    public void Run_With_3d_Assets_Builds_The_Static_System(string units)
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var assets = ASH.Core.Layout.AssetKinds.Blocks.Select((k, i) => new ASH.Core.Layout.LayoutAsset { Kind = k, X = 14 + 4 * (i % 5), Y = 2 + 6 * (i / 5), RotDeg = 30 * i }).ToList();
        assets.Add(new ASH.Core.Layout.LayoutAsset { Kind = "WALL", Segments = { new LineSeg(new Point2(-3, -3), new Point2(13, -3)), new ArcSeg(new Point2(13, 0), new Point2(13, -3), 90) } });
        assets.Add(new ASH.Core.Layout.LayoutAsset { Kind = "FENCE", Segments = { new LineSeg(new Point2(-3, 13), new Point2(13, 13)) } });
        var run = BuildAndRun(LogicGeneratorTests.Sample(), new Dictionary<string, int> { ["G1"] = 1 }, units: units, assets: assets);
        Assert.Contains("STATIC name ash_3d", File.ReadAllText(Path.Combine(run.Work, Model + ".arc", "model.amo")));
        Assert.True(File.Exists(Path.Combine(run.Work, Model + ".dir", "ash_3d.sys")), "AutoMod compiled the static system");
        Assert.Equal(3600, V(run.Rows.Single(x => x[0] == "clock"), 2), 3);
        Cleanup(run.Work);
    }

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
        Assert.Equal("kind\tname\tgroup\tvalue1\tvalue2\tvalue3\tvalue4\tvalue5\tvalue6\tvalue7\tvalue8\tvalue9\tvalue10\tvalue11", run.Tsv.Split('\n')[0].TrimEnd('\r'));
        var mats = run.Rows.Where(x => x[0] == "material").ToList();
        Assert.Equal(new[] { "M1", "M2" }, mats.Select(x => x[1]));
        Assert.All(mats, x => { Assert.Equal(11, x.Length); Assert.True(V(x, 3) > 0, "supplies"); Assert.True(V(x, 4) > 0, "expected");
            Assert.True(V(x, 7) > 0, "supply jobs' distance"); Assert.True(V(x, 8) > 0, "supply jobs"); });
        var blk = run.Rows.Single(x => x[0] == "block" && x[1] == "B_x1");
        Assert.True(V(blk, 1) > 0, "block passes"); // first real check of the 1x1 block template geometry
        Assert.Equal(3600, V(run.Rows.Single(x => x[0] == "clock"), 2), 3);
        Assert.Equal(0, V(run.Rows.Single(x => x[0] == "deadlock"), 1));
        Assert.DoesNotContain(run.Rows, x => x[0] == "blockgroup");
        var veh = run.Rows.Where(x => x[0] == "vehicle").ToList();
        Assert.Equal(3, veh.Count);
        Assert.All(veh, x => { Assert.Equal(14, x.Length); Assert.Equal(0, V(x, 6)); Assert.Equal(0, V(x, 11)); }); // no MTBF: no repair time
        // the sample's vehicle waits at the buffer B001 for the unload threshold (pickup -> delivered is mostly that hold), which is not busy time:
        // a whole job's busy time stays under the pickup -> delivered span alone
        var tug = veh.Single(x => x[1] == "Tug1"); var st = run.Rows.Single(x => x[0] == "matstat" && x[1] == "M1");
        Assert.True(V(tug, 1) / V(tug, 3) < V(st, 3) - V(st, 1), $"busy/job {V(tug, 1) / V(tug, 3):0} s vs pickup->delivered {V(st, 3) - V(st, 1):0} s");
        Assert.Equal(V(tug, 3), mats.Sum(x => V(x, 8)));                    // the materials' supply jobs are the vehicle's jobs
        Assert.Equal(V(tug, 2), mats.Sum(x => V(x, 7)), 3);                 // and so is their distance
        Assert.DoesNotContain("ASH:", File.ReadAllText(Path.Combine(run.Result.RunDir, Model + ".message"))); // Debug=0: no logic prints
        Cleanup(run.Work); // kept on failure for inspection
    }

    [SkippableFact]
    public void Buffer_Wait_Allowance_Counts_The_Hold_As_Busy()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        static string[] Tug(Run r) => r.Rows.Single(x => x[0] == "vehicle" && x[1] == "Tug1");
        Run With(double sec)
        {
            var input = LogicGeneratorTests.Sample();
            for (int i = 0; i < input.Routes.Count; i++) input.Routes[i] = input.Routes[i] with { BufferWaitSec = sec };
            return BuildAndRun(input, 1);
        }
        // the allowance only changes the accounting: same seed, same jobs, so the busy times compare one to one
        var none = With(0); var some = With(60); var all = With(100000); // 100000 s: longer than any hold, all of it busy
        double b0 = V(Tug(none), 1), b60 = V(Tug(some), 1), ball = V(Tug(all), 1), jobs = V(Tug(none), 3);
        Console.WriteLine($"Tug1 busy over {jobs} jobs: allowance 0 {b0:0} s, 60 {b60:0} s, 100000 {ball:0} s; utilization {b0 / 3240:P1} / {b60 / 3240:P1} / {ball / 3240:P1}");
        Assert.Equal(jobs, V(Tug(some), 3)); Assert.Equal(jobs, V(Tug(all), 3));
        Assert.True(b0 < b60 && b60 < ball, "a larger allowance counts more of the hold as busy");
        Assert.True(b60 - b0 <= 60 * jobs + 1e-6, "at most the allowance per job comes back");
        Cleanup(none.Work); Cleanup(some.Work); Cleanup(all.Work);
    }

    // the time split the job page draws: the busy parts make up exactly the busy time (the utilization), all parts the measured window
    [SkippableFact]
    public void Time_Split_Adds_Up_To_Busy_Time_And_The_Window()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var input = LogicGeneratorTests.Sample();
        input.AgvTypes[0] = input.AgvTypes[0] with { MTBF = 600, MTTR = 60 };
        for (int i = 0; i < input.Materials.Count; i++) input.Materials[i] = input.Materials[i] with { EmptyReturn = true };
        for (int i = 0; i < input.Routes.Count; i++) input.Routes[i] = input.Routes[i] with { BufferWaitSec = 60, ExtraTime = 5, ReturnFromCP = "U001", ReturnToCP = "L001" };
        var run = BuildAndRun(input, new Dictionary<string, int> { ["G1"] = 2 });
        var rs = ResultParser.Parse(run.ResultTsvPath);
        var active = rs.Vehicles!.Where(x => x.Index <= 2).ToList();
        foreach (var v in active)
        {
            var s = v.Split!;
            Console.WriteLine($"{v.Name}: window {s.Window:0} busy {s.Busy:0} ({s.Busy / s.Window:P1}) = loaded {s.Loaded:0} + empty {s.Empty:0} + handling {s.Handling:0} + buffer(busy) {s.BufferBusy:0} + repair {s.Repair:0}; " +
                              $"buffer(not busy) {s.BufferIdle:0}, idle {s.Idle:0}; repair total {v.DownSec:0}; jobs {v.Jobs}");
            Assert.Equal(v.BusySec, s.Busy);
            Assert.Equal(s.Window, s.Loaded + s.Empty + s.Handling + s.BufferBusy + s.Repair + s.BufferIdle + s.Idle, 6);
            Assert.True(v.Jobs > 0 && s.Loaded > 0 && s.Empty > 0 && s.Idle >= 0 && s.BufferIdle >= 0, "every part in range");
            Assert.Equal(v.Jobs * (10 + 10 + 5 + 10 + 10), s.Handling, 6); // load + unload + extra + the empty container's load + unload
            Assert.True(s.BufferBusy <= 60 * v.Jobs + 1e-6, "at most the allowance per job is busy");
            Assert.True(s.Repair <= v.DownSec + 1e-6, "repairs that held a job are part of all repairs");
        }
        var kpi = Kpi.FromRun(rs, null, input).Single();
        Assert.Equal(kpi.Utilization, active.Sum(x => x.Split!.Busy) / active.Sum(x => x.Split!.Window), 9); // busy share = the reported utilization
        Cleanup(run.Work);
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

    // user report: AGVs piled up on the standby CP. AutoMod puts vehicle k on the k-th start-list CP and inserts the ones beyond the list later,
    // on top of the vehicle leaving the CP. With a start CP per vehicle back along the lane, every vehicle is on the path at time 0, length + 0.5 m
    // apart, and none is dropped in later. Positions come from the AutoView motion file (.mdf: µm along the path the vehicle was put on).
    [SkippableTheory]
    [InlineData("Meters", 3)]
    [InlineData("Millimeters", 5)] // 5 x 1.7 m: the lane goes back round the corner onto p4
    public void Vehicles_Start_Spaced_Along_The_Standby_Lane(string units, int max)
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var input = LogicGeneratorTests.Sample();
        input.AgvTypes[0] = input.AgvTypes[0] with { Length = 1.2, Width = 0.8 };
        input.Groups[0] = input.Groups[0] with { MaxCount = max };
        var run = BuildAndRun(input, new Dictionary<string, int> { ["G1"] = max }, units: units, autoView: true);
        var mdf = File.ReadAllLines(Path.Combine(run.Work, Model + ".avw", Model + ".mdf"));
        int t1 = Array.FindIndex(mdf, x => System.Text.RegularExpressions.Regex.IsMatch(x, @"^time (?!0 )[\d.]+ ")); // first event after time 0
        var path = new Dictionary<string, string>(); var pos = new Dictionary<string, double>();
        foreach (var x in mdf.Take(t1))
        {
            var m = System.Text.RegularExpressions.Regex.Match(x, @"object agv:G1\((\d+)\) relative to path agv:(\S+) visible");
            if (m.Success) path[m.Groups[1].Value] = m.Groups[2].Value;
            m = System.Text.RegularExpressions.Regex.Match(x, @"^\s*object agv:G1\((\d+)\) pos ([-\d.]+),");
            if (m.Success) pos[m.Groups[1].Value] = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / 1e6;
        }
        Assert.Equal(max, path.Count);                                                                 // all on the path at time 0
        Assert.DoesNotContain(mdf.Skip(t1), x => System.Text.RegularExpressions.Regex.IsMatch(x, @"object agv:G1\(\d+\) relative to path \S+ visible\s*$")); // none dropped in later (the end-of-run dump has "visible color ...")
        // place along the lane, metres before the p4/p1 corner negative: S001 at 5, then 3.3, 1.6, -0.1, -1.8
        var lane = path.Keys.Select(v => (pos.TryGetValue(v, out var d) ? d : 0) - (path[v] == "p4" ? 10 : 0)).OrderByDescending(x => x).ToList();
        Console.WriteLine(string.Join(" ", path.Keys.Select(v => $"G1({v}) {path[v]} {(pos.TryGetValue(v, out var d) ? d : 0):0.###}")));
        Assert.All(path.Values, p => Assert.Contains(p, new[] { "p1", "p4" }));
        Assert.Equal(5, lane[0], 3);
        for (int i = 1; i < lane.Count; i++) Assert.True(lane[i - 1] - lane[i] >= 1.7 - 1e-3, $"vehicles {lane[i - 1]} and {lane[i]} only {lane[i - 1] - lane[i]:0.###} m apart");
        Assert.Equal(0, V(run.Rows.Single(x => x[0] == "deadlock"), 1));
        Assert.All(run.Rows.Where(x => x[0] == "material"), x => Assert.True(V(x, 3) > 0, "supplies"));
        Cleanup(run.Work);
    }

    // user report: vehicles parked on one standby CP overlapped (brake distance 0 m). The built model must carry brake/stop >= 0.5 m for both
    // load states, even without a Decel in the type, and still build and run with three vehicles of the group waiting.
    [SkippableFact]
    public void Parked_Vehicles_Model_Has_Brake_And_Stop_Distance_Of_Half_A_Meter()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var input = LogicGeneratorTests.Sample();
        input.AgvTypes[0] = input.AgvTypes[0] with { Accel = 0, Decel = 0 };
        var run = BuildAndRun(input, new Dictionary<string, int> { ["G1"] = 3 });
        var asy = File.ReadAllLines(Path.Combine(run.Work, Model + ".arc", "agv.asy")).Select(x => x.Trim().Split('	')).ToList();
        var d = asy.Where(x => x[0] is "brakedist" or "stopdist").ToList();
        Assert.Equal(4 * input.Groups.Count, d.Count);                                                  // 2 states x brake/stop per vehicle type
        // Default "0 b" holds the distance; Empty "1 0" = Default x 1 + 0 inherits it
        double M(string[] x) => double.Parse(x[1].Split(' ')[1], CultureInfo.InvariantCulture) * (x[1].EndsWith("Feet") ? 0.3048 : 1);
        Assert.All(d.Where(x => x[1].StartsWith("0 ")), x => Assert.True(M(x) >= 0.5 - 1e-9, string.Join(" ", x)));
        Assert.All(d.Where(x => x[1].StartsWith("1 ")), x => Assert.StartsWith("1 0 ", x[1]));
        Cleanup(run.Work);
    }

    // the distances must reach the running vehicles, not only the file: a print in the logic reads them back from AutoMod. With "ignore" on the
    // system's ALTERNATE line the vehicles did not sense one another and every one of these read 0 (checked 2026-10-26).
    [SkippableFact]
    public void Running_Vehicles_Stop_Half_A_Meter_Behind_The_One_Ahead()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var input = LogicGeneratorTests.Sample();
        var run = BuildAndRun(input, new Dictionary<string, int> { ["G1"] = 2 });
        var arc = Path.Combine(run.Work, Model + ".arc");
        var logic = Directory.GetFiles(arc, "*.asy").First(f => File.ReadAllText(f).Contains("vehicle initialization procedure"));
        File.WriteAllText(logic, File.ReadAllText(logic).Replace("\tscale to x 1, y 1, z 1\n",
            "\tprint \"PROBE \" A_veh defined stop distance \" \" A_veh current stop distance to message\n\tscale to x 1, y 1, z 1\n"));
        ModelBuilder.PrepareBuildDir(run.Work, Model);
        var b = AmBuilder.Build(AmHome!, run.Work, Model, TimeSpan.FromMinutes(5));
        Assert.True(b.Success, b.MakeErrText + b.Am2ErrText);
        var o = ModelRunner.Run(new RunRequest(run.Work, Model, input, new RunScenario(1, new Dictionary<string, int> { ["G1"] = 2 }), TimeSpan.FromMinutes(2), "Student Version"));
        Assert.True(o.Success, o.Error);
        var probes = File.ReadAllLines(Path.Combine(o.RunDir, Model + ".message")).Where(x => x.Contains("PROBE ")).ToList();
        Assert.NotEmpty(probes);
        foreach (var p in probes)
        {
            var v = p.Substring(p.IndexOf("PROBE ", StringComparison.Ordinal) + 6).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            Assert.Equal(0.5, v[0], 3); Assert.Equal(0.5, v[1], 3); // metres (the model's unit)
        }
        Cleanup(run.Work);
    }
    /// <summary>CV001 north of the loop: 8 m east, a counter-clockwise quarter arc (radius 2 m), 5 m north; plus a picture-only conveyor.</summary>
    static void AddConveyors(ASH.Core.Layout.Layout l)
    {
        l.Conveyors.Add(new ASH.Core.Layout.LayoutConveyor { Name = "CV001", Id = "1A", Segments = { new LineSeg(new Point2(0, 15), new Point2(8, 15)),
            new ArcSeg(new Point2(8, 17), new Point2(8, 15), 90), new LineSeg(new Point2(10, 17), new Point2(10, 22)) } });
        l.Conveyors.Add(new ASH.Core.Layout.LayoutConveyor { GraphicOnly = true, WidthM = 1.0, Segments = { new LineSeg(new Point2(0, 25), new Point2(10, 25)) } });
    }

    // the conveyor line (design 9.3): M1 starts with no stock and one product every 5 s uses 0.1 container, more than one vehicle brings,
    // so the line stops at the use point, backs up to the entry (input delay) and runs again after each delivery; M2 keeps the takt clock.
    // An empty ConsumeAt puts _use on _out at the end.
    [SkippableTheory]
    [InlineData(10.0)]
    [InlineData(null)]
    public void Conveyor_Line_Stops_Without_Stock_Backs_Up_And_Restarts_After_Delivery(double? consumeAt)
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var input = LogicGeneratorTests.Sample();
        input.Materials[0] = input.Materials[0] with { TaktTime = 5, InitStock = 0, ConveyorFed = true }; // as the reader sets a Conveyors material
        input.Conveyors.Add(new ConveyorInfo("CV001", "M1", 5, 1, 0.3, null, consumeAt, Row: 2));
        var run = BuildAndRun(input, new Dictionary<string, int> { ["G1"] = 1 }, layoutExtra: AddConveyors);
        var arc = Path.Combine(run.Work, Model + ".arc");
        Assert.Contains("MOVESYS name ash_conv", File.ReadAllText(Path.Combine(arc, "model.amo")));
        var rs = ResultParser.Parse(run.ResultTsvPath);
        var cv = Assert.Single(rs.Conveyors!);
        var m1 = rs.Materials.Single(m => m.Name == "M1"); var m2 = rs.Materials.Single(m => m.Name == "M2");
        Console.WriteLine($"CV001 passed {cv.Passed}, stopped {cv.StopSec:0} s, input delay {cv.InputDelaySec:0} s; M1 supplies {m1.Supplies} consumed {m1.Consumed:0.##}; M2 consumed {m2.Consumed:0.##}");
        Assert.Equal("M1", cv.Material);
        Assert.True(cv.Passed > 0, "products passed the end");
        Assert.True(cv.StopSec > 0, "the line stood at the use point without stock");
        Assert.True(cv.InputDelaySec > 0, "the stop backed the products up to the entry");
        Assert.True(m1.Supplies > 0);
        // every product consumes 0.1 container at the use point; those between the use point and the end at the snap are consumed, not passed
        Assert.InRange(m1.Consumed - cv.Passed * 0.1, -1e-6, 2.0);
        Assert.True(m2.Consumed > 0, "M2 is still consumed by the takt clock");
        // stop and restart in time: every restart comes right after (at most the 1 s poll) a delivery of M1
        var o = ModelRunner.Run(new RunRequest(run.Work, Model, input, new RunScenario(1, new Dictionary<string, int> { ["G1"] = 1 }, Events: true), TimeSpan.FromMinutes(2), "Student Version"));
        Assert.True(o.Success, o.Error);
        var ev = EventLog.Read(Path.Combine(o.RunDir, "events.tsv"));
        var stops = ev.Where(e => e.Kind == "convstop").ToList(); var goes = ev.Where(e => e.Kind == "convgo").ToList();
        var delivers = ev.Where(e => e.Kind == "deliver" && e.Material == "M1").Select(e => e.T).ToList();
        Console.WriteLine($"{stops.Count} stops, {goes.Count} restarts, first stop {stops.FirstOrDefault()?.T:0} s, first restart {goes.FirstOrDefault()?.T:0} s");
        Assert.NotEmpty(stops); Assert.NotEmpty(goes);
        Assert.All(goes.Where(g => g.T > 361), g => Assert.Contains(delivers, d => d <= g.T + 1e-6 && g.T - d <= 1.0 + 1e-6));
        Cleanup(run.Work);
    }
}
