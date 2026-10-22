using System;

namespace ASH.Core.Film;

/// <summary>View maths from docs/autoview-guide.md sections 4, 5 and 10 (verified there to ~1%, k(theta) = 1.00).</summary>
public static class CameraMath
{
    /// <summary>Focus-to-camera distance: 100 ft (guide 4/10); only perspective distortion depends on it.</summary>
    const double CameraDistanceMetres = 30.48;

    /// <summary>Frames rect <paramref name="r"/> (metres) in a width x height movie (width >= height; F0 is the short-side field, guide 4).
    /// <paramref name="f0Metres"/> is the scene's reference field (guide 10.4), supplied by the caller. Output is in <paramref name="unitsPerMeter"/> (1e6 = um).
    /// <paramref name="tiltDeg"/>: 0 = straight down, 90 would be horizontal. <paramref name="dirDeg"/>: compass direction shown screen-up;
    /// 0 = looking north (+Y up, the plan view, camera south of the focus), 90 = looking east (+X up).
    /// <paramref name="heightMetres"/>: extra height to keep in frame (vehicles/racks); focus is raised to half of it (guide 10.1).</summary>
    public static View Frame(Rect r, double unitsPerMeter, double f0Metres, int width, int height, double tiltDeg, double dirDeg, double margin = 0.08,
        double heightMetres = 0, bool perspective = false)
    {
        if (tiltDeg < 0 || tiltDeg > 70) throw new ArgumentOutOfRangeException(nameof(tiltDeg), "tilt must be 0..70");
        if (width < height) throw new ArgumentOutOfRangeException(nameof(width), "width must be >= height (F0 is the short-side field)");
        if (tiltDeg < 5) tiltDeg = 0; // a near-vertical sight line makes upvector (0,0,1) degenerate: use the plan view
        double th = tiltDeg * Math.PI / 180, ph = dirDeg * Math.PI / 180;
        double sT = Math.Sin(th), cT = Math.Cos(th), sP = Math.Sin(ph), cP = Math.Cos(ph);
        double[] d = { sT * sP, sT * cP, -cT }, right = { cP, -sP, 0 }, up = { cT * sP, cT * cP, sT };
        double cx = r.X + r.W / 2, cy = r.Y + r.H / 2, cz = heightMetres / 2, D = CameraDistanceMetres;

        double ax = 0, ay = 0; // guide 10.2: half-extents of the 8 projected corners
        foreach (var x in new[] { r.X, r.X + r.W })
            foreach (var y in new[] { r.Y, r.Y + r.H })
                foreach (var z in new[] { 0, heightMetres })
                {
                    double vx = x - cx, vy = y - cy, vz = z - cz;
                    double f = perspective ? D / (D + vx * d[0] + vy * d[1] + vz * d[2]) : 1;
                    ax = Math.Max(ax, Math.Abs(f * (vx * right[0] + vy * right[1])));
                    ay = Math.Max(ay, Math.Abs(f * (vx * up[0] + vy * up[1] + vz * up[2])));
                }
        double l = Math.Max(2 * ay, 2 * ax * height / width) * (1 + margin);
        if (!(l > 0)) throw new ArgumentException("rectangle has no extent", nameof(r));

        double u = unitsPerMeter;
        return new View(cx * u, cy * u, cz * u, (cx - D * d[0]) * u, (cy - D * d[1]) * u, (cz - D * d[2]) * u,
            tiltDeg == 0 ? sP : 0, tiltDeg == 0 ? cP : 0, tiltDeg == 0 ? 0 : 1, f0Metres / l);
    }

    /// <summary>Guide 5.1: one movie frame = displayStep x timeScale simulation seconds, so timeScale = speed / (displayStep x fps).</summary>
    public static double TimeScale(double speed, int fps, double displayStep = 0.5) => speed / (displayStep * fps);
}
