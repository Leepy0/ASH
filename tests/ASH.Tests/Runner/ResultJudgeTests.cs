using ASH.Core.Input;
using ASH.Core.Runner;
using ASH.Tests.Model;

namespace ASH.Tests.Runner;

public class ResultJudgeTests
{
    static string Fx(string n) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "runner", n);
    static RunKpi K(double util = 0.5, double stock = 1.5, int sup = 10, int exp = 10, int dead = 0) => new("G1", 1, util, stock, sup, exp, 0, dead);
    static readonly ASH.Core.Input.Settings S = new() { UtilMax = 0.75 };

    [Fact]
    public void Parse_Result_Tsv_Rows()
    {
        var r = ResultParser.Parse(Fx("result.tsv"));
        Assert.Equal(2, r.Materials.Count);
        var m1 = r.Materials[0];
        Assert.Equal(("M1", "G1", 1.8, 3, 5.4, 0.0), (m1.Name, m1.Group, m1.MinStock, m1.Supplies, m1.Expected, m1.StopTimeSec));
        Assert.Equal(2.636364, m1.AvgStock, 6);
        Assert.Equal(new[] { ("G1", 1), ("G2", 1) }, r.Groups.Select(g => (g.Group, g.ActiveCount)));
        Assert.Equal(("B_x1", 6.0), (r.Blocks[0].Name, r.Blocks[0].Passes));
        Assert.Equal(0.488639, r.Blocks[0].Utilization, 6);
        Assert.Equal(0, r.Deadlocks);
        Assert.Equal(3600, r.SimSec);
    }

    [Fact]
    public void ParseReport_Extracts_Vehicle_State_Ratios()
    {
        var v = ResultParser.ParseReport(Fx("ashrun.report"));
        Assert.Equal(new[] { "Tug", "Tug2" }, v.Types.Keys.OrderBy(x => x));
        var tug = v.Types["Tug"];
        Assert.Equal(3, tug.Vehicles.Count);
        Assert.Equal(0.030, tug.Vehicles[0].DeliveringPct, 6);
        Assert.Equal(6, tug.Vehicles[0].DeliveringTrips);
        Assert.Equal(0.025, tug.Vehicles[0].RetrievingPct, 6);
        Assert.Equal(0.918, tug.Vehicles[0].ParkingPct, 6);
        Assert.Equal(0.634, tug.All!.ParkingPct, 6);
        Assert.Equal(2, v.Types["Tug2"].Vehicles.Count);
        Assert.Equal(0.489, v.Blocks.Single(b => b.Name == "B_x1").Util, 6);
        Assert.NotEmpty(v.RawLines);
    }

    [Fact]
    public void Kpi_FromRun_Uses_Group_Type_And_Active()
    {
        var input = LogicGeneratorTests.TwoGroups();
        var k = Kpi.FromRun(ResultParser.Parse(Fx("result.tsv")), ResultParser.ParseReport(Fx("ashrun.report")), input);
        var g1 = k.Single(x => x.Group == "G1");
        Assert.Equal(1, g1.Active);
        Assert.Equal(0.030 + 0.025, g1.Utilization, 6); // active 1: one vehicle's delivering+retrieving share
        Assert.Equal(3, g1.Supplies);
        Assert.InRange(g1.MinStockRatio, 0.1, 100);
    }

    [Fact] public void Judge_Fails_When_Stock_Below_Safety() => Assert.Contains("재고", Judge.Evaluate(new[] { K(stock: 0.5), K(stock: 1.2) }, S).Reason);
    [Fact] public void Judge_Fails_When_Util_Over_Max() { var v = Judge.Evaluate(new[] { K(util: 0.9) }, S); Assert.False(v.Ok); Assert.Contains("가동률", v.Reason); }
    [Fact] public void Judge_Fails_On_Supply_Shortfall_Then_Deadlock()
    {
        Assert.Contains("공급", Judge.Evaluate(new[] { K(sup: 9, exp: 10) }, S).Reason);
        Assert.Contains("교착", Judge.Evaluate(new[] { K(), K(dead: 1) }, S).Reason);
    }
    [Fact] public void Judge_Passes_Good_Run() => Assert.True(Judge.Evaluate(new[] { K(), K(util: 0.6, stock: 1.0, sup: 10) }, S).Ok);

    [Fact]
    public void Summary_Appends_And_Reads_Back()
    {
        var p = Path.Combine(Path.GetTempPath(), "ash_sum_" + Guid.NewGuid().ToString("N"), "summary.tsv");
        var a = new SummaryRow("n1_s1", "G1", 1, 1, true, "", 0.55, 1.8, 3, 5, 0, 0, 1.25);
        var b = a with { RunId = "n2_s1", Ok = false, Reason = "재고\t부족", Utilization = 0.123456 };
        SummaryTsv.Append(p, a); SummaryTsv.Append(p, b);
        Assert.Equal(3, File.ReadAllLines(p).Length); // header once
        var rows = SummaryTsv.ReadAll(p);
        Assert.Equal(a, rows[0]);
        Assert.Equal(b with { Reason = "재고 부족" }, rows[1]);
        Directory.Delete(Path.GetDirectoryName(p)!, true);
    }
}
