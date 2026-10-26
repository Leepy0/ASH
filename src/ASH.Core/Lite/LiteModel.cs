using System;
using System.Collections.Generic;

namespace ASH.Core.Lite;

/// <summary>Polyline vertex; Bulge = tan(sweep/4) of the arc to the next vertex (positive = counter-clockwise), as in DXF.</summary>
public readonly record struct LiteVertex(double X, double Y, double Bulge = 0);

/// <summary>Neutral 2D geometry of the lite drawing (AGVLITE): drawing units, angles in degrees, arcs counter-clockwise.
/// Kind is the R12 entity the writer emits.</summary>
public abstract record LiteEntity(string Layer)
{
    public abstract string Kind { get; }
    internal abstract void Extend(ref LiteBox b);
}

public sealed record LiteLine(string Layer, double X1, double Y1, double X2, double Y2) : LiteEntity(Layer)
{
    public override string Kind => "LINE";
    internal override void Extend(ref LiteBox b) { b.Add(X1, Y1); b.Add(X2, Y2); }
}

public sealed record LitePolyline(string Layer, IReadOnlyList<LiteVertex> Vertices, bool Closed) : LiteEntity(Layer)
{
    public override string Kind => "POLYLINE";
    internal override void Extend(ref LiteBox b)
    {
        int n = Vertices.Count;
        for (int i = 0; i < n; i++)
        {
            var v = Vertices[i];
            b.Add(v.X, v.Y);
            if (v.Bulge != 0 && (Closed || i + 1 < n) && CurveApprox.BulgeArc(v, Vertices[(i + 1) % n]) is var (cx, cy, r, a0, sw) && r > 0)
                LiteBox.AddArc(ref b, cx, cy, r, a0, sw);
        }
    }
}

public sealed record LiteCircle(string Layer, double Cx, double Cy, double R) : LiteEntity(Layer)
{
    public override string Kind => "CIRCLE";
    internal override void Extend(ref LiteBox b) { b.Add(Cx - R, Cy - R); b.Add(Cx + R, Cy + R); }
}

/// <summary>Counter-clockwise from StartDeg to EndDeg (both 0..360).</summary>
public sealed record LiteArc(string Layer, double Cx, double Cy, double R, double StartDeg, double EndDeg) : LiteEntity(Layer)
{
    public override string Kind => "ARC";
    internal override void Extend(ref LiteBox b)
    {
        double sw = EndDeg - StartDeg;
        if (sw <= 0) sw += 360;
        LiteBox.AddArc(ref b, Cx, Cy, R, StartDeg * Math.PI / 180, sw * Math.PI / 180);
    }
}

public struct LiteBox
{
    public double MinX, MinY, MaxX, MaxY;
    public bool IsEmpty => !(MaxX >= MinX);
    public double Diagonal => IsEmpty ? 0 : Math.Sqrt((MaxX - MinX) * (MaxX - MinX) + (MaxY - MinY) * (MaxY - MinY));
    public static LiteBox Empty => new LiteBox { MinX = double.PositiveInfinity, MinY = double.PositiveInfinity, MaxX = double.NegativeInfinity, MaxY = double.NegativeInfinity };

    public void Add(double x, double y)
    {
        if (x < MinX) MinX = x;
        if (x > MaxX) MaxX = x;
        if (y < MinY) MinY = y;
        if (y > MaxY) MaxY = y;
    }

    public static LiteBox Of(IEnumerable<LiteEntity> ents)
    {
        var b = Empty;
        foreach (var e in ents) e.Extend(ref b);
        return b;
    }

    /// <summary>AGVLITE's size of one entity: the diagonal of its box, which is a line's length.</summary>
    public static double Size(LiteEntity e)
    {
        var b = Empty;
        e.Extend(ref b);
        return b.Diagonal;
    }

    /// <summary>Arc end points plus every axis crossing (0, 90, 180, 270 degrees) inside the sweep (radians, may be negative).</summary>
    internal static void AddArc(ref LiteBox b, double cx, double cy, double r, double startRad, double sweepRad)
    {
        double s = sweepRad >= 0 ? startRad : startRad + sweepRad, sw = Math.Abs(sweepRad);
        b.Add(cx + r * Math.Cos(s), cy + r * Math.Sin(s));
        b.Add(cx + r * Math.Cos(s + sw), cy + r * Math.Sin(s + sw));
        double first = Math.Ceiling(s / (Math.PI / 2)) * (Math.PI / 2);
        for (double a = first; a < s + sw && a < first + 2 * Math.PI + 1e-9; a += Math.PI / 2) b.Add(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
    }
}

/// <summary>x' = A x + B y + E, y' = C x + D y + F (a block insert's plan transform).</summary>
public readonly record struct Affine2(double A, double B, double C, double D, double E, double F)
{
    public static readonly Affine2 Identity = new(1, 0, 0, 1, 0, 0);
    public (double X, double Y) Ap(double x, double y) => (A * x + B * y + E, C * x + D * y + F);
    /// <summary>This first, then o.</summary>
    public Affine2 Then(Affine2 o) => new(o.A * A + o.B * C, o.A * B + o.B * D, o.C * A + o.D * C, o.C * B + o.D * D, o.A * E + o.B * F + o.E, o.C * E + o.D * F + o.F);
    public bool IsIdentity => this == Identity;
    public double Det => A * D - B * C;
    /// <summary>Largest stretch factor (spectral norm of the 2x2 part): a chord error e before the transform is at most e x Norm after it.</summary>
    public double Norm
    {
        get
        {
            double t = A * A + B * B + C * C + D * D, det = Det;
            return Math.Sqrt((t + Math.Sqrt(Math.Max(0, t * t - 4 * det * det))) / 2);
        }
    }
    /// <summary>Rotation + uniform scale (+ mirror): circles stay circles. Scale and the rotation angle of the x axis (radians) come out.</summary>
    public bool IsSimilarity(out double scale, out double angle, out bool mirrored)
    {
        scale = Math.Sqrt(A * A + C * C);
        angle = Math.Atan2(C, A);
        mirrored = Det < 0;
        double tol = 1e-9 * Math.Max(1, scale);
        return scale > 0 && (mirrored ? Math.Abs(A + D) <= tol && Math.Abs(B - C) <= tol : Math.Abs(A - D) <= tol && Math.Abs(B + C) <= tol);
    }
}
