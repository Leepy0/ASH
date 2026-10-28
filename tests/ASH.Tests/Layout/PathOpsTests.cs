using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;

namespace ASH.Tests.Layout;

public class PathOpsTests
{
    [Fact] public void Reverse_Swaps_Ends() { var r = PathOps.Reverse(L.Line("a",0,0,10,0)); Assert.Equal(new Point2(10,0), r.Start); Assert.Equal(new Point2(0,0), r.End); }
    [Fact] public void Offset_Left_Of_Direction() { var o = PathOps.Offset(L.Line("a",0,0,10,0), 2, reverseDirection: true); Assert.Equal(new Point2(10,2), o.Start); Assert.Equal(new Point2(0,2), o.End); }
    [Fact] public void Offset_Rejoins_Line_Corner()
    {
        var p = L.Line("a",0,0,10,0); p.Segments.Add(new LineSeg(new Point2(10,0), new Point2(10,10)));
        var o = PathOps.Offset(p, 1, reverseDirection: false);
        Assert.Equal(new Point2(9,1), o.Segments[0].End); Assert.Equal(new Point2(9,1), o.Segments[1].Start); Assert.Equal(new Point2(9,10), o.End);
    }
    [Fact] public void Array_Names_And_Positions() { var a = PathOps.Array(L.Line("a",0,0,1,0), 0, 5, 3); Assert.Equal(new[]{"a_1","a_2","a_3"}, a.Select(p => p.Name)); Assert.Equal(15, a[2].Start.Y); }
    [Fact] public void Join_Requires_Touching_Ends() { Assert.NotNull(PathOps.Join(L.Line("a",0,0,5,0), L.Line("b",5,0,10,0), 0.001)); Assert.Null(PathOps.Join(L.Line("a",0,0,5,0), L.Line("b",6,0,10,0), 0.001)); }
    [Fact] public void Fillet_Inserts_Ccw_Arc_On_Left_Turn()
    {
        var f = PathOps.Fillet(L.Line("a",0,0,10,0), L.Line("b",10,0,10,10), 2, 0.001)!.Value;
        Assert.Equal(8, f.A.End.X, 9); Assert.Equal(2, f.B.Start.Y, 9);
        var arc = Assert.IsType<ArcSeg>(f.Fillet.Segments[0]); Assert.Equal(90, arc.SweepDeg, 9); Assert.Equal(new Point2(8,2), arc.Center);
    }

    [Fact]
    public void Fillet_Extended_Bridges_A_Gap_In_Either_Order()
    {
        // a stops 2 short of the corner, b starts 2 above it: the lines meet at (10, 0)
        foreach (var (x, y) in new[] { (L.Line("a", 0, 0, 8, 0), L.Line("b", 10, 2, 10, 10)), (L.Line("b", 10, 2, 10, 10), L.Line("a", 0, 0, 8, 0)) })
        {
            Assert.Null(PathOps.Fillet(x, y, 2, 0.001));
            var f = PathOps.FilletExtended(x, y, 2, 0.001)!.Value;
            Assert.Equal("a", f.A.Name); Assert.Equal("b", f.B.Name);
            Assert.Equal(8, f.A.End.X, 9); Assert.Equal(0, f.A.End.Y, 9); Assert.Equal(10, f.B.Start.X, 9); Assert.Equal(2, f.B.Start.Y, 9);
            var arc = Assert.IsType<ArcSeg>(f.Fillet.Segments[0]); Assert.Equal(90, arc.SweepDeg, 9); Assert.Equal(8, arc.Center.X, 9); Assert.Equal(2, arc.Center.Y, 9);
        }
    }

    [Fact]
    public void Fillet_Extended_Trims_Paths_That_Run_Past_Each_Other_And_Refuses_Parallels()
    {
        var f = PathOps.FilletExtended(L.Line("a", 0, 0, 12, 0), L.Line("b", 10, -2, 10, 10), 2, 0.001)!.Value;
        Assert.Equal(8, f.A.End.X, 9); Assert.Equal(0, f.A.End.Y, 9);
        Assert.Equal(10, f.B.Start.X, 9); Assert.Equal(2, f.B.Start.Y, 9);
        Assert.Null(PathOps.FilletExtended(L.Line("a", 0, 0, 8, 0), L.Line("b", 10, 2, 20, 2), 2, 0.001));   // parallel
        Assert.Null(PathOps.FilletExtended(L.Line("a", 0, 0, 8, 0), L.Line("b", 10, 1, 10, 2), 3, 0.001));   // too short for the radius
    }

    [Fact]
    public void Fillet_Inserts_Cw_Arc_On_Right_Turn()
    {
        var f = PathOps.Fillet(L.Line("a",0,0,10,0), L.Line("b",10,0,10,-10), 2, 0.001)!.Value;
        Assert.Equal(8, f.A.End.X, 9); Assert.Equal(-2, f.B.Start.Y, 9);
        var arc = Assert.IsType<ArcSeg>(f.Fillet.Segments[0]); Assert.Equal(-90, arc.SweepDeg, 9);
        Assert.Equal(8, arc.Center.X, 9); Assert.Equal(-2, arc.Center.Y, 9);
    }

    [Theory]
    [InlineData(90, 4)]   // CCW: left is toward the centre
    [InlineData(-90, 6)]  // CW: left is away from the centre
    public void Offset_Arc_Changes_Radius_By_Turn_Direction(double sweep, double radius)
    {
        var p = new LayoutPath("a") { Segments = { new ArcSeg(new Point2(0, 0), new Point2(5, 0), sweep) } };
        var arc = Assert.IsType<ArcSeg>(PathOps.Offset(p, 1, reverseDirection: false).Segments[0]);
        Assert.Equal(radius, arc.Radius, 9); Assert.Equal(sweep, arc.SweepDeg, 9); Assert.Equal(new Point2(0, 0), arc.Center);
    }

    [Fact] public void Array_Custom_Suffix_Start() => Assert.Equal(new[]{"a_5","a_6"}, PathOps.Array(L.Line("a",0,0,1,0), 1, 0, 2, "_5").Select(p => p.Name));
}
