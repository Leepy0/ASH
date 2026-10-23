using System.Collections.Generic;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using Xunit;

namespace ASH.Tests.Layout;

public class PathConvertTests
{
    private static readonly string[] PathLayers = { "AGV_PATH", "AGV_PATH_TWO", "AGV_PATH_SPUR", "AGV_PATH_CRAB" };
    private static (Point2, double) V(double x, double y, double bulge = 0) => (new Point2(x, y), bulge);

    [Theory]
    [InlineData("LINE", "0", false, null)]
    [InlineData("ARC", "WALL_GUIDE", false, null)]
    [InlineData("LWPOLYLINE", "0", false, null)]
    [InlineData("POLYLINE2D", "0", false, null)]
    [InlineData("LINE", "agv_path_two", false, "이미 AGV 경로")]
    [InlineData("LWPOLYLINE", "AGV_BLOCK", false, "AGV 레이어 AGV_BLOCK")]
    [InlineData("LINE", "0", true, "잠긴 레이어 0")]
    [InlineData("POLYLINE2D_FIT", "0", false, "곡선·스플라인 맞춤 폴리선")]
    [InlineData("SPLINE", "0", false, "SPLINE(지원 안 함)")]
    [InlineData("3D POLYLINE", "0", false, "3D POLYLINE(지원 안 함)")]
    public void SkipReason(string type, string layer, bool locked, string? expected) =>
        Assert.Equal(expected, PathConvert.SkipReason(type, layer, locked, PathLayers));

    [Fact] public void Vertices_Keep_Drawn_Order_And_Turn_Bulges_Into_Arcs()
    {
        // L-shape whose second piece is a CCW half circle (bulge 1), plus a repeated vertex that must vanish
        var segs = PathConvert.FromVertices(new[] { V(0, 0), V(10, 0), V(10, 0, 1), V(10, 10) }, closed: false);
        Assert.Equal(2, segs.Count);
        Assert.IsType<LineSeg>(segs[0]);
        Assert.Equal(new Point2(0, 0), segs[0].Start);
        var arc = Assert.IsType<ArcSeg>(segs[1]);
        Assert.Equal(180, arc.SweepDeg, 9);
        Assert.Equal(10, arc.End.Y, 9);
    }

    [Fact] public void Closed_Polyline_Gets_The_Closing_Segment() =>
        Assert.Equal(new Point2(0, 0), PathConvert.FromVertices(new[] { V(0, 0), V(10, 0), V(10, 10) }, closed: true).Last().End);

    [Fact] public void One_Way_Piece_Per_Segment_Named_After_Existing_Paths()
    {
        var names = new List<string> { "P001", "P007_2", "P002" };
        var segs = PathConvert.FromVertices(new[] { V(0, 0), V(10, 0), V(10, 10) }, closed: false);
        var pieces = PathConvert.Pieces(segs, (false, false, false), names);
        Assert.Equal(new[] { "P003", "P004" }, pieces.Select(p => p.Name));
        Assert.Contains("P004", names);
        Assert.All(pieces, p => Assert.False(p.IsSpur || p.IsTwoWay || p.IsCrab));
        Assert.Equal("단방향", PathKinds.Label(pieces[0]));
        Assert.Equal(new Point2(0, 0), pieces[0].Start); // drawn start = travel start
        Assert.Equal(new Point2(10, 10), pieces[1].End);
    }

    [Fact] public void Converted_Crossing_Lines_Split_Into_Four_Keeping_Direction()
    {
        var names = new List<string>();
        var paths = PathConvert.Pieces(PathConvert.FromVertices(new[] { V(-10, 0), V(10, 0) }, false), default, names)
            .Concat(PathConvert.Pieces(PathConvert.FromVertices(new[] { V(0, 10), V(0, -10) }, false), default, names)).ToList();
        var r = PathSplitter.SplitAll(paths, 0.001);
        Assert.Equal(1, r.IntersectionCount);
        Assert.Equal(new[] { "P001_1", "P001_2", "P002_1", "P002_2" }, r.Paths.Select(p => p.Name));
        Assert.Equal(-10, r.Paths[0].Start.X);  // still runs west -> east
        Assert.Equal(10, r.Paths[2].Start.Y);   // still runs north -> south
    }

    [Fact] public void Summary_Lists_Skipped_Largest_First()
    {
        var s = PathConvert.Summary(3, 7, new Dictionary<string, int> { ["SPLINE(지원 안 함)"] = 1, ["이미 AGV 경로"] = 4 });
        Assert.Equal("변환: 선·폴리선 3개 → 단방향 경로 7개 (AGV_PATH 레이어)\n건너뜀: 이미 AGV 경로 4개, SPLINE(지원 안 함) 1개", s);
        Assert.DoesNotContain('\n', PathConvert.Summary(1, 1, new Dictionary<string, int>()));
    }

    [Fact] public void Guidance_Names_The_Commands()
    {
        foreach (var cmd in new[] { "AGVREVERSE", "AGVPTYPE", "AGVCP", "AGVCHECK", "Ctrl+Z" }) Assert.Contains(cmd, PathConvert.Guidance);
    }
}
