using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using Xunit;

namespace ASH.Tests.Layout;

public class LayoutCheckerTests
{
    static CheckOptions O => new();

    [Fact] public void Dangling_End_Reported_Except_Spur() {
        var l = L.Of(L.Line("a",0,0,10,0), L.Spur("s",5,0,5,3));
        var f = LayoutChecker.Check(l, O).Where(x => x.Code == FindingCode.DanglingEnd).ToList();
        Assert.Equal(2, f.Count); Assert.DoesNotContain(f, x => x.EntityId == "s");
    }
    [Fact] public void Dead_End_Node() {
        var l = L.Of(L.Line("a",-10,0,0,0), L.Line("b",10,0,0,0)); // 둘 다 (0,0)으로 들어옴
        Assert.Contains(LayoutChecker.Check(l, O), x => x.Code == FindingCode.DeadEndNode && x.Where == new Point2(0,0));
    }
    [Fact] public void Duplicate_And_Unnamed_Cp() {
        var l = L.Of(L.Line("a",0,0,10,0)); l.Cps.Add(L.Cp("T001", 2,0)); l.Cps.Add(L.Cp("T001", 4,0)); l.Cps.Add(L.Cp("", 6,0));
        var f = LayoutChecker.Check(l, O);
        Assert.Equal(2, f.Count(x => x.Code == FindingCode.DuplicateCpName)); Assert.Single(f, x => x.Code == FindingCode.UnnamedCp);
    }
    [Fact] public void Cp_Off_Path() {
        var l = L.Of(L.Line("a",0,0,10,0)); l.Cps.Add(L.Cp("T001", 5, 0.06));
        Assert.Single(LayoutChecker.Check(l, O), x => x.Code == FindingCode.CpOffPath);
    }
    [Fact] public void Block_Touches_No_Path() {
        var l = L.Of(L.Line("a",0,0,10,0)); l.Blocks.Add(L.Block("B001", 20,20, 25,25));
        Assert.Single(LayoutChecker.Check(l, O), x => x.Code == FindingCode.BlockTouchesNoPath);
    }
    [Fact] public void Unsplit_Intersection() {
        var l = L.Of(L.Line("h",-10,0,10,0), L.Line("v",0,-10,0,10));
        Assert.Single(LayoutChecker.Check(l, O), x => x.Code == FindingCode.UnsplitIntersection && x.Where == new Point2(0,0));
    }
    [Fact] public void Route_Cp_Missing_Only_When_List_Given() {
        var l = L.Of(L.Line("a",0,0,10,0)); l.Cps.Add(L.Cp("L001", 2,0));
        Assert.DoesNotContain(LayoutChecker.Check(l, O), x => x.Code == FindingCode.RouteCpMissing);
        Assert.Single(LayoutChecker.Check(l, new CheckOptions(RouteCpNames: new[]{"L001","U001"})), x => x.Code == FindingCode.RouteCpMissing && x.Message.Contains("U001"));
    }
}
