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
    [Fact] public void Judge_Checks_Utilization_Last_So_Its_Verdict_Means_Only_The_Limit_Was_Missed()
    {
        var u = Judge.Evaluate(new[] { K(util: 0.9) }, S);
        Assert.Equal(RuleCode.Utilization, u.Rule); Assert.StartsWith(Judge.UtilPrefix + ":", u.Reason);
        var d = Judge.Evaluate(new[] { K(util: 0.9, dead: 1) }, S);
        Assert.False(d.Ok); Assert.Equal(RuleCode.Deadlock, d.Rule);   // the real failure wins the reason; Ok is the same as before
        Assert.Equal(RuleCode.Supply, Judge.Evaluate(new[] { K(util: 0.9, con: 9, exp: 10) }, S).Rule);
        Assert.Equal(RuleCode.StopTime, Judge.Evaluate(new[] { K(util: 0.9, stop: 5) }, S).Rule);
    }
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
    public void ResultParser_Reads_The_Supply_Wait_Values_NaN_When_The_Row_Has_None()
    {
        const string H = "kind\tname\tgroup\tvalue1\nclock\tclock\t\t0\t5\n";
        // a row from a model built before value9..15: the new fields are unknown, not 0
        var old = Assert.Single(ResultParser.Parse(W(H + "material\tM001\tG1\t1\t2\t3\t4\t5\t6\t7\t8\n")).Materials);
        Assert.Equal(6, old.Consumed);
        Assert.All(new[] { old.WaitAvg, old.WaitMax, old.WaitVehAvg, old.SwapN, old.SwapSum, old.SwapMax, old.FollowN }, x => Assert.True(double.IsNaN(x)));
        var m = Assert.Single(ResultParser.Parse(W(H + "material\tM001\tG1\t1\t2\t3\t4\t5\t6\t7\t8\t9\t10\t11\t12\t13\t14\t15\n")).Materials);
        Assert.Equal((9.0, 10.0, 11.0, 12.0, 13.0, 14.0, 15.0), (m.WaitAvg, m.WaitMax, m.WaitVehAvg, m.SwapN, m.SwapSum, m.SwapMax, m.FollowN));
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
    public void Parse_Reads_The_Time_Split_Of_A_Vehicle_Row_Only_When_It_Is_There()
    {
        const string H = "kind\tname\tgroup\tvalue1\nclock\tclock\t\t0\t5\n";
        // busy 100 of 400: loaded 30, handling 20, buffer within the allowance 10 / beyond it 50, repair 5 -> empty 35, idle 250
        var r = ResultParser.Parse(W(H + "vehicle\tT1\tG1\t100\t50\t3\t400\t1\t40\t30\t20\t10\t50\t5\n"));
        var s = Assert.Single(r.Vehicles!).Split!;
        Assert.Equal(new TimeSplit(400, 100, 30, 20, 10, 50, 5), s);
        Assert.Equal(35, s.Empty); Assert.Equal(250, s.Idle);
        Assert.Equal(s.Busy, s.Loaded + s.Empty + s.Handling + s.BufferBusy + s.Repair);
        // models before the split write 0 0 in value7/value8 (or nothing): no split
        Assert.Null(Assert.Single(ResultParser.Parse(W(H + "vehicle\tT1\tG1\t100\t50\t3\t400\t1\t40\t0\t0\n")).Vehicles!).Split);
        Assert.Null(Assert.Single(ResultParser.Parse(W(H + "vehicle\tT1\tG1\t100\t50\t3\t400\t1\n")).Vehicles!).Split);
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
    public void Judge_Keeps_The_Call_Group_Reasons()
    {
        Assert.Equal(new Verdict(false, "가동률 초과: 90 % > 75 %", RuleCode.Utilization), Judge.Evaluate(new[] { K(util: 0.9) }, S)); // invariant P0 puts a space
        Assert.Equal(new Verdict(false, "재고 부족(공정 정지 120초)", RuleCode.StopTime), Judge.Evaluate(new[] { K(stop: 120) }, S));
        Assert.Equal(new Verdict(false, "생산 미달: 소비 9 < 이론 10의 95%", RuleCode.Supply), Judge.Evaluate(new[] { K(con: 9) }, S));
        Assert.Equal(new Verdict(false, "교착 1건", RuleCode.Deadlock), Judge.Evaluate(new[] { K(dead: 1) }, S));
        Assert.Equal(new Verdict(true, "주의: 평균 최소재고가 안전재고의 0.8배", RuleCode.None), Judge.Evaluate(new[] { K(stock: 0.8) }, S));
        // a call kpi with a follow rate (it never has one) is still judged by the call rules
        Assert.Equal(RuleCode.Utilization, Judge.Evaluate(new[] { K(util: 0.9) with { FollowRate = 0.1 } }, S).Rule);
    }

    static RunKpi C(double follow, double stop = 0, int dead = 0) => K(stop: stop, dead: dead) with { Cycle = true, FollowRate = follow };

    [Fact]
    public void Judge_Cycle_Group_On_Follow_Rate()
    {
        Assert.Equal(new Verdict(true, "", RuleCode.None), Judge.Evaluate(new[] { C(0.96) }, S));
        Assert.Equal(new Verdict(false, "뒤차 대기 90% < 95%", RuleCode.Follow), Judge.Evaluate(new[] { C(0.90) }, S));
        Assert.Equal(new Verdict(false, "교체 없음", RuleCode.Follow), Judge.Evaluate(new[] { C(double.NaN), C(double.NaN) }, S));
        // a process stop is a note for a cycle group, not a failure
        Assert.Equal(new Verdict(true, "주의: 공정 정지 30초", RuleCode.None), Judge.Evaluate(new[] { C(0.96, stop: 30) }, S));
        // replications averaged, a replication without swaps left out: (0.9 + 1.0) / 2 = 0.95 passes
        Assert.True(Judge.Evaluate(new[] { C(0.9), C(double.NaN), C(1.0) }, S).Ok);
        Assert.Equal(RuleCode.Follow, Judge.Evaluate(new[] { C(0.96) }, new ASH.Core.Input.Settings { CycleFollowRate = 0.97 }).Rule);
        // utilization, production and stock rules do not apply
        Assert.True(Judge.Evaluate(new[] { K(util: 0.99, stock: 0.1, con: 0) with { Cycle = true, FollowRate = 1 } }, S).Ok);
    }

    [Fact]
    public void Judge_Cycle_Group_Order_Is_NoResult_Deadlock_Follow()
    {
        Assert.Equal(new Verdict(false, "교착 1건", RuleCode.Deadlock), Judge.Evaluate(new[] { C(0.5, dead: 1) }, S));
        Assert.Equal(RuleCode.NoResult, Judge.Evaluate(new[] { C(0.5, dead: 1) with { Utilization = double.NaN } }, S).Rule);
        Assert.Equal(RuleCode.NoResult, Judge.Evaluate(Array.Empty<RunKpi>(), S).Rule);
    }

    static ResultSet Swaps(params string[] rows) =>
        ResultParser.Parse(W("kind\tname\tgroup\tvalue1\nclock\tclock\t\t0\t5\n" + string.Concat(rows.Select(r => r + "\n")))) with { Groups = new[] { new GroupResult("G1", 2) } };

    [Fact]
    public void Kpi_Cycle_Group_Follow_Rate_And_Swap_Times()
    {
        var input = LogicGeneratorTests.Sample();
        input.Groups[0] = input.Groups[0] with { SupplyMode = "Cycle" };
        // value12..15 = SwapN, SwapSum, SwapMax, FollowN: M1 9 of 10 swaps followed, M2 10 of 10
        var k = Kpi.FromRun(Swaps("material\tM1\tG1\t1\t2\t3\t4\t0\t4\t0\t0\t0\t0\t0\t10\t300\t50\t9",
            "material\tM2\tG1\t1\t2\t3\t4\t0\t4\t0\t0\t0\t0\t0\t10\t500\t80\t10"), null, input).Single();
        Assert.True(k.Cycle);
        Assert.Equal(0.95, k.FollowRate, 9);
        Assert.Equal((40.0, 80.0), (k.SwapAvgSec, k.SwapMaxSec)); // (300 + 500) / 20, max
        // no swaps at all (no vehicle active, or too short a run): no rate, and the judge says so instead of dividing by zero
        var none = Kpi.FromRun(Swaps("material\tM1\tG1\t1\t2\t3\t4\t0\t4\t0\t0\t0\t0\t0\t0\t0\t0\t0",
            "material\tM2\tG1\t1\t2\t3\t4\t0\t4\t0\t0\t0\t0\t0\t0\t0\t0\t0"), null, input).Single();
        Assert.All(new[] { none.FollowRate, none.SwapAvgSec, none.SwapMaxSec }, x => Assert.True(double.IsNaN(x)));
        Assert.Equal(new Verdict(false, "교체 없음", RuleCode.Follow), Judge.Evaluate(new[] { none with { Utilization = 0 } }, S));
        // a model from before the swap values, and a call group (SwapN 0): blank, not a rate
        var old = Kpi.FromRun(Swaps("material\tM1\tG1\t1\t2\t3\t4\t0\t4", "material\tM2\tG1\t1\t2\t3\t4\t0\t4"), null, input).Single();
        Assert.True(double.IsNaN(old.FollowRate) && double.IsNaN(old.SwapMaxSec));
        var call = Kpi.FromRun(Swaps("material\tM1\tG1\t1\t2\t3\t4\t0\t4\t0\t0\t0\t0\t0\t0\t0\t0\t0", "material\tM2\tG1\t1\t2\t3\t4\t0\t4"), null,
            LogicGeneratorTests.Sample()).Single();
        Assert.False(call.Cycle);
        Assert.All(new[] { call.FollowRate, call.SwapAvgSec, call.SwapMaxSec }, x => Assert.True(double.IsNaN(x)));
    }

    [Fact]
    public void Summary_Swap_Columns_Read_Back_And_An_Old_File_Still_Reads()
    {
        var p = Path.Combine(Path.GetTempPath(), "ash_sum_" + Guid.NewGuid().ToString("N"), "summary.tsv");
        var cycle = new SummaryRow("n2_s1", "G2", 2, 1, true, "", 0.4, 2, 10, 10, 0, 0, 5, "G1=1;G2=2", 0.95, 40.5, 80);
        var call = cycle with { Group = "G1", Active = 1, FollowRate = double.NaN, SwapAvgSec = double.NaN, SwapMaxSec = double.NaN };
        SummaryTsv.Append(p, cycle); SummaryTsv.Append(p, call);
        var lines = File.ReadAllLines(p);
        Assert.EndsWith("\tscenario\tfollowRate\tswapAvgSec\tswapMaxSec", lines[0]);
        Assert.EndsWith("\tG1=1;G2=2\t0.95\t40.5\t80", lines[1]);
        Assert.EndsWith("\tG1=1;G2=2\t\t\t", lines[2]); // a call group's are blank
        Assert.Equal(new[] { cycle, call }, SummaryTsv.ReadAll(p));

        // a summary.tsv written before the swap columns: read with NaN, and appending to it still works
        File.WriteAllText(p, "runId\tgroup\tactive\tseed\tok\treason\tutilization\tminStockRatio\tsupplies\texpected\tstopTimeSec\tdeadlocks\tdurationSec\tscenario\n"
            + "n1_s1\tG1\t1\t1\t1\t\t0.55\t1.8\t3\t5.5\t0\t0\t1.25\tG1=1\n");
        var old = new SummaryRow("n1_s1", "G1", 1, 1, true, "", 0.55, 1.8, 3, 5.5, 0, 0, 1.25, "G1=1");
        Assert.Equal(new[] { old }, SummaryTsv.ReadAll(p));
        Assert.True(double.IsNaN(SummaryTsv.ReadAll(p)[0].FollowRate));
        SummaryTsv.Append(p, cycle);
        Assert.Equal(new[] { old, cycle }, SummaryTsv.ReadAll(p, out var skipped));
        Assert.Equal(0, skipped);
        Directory.Delete(Path.GetDirectoryName(p)!, true);
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
