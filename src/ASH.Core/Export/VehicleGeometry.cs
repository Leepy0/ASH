using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ASH.Core.Export;

/// <summary>
/// The AutoMod picture of a vehicle, built from its real Length and Width instead of stretching a fixed picture: the demo vehicle's chassis, top plate and lift
/// plate are replaced by boxes of the asked size, so wheels, rollers and the lift plate keep their own absolute size whatever the vehicle size is. The bumper
/// zones (Bump1, Bump2) and the lift linkage are taken over from the template untouched, because the traffic and load handling hang on them.
/// The picture's local frame is the demo's: x across, y along the travel direction (front = +y; the template's AGVset assembly turns it to travel along +x).
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
    const int Red = 1, Blue = 4, LtGray = 8, DkGray = 9;

    const double MinBody = 0.8; // feet: below this the chassis insets do not leave a box

    static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    static IEnumerable<string> Prim(string name, int color, double x, double y, double z, double bx, double by, double tx, double ty, double h, string flag = "21")
    {
        yield return "310 " + flag;
        yield return $"{color} {color} 0 1 0 {name}";
        yield return $"{F(x)}  {F(y)}  {F(z)}";
        yield return "0" + (char)9 + "0.00" + (char)9 + "0.00" + (char)9 + "0.00";
        yield return (char)9 + "1.00" + (char)9 + "1.00" + (char)9 + "1.00";
        yield return $"{F(bx)} {F(by)} {F(tx)} {F(ty)} {F(h)} 0 0";
    }

    /// <summary>The vehicle segment text (from the line after `template Feet` to before `end`) for a kind, or the template's own lines when the size or unit is unusable.</summary>
    public static IReadOnlyList<string> Build(IReadOnlyList<string> template, string kind, double length, double width, string units)
    {
        if (string.IsNullOrWhiteSpace(kind)) return template; // no kind given: the demo picture as it is
        var k = ASH.Core.Input.VehicleKinds.Find(kind);
        var fpu = FeetPerUnit(units);
        if (k == null || fpu == null || !(length > 0) || !(width > 0) || double.IsInfinity(length) || double.IsInfinity(width)) return template;

        double L = Math.Max(MinBody, length * fpu.Value), W = Math.Max(MinBody, width * fpu.Value); // length along travel (y), width across (x)
        int Index(string name) { var i = template.ToList().FindIndex(x => x.EndsWith(" " + name, StringComparison.Ordinal)); if (i < 1) throw new InvalidOperationException("vehicle template has no " + name); return i - 1; }
        int iGeom = Index("Geom1"), iBase1 = Index("Base1"), iBump1 = Index("Bump1"), iBase3 = Index("Base3_Blue"), iLift = Index("Lift_Joint"), iTop = Index("Top"), iLink = Index("Link1");
        var res = new List<string>();
        res.AddRange(template.Take(iGeom + 2)); // up to and including Geom1's name line, then its child count follows
        var parts = new List<IEnumerable<string>>();

        // chassis: demo Base2 5.5 x 4 (2 high) with Base1 inset by 0.5 ft below a 0.25 ft smaller top; the insets stay absolute
        double inset = 0.5, taper = 0.25;
        double b1x = Math.Max(0.3, W - 2 * inset), b1y = Math.Max(0.3, L - 2 * inset);
        parts.Add(Prim("Base1", DkGray, 0, 0, -0.5, b1x, b1y, Math.Max(0.2, b1x - 2 * taper), Math.Max(0.2, b1y - 2 * taper), 0.5));
        parts.Add(Prim("Base2", DkGray, 0, 0, -2.5, W, L, W, L, 2));
        switch (k.Name)
        {
            case "Standard":
                parts.Add(Prim("Bumper", LtGray, 0, L / 2, -1.5, W - 0.4, 0.2, W - 0.4, 0.2, 0.6));
                break;
            case "Conveyor":
                {
                    double dx = Math.Max(0.3, W - 0.6), dy = Math.Max(0.3, L - 0.6);
                    parts.Add(Prim("CV_Deck", LtGray, 0, 0, 0, dx, dy, dx, dy, 0.15));
                    double pitch = 0.35, rx = Math.Max(0.2, dx - 0.3);
                    int n = (int)Math.Min(40, Math.Floor((dy - 0.3) / pitch) + 1);
                    for (int i = 0; i < n; i++)
                        parts.Add(Prim("CV_Roller" + (i + 1), DkGray, 0, -((n - 1) * pitch) / 2 + i * pitch, 0.15, rx, 0.12, rx, 0.12, 0.1));
                    break;
                }
            case "Magnet":
                {
                    double mw = Math.Max(0.4, Math.Min(W - 1.0, 2.0));
                    parts.Add(Prim("Magnet", Red, 0, -L / 2 - 0.2, -2.0, mw, 0.4, mw, 0.4, 0.7));
                    break;
                }
        }
        int count = parts.Count + 2 /* Bump1, Bump2 */ + 1 /* Base3_Blue */ + 1 /* Lift_Joint */;
        res.Add(count.ToString(CultureInfo.InvariantCulture));
        foreach (var p in parts) res.AddRange(p);
        res.AddRange(template.Skip(iBump1).Take(iBase3 - iBump1));                                  // Bump1, Bump2 as in the template
        res.AddRange(Prim("Base3_Blue", Blue, 0, 0, -0.87, W, L, W, L, 0.1, flag: "5"));            // the coloured plate: the vehicle colour
        res.AddRange(template.Skip(iLift).Take(iTop - iLift));                                       // lift joint header
        // the plate on the lift joint: only the Ant lifts; the others keep a token plate (the joint stays because the load link hangs on it)
        double lw = k.Name == "Ant" ? Math.Max(0.5, Math.Min(1.64, Math.Min(W, L) - 0.6)) : 0.05;
        res.Add("310 17");
        res.Add(template[iTop + 1]);
        res.Add($"{F(lw)} {F(lw)} {F(lw)} {F(lw)} {(k.Name == "Ant" ? "0.333" : "0.05")} 0 0");
        res.AddRange(template.Skip(iLink).Take(template.Count - iLink));
        return res;
    }
}
