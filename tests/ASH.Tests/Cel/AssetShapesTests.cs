using ASH.Core.Cel;
using ASH.Core.Geometry;
using ASH.Core.Layout;

namespace ASH.Tests.Cel;

public class AssetShapesTests
{
    static LayoutAsset Block(string kind, double sx = 1, double sy = 1, double rot = 0) => new() { Kind = kind, X = 10, Y = 5, RotDeg = rot, Sx = sx, Sy = sy };
    static LayoutAsset Line(string kind, params Segment[] segs) => new() { Kind = kind, Segments = segs.ToList() };
    static (double Min, double Max) ExtentX(IEnumerable<AssetPart> ps) => (ps.Min(p => p.X - p.Dx / 2), ps.Max(p => p.X + p.Dx / 2));

    [Fact]
    public void Every_Block_Kind_Has_Parts_With_Positive_Sizes()
    {
        foreach (var k in AssetKinds.Blocks)
        {
            var c = AssetShapes.Cell(Block(k), 1);
            Assert.NotNull(c);
            Assert.NotEmpty(c!.Parts);
            Assert.All(c.Parts, p => Assert.True(p.H > 0 && (p.Radius > 0 || p.Dx > 0 && p.Dy > 0), k + " " + p.Name));
            Assert.Equal((10, 5), (c.X, c.Y));
        }
    }

    [Fact]
    public void Unknown_Kind_Missing_Segments_Or_Zero_Scale_Give_No_Cell()
    {
        Assert.Null(AssetShapes.Cell(Block("SOFA"), 1));
        Assert.Null(AssetShapes.Cell(Line(AssetKinds.Wall), 1));
        Assert.Null(AssetShapes.Cell(Block(AssetKinds.Rack, 0), 1));
    }

    [Fact]
    public void Scale_Multiplies_The_Base_Size()
    {
        Assert.Equal((-3, 3), ExtentX(AssetShapes.Parts(AssetKinds.Door, 1, 1)!.Where(p => p.Name.StartsWith("wall"))));     // door base 6 m
        Assert.Equal((-1.5, 1.5), ExtentX(AssetShapes.Parts(AssetKinds.Door, 0.5, 1)!.Where(p => p.Name.StartsWith("wall"))));
        var rack = AssetShapes.Parts(AssetKinds.Rack, 2, 1)!;                                                                 // 6 m rack: 2 bays, 3 post pairs
        Assert.Equal(6, rack.Count(p => p.Name.StartsWith("post")));
        Assert.Equal(6, rack.Single(p => p.Name == "beam_f0").Dx, 9);
        var m = AssetShapes.Parts(AssetKinds.Machine, 2, 0.5)!.Single(p => p.Name == "body");
        Assert.Equal((5, 1, 2.2), (m.Dx, m.Dy, m.H));
        double Top(string k, double s = 1) => AssetShapes.Parts(k, s, 1)!.Max(p => p.Z + p.H);
        Assert.Equal(1.6 * Top(AssetKinds.RobotM), Top(AssetKinds.RobotL), 9);                                                // the prototype's robot sizes 1.6 / 1 / 0.55
        Assert.Equal(0.55 * Top(AssetKinds.RobotM), Top(AssetKinds.RobotS), 9);
        Assert.Equal(2 * Top(AssetKinds.RobotM), Top(AssetKinds.RobotM, 2), 9);
    }

    [Fact]
    public void Footprint_Bounds_The_Plan_Of_The_Base_Size()
    {
        var z = AssetShapes.Footprint(AssetKinds.Zone)!.Value;                                   // the turned cross stays inside the 3 x 2 m border
        Assert.Equal((-1.55, -1.05, 1.55, 1.05), (Math.Round(z.MinX, 9), Math.Round(z.MinY, 9), Math.Round(z.MaxX, 9), Math.Round(z.MaxY, 9)));
        var r = AssetShapes.Footprint(AssetKinds.RobotM)!.Value;                                 // the arm reaches +x
        Assert.True(r.MaxX > 1.2 && r.MinX > -0.5, r.ToString());
        Assert.Null(AssetShapes.Footprint(AssetKinds.Wall));
    }

    [Fact]
    public void Wall_Is_One_Box_Per_Segment_Turned_Along_It()
    {
        var c = AssetShapes.Cell(Line(AssetKinds.Wall, new LineSeg(new Point2(0, 0), new Point2(10, 0)), new LineSeg(new Point2(10, 0), new Point2(10, 4))), 1)!;
        Assert.Equal((0, 0, 0), (c.X, c.Y, c.RotDeg));
        Assert.Equal(2, c.Parts.Count);
        var (a, b) = (c.Parts[0], c.Parts[1]);
        Assert.Equal((5, 0, 10.2, 0.2, 3, 0), (a.X, a.Y, a.Dx, a.Dy, a.H, a.RotZ));   // longer by the thickness so corners close
        Assert.Equal((10, 2, 4.2, 90), (b.X, b.Y, b.Dx, b.RotZ));
        var wide = AssetShapes.Cell(new LayoutAsset { Kind = AssetKinds.Wall, WidthM = 0.5, Segments = { new LineSeg(new Point2(0, 0), new Point2(10, 0)) } }, 1)!;
        Assert.Equal((10.5, 0.5), (wide.Parts[0].Dx, wide.Parts[0].Dy));
    }

    [Fact]
    public void Arcs_Become_Chords_Of_At_Most_Ten_Degrees()
    {
        var arc = new ArcSeg(new Point2(0, 0), new Point2(5, 0), 90);
        var c = AssetShapes.Cell(Line(AssetKinds.Wall, arc), 1)!;
        Assert.Equal(9, c.Parts.Count);
        Assert.All(c.Parts, p => Assert.Equal(5, Math.Sqrt(p.X * p.X + p.Y * p.Y), 1));         // chord midpoints near the radius
        Assert.Equal(95, c.Parts[0].RotZ, 6);                                                   // first chord heads 5° past north
        Assert.Equal(5, AssetShapes.Cell(Line(AssetKinds.Wall, new ArcSeg(new Point2(0, 0), new Point2(5, 0), -45)), 1)!.Parts.Count);
    }

    [Fact]
    public void Fence_Posts_Every_Two_Metres_Shared_At_Joints()
    {
        var one = AssetShapes.Cell(Line(AssetKinds.Fence, new LineSeg(new Point2(0, 0), new Point2(6, 0))), 1)!;
        Assert.Equal(4, one.Parts.Count(p => p.Name.StartsWith("post")));
        Assert.Equal(new[] { "mesh0", "kick0", "rail0" }, one.Parts.Where(p => !p.Name.StartsWith("post")).Select(p => p.Name));
        Assert.Equal(2, one.Parts.Max(p => p.Z + p.H), 9);
        var two = AssetShapes.Cell(Line(AssetKinds.Fence, new LineSeg(new Point2(0, 0), new Point2(6, 0)), new LineSeg(new Point2(6, 0), new Point2(6, 3))), 1)!;
        Assert.Equal(4 + 2, two.Parts.Count(p => p.Name.StartsWith("post")));                   // 3 m: 2 pitches, the joint post is not doubled
    }

    [Fact]
    public void InMetres_Moves_And_Scales_Positions_And_Segments()
    {
        var a = new LayoutAsset { Kind = AssetKinds.Wall, X = 2000, Y = 3000, RotDeg = 30, Sx = 2, WidthM = 0.3,
            Segments = { new LineSeg(new Point2(1000, 1000), new Point2(5000, 1000)), new ArcSeg(new Point2(5000, 2000), new Point2(5000, 1000), 90) } };
        var m = AssetShapes.InMetres(a, new Point2(1000, 1000), 0.001);
        Assert.Equal((1, 2, 30, 2, 0.3), (m.X, m.Y, m.RotDeg, m.Sx, m.WidthM));
        Assert.Equal(new Point2(4, 0), m.Segments[0].End);
        var arc = Assert.IsType<ArcSeg>(m.Segments[1]);
        Assert.Equal((new Point2(4, 1), 90), (arc.Center, arc.SweepDeg));
    }

    [Fact]
    public void Asy_Is_A_Static_System_In_The_Model_Units()
    {
        var cells = new[] { AssetShapes.Cell(Block(AssetKinds.RobotS, rot: 90), 1)!, AssetShapes.Cell(Block(AssetKinds.Column), 2)! };
        var l = AssetShapes.Asy(cells, "Meters").TrimEnd('\n').Split('\n');
        Assert.Equal(new[] { "SYSTYPE Static", "UNITS Meters Seconds", "SYSDEF", "FLAGS", "\tSystem Inherit", "STATICDEF UserId 9", " IND 0 display picpos endx 1", "\ttemplate Meters",
            "700 1", "1 1 0 1 1 ash_3d", "2", "700 21", "1 1 0 1 1 robot_s1", "10  5  0", "0 0 0 90", "\t1.00\t1.00\t1.00", cells[0].Parts.Count.ToString() }, l.Take(17));
        Assert.Equal("end", l.Last());
        Assert.Equal(cells.Sum(c => c.Parts.Count(p => p.Radius > 0)), l.Count(x => x == "320 5"));
        Assert.Equal(cells.Sum(c => c.Parts.Count(p => p.Radius == 0)), l.Count(x => x == "310 5"));
        var col = l.Skip(Array.IndexOf(l, "1 1 0 1 1 column2")).ToList();
        Assert.Equal("0.5 0.5 0.5 0.5 4.5 0 0", col[col.IndexOf("8 8 0 1 1 col") + 4]);         // y extent first, then x, top y, top x, height

        var mm = AssetShapes.Asy(cells, "Millimeters").Split('\n');
        Assert.Contains("\ttemplate Millimeters", mm);
        Assert.Contains("10000  5000  0", mm);
        Assert.Contains("500 500 500 500 4500 0 0", mm);
        Assert.Throws<ArgumentException>(() => AssetShapes.Asy(cells, "Parsecs"));
    }

    [Fact]
    public void AddTo_Writes_The_System_And_Removes_It_When_None_Are_Left()
    {
        var arc = Path.Combine(Path.GetTempPath(), "ash_3d_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(arc);
        var amo = Path.Combine(arc, "model.amo");
        File.WriteAllText(amo, "UNITS Meters Seconds\nMOVESYS name agv\n\tSYSPOS endx 1\nPROCSYS name m~\n\tSYSPOS endx 1\nCONTROL snaplen 1 Hours counts 1\n");
        Assert.Equal(2, AssetShapes.AddTo(arc, new[] { Block(AssetKinds.Bench), Block("SOFA"), Line(AssetKinds.Fence, new LineSeg(new Point2(0, 0), new Point2(4, 0))) }, "Meters"));
        Assert.True(File.Exists(Path.Combine(arc, "ash_3d.asy")));
        Assert.Contains("\tSYSPOS endx 1\nSTATIC name ash_3d\n\tSYSPOS endx 1\nPROCSYS name m~", File.ReadAllText(amo));
        AssetShapes.AddTo(arc, new[] { Block(AssetKinds.Bench) }, "Meters");                    // again: one STATIC line only
        Assert.Single(File.ReadAllLines(amo), x => x == "STATIC name ash_3d");
        Assert.Equal(0, AssetShapes.AddTo(arc, Array.Empty<LayoutAsset>(), "Meters"));
        Assert.False(File.Exists(Path.Combine(arc, "ash_3d.asy")));
        Assert.DoesNotContain("ash_3d", File.ReadAllText(amo));
        Directory.Delete(arc, true);
    }
}
