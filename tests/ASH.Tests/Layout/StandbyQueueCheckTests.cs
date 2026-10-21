using ASH.Core.Geometry;
using ASH.Core.Input;
using ASH.Core.Layout;

namespace ASH.Tests.Layout;

public class StandbyQueueCheckTests
{
    // a straight lane: block over x 0..3, standby CP at x 10, so 7 m to queue in
    static ASH.Core.Layout.Layout Lane()
    {
        var l = L.Of(L.Line("p1", 0, 0, 20, 0));
        l.Blocks.Add(L.Block("B1", 0, -1, 3, 1));
        l.Cps.Add(L.Cp("S1", 10, 0, "standby", "G1"));
        return l;
    }

    static ProjectInput Input(int max)
    {
        var p = new ProjectInput();
        p.AgvTypes.Add(new AgvType("T1", "G1", 1, 1, 0.3, 0.3, 1, 20, 20, 0, 0, 1.2, 0.8, "Blue"));
        p.Groups.Add(new AgvGroup("G1", "T1", 1, max, new List<string> { "S1" }, "Nearest"));
        return p;
    }

    [Fact]
    public void Warns_When_The_Queue_Would_Reach_The_Block()
    {
        var w = Assert.Single(StandbyQueueCheck.Check(Lane(), Input(5), 1.0)); // 5 x 1.7 = 8.5 m > 7 m
        Assert.Contains("S1", w); Assert.Contains("7 m", w); Assert.Contains("8.5 m", w);
        Assert.Empty(StandbyQueueCheck.Check(Lane(), Input(4), 1.0));         // 6.8 m fits
    }

    [Fact]
    public void Walks_Back_Through_A_Corner_But_Stops_At_A_Junction()
    {
        // lane comes down x=0 from y=20 to 0, turns east to the standby CP at x=4: 20 + 4 m of room, no block
        var l = L.Of(L.Line("down", 0, 20, 0, 0), L.Line("east", 0, 0, 10, 0));
        l.Cps.Add(L.Cp("S1", 4, 0, "standby", "G1"));
        Assert.Empty(StandbyQueueCheck.Check(l, Input(12), 1.0));              // 12 x 1.7 = 20.4 m < 24 m
        Assert.Single(StandbyQueueCheck.Check(l, Input(15), 1.0));             // 25.5 m > 24 m
        // drawing units in mm: the same lane 1000 times bigger is the same room
        var mm = L.Of(L.Line("down", 0, 20000, 0, 0), L.Line("east", 0, 0, 10000, 0));
        mm.Cps.Add(L.Cp("S1", 4000, 0, "standby", "G1"));
        Assert.Empty(StandbyQueueCheck.Check(mm, Input(12), 0.001));
    }
}
