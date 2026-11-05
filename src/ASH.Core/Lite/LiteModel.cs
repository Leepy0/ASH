using System;
using System.Collections.Generic;
using ASH.Core.Cel;

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

/// <summary>A block transform in 3D, row-major as Matrix3d.ToArray(): x' = M00 x + M01 y + M02 z + M03, y' = M10 x + ... + M13, z' = M20 x + ... + M23.</summary>
public readonly record struct Affine3(double M00, double M01, double M02, double M03, double M10, double M11, double M12, double M13, double M20, double M21, double M22, double M23)
{
    public static readonly Affine3 Identity = new(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0);
    /// <summary>From Matrix3d.ToArray(): the bottom row of its 16 values is always 0 0 0 1.</summary>
    public static Affine3 From(double[] a) => new(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7], a[8], a[9], a[10], a[11]);
    public (double X, double Y, double Z) Ap(double x, double y, double z) =>
        (M00 * x + M01 * y + M02 * z + M03, M10 * x + M11 * y + M12 * z + M13, M20 * x + M21 * y + M22 * z + M23);
    /// <summary>This first, then o.</summary>
    public Affine3 Then(Affine3 o) => new(
        o.M00 * M00 + o.M01 * M10 + o.M02 * M20, o.M00 * M01 + o.M01 * M11 + o.M02 * M21, o.M00 * M02 + o.M01 * M12 + o.M02 * M22, o.M00 * M03 + o.M01 * M13 + o.M02 * M23 + o.M03,
        o.M10 * M00 + o.M11 * M10 + o.M12 * M20, o.M10 * M01 + o.M11 * M11 + o.M12 * M21, o.M10 * M02 + o.M11 * M12 + o.M12 * M22, o.M10 * M03 + o.M11 * M13 + o.M12 * M23 + o.M13,
        o.M20 * M00 + o.M21 * M10 + o.M22 * M20, o.M20 * M01 + o.M21 * M11 + o.M22 * M21, o.M20 * M02 + o.M21 * M12 + o.M22 * M22, o.M20 * M03 + o.M21 * M13 + o.M22 * M23 + o.M23);
    /// <summary>The plan rows without their z terms; the whole plan transform when PlanHasZ is false.</summary>
    public Affine2 Plan => new(M00, M01, M10, M11, M03, M13);
    /// <summary>A point's plan position depends on its local z: the placement tilts the block.</summary>
    public bool PlanHasZ => Math.Abs(M02) > 1e-9 || Math.Abs(M12) > 1e-9;
    /// <summary>The 3x3 part alone (translation zero).</summary>
    public Affine3 Linear => new(M00, M01, M02, 0, M10, M11, M12, 0, M20, M21, M22, 0);
}

/// <summary>3D geometry as read from a block definition, in the block's local coordinates (WCS in model space), before any insert transform.
/// Flatten turns it into LiteEntity for one placement. Normals are unit vectors (Entity.Normal). Thickness is ignored by design.</summary>
public abstract record Geo3(string Layer);

/// <summary>Line.StartPoint and Line.EndPoint.</summary>
public sealed record Line3(string Layer, Vec3 A, Vec3 B) : Geo3(Layer);

/// <summary>Center is the local 3D point Circle.Center reports, not OCS coordinates.</summary>
public sealed record Circle3(string Layer, Vec3 Center, Vec3 Normal, double R) : Geo3(Layer);

/// <summary>Center as in Circle3. StartRad and EndRad are Arc.StartAngle and Arc.EndAngle unchanged: radians from the OCS x axis (DXF arbitrary axis algorithm on Normal),
/// counter-clockwise about Normal, so a -Z normal needs no swap or sign change. The arc runs from StartRad up to EndRad; EndRad &lt;= StartRad wraps past 2 pi.</summary>
public sealed record Arc3(string Layer, Vec3 Center, Vec3 Normal, double R, double StartRad, double EndRad) : Geo3(Layer);

/// <summary>Polyline or SimplePoly Polyline2d. Vertices hold X and Y in the OCS of Normal (Polyline.GetPoint2dAt, Vertex2d.Position; not the WCS of GetPoint3dAt) and the Bulge
/// as stored, counter-clockwise about Normal (GetBulgeAt, Vertex2d.Bulge; no flip for a -Z normal). Elevation is the OCS z of the plane (Polyline.Elevation).</summary>
public sealed record Poly3(string Layer, IReadOnlyList<LiteVertex> Vertices, bool Closed, Vec3 Normal, double Elevation) : Geo3(Layer);

/// <summary>Local 3D points joined by straight spans: Polyline3d vertices, or samples of a curve (spline, ellipse, fit polyline).</summary>
public sealed record Points3(string Layer, IReadOnlyList<Vec3> Points, bool Closed) : Geo3(Layer);
