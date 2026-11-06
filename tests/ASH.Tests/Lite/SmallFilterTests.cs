using ASH.Core.Lite;

namespace ASH.Tests.Lite;

public class SmallFilterTests
{
    const double Grid = 0.1;

    static LiteLine L(double x1, double y1, double x2, double y2, string layer = "A") => new(layer, x1, y1, x2, y2);
    static LiteCircle Dia(double d, double x = 0) => new("A", x, 0, d / 2);
    static LitePolyline Square(double x, double y, double side, string layer = "A") =>
        new(layer, new[] { new LiteVertex(x, y), new LiteVertex(x + side, y), new LiteVertex(x + side, y + side), new LiteVertex(x, y + side) }, true);

    /// <summary>1 m along x in 10 mm pieces, with a 300 mm stem at x = 500 in 10 mm pieces.</summary>
    static List<LiteEntity> Tee()
    {
        var o = new List<LiteEntity>();
        for (int x = 0; x < 1000; x += 10) o.Add(L(x, 0, x + 10, 0));
        for (int y = 0; y < 300; y += 10) o.Add(L(500, y, 500, y + 10));
        return o;
    }

    [Fact]
    public void A_Lone_Circle_Is_Judged_By_Its_Box()
    {
        var keep = Dia(60, 1000);
        var r = SmallFilter.Apply(new LiteEntity[] { Dia(20), keep }, 50, Grid, out int removed);
        Assert.Same(keep, Assert.Single(r));
        Assert.Equal(1, removed);
    }

    [Fact]
    public void One_Hundred_10_Mm_Lines_In_A_Row_Stay()
    {
        var pieces = Enumerable.Range(0, 100).Select(i => (LiteEntity)L(i * 10, 0, (i + 1) * 10, 0)).ToList();
        var r = SmallFilter.Apply(pieces, 50, Grid, out int removed);
        Assert.Equal(100, r.Count);
        Assert.Equal(0, removed);
    }

    [Fact]
    public void Minimum_Size_0_Removes_Nothing()
    {
        var src = new LiteEntity[] { Dia(0.001), L(0, 0, 0, 0), Square(5, 5, 0.001) };
        var r = SmallFilter.Apply(src, 0, Grid, out int removed);
        Assert.Equal(src, r);
        Assert.Equal(0, removed);
    }

    [Fact]
    public void A_T_Branched_Outline_Stays_Whole_Though_Join_Splits_It_At_The_Branch()
    {
        var parts = Chain.Join(Tee(), Grid);
        Assert.Equal(3, parts.Count);
        Assert.All(parts, p => Assert.True(LiteBox.Size(p) < 800)); // each arm alone would go

        var r = SmallFilter.Apply(parts, 800, Grid, out int removed);
        Assert.Equal(parts, r);
        Assert.Equal(0, removed);
    }

    [Fact]
    public void The_Group_Ignores_Layers()
    {
        var r = SmallFilter.Apply(new LiteEntity[] { L(0, 0, 40, 0, "A"), L(40, 0, 80, 0, "B") }, 50, Grid, out int removed);
        Assert.Equal(2, r.Count);
        Assert.Equal(0, removed);
    }

    [Fact]
    public void A_Closed_Shape_Stands_Alone_Though_It_Touches_A_Big_Outline()
    {
        var wall = L(0, 0, 5000, 0);
        var r = SmallFilter.Apply(new LiteEntity[] { wall, Square(0, 0, 10), new LiteCircle("A", 100, 0, 5) }, 50, Grid, out int removed);
        Assert.Same(wall, Assert.Single(r));
        Assert.Equal(2, removed);
    }

    [Fact]
    public void GroupSizes_Judges_A_Big_Outline_Of_Short_Pieces_As_A_Whole()
    {
        var items = new List<LiteEntity>();
        // 4000 x 2000 equipment outline drawn as 100-long segments, closed by an arc and an open polyline
        for (int x = 0; x < 4000; x += 100) { items.Add(L(x, 0, x + 100, 0, "E")); items.Add(L(x, 2000, x + 100.5, 2000, "E")); }
        items.Add(new LiteArc("E", 4000, 1000, 1000, -90, 90));
        items.Add(new LitePolyline("E", new[] { new LiteVertex(0, 2000), new LiteVertex(-50, 1000), new LiteVertex(0, 0) }, false));
        int lone = items.Count; items.Add(L(9000, 9000, 9100, 9000, "E"));
        int hole = items.Count; items.Add(new LiteCircle("E", 100, 0, 20));      // touches the outline but stands alone
        int box = items.Count; items.Add(new LitePolyline("E", new[] { new LiteVertex(0, 0), new LiteVertex(50, 0), new LiteVertex(50, 50) }, true));
        var size = new double[items.Count];
        SmallFilter.GroupSizes(items, 1, size);
        double whole = Math.Sqrt(5050 * 5050 + 2000 * 2000);
        for (int i = 0; i < lone; i++) Assert.Equal(whole, size[i], 3);
        Assert.Equal(100, size[lone], 9);
        Assert.Equal(Math.Sqrt(40 * 40 + 40 * 40), size[hole], 9);
        Assert.Equal(Math.Sqrt(50 * 50 + 50 * 50), size[box], 9);
    }

    [Fact]
    public void Clean_Removes_Each_Kind_In_Its_Own_Step_And_Counts_It()
    {
        var src = new LiteEntity[]
        {
            L(0, 0, 0.001, 0),                                 // degenerate
            L(5000, 0, 6000, 0), L(6000, 0, 5000, 0),          // duplicate
            L(0, 1000, 600, 1000), L(500, 1000, 1000, 1000),   // overlap
            Dia(20, 9000),                                     // small
        };
        var one = new LiteEntity[] { L(0, 0, 0.001, 0) };
        var dup = new LiteEntity[] { L(6000, 0, 5000, 0) };
        var over = new LiteEntity[] { src[3], src[4] };
        long Bytes(IEnumerable<LiteEntity> l) => l.Sum(e => LiteDxfWriter.Bytes(e, e.Layer, 0, 0, LiteDxfWriter.Precision(4))); // the writer's precision for this unit

        var stats = new LiteStats();
        var r = LitePipeline.Run(src, new LiteThresholds(1, 50, 4), 1, stats);

        Assert.Equal(2, r.Count);
        Assert.Equal((1, Bytes(one)), (stats.Degenerate.Count, stats.Degenerate.Bytes));
        Assert.Equal((1, Bytes(dup)), (stats.Duplicate.Count, stats.Duplicate.Bytes));
        Assert.Equal((1, Bytes(over) - Bytes(new[] { L(0, 1000, 1000, 1000) })), (stats.Overlap.Count, stats.Overlap.Bytes));
        Assert.Equal((1, Bytes(new[] { Dia(20, 9000) })), (stats.Small.Count, stats.Small.Bytes));
    }

    [Fact]
    public void Stats_Add_Up_Over_Runs()
    {
        var stats = new LiteStats();
        LitePipeline.Run(new[] { Dia(20) }, new LiteThresholds(1, 50, 4), 1, stats);
        LitePipeline.Run(new[] { Dia(20) }, new LiteThresholds(1, 50, 4), 1, stats);
        Assert.Equal(2, stats.Small.Count);
    }

    [Fact]
    public void A_Split_Arc_Survives_As_One_Thin_Polyline()
    {
        var stats = new LiteStats();
        var r = LitePipeline.Run(ChainTests.ArcPieces(), new LiteThresholds(10, 50, 4), 1, stats);
        var p = Assert.IsType<LitePolyline>(Assert.Single(r));
        Assert.InRange(p.Vertices.Count, 3, CurveApprox.Steps(3, 5000, 10) * 3 / 2 + 1);
        Assert.Equal(0, stats.Small.Count);
    }

    [Fact]
    public void Pieces_A_Tolerance_Apart_Still_Count_As_One_Outline()
    {
        // gaps of 0.5 mm: no chain at the 0.1 mm grid, one group at tolerance 1 mm as AGVLITE always judged it
        var pieces = Enumerable.Range(0, 100).Select(i => L(i * 10.5, 0, i * 10.5 + 10, 0));
        Assert.Equal(100, LitePipeline.Run(pieces, new LiteThresholds(1, 50, 4), 1, new LiteStats()).Count);
    }

    [Fact]
    public void Joining_Comes_Before_The_Small_Filter()
    {
        // Filtered first, these lines would count with the wall they touch on another layer. Joined first, they are a closed shape that stands alone.
        var ring = new LiteEntity[] { L(0, 0, 10, 0), L(10, 0, 10, 10), L(10, 10, 0, 10), L(0, 10, 0, 0) };
        var wall = L(-2000, -1000, 0, 0, "WALL"); // not along an edge: MergeCollinear ignores layers
        var stats = new LiteStats();
        var r = LitePipeline.Run(ring.Append(wall), new LiteThresholds(1, 50, 4), 1, stats);
        Assert.Same(wall, Assert.Single(r));
        Assert.Equal(1, stats.Small.Count);
    }

    [Fact]
    public void Simplifying_Comes_After_Joining()
    {
        // only a joined chain has vertices to thin: bow 0.5 mm over 1 m, tolerance 1 mm
        var a = L(0, 0, 500, 0.5);
        var b = L(500, 0.5, 1000, 0);
        var p = Assert.IsType<LitePolyline>(Assert.Single(LitePipeline.Run(new[] { a, b }, new LiteThresholds(1, 0, 4), 1, new LiteStats())));
        Assert.Equal(2, p.Vertices.Count);
    }

    [Fact]
    public void A_Line_Cut_In_Two_Merges_Before_Joining_And_Stays_A_Line()
    {
        var r = LitePipeline.Run(new[] { L(0, 0, 500, 0), L(500, 0, 1000, 0) }, new LiteThresholds(1, 0, 4), 1, new LiteStats());
        Assert.Equal(L(0, 0, 1000, 0), Assert.Single(r));
    }

    [Fact]
    public void Thresholds_Follow_The_Largest_Instance_Scale()
    {
        var t = new LiteThresholds(1, 50, 4);
        // 20 mm diameter, box 28 mm: 57 mm at scale 2 in the biggest instance
        Assert.Empty(LitePipeline.Run(new[] { Dia(20) }, t, 1, new LiteStats()));
        Assert.Single(LitePipeline.Run(new[] { Dia(20) }, t, 2, new LiteStats()));

        // a 0.006 line is 0.012 at scale 2, over the 0.01 mm degenerate limit
        Assert.Empty(LitePipeline.Run(new[] { L(0, 0, 0.006, 0) }, new LiteThresholds(1, 0, 4), 1, new LiteStats()));
        Assert.Single(LitePipeline.Run(new[] { L(0, 0, 0.006, 0) }, new LiteThresholds(1, 0, 4), 2, new LiteStats()));

        // a bow of 0.75 mm goes at tolerance 1, but it would be 1.5 mm at scale 2
        var bow = new LiteEntity[] { L(0, 0, 500, 0.75), L(500, 0.75, 1000, 0) };
        Assert.Equal(2, ((LitePolyline)Assert.Single(LitePipeline.Run(bow, new LiteThresholds(1, 0, 4), 1, new LiteStats()))).Vertices.Count);
        Assert.Equal(3, ((LitePolyline)Assert.Single(LitePipeline.Run(bow, new LiteThresholds(1, 0, 4), 2, new LiteStats()))).Vertices.Count);
    }

    [Fact]
    public void Thresholds_Are_Millimetres_In_Any_Drawing_Unit()
    {
        var metres = new LiteThresholds(1, 50, 6);
        Assert.Equal(0.001, metres.UnitsPerMm, 12);
        Assert.Empty(LitePipeline.Run(new[] { Dia(0.02) }, metres, 1, new LiteStats()));
        Assert.Single(LitePipeline.Run(new[] { Dia(0.06) }, metres, 1, new LiteStats()));
    }

    [Fact]
    public void A_Scale_That_Is_Not_Positive_Is_Refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LitePipeline.Run(new[] { Dia(20) }, new LiteThresholds(1, 50, 4), 0, new LiteStats()));
    }

    [Fact]
    public void Layer_Names_Do_Not_Change_The_Geometry()
    {
        static double[][] Shape(string a, string b)
        {
            var src = new LiteEntity[] { L(0, 0, 40, 0, a), L(40, 0, 40, 40, a), L(40, 0, 80, 0, b), Dia(20) };
            return LitePipeline.Run(src, new LiteThresholds(1, 50, 4), 1, new LiteStats())
                .Select(e => { var x = LiteBox.Of(new[] { e }); return new[] { x.MinX, x.MinY, x.MaxX, x.MaxY }; }).ToArray();
        }
        Assert.Equal(Shape("A", "B"), Shape("설비", "AA"));
    }
}
