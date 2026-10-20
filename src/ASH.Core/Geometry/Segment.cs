using System;

namespace ASH.Core.Geometry;

public abstract record Segment
{
    public abstract Point2 Start { get; init; }
    public abstract Point2 End { get; }
    public abstract double Length { get; }
    public abstract Point2 PointAt(double dist);
    public abstract Segment Reversed();
    /// <summary>Distance from Start to the point of this segment nearest p (clamped to [0, Length]).</summary>
    public abstract double DistanceAlong(Point2 p);
}

public sealed record LineSeg(Point2 Start, Point2 End) : Segment
{
    public override Point2 Start { get; init; } = Start;
    public override Point2 End { get; } = End;
    public override double Length => Start.DistanceTo(End);

    public override Point2 PointAt(double dist)
    {
        double len = Length;
        if (len <= 0) return Start;
        double t = Math.Max(0, Math.Min(1, dist / len));
        return new Point2(Start.X + (End.X - Start.X) * t, Start.Y + (End.Y - Start.Y) * t);
    }

    public override Segment Reversed() => new LineSeg(End, Start);

    public override double DistanceAlong(Point2 p)
    {
        double len = Length;
        if (len <= 0) return 0;
        double d = ((p.X - Start.X) * (End.X - Start.X) + (p.Y - Start.Y) * (End.Y - Start.Y)) / len;
        return Math.Max(0, Math.Min(len, d));
    }
}

/// <summary>Arc from Start around Center; SweepDeg positive = counter-clockwise.</summary>
public sealed record ArcSeg(Point2 Center, Point2 Start, double SweepDeg) : Segment
{
    public override Point2 Start { get; init; } = Start;
    public double Radius => Center.DistanceTo(Start);
    public override double Length => Radius * Math.Abs(SweepDeg) * Math.PI / 180.0;
    public override Point2 End => Rotated(SweepDeg);

    private Point2 Rotated(double deg)
    {
        double r = deg * Math.PI / 180.0, c = Math.Cos(r), s = Math.Sin(r);
        double dx = Start.X - Center.X, dy = Start.Y - Center.Y;
        return new Point2(Center.X + dx * c - dy * s, Center.Y + dx * s + dy * c);
    }

    public override Point2 PointAt(double dist)
    {
        double len = Length;
        if (len <= 0) return Start;
        double t = Math.Max(0, Math.Min(1, dist / len));
        return Rotated(SweepDeg * t);
    }

    public override Segment Reversed() => new ArcSeg(Center, End, -SweepDeg);

    /// <summary>Angle (deg) from Start to p about Center, normalised into the sweep direction: [0,360) for CCW, (-360,0] for CW.</summary>
    public double AngleFromStart(Point2 p)
    {
        double a = (Math.Atan2(p.Y - Center.Y, p.X - Center.X) - Math.Atan2(Start.Y - Center.Y, Start.X - Center.X)) * 180.0 / Math.PI;
        a %= 360.0;
        if (SweepDeg >= 0) { if (a < 0) a += 360.0; }
        else if (a > 0) a -= 360.0;
        return a;
    }

    public override double DistanceAlong(Point2 p)
    {
        double sw = Math.Abs(SweepDeg), m = Math.Abs(AngleFromStart(p));
        if (m > sw) m = (m - sw) < (360.0 - m) ? sw : 0; // outside the sweep: snap to the nearer end
        return Radius * m * Math.PI / 180.0;
    }
}
