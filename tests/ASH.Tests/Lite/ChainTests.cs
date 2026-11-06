using ASH.Core.Lite;

namespace ASH.Tests.Lite;

public class ChainTests
{
    const double Grid = 0.1;

    static LiteLine L(double x1, double y1, double x2, double y2, string layer = "A") => new(layer, x1, y1, x2, y2);
    static LitePolyline P(bool closed, params LiteVertex[] v) => new("A", v, closed);
    static LiteVertex V(double x, double y, double bulge = 0) => new(x, y, bulge);
    static double[] Xs(LiteEntity e) => ((LitePolyline)e).Vertices.Select(v => v.X).ToArray();

    /// <summary>Radius 5 m, 3,000 lines of 5 mm: 3 radians of arc.</summary>
    internal static List<LiteEntity> ArcPieces()
    {
        const double R = 5000, Step = 5.0 / R;
        var o = new List<LiteEntity>();
        for (int i = 0; i < 3000; i++)
            o.Add(L(R * Math.Cos(i * Step), R * Math.Sin(i * Step), R * Math.Cos((i + 1) * Step), R * Math.Sin((i + 1) * Step)));
        return o;
    }

    [Fact]
    public void An_Arc_Of_3000_Short_Lines_Becomes_One_Polyline_Within_Tolerance()
    {
        var pieces = ArcPieces();
        var joined = Assert.IsType<LitePolyline>(Assert.Single(Chain.Join(pieces, Grid)));
        Assert.Equal(3001, joined.Vertices.Count);
        Assert.False(joined.Closed);

        var thin = Chain.Simplify(joined, 10);
        int steps = CurveApprox.Steps(3, 5000, 10);
        Assert.InRange(thin.Vertices.Count, 3, steps * 3 / 2 + 1); // Douglas-Peucker halves, CurveApprox divides evenly
        foreach (var v in joined.Vertices)
        {
            double best = double.PositiveInfinity;
            for (int i = 0; i + 1 < thin.Vertices.Count; i++)
                best = Math.Min(best, CurveApprox.DistToSegment((v.X, v.Y), (thin.Vertices[i].X, thin.Vertices[i].Y), (thin.Vertices[i + 1].X, thin.Vertices[i + 1].Y)));
            Assert.True(best <= 10, $"{best} mm off at ({v.X}, {v.Y})");
        }
    }

    [Fact]
    public void A_T_Branch_Breaks_Into_Three()
    {
        var r = Chain.Join(new LiteEntity[] { L(0, 0, 10, 0), L(10, 0, 20, 0), L(20, 0, 30, 0), L(30, 0, 40, 0), L(20, 0, 20, 10), L(20, 10, 20, 20) }, Grid);
        Assert.Equal(3, r.Count);
        Assert.Equal(new double[] { 0, 10, 20 }, Xs(r[0]));
        Assert.Equal(new double[] { 20, 30, 40 }, Xs(r[1]));
        Assert.Equal(new double[] { 20, 20, 20 }, Xs(r[2]));
    }

    [Fact]
    public void Four_Lines_Of_A_Square_Close_Whatever_Way_They_Were_Drawn()
    {
        var r = Assert.IsType<LitePolyline>(Assert.Single(Chain.Join(new LiteEntity[] { L(0, 0, 10, 0), L(10, 10, 10, 0), L(0, 10, 10, 10), L(0, 10, 0, 0) }, Grid)));
        Assert.True(r.Closed);
        Assert.Equal(new[] { V(0, 0), V(10, 0), V(10, 10), V(0, 10) }, r.Vertices);
    }

    [Fact]
    public void Only_The_Same_Layer_Links_And_Other_Layers_Are_No_Branch()
    {
        Assert.Equal(2, Chain.Join(new LiteEntity[] { L(0, 0, 10, 0), L(10, 0, 20, 0, "B") }, Grid).Count);

        var r = Chain.Join(new LiteEntity[] { L(0, 0, 10, 0), L(10, 0, 20, 0), L(10, 0, 10, 10, "B") }, Grid);
        Assert.Equal(2, r.Count);
        Assert.Equal(new double[] { 0, 10, 20 }, Xs(r[0]));
        Assert.Equal("B", r[1].Layer);
    }

    [Fact]
    public void A_Chain_Sits_Where_Its_First_Piece_Was_And_Runs_That_Piece_S_Way()
    {
        var c1 = new LiteCircle("A", 100, 100, 1);
        var c2 = new LiteCircle("A", 200, 100, 1);
        var r = Chain.Join(new LiteEntity[] { c1, L(20, 0, 10, 0), c2, L(0, 0, 10, 0), L(20, 0, 30, 0) }, Grid);
        Assert.Equal(3, r.Count);
        Assert.Same(c1, r[0]);
        Assert.Equal(new double[] { 30, 20, 10, 0 }, Xs(r[1]));
        Assert.Same(c2, r[2]);

        // the first piece in the middle: pieces on both sides, the same way
        r = Chain.Join(new LiteEntity[] { L(10, 0, 20, 0), L(30, 0, 20, 0), L(10, 0, 0, 0) }, Grid);
        Assert.Equal(new double[] { 0, 10, 20, 30 }, Xs(Assert.Single(r)));
    }

    [Fact]
    public void A_Lone_Piece_And_What_Cannot_Chain_Come_Through_As_They_Are()
    {
        var src = new LiteEntity[]
        {
            L(0, 0, 10, 0),
            P(false, V(50, 0), V(60, 0, 0.5), V(70, 0)),
            P(true, V(100, 0), V(110, 0), V(110, 10)),
            new LiteArc("A", 10, 0, 5, 0, 90),
            new LiteCircle("A", 10, 0, 5),
        };
        var r = Chain.Join(src, Grid);
        Assert.Equal(src.Length, r.Count);
        for (int i = 0; i < src.Length; i++) Assert.Same(src[i], r[i]);
    }

    [Fact]
    public void A_Polyline_Whose_Ends_Meet_Stays_Out_Of_The_Chains()
    {
        var loop = P(false, V(0, 0), V(10, 0), V(10, 10), V(0, 0));
        var tiny = L(5, 5, 5.03, 5); // under a grid cell
        var r = Chain.Join(new LiteEntity[] { L(-10, 0, 0, 0), loop, L(0, 0, 0, -10), tiny }, Grid);
        Assert.Equal(new double[] { -10, 0, 0 }, Xs(r[0]));
        Assert.Same(loop, r[1]);
        Assert.Same(tiny, r[2]);
    }

    [Fact]
    public void Two_Lines_On_The_Same_Two_Points_Do_Not_Make_A_Closed_Two_Point_Polyline()
    {
        var r = Assert.IsType<LitePolyline>(Assert.Single(Chain.Join(new LiteEntity[] { L(0, 0, 10, 0), L(10, 0, 0, 0) }, Grid)));
        Assert.False(r.Closed);
        Assert.Equal(new[] { V(0, 0), V(10, 0), V(0, 0) }, r.Vertices);
    }

    [Fact]
    public void Joining_Keeps_Bulges_And_Turns_Them_With_A_Reversed_Polyline()
    {
        var arc = P(false, V(10, 0, 1), V(0, 0));
        var r = Assert.IsType<LitePolyline>(Assert.Single(Chain.Join(new LiteEntity[] { L(-10, 0, 0, 0), arc, L(10, 0, 20, 0) }, Grid)));
        Assert.Equal(new[] { V(-10, 0), V(0, 0, -1), V(10, 0), V(20, 0) }, r.Vertices);
        var (want, got) = (LiteBox.Of(new LiteEntity[] { arc, L(-10, 0, 20, 0) }), LiteBox.Of(new[] { r }));
        Assert.Equal(5, want.MaxY, 9);
        Assert.Equal(want.MaxY, got.MaxY, 9); // the same side of the chord
        Assert.Equal(want.MinY, got.MinY, 9);
    }

    [Fact]
    public void A_Bulge_On_An_Open_End_Does_Not_Bend_The_Chain()
    {
        var r = Assert.IsType<LitePolyline>(Assert.Single(Chain.Join(new LiteEntity[] { P(false, V(0, 0), V(10, 0, 0.7)), L(10, 0, 20, 0) }, Grid)));
        Assert.Equal(new[] { V(0, 0), V(10, 0), V(20, 0) }, r.Vertices);
    }

    [Fact]
    public void Vertices_On_A_Straight_Line_Go()
    {
        var r = Chain.Simplify(P(false, V(0, 0), V(10, 0), V(20, 0), V(30, 0), V(30, 40)), 0.1);
        Assert.Equal(new[] { V(0, 0), V(30, 0), V(30, 40) }, r.Vertices);
    }

    [Fact]
    public void Bulge_Spans_Stay_And_The_Straight_Runs_Around_Them_Thin_Out()
    {
        var src = P(false, V(0, 0), V(10, 0), V(20, 0, 0.5), V(30, 0), V(40, 0), V(50, 0));
        var r = Chain.Simplify(src, 0.1);
        Assert.Equal(new[] { V(0, 0), V(20, 0, 0.5), V(30, 0), V(50, 0) }, r.Vertices);
        Assert.Equal(LiteBox.Of(new[] { src }), LiteBox.Of(new[] { r }));
    }

    [Fact]
    public void A_Closed_Polyline_Keeps_Its_Corners_And_Its_Closing_Span()
    {
        var r = Chain.Simplify(P(true, V(0, 0), V(50, 0), V(100, 0), V(100, 50), V(100, 100), V(50, 100), V(0, 100), V(0, 50)), 0.1);
        Assert.True(r.Closed);
        Assert.Equal(new[] { V(0, 0), V(100, 0), V(100, 100), V(0, 100) }, r.Vertices);
    }

    [Fact]
    public void A_Closed_Sliver_Inside_The_Tolerance_Is_No_Closed_Two_Point_Polyline()
    {
        var r = Chain.Simplify(P(true, V(0, 0), V(50, 1), V(100, 0), V(50, -1)), 10);
        Assert.False(r.Closed);
        Assert.Equal(new[] { V(0, 0), V(100, 0) }, r.Vertices);
    }

    [Fact]
    public void A_Closed_Polyline_Thinned_To_Two_Points_With_A_Bulge_Stays_Closed()
    {
        var r = Chain.Simplify(P(true, V(0, 0, 1), V(100, 0), V(50, 0.5)), 10);
        Assert.True(r.Closed);
        Assert.Equal(new[] { V(0, 0, 1), V(100, 0) }, r.Vertices);
    }

    [Fact]
    public void Nothing_To_Thin_Returns_The_Same_Polyline()
    {
        var p = P(false, V(0, 0), V(10, 0), V(10, 10));
        Assert.Same(p, Chain.Simplify(p, 0.1));
    }
}
