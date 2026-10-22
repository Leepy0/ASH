using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Traffic;
using ASH.Tests.Layout;
using Xunit;

namespace ASH.Tests.Traffic;

public class BlockGeneratorTests
{
    static readonly TrafficParams P = new(VehicleLength: 2.0, VehicleWidth: 1.0, SafetyGap: 0.5);   // right-angle clearance 0.5 + 0.5, merge gap 3.0
    [Fact] public void Cross_Gives_One_Tight_Block()
    {
        // a vehicle at the rect edge is half a width plus the gap from the crossing lane; the old reach (length + gap, +width/2 + gap) made it 7 x 7
        var l = L.Of(L.Line("a",-10,0,0,0), L.Line("b",0,0,10,0), L.Line("c",0,-10,0,0), L.Line("d",0,0,0,10));
        var r = BlockGenerator.Generate(l, P);
        var b = Assert.Single(r.Blocks); Assert.Equal("B001", b.Name); Assert.Equal(1, b.Cap);
        Assert.Equal(new Rect2(new(-1,-1), new(1,1)), b.Rect);
        Assert.Empty(r.Warnings);
    }
    [Fact] public void T_Junction_Reaches_Only_Toward_The_Branch()
    {
        var b = Assert.Single(BlockGenerator.Generate(L.Of(L.Line("a",-10,0,0,0), L.Line("b",0,0,10,0), L.Line("c",0,-10,0,0)), P).Blocks);
        Assert.Equal(new Rect2(new(-1,-1), new(1,0.5)), b.Rect);
    }
    [Fact] public void Shallow_Merge_Reaches_Further_Along_The_Converging_Arms()
    {
        // 45° merge: a and c stay within a body width of each other for longer (a vehicle's corner leans toward the other lane)
        var b = Assert.Single(BlockGenerator.Generate(L.Of(L.Line("a",-10,0,0,0), L.Line("c",-10,-10,0,0), L.Line("b",0,0,10,0)), P).Blocks);
        Assert.Equal(-1.95, b.Rect.Min.X, 6);   // a clears at 1.95 (0.707 s >= 1 + 0.354)
        Assert.Equal(1.4, b.Rect.Max.X, 6);     // the exit clears c's lane at 1.4
    }
    [Fact] public void Short_Arm_Clamps_At_Path_End()
    {
        var l = L.Of(L.Line("a",-10,0,0,0), L.Line("b",0,0,0.3,0), L.Line("c",0,-10,0,0));
        var b = Assert.Single(BlockGenerator.Generate(l, P).Blocks);
        Assert.Equal(0.8, b.Rect.Max.X, 9);   // 0.3 + half width
    }
    [Fact] public void Stub_Shorter_Than_A_Vehicle_Before_A_Corner_Is_Taken_Whole()
    {
        // c turns the corner 1.5 m below the junction: 1.5 - 0.5 leaves 1.0 < vehicle 2 + gap, so the block runs past the corner (entry CP goes on c0)
        var l = L.Of(L.Line("a",-10,0,0,0), L.Line("b",0,0,10,0), L.Line("c0",-10,-1.5,0,-1.5), L.Line("c",0,-1.5,0,0));
        Assert.Equal(-2.0, Assert.Single(BlockGenerator.Generate(l, P).Blocks).Rect.Min.Y, 9);
    }
    [Fact] public void Block_Reach_Setting_Fixes_The_Arm_Length()
    {
        var l = L.Of(L.Line("a",-10,0,0,0), L.Line("b",0,0,10,0), L.Line("c",0,-10,0,0), L.Line("d",0,0,0,10));
        Assert.Equal(new Rect2(new(-2.5,-2.5), new(2.5,2.5)), Assert.Single(BlockGenerator.Generate(l, P with { BlockReach = 2.0 }).Blocks).Rect);
    }
    [Fact] public void Two_Close_Junctions_Reported_As_Overlap_Group()
    {
        // rects 1 m apart: no room for a 2 m vehicle to wait between them
        var l = L.Of(L.Line("a",-10,0,0,0), L.Line("b",0,0,3,0), L.Line("c",3,0,13,0), L.Line("d",0,0,0,10), L.Line("e",3,0,3,10));
        var r = BlockGenerator.Generate(l, P);
        Assert.Equal(2, r.Blocks.Count); var g = Assert.Single(r.OverlapGroups); Assert.Equal(new[]{"B001","B002"}, g);
    }
    [Fact] public void Junctions_With_Room_For_A_Vehicle_Between_Stay_Separate()
    {
        // rects 4 m apart > vehicle 2 + gaps 1: one block each (the old 7 m blocks overlapped here and were merged into one 13 m block)
        var l = L.Of(L.Line("a",-10,0,0,0), L.Line("b",0,0,6,0), L.Line("c",6,0,16,0), L.Line("d",0,0,0,10), L.Line("e",6,0,6,10));
        var r = BlockGenerator.Generate(l, P);
        Assert.Equal(2, r.Blocks.Count); Assert.Empty(r.OverlapGroups);
    }
    [Fact] public void Stop_Cp_Inside_A_Block_Is_Warned()
    {
        var l = L.Of(L.Line("a",-10,0,0,0), L.Line("b",0,0,10,0), L.Line("c",0,-10,0,0));
        l.Cps.Add(L.Cp("L001", 0.5, 0, "load")); l.Cps.Add(L.Cp("S001", 2, 0, "standby")); l.Cps.Add(L.Cp("T001", -0.5, 0));
        var w = Assert.Single(BlockGenerator.Generate(l, P).Warnings);
        Assert.Contains("L001", w); Assert.DoesNotContain("S001", w); Assert.DoesNotContain("T001", w);
    }
    [Fact] public void Existing_Block_Covering_Node_Is_Kept()
    {
        var l = L.Of(L.Line("a",-10,0,0,0), L.Line("b",0,0,10,0), L.Line("c",0,-10,0,0)); l.Blocks.Add(L.Block("B007",-1,-1,1,1));
        var r = BlockGenerator.Generate(l, P); Assert.Empty(r.Blocks);
    }
    [Fact] public void Renumber_From_The_Origin_Ignoring_Old_Names()
    {
        // centres: far (20,0), near (2,0), and two at distance 5: (4,3) and (3,4) -> lower y first
        var far = L.Block("B001",19,-1,21,1); var near = L.Block("X9",1,-1,3,1); var a = L.Block("B002",3,2,5,4); var b = L.Block("",2,3,4,5);
        var r = BlockGenerator.Renumber(new[] { far, b, near, a });
        Assert.Equal(new[] { near, a, b, far }, r.Select(x => x.Block));
        Assert.Equal(new[] { "B001", "B002", "B003", "B004" }, r.Select(x => x.Name));
    }
    [Fact] public void Merge_Is_Union_Bbox() => Assert.Equal(new Rect2(new(0,0), new(5,5)), BlockGenerator.Merge(new[]{ L.Block("x",0,0,2,2), L.Block("y",3,3,5,5) }, "B001").Rect);

    [Fact] public void Corridor_Blocks_Between_Adjacent_Traffic_Cps()
    {
        var l = L.Of(L.Line("a",0,0,30,0)); l.Cps.Add(L.Cp("T001",0,0)); l.Cps.Add(L.Cp("T002",10,0)); l.Cps.Add(L.Cp("T003",20,0));
        var r = BlockGenerator.GenerateCorridorBlocks(l, P);
        Assert.Equal(2, r.Count);
        Assert.Equal(new Rect2(new(-1,-1), new(11,1)), r[0].Rect);
    }
}
