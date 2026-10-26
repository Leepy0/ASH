using System.Linq;
using ASH.Core.Cause;
using ASH.Core.Geometry;
using ASH.Core.Input;
using ASH.Core.Layout;
using ASH.Core.Settings;
using Xunit;

namespace ASH.Tests.Cause;

public class CauseFrameTests
{
    static ASH.Core.Layout.Layout Drawing()
    {
        var l = new ASH.Core.Layout.Layout { Tolerance = 1 };
        var p = new LayoutPath("P1") { IsTwoWay = true };
        p.Segments.Add(new LineSeg(new Point2(1000, 2000), new Point2(11000, 2000)));
        p.Segments.Add(new ArcSeg(new Point2(11000, 7000), new Point2(11000, 2000), 90));
        l.Paths.Add(p);
        l.Cps.Add(new LayoutCp { Name = "CP1", Group = "G1", Position = new Point2(5000, 2000) });
        l.Blocks.Add(new LayoutBlock { Name = "B001", Cap = 1, Rect = new Rect2(new Point2(4000, 1000), new Point2(6000, 3000)) });
        l.Stations.Add(new LayoutStation { Name = "S1", Position = new Point2(1000, 2000) });
        return l;
    }

    [Fact] public void ToMetres_Moves_The_Origin_And_Scales_Every_Part()
    {
        var m = CauseFrame.ToMetres(Drawing(), new Point2(1000, 2000), 0.001); // mm drawing, Origin at the path start
        var p = m.Paths.Single();
        Assert.Equal("P1", p.Name); Assert.True(p.IsTwoWay);
        Assert.Equal(new Point2(0, 0), p.Start); Assert.Equal(new Point2(10, 0), p.Segments[0].End);
        var arc = Assert.IsType<ArcSeg>(p.Segments[1]);
        Assert.Equal(new Point2(10, 5), arc.Center); Assert.Equal(90, arc.SweepDeg); Assert.Equal(5, arc.Radius, 9); Assert.Equal(10, p.Length - arc.Length, 9);
        Assert.Equal(new Point2(4, 0), m.Cps.Single().Position); Assert.Equal("G1", m.Cps.Single().Group);
        var b = m.Blocks.Single(); Assert.Equal(new Point2(3, -1), b.Rect.Min); Assert.Equal(new Point2(5, 1), b.Rect.Max);
        Assert.Equal(new Point2(0, 0), m.Stations.Single().Position); Assert.Equal(0.001, m.Tolerance, 12);
    }

    [Fact] public void ToDrawing_Is_The_Inverse()
    {
        var o = new Point2(1000, 2000); const double k = 0.001;
        var m = CauseFrame.ToMetres(Drawing(), o, k);
        var back = CauseFrame.ToDrawing(m.Cps.Single().Position.X, m.Cps.Single().Position.Y, o, k);
        Assert.Equal(5000, back.X, 6); Assert.Equal(2000, back.Y, 6);
    }

    [Fact] public void Context_Takes_Settings_When_There_Are_Some_Else_The_First_Agv_Type()
    {
        var l = new ASH.Core.Layout.Layout(); var input = new ProjectInput();
        var none = CauseFrame.Context(l, input, null);
        Assert.Equal(2.0, none.VehicleLength); Assert.Equal(1.0, none.VehicleWidth); Assert.Equal(0.5, none.ClaimDist); Assert.Equal(0, none.BlockReach); // ProjectSettings defaults
        input.AgvTypes.Add(new AgvType("T0", 1, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, "", Row: 0)); // no size: skipped
        input.AgvTypes.Add(new AgvType("T1", 1, 1, 0, 0, 1, 0, 0, 0, 0, 1.8, 0.9, ""));
        var types = CauseFrame.Context(l, input, null);
        Assert.Equal(1.8, types.VehicleLength); Assert.Equal(0.9, types.VehicleWidth);
        var s = CauseFrame.Context(l, input, new ProjectSettings(VehicleLength: 3, VehicleWidth: 1.2, ClaimDist: 2, BlockReach: 4));
        Assert.Equal(3, s.VehicleLength); Assert.Equal(1.2, s.VehicleWidth); Assert.Equal(2, s.ClaimDist); Assert.Equal(4, s.BlockReach);
    }

    [Fact] public void StaticOf_Runs_On_A_Drawing_Without_An_Excel_Input()
    {
        var s = new ProjectSettings(Scale: 0.001);
        Assert.DoesNotContain(CauseFrame.StaticOf(Drawing(), s, null), i => i.Kind == CauseKinds.Detour); // no Routes: no detour findings
        Assert.NotNull(CauseFrame.StaticOf(Drawing(), s, new ProjectInput()));
    }
}
