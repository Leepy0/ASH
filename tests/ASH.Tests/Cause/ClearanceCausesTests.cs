using System.Linq;
using ASH.Core.Cause;
using ASH.Core.Geometry;
using ASH.Core.Input;
using ASH.Core.Layout;
using ASH.Tests.Layout;
using Xunit;

namespace ASH.Tests.Cause;

public class ClearanceCausesTests
{
    // vehicle 1.0 m wide + gap 0.5 -> a path must keep 1.0 m from an obstacle's edge
    static CauseContext Ctx(ASH.Core.Layout.Layout l) => new(l, new ProjectInput(), VehicleWidth: 1.0, SafetyGap: 0.5);
    static ASH.Core.Layout.Layout Lay(params LayoutAsset[] assets)
    {
        var l = L.Of(L.Line("P1", 0, 0, 10, 0));
        l.Assets.AddRange(assets);
        return l;
    }
    static LayoutAsset Line(string kind, double x1, double y1, double x2, double y2, double w = 0) =>
        new() { Kind = kind, WidthM = w, Segments = { new LineSeg(new Point2(x1, y1), new Point2(x2, y2)) } };
    static LayoutAsset Block(string kind, double x, double y, double rot = 0) => new() { Kind = kind, X = x, Y = y, RotDeg = rot };

    [Fact] public void Wall_Far_Enough_Is_Quiet_Near_Is_A_Gap()
    {
        Assert.Empty(ClearanceCauses.Find(Ctx(Lay(Line("WALL", 0, 1.5, 10, 1.5, 0.2))))); // edge 1.4 m away
        var i = Assert.Single(ClearanceCauses.Find(Ctx(Lay(Line("WALL", 0, 1.0, 10, 1.0, 0.2))))); // edge 0.9 m away
        Assert.Equal(CauseKinds.Clearance, i.Kind); Assert.Equal(1, i.Severity);
        Assert.Equal("P1 간격 0.9 m", i.Title); Assert.Equal("기준 1.0 m · 벽", i.Detail); Assert.Equal("P1", i.Where); Assert.Equal(new[] { "P1" }, i.Refs);
    }

    [Fact] public void Path_Through_A_Wall_Or_Fence_Is_A_Crossing()
    {
        var w = Assert.Single(ClearanceCauses.Find(Ctx(Lay(Line("WALL", 5, -3, 5, 3, 0.2)))));
        Assert.Equal("P1·벽 교차", w.Title); Assert.Equal(2, w.Severity); Assert.Equal(5, w.X!.Value, 0);
        Assert.Equal("P1·펜스 교차", Assert.Single(ClearanceCauses.Find(Ctx(Lay(Line("FENCE", 5, -3, 5, 3))))).Title);
    }

    [Fact] public void Door_Opening_Is_Free_Its_Wall_Pieces_Are_Not()
    {
        var l = L.Of(L.Line("P1", 5, -6, 5, 6)); // through the opening of a door at the origin, wall along x
        l.Assets.Add(Block("DOOR", 5, 0)); // 6 m wide, 2.2 m opening: 1.1 m from the path to each wall piece
        Assert.Empty(ClearanceCauses.Find(Ctx(l)));
        var off = L.Of(L.Line("P1", 3.5, -6, 3.5, 6)); // 3.5 is inside the wall piece (x 2..3.9)
        off.Assets.Add(Block("DOOR", 5, 0));
        Assert.Equal("P1·출입문 교차", Assert.Single(ClearanceCauses.Find(Ctx(off))).Title);
    }

    [Fact] public void Blocks_Use_Their_Rotated_Footprint_Floor_Zones_Are_Ignored()
    {
        var m = Assert.Single(ClearanceCauses.Find(Ctx(Lay(Block("MACHINE", 5, 1.2))))); // 2.5 x 2 m: edge 0.2 m from the path
        Assert.Equal("P1 간격 0.2 m", m.Title); Assert.Contains("설비", m.Detail);
        Assert.Empty(ClearanceCauses.Find(Ctx(Lay(Block("RACK", 5, 2))))); // 3 x 1.1 m, edge 1.45 m away
        Assert.Equal("P1 간격 0.5 m", Assert.Single(ClearanceCauses.Find(Ctx(Lay(Block("RACK", 5, 2, 90))))).Title); // turned: 3 m deep, edge 0.5 m
        Assert.Empty(ClearanceCauses.Find(Ctx(Lay(Block("ZONE", 5, 0)))));
    }

    [Fact] public void Conveyors_Are_Obstacles_Of_Their_Width()
    {
        var l = Lay();
        l.Conveyors.Add(new LayoutConveyor { Name = "CV001", WidthM = 1.0, Segments = { new LineSeg(new Point2(0, 1.2), new Point2(10, 1.2)) } }); // edge 0.7 m away
        var i = Assert.Single(ClearanceCauses.Find(Ctx(l)));
        Assert.Equal("P1 간격 0.7 m", i.Title); Assert.Contains("컨베이어", i.Detail);
    }

    [Fact] public void Wider_Vehicle_Or_Gap_Raises_The_Bar_And_Static_Includes_It()
    {
        var l = Lay(Line("WALL", 0, 1.5, 10, 1.5, 0.2));
        Assert.Single(ClearanceCauses.Find(Ctx(l) with { VehicleWidth = 2.0 })); // need 1.5, edge 1.4
        Assert.Contains(CauseAnalysis.Static(Ctx(l) with { SafetyGap = 1.0 }), i => i.Kind == CauseKinds.Clearance);
    }
}
