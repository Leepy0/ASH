using ASH.Core.AutoMod;
using ASH.Core.Input;
using ASH.Core.Layout;
using ASH.Core.Model;
using ASH.Core.Runner;
using ASH.Tests.Layout;
using ASH.Tests.Model;
using ASH.Tests.Runner;
using static ASH.Tests.AutoMod.ModelRunIntegrationTests;

namespace ASH.Tests.AutoMod;

/// <summary>Supply modes in a real AutoMod build and run, on the one-way square loop of ModelRunIntegrationTests.
/// Set ASH_AM_HOME (e.g. C:\AutoMod\12_6); ASH_KEEP_TEMP=1 keeps the model folder.</summary>
public class SupplyModesIntegrationTests
{
    /// <summary>G1 = Cycle with <paramref name="vehicles"/> vehicles 1.2 m long (1 m/s, blank accel/decel: the template default), M001 from L001 (moved up to (10, 8), so the start
    /// CPs fit on the lane into it) to <paramref name="stations"/>, optionally via the buffer B001. The unload threshold is never reached, so no
    /// vehicle is held: on the one-way loop a held vehicle would stop the ones behind it.</summary>
    static Run CycleRun(int vehicles, string[] stations, bool buffer, double unloadTime = 10)
    {
        var p = LogicGeneratorTests.Sample(takt: 20);
        p.AgvTypes[0] = p.AgvTypes[0] with { Length = 1.2, Width = 0.8, UnloadTime = unloadTime };
        p.Groups[0] = p.Groups[0] with { MinCount = vehicles, MaxCount = vehicles, StandbyCPs = new List<string>(), SupplyMode = "Cycle" };
        p.Materials.RemoveAt(1); p.Routes.RemoveAt(1);
        p.Materials[0] = p.Materials[0] with { Material = "M001", UnloadThreshold = 1000 };
        p.Routes[0] = p.Routes[0] with { Material = "M001", SupplyToCP = stations[0], BufferCP = buffer ? "B001" : null };
        if (stations.Length > 1) StationExpansion.Expand(p, new Dictionary<string, List<string>> { ["M001"] = stations.ToList() });
        // the corner block moves from the lane into L001 to the opposite corner
        var run = BuildAndRun(p, new Dictionary<string, int> { ["G1"] = vehicles }, layoutExtra: l =>
        {
            l.Blocks.Clear(); l.Blocks.Add(L.Block("B_x1", -1, -1, 2, 2));
            l.Cps.RemoveAll(c => c.Name == "L001"); l.Cps.Add(L.Cp("L001", 10, 8, "load")); l.Cps.Add(L.Cp("U002", 0, 5, "unload"));
        });
        var logic = File.ReadAllText(Path.Combine(run.Work, "ashrun.arc", "ashrun~.asy"));
        Assert.Contains("begin P_cycle_1 arriving procedure", logic);
        Assert.DoesNotContain("P_supply_", logic);
        Assert.Equal(0, V(run.Rows.Single(x => x[0] == "deadlock"), 1));
        Assert.DoesNotContain(run.Rows, x => x[0] == "dlock");
        var veh = run.Rows.Where(x => x[0] == "vehicle").ToList();
        Assert.Equal(vehicles, veh.Count);
        Assert.All(veh, x => Assert.True(V(x, 3) > 0, x[1] + " laps"));
        Assert.Equal(3600, V(run.Rows.Single(x => x[0] == "clock"), 2), 3);
        return run;
    }

    // value3 = supplies, value9 = WaitAvg, value12 = SwapN, value13 = SwapSum, value14 = SwapMax, value15 = Follow
    static (double N, double Avg, double Max, double Follow) Swaps(string[] m)
    {
        Assert.True(V(m, 3) > 0, m[1] + " supplies");
        Assert.True(V(m, 9) >= 0, m[1] + " supply wait average");
        Assert.True(V(m, 12) > 0, m[1] + " swaps");
        // every departure after warmup is one swap (capacity 1: one per supply); only the last one can still be open at the end. A departure that
        // overwrites a swap still open (a follower ready at the buffer that went uncounted) drops one
        Assert.InRange(V(m, 12), V(m, 3) - 1, V(m, 3));
        double avg = V(m, 13) / V(m, 12);
        Assert.InRange(avg, 0, V(m, 14));
        Assert.InRange(V(m, 15), 0, V(m, 12));
        return (V(m, 12), avg, V(m, 14), V(m, 15));
    }

    [SkippableFact]
    public void Cycle_group_two_stations_builds_and_runs()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        // 3 vehicles take the 2 stations in turn: a station is served by each vehicle in turn, so its swap is a fraction of a lap
        var run = CycleRun(3, new[] { "U001", "U002" }, false);
        double lap = run.Rows.Where(x => x[0] == "vehicle").Average(x => V(x, 4) / V(x, 3));  // window / laps
        foreach (var name in new[] { "M001@U001", "M001@U002" })
        {
            var s = Swaps(Mat(run, name));
            Console.WriteLine($"{name}: swaps {s.N} avg {s.Avg:0.0} s max {s.Max:0.0} s follow {s.Follow}, lap {lap:0.0} s");
            Assert.True(s.Avg < lap, $"{name}: swap {s.Avg:0.0} s vs lap {lap:0.0} s");
        }
        Cleanup(run.Work);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cycle_follower_takes_over_the_station(bool buffer)
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        // 2 vehicles, one station: the second one queues behind the first at the load CP and follows it to the station. Without a buffer it
        // pulls up as the first leaves (a swap within FOLLOW); with one it is ready at the buffer when the first leaves (a 0 s swap)
        var run = CycleRun(2, new[] { "U001" }, buffer, 40);
        var s = Swaps(Mat(run, "M001"));
        Console.WriteLine($"buffer {buffer}: swaps {s.N} avg {s.Avg:0.0} s max {s.Max:0.0} s follow {s.Follow}");
        Assert.True(s.Follow > 0, $"buffer {buffer}: no follower in {s.N} swaps");
        Cleanup(run.Work);
    }

    [SkippableFact]
    public void Call_two_load_cps_builds_and_runs()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        // one call-supplied material that picks up at L001 and L002 in turn (L002 on the lane after L001)
        var p = LogicGeneratorTests.Sample(takt: 20);
        p.Materials.RemoveAt(1); p.Routes.RemoveAt(1);
        p.Routes[0] = p.Routes[0] with { SupplyFromCPs = new[] { "L001", "L002" } };
        var run = BuildAndRun(p, new Dictionary<string, int> { ["G1"] = 2 }, layoutExtra: l => l.Cps.Add(L.Cp("L002", 10, 8, "load")));
        var logic = File.ReadAllText(Path.Combine(run.Work, "ashrun.arc", "ashrun~.asy"));
        Assert.Contains("V_fromRot", logic);
        Assert.Equal(0, V(run.Rows.Single(x => x[0] == "deadlock"), 1));
        var m = Mat(run, "M1");
        Console.WriteLine($"M1: supplies {V(m, 3)}");
        Assert.True(V(m, 3) > 0, "M1 supplies");
        Cleanup(run.Work);
    }

    sealed class Collect(List<RunOutcome> into) : IProgress<RunOutcome> { public void Report(RunOutcome v) { lock (into) into.Add(v); } }

    [SkippableFact]
    public void Cycle_group_runs_a_variant_model_per_count()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        // the source files as AGVMODEL leaves them, G_EX a Cycle group of 1..3 vehicles, and its base model built
        var dir = ModelVariantsTests.CycleSource(ModelVariantsTests.Loop());
        var made = SourceBuild.Run(dir, "Agv", null, new List<string>());
        Assert.DoesNotContain(made.Findings, f => f.Code == FindingCode.InputError);
        ModelBuilder.PrepareBuildDir(dir, "Agv");
        var b = AmBuilder.Build(AmHome!, dir, "Agv", TimeSpan.FromMinutes(10));
        Assert.True(b.Success, b.Facts + "\n" + b.MakeErrText + b.Am2ErrText);
        ProjectInput input;
        using (var wb = CellDump.Load(Path.Combine(dir, CellDump.FileName))) input = ExcelReader.Read(wb, out _);

        // 2 and 3 vehicles at once in two slots, as the search runs them: each count gets its own model, built on first use
        var seen = new List<RunOutcome>();
        var mx = new ModelExecutor(new ExecutorOptions(dir, "Agv", 2, TimeSpan.FromMinutes(5), "Student Version", AmHome, Progress: new Collect(seen)), input);
        new ParallelExecutor(2, mx.Run).ExecuteMany(new[] { 2, 3 }.Select(n => new RunScenario(1, new Dictionary<string, int> { ["G_EX"] = n })).ToList());
        Assert.True(mx.Failures == 0, dir + "\n" + string.Join("\n", mx.FailureMessages));
        foreach (var n in new[] { 2, 3 })
        {
            Assert.True(File.Exists(Path.Combine(dir, $"Agv_c{n}.exe")), $"Agv_c{n}.exe");
            var o = seen.Single(x => x.RunId == $"n{n}_s1");
            Assert.True(o.Success, o.Error);
            Assert.True(File.Exists(Path.Combine(o.RunDir, $"Agv_c{n}.report")));
            var veh = File.ReadAllLines(o.ResultTsvPath!).Count(l => l.StartsWith("vehicle\t"));
            Console.WriteLine($"{n} vehicles: {veh} vehicle rows, run {o.Duration.TotalSeconds:0.0} s");
            Assert.Equal(n, veh); // only this count's vehicles: none left idle on the lane
        }
        Assert.False(File.Exists(Path.Combine(dir, "Agv_c1.exe")));
        Cleanup(dir);
    }
}
