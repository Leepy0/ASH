using ASH.Core.Geometry;

namespace ASH.Tests.Geometry;

public class BulgeTests
{
    [Fact] public void Zero_Is_Line() => Assert.IsType<LineSeg>(Bulge.ToSegment(new Point2(0, 0), new Point2(2, 0), 0));

    [Fact]
    public void Semicircle_Centre_On_Chord_Midpoint()
    {
        var a = Assert.IsType<ArcSeg>(Bulge.ToSegment(new Point2(0, 0), new Point2(2, 0), 1));
        Assert.Equal(1, a.Center.X, 9); Assert.Equal(0, a.Center.Y, 9); Assert.Equal(180, a.SweepDeg, 9);
        Assert.Equal(2, a.End.X, 9); Assert.Equal(0, a.End.Y, 9);
    }

    [Theory]
    [InlineData(0.5, 0.75)]
    [InlineData(-0.5, -0.75)]
    public void Half_Bulge_Centre_Side_And_Sweep(double b, double cy)
    {
        var a = Assert.IsType<ArcSeg>(Bulge.ToSegment(new Point2(0, 0), new Point2(2, 0), b));
        Assert.Equal(1, a.Center.X, 9); Assert.Equal(cy, a.Center.Y, 9);
        Assert.Equal(System.Math.Sign(b) * 106.2602047, a.SweepDeg, 6);
        Assert.Equal(2, a.End.X, 9); Assert.Equal(0, a.End.Y, 9);
        Assert.Equal(b, Bulge.FromSweepDeg(a.SweepDeg), 9);
    }
}
