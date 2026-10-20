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
}
