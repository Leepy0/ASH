using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ASH.Core.Export;

/// <summary>
/// The AutoMod picture of a vehicle, built from its real Length and Width instead of stretching a fixed picture: the demo vehicle's chassis, top plate and lift
/// plate are replaced by boxes of the asked size, so wheels, rollers and the lamps keep their own absolute size whatever the vehicle size is. The bumper
/// zones (Bump1, Bump2) and the lift joint are taken over from the template, because the traffic and load handling hang on them.
/// The vehicle drives both ways, so the picture is symmetric front to back (except the Magnet's rear-only magnet module), with a three-colour status lamp
/// (red, yellow, green) on each end face.
/// Heights (metres): Standard, Conveyor and Magnet 0.6; Ant 0.15, with its wheels under the lower plate (ground clearance 0.06) and a top plate on which the
/// trolley stands. The load attach point is the load surface (the top) for Standard, Conveyor and Ant. The Magnet tows: its trolley stands on the floor, behind
/// the vehicle with its casters on the same floor line as the vehicle's wheels, coupled to the rear magnet (the distance assumes a 1 m long trolley).
/// The picture's local frame is the demo's: x across, y along the travel direction (the template's AGVset assembly turns it to travel along +x).
/// All numbers in the template are feet; <see cref="FeetPerUnit"/> converts from the model units.
/// </summary>
public static class VehicleGeometry
{
    /// <summary>Feet in one model unit, or null for a unit the picture cannot be converted from (the demo picture is then used unchanged).</summary>
    public static double? FeetPerUnit(string units)
    {
        switch ((units ?? "").Trim().ToLowerInvariant())
        {
            case "feet": return 1;
            case "meters": return 3.280839895;
            case "inches": return 1.0 / 12;
            case "centimeters": return 0.03280839895;
            case "millimeters": return 0.003280839895;
            default: return null;
        }
    }

    // AutoMod standard colors (AutoMod's colorwin.h)
    const int Black = 0, Red = 1, Green = 2, Yellow = 3, Blue = 4, LtGray = 8, DkGray = 9;

    const double FtPerM = 3.280839895;
    const double FloorRel = -2.5;      // the template's AGVset sits 2.5 ft above the floor: the floor is z = -2.5 in the picture's frame
    const double MinBodyM = 0.25;      // below this the chassis insets do not leave a box

    /// <summary>Height of the load surface (metres) a kind has; also the vehicle height for Standard and Conveyor.</summary>
    public static double TopHeightM(string kind) => kind == "Ant" ? 0.15 : 0.6;

    static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    static IEnumerable<string> Prim(string name, int color, double x, double y, double z, double bx, double by, double tx, double ty, double h, string flag = "21")
    {
        yield return "310 " + flag;
        yield return color + " " + color + " 0 1 0 " + name;
        yield return F(x) + "  " + F(y) + "  " + F(z);
        yield return "0" + (char)9 + "0.00" + (char)9 + "0.00" + (char)9 + "0.00";
        yield return (char)9 + "1.00" + (char)9 + "1.00" + (char)9 + "1.00";
        yield return F(bx) + " " + F(by) + " " + F(tx) + " " + F(ty) + " " + F(h) + " 0 0";
    }

    /// <summary>The vehicle segment text for a kind (the whole template with its picture parts replaced), or the template itself when the size or unit is unusable.</summary>
    public static IReadOnlyList<string> Build(IReadOnlyList<string> template, string kind, double length, double width, string units)
    {
        if (string.IsNullOrWhiteSpace(kind)) return template; // no kind given: the demo picture as it is
        var k = ASH.Core.Input.VehicleKinds.Find(kind);
        var fpu = FeetPerUnit(units);
        if (k == null || fpu == null || !(length > 0) || !(width > 0) || double.IsInfinity(length) || double.IsInfinity(width)) return template;

        double mPerUnit = fpu.Value / FtPerM;
        double L = Math.Max(MinBodyM, length * mPerUnit), W = Math.Max(MinBodyM, width * mPerUnit); // metres: length along travel (y), width across (x)
        var tl = template.ToList();
        int Index(string name)
        {
            var i = tl.FindIndex(x => x.EndsWith(" " + name, StringComparison.Ordinal));
            if (i < 1) throw new InvalidOperationException("vehicle template has no " + name);
            return i - 1;
        }
        int iGeom = Index("Geom1"), iBump1 = Index("Bump1"), iBase3 = Index("Base3_Blue"), iLift = Index("Lift_Joint"), iTop = Index("Top"), iLink = Index("Link1");

        double H = TopHeightM(k.Name);
        double clr = k.Name == "Ant" ? 0.06 : k.Name == "Magnet" ? 0.04 : 0.04;      // ground clearance (the Ant's wheels hang below its lower plate)
        double Z(double m) => FloorRel + m * FtPerM;                                   // metres above the floor -> picture z in feet
        // one box: metres in, feet out. x, y = centre, z0 = bottom
        IEnumerable<string> B(string name, int color, double x, double y, double z0, double dx, double dy, double h, double tdx = -1, double tdy = -1, string flag = "21") =>
            Prim(name, color, x * FtPerM, y * FtPerM, Z(z0), dx * FtPerM, dy * FtPerM, (tdx < 0 ? dx : tdx) * FtPerM, (tdy < 0 ? dy : tdy) * FtPerM, h * FtPerM, flag);

        var parts = new List<IEnumerable<string>>();
        double top = H;                                                                 // top of the chassis, where the lift joint stands
        if (k.Name == "Standard")
        {
            double deck = 0.08;
            parts.Add(B("Base2", DkGray, 0, 0, clr, W, L, H - deck - clr));
            double ix = Math.Max(0.1, W - 0.2), iy = Math.Max(0.1, L - 0.2);
            parts.Add(B("Base1", DkGray, 0, 0, H - deck, ix, iy, deck, Math.Max(0.05, ix - 0.1), Math.Max(0.05, iy - 0.1)));
            foreach (var s in new[] { 1, -1 })                                            // a bumper on each end: the vehicle drives both ways
                parts.Add(B(s > 0 ? "BumperF" : "BumperR", LtGray, 0, s * L / 2, 0.1, Math.Max(0.1, W - 0.2), 0.03, 0.12));
        }
        else if (k.Name == "Conveyor")
        {
            double deckT = 0.03, rollerH = 0.06, rollerT = 0.04, pitch = 0.1;
            double chassisTop = H - deckT - rollerH;
            parts.Add(B("Base2", DkGray, 0, 0, clr, W, L, chassisTop - clr));
            double dx = Math.Max(0.1, W - 0.1), dy = Math.Max(0.1, L - 0.1);
            parts.Add(B("CV_Deck", LtGray, 0, 0, chassisTop, dx, dy, deckT));
            double rx = Math.Max(0.05, dx - 0.06);
            int n = (int)Math.Min(40, Math.Floor((dy - 0.08) / pitch) + 1);
            for (int i = 0; i < n; i++)
                parts.Add(B("CV_Roller" + (i + 1), DkGray, 0, -((n - 1) * pitch) / 2 + i * pitch, chassisTop + deckT, rx, rollerT, rollerH));
        }
        else if (k.Name == "Ant")
        {
            double plateT = 0.03;
            parts.Add(B("Base2", DkGray, 0, 0, clr, W, L, H - plateT - clr));                                     // lower plate and body
            parts.Add(B("TopPlate", LtGray, 0, 0, H - plateT, Math.Max(0.1, W - 0.06), Math.Max(0.1, L - 0.06), plateT)); // the plate the trolley stands on
        }
        else
        {
            parts.Add(B("Base2", DkGray, 0, 0, clr, W, L, H - clr));
            double mw = Math.Max(0.1, Math.Min(W - 0.2, 0.4));
            parts.Add(B("MagnetR", Red, 0, -(L / 2 + 0.04), 0.03, mw, 0.12, 0.08));       // the magnet module is at the rear only
        }

        // wheels: on the floor line, outside the chassis sides near the ends
        double wh = Math.Min(0.12, k.Name == "Ant" ? clr : H - 0.01), wx = W / 2, wy = Math.Max(0.05, L / 2 - 0.15);
        var wheels = new[] { (1, 1, "WheelFR"), (-1, 1, "WheelFL"), (1, -1, "WheelRR"), (-1, -1, "WheelRL") };
        foreach (var w in wheels)
            parts.Add(B(w.Item3, Black, w.Item1 * (k.Name == "Ant" ? wx - 0.06 : wx - 0.01), w.Item2 * wy, 0, 0.04, wh, wh)); // the Ant's wheels sit inside, under the lower plate

        // three-colour status lamp on each end face: red, yellow, green side by side, mirrored so each face reads the same from outside
        double lampW = 0.05, lampD = 0.02, lampH = 0.04;
        double lz = k.Name == "Ant" ? clr + 0.01 : H / 2;
        var lampColors = new[] { Red, Yellow, Green };
        foreach (var s in new[] { 1, -1 })
            for (int c = 0; c < 3; c++)
                parts.Add(B("Lamp" + (s > 0 ? "F" : "R") + "RYG"[c], lampColors[c], s * (1 - c) * (lampW + 0.005), s * L / 2, lz, lampW, lampD, lampH));

        var res = new List<string>();
        res.AddRange(tl.Take(iGeom + 2));                                              // up to and including Geom1's name line, then its child count
        int count = parts.Count + 2 /* Bump1, Bump2 */ + 1 /* Base3_Blue */ + 1 /* Lift_Joint */;
        res.Add(count.ToString(CultureInfo.InvariantCulture));
        foreach (var p in parts) res.AddRange(p);
        res.AddRange(tl.Skip(iBump1).Take(iBase3 - iBump1));                           // Bump1, Bump2 as in the template
        double bandZ = clr + Math.Max(0.01, (H - clr) * 0.5 - 0.01);
        res.AddRange(B("Base3_Blue", Blue, 0, 0, bandZ, W, L, 0.03, flag: "5"));       // the coloured band round the chassis: the vehicle colour

        // the lift joint stands on the chassis top; only the Ant has a plate worth seeing on it
        int jointStart = res.Count;
        res.AddRange(tl.Skip(iLift).Take(iTop - iLift));
        res[jointStart + 4] = "0  0  " + F(Z(top));                                    // the joint's own position line
        double plate = 0.01, plateH = 0.003;                                            // token plate: the joint is kept for the load link, nothing is lifted
        res.Add("310 17");
        res.Add(tl[iTop + 1]);
        res.Add(F(plate * FtPerM) + " " + F(plate * FtPerM) + " " + F(plate * FtPerM) + " " + F(plate * FtPerM) + " " + F(plateH * FtPerM) + " 0 0");

        // the load attach point: the load surface for Standard, Conveyor and Ant; for the Magnet the floor, behind the rear magnet (trolley length 1 m assumed)
        double attachY = k.Name == "Magnet" ? -(L / 2 + 0.10 + 0.5) * FtPerM : 0;
        double attachZ = k.Name == "Magnet" ? Z(0) : Z(H);
        for (int i = iLink; i < tl.Count; i++)
            res.Add(tl[i].Trim() == "0  0  0.349999994039536" ? "0  " + F(attachY) + "  " + F(attachZ) : tl[i]);
        return res;
    }
}
