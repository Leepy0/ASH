using System;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using Xunit;

namespace ASH.Tests.Layout;

public class PathGraphTests
{
    [Fact] public void Cross_Of_Four_Arms_Is_One_Junction_Degree_4()
    {
        var g = PathGraph.Build(L.Of(L.Line("a",-10,0,0,0), L.Line("b",0,0,10,0), L.Line("c",0,-10,0,0), L.Line("d",0,0,0,10)));
        var j = Assert.Single(g.Junctions); Assert.Equal(4, j.Degree); Assert.Equal(2, j.InCount); Assert.Equal(2, j.OutCount);
        Assert.Equal(4, g.DanglingEnds.Count());
    }
    [Fact] public void Graph_EndpointsWithinTolerance_Connect()
    {
        var g1 = PathGraph.Build(L.Of(L.Line("a",0,0,10,0), L.Line("b",10.0009,0,20,0)));
        Assert.Single(g1.Nodes, n => n.Degree == 2);
        var g2 = PathGraph.Build(L.Of(L.Line("a",0,0,10,0), L.Line("b",10.0011,0,20,0)));
        Assert.DoesNotContain(g2.Nodes, n => n.Degree == 2);
    }
    [Fact] public void Grid_Near_Crosses_Cell_Boundary()
    {
        var grid = new SpatialGrid<int>(1.0); grid.Add(new(0.9999,0), 1); grid.Add(new(1.0001,0), 2);
        Assert.Equal(new[]{1,2}, grid.Near(new(1.0,0), 0.001).OrderBy(x=>x));
    }
    [Fact] public void ArcSeg_End_And_Length() { var a = new ArcSeg(new(0,0), new(1,0), 90); Assert.Equal(0, a.End.X, 9); Assert.Equal(1, a.End.Y, 9); Assert.Equal(Math.PI/2, a.Length, 9); }
}
