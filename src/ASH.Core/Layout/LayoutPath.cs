using System;
using System.Collections.Generic;
using ASH.Core.Geometry;

namespace ASH.Core.Layout;

public sealed class LayoutPath
{
    public LayoutPath(string name) { Name = name; }
    public string Name { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool IsSpur { get; set; }
    /// <summary>Drawing order = travel direction.</summary>
    public List<Segment> Segments { get; set; } = new();

    public Point2 Start => Segments[0].Start;
    public Point2 End => Segments[Segments.Count - 1].End;

    public double Length
    {
        get { double s = 0; foreach (var g in Segments) s += g.Length; return s; }
    }

    public Point2 PointAt(double dist)
    {
        if (dist <= 0) return Start;
        foreach (var g in Segments)
        {
            if (dist <= g.Length) return g.PointAt(dist);
            dist -= g.Length;
        }
        return End;
    }
}
