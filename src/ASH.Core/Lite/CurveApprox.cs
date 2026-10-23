using System;
using System.Collections.Generic;
using System.Linq;

namespace ASH.Core.Lite;

/// <summary>Curve-to-chord math for AGVLITE. tol is the largest allowed distance between a curve and its chords (drawing units).
/// A circular arc of radius r cut into steps of angle d has chord error r(1 - cos(d/2)); an ellipse C + M cos t + N sin t is the image of the unit circle
/// under [M N], so equal parameter steps keep its error below |[M N]| (1 - cos(d/2)) (a linear map stretches distances by at most its norm).</summary>
public static class CurveApprox
{
    /// <summary>Points per curve never exceed this, whatever the tolerance (a malformed huge radius cannot blow up the file).</summary>
    public const int MaxPoints = 4096;

    /// <summary>Steps for a sweep (radians) on a circle of radius r, at least one per 90 degrees.</summary>
    public static int Steps(double sweepRad, double r, double tol)
    {
        double sw = Math.Abs(sweepRad);
        if (!(sw > 0) || !(r > 0)) return 1;
        int quarter = (int)Math.Ceiling(sw / (Math.PI / 2) - 1e-9);
        double n = tol > 0 && tol < r ? Math.Ceiling(sw / (2 * Math.Acos(1 - tol / r)) - 1e-9) : 1;
        return (int)Math.Min(MaxPoints - 1, Math.Max(Math.Max(1, quarter), n));
    }

    /// <summary>Points on an arc from startRad over sweepRad (negative = clockwise), both ends included.</summary>
    public static List<(double X, double Y)> Arc(double cx, double cy, double r, double startRad, double sweepRad, double tol)
    {
        int k = Steps(sweepRad, r, tol);
        var o = new List<(double, double)>(k + 1);
        for (int j = 0; j <= k; j++) { double a = startRad + sweepRad * j / k; o.Add((cx + r * Math.Cos(a), cy + r * Math.Sin(a))); }
        return o;
    }

    /// <summary>Points of C + M cos t + N sin t for t from t0 to t1 (an ellipse, or the plan projection of a tilted one).</summary>
    public static List<(double X, double Y)> Ellipse(double cx, double cy, double mx, double my, double nx, double ny, double t0, double t1, double tol)
    {
        double norm = new Affine2(mx, nx, my, ny, 0, 0).Norm;
        int k = Steps(t1 - t0, norm, tol);
        var o = new List<(double, double)>(k + 1);
        for (int j = 0; j <= k; j++)
        {
            double t = t0 + (t1 - t0) * j / k, c = Math.Cos(t), s = Math.Sin(t);
            o.Add((cx + mx * c + nx * s, cy + my * c + ny * s));
        }
        return o;
    }

    /// <summary>Samples any parametric curve (a spline evaluated by ZWCAD): `initial` equal steps, then each step is halved while its mid or quarter points lie farther than tol
    /// from the chord (16 halvings at most, MaxPoints in all). Start the count high enough to catch S bends inside one step (the plugin uses 4 per control point).</summary>
    public static List<(double X, double Y)> Adaptive(Func<double, (double X, double Y)> f, double t0, double t1, double tol, int initial)
    {
        initial = Math.Max(1, Math.Min(MaxPoints - 1, initial));
        var o = new List<(double X, double Y)> { f(t0) };
        var stack = new Stack<(double A, double B, (double X, double Y) Pb, int Depth)>();
        for (int i = initial; i >= 1; i--) stack.Push((t0 + (t1 - t0) * (i - 1) / initial, t0 + (t1 - t0) * i / initial, f(t0 + (t1 - t0) * i / initial), 0));
        while (stack.Count > 0)
        {
            var (a, b, pb, depth) = stack.Pop();
            var pa = o[o.Count - 1];
            double m = (a + b) / 2;
            var pm = f(m);
            // quarter points too: an S bend centred in the step puts its mid point right on the chord
            if (depth < 16 && o.Count + stack.Count < MaxPoints - 2 &&
                (DistToSegment(pm, pa, pb) > tol || DistToSegment(f((a + m) / 2), pa, pb) > tol || DistToSegment(f((m + b) / 2), pa, pb) > tol))
            {
                stack.Push((m, b, pb, depth + 1));
                stack.Push((a, m, pm, depth + 1));
            }
            else o.Add(pb);
        }
        return o;
    }

    /// <summary>Centre, radius, start angle and signed sweep (radians) of the bulge arc from a to b; R = 0 for a straight or degenerate span.</summary>
    public static (double Cx, double Cy, double R, double StartRad, double SweepRad) BulgeArc(LiteVertex a, LiteVertex b)
    {
        double chord = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        if (a.Bulge == 0 || chord == 0 || double.IsNaN(a.Bulge) || double.IsInfinity(a.Bulge)) return (0, 0, 0, 0, 0);
        double sweep = 4 * Math.Atan(a.Bulge);
        double r = chord / (2 * Math.Sin(Math.Abs(sweep) / 2)), mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
        double d = r * Math.Cos(sweep / 2) * Math.Sign(sweep); // centre sits left of the chord for counter-clockwise arcs
        double cx = mx - (b.Y - a.Y) / chord * d, cy = my + (b.X - a.X) / chord * d;
        return (cx, cy, r, Math.Atan2(a.Y - cy, a.X - cx), sweep);
    }

    /// <summary>Polyline vertices with every bulge replaced by chords (all bulges 0 in the result).</summary>
    public static List<LiteVertex> Flatten(IReadOnlyList<LiteVertex> v, bool closed, double tol)
    {
        var o = new List<LiteVertex>(v.Count);
        int n = v.Count;
        for (int i = 0; i < n; i++)
        {
            o.Add(new LiteVertex(v[i].X, v[i].Y));
            if (!closed && i == n - 1) break;
            var (cx, cy, r, a0, sw) = BulgeArc(v[i], v[(i + 1) % n]);
            if (r > 0) foreach (var p in Arc(cx, cy, r, a0, sw, tol).Skip(1).Take(Steps(sw, r, tol) - 1)) o.Add(new LiteVertex(p.X, p.Y));
        }
        return o;
    }

    /// <summary>The entity under a block transform. Rotation, uniform scale and mirroring keep circles, arcs and bulges exact (a mirror flips arc direction);
    /// any other stretch turns them into chords with error tol after the transform.</summary>
    public static LiteEntity Transform(LiteEntity e, Affine2 xf, double tol, string layer)
    {
        if (xf.IsIdentity) return e.Layer == layer ? e : e with { Layer = layer };
        bool sim = xf.IsSimilarity(out var s, out var rot, out var mirrored);
        double tolLocal = tol / Math.Max(xf.Norm, 1e-12);
        LiteVertex V(double x, double y, double bulge = 0) { var (px, py) = xf.Ap(x, y); return new LiteVertex(px, py, bulge); }
        LitePolyline Poly(IEnumerable<(double X, double Y)> pts, bool closed) => new(layer, pts.Select(p => V(p.X, p.Y)).ToArray(), closed);
        switch (e)
        {
            case LiteLine l:
            {
                var (x1, y1) = xf.Ap(l.X1, l.Y1); var (x2, y2) = xf.Ap(l.X2, l.Y2);
                return new LiteLine(layer, x1, y1, x2, y2);
            }
            case LiteCircle c:
            {
                if (sim) { var (x, y) = xf.Ap(c.Cx, c.Cy); return new LiteCircle(layer, x, y, c.R * s); }
                var pts = Arc(c.Cx, c.Cy, c.R, 0, 2 * Math.PI, tolLocal);
                pts.RemoveAt(pts.Count - 1); // closed polyline: the last point repeats the first
                return Poly(pts, true);
            }
            case LiteArc a:
            {
                double sw = a.EndDeg - a.StartDeg;
                if (sw <= 0) sw += 360;
                if (sim)
                {
                    var (x, y) = xf.Ap(a.Cx, a.Cy);
                    double deg = rot * 180 / Math.PI;
                    return mirrored ? new LiteArc(layer, x, y, a.R * s, Norm360(deg - a.EndDeg), Norm360(deg - a.StartDeg))
                                    : new LiteArc(layer, x, y, a.R * s, Norm360(a.StartDeg + deg), Norm360(a.EndDeg + deg));
                }
                return Poly(Arc(a.Cx, a.Cy, a.R, a.StartDeg * Math.PI / 180, sw * Math.PI / 180, tolLocal), false);
            }
            case LitePolyline p:
            {
                var src = sim ? p.Vertices : Flatten(p.Vertices, p.Closed, tolLocal);
                return new LitePolyline(layer, src.Select(v => V(v.X, v.Y, sim && mirrored ? -v.Bulge : v.Bulge)).ToArray(), p.Closed);
            }
            default: throw new ArgumentException("unknown lite entity " + e.GetType().Name);
        }
    }

    public static double Norm360(double deg)
    {
        deg %= 360;
        return deg < 0 ? deg + 360 : deg;
    }

    static double DistToSegment((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
        double t = len2 > 0 ? Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2)) : 0;
        double ex = a.X + t * dx - p.X, ey = a.Y + t * dy - p.Y;
        return Math.Sqrt(ex * ex + ey * ey);
    }
}
