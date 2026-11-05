using System;
using System.Linq;
using ASH.Core.Cel;

namespace ASH.Core.Lite;

/// <summary>Step B of AGVLITE: 3D block geometry through a 3D matrix to the plan (Z dropped). A planar curve reaches the plan through the 2D affine map of its own plane,
/// so CurveApprox.Transform decides what it becomes: exact while that map is a similarity (mirror included), chords of the projected ellipse under a tilt or an uneven stretch.</summary>
public static class Flatten
{
    /// <summary>|det| / norm^2 of a plane's plan map below this: the plane is seen edge-on.</summary>
    const double EdgeOn = 1e-9;

    /// <summary>g through m with error tol in the result. Degenerate results (a vertical line's point) come out as they are: Clean.Degenerate removes them.</summary>
    public static LiteEntity To2D(Geo3 g, Affine3 m, double tol, string layer)
    {
        switch (g)
        {
            case Line3 l:
            {
                var a = m.Ap(l.A.X, l.A.Y, l.A.Z);
                var b = m.Ap(l.B.X, l.B.Y, l.B.Z);
                return new LiteLine(layer, a.X, a.Y, b.X, b.Y);
            }
            case Points3 p:
                return new LitePolyline(layer, p.Points.Select(v => { var q = m.Ap(v.X, v.Y, v.Z); return new LiteVertex(q.X, q.Y); }).ToArray(), p.Closed);
            case Circle3 c:
                return Project(new LiteCircle(layer, 0, 0, c.R), PlanMap(m, c.Normal, c.Center), tol, layer);
            case Arc3 a:
                return Project(new LiteArc(layer, 0, 0, a.R, Deg(a.StartRad), Deg(a.EndRad)), PlanMap(m, a.Normal, a.Center), tol, layer);
            case Poly3 p:
            {
                var (n, e) = (p.Normal, p.Elevation);
                return Project(new LitePolyline(layer, p.Vertices, p.Closed), PlanMap(m, n, new Vec3(n.X * e, n.Y * e, n.Z * e)), tol, layer);
            }
            default: throw new ArgumentException("unknown geometry " + g.GetType().Name);
        }
    }

    /// <summary>e is in plane coordinates. Edge-on it collapses onto a line: a circle gives its exact diameter, anything else the span of its chord points within tol.
    /// The span, not the two end points: a bow whose ends share a plan point would vanish.</summary>
    static LiteEntity Project(LiteEntity e, Affine2 xf, double tol, string layer)
    {
        double uu = xf.A * xf.A + xf.C * xf.C, vv = xf.B * xf.B + xf.D * xf.D, norm = xf.Norm;
        if (Math.Abs(xf.Det) > EdgeOn * norm * norm) return CurveApprox.Transform(e, xf, tol, layer);
        var (dx, dy) = uu >= vv ? (xf.A, xf.C) : (xf.B, xf.D); // the line's direction, any length
        if (e is LiteCircle c)
        {
            double k = c.R * Math.Sqrt((uu + vv) / Math.Max(Math.Max(uu, vv), 1e-300)); // rank one: the reach is R sqrt(uu + vv)
            return new LiteLine(layer, xf.E - k * dx, xf.F - k * dy, xf.E + k * dx, xf.F + k * dy);
        }
        var along = ((LitePolyline)CurveApprox.Transform(e, xf, tol, layer)).Vertices.OrderBy(p => p.X * dx + p.Y * dy).ToList();
        var (a, b) = (along.FirstOrDefault(), along.LastOrDefault());
        return new LiteLine(layer, a.X, a.Y, b.X, b.Y);
    }

    /// <summary>Plan image of the plane through local point o with unit normal n: (x, y) along the plane's OCS axes, measured from o, to plan coordinates.</summary>
    static Affine2 PlanMap(Affine3 m, Vec3 n, Vec3 o)
    {
        // DXF arbitrary axis algorithm
        bool nearZ = Math.Abs(n.X) < 1.0 / 64 && Math.Abs(n.Y) < 1.0 / 64;
        double ax = nearZ ? n.Z : -n.Y, ay = nearZ ? 0 : n.X, az = nearZ ? -n.X : 0, len = Math.Sqrt(ax * ax + ay * ay + az * az);
        ax /= len; ay /= len; az /= len;
        var l = m.Linear;
        var u = l.Ap(ax, ay, az);
        var v = l.Ap(n.Y * az - n.Z * ay, n.Z * ax - n.X * az, n.X * ay - n.Y * ax); // OCS y = n x OCS x
        var c = m.Ap(o.X, o.Y, o.Z);
        return new Affine2(u.X, v.X, u.Y, v.Y, c.X, c.Y);
    }

    static double Deg(double rad) => CurveApprox.Norm360(rad * 180 / Math.PI);
}
