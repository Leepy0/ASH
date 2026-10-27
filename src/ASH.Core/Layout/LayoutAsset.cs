using System;
using System.Collections.Generic;
using ASH.Core.Geometry;

namespace ASH.Core.Layout;

/// <summary>Kinds of 3D picture assets. Block kinds come from ASH_3D_&lt;KIND&gt; block names, line kinds from the AGV_3D_WALL / AGV_3D_FENCE layers.</summary>
public static class AssetKinds
{
    public const string Wall = "WALL", Fence = "FENCE", Door = "DOOR", RobotL = "ROBOT_L", RobotM = "ROBOT_M", RobotS = "ROBOT_S",
        Rack = "RACK", Machine = "MACHINE", Column = "COLUMN", Charger = "CHARGER", Zone = "ZONE", Bench = "BENCH";
    /// <summary>Kinds placed as blocks (AGV3D), in palette order.</summary>
    public static readonly string[] Blocks = { RobotL, RobotM, RobotS, Rack, Machine, Column, Charger, Bench, Door, Zone };
    /// <summary>Kinds drawn as lines or polylines on their own layer.</summary>
    public static readonly string[] Lines = { Wall, Fence };
}

/// <summary>A 3D picture asset. X, Y and Segments are in the drawing frame like the rest of Layout (DrawingReader); AssetShapes.InMetres takes them to metres
/// in the model frame. Block kinds: X, Y = insertion point (floor centre), RotDeg, Sx/Sy = multiples of the kind's base size.
/// Line kinds: Segments in drawing order, WidthM = polyline global width in metres (0 = kind default).</summary>
public sealed class LayoutAsset
{
    public string Kind { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double RotDeg { get; set; }
    public double Sx { get; set; } = 1;
    public double Sy { get; set; } = 1;
    public List<Segment> Segments { get; set; } = new();
    public double WidthM { get; set; }
}

/// <summary>A conveyor polyline, segments in drawing units like LayoutPath (ExportOptions.ToModel converts); drawing order = flow direction. Name is the AGVCONV name (CV001), empty for GraphicOnly.</summary>
public sealed class LayoutConveyor
{
    public string Name { get; set; } = "";
    public bool GraphicOnly { get; set; }
    public List<Segment> Segments { get; set; } = new();
    /// <summary>Polyline global width in metres (0 = 0.6 m default).</summary>
    public double WidthM { get; set; }
    public string Id { get; set; } = "";
    /// <summary>Use point from the drawing marker (AGVCONVUSE): metres from the polyline start; null = no marker. Wins over the Excel ConsumeAt.</summary>
    public double? ConsumeAtM { get; set; }

    /// <summary>The point of the polyline nearest p: distance from the start (drawing units), the point and the flow direction there (radians).</summary>
    public (double Dist, Point2 Point, double Angle) Nearest(Point2 p)
    {
        double best = double.MaxValue, before = 0, bestAlong = 0, run = 0;
        Segment? hit = null;
        foreach (var s in Segments)
        {
            double along = s.DistanceAlong(p), d = s.PointAt(along).DistanceTo(p);
            if (d < best) { best = d; hit = s; before = run; bestAlong = along; }
            run += s.Length;
        }
        if (hit == null) return (0, p, 0);
        var at = hit.PointAt(bestAlong);
        double e = Math.Max(1e-6, hit.Length * 1e-3), a0 = Math.Max(0, bestAlong - e), a1 = Math.Min(hit.Length, bestAlong + e);
        var q0 = hit.PointAt(a0); var q1 = hit.PointAt(a1);
        return (before + bestAlong, at, Math.Atan2(q1.Y - q0.Y, q1.X - q0.X));
    }
}
