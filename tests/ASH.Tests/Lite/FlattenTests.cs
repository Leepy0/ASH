using ASH.Core.Cel;
using ASH.Core.Lite;

namespace ASH.Tests.Lite;

public class Affine3Tests
{
    static readonly Affine3 RotX90 = new(1, 0, 0, 0, 0, 0, -1, 0, 0, 1, 0, 0); // y' = -z, z' = y

    [Fact]
    public void From_Reads_Matrix3d_ToArray_Row_Major()
    {
        var m = Affine3.From(new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 0, 0, 0, 1 });
        Assert.Equal((1 + 2 * 2 + 3 * 3 + 4.0, 5 + 6 * 2 + 7 * 3 + 8.0, 9 + 10 * 2 + 11 * 3 + 12.0), m.Ap(1, 2, 3));
    }

    [Fact]
    public void Then_Applies_This_First()
    {
        var move = new Affine3(1, 0, 0, 0, 0, 1, 0, 7, 0, 0, 1, 0);
        var p = RotX90.Ap(1, 2, 3);
        var q = move.Ap(p.X, p.Y, p.Z);
        Assert.Equal(q, RotX90.Then(move).Ap(1, 2, 3));
        Assert.NotEqual(q, move.Then(RotX90).Ap(1, 2, 3));
    }

    [Fact]
    public void Plan_Drops_The_Z_Column_And_PlanHasZ_Reports_It()
    {
        var m = new Affine3(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
        Assert.Equal(new Affine2(1, 2, 5, 6, 4, 8), m.Plan);
        Assert.True(m.PlanHasZ);
        Assert.True(RotX90.PlanHasZ);
        Assert.False(Affine3.Identity.PlanHasZ);
        Assert.False(new Affine3(0, -2, 0, 5, 2, 0, 0, 6, 0, 0, 2, 9).PlanHasZ); // rotated and lifted in Z
    }

    [Fact]
    public void Linear_Clears_The_Translation_Only()
    {
        var l = new Affine3(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12).Linear;
        Assert.Equal(new Affine3(1, 2, 3, 0, 5, 6, 7, 0, 9, 10, 11, 0), l);
    }
}

public class FlattenTests
{
    const double Tol = 0.1, Eps = 0.01;
    static readonly Vec3 Up = new(0, 0, 1), Down = new(0, 0, -1), East = new(1, 0, 0), Origin = new(0, 0, 0);
    static readonly Affine3 Id = Affine3.Identity;
    static readonly Affine3 RotX90 = new(1, 0, 0, 0, 0, 0, -1, 0, 0, 1, 0, 0); // y' = -z, z' = y
    static readonly double Sin30 = Math.Sin(Math.PI / 6), Cos30 = Math.Cos(Math.PI / 6);

    static Affine3 RotZ(double deg, double dx = 0, double dy = 0, double dz = 0)
    {
        double c = Math.Cos(deg * Math.PI / 180), s = Math.Sin(deg * Math.PI / 180);
        return new Affine3(c, -s, 0, dx, s, c, 0, dy, 0, 0, 1, dz);
    }

    static LiteVertex[] Square => new[] { new LiteVertex(0, 0), new LiteVertex(10, 0), new LiteVertex(10, 10), new LiteVertex(0, 10) };

    [Fact]
    public void Square_In_Local_XY_Under_RotX90_Is_One_Line()
    {
        var l = Assert.IsType<LiteLine>(Flatten.To2D(new Poly3("A", Square, true, Up, 0), RotX90, Tol, "L"));
        Assert.Equal("L", l.Layer);
        Assert.Equal(new LiteLine("L", 0, 0, 10, 0), l);
        Assert.Single(Clean.Degenerate(new LiteEntity[] { l }, Eps, out _));
    }

    [Fact]
    public void Elevation_Lifts_A_Polyline_Along_Its_Normal()
    {
        // the plane z = 5 lands on y = -5 under RotX90
        var a = Assert.IsType<LiteLine>(Flatten.To2D(new Poly3("A", Square, true, Up, 5), RotX90, Tol, "A"));
        Assert.Equal(-5, a.Y1, 9);
        Assert.Equal(-5, a.Y2, 9);

        // normal +X: OCS x is world Y, OCS y is world Z, elevation is world X
        var b = Assert.IsType<LiteLine>(Flatten.To2D(new Poly3("A", new[] { new LiteVertex(0, 0), new LiteVertex(10, 3) }, false, East, 4), Id, Tol, "A"));
        Assert.Equal(new LiteLine("A", 4, 0, 4, 10), b);
    }

    [Fact]
    public void Circle_Under_Z_Rotation_And_Move_Stays_A_Circle()
    {
        var c = Assert.IsType<LiteCircle>(Flatten.To2D(new Circle3("A", new Vec3(10, 0, 3), Up, 5), RotZ(30, 100, 200, 50), Tol, "A"));
        Assert.Equal(100 + 10 * Cos30, c.Cx, 9);
        Assert.Equal(200 + 10 * Sin30, c.Cy, 9);
        Assert.Equal(5, c.R, 9);
    }

    [Fact]
    public void Circle_Upside_Down_Stays_A_Circle() =>
        Assert.Equal(new LiteCircle("A", 1, 2, 5), Flatten.To2D(new Circle3("A", new Vec3(1, 2, 3), Down, 5), Id, Tol, "A"));

    [Fact]
    public void Circle_With_Horizontal_Normal_Is_Its_Diameter()
    {
        // a coarse tol, so no sample lands on the far side: the diameter has to be exact anyway
        var l = Assert.IsType<LiteLine>(Flatten.To2D(new Circle3("A", new Vec3(5, 6, 7), East, 3), Id, 1.3, "A"));
        Assert.Equal(new LiteLine("A", 5, 3, 5, 9), l);

        double h = Math.Sqrt(0.5);
        var d = Assert.IsType<LiteLine>(Flatten.To2D(new Circle3("A", Origin, new Vec3(h, h, 0), 10), RotZ(0, 1, 2), 1.3, "A"));
        Assert.Equal(20, Math.Sqrt(Math.Pow(d.X2 - d.X1, 2) + Math.Pow(d.Y2 - d.Y1, 2)), 9);
        Assert.Equal(1, (d.X1 + d.X2) / 2, 9);
        Assert.Equal(2, (d.Y1 + d.Y2) / 2, 9);
    }

    [Fact]
    public void Tilted_Circle_Is_The_Projected_Ellipse_Within_Tolerance()
    {
        const double r = 100;
        var p = Assert.IsType<LitePolyline>(Flatten.To2D(new Circle3("A", new Vec3(0, 0, 40), new Vec3(0, Sin30, Cos30), r), Id, Tol, "A"));
        Assert.True(p.Closed);
        Assert.True(p.Vertices.Count > 8);
        double Unit(double x, double y) => Math.Sqrt(x * x / (r * r) + y * y / (r * r * Cos30 * Cos30)); // 1 on the ellipse
        for (int i = 0; i < p.Vertices.Count; i++)
        {
            var a = p.Vertices[i];
            var b = p.Vertices[(i + 1) % p.Vertices.Count];
            Assert.Equal(1, Unit(a.X, a.Y), 9);
            Assert.InRange(r * (1 - Unit((a.X + b.X) / 2, (a.Y + b.Y) / 2)), 0, Tol);
        }
    }

    [Fact]
    public void Uneven_Stretch_Turns_A_Circle_Into_Chords()
    {
        var stretch = new Affine3(3, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0);
        var p = Assert.IsType<LitePolyline>(Flatten.To2D(new Circle3("A", Origin, Up, 100), stretch, 0.5, "A"));
        Assert.All(p.Vertices, v => Assert.Equal(1, Math.Sqrt(v.X * v.X / 90000 + v.Y * v.Y / 10000), 9));
    }

    [Fact]
    public void Arc_Angles_Are_Measured_In_The_Entitys_Own_OCS()
    {
        const double q = Math.PI / 2;
        Assert.Equal(new LiteArc("A", 3, 4, 5, 0, 90), Flatten.To2D(new Arc3("A", new Vec3(3, 4, 9), Up, 5, 0, q), Id, Tol, "A"));

        // normal -Z: OCS x is world -X, so 0..90 about the normal runs from west to north, clockwise in plan
        var d = Assert.IsType<LiteArc>(Flatten.To2D(new Arc3("A", new Vec3(3, 4, 9), Down, 5, 0, q), Id, Tol, "A"));
        Assert.Equal(90, d.StartDeg, 9);
        Assert.Equal(180, d.EndDeg, 9);

        var r = Assert.IsType<LiteArc>(Flatten.To2D(new Arc3("A", Origin, Up, 5, q, 2 * q), RotZ(90), Tol, "A"));
        Assert.Equal(180, r.StartDeg, 9);
        Assert.Equal(270, r.EndDeg, 9);
    }

    [Fact]
    public void Tilted_Arc_Keeps_Its_End_Points()
    {
        // normal (0, sin30, cos30): OCS x is -X, OCS y is (0, -cos30, sin30)
        var p = Assert.IsType<LitePolyline>(Flatten.To2D(new Arc3("A", Origin, new Vec3(0, Sin30, Cos30), 100, 0, Math.PI / 2), Id, Tol, "A"));
        Assert.False(p.Closed);
        Assert.Equal(-100, p.Vertices[0].X, 9);
        Assert.Equal(0, p.Vertices[0].Y, 9);
        Assert.Equal(0, p.Vertices[p.Vertices.Count - 1].X, 9);
        Assert.Equal(-100 * Cos30, p.Vertices[p.Vertices.Count - 1].Y, 9);
    }

    [Fact]
    public void Arc_With_Horizontal_Normal_Is_The_Segment_Between_Its_Ends()
    {
        // normal +X: OCS x is world Y, OCS y is world Z
        var l = Assert.IsType<LiteLine>(Flatten.To2D(new Arc3("A", new Vec3(7, 0, 0), East, 10, 0, Math.PI / 2), Id, Tol, "A"));
        Assert.Equal(0, Math.Min(l.Y1, l.Y2), 9);
        Assert.Equal(10, Math.Max(l.Y1, l.Y2), 9);
        Assert.All(new[] { l.X1, l.X2 }, x => Assert.Equal(7, x, 9));
    }

    [Fact]
    public void Arc_Seen_Edge_On_Keeps_Its_Full_Reach_When_Its_Ends_Coincide()
    {
        // 90..270 about +X: both ends project onto the centre, the bow reaches y = -10
        var l = Assert.IsType<LiteLine>(Flatten.To2D(new Arc3("A", Origin, East, 10, Math.PI / 2, 3 * Math.PI / 2), Id, Tol, "A"));
        Assert.Equal(10, Math.Abs(l.Y2 - l.Y1), 0.2);
        Assert.Single(Clean.Degenerate(new LiteEntity[] { l }, Eps, out _));
    }

    [Fact]
    public void Bulge_Flips_Under_A_Mirror_And_Stays_Under_A_Rotation()
    {
        var src = new[] { new LiteVertex(0, 0, 1), new LiteVertex(10, 0) };
        var mirror = new Affine3(-1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0);
        var m = Assert.IsType<LitePolyline>(Flatten.To2D(new Poly3("A", src, false, Up, 0), mirror, Tol, "A"));
        Assert.Equal(-1, m.Vertices[0].Bulge);
        Assert.Equal(-10, m.Vertices[1].X, 9);

        var r = Assert.IsType<LitePolyline>(Flatten.To2D(new Poly3("A", src, false, Up, 0), RotZ(90), Tol, "A"));
        Assert.Equal(1, r.Vertices[0].Bulge);
        Assert.Equal(10, r.Vertices[1].Y, 9);
    }

    [Fact]
    public void Bulge_Is_Measured_About_The_Polylines_Own_Normal()
    {
        var p = Assert.IsType<LitePolyline>(Flatten.To2D(new Poly3("A", new[] { new LiteVertex(0, 0, 1), new LiteVertex(10, 0) }, false, Down, 0), Id, Tol, "A"));
        Assert.Equal(-1, p.Vertices[0].Bulge);
        Assert.Equal(-10, p.Vertices[1].X, 9); // OCS x is world -X under a -Z normal
    }

    [Fact]
    public void Bulge_On_A_Tilted_Plane_Becomes_Chords()
    {
        var p = Assert.IsType<LitePolyline>(Flatten.To2D(new Poly3("A", new[] { new LiteVertex(0, 0, 1), new LiteVertex(100, 0) }, false, new Vec3(0, Sin30, Cos30), 0), Id, Tol, "A"));
        Assert.True(p.Vertices.Count > 2);
        Assert.All(p.Vertices, v => Assert.Equal(0, v.Bulge));
    }

    [Fact]
    public void Bulge_Seen_Edge_On_Keeps_Its_Full_Reach()
    {
        // normal +X, vertical chord: the ends share a plan point, the bow does not
        var l = Assert.IsType<LiteLine>(Flatten.To2D(new Poly3("A", new[] { new LiteVertex(0, -5, 1), new LiteVertex(0, 5) }, false, East, 0), Id, Tol, "A"));
        Assert.Equal(5, Math.Abs(l.Y2 - l.Y1), 0.2);
    }

    [Fact]
    public void Points_Lose_Their_Z_After_The_Whole_Matrix()
    {
        var p = Assert.IsType<LitePolyline>(Flatten.To2D(new Points3("A", new[] { new Vec3(1, 2, 3), new Vec3(4, 5, 6) }, true), RotX90, Tol, "L"));
        Assert.True(p.Closed);
        Assert.Equal(new[] { new LiteVertex(1, -3), new LiteVertex(4, -6) }, p.Vertices);
        Assert.Equal("L", p.Layer);
    }

    [Fact]
    public void Line_Goes_Through_The_Whole_Matrix() =>
        Assert.Equal(new LiteLine("L", 1, -3, 4, -6), Flatten.To2D(new Line3("A", new Vec3(1, 2, 3), new Vec3(4, 5, 6)), RotX90, Tol, "L"));

    [Fact]
    public void Line_That_Differs_Only_In_Z_Is_Removed_As_Degenerate()
    {
        var l = Flatten.To2D(new Line3("A", new Vec3(1, 2, 0), new Vec3(1, 2, 500)), Id, Tol, "A");
        Assert.Empty(Clean.Degenerate(new[] { l }, Eps, out int removed));
        Assert.Equal(1, removed);
    }

    [Fact]
    public void Empty_Polyline_Seen_Edge_On_Goes_Away()
    {
        var e = Flatten.To2D(new Poly3("A", Array.Empty<LiteVertex>(), false, East, 0), Id, Tol, "A");
        Assert.Empty(Clean.Degenerate(new[] { e }, Eps, out _));
    }
}

public class CleanDegenerateTests
{
    const double Eps = 0.01;

    static List<LiteEntity> Run(LiteEntity e, out int removed) => Clean.Degenerate(new[] { e }, Eps, out removed);

    [Fact]
    public void Line_Shorter_Than_The_Threshold_Goes()
    {
        Assert.Empty(Run(new LiteLine("A", 0, 0, 0.009, 0), out int removed));
        Assert.Equal(1, removed);
        Assert.Single(Run(new LiteLine("A", 0, 0, 0.011, 0), out removed));
        Assert.Equal(0, removed);
    }

    [Fact]
    public void Circle_And_Arc_With_No_Radius_Go()
    {
        var ents = new LiteEntity[] { new LiteCircle("A", 1, 1, 0), new LiteArc("A", 1, 1, 0, 0, 90), new LiteCircle("A", 1, 1, 0.5), new LiteArc("A", 1, 1, 0.5, 0, 90) };
        var o = Clean.Degenerate(ents, Eps, out int removed);
        Assert.Equal(2, removed);
        Assert.Equal(new[] { ents[2], ents[3] }, o);
    }

    [Fact]
    public void Repeated_Vertices_Go_And_The_Polyline_Stays()
    {
        var p = new LitePolyline("A", new[] { new LiteVertex(0, 0), new LiteVertex(0, 0), new LiteVertex(10, 0), new LiteVertex(10.005, 0), new LiteVertex(10, 10) }, false);
        var o = Assert.IsType<LitePolyline>(Assert.Single(Run(p, out int removed)));
        Assert.Equal(0, removed); // the entity survives
        Assert.Equal(new[] { new LiteVertex(0, 0), new LiteVertex(10, 0), new LiteVertex(10, 10) }, o.Vertices);
    }

    [Fact]
    public void A_Run_Of_Tiny_Steps_Is_Not_Eaten_Whole()
    {
        var v = Enumerable.Range(0, 1000).Select(i => new LiteVertex(i * 0.005, 0)).ToArray(); // 5 long, each step under the threshold
        var o = Assert.IsType<LitePolyline>(Assert.Single(Run(new LitePolyline("A", v, false), out _)));
        Assert.True(o.Vertices[o.Vertices.Count - 1].X > 4.9);
    }

    [Fact]
    public void Dropping_A_Repeated_Vertex_Keeps_The_Arc_That_Leaves_It()
    {
        var p = new LitePolyline("A", new[] { new LiteVertex(0, 0), new LiteVertex(10, 0), new LiteVertex(10, 0, 1), new LiteVertex(20, 0) }, false);
        var o = Assert.IsType<LitePolyline>(Assert.Single(Run(p, out _)));
        Assert.Equal(new[] { new LiteVertex(0, 0), new LiteVertex(10, 0, 1), new LiteVertex(20, 0) }, o.Vertices);
    }

    [Fact]
    public void Nearly_Full_Bulge_On_A_Tiny_Chord_Is_Not_A_Repeat()
    {
        var p = new LitePolyline("A", new[] { new LiteVertex(0, 0, 40000), new LiteVertex(0.005, 0) }, false);
        Assert.Same(p, Assert.Single(Run(p, out _)));
    }

    [Fact]
    public void Closed_Polyline_Loses_The_Vertex_That_Repeats_Its_First()
    {
        var p = new LitePolyline("A", new[] { new LiteVertex(0, 0), new LiteVertex(10, 0), new LiteVertex(10, 10), new LiteVertex(0, 0) }, true);
        var o = Assert.IsType<LitePolyline>(Assert.Single(Run(p, out _)));
        Assert.True(o.Closed);
        Assert.Equal(3, o.Vertices.Count);
    }

    [Fact]
    public void Two_Vertex_Bulged_Circle_Survives()
    {
        var p = new LitePolyline("A", new[] { new LiteVertex(0, 0, 1), new LiteVertex(10, 0, 1) }, true);
        Assert.Same(p, Assert.Single(Run(p, out _)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Polyline_With_Under_Two_Distinct_Vertices_Goes(bool closed)
    {
        Assert.Empty(Run(new LitePolyline("A", new[] { new LiteVertex(1, 1), new LiteVertex(1, 1), new LiteVertex(1.002, 1) }, closed), out int removed));
        Assert.Equal(1, removed);
        Assert.Empty(Run(new LitePolyline("A", new[] { new LiteVertex(1, 1) }, closed), out _));
        Assert.Empty(Run(new LitePolyline("A", Array.Empty<LiteVertex>(), closed), out _));
    }

    [Fact]
    public void Clean_Entities_Pass_Through_Untouched_And_In_Order()
    {
        var ents = new LiteEntity[] { new LiteLine("B", 0, 0, 5, 0), new LitePolyline("A", new[] { new LiteVertex(0, 0), new LiteVertex(1, 1) }, false), new LiteCircle("C", 0, 0, 1) };
        var o = Clean.Degenerate(ents, Eps, out int removed);
        Assert.Equal(0, removed);
        Assert.Equal(3, o.Count);
        for (int i = 0; i < 3; i++) Assert.Same(ents[i], o[i]);
    }
}
