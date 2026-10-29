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

    [Fact]
    public void Cause_Analysis_Lists_Them_After_Deadlocks_And_Ms_Formats()
    {
        var all = CauseAnalysis.All(new CauseContext(Corridor(), Input(1), Run(700, Stat(5, 66, 90))));
        Assert.Contains(all, i => i.Kind == CauseKinds.Stockout);
        Assert.Equal(1, Array.IndexOf(CauseKinds.Order, CauseKinds.Stockout));
        Assert.Equal(("0:05", "1:05", "1:00:00", "-"), (StockoutCauses.Ms(5), StockoutCauses.Ms(65), StockoutCauses.Ms(3600), StockoutCauses.Ms(double.NaN)));
    }
}
