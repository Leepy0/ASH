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
}
