using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Geometry;

namespace ASH.Core.Layout;

public sealed class PathNode
{
    public Point2 Position { get; set; }
    public List<(LayoutPath Path, bool AtStart)> Ends { get; } = new();
    public int Degree => Ends.Count;
    public bool IsJunction => Degree >= 3;
    // a two-way path can be entered and left at both ends
    public int InCount => Ends.Count(e => e.Path.IsTwoWay || !e.AtStart);
    public int OutCount => Ends.Count(e => e.Path.IsTwoWay || e.AtStart);
}

public sealed class PathGraph
{
    private PathGraph(List<PathNode> nodes) { Nodes = nodes; }
    public IReadOnlyList<PathNode> Nodes { get; }
    public IEnumerable<PathNode> Junctions => Nodes.Where(n => n.IsJunction);
    public IEnumerable<(LayoutPath Path, bool AtStart)> DanglingEnds =>
        Nodes.Where(n => n.Degree == 1).Select(n => n.Ends[0]);

    public static PathGraph Build(Layout l)
    {
        double tol = l.Tolerance;
        var grid = new SpatialGrid<PathNode>(Math.Max(tol * 10, 0.01));
        var nodes = new List<PathNode>();
        void Attach(LayoutPath path, bool atStart)
        {
            var p = atStart ? path.Start : path.End;
            var node = grid.Near(p, tol).FirstOrDefault();
            if (node == null)
            {
                node = new PathNode { Position = p };
                grid.Add(p, node);
                nodes.Add(node);
            }
            node.Ends.Add((path, atStart));
        }
        foreach (var path in l.Paths)
        {
            if (path.Segments.Count == 0) continue;
            Attach(path, true);
            Attach(path, false);
        }
        return new PathGraph(nodes.OrderBy(n => n.Position.X).ThenBy(n => n.Position.Y).ToList());
    }
}
