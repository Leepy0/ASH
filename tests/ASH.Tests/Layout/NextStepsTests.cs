using ASH.Core.Layout;

namespace ASH.Tests.Layout;

public class NextStepsTests
{
    static readonly NextStepFacts Done = new NextStepFacts(Saved: true, HasSettings: true, HasInput: true, ModelBuilt: true);

    static string[] Labels(ASH.Core.Layout.Layout l, NextStepFacts f) => NextSteps.Of(l, f).Select(s => s.Label).ToArray();

    // one lane, a standby and a load CP of G1, no junction
    static ASH.Core.Layout.Layout Ready()
    {
        var l = L.Of(L.Line("p1", 0, 0, 20, 0));
        l.Cps.Add(L.Cp("S1", 2, 0, "standby", "G1"));
        l.Cps.Add(L.Cp("L1", 10, 0, "load", "G1"));
        return l;
    }

    [Fact]
    public void Nothing_Left_Gives_An_Empty_List() => Assert.Empty(NextSteps.Of(Ready(), Done));

    [Fact]
    public void Empty_Drawing_Asks_For_Paths_Then_Cps_Then_The_Files()
    {
        var f = new NextStepFacts(true, true, false, false);
        Assert.Equal(new[] { "경로 없음(AGVPATH)", "CP 없음(AGVCP)", "input.xlsx 없음(AGVEXCEL)" }, Labels(new ASH.Core.Layout.Layout(), f));
    }

    [Fact]
    public void Group_Without_Standby_Or_Load_Is_Named()
    {
        var l = L.Of(L.Line("p1", 0, 0, 20, 0));
        l.Cps.Add(L.Cp("T1", 5, 0));                          // traffic CP, no group: belongs to none
        l.Cps.Add(L.Cp("L1", 10, 0, "load", "G1"));           // G1 has load, no standby
        l.Cps.Add(L.Cp("S2", 2, 0, "buffer", "G2"));          // G2 has a buffer (counts as waiting place), no load
        Assert.Equal(new[] { "그룹 G1: 대기 CP 없음", "그룹 G2: load 없음" }, Labels(l, Done));
    }

    [Fact]
    public void Named_Group_Without_Cps_Needs_Both()
    {
        var f = Done with { ExtraGroups = new[] { "G1", "G3" } };
        Assert.Equal(new[] { "그룹 G3: 대기 CP 없음", "그룹 G3: load 없음" }, Labels(Ready(), f));
    }

    [Fact]
    public void Block_Is_Asked_For_Only_Where_Paths_Meet()
    {
        var l = Ready();
        l.Paths = new List<LayoutPath> { L.Line("a", 0, 0, 10, 0), L.Line("b", 10, 0, 20, 0), L.Line("c", 10, 0, 10, 10) }; // T junction: three path ends meet at (10,0)
        Assert.Equal(new[] { "block 없음(AGVTRAFFIC)" }, Labels(l, Done));
        l.Blocks.Add(L.Block("B1", 9, -1, 11, 1));
        Assert.Empty(NextSteps.Of(l, Done));
    }

    [Fact]
    public void Unsaved_Drawing_Says_Save_And_Skips_The_Files()
    {
        var f = new NextStepFacts(false, false, false, false);
        Assert.Equal(new[] { "도면 저장(QSAVE)" }, Labels(Ready(), f));
    }

    [Fact]
    public void Errors_And_Model_Come_After_The_Files()
    {
        var f = new NextStepFacts(true, false, true, false, CheckErrors: 3);
        Assert.Equal(new[] { "ash.settings.tsv 없음", "검증 오류 3건(AGVCHECK)", "모델 없음(AGVMODEL)" }, Labels(Ready(), f));
        var steps = NextSteps.Of(Ready(), f);
        Assert.Equal("", steps[0].Command);                    // nothing to send for the settings file
        Assert.Equal("AGVCHECK", steps[1].Command);
    }

    [Fact]
    public void No_Model_Is_Not_Asked_For_Before_The_Workbook_Exists()
    {
        var f = new NextStepFacts(true, true, false, false);
        Assert.Equal(new[] { "input.xlsx 없음(AGVEXCEL)" }, Labels(Ready(), f));
    }
}
