using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;

namespace ASH.Tests.Layout;

public class PathConnectTests
{
    const double Tol = 0.001, Gap = 0.3;

    [Fact]
    public void End_Within_Gap_Snaps_Onto_Other_End()
    {
        var a = L.Line("a", 0, 0, 10, 0);
        var b = L.Line("b", 10.2, 0, 20, 0);
        var r = PathConnect.Connect(new[] { a, b }, new[] { b.Id }, Gap, Tol);
        Assert.Equal(new[] { b.Id }, r.Removed);
        var nb = Assert.Single(r.Added);
        Assert.Equal("b", nb.Name);
        Assert.Equal(new Point2(10, 0), nb.Start); // exactly a's end
        Assert.Equal(new Point2(20, 0), nb.End);
        Assert.Single(r.Notes);
    }

    [Fact]
    public void End_Beyond_Gap_Stays()
    {
        var r = PathConnect.Connect(new[] { L.Line("a", 0, 0, 10, 0), L.Line("b", 10.4, 0, 20, 0) }, null, Gap, Tol);
        Assert.Empty(r.Added);
        Assert.Empty(r.Removed);
        Assert.Empty(r.Notes);
    }

    [Fact]
    public void End_Near_Middle_Snaps_And_Splits_The_Other_Path()
    {
        var a = L.Line("a", 0, 0, 10, 0);
        var b = L.Line("b", 5, 0.25, 5, 10);
        var r = PathConnect.Connect(new[] { a, b }, new[] { b.Id }, Gap, Tol);
        Assert.Equal(new[] { a.Id, b.Id }.OrderBy(x => x), r.Removed.OrderBy(x => x));
        var nb = r.Added.Single(p => p.Name == "b");
        Assert.Equal(new Point2(5, 0), nb.Start);
        Assert.Equal(new[] { "a_1", "a_2" }, r.Added.Where(p => p.Name.StartsWith("a")).Select(p => p.Name).OrderBy(x => x));
        Assert.Equal(new Point2(5, 0), r.Added.Single(p => p.Name == "a_1").End);
        Assert.Equal(new Point2(5, 0), r.Added.Single(p => p.Name == "a_2").Start);
    }

    [Fact]
    public void End_Beyond_Gap_From_Middle_Does_Not_Split()
    {
        var r = PathConnect.Connect(new[] { L.Line("a", 0, 0, 10, 0), L.Line("b", 5, 0.5, 5, 10) }, null, Gap, Tol);
        Assert.Empty(r.Added);
    }

    [Fact]
    public void Joined_Ends_And_Fixed_Paths_Are_Left_Alone()
    {
        // b already meets a; c is near a's end but only b may move
        var a = L.Line("a", 0, 0, 10, 0);
        var b = L.Line("b", 10, 0, 20, 0);
        var c = L.Line("c", 0.2, 5, 0.2, 0.2);
        var r = PathConnect.Connect(new[] { a, b, c }, new[] { b.Id }, Gap, Tol);
        Assert.Empty(r.Added);
    }

    [Fact]
    public void Arc_End_Gets_A_Straight_Bridge()
    {
        var a = L.Line("a", 0, 0, 10, 0);
        var arc = new LayoutPath("r") { Segments = { new ArcSeg(new Point2(10.1, 5), new Point2(10.1, 0), 90) } };
        var r = PathConnect.Connect(new[] { a, arc }, new[] { arc.Id }, Gap, Tol);
        var n = r.Added.Single(p => p.Name == "r");
        Assert.Equal(2, n.Segments.Count);
        Assert.Equal(new Point2(10, 0), n.Start);
        Assert.IsType<ArcSeg>(n.Segments[1]);
    }

    [Fact]
    public void Gaps_Mark_Free_Ends_But_Not_Spur_Ends_Or_T_Ends()
    {
        var l = L.Of(L.Line("a", 0, 0, 10, 0), L.Line("b", 10, 0, 20, 0), L.Spur("s", 20, 0, 20, 5), L.Line("t", 5, 5, 5, 0));
        var g = PathConnect.Gaps(l);
        // a's start and t's start are free; b→s ends in a spur; t's end lies on a's middle
        Assert.Equal(new[] { new Point2(0, 0), new Point2(5, 5) }, g.Where(x => !x.Flow).Select(x => x.At).OrderBy(p => p.X));
    }

    [Fact]
    public void Offset_Copy_Narrower_Than_Gap_Never_Joins_Its_Source_Or_Other_Copies()
    {
        var a = L.Line("a", 0, 0, 10, 0);
        var copy1 = PathOps.Offset(a, 0.2, reverseDirection: false);
        var copy2 = PathOps.Offset(a, -0.2, reverseDirection: false); // 0.4 from copy1, 0.2 from a: both off limits
        var other = L.Line("c", 10.1, 0.2, 20, 0.2); // a different path near copy1's end
        var copies = new[] { copy1.Id, copy2.Id };
        var r = PathConnect.Connect(new[] { a, copy1, copy2, other }, copies, Gap, Tol, copies.Concat(new[] { a.Id }).ToList());
        var n = Assert.Single(r.Added);
        Assert.Equal(copy1.Id + "#0", n.Id);
        Assert.Equal(new Point2(0, 0.2), n.Start); // start stays: a and copy2 are off limits
        Assert.Equal(new Point2(10.1, 0.2), n.End); // end joins the other path
        Assert.Single(r.Notes);
        Assert.NotEmpty(PathConnect.Connect(new[] { a, copy1 }, new[] { copy1.Id }, Gap, Tol).Notes); // without avoid it would join a
    }

    [Fact]
    public void Gaps_Skip_Two_Way_Free_End_Like_AgvCheck()
    {
        var two = L.Line("t", 10, 0, 20, 0);
        two.IsTwoWay = true;
        var l = L.Of(L.Line("a", 0, 0, 10, 0), two);
        var g = PathConnect.Gaps(l);
        Assert.Equal(new[] { new Point2(0, 0) }, g.Where(x => !x.Flow).Select(x => x.At));
        Assert.Equal(LayoutChecker.Check(l, new CheckOptions()).Count(f => f.Code == FindingCode.DanglingEnd), g.Count(x => !x.Flow));
    }

    [Fact]
    public void Gaps_Mark_One_Way_Flow_Dead_End()
    {
        var l = L.Of(L.Line("a", 0, 0, 10, 0), L.Line("b", 20, 0, 10, 0)); // both run into (10,0)
        var g = PathConnect.Gaps(l);
        Assert.Contains(g, x => x.Flow && x.At == new Point2(10, 0));
        var two = L.Line("b", 20, 0, 10, 0);
        two.IsTwoWay = true;
        Assert.DoesNotContain(PathConnect.Gaps(L.Of(L.Line("a", 0, 0, 10, 0), two)), x => x.Flow);
    }

    [Fact]
    public void Flow_Frame_Moves_Arrows_Forward_With_Phase()
    {
        var a = L.Line("a", 0, 0, 10, 0);
        var f0 = PathFlow.Frame(new[] { a }, 5, 1, 0);
        var f1 = PathFlow.Frame(new[] { a }, 5, 1, 0.5);
        Assert.Equal(2, f0.Count);
        Assert.Equal(f0.Count, f1.Count);
        Assert.Equal(0.5, f0[0].Tip.X, 9);
        Assert.Equal(3.0, f1[0].Tip.X, 9);
        Assert.True(f0[0].Left.X < f0[0].Tip.X && f0[0].Left.Y > 0 && f0[0].Right.Y < 0);
        var two = L.Line("t", 0, 0, 10, 0);
        two.IsTwoWay = true;
        var ft = PathFlow.Frame(new[] { two }, 5, 1, 0.2);
        Assert.Equal(4, ft.Count);
        Assert.True(ft[1].Tip.X < ft[1].Left.X); // the backward arrow points -x
    }
}
