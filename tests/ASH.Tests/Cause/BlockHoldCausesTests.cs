using ASH.Core.Cause;
using ASH.Core.Input;
using ASH.Core.Runner;
using ASH.Tests.Layout;

namespace ASH.Tests.Cause;

public class BlockHoldCausesTests
{
    // one path a (-10,0)->(10,0), block B001 half-width w: entry at -w, exit at +w; claim 0.5 m, vehicle 1.5 m
    static CauseContext Ctx(double w, bool twoWay = false, ResultSet? rs = null, ReportStats? rep = null, params (string Name, double X)[] stops)
    {
        var path = L.Line("a", -10, 0, 10, 0); path.IsTwoWay = twoWay;
        var l = L.Of(path); l.Blocks.Add(L.Block("B001", -w, -3, w, 3));
        foreach (var (n, x) in stops) l.Cps.Add(L.Cp(n, x, 0, n.StartsWith("T") ? "traffic" : "unload"));
        var inp = new ProjectInput();
        inp.AgvTypes.Add(new AgvType("T1", 1.0, 1.0, 1, 1, 1, 0, 0, 0, 0, 1.5, 1.0, "", 0));
        inp.Groups.Add(new AgvGroup("G1", "T1", 1, 1, new List<string>(), "", 0));
        return new CauseContext(l, inp, rs, rep, VehicleLength: 1.5, VehicleWidth: 1.0, ClaimDist: 0.5);
    }

    static ResultSet Rs(double passes, double wait, double util) =>
        new(new List<MaterialResult>(), new List<GroupResult>(), new[] { new BlockResult("B001", passes, wait, wait * 2, util) }, 0, 28800, new List<ResultRow>());
    static ReportStats Rep(double avTime) =>
        new(new Dictionary<string, TypeStats>(), new[] { new BlockStat("B001", 20, 0.03, avTime, 5) }, new List<string>());

    [Fact] public void Clean_Layout_Has_No_Findings() =>
        Assert.Empty(BlockHoldCauses.Find(Ctx(3.5, false, null, null, ("L1", -6), ("U1", 6), ("U2", -4.0), ("U3", 5.0))));

    [Fact] public void Stop_Inside_Block_Is_Flagged_With_Move_Before_Traffic_Cp()
    {
        var x = Assert.Single(BlockHoldCauses.Find(Ctx(3.5, false, null, null, ("U003", -3.2))));
        Assert.Equal(CauseKinds.BlockHold, x.Kind); Assert.Equal(1, x.Severity);
        Assert.Equal("B001 안 정차", x.Title); Assert.Equal("B001", x.Where); Assert.Equal("U003 입구 쪽 0.8 m 이동", x.Fix);
        Assert.Equal(-3.2, x.X); Assert.Equal(0, x.Y); Assert.Null(x.AtSec);
    }
    [Fact] public void Stop_Inside_Near_Exit_Moves_Out_Past_Tail()
    {
        var x = Assert.Single(BlockHoldCauses.Find(Ctx(3.5, false, null, null, ("U003", 3.0))));
        Assert.Equal("B001 안 정차", x.Title); Assert.Equal("U003 출구 쪽 2.0 m 이동", x.Fix); // 0.5 to the exit + 1.5 vehicle
    }
    [Fact] public void Stop_Between_Traffic_Cp_And_Entry_Claims_Early()
    {
        var x = Assert.Single(BlockHoldCauses.Find(Ctx(3.5, false, null, null, ("U003", -3.8))));
        Assert.Equal("B001 이른 점유", x.Title); Assert.Equal("U003 입구 앞 0.3 m · 점유 거리 0.5 m", x.Detail); Assert.Equal("U003 입구 쪽 0.2 m 이동", x.Fix);
    }
    [Fact] public void Stop_Less_Than_A_Vehicle_Length_Past_Exit_Releases_Late()
    {
        var x = Assert.Single(BlockHoldCauses.Find(Ctx(3.5, false, null, null, ("U003", 4.3))));
        Assert.Equal("B001 늦은 해제", x.Title); Assert.Equal("U003 출구 뒤 0.8 m · 차 길이 1.5 m", x.Detail); Assert.Equal("U003 출구 쪽 0.7 m 이동", x.Fix);
    }
    [Fact] public void Traffic_Cp_Is_Not_A_Stop() =>
        Assert.Empty(BlockHoldCauses.Find(Ctx(3.5, false, null, null, ("T1", -3.8), ("T2", 3.8))));
    [Fact] public void Two_Way_Path_Is_Entered_From_Both_Ends()
    {
        var one = BlockHoldCauses.Find(Ctx(3.5, false, null, null, ("U003", 3.8))).ToList();
        Assert.Equal("B001 늦은 해제", Assert.Single(one).Title); // one-way: past the exit by 0.3
        var two = Assert.Single(BlockHoldCauses.Find(Ctx(3.5, true, null, null, ("U003", 3.8))));
        Assert.Equal("B001 늦은 해제", two.Title); // the first direction that hits wins
        var twoFar = Assert.Single(BlockHoldCauses.Find(Ctx(3.5, true, null, null, ("U003", -4.2))));
        Assert.Equal("B001 늦은 해제", twoFar.Title); Assert.Equal("U003 출구 쪽 0.8 m 이동", twoFar.Fix); // entered from the right: -3.5 is its exit, 0.7 past it
    }
    [Fact] public void One_Way_Stop_Behind_Entry_Beyond_Claim_Is_Clean() =>
        Assert.Empty(BlockHoldCauses.Find(Ctx(3.5, false, null, null, ("U003", -4.2))));

    [Fact] public void Long_Hold_Flags_Excess_Severity2_When_Wait_High_With_Layout_Fix()
    {
        // block +-3.75: 7.5 m inside + 1.5 m vehicle at 1 m/s = 9 s pure; hold 48 s
        var ctx = Ctx(3.75, false, Rs(20, 22, 0.1), Rep(48), ("U003", 4.55));
        var x = BlockHoldCauses.Find(ctx).Single(i => i.Title.Contains("초과"));
        Assert.Equal(2, x.Severity); Assert.Equal("B001 초과 점유 39초", x.Title); Assert.Equal("1회 점유 48초 · 통과 9초 · 대기 평균 22초", x.Detail);
        Assert.Equal("U003 출구 쪽 0.7 m 이동", x.Fix); Assert.Equal("B001", x.Where); Assert.Equal(4.55, x.X);
        Assert.Contains(BlockHoldCauses.Find(ctx), i => i.Title == "B001 늦은 해제"); // the layout finding rides along in a run
        Assert.DoesNotContain(BlockHoldCauses.Find(ctx with { Result = null, Report = null }), i => i.Title.Contains("초과"));
    }
    [Fact] public void Hold_Falls_Back_To_Utilization_When_No_Report_And_Low_Wait_Is_Severity1()
    {
        var x = Assert.Single(BlockHoldCauses.Find(Ctx(3.75, false, Rs(20, 3, 48.0 * 20 / 28800))));
        Assert.Equal(1, x.Severity); Assert.Equal("B001 초과 점유 39초", x.Title); Assert.Equal("", x.Fix); Assert.Equal(0, x.X); // block centre, no layout cause
        Assert.Equal("1회 점유 48초 · 통과 9초 · 대기 평균 3초", x.Detail);
    }
    [Fact] public void Capacity_Scales_Utilization()
    {
        var ctx = Ctx(3.75, false, Rs(20, 3, 48.0 * 20 / 28800 / 2));
        ctx.Layout.Blocks[0].Cap = 2;
        Assert.Equal("1회 점유 48초 · 통과 9초 · 대기 평균 3초", Assert.Single(BlockHoldCauses.Find(ctx)).Detail);
    }
    [Fact] public void Normal_Hold_Few_Passes_And_Missing_Speed_Are_Not_Flagged()
    {
        Assert.Empty(BlockHoldCauses.Find(Ctx(3.75, false, Rs(20, 22, 0.1), Rep(15)))); // excess 6 s
        Assert.Empty(BlockHoldCauses.Find(Ctx(3.75, false, Rs(3, 22, 0.1), Rep(48)))); // 3 passes: too few
        Assert.Empty(BlockHoldCauses.Find(Ctx(3.75, false, Rs(20, 22, 0.1), Rep(25)))); // excess 16 s but < 2 x pure 9
        var noSpeed = Ctx(3.75, false, Rs(20, 22, 0.1), Rep(48)); noSpeed.Input.AgvTypes.Clear();
        Assert.Empty(BlockHoldCauses.Find(noSpeed));
    }
}
