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
    [Fact] public void Dangling_End_Of_Two_Way_Path_Not_Reported() {
        var two = L.Line("t",5,0,5,3); two.IsTwoWay = true;
        var f = LayoutChecker.Check(L.Of(L.Line("a",0,0,10,0), two), O).Where(x => x.Code == FindingCode.DanglingEnd).ToList();
        Assert.Equal(2, f.Count); Assert.All(f, x => Assert.Equal("a", x.EntityId));
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
    [Fact] public void Duplicate_Path_Name_Flagged_Per_Path() {
        var l = L.Of(L.Line("a",0,0,10,0), L.Line("a",10,0,20,0), L.Line("b",20,0,30,0));
        var f = LayoutChecker.Check(l, O).Where(x => x.Code == FindingCode.DuplicatePathName).ToList();
        Assert.Equal(2, f.Count); Assert.All(f, x => Assert.Equal("a", x.EntityId));
    }
    [Fact] public void Invalid_Names_Flagged() {
        var l = L.Of(L.Line("a b",0,0,10,0), L.Line("",10,0,20,0), L.Line("ANGLE",20,0,30,0), L.Line("ok",30,0,40,0));
        l.Cps.Add(L.Cp("cap", 2, 0)); l.Cps.Add(L.Cp("T 1", 4, 0)); l.Cps.Add(L.Cp("T2", 6, 0));
        l.Blocks.Add(L.Block("", 0,-1, 1,1)); l.Blocks.Add(L.Block("B 1", 2,-1, 3,1)); l.Blocks.Add(L.Block("B1", 4,-1, 5,1));
        var f = LayoutChecker.Check(l, O).Where(x => x.Code == FindingCode.InvalidName).ToList();
        Assert.Equal(7, f.Count);
        Assert.DoesNotContain(f, x => x.EntityId == "ok" || x.EntityId == "T2" || x.EntityId == "B1");
    }
    [Fact] public void Question_Mark_Path_Name_Is_Invalid() =>
        Assert.Single(LayoutChecker.Check(L.Of(L.Line("?1F",0,0,10,0)), O), x => x.Code == FindingCode.InvalidName && x.EntityId == "?1F");
    [Fact] public void Duplicate_Block_Name_Flagged_Per_Block() {
        var l = L.Of(L.Line("a",0,0,10,0)); l.Blocks.Add(L.Block("B001",0,-1,1,1)); l.Blocks.Add(L.Block("B001",2,-1,3,1)); l.Blocks.Add(L.Block("B002",4,-1,5,1));
        var f = LayoutChecker.Check(l, O).Where(x => x.Code == FindingCode.DuplicateBlockName).ToList();
        Assert.Equal(2, f.Count); Assert.All(f, x => Assert.Equal("B001", x.EntityId));
    }
    [Fact] public void PathRenamer_Keeps_First_Duplicate_And_Renames_Rest() {
        var a1 = L.Line("a",0,0,1,0); var a2 = L.Line("a",1,0,2,0); var q = L.Line("?1F",2,0,3,0); var b = L.Line("b",3,0,4,0);
        var plan = PathRenamer.Plan(new[] { a1, a2, q, b });
        Assert.Equal(2, plan.Count); Assert.Equal("P001", plan[a2.Id]); Assert.Equal("P002", plan[q.Id]);
    }
}
