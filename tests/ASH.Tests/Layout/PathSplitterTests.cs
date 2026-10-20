using System.Collections.Generic;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using Xunit;

namespace ASH.Tests.Layout;

public class PathSplitterTests
{
    [Fact] public void Cross_Splits_Both_Into_Two()
    {
        var r = PathSplitter.SplitAll(new[]{ L.Line("h",-10,0,10,0), L.Line("v",0,-10,0,10) }, 0.001);
        Assert.Equal(1, r.IntersectionCount); Assert.Equal(4, r.Paths.Count);
        Assert.Equal(new[]{"h_1","h_2"}, r.Renamed["h"]);
        var h1 = r.Paths.Single(p => p.Name=="h_1"); Assert.Equal(-10, h1.Start.X); Assert.Equal(0, h1.End.X, 9);
    }
    [Fact] public void T_Junction_Splits_Only_The_Crossed_Path()
    {
        var r = PathSplitter.SplitAll(new[]{ L.Line("h",-10,0,10,0), L.Line("v",0,0,0,10) }, 0.001);
        Assert.Equal(3, r.Paths.Count); Assert.Single(r.Paths, p => p.Name == "v");
    }
    [Fact] public void N_Intersections_Give_N_Plus_One_Pieces()
    {
        var paths = new List<LayoutPath>{ L.Line("h",0,0,100,0) };
        for (int i = 1; i <= 5; i++) paths.Add(L.Line("v"+i, i*10,-5, i*10,5));
        var r = PathSplitter.SplitAll(paths, 0.001);
        Assert.Equal(6, r.Renamed["h"].Count);
    }
    [Fact] public void Line_Arc_Intersection_Splits_Arc_Keeping_Sweep_Sign()
    {
        var arc = new LayoutPath("arc"){ Segments = { new ArcSeg(new(0,0), new(10,0), 180) } };   // (10,0)→(-10,0) 위쪽 반원
        var r = PathSplitter.SplitAll(new[]{ arc, L.Line("v",0,-20,0,20) }, 0.001);
        var pieces = r.Paths.Where(p => p.Name.StartsWith("arc_")).ToList();
        Assert.Equal(2, pieces.Count); Assert.All(pieces, p => Assert.True(((ArcSeg)p.Segments[0]).SweepDeg > 0));
        Assert.Equal(0, pieces[0].End.X, 6); Assert.Equal(10, pieces[0].End.Y, 6);
    }
    [Fact] public void Touching_Endpoints_Are_Not_Intersections()
    {
        var r = PathSplitter.SplitAll(new[]{ L.Line("a",0,0,10,0), L.Line("b",10,0,20,0) }, 0.001);
        Assert.Equal(0, r.IntersectionCount); Assert.Equal(2, r.Paths.Count);
    }
    [Fact] public void SplitAll_ClosedLoop_ProducesOnePiecePerArc()
    {
        var loop = new LayoutPath("loop"){ Segments = { new ArcSeg(new(0,0), new(10,0), 180), new ArcSeg(new(0,0), new(-10,0), 180) } };
        var r = PathSplitter.SplitAll(new[]{ loop, L.Line("v",0,-20,0,20) }, 0.001);
        Assert.Equal(2, r.IntersectionCount); Assert.Equal(3, r.Renamed["loop"].Count);  // 루프는 시작점에서 이미 끊겨 있으므로 2교차 → 3조각
    }
    [Fact] public void T_Junction_At_Vertex_Of_LineArc_Path_Splits_It()
    {
        var b = new LayoutPath("b"){ Segments = { new LineSeg(new(0,0), new(10,0)), new ArcSeg(new(10,2), new(10,0), 90) } };
        var r = PathSplitter.SplitAll(new[]{ b, L.Line("a",10,-5,10,0) }, 0.001);
        Assert.Equal(1, r.IntersectionCount); Assert.Equal(3, r.Paths.Count);
        Assert.Equal(new[]{"b_1","b_2"}, r.Renamed["b"]);
        Assert.IsType<LineSeg>(r.Paths.Single(p => p.Name=="b_1").Segments.Single());
        Assert.IsType<ArcSeg>(r.Paths.Single(p => p.Name=="b_2").Segments.Single());
        Assert.Single(r.Paths, p => p.Name == "a");
    }
    [Fact] public void Cross_At_Shared_Vertex_Splits_Both()
    {
        var h = new LayoutPath("H"){ Segments = { new LineSeg(new(-10,0), new(0,0)), new LineSeg(new(0,0), new(10,0)) } };
        var v = new LayoutPath("V"){ Segments = { new LineSeg(new(0,-10), new(0,0)), new LineSeg(new(0,0), new(0,10)) } };
        var r = PathSplitter.SplitAll(new[]{ h, v }, 0.001);
        Assert.Equal(1, r.IntersectionCount); Assert.Equal(4, r.Paths.Count);
    }
    [Fact] public void Arc_Starting_On_Line_Interior_Splits_Line_In_Every_Direction()
    {
        // Arc starts at (5,0) on the line; rotated about (5,0). Headings in [135,180) and [315,360) curl back across the line
        // within the 90-degree sweep (a genuine second crossing), so only the T-junction cut at x=5 is asserted there.
        for (int k = 0; k < 52; k++)
        {
            double phi = k * 7.0, r = phi * Math.PI / 180.0;
            var center = new Point2(5 - 2 * Math.Sin(r), 2 * Math.Cos(r));
            var arc = new LayoutPath("arc"){ Segments = { new ArcSeg(center, new(5,0), 90) } };
            var res = PathSplitter.SplitAll(new[]{ L.Line("h",0,0,20,0), arc }, 0.001);
            var h = res.Paths.Where(p => p.Name.StartsWith("h")).OrderBy(p => p.Start.X).ToList();
            double m = phi % 180;
            bool returns = m >= 135;
            if (!returns) { Assert.True(1 == res.IntersectionCount, "phi " + phi); Assert.Equal(2, h.Count); }
            else Assert.True(res.IntersectionCount >= 1, "phi " + phi);
            Assert.Contains(h, p => Math.Abs(p.End.X - 5) < 1e-6);
        }
    }
    [Fact] public void Tangent_Arc_On_Line_Interior_Splits_Line_In_Rotated_Scenes()
    {
        // Whole scene (line + tangent arc) rotated about (5,0): float noise makes the tangent disc slightly negative.
        static Point2 Rot(double x, double y, double r) => new(5 + (x - 5) * Math.Cos(r) - y * Math.Sin(r), (x - 5) * Math.Sin(r) + y * Math.Cos(r));
        for (int k = 0; k < 52; k++)
        {
            double r = k * 7.0 * Math.PI / 180.0;
            var line = new LayoutPath("h"){ Segments = { new LineSeg(Rot(0,0,r), Rot(20,0,r)) } };
            var arc = new LayoutPath("arc"){ Segments = { new ArcSeg(Rot(5,2,r), new(5,0), 90) } };
            var res = PathSplitter.SplitAll(new[]{ line, arc }, 0.001);
            Assert.True(1 == res.IntersectionCount, "k " + k);
            Assert.Equal(2, res.Paths.Count(p => p.Name.StartsWith("h_")));
            var hits = Intersections.LineArc((LineSeg)line.Segments[0], (ArcSeg)arc.Segments[0], 0.001);
            Assert.True(hits.Count >= 1, "LineArc k " + k);
        }
    }
    [Fact] public void Tangent_Line_Arc_Is_Found()
    {
        var hits = Intersections.LineArc(new LineSeg(new(0,0), new(20,0)), new ArcSeg(new(5,2.0000005), new(5,0.0000005), -180), 0.001);
        Assert.Single(hits);
    }
    [Fact] public void P_Loop_End_On_Own_First_Segment_Is_Split()
    {
        var p = new LayoutPath("p"){ Segments = { new LineSeg(new(0,0), new(10,0)), new LineSeg(new(10,0), new(10,5)), new LineSeg(new(10,5), new(5,5)), new LineSeg(new(5,5), new(5,0)) } };
        Assert.Single(PathSplitter.FindUnsplitIntersections(new[]{ p }, 0.001));
        var r = PathSplitter.SplitAll(new[]{ p }, 0.001);
        Assert.Equal(2, r.Paths.Count); Assert.Equal(5, r.Paths[0].End.X, 9);
    }
    [Fact] public void Figure8_Self_Cross_Is_One_Intersection()
    {
        var p = new LayoutPath("f"){ Segments = { new LineSeg(new(0,0), new(10,10)), new LineSeg(new(10,10), new(10,0)), new LineSeg(new(10,0), new(0,10)) } };
        Assert.Single(PathSplitter.FindUnsplitIntersections(new[]{ p }, 0.001));
        Assert.Equal(3, PathSplitter.SplitAll(new[]{ p }, 0.001).Paths.Count);
    }
    [Fact] public void Polyline_Vertices_Are_Not_Self_Intersections()
    {
        var p = new LayoutPath("z"){ Segments = { new LineSeg(new(0,0), new(10,0)), new LineSeg(new(10,0), new(10,10)), new ArcSeg(new(5,10), new(10,10), 180) } };
        Assert.Empty(PathSplitter.FindUnsplitIntersections(new[]{ p }, 0.001));
    }
    [Fact] public void Split_Piece_Names_Avoid_Existing_Names()
    {
        var r = PathSplitter.SplitAll(new[]{ L.Line("path11",-10,0,10,0), L.Line("path11_1",0,-10,0,10) }, 0.001);
        Assert.Equal(4, r.Paths.Select(p => p.Name).Distinct().Count());
        Assert.Equal(new[]{"path11_2","path11_3"}, r.Renamed["path11"]);
    }
    [Fact] public void Piece_Ids_Are_Original_Id_Hash_K() // PathCommands.SplitPaths parses this
    {
        var h = L.Line("h",-10,0,10,0); h.Id = "1A";
        var r = PathSplitter.SplitAll(new[]{ h, L.Line("v",0,-10,0,10) }, 0.001);
        Assert.Equal(new[]{"1A#1","1A#2"}, r.Paths.Where(p => p.Name.StartsWith("h_")).Select(p => p.Id).OrderBy(x => x));
    }
}
