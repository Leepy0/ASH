using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Traffic;
using ASH.Tests.Layout;
using Xunit;

namespace ASH.Tests.Traffic;

public class BlockGeneratorTests
{
    static readonly TrafficParams P = new(VehicleLength: 2.0, VehicleWidth: 1.0, SafetyGap: 0.5);   // reach 2.5, expand 1.0
    [Fact] public void Cross_Gives_One_Block_Of_Expected_Size()
    {
        var l = L.Of(L.Line("a",-10,0,0,0), L.Line("b",0,0,10,0), L.Line("c",0,-10,0,0), L.Line("d",0,0,0,10));
        var r = BlockGenerator.Generate(l, P);
        var b = Assert.Single(r.Blocks); Assert.Equal("B001", b.Name); Assert.Equal(1, b.Cap);
        Assert.Equal(new Rect2(new(-3.5,-3.5), new(3.5,3.5)), b.Rect);
    }
    [Fact] public void Short_Arm_Clamps_At_Path_End()
    {
        var l = L.Of(L.Line("a",-10,0,0,0), L.Line("b",0,0,1,0), L.Line("c",0,-10,0,0));
        var b = Assert.Single(BlockGenerator.Generate(l, P).Blocks);
        Assert.Equal(2.0, b.Rect.Max.X, 9);   // 1.0 + expand 1.0
    }
    [Fact] public void Two_Close_Junctions_Reported_As_Overlap_Group()
    {
        var l = L.Of(L.Line("a",-10,0,0,0), L.Line("b",0,0,3,0), L.Line("c",3,0,13,0), L.Line("d",0,0,0,10), L.Line("e",3,0,3,10));
        var r = BlockGenerator.Generate(l, P);
        Assert.Equal(2, r.Blocks.Count); var g = Assert.Single(r.OverlapGroups); Assert.Equal(new[]{"B001","B002"}, g);
    }
    [Fact] public void Existing_Block_Covering_Node_Is_Kept()
    {
        var l = L.Of(L.Line("a",-10,0,0,0), L.Line("b",0,0,10,0), L.Line("c",0,-10,0,0)); l.Blocks.Add(L.Block("B007",-1,-1,1,1));
        var r = BlockGenerator.Generate(l, P); Assert.Empty(r.Blocks);
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
