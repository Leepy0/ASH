using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using ASH.Core.Settings;
using ASH.Core.Traffic;

namespace ASH.Tests.Layout;

/// <summary>Fillet inside one polyline, offset side detection, CP capacity/colour rules, the new default Scale.</summary>
public class PolylineFilletTests
{
    static LayoutPath Poly(string name, params (double X, double Y)[] pts)
    {
        var p = new LayoutPath(name);
        for (int i = 0; i + 1 < pts.Length; i++) p.Segments.Add(new LineSeg(new Point2(pts[i].X, pts[i].Y), new Point2(pts[i + 1].X, pts[i + 1].Y)));
        return p;
    }

    [Fact] public void An_L_polyline_gets_a_quarter_circle_at_its_corner()
    {
        var r = PathOps.FilletCorners(Poly("P001", (0, 0), (10, 0), (10, 10)), 2, 1e-6);
        Assert.NotNull(r);
        var (path, rounded, skipped) = r!.Value;
        Assert.Equal(1, rounded);
        Assert.Empty(skipped);
        Assert.Equal("P001", path.Name);
        Assert.Equal(3, path.Segments.Count);
        var arc = Assert.IsType<ArcSeg>(path.Segments[1]);
        Assert.Equal(90, arc.SweepDeg, 6);
        Assert.Equal(2, arc.Radius, 9);
        Assert.Equal(8, path.Segments[0].End.X, 9);
        Assert.Equal(0, path.Segments[0].End.Y, 9);
        Assert.Equal(10, path.Segments[2].Start.X, 9);
        Assert.Equal(2, path.Segments[2].Start.Y, 9);
        Assert.Equal(path.Segments[0].End.X, arc.Start.X, 9);          // the pieces meet end to start
        Assert.Equal(arc.End.X, path.Segments[2].Start.X, 9);
        Assert.Equal(arc.End.Y, path.Segments[2].Start.Y, 9);
        Assert.Equal(path.Start, new Point2(0, 0));
        Assert.Equal(path.End, new Point2(10, 10));
    }

    [Fact] public void A_right_turn_gets_a_clockwise_arc()
    {
        var r = PathOps.FilletCorners(Poly("P", (0, 0), (10, 0), (10, -10)), 2, 1e-6)!.Value;
        Assert.Equal(-90, ((ArcSeg)r.Path.Segments[1]).SweepDeg, 6);
    }

    [Fact] public void Every_corner_of_a_zigzag_is_rounded_by_default()
    {
        var r = PathOps.FilletCorners(Poly("P", (0, 0), (10, 0), (10, 10), (20, 10), (20, 20)), 1, 1e-6)!.Value;
        Assert.Equal(3, r.Rounded);
        Assert.Equal(7, r.Path.Segments.Count);
        Assert.Equal(new Point2(0, 0), r.Path.Start);
        Assert.Equal(new Point2(20, 20), r.Path.End);
        for (int i = 0; i + 1 < r.Path.Segments.Count; i++)   // continuous
        {
            Assert.Equal(r.Path.Segments[i].End.X, r.Path.Segments[i + 1].Start.X, 9);
            Assert.Equal(r.Path.Segments[i].End.Y, r.Path.Segments[i + 1].Start.Y, 9);
        }
    }

    [Fact] public void Only_the_corner_nearest_the_pick_is_rounded()
    {
        var r = PathOps.FilletCorners(Poly("P", (0, 0), (10, 0), (10, 10), (20, 10)), 1, 1e-6, near: new Point2(10.4, 10.3))!.Value;
        Assert.Equal(1, r.Rounded);
        Assert.Equal(new Point2(10, 0), r.Path.Segments[0].End);        // first corner untouched
        Assert.IsType<ArcSeg>(r.Path.Segments[2]);                       // second corner rounded: line, trimmed line, arc, trimmed line
    }

    [Fact] public void A_corner_too_tight_for_the_radius_is_skipped_and_reported()
    {
        var r = PathOps.FilletCorners(Poly("P", (0, 0), (10, 0), (10, 1.5), (20, 1.5)), 2, 1e-6)!.Value;
        Assert.Equal(0, r.Rounded);
        Assert.Equal(2, r.Skipped.Count);
        Assert.Equal(3, r.Path.Segments.Count);                          // unchanged
    }

    [Fact] public void Neighbouring_corners_share_a_segment_without_overlapping()
    {
        // the middle segment is 4 long; two 90-degree corners with radius 1.5 trim 1.5 + 1.5 = 3 of it
        var r = PathOps.FilletCorners(Poly("P", (0, 0), (10, 0), (10, 4), (20, 4)), 1.5, 1e-6)!.Value;
        Assert.Equal(2, r.Rounded);
        // radius 2.5 would need 5 of the 4: the second corner is skipped
        var r2 = PathOps.FilletCorners(Poly("P", (0, 0), (10, 0), (10, 4), (20, 4)), 2.5, 1e-6)!.Value;
        Assert.Equal(1, r2.Rounded);
        Assert.Single(r2.Skipped);
    }

    [Fact] public void Straight_and_single_segment_paths_have_nothing_to_round()
    {
        Assert.Null(PathOps.FilletCorners(Poly("P", (0, 0), (10, 0)), 1, 1e-6));
        Assert.Null(PathOps.FilletCorners(Poly("P", (0, 0), (5, 0), (10, 0)), 1, 1e-6));     // collinear
    }

    [Fact] public void Type_and_flags_survive_the_fillet()
    {
        var p = Poly("P", (0, 0), (10, 0), (10, 10));
        p.IsTwoWay = true; p.IsCrab = true;
        var r = PathOps.FilletCorners(p, 2, 1e-6)!.Value.Path;
        Assert.True(r.IsTwoWay); Assert.True(r.IsCrab);
    }

    [Theory]
    [InlineData(5, 3, 1)]     // above a path running +X: left
    [InlineData(5, -3, -1)]   // below: right
    [InlineData(5, 0, 0)]     // on it
    public void Side_of_a_point_relative_to_the_direction_of_travel(double x, double y, int side)
        => Assert.Equal(side, PathOps.SideOf(Poly("P", (0, 0), (10, 0)), new Point2(x, y), 1e-6));

    [Fact] public void Side_follows_the_travel_direction_not_the_screen()
    {
        var back = Poly("P", (10, 0), (0, 0));          // travels -X: "above" is now on its right
        Assert.Equal(-1, PathOps.SideOf(back, new Point2(5, 3), 1e-6));
    }

    [Fact] public void Side_works_next_to_an_arc()
    {
        var arc = new LayoutPath("A"); arc.Segments.Add(new ArcSeg(new Point2(0, 0), new Point2(10, 0), 90)); // CCW quarter, centre on its left
        Assert.Equal(1, PathOps.SideOf(arc, new Point2(2, 2), 1e-6));    // inside the curve = left
        Assert.Equal(-1, PathOps.SideOf(arc, new Point2(9, 9), 1e-6));   // outside = right
    }

    // ---- CP types ----

    [Fact] public void Every_cp_defaults_to_unlimited_and_traffic_cap_one_has_its_own_layer()
    {
        foreach (var t in CpTypes.All) Assert.Equal(CpTypes.Infinity, CpTypes.DefaultCap(t));
        Assert.True(CpTypes.IsSingleTraffic(" Traffic ", 1));
        Assert.False(CpTypes.IsSingleTraffic("traffic", 2)); Assert.False(CpTypes.IsSingleTraffic("traffic", CpTypes.Infinity)); Assert.False(CpTypes.IsSingleTraffic("load", 1));
        Assert.NotEqual(ASH.Core.Layout.LayerColors.DefaultOf("AGV_CP_TRAFFIC"), ASH.Core.Layout.LayerColors.DefaultOf(CpTypes.SingleTrafficLayer));
    }

    [Theory]
    [InlineData("INF", true, int.MaxValue)]
    [InlineData("inf", true, int.MaxValue)]
    [InlineData(" Infinity ", true, int.MaxValue)]
    [InlineData("무한", true, int.MaxValue)]
    [InlineData("3", true, 3)]
    [InlineData("0", false, 1)]
    [InlineData("-2", false, 1)]
    [InlineData("abc", false, 1)]
    [InlineData("", false, 1)]
    public void Cap_text_is_parsed(string text, bool ok, int cap)
    {
        Assert.Equal(ok, CpTypes.TryParseCap(text, out var c));
        Assert.Equal(cap, c);
    }

    [Fact] public void Cap_is_written_as_INF_when_unlimited() { Assert.Equal("INF", CpTypes.FormatCap(int.MaxValue)); Assert.Equal("1", CpTypes.FormatCap(1)); Assert.Equal("1", CpTypes.FormatCap(0)); }

    [Fact] public void Every_cp_type_has_its_own_colour_and_prefix()
    {
        Assert.Equal(CpTypes.All.Length, CpTypes.All.Select(CpTypes.ColorIndex).Distinct().Count());
        Assert.Equal(CpTypes.All.Length, CpTypes.All.Select(AutoNamer.PrefixFor).Distinct().Count());
        Assert.Equal("L###", AutoNamer.PatternFor("load"));
    }

    // ---- settings ----

    [Theory]
    [InlineData(4, 0.001)]
    [InlineData(5, 0.01)]
    [InlineData(6, 1.0)]
    [InlineData(1, 0.0254)]
    [InlineData(2, 0.3048)]
    [InlineData(0, 0.001)]     // no unit set: AGV layouts are drawn in mm
    [InlineData(99, 0.001)]
    public void New_settings_scale_comes_from_the_drawing_units(int insunits, double scale) => Assert.Equal(scale, UnitScale.FromInsUnits(insunits));

    [Fact] public void Default_scale_is_millimetres() => Assert.Equal(0.001, new ProjectSettings().Scale);
}
