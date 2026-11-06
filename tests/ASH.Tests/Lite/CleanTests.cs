using System.Diagnostics;
using ASH.Core.Cel;
using ASH.Core.Lite;
using Xunit.Abstractions;

namespace ASH.Tests.Lite;

public class SegKeyTests
{
    const double Grid = 0.1;

    static long Hash(LiteEntity e) => SegKey.Of(e, Grid).Hash;

    [Fact]
    public void A_Segment_And_Its_Reverse_Share_The_Key()
    {
        Assert.Equal(SegKey.Of(1, 2, 3, 4, Grid), SegKey.Of(3, 4, 1, 2, Grid));
        Assert.Equal(SegKey.Of(1, 2, 3, 4, Grid).Hash, SegKey.Of(3, 4, 1, 2, Grid).Hash);
        Assert.Equal(SegKey.Of(5, 9, 5, 1, Grid), SegKey.Of(5, 1, 5, 9, Grid)); // equal x: y decides the order
    }

    [Fact]
    public void Hash_Is_Pinned_So_It_Cannot_Drift_Between_Runs_Or_Runtimes()
    {
        // these run on net10.0 and net48; a change here breaks the write-time dedup contract
        Assert.Equal(unchecked((long)0x2C4B6D423AC6631CUL), SegKey.Of(1, 2, 3, 4, Grid).Hash);
        Assert.Equal(unchecked((long)0x30F07BF4258663BFUL), Hash(new LiteCircle("A", 1, 2, 3)));
        Assert.Equal(unchecked((long)0xC62DC6FF5F278B49UL), Hash(new LiteArc("A", 1, 2, 3, 10, 20)));
        Assert.Equal(unchecked((long)0x6F0E060F1A56946CUL), Hash(new LitePolyline("A", new[] { new LiteVertex(0, 0, 0.5), new LiteVertex(1, 0) }, false)));
    }

    [Fact]
    public void Negative_Zero_Is_Zero()
    {
        Assert.Equal(SegKey.Of(0, 0, 1, 1, Grid), SegKey.Of(-0.0, -0.01, 1, 1, Grid));
    }

    [Fact]
    public void Hash_Keeps_A_Dense_Lattice_Of_Segments_Apart()
    {
        var seen = new HashSet<long>();
        for (int i = 0; i < 1000; i++)
            for (int j = 0; j < 1000; j++)
                seen.Add(SegKey.Of(i, j, i + 1, j, 1).Hash);
        Assert.Equal(1000 * 1000, seen.Count);
    }

    [Fact]
    public void Kinds_Do_Not_Collide()
    {
        Assert.NotEqual(SegKey.Of(new LiteLine("A", 1, 2, 3, 0), Grid), SegKey.Of(new LiteCircle("A", 1, 2, 3), Grid));
        Assert.NotEqual(SegKey.Of(new LiteCircle("A", 1, 2, 3), Grid), SegKey.Of(new LiteArc("A", 1, 2, 3, 0, 0), Grid));
    }

    [Fact]
    public void Key_Ignores_The_Layer()
    {
        Assert.Equal(SegKey.Of(new LiteLine("A", 1, 2, 3, 4), Grid), SegKey.Of(new LiteLine("B", 3, 4, 1, 2), Grid));
    }

    [Fact]
    public void Bad_Grid_Is_Refused_Not_A_Single_Key_For_Everything()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SegKey.Of(1, 2, 3, 4, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Clean.MergeCollinear(new LiteEntity[] { new LiteLine("A", 0, 0, 1, 0) }, 0, out _));
    }
}

public class CleanTests
{
    const double Grid = 0.1;
    readonly ITestOutputHelper log;

    public CleanTests(ITestOutputHelper log) => this.log = log;

    static LiteLine L(double x1, double y1, double x2, double y2, string layer = "A") => new(layer, x1, y1, x2, y2);
    static LiteVertex V(double x, double y, double b = 0) => new(x, y, b);
    static LitePolyline P(bool closed, params LiteVertex[] v) => new("A", v, closed);
    static List<LiteEntity> Dedup(params LiteEntity[] e) => Clean.Dedup(e, Grid, out _);
    static List<LiteEntity> Merge(params LiteEntity[] e) => Clean.MergeCollinear(e, Grid, out _);

    [Fact]
    public void Ten_Copies_With_Swapped_Ends_Are_One()
    {
        var src = Enumerable.Range(0, 10).Select(i => i % 2 == 0 ? L(0, 0, 100, 50) : L(100, 50, 0, 0)).ToArray();
        var o = Clean.Dedup(src, Grid, out int removed);
        Assert.Same(src[0], Assert.Single(o));
        Assert.Equal(9, removed);
    }

    [Fact]
    public void Ends_Within_The_Grid_Are_One_And_Three_Cells_Apart_Are_Two()
    {
        Assert.Single(Dedup(L(0, 0, 100, 0), L(0, 0.05, 100, 0.05)));
        Assert.Single(Dedup(L(0, 0.02, 100, 0.02), L(0, -0.03, 100, -0.03)));
        Assert.Equal(2, Dedup(L(0, 0, 100, 0), L(0, 0.3, 100, 0.3)).Count);
    }

    [Fact]
    public void Ends_Straddling_A_Rounding_Edge_Stay_Two()
    {
        // the documented limit of SegKey: rounding edges fall at 0.05, 0.15, ...
        Assert.Equal(2, Dedup(L(0, 0.04, 100, 0.04), L(0, 0.06, 100, 0.06)).Count);
    }

    [Fact]
    public void The_Same_Box_At_Two_Heights_Flattens_To_Four_Lines()
    {
        var src = new List<LiteEntity>();
        foreach (double z in new double[] { 0, 1200 })
        {
            var c = new[] { new Vec3(0, 0, z), new Vec3(10, 0, z), new Vec3(10, 10, z), new Vec3(0, 10, z) };
            for (int i = 0; i < 4; i++)
                src.Add(Flatten.To2D(new Line3("A", c[i], c[(i + 1) % 4]), Affine3.Identity, Grid, "A"));
        }
        var o = Clean.Dedup(src, Grid, out int removed);
        Assert.Equal(4, o.Count);
        Assert.Equal(4, removed);
    }

    [Fact]
    public void The_First_Layer_Wins()
    {
        var o = Dedup(L(0, 0, 10, 0, "First"), L(10, 0, 0, 0, "Second"));
        Assert.Equal("First", Assert.Single(o).Layer);
    }

    [Fact]
    public void Same_Circles_Are_One_And_Another_Radius_Stays()
    {
        Assert.Single(Dedup(new LiteCircle("A", 5, 5, 3), new LiteCircle("B", 5, 5, 3), new LiteCircle("C", 5.02, 5, 3)));
        Assert.Equal(2, Dedup(new LiteCircle("A", 5, 5, 3), new LiteCircle("A", 5, 5, 3.3)).Count);
    }

    [Fact]
    public void Same_Arcs_Are_One_And_Another_Sweep_Stays()
    {
        Assert.Single(Dedup(new LiteArc("A", 0, 0, 100, 10, 90), new LiteArc("B", 0, 0, 100, 10, 90)));
        Assert.Single(Dedup(new LiteArc("A", 0, 0, 100, 0, 90), new LiteArc("A", 0, 0, 100, 360, 90))); // 0 and 360 degrees are one direction
        Assert.Single(Dedup(new LiteArc("A", 0, 0, 100, 0.01, 90), new LiteArc("A", 0, 0, 100, 0.02, 90))); // 0.0175 mm along the curve
        Assert.Equal(2, Dedup(new LiteArc("A", 0, 0, 100, 10, 90), new LiteArc("A", 0, 0, 100, 10, 180)).Count);
        Assert.Equal(2, Dedup(new LiteArc("A", 0, 0, 100, 0, 90), new LiteArc("A", 0, 0, 100, 90, 0)).Count); // the complement, not the reverse
        Assert.Equal(2, Dedup(new LiteArc("A", 0, 0, 100000, 45, 90), new LiteArc("A", 0, 0, 100000, 45.01, 90)).Count); // 17 mm on this radius
    }

    [Fact]
    public void Polylines_Match_Only_On_The_Whole_Vertex_Run()
    {
        var a = P(false, V(0, 0), V(10, 0), V(10, 10));
        Assert.Single(Dedup(a, P(false, V(0, 0), V(10, 0), V(10, 10))));
        Assert.Single(Dedup(a, P(false, V(10, 10), V(10, 0), V(0, 0))));
        Assert.Equal(2, Dedup(a, P(false, V(0, 0), V(10, 0))).Count);
        Assert.Equal(2, Dedup(a, P(false, V(0, 0), V(10, 0), V(10, 10), V(0, 10))).Count);
        Assert.Equal(2, Dedup(a, P(false, V(0, 0), V(10, 0), V(10, 11))).Count);
        Assert.Equal(2, Dedup(a, P(true, V(0, 0), V(10, 0), V(10, 10))).Count); // closing adds a side
    }

    [Fact]
    public void Reversing_A_Polyline_Flips_And_Shifts_Its_Bulges()
    {
        var open = P(false, V(0, 0, 0.5), V(10, 0, -0.25), V(10, 10));
        Assert.Single(Dedup(open, P(false, V(10, 10, 0.25), V(10, 0, -0.5), V(0, 0))));
        Assert.Equal(2, Dedup(open, P(false, V(10, 10, -0.25), V(10, 0, 0.5), V(0, 0))).Count); // same bulges unflipped: the other side
        Assert.Single(Dedup(P(false, V(0, 0, 0.5), V(10, 0, 0.9)), P(false, V(0, 0, 0.5), V(10, 0)))); // the last vertex has no span

        var closed = P(true, V(0, 0, 0.1), V(10, 0, 0.2), V(10, 10, 0.3));
        Assert.Single(Dedup(closed, P(true, V(10, 10, -0.2), V(10, 0, -0.1), V(0, 0, -0.3))));
    }

    [Fact]
    public void Dedup_Keeps_Input_Order_Across_Kinds()
    {
        var src = new LiteEntity[]
        {
            new LiteCircle("A", 0, 0, 1), L(0, 0, 1, 0), new LiteCircle("A", 0, 0, 1), P(false, V(0, 0), V(1, 1)), L(5, 5, 6, 6), L(1, 0, 0, 0), new LiteArc("A", 0, 0, 1, 0, 90),
        };
        Assert.Equal(new[] { src[0], src[1], src[3], src[4], src[6] }, Clean.Dedup(src, Grid, out int removed));
        Assert.Equal(2, removed);
    }

    [Fact]
    public void Overlapping_And_Touching_Spans_Become_One()
    {
        var o = Clean.MergeCollinear(new LiteEntity[] { L(0, 0, 10, 0), L(5, 0, 20, 0), L(20, 0, 30, 0) }, Grid, out int removed);
        Assert.Equal(L(0, 0, 30, 0), Assert.Single(o));
        Assert.Equal(2, removed);
    }

    [Fact]
    public void A_Gap_Over_The_Grid_Stays_And_One_Within_It_Is_Bridged()
    {
        var src = new LiteEntity[] { L(0, 0, 10, 0), L(11, 0, 20, 0) };
        var o = Clean.MergeCollinear(src, Grid, out int removed);
        Assert.Equal(src, o);
        Assert.Same(src[0], o[0]); // untouched lines keep their exact values
        Assert.Equal(0, removed);
        Assert.Equal(L(0, 0, 20, 0), Assert.Single(Merge(L(0, 0, 10, 0), L(10.08, 0, 20, 0))));
    }

    [Fact]
    public void A_Contained_Span_Disappears()
    {
        Assert.Equal(L(0, 0, 30, 0), Assert.Single(Merge(L(0, 0, 30, 0), L(5, 0, 10, 0))));
    }

    [Fact]
    public void Direction_Of_A_Line_Does_Not_Matter()
    {
        Assert.Equal(L(0, 0, 30, 0), Assert.Single(Merge(L(10, 0, 0, 0), L(30, 0, 5, 0))));
    }

    [Fact]
    public void Slanted_Lines_Far_From_The_Origin_Merge_On_Their_Own_End_Points()
    {
        double dx = Math.Cos(Math.PI / 6), dy = Math.Sin(Math.PI / 6), x0 = 1e6, y0 = -2e6;
        (double, double) At(double t) => (x0 + t * dx, y0 + t * dy);
        LiteLine S(double t0, double t1) => L(At(t0).Item1, At(t0).Item2, At(t1).Item1, At(t1).Item2);
        Assert.Equal(S(0, 3000), Assert.Single(Merge(S(0, 1000), S(500, 3000))));
    }

    [Fact]
    public void Lines_On_Either_Side_Of_The_Angle_Wrap_Merge()
    {
        // headings of -0.0006 and +0.0006 degrees; undirected, the first reads 179.9994
        Assert.Equal(L(-1000, 0.01, 1000, 0.01), Assert.Single(Merge(L(-1000, 0.01, 0, 0), L(0, 0, 1000, 0.01))));
    }

    [Fact]
    public void Lines_Of_One_Angle_Bin_Share_An_Axis_Far_From_The_Origin()
    {
        // slopes 1e-5 and 2e-5 at y = 1e6: positions along each line's own direction would disagree by 10 mm at the shared end
        Assert.Equal(L(-1000, 1e6 - 0.01, 1000, 1e6 + 0.02), Assert.Single(Merge(L(-1000, 1e6 - 0.01, 0, 1e6), L(0, 1e6, 1000, 1e6 + 0.02))));
    }

    [Fact]
    public void Parallel_Lines_Stay_Apart()
    {
        Assert.Equal(2, Merge(L(0, 0, 10, 0), L(0, 0.3, 10, 0.3)).Count);
        Assert.Equal(2, Merge(L(0, 0, 10, 0), L(0, 0, 0, 10)).Count); // a corner
    }

    [Fact]
    public void Merge_Sits_Where_The_First_Line_Was_On_The_First_Layer()
    {
        var circle = new LiteCircle("A", 0, 0, 1);
        var arc = new LiteArc("A", 0, 0, 1, 0, 90);
        var poly = P(false, V(0, 0), V(1, 1));
        var other = L(0, 50, 10, 50);
        var o = Merge(circle, L(5, 0, 20, 0, "First"), arc, other, L(0, 0, 10, 0, "Second"), poly);
        Assert.Equal(new LiteEntity[] { circle, L(0, 0, 20, 0, "First"), arc, other, poly }, o);
    }

    [Fact]
    public void Merge_Passes_Zero_Length_Lines_Through()
    {
        var dot = L(3, 3, 3, 3);
        Assert.Same(dot, Assert.Single(Merge(dot)));
    }

    [Fact]
    public void A_Million_Lines_Stay_Under_The_Limit()
    {
        var rnd = new Random(7);
        var src = new List<LiteEntity>(1000000);
        for (int i = 0; i < 1000000; i++)
        {
            double x = rnd.Next(1000) * 100.0, y = rnd.Next(1000) * 100.0, len = 100.0 * (1 + rnd.Next(40));
            src.Add(rnd.Next(3) switch
            {
                0 => L(x, y, x + len, y), // walls on a coarse lattice: duplicates and overlaps
                1 => L(x, y, x, y + len),
                _ => L(rnd.NextDouble() * 1e5, rnd.NextDouble() * 1e5, rnd.NextDouble() * 1e5, rnd.NextDouble() * 1e5),
            });
        }
        var watch = Stopwatch.StartNew();
        var once = Clean.Dedup(src, Grid, out int dup);
        var o = Clean.MergeCollinear(once, Grid, out int merged);
        watch.Stop();
        log.WriteLine($"1,000,000 lines: {watch.Elapsed.TotalSeconds:0.000} s, duplicates {dup}, overlaps {merged}, left {o.Count}");
        Assert.True(watch.Elapsed.TotalSeconds < 10, $"took {watch.Elapsed.TotalSeconds:0.0} s");
        Assert.True(dup > 0 && merged > 0);
        Assert.Equal(1000000 - dup - merged, o.Count);
    }
}
