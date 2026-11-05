using ASH.Core.Lite;

namespace ASH.Tests.Lite;

/// <summary>Step G in local mm (INSUNITS 4, scale 1). Level values are the §3 table: 2 = (10, 50, 20000, 6 m), 3 = (20, 100, 10000, 30 m), 4 = (30, 200, 5000, 100 m).</summary>
public class BlobsTests
{
    public static readonly LiteThresholds L2 = new(10, 50, 4, 20_000, 6_000), L3 = new(20, 100, 4, 10_000, 30_000), L4 = new(30, 200, 4, 5_000, 100_000);
    const double Cell = 200;

    static LiteLine L(double x1, double y1, double x2, double y2, string layer = "A") => new(layer, x1, y1, x2, y2);

    /// <summary>A mesh of n x n lines at 20 mm pitch, each cut into n pieces 20 mm long (n = 100: 2 m x 2 m, 20,000 pieces), lower left at (x, y).</summary>
    public static IEnumerable<LiteEntity> Robot(double x, double y, string layer = "ROBOT", int n = 100)
    {
        for (int i = 0; i < n; i++)
            for (int k = 0; k < n; k++)
            {
                yield return L(x + i * 20, y + k * 20, x + i * 20, y + k * 20 + 20, layer);
                yield return L(x + k * 20, y + i * 20, x + k * 20 + 20, y + i * 20, layer);
            }
    }

    /// <summary>Two 100 m wall lines 200 mm apart.</summary>
    public static IEnumerable<LiteEntity> Walls(double y = 10_000) => new[] { L(-10_000, y, 90_000, y, "WALL"), L(-10_000, y + 200, 90_000, y + 200, "WALL") };

    /// <summary>0.6 m square columns at 8 m spacing on a 2 x 2 grid from (0, 0).</summary>
    public static IEnumerable<LiteEntity> Columns()
    {
        for (int i = 0; i < 2; i++)
            for (int k = 0; k < 2; k++)
            {
                double x = i * 8000, y = k * 8000;
                yield return new LitePolyline("COL", new[] { new LiteVertex(x, y), new LiteVertex(x + 600, y), new LiteVertex(x + 600, y + 600), new LiteVertex(x, y + 600) }, true);
            }
    }

    /// <summary>20 m roller conveyor: rails at y = 100 and 900, 400 rollers 50 mm apart.</summary>
    public static IEnumerable<LiteEntity> Conveyor()
    {
        yield return L(0, 100, 20_000, 100, "CONV");
        yield return L(0, 900, 20_000, 900, "CONV");
        for (int i = 0; i < 400; i++) yield return L(i * 50, 100, i * 50, 900, "CONV");
    }

    static List<LiteEntity> Replace(IEnumerable<LiteEntity> ents, LiteThresholds t, LiteStats? stats = null) => Blobs.Replace(ents, t, 1, stats ?? new LiteStats());

    [Fact]
    public void Occupy_Splits_A_Diagonal_Over_Its_Cells()
    {
        var cells = Blobs.Occupy(new[] { L(0, 0, 1000, 1000) }, Cell, 1, double.PositiveInfinity);
        Assert.Equal(5, cells.Count);
        for (int i = 0; i < 5; i++) Assert.Equal(200 * Math.Sqrt(2), cells[(i, i)], 6);
    }

    [Fact]
    public void Occupy_Leaves_Out_Entities_As_Long_As_MaxBlob()
    {
        Assert.Empty(Blobs.Occupy(new[] { L(0, 0, 6000, 0) }, Cell, 1, 6000));
        Assert.NotEmpty(Blobs.Occupy(new[] { L(0, 0, 5999, 0) }, Cell, 1, 6000));
    }

    [Fact]
    public void Group_Is_Eight_Connected()
    {
        var apart = Blobs.Occupy(new[] { L(0, 0, 1000, 0), L(3000, 0, 4000, 0) }, Cell, 1, double.PositiveInfinity);
        Assert.Equal(2, Blobs.Group(apart).Count);
        var touching = Blobs.Occupy(new[] { L(0, 0, 1000, 0), L(1000, 0, 2000, 0) }, Cell, 1, double.PositiveInfinity);
        Assert.Single(Blobs.Group(touching));
        var diagonal = Blobs.Occupy(new[] { L(100, 100, 150, 150), L(250, 250, 300, 300) }, Cell, 1, double.PositiveInfinity); // cells (0,0) and (1,1)
        Assert.Single(Blobs.Group(diagonal));
    }

    [Fact]
    public void Density_Is_Line_Length_Per_Square_Metre()
    {
        var lines = Enumerable.Range(0, 20).Select(i => (LiteEntity)L(0, i * 50, 1000, i * 50)).ToList();
        var cells = Blobs.Occupy(lines, Cell, 1, double.PositiveInfinity);
        var g = Assert.Single(Blobs.Group(cells));
        var m = Blobs.Measure(g, cells, Cell, 1);
        Assert.Equal(20_000, m.Density, 6);
        Assert.Equal(Math.Sqrt(2) * 1000, m.DiagMm, 6);
    }

    [Fact]
    public void Outline_Follows_An_L_And_Drops_Holes()
    {
        // L: a 3 x 1 bar plus a 1 x 2 stem up from its left end; a 3 x 3 ring with the centre empty
        var ell = new[] { (0L, 0L), (1L, 0L), (2L, 0L), (0L, 1L), (0L, 2L) };
        var p = Blobs.Outline(ell, Cell, 1, "A");
        Assert.True(p.Closed);
        Assert.Equal(6, p.Vertices.Count);
        Assert.Contains(p.Vertices, v => v.X == 200 && v.Y == 200); // the inner corner: not a convex hull
        var ring = new List<(long, long)>();
        for (long x = 0; x < 3; x++) for (long y = 0; y < 3; y++) if (x != 1 || y != 1) ring.Add((x, y));
        var r = Blobs.Outline(ring, Cell, 1, "A");
        Assert.Equal(4, r.Vertices.Count);
        Assert.All(r.Vertices, v => Assert.True((v.X == 0 || v.X == 600) && (v.Y == 0 || v.Y == 600)));
    }

    [Fact]
    public void Outline_Keeps_Diagonal_Cells_In_One_Ring()
    {
        var p = Blobs.Outline(new[] { (0L, 0L), (1L, 1L) }, Cell, 1, "A");
        Assert.Equal(8, p.Vertices.Count);
    }

    [Fact]
    public void Robot_Becomes_An_Outline_Walls_And_Columns_Stay_At_Every_Level()
    {
        var scene = Robot(3000, 3000).Concat(Walls()).Concat(Columns()).ToList();
        foreach (var t in new[] { L2, L3, L4 })
        {
            var stats = new LiteStats();
            var r = Replace(scene, t, stats);
            Assert.Equal(2, r.OfType<LiteLine>().Count(l => l.Layer == "WALL"));
            Assert.Equal(4, r.Count(e => e.Layer == "COL"));
            var robot = Assert.Single(r.Where(e => e.Layer == "ROBOT"));
            var box = LiteBox.Of(new[] { robot });
            Assert.InRange(box.MinX, 2800, 3000);
            Assert.InRange(box.MaxX, 5000, 5200);
            Assert.Equal(1, stats.Blobs);
            Assert.Equal(20_000, stats.Blob.Count);
            Assert.True(stats.Blob.Bytes > 0);
            var top = Assert.Single(stats.TopBlobs);
            Assert.Equal(20_000, top.Lines);
        }
    }

    [Fact]
    public void Dense_Off_Replaces_Nothing()
    {
        var scene = Robot(0, 0).ToList();
        Assert.Equal(scene.Count, Replace(scene, new LiteThresholds(10, 50, 4)).Count);
    }

    [Fact]
    public void Conveyor_Stays_At_Level_2_And_Is_An_Outline_At_Level_3()
    {
        var conv = Conveyor().ToList();
        Assert.Equal(conv.Count, Replace(conv, L2).Count);
        var p = Assert.IsType<LitePolyline>(Assert.Single(Replace(conv, L3)));
        Assert.True(p.Closed);
        Assert.Equal("CONV", p.Layer);
        var box = LiteBox.Of(new[] { p });
        Assert.InRange(box.MaxX - box.MinX, 20_000, 20_400);
    }

    [Fact]
    public void A_Wall_Through_A_Robot_Stays_While_The_Robot_Is_Outlined()
    {
        var scene = Robot(3000, 3000).Concat(Walls(4000)).ToList();
        var r = Replace(scene, L2);
        Assert.Equal(2, r.OfType<LiteLine>().Count(l => l.Layer == "WALL" && Math.Abs(l.X2 - l.X1) == 100_000));
        Assert.Single(r.Where(e => e.Layer == "ROBOT"));
    }

    [Fact]
    public void Outline_Takes_The_Layer_Most_Removed_Lines_Had()
    {
        var scene = Robot(0, 0, "MANY").Concat(Enumerable.Range(0, 10).Select(i => (LiteEntity)L(10, i * 20 + 10, 30, i * 20 + 10, "FEW"))).ToList();
        Assert.Equal("MANY", Assert.Single(Replace(scene, L2)).Layer);
    }

    [Fact]
    public void Top_Blobs_Are_Capped_At_30_And_Sorted_By_Lines()
    {
        var scene = new List<LiteEntity>();
        for (int i = 0; i < 35; i++) scene.AddRange(Robot(i * 5000, 0, "R", 50));
        var stats = new LiteStats();
        Replace(scene, L2, stats);
        Assert.Equal(35, stats.Blobs);
        Assert.Equal(30, stats.TopBlobs.Count);
        Assert.Equal(stats.TopBlobs.OrderByDescending(b => b.Lines).ThenBy(b => b.X).ThenBy(b => b.Y).ToList(), stats.TopBlobs);
    }
}
