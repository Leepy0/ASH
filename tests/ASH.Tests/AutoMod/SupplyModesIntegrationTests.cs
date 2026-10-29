using ASH.Core.Input;
using ASH.Tests.Layout;
using ASH.Tests.Model;
using static ASH.Tests.AutoMod.ModelRunIntegrationTests;

namespace ASH.Tests.AutoMod;

/// <summary>Supply modes in a real AutoMod build and run, on the one-way square loop of ModelRunIntegrationTests.
/// Set ASH_AM_HOME (e.g. C:\AutoMod\12_6); ASH_KEEP_TEMP=1 keeps the model folder.</summary>
public class SupplyModesIntegrationTests
{
    [SkippableFact]
    public void Cycle_group_two_stations_builds_and_runs()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        // G1 = Cycle, 2 vehicles 1.2 m long; M001 from L001 to the stations U001 (top side) and U002 (left side). The unload threshold is
        // never reached, so no vehicle is held: on the one-way loop a held vehicle would stop the one behind it
        var p = LogicGeneratorTests.Sample(takt: 20);
        p.AgvTypes[0] = p.AgvTypes[0] with { Length = 1.2, Width = 0.8 };
        p.Groups[0] = p.Groups[0] with { MinCount = 2, MaxCount = 2, StandbyCPs = new List<string>(), SupplyMode = "Cycle" };
        p.Materials.RemoveAt(1); p.Routes.RemoveAt(1);
        p.Materials[0] = p.Materials[0] with { Material = "M001", UnloadThreshold = 50 };
        p.Routes[0] = p.Routes[0] with { Material = "M001", BufferCP = null };
        StationExpansion.Expand(p, new Dictionary<string, List<string>> { ["M001"] = new() { "U001", "U002" } });
        // the start CPs go on the lane into L001: the corner block moves from there to the opposite corner
        var run = BuildAndRun(p, new Dictionary<string, int> { ["G1"] = 2 },
            layoutExtra: l => { l.Blocks.Clear(); l.Blocks.Add(L.Block("B_x1", -1, -1, 2, 2)); l.Cps.Add(L.Cp("U002", 0, 5, "unload")); });

        var logic = File.ReadAllText(Path.Combine(run.Work, "ashrun.arc", "ashrun~.asy"));
        Assert.Contains("begin P_cycle_1 arriving procedure", logic);
        Assert.DoesNotContain("P_supply_", logic);
        foreach (var name in new[] { "M001@U001", "M001@U002" })
        {
            var m = Mat(run, name);
            Assert.True(V(m, 3) > 0, name + " supplies");
            Assert.True(V(m, 12) > 0, name + " swaps");                 // value12 = SwapN
            Assert.True(V(m, 9) >= 0, name + " supply wait average");   // value9 = WaitAvg
            Assert.InRange(V(m, 13) / V(m, 12), 0, V(m, 14));            // swap average within 0..max
        }
        Assert.Equal(0, V(run.Rows.Single(x => x[0] == "deadlock"), 1));
        Assert.DoesNotContain(run.Rows, x => x[0] == "dlock");
        var veh = run.Rows.Where(x => x[0] == "vehicle").ToList();
        Assert.Equal(2, veh.Count);
        Assert.All(veh, x => Assert.True(V(x, 3) > 0, x[1] + " laps"));
        Assert.Equal(3600, V(run.Rows.Single(x => x[0] == "clock"), 2), 3);
        Cleanup(run.Work);
    }
}
