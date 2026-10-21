using ASH.Core.Input;
using ASH.Core.Runner;
using ASH.Tests.Model;

namespace ASH.Tests.Runner;

public class ResultJudgeTests
{
    static string Fx(string n) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "runner", n);
    static RunKpi K(double util = 0.5, double stock = 1.5, int sup = 10, int exp = 10, int dead = 0, double stop = 0, double con = 10) => new("G1", 1, util, stock, sup, con, exp, stop, dead);
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
        Assert.Contains("생산 미달", Judge.Evaluate(new[] { K(con: 9, exp: 10) }, S).Reason);
        Assert.Contains("교착", Judge.Evaluate(new[] { K(), K(dead: 1) }, S).Reason);
    }
    [Fact]
    public void Judge_Passes_When_Consumption_Meets_Theory_Despite_Low_Supplies()
    {
        // initial stock covers part of the run: 3 deliveries for 5.4 containers consumed, as in the 1 h sample model
        var v = Judge.Evaluate(new[] { K(sup: 3, exp: 5, con: 5) }, S);
        Assert.True(v.Ok, v.Reason);
        Assert.Equal(RuleCode.Supply, Judge.Evaluate(new[] { K(sup: 10, exp: 10, con: 9.4) }, S).Rule);
    }

    [Fact]
    public void Material_Row_Value6_Is_Consumed()
    {
        var r = ResultParser.Parse(W("kind\tname\tgroup\tvalue1\tvalue2\tvalue3\tvalue4\tvalue5\tvalue6\nmaterial\tM1\tG1\t2\t3\t4\t5.4\t0\t5.4\nclock\tclock\t\t0\t3600\n"));
        Assert.Equal(5.4, r.Materials[0].Consumed);
        var input = LogicGeneratorTests.Sample(); input.Materials.RemoveAt(1);
        var k = Kpi.FromRun(r with { Groups = new[] { new GroupResult("G1", 1) } }, null, input).Single();
        Assert.Equal((4, 5.4), (k.Supplies, k.Consumed));
    }

    [Fact]
    public void Judge_Fails_On_Stop_Time_Even_If_Ratio_Ok()
    {
        var v = Judge.Evaluate(new[] { K(stock: 5, stop: 120) }, S);
        Assert.False(v.Ok); Assert.Equal(RuleCode.StopTime, v.Rule); Assert.Contains("공정 정지 120", v.Reason);
    }

    static string W(string body, string ext = ".tsv") { var p = Path.Combine(Path.GetTempPath(), "ash_rt_" + Guid.NewGuid().ToString("N") + ext); File.WriteAllText(p, body); return p; }

    [Fact]
    public void Parse_Rejects_Empty_Short_Rows_And_Missing_Clock()
    {
        const string H = "kind\tname\tgroup\tvalue1\n";
        Assert.Contains("empty", Assert.Throws<InvalidDataException>(() => ResultParser.Parse(W(""))).Message);
        Assert.Contains("line 3", Assert.Throws<InvalidDataException>(() => ResultParser.Parse(W(H + "clock\tclock\t\t0\ngroup\tG1\n"))).Message);
        Assert.Contains("clock", Assert.Throws<InvalidDataException>(() => ResultParser.Parse(W(H + "group\tG1\tG1\t1\n"))).Message);
        Assert.False(ResultParser.Validate(W(H + "group\tG1\tG1\t1\n"), out var err)); Assert.NotNull(err);
        Assert.True(ResultParser.Validate(Fx("result.tsv"), out _));
        var r = ResultParser.Parse(W(H + "clock\tclock\t\t0\t5\nfuture\tx\tG1\t7\nvehicle\tT1\tG1\t100\t50\t3\t3600\t1\t40\n"));
        Assert.Equal(new[] { "clock", "future", "vehicle" }, r.Rows.Select(x => x.Kind)); // unknown kinds kept
        Assert.Equal(7, r.Rows[1].Values[0]);
        Assert.Equal(new VehicleResult("T1", "G1", 100, 50, 3, 3600, 1, 40), Assert.Single(r.Vehicles!));
    }

    [Fact]
    public void ParseReport_Dash_Is_NaN_And_Block_Without_AvWait_Kept()
    {
        var p = W("Block Statistics\n=====\nB1 3 0 1.5 2 1 0 0.25 4.5\nB2 3 0 1.5 2 1 0 -- 4.5 --\n\n", ".report");
        var rs = ResultParser.ParseReport(p);
        Assert.Equal(2, rs.Blocks.Count);
        Assert.True(double.IsNaN(rs.Blocks[0].AvWait)); Assert.Equal(0.25, rs.Blocks[0].Util);
        Assert.True(double.IsNaN(rs.Blocks[1].Util) && double.IsNaN(rs.Blocks[1].AvWait));
    }

    [Fact]
    public void Kpi_Tolerates_Null_Report_Missing_Material_And_Materialless_Group()
    {
        var input = LogicGeneratorTests.TwoGroups();
        var rs = ResultParser.Parse(Fx("result.tsv"));
        var noM2 = rs with { Materials = rs.Materials.Where(m => m.Name != "M2").ToList() };
        var k = Kpi.FromRun(noM2, null, input);
        var g1 = k.Single(x => x.Group == "G1"); var g2 = k.Single(x => x.Group == "G2");
        Assert.True(double.IsNaN(g1.Utilization)); Assert.Contains("보고서 없음", g1.Note);
        Assert.Equal(0, g2.MinStockRatio); Assert.Contains("M2", g2.Note);
        var empty = LogicGeneratorTests.TwoGroups(); empty.Materials.RemoveAll(m => m.Group == "G2");
        var ke = Kpi.FromRun(rs, null, empty).Where(x => x.Group == "G2").ToList();
        Assert.True(ke[0].MinStockRatio >= 1);
        Assert.Equal(RuleCode.NoResult, Judge.Evaluate(ke, S).Rule);   // NaN utilization (no report) never passes
    }

    [Fact] public void Judge_Passes_Good_Run() => Assert.True(Judge.Evaluate(new[] { K(), K(util: 0.6, stock: 1.0, sup: 10) }, S).Ok);

    [Fact]
    public void Summary_Appends_And_Reads_Back()
    {
        var p = Path.Combine(Path.GetTempPath(), "ash_sum_" + Guid.NewGuid().ToString("N"), "summary.tsv");
        var a = new SummaryRow("n1_s1", "G1", 1, 1, true, "", 0.55, 1.8, 3, 5.5, 0, 0, 1.25, "G1=1");
        var b = a with { RunId = "n2_s1", Ok = false, Reason = "재고\t부족", Utilization = 0.123456 };
        SummaryTsv.Append(p, a); SummaryTsv.Append(p, b);
        Assert.Equal(3, File.ReadAllLines(p).Length); // header once
        var rows = SummaryTsv.ReadAll(p);
        Assert.Equal(a, rows[0]);
        Assert.Equal(b with { Reason = "재고 부족" }, rows[1]);
        File.AppendAllText(p, "torn\tline\n");
        Assert.Equal(2, SummaryTsv.ReadAll(p, out var skipped).Count); Assert.Equal(1, skipped);
        File.WriteAllText(p, "wrong\theader\n");
        Assert.Throws<InvalidDataException>(() => SummaryTsv.ReadAll(p));
        Assert.Throws<InvalidDataException>(() => SummaryTsv.Append(p, a));
        Directory.Delete(Path.GetDirectoryName(p)!, true);
    }

    [Fact]
    public void Judge_Fails_On_NaN_Kpi()
    {
        Assert.Equal(RuleCode.NoResult, Judge.Evaluate(new[] { K(util: double.NaN) }, S).Rule);
        Assert.Equal(RuleCode.NoResult, Judge.Evaluate(new[] { K(stock: double.NaN) }, S).Rule);
    }

    [Fact]
    public void Min_Stock_Under_Safety_Without_A_Stop_Is_A_Warning()
    {
        var v = Judge.Evaluate(new[] { K(stock: 0.8) }, new ASH.Core.Input.Settings());
        Assert.True(v.Ok);
        Assert.Contains("주의", v.Reason);
        Assert.False(Judge.Evaluate(new[] { K(stock: 0.8, stop: 60) }, new ASH.Core.Input.Settings()).Ok); // a process stop still fails
    }
}
