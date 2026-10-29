using ASH.Core.Cause;
using ASH.Core.Input;
using ASH.Core.Layout;
using ASH.Core.Runner;
using ASH.Tests.Layout;

namespace ASH.Tests.Cause;

public class DeadlockTests
{
    const string Header = "kind\tname\tgroup\tvalue1\tvalue2\tvalue3\tvalue4\tvalue5\tvalue6\tvalue7\tvalue8\tvalue9\tvalue10\tvalue11";

    static ResultSet Parse(params string[] rows)
    {
        var p = Path.Combine(Path.GetTempPath(), "ash_dl_" + Guid.NewGuid().ToString("N") + ".tsv");
        File.WriteAllLines(p, new[] { Header }.Concat(rows).Concat(new[] { "clock\tclock\t\t0\t3600\t0\t0\t0\t0\t0\t0" }));
        try { return ResultParser.Parse(p); } finally { File.Delete(p); }
    }

    [Fact]
    public void Dlock_Rows_Become_One_Event_Per_Episode_In_Time_Order()
    {
        var rs = Parse(
            "deadlock\tdeadlock\t\t3\t0\t0\t0\t0\t0\t0\t0",
            "dlock\tTug1\tG1\t2500.000000\t2\t5.000000\t0.000000\tB007\tB005",
            "dlock\tTug1\tG1\t1260.000000\t1\t5.000000\t0.000000\tagv:L001\tBL\tBR",
            "dlock\tTug3\tG1\t1260.000000\t1\t\t\tagv:B001",
            "dlock\tTug2\tG1\t1260.000000\t1\t6.000000\t10.000000\tBL",
            "dlock\tTug2\tG1\t2500.000000\t2\t6.5\t1\tB005\tB007");
        Assert.Equal(3, rs.Deadlocks);
        Assert.DoesNotContain(rs.Rows, r => r.Kind == "dlock");
        Assert.Equal(new[] { 1260.0, 2500.0 }, rs.DeadlockEvents!.Select(e => e.AtSec));
        var first = rs.DeadlockEvents![0];
        Assert.Equal(new[] { "Tug1", "Tug3", "Tug2" }, first.Links.Select(k => k.Vehicle));
        Assert.Equal(new[] { "L001", "B001", "BL" }, first.Links.Select(k => k.WaitsFor)); // a CP loses its system prefix
        Assert.Equal(new[] { "BL", "BR" }, first.Links[0].Holds);
        Assert.Empty(first.Links[1].Holds);
        Assert.True(double.IsNaN(first.Links[1].X) && double.IsNaN(first.Links[1].Y));
        Assert.Equal((6.0, 10.0), (first.Links[2].X, first.Links[2].Y));
        Assert.Equal("G1", first.Links[2].Group);
    }

    [Fact]
    public void No_Dlock_Rows_Give_No_Events_And_A_Short_Row_Is_Invalid()
    {
        Assert.Empty(Parse("deadlock\tdeadlock\t\t0\t0\t0\t0\t0\t0\t0\t0").DeadlockEvents!);
        Assert.Throws<InvalidDataException>(() => Parse("dlock\tTug1\tG1\t10\t1\t0\t0"));
        Assert.Throws<InvalidDataException>(() => Parse("dlock\tTug1\tG1\tx\t1\t0\t0\tB1"));
    }

    static readonly DeadlockEvent Event = new(1260, new[]
    {
        new DeadlockLink("V3", "G1", "B007", new[] { "B005" }, 3, 0.5),
        new DeadlockLink("V5", "G1", "B005", new[] { "B007" }, 6, 0.5),
    });

    static CauseContext Ctx(ASH.Core.Layout.Layout l, params DeadlockEvent[] events) =>
        new(l, new ProjectInput(), new ResultSet(new List<MaterialResult>(), new List<GroupResult>(), new List<BlockResult>(), events.Length, 3600,
            new List<ResultRow>(), DeadlockEvents: events), VehicleLength: 1.5, ClaimDist: 0.5);

    // two blocks on a straight line, gap between them in metres
    static ASH.Core.Layout.Layout Two(double gap, params LayoutCp[] cps)
    {
        var l = L.Of(L.Line("p1", 0, 0, 20, 0));
        l.Blocks.Add(L.Block("B005", 2, -1, 5, 1));
        l.Blocks.Add(L.Block("B007", 5 + gap, -1, 8 + gap, 1));
        l.Cps.AddRange(cps);
        return l;
    }

    [Fact]
    public void An_Episode_Says_When_Where_Who_And_Moves_A_Stop_Cp_Out_Of_The_Block()
    {
        var c = Ctx(Two(0.2, L.Cp("U003", 7, 0, "unload"), L.Cp("T001", 4, 0)), Event);
        var item = Assert.Single(DeadlockCauses.Find(c));
        Assert.Equal(CauseKinds.Deadlock, item.Kind);
        Assert.Equal(2, item.Severity);
        Assert.Equal("교착 B005·B007", item.Title);
        Assert.Equal("B005·B007", item.Where);
        Assert.Equal("V3 대기 B007 · 점유 B005 / V5 대기 B005 · 점유 B007", item.Detail);
        Assert.Equal(1260, item.AtSec);
        Assert.Equal(5.1, item.X!.Value, 6); Assert.Equal(0, item.Y!.Value, 6); // centre of the two rects
        // U003 sits 1.2 m from B007's end along its path (x 5.2..8.2): out to one vehicle length = 2.7 m; the traffic CP T001 is not a stop
        Assert.Equal("U003 block 밖 2.7 m 이동", item.Fix);
        Assert.DoesNotContain("결품", item.Title + item.Detail + item.Fix);
    }

    [Fact]
    public void The_Stop_Cp_Where_A_Holder_Stands_Comes_First()
    {
        // V5 stands at S001, 0.5 m past B005 (x 2..5), its tail still in it: S001 must go 1.0 m further out for a 1.5 m vehicle.
        // U003 deep inside B007 would be the layout rule's pick.
        var ev = new DeadlockEvent(80, new[]
        {
            new DeadlockLink("V3", "G1", "B005", new string[0], 1, 0),
            new DeadlockLink("V5", "G1", "B007", new[] { "B005" }, 5.5, 0),
        });
        var item = Assert.Single(DeadlockCauses.Find(Ctx(Two(3, L.Cp("S001", 5.5, 0, "standby"), L.Cp("U003", 9.5, 0, "unload")), ev)));
        Assert.Equal("S001 block 밖 1.0 m 이동", item.Fix);
    }

    [Fact]
    public void Blocks_Without_Room_Between_Are_Merged_Else_Room_Before_The_Waited_Block()
    {
        Assert.Equal("B005·B007 병합", Assert.Single(DeadlockCauses.Find(Ctx(Two(0.2), Event))).Fix);
        Assert.Equal(new[] { "B005", "B007" }, Assert.Single(DeadlockCauses.Find(Ctx(Two(0.2), Event))).Refs!.OrderBy(n => n));
        // 3 m apart (more than 1.5 + 2 x 0.5): no merge; room before the block the first vehicle waits for
        Assert.Equal("대기 자리 확보 B007 앞", Assert.Single(DeadlockCauses.Find(Ctx(Two(3), Event))).Fix);
        // a stop CP 2 m outside (more than a vehicle length) is no reason
        Assert.Equal("대기 자리 확보 B007 앞", Assert.Single(DeadlockCauses.Find(Ctx(Two(3, L.Cp("U003", 13, 0, "unload")), Event))).Fix);
    }

    [Fact]
    public void An_Analyser_That_Throws_Is_Left_Out_Not_The_Whole_List()
    {
        // a link with no holds list (data the analyser does not expect) makes DeadlockCauses throw; the other analysers still answer
        var bad = new DeadlockEvent(10, new[] { new DeadlockLink("V1", "G1", "B005", null!, 1, 0) });
        var failed = new List<string>();
        void Note(string n, Exception e) { lock (failed) failed.Add(n); }
        CauseAnalysis.Failed += Note;
        try
        {
            var all = CauseAnalysis.All(Ctx(Two(0.2), bad));
            Assert.DoesNotContain(all, i => i.Kind == CauseKinds.Deadlock);
            Assert.Contains("교착", failed);
        }
        finally { CauseAnalysis.Failed -= Note; }
    }

    [Fact]
    public void No_Result_Or_A_Cp_Only_Lock()
    {
        Assert.Empty(DeadlockCauses.Find(new CauseContext(Two(3), new ProjectInput())));
        var cpOnly = new DeadlockEvent(50, new[] { new DeadlockLink("V1", "G1", "C2", new string[0], 1, 0), new DeadlockLink("V2", "G1", "C1", new string[0], 3, 0) });
        var item = Assert.Single(DeadlockCauses.Find(Ctx(Two(3), cpOnly)));
        Assert.Equal("교착 C1·C2", item.Title);
        Assert.Equal("", item.Fix); Assert.Equal(new[] { "C1", "C2" }, item.Refs!.OrderBy(n => n));
        Assert.Equal(2, item.X!.Value, 6); // the vehicles' positions when no block is involved
    }

    static ASH.Core.Layout.Layout Loop() => L.Of(L.Line("p1", 0, 0, 10, 0), L.Line("p2", 10, 0, 10, 10), L.Line("p3", 10, 10, 0, 10), L.Line("p4", 0, 10, 0, 0));

    static CauseContext Static(ASH.Core.Layout.Layout l) => new(l, new ProjectInput(), VehicleLength: 1.5, ClaimDist: 0.5);

    [Fact]
    public void A_One_Way_Loop_Covered_By_Two_Blocks_Is_A_Lock_Ring()
    {
        var l = Loop();
        l.Blocks.Add(L.Block("BL", -1, -1, 4.5, 11));
        l.Blocks.Add(L.Block("BR", 4.5, -1, 11, 11));
        var item = Assert.Single(DeadlockRisk.Find(Static(l)));
        Assert.Equal(CauseKinds.DeadlockRisk, item.Kind);
        Assert.Equal(1, item.Severity);
        Assert.Equal("교착 위험 BL·BR", item.Title);
        Assert.Equal("BL·BR", item.Where);
        Assert.Equal("BL→BR 0.0 m · BR→BL 0.0 m", item.Detail);
        Assert.Null(item.AtSec);
        Assert.Equal("BL·BR 병합", item.Fix);
        // the same through CauseAnalysis.Static, before any run
        Assert.Contains(CauseAnalysis.Static(Static(l)), x => x.Kind == CauseKinds.DeadlockRisk);
    }

    [Fact]
    public void Corner_Blocks_With_Room_Between_Or_A_One_Way_Chain_Are_No_Ring()
    {
        var l = Loop();
        foreach (var (n, x, y) in new[] { ("B1", 0, 0), ("B2", 10, 0), ("B3", 10, 10), ("B4", 0, 10) }) l.Blocks.Add(L.Block(n, x - 1, y - 1, x + 1, y + 1));
        Assert.Empty(DeadlockRisk.Find(Static(l))); // 8 m between corners
        // one-way straight path: B1 then B2 right after, but nothing leads back
        var s = L.Of(L.Line("p1", 0, 0, 20, 0));
        s.Blocks.Add(L.Block("B1", 2, -1, 5, 1)); s.Blocks.Add(L.Block("B2", 5.5, -1, 8, 1));
        Assert.Empty(DeadlockRisk.Find(Static(s)));
        Assert.Contains("B2", DeadlockRisk.Succession(s, 2.5)["B1"].Keys);
        // the same path two-way: vehicles meet head-on, each holding one
        s.Paths[0].IsTwoWay = true;
        var item = Assert.Single(DeadlockRisk.Find(Static(s)));
        Assert.Equal("B1·B2", item.Where);
        Assert.Equal("B1→B2 0.5 m · B2→B1 0.5 m", item.Detail);
    }

    [Fact]
    public void Succession_Walks_Across_Path_Joints()
    {
        // B1 ends 0.6 m before the joint at x=10, B2 starts 0.4 m after it on the next path, around the corner
        var l = L.Of(L.Line("a", 0, 0, 10, 0), L.Line("b", 10, 0, 10, 10));
        l.Blocks.Add(L.Block("B1", 6, -1, 9.4, 1));
        l.Blocks.Add(L.Block("B2", 9, 0.4, 11, 3));
        var next = DeadlockRisk.Succession(l, 2.5);
        Assert.Equal(1.0, next["B1"]["B2"], 1);
        Assert.Empty(next["B2"]);
    }
}
