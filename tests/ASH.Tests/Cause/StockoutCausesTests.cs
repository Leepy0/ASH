using System.Linq;
using ASH.Core.Cause;
using ASH.Core.Geometry;
using ASH.Core.Input;
using ASH.Core.Layout;
using ASH.Core.Runner;
using ASH.Tests.Layout;

namespace ASH.Tests.Cause;

/// <summary>Corridor 0..100 m: standby S at 0, load A at 10, unload B at 50, block B1 over 20..30 on the way, B9 off it. Vehicle 1 m/s loaded, 2 m/s empty,
/// load and unload 10 s: empty run 5 s, free loaded leg 10 + 40 + 10 = 60 s, minimum lead 65 s. One container lasts 60 s (takt 60, 1 per cycle, 1 per container).</summary>
public class StockoutCausesTests
{
    static ProjectInput Input(double call)
    {
        var p = new ProjectInput();
        p.AgvTypes.Add(new AgvType("T1", 1, 2, 0.5, 0.5, 1, 10, 10, 0, 0, 1.5, 1, ""));
        p.Groups.Add(new AgvGroup("G1", "T1", 1, 3, new List<string> { "S" }, "Nearest"));
        p.Materials.Add(new MaterialInfo("M1", "ST1", "G1", 60, 1, 1, call, 1, null, null, false));
        p.Routes.Add(new Route("M1", "A", "B", null, null, null, 0));
        return p;
    }

    static ASH.Core.Layout.Layout Corridor()
    {
        var line = L.Line("P1", 0, 0, 100, 0); line.IsTwoWay = true;
        var l = L.Of(line);
        foreach (var (n, x) in new[] { ("S", 0.0), ("A", 10), ("B", 50) }) l.Cps.Add(L.Cp(n, x, 0));
        l.Blocks.Add(new LayoutBlock { Name = "B1", Cap = 1, Rect = new Rect2(new Point2(20, -1), new Point2(30, 1)) });
        l.Blocks.Add(new LayoutBlock { Name = "B9", Cap = 1, Rect = new Rect2(new Point2(80, -1), new Point2(90, 1)) });
        return l;
    }

    static ResultSet Run(double stop, MaterialStat? st = null, GroupTime? gt = null, double idleShare = double.NaN, IReadOnlyList<BlockResult>? blocks = null,
        IReadOnlyList<DeadlockEvent>? dl = null)
    {
        var veh = double.IsNaN(idleShare) ? new List<VehicleResult>()
            : new List<VehicleResult> { new("V1", "G1", 0, 0, 10, 1000, 1, 0, new TimeSplit(1000, 1000 * (1 - idleShare), 0, 0, 0, 0, 0)),
                                        new("V9", "G1", 0, 0, 10, 1000, 9, 0, new TimeSplit(1000, 1000, 0, 0, 0, 0, 0)) }; // not active
        return new ResultSet(new List<MaterialResult> { new("M1", "G1", 0, 2, 50, 60, stop, 50), new("M2", "G1", 1, 3, 10, 10, 0, 10) },
            new List<GroupResult> { new("G1", 1) }, blocks ?? new List<BlockResult>(), dl?.Count ?? 0, 3600, new List<ResultRow>(), veh,
            st == null ? new List<MaterialStat>() : new List<MaterialStat> { st }, null, dl, gt == null ? null : new List<GroupTime> { gt });
    }

    static MaterialStat Stat(double pickAvg, double delivAvg, double delivMax) => new("M1", "G1", pickAvg, pickAvg * 2, delivAvg, delivMax, 2, 1, 0, 4);

    static StockoutCheck One(double call, ResultSet r) => Assert.Single(StockoutCauses.Check(new CauseContext(Corridor(), Input(call), r)));

    [Fact]
    public void Structural_When_The_Stock_At_The_Call_Lasts_Less_Than_The_Fastest_Delivery()
    {
        var k = One(1, Run(700, Stat(5, 66, 90)));
        Assert.Equal((60.0, 60.0, 5.0, 60.0, 65.0), (k.ContainerSec, k.CoverageSec, k.EmptyRunSec, k.FreeLegSec, k.MinLeadSec));
        Assert.Equal((StockoutCauses.Structural, 3), (k.Verdict, k.SuggestCall)); // ceil(65 / 60) + 1
        Assert.Equal("CallThreshold 1→3 · 대수 무관", k.Fix);
        var f = Assert.Single(StockoutCauses.Find(new CauseContext(Corridor(), Input(1), Run(700, Stat(5, 66, 90)))));
        Assert.Equal((CauseKinds.Stockout, 2, "M1 자재 품절 · 구조적", "A→B"), (f.Kind, f.Severity, f.Title, f.Where));
        Assert.StartsWith("호출 시 남은 1:00 · 최소 리드 1:05 · 리드 평균 1:06 / 최대 1:30", f.Detail);
        Assert.EndsWith("품절 11:40 · 자료 부족: tsplit·차량 시간", f.Detail);
        Assert.Equal(new[] { "A", "B", "S", "B1" }, f.Refs); // B9 is off the route
        Assert.Equal((50.0, 0.0), (f.X, f.Y)); // the unload CP
    }

    [Fact]
    public void Dispatch_Wait_With_Idle_Vehicles_Points_At_Standby_And_Rule()
    {
        // call 3 = 180 s left; pickup takes 100 s for a 5 s empty run while the vehicle stood idle 60 %
        var k = One(3, Run(120, Stat(100, 170, 300), idleShare: 0.6));
        Assert.Equal((95.0, 10.0), (k.DispatchWaitSec, k.TrafficSec));
        Assert.Equal(0.6, k.IdleShare, 9); // the inactive V9 is left out
        Assert.Equal(StockoutCauses.Dispatch, k.Verdict);
        Assert.Equal("대기 위치 A 쪽 · 배차 규칙 Nearest 점검 · 호출 몰림", k.Fix);
        // busy vehicles: too few at the burst
        Assert.Equal("G1 대수 +1 · 호출 몰림", One(3, Run(120, Stat(100, 170, 300), idleShare: 0.1)).Fix);
        Assert.Equal(1, StockoutCauses.Find(new CauseContext(Corridor(), Input(3), Run(120, Stat(100, 170, 300)))).Single().Severity);
    }

    [Fact]
    public void Traffic_Names_The_Block_On_The_Route_That_Waited_Most()
    {
        // loaded leg 140 s against 60 s free; the group stood 40 % of its job time; B1 20 claims x 15 s over 10 jobs = 30 s a job; B9 waits longer but is off the route
        var gt = new GroupTime("G1", 10, 2000, 1000, 800, 200, 0, new Dictionary<string, int> { ["B1"] = 20, ["B9"] = 20 });
        var blocks = new List<BlockResult> { new("B1", 40, 15, 40, 0.5), new("B9", 40, 90, 120, 0.9) };
        var k = One(3, Run(30, Stat(10, 150, 170), gt, 0.5, blocks));
        Assert.Equal((5.0, 80.0, 0.4), (k.DispatchWaitSec, k.TrafficSec, k.StopShare));
        Assert.Equal(StockoutCauses.Traffic, k.Verdict);
        Assert.Equal("B1 block 점검 · 대기 30초", k.Fix);
        Assert.Equal(new[] { new KeyValuePair<string, double>("B1", 30) }, k.RouteBlocks);
        Assert.Empty(k.Missing);
        Assert.Equal(0, StockoutCauses.Find(new CauseContext(Corridor(), Input(3), Run(30, Stat(10, 150, 170), gt, 0.5, blocks))).Single().Severity);

        // a deadlock of the group's vehicle counts even when the averages look fine
        var dl = new List<DeadlockEvent> { new(500, new List<DeadlockLink> { new("V1", "G1", "B1", new List<string> { "B9" }, double.NaN, double.NaN) }) };
        var d = One(3, Run(30, Stat(10, 75, 100), gt, 0.5, blocks, dl));
        Assert.Equal((StockoutCauses.Traffic, 1), (d.Verdict, d.Deadlocks));
        Assert.StartsWith("교착 1회 확인 · B1 block 점검", d.Fix);
    }

    [Fact]
    public void Variability_When_Only_The_Slowest_Delivery_Is_Late()
    {
        // average 75 s < 180 s left <= slowest 240 s: one container more covers the 60 s gap
        var k = One(3, Run(40, Stat(8, 75, 240)));
        Assert.Equal(StockoutCauses.Variability, k.Verdict);
        Assert.Equal(("CallThreshold 3→4 또는 안전재고 +1", 4), (k.Fix, k.SuggestCall));
        // call 2 = 120 s left: the average 123 s is late with 29 s dispatch wait and 29 s traffic, neither large: thin margin, cover the slowest 200 s
        var t = One(2, Run(40, Stat(34, 123, 200)));
        Assert.Equal((StockoutCauses.Thin, "CallThreshold 2→5"), (t.Verdict, t.Fix));
    }

    [Fact]
    public void Missing_Rows_Classify_With_What_Is_There_And_Never_Throw()
    {
        // no matstat, tsplit or vehicle split: the structural floor still decides
        var s = One(1, Run(100));
        Assert.Equal(StockoutCauses.Structural, s.Verdict);
        Assert.Equal(new[] { "matstat", "tsplit", "차량 시간" }, s.Missing);
        Assert.True(double.IsNaN(s.DelivAvg) && double.IsNaN(s.DispatchWaitSec));
        // nothing to go on beyond the floor
        var n = One(3, Run(100));
        Assert.Equal((StockoutCauses.NoData, ""), (n.Verdict, n.Fix));
        Assert.Contains("자료 부족: matstat·tsplit·차량 시간", StockoutCauses.Detail(n));
        // no layout, no input: everything unknown, still one row
        var e = Assert.Single(StockoutCauses.Check(new CauseContext(new ASH.Core.Layout.Layout(), new ProjectInput(), Run(100, Stat(10, 50, 60)))));
        Assert.Equal(StockoutCauses.NoData, e.Verdict);
        Assert.Contains("입력", e.Missing); Assert.Contains("경로 길이", e.Missing);
        Assert.Equal("M1", e.Where);
        // no stop, no result: nothing
        Assert.Empty(StockoutCauses.Check(new CauseContext(Corridor(), Input(1), Run(0, Stat(5, 66, 90)))));
        Assert.Empty(StockoutCauses.Find(new CauseContext(Corridor(), Input(1))));
    }

    /// <summary>G1 as a Cycle group: Materials row M1 split into stations M1@B (unload B at 50) and M1@C (unload C at 70), both loaded at A, takt
    /// <paramref name="takt"/> each (1 container a cycle). Free lap of M1@B = 10 + 10 + 40 m / 1 + 40 m back to A / 2 = 80 s.</summary>
    static ProjectInput CycleInput(double takt, int max)
    {
        var p = new ProjectInput();
        p.AgvTypes.Add(new AgvType("T1", 1, 2, 0.5, 0.5, 1, 10, 10, 0, 0, 1.5, 1, ""));
        p.Groups.Add(new AgvGroup("G1", "T1", 1, max, new List<string>(), "Nearest", SupplyMode: "Cycle"));
        foreach (var cp in new[] { "B", "C" })
        {
            p.Materials.Add(new MaterialInfo("M1@" + cp, "ST" + cp, "G1", takt, 1, 1, 0, 1, null, null, false, Source: "M1"));
            p.Routes.Add(new Route("M1@" + cp, "A", cp, null, null, null, 0));
        }
        return p;
    }

    static ASH.Core.Layout.Layout CycleCorridor() { var l = Corridor(); l.Cps.Add(L.Cp("C", 70, 0)); return l; }

    /// <summary>M1@B stood <paramref name="stop"/> s; <paramref name="active"/> vehicles each 1000 s window over <paramref name="jobs"/> laps (none at 0), idle 20 %.</summary>
    static ResultSet CycleRun(double stop, int active, int jobs, double waitC = 0, double waitB = 0.2)
    {
        var veh = jobs == 0 ? new List<VehicleResult>()
            : Enumerable.Range(1, active).Select(i => new VehicleResult("V" + i, "G1", 800, 0, jobs, 1000, i, 0, new TimeSplit(1000, 800, 0, 0, 0, 0, 0))).ToList();
        return new ResultSet(new List<MaterialResult> { new("M1@B", "G1", 0, 1, 10, 10, stop, 10, waitB), new("M1@C", "G1", 0, 1, 10, 10, 0, 10, waitC) },
            new List<GroupResult> { new("G1", active) }, new List<BlockResult>(), 0, 3600, new List<ResultRow>(), veh, new List<MaterialStat>());
    }

    static StockoutCheck Cycle(ProjectInput p, ResultSet r) => Assert.Single(StockoutCauses.Check(new CauseContext(CycleCorridor(), p, r)));

    static void NoCallHint(StockoutCheck k)
    {
        Assert.Null(k.SuggestCall);
        Assert.DoesNotContain("CallThreshold", k.Fix); Assert.DoesNotContain("호출 기준", k.Fix);
    }

    [Fact]
    public void Cycle_Structural_When_Even_Max_Vehicles_Lap_Too_Slowly()
    {
        // 3 vehicles x 1 / 80 s = 0.0375 a second < 2 stations x 1 / 40 s = 0.05
        var k = Cycle(CycleInput(40, 3), CycleRun(300, 2, 10));
        Assert.Equal((StockoutCauses.Structural, 80.0), (k.Verdict, k.FreeLapSec));
        NoCallHint(k);
        var f = Assert.Single(StockoutCauses.Find(new CauseContext(CycleCorridor(), CycleInput(40, 3), CycleRun(300, 2, 10))));
        Assert.Equal(("M1@B 자재 품절 · 구조적", "B", 50.0), (f.Title, f.Where, f.X)); // the station's unload CP
    }

    [Fact]
    public void Cycle_Fleet_Names_The_Vehicles_That_Would_Do()
    {
        // 2 stations x 1 / 60 s = 1/30 a second: 4 vehicles give 0.05, the 2 active only 0.025; ceil(80 / 30) = 3 do
        var k = Cycle(CycleInput(60, 4), CycleRun(300, 2, 10));
        Assert.Equal(StockoutCauses.Fleet, k.Verdict);
        Assert.Equal("G1 대수 3대 이상", k.Fix);
        NoCallHint(k);
        Assert.Equal("자유 바퀴 1:20 · 실제 바퀴 1:40 · 공급 90·소비 120 · 대수 2/4 · 공급 대기 0.2 · 유휴 20% · 품절 5:00 · 자료 부족: tsplit", StockoutCauses.Detail(k));
    }

    [Fact]
    public void Cycle_Traffic_When_The_Actual_Lap_Is_Inflated()
    {
        // enough vehicles (1 / 80 s >= 2 / 200 s) but a lap takes 1000 / 5 = 200 s against 80 s free
        var k = Cycle(CycleInput(200, 3), CycleRun(300, 1, 5));
        Assert.Equal((StockoutCauses.Traffic, 200.0), (k.Verdict, k.LapSec));
        Assert.Equal("경로 정체 점검", k.Fix);
        NoCallHint(k);
    }

    [Fact]
    public void Cycle_Turn_When_Vehicles_Are_Held_At_The_Other_Station()
    {
        // lap 100 s is within 1.5 x 80 s; M1@C keeps 1.5 containers waiting on vehicles
        var k = Cycle(CycleInput(200, 3), CycleRun(300, 1, 10, waitC: 1.5));
        Assert.Equal(StockoutCauses.Turn, k.Verdict);
        Assert.Equal("M1@C UnloadThreshold 낮춤 · 공정 분리", k.Fix);
        NoCallHint(k);
        // nothing held elsewhere: no class applies
        Assert.Equal(StockoutCauses.Other, Cycle(CycleInput(200, 3), CycleRun(300, 1, 10, waitC: 0.5)).Verdict);
    }

    [Fact]
    public void Cycle_Without_Vehicle_Rows_Is_No_Data()
    {
        var k = Cycle(CycleInput(200, 3), CycleRun(300, 1, 0));
        Assert.Contains("차량 시간", k.Missing);
        Assert.Equal((StockoutCauses.NoData, ""), (k.Verdict, k.Fix));
        Assert.True(double.IsNaN(k.LapSec));
    }

    [Fact]
    public void Cause_Analysis_Lists_Them_After_Deadlocks_And_Ms_Formats()
    {
        var all = CauseAnalysis.All(new CauseContext(Corridor(), Input(1), Run(700, Stat(5, 66, 90))));
        Assert.Contains(all, i => i.Kind == CauseKinds.Stockout);
        Assert.Equal(1, Array.IndexOf(CauseKinds.Order, CauseKinds.Stockout));
        Assert.Equal(("0:05", "1:05", "1:00:00", "-"), (StockoutCauses.Ms(5), StockoutCauses.Ms(65), StockoutCauses.Ms(3600), StockoutCauses.Ms(double.NaN)));
    }

    [Fact]
    public void Two_Load_Cps_Average_The_Free_Lead_Of_Each()
    {
        // A2 at 30: A→B 40 m and A2→B 20 m average 30 s loaded; standby S→A 10 m and S→A2 30 m at 2 m/s average 10 s
        var l = Corridor(); l.Cps.Add(L.Cp("A2", 30, 0));
        var p = Input(1); p.Routes[0] = p.Routes[0] with { SupplyFromCPs = new[] { "A", "A2" } };
        var k = Assert.Single(StockoutCauses.Check(new CauseContext(l, p, Run(700, Stat(5, 66, 90)))));
        Assert.Equal((10.0, 50.0, 60.0), (k.EmptyRunSec, k.FreeLegSec, k.MinLeadSec));
    }

    [Fact]
    public void Cycle_Two_Load_Cps_Average_The_Free_Lap_Of_Each()
    {
        // M1@B from A: 20 + 40 + 40 / 2 = 80 s; from A2 (30): 20 + 20 + 20 / 2 = 50 s; average 65 s
        var l = CycleCorridor(); l.Cps.Add(L.Cp("A2", 30, 0));
        var p = CycleInput(40, 3);
        for (int i = 0; i < p.Routes.Count; i++) p.Routes[i] = p.Routes[i] with { SupplyFromCPs = new[] { "A", "A2" } };
        var k = Assert.Single(StockoutCauses.Check(new CauseContext(l, p, CycleRun(300, 2, 10))));
        Assert.Equal(65.0, k.FreeLapSec, 6);
    }
}
