using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using ASH.Core.Traffic;
using ASH.Tests.Layout;
using Xunit;

namespace ASH.Tests.Traffic;

public class CpRenumberTests
{
    private static string[] Names(System.Collections.Generic.IEnumerable<LayoutCp> cps) => cps.Select(c => c.Name).ToArray();

    [Fact] public void Order_Follows_Path_Direction_Not_List_Order()
    {
        // b is listed first but runs after a (a: 0..10, b: 10..20)
        var l = L.Of(L.Line("b", 10, 0, 20, 0), L.Line("a", 0, 0, 10, 0));
        var cps = new[] { L.Cp("x", 15, 0), L.Cp("y", 2, 0), L.Cp("z", 8, 0) };
        Assert.Equal(new[] { "y", "z", "x" }, Names(CpRenumber.TravelOrder(l, cps)));
    }

    [Fact] public void Order_Walks_A_Branch_Before_Returning()
    {
        // a feeds b (east) and c (north); b comes first in the list, so its CPs are numbered before c's
        var l = L.Of(L.Line("a", 0, 0, 10, 0), L.Line("b", 10, 0, 20, 0), L.Line("c", 10, 0, 10, 10), L.Line("d", 20, 0, 30, 0));
        var cps = new[] { L.Cp("c1", 10, 5), L.Cp("d1", 25, 0), L.Cp("b1", 15, 0), L.Cp("a1", 5, 0) };
        Assert.Equal(new[] { "a1", "b1", "d1", "c1" }, Names(CpRenumber.TravelOrder(l, cps)));
    }

    [Fact] public void Start_Point_Picks_The_First_Path()
    {
        // a closed loop has no source: the first path in the list starts the numbering, a start point overrides that
        var l2 = L.Of(L.Line("a", 0, 0, 10, 0), L.Line("b", 10, 0, 10, 10), L.Line("c", 10, 10, 0, 0));
        var c = new[] { L.Cp("onA", 5, 0), L.Cp("onB", 10, 5), L.Cp("onC", 5, 5) };
        Assert.Equal(new[] { "onA", "onB", "onC" }, Names(CpRenumber.TravelOrder(l2, c)));
        Assert.Equal(new[] { "onB", "onC", "onA" }, Names(CpRenumber.TravelOrder(l2, c, new Point2(10, 5))));
    }

    [Fact] public void Two_Way_Path_Entered_At_Its_End_Counts_Backwards()
    {
        // a ends at 10; the two-way path t is drawn 20 -> 10 and is entered at its end... here a feeds t at (10,0)
        var t = L.Line("t", 20, 0, 10, 0); t.IsTwoWay = true;
        var l = L.Of(L.Line("a", 0, 0, 10, 0), t);
        var cps = new[] { L.Cp("far", 18, 0), L.Cp("near", 12, 0) };
        Assert.Equal(new[] { "near", "far" }, Names(CpRenumber.TravelOrder(l, cps)));
    }

    [Fact] public void Ties_Keep_Input_Order_And_Offpath_Goes_Last()
    {
        var l = L.Of(L.Line("a", 0, 0, 10, 0));
        var cps = new[] { L.Cp("late", 5, 0), L.Cp("same", 5, 0), L.Cp("early", 1, 0) };
        Assert.Equal(new[] { "early", "late", "same" }, Names(CpRenumber.TravelOrder(l, cps)));
        Assert.Equal(new[] { "k", "j" }, Names(CpRenumber.TravelOrder(L.Of(), new[] { L.Cp("k", 0, 0), L.Cp("j", 1, 0) })));
    }

    [Fact] public void Plan_Numbers_One_Type_In_Travel_Order()
    {
        var l = L.Of(L.Line("a", 0, 0, 10, 0));
        l.Cps.AddRange(new[] { L.Cp("L009", 8, 0, "load"), L.Cp("L002", 2, 0, "load"), L.Cp("U005", 5, 0, "unload"), L.Cp("T1", 1, 0) });
        var plan = CpRenumber.Plan(l, new[] { "load" });
        Assert.Equal(new[] { ("L002", "L001"), ("L009", "L002") }, plan.Select(r => (r.OldName, r.NewName)).ToArray());
        Assert.Equal(new[] { "load", "load" }, plan.Select(r => r.Cp.Type).ToArray());
    }

    [Fact] public void Plan_All_Types_Uses_Each_Types_Prefix()
    {
        var l = L.Of(L.Line("a", 0, 0, 10, 0));
        l.Cps.AddRange(new[] { L.Cp("x", 3, 0, "unload"), L.Cp("y", 1, 0, "load"), L.Cp("z", 9, 0, "load") });
        var plan = CpRenumber.Plan(l, null);
        Assert.Equal(new[] { "L001", "L002", "U001" }, plan.Select(r => r.NewName).ToArray());
    }

    [Fact] public void Plan_Skips_Names_Other_Cps_Keep()
    {
        var l = L.Of(L.Line("a", 0, 0, 10, 0));
        l.Cps.AddRange(new[] { L.Cp("L001", 1, 0, "unload"), L.Cp("q", 2, 0, "load"), L.Cp("r", 3, 0, "load") });
        var plan = CpRenumber.Plan(l, new[] { "load" });
        Assert.Equal(new[] { "L002", "L003" }, plan.Select(r => r.NewName).ToArray());
    }

    [Fact] public void Tsv_Lists_Only_Changed_Names()
    {
        var l = L.Of(L.Line("a", 0, 0, 10, 0));
        l.Cps.AddRange(new[] { L.Cp("L001", 1, 0, "load", "G1"), L.Cp("L007", 2, 0, "load", "G1") });
        var tsv = CpRenumber.Tsv(CpRenumber.Plan(l, null));
        Assert.Equal("이전 이름\t새 이름\t종류\t그룹\nL007\tL002\tload\tG1\n", tsv);
    }

    [Fact] public void ConnectedPaths_Ignore_Direction_And_Skip_Islands()
    {
        var l = L.Of(L.Line("a", 0, 0, 10, 0), L.Line("b", 20, 0, 10, 0), L.Line("far", 100, 0, 110, 0));
        Assert.Equal(new[] { "a", "b" }, CpRenumber.ConnectedPaths(l, l.Paths[0]).Select(p => p.Name).ToArray());
    }
}
