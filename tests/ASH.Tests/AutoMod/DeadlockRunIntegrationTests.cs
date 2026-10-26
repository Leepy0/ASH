using System.Globalization;
using ASH.Core.Cause;
using ASH.Core.Export;
using ASH.Core.Model;
using ASH.Core.Runner;
using ASH.Tests.Layout;
using ASH.Tests.Model;

namespace ASH.Tests.AutoMod;

/// <summary>A model that locks up: the 10 m one-way square loop of ModelRunIntegrationTests covered by two adjacent blocks (left half BL, right half BR),
/// no room between them, three vehicles. A vehicle heading for U001 waits for BL while the one holding BL (its tail still in it) queues behind it.
/// Set ASH_AM_HOME (e.g. C:\AutoMod\12_6); ASH_KEEP_TEMP=1 keeps the model folder.</summary>
public class DeadlockRunIntegrationTests
{
    static string? AmHome => Environment.GetEnvironmentVariable("ASH_AM_HOME");
    const string Model = "dlock";

    [SkippableFact]
    public void Two_Blocks_In_A_Ring_Log_One_Episode_With_Who_Waits_For_Whom()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var input = LogicGeneratorTests.Sample();
        var l = L.Of(L.Line("p1", 0, 0, 10, 0), L.Line("p2", 10, 0, 10, 10), L.Line("p3", 10, 10, 0, 10), L.Line("p4", 0, 10, 0, 0));
        l.Cps.Add(L.Cp("S001", 5, 0, "standby", "G1"));
        l.Cps.Add(L.Cp("L001", 10, 5, "load"));
        l.Cps.Add(L.Cp("B001", 6, 10, "buffer"));
        l.Cps.Add(L.Cp("U001", 2, 10, "unload"));
        l.Blocks.Add(L.Block("BL", -1, -1, 4.5, 11));
        l.Blocks.Add(L.Block("BR", 4.5, -1, 11, 11));
        var work = Path.Combine(Path.GetTempPath(), "ash_dlock_" + Guid.NewGuid().ToString("N"));
        var res = ModelBuilder.Build(new ModelBuildRequest(work, Model, input, l, new ExportOptions(Units: "Meters", Groups: ModelBuilder.VehicleGroups(input))), AmHome!, TimeSpan.FromMinutes(5));
        Assert.True(res.Build?.Success == true, work + "\n" + res.Build?.MakeErrText + res.Build?.Am2ErrText);
        var o = ModelRunner.Run(new RunRequest(work, Model, input, new RunScenario(1, new Dictionary<string, int> { ["G1"] = 3 }, Events: true), TimeSpan.FromMinutes(3), "Student Version"));
        var msg = Path.Combine(o.RunDir, Model + ".message");
        Assert.True(o.Success, work + "\n" + o.Error + "\n" + (File.Exists(msg) ? File.ReadAllText(msg) : ""));
        var tsv = Path.Combine(o.RunDir, "result.tsv");
        Console.WriteLine(work + "\n" + string.Join("\n", File.ReadAllLines(tsv).Where(x => x.StartsWith("dlock") || x.StartsWith("deadlock") || x.StartsWith("block"))));

        var rs = ResultParser.Parse(tsv);
        var ev = Assert.Single(rs.DeadlockEvents!);
        Assert.True(rs.Deadlocks > 0, "the leg counter saw the lock too");
        Assert.InRange(ev.AtSec, 10, rs.SimSec - 600); // stuck legs pass DeadlockSec (600 s) only when the lock came early enough
        Assert.Equal(0, ev.AtSec % 10, 6);              // the watchdog looks every 10 s
        // all three vehicles are in it; seen 2026-10-27: Tug2 at B001 waits for BL, held by Tug1 at S001 (its tail), which waits for Tug3 at L001,
        // which waits for Tug2 at B001 (CP claims): a block wait closed by CP waits
        Assert.Equal(3, ev.Links.Count);
        var at = l.Cps.ToDictionary(c => c.Name, c => c.Position);
        foreach (var k in ev.Links)
        {
            Assert.StartsWith("Tug", k.Vehicle);
            Assert.Equal("G1", k.Group);
            Assert.True(at.Values.Any(p => Math.Abs(p.X - k.X) < 1e-6 && Math.Abs(p.Y - k.Y) < 1e-6), $"{k.Vehicle} at {k.X},{k.Y}: not a CP"); // the last CP passed, metres
            if (k.WaitsFor is "BL" or "BR")
                Assert.Contains(ev.Links, x => x != k && x.Holds.Contains(k.WaitsFor));             // someone in the cycle holds it
            else
                Assert.Contains(ev.Links, x => x != k && at[k.WaitsFor].X == x.X && at[k.WaitsFor].Y == x.Y); // someone in the cycle stands at that CP
        }
        Assert.Contains(ev.Links, k => k.WaitsFor is "BL" or "BR");
        // the lock is for good and comes right after the last move: no leg of any vehicle ends after it (events.tsv "wait" rows are leg ends)
        var legEnds = File.ReadAllLines(Path.Combine(o.RunDir, "events.tsv")).Skip(1).Select(x => x.Split('\t')).Where(x => x[1] == "wait")
            .Select(x => double.Parse(x[0], CultureInfo.InvariantCulture)).ToList();
        Console.WriteLine($"episode at {ev.AtSec} s, last leg end {(legEnds.Count == 0 ? double.NaN : legEnds.Max())} s, legs over DeadlockSec {rs.Deadlocks}");
        Assert.NotEmpty(legEnds);
        Assert.InRange(legEnds.Max(), ev.AtSec - 60, ev.AtSec);
        if (Environment.GetEnvironmentVariable("ASH_KEEP_TEMP") != "1") Directory.Delete(work, true);
    }
}
