using System;

namespace ASH.Core.Geometry;

/// <summary>DWG polyline bulge = tan(sweep/4); positive = counter-clockwise, centre left of the chord.</summary>
public static class Bulge
{
    /// <summary>Segment p0→p1 with the given bulge; null for a zero-length chord.</summary>
    public static Segment? ToSegment(Point2 p0, Point2 p1, double bulge)
    {
        double dx = p1.X - p0.X, dy = p1.Y - p0.Y, c = Math.Sqrt(dx * dx + dy * dy);
        if (c <= 1e-9) return null;
        if (Math.Abs(bulge) < 1e-12) return new LineSeg(p0, p1);
        double h = (1 - bulge * bulge) / (4 * bulge); // signed centre offset per unit chord, left of p0→p1
        var center = new Point2((p0.X + p1.X) / 2 - dy * h, (p0.Y + p1.Y) / 2 + dx * h);
        return new ArcSeg(center, p0, 4 * Math.Atan(bulge) * 180 / Math.PI);
    }

    public static double FromSweepDeg(double sweepDeg) => Math.Tan(sweepDeg * Math.PI / 180 / 4);
}
