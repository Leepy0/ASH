using System;
using System.Collections.Generic;
using System.Globalization;
using ASH.Core.Input;

namespace ASH.Core.Model;

/// <summary>
/// The AutoMod picture of a load (box, empty box, pallet, trolley), built from its own length, width and height in the model units. Each part has an absolute
/// size (board width, wall thickness, caster size), only the outer measures follow the material's size, so a bigger load is a bigger load and not a stretched one.
/// Length runs along y, width along x (the vehicle picture's frame: y is the travel direction, so a load rides lengthwise), the parts stand on z = 0.
/// </summary>
public static class LoadGeometry
{
    // AutoMod standard colors (AutoMod's colorwin.h)
    const int Red = 1, LtGray = 8, DkGray = 9, Brown = 10, Purple = 12, Orange = 13;

    /// <summary>Default size in meters (length, width, height) by form.</summary>
    public static (double L, double W, double H) DefaultSize(string form)
    {
        switch (form)
        {
            case MaterialForms.Pallet: return (1.1, 1.1, 0.15);
            case MaterialForms.Trolley: return (1.0, 0.7, 0.25);
            default: return (0.6, 0.4, 0.3);
        }
    }

    /// <summary>Model units in one meter, or null for a unit that cannot be converted (the plain cube is then kept).</summary>
    public static double? UnitsPerMeter(string units)
    {
        switch ((units ?? "").Trim().ToLowerInvariant())
        {
            case "meters": return 1;
            case "feet": return 3.280839895;
            case "inches": return 39.37007874;
            case "centimeters": return 100;
            case "millimeters": return 1000;
            default: return null;
        }
    }

    static string F(double v) => v.ToString("0.#######", CultureInfo.InvariantCulture);

    static IEnumerable<string> Prim(string name, int color, double x, double y, double z, double bx, double by, double h)
    {
        yield return "310 21";
        yield return $"{color} {color} 0 1 1 {name}"; // display 1 = solid (0 draws the outline only)
        yield return $"{F(x)}  {F(y)}  {F(z)}";
        yield return "0" + (char)9 + "0.00" + (char)9 + "0.00" + (char)9 + "0.00";
        yield return (char)9 + "1.00" + (char)9 + "1.00" + (char)9 + "1.00";
        yield return $"{F(bx)} {F(by)} {F(bx)} {F(by)} {F(h)} 0 0";
    }

    /// <summary>The geometry lines of the LDTYPE (between ` template ...` and `end`), or null when the unit is unknown. <paramref name="empty"/> is an open box,
    /// or a grey pallet or trolley (the full one carries nothing visible either, so the colour tells them apart).
    /// Sizes are in meters; a part is never smaller than the numbers below.</summary>
    public static IReadOnlyList<string>? Build(string form, bool empty, double lengthM, double widthM, double heightM, string units)
    {
        var upm = UnitsPerMeter(units);
        if (upm == null) return null;
        double u = upm.Value;
        double L = Math.Max(0.05, lengthM), W = Math.Max(0.05, widthM), H = Math.Max(0.02, heightM);
        var parts = new List<IEnumerable<string>>();
        // the shapes below are written with length along x; the swap of x and y turns them to run along y
        IEnumerable<string> P(string name, int color, double x, double y, double z, double bx, double by, double h) => Prim(name, color, y * u, x * u, z * u, by * u, bx * u, h * u);
        switch (form)
        {
            case MaterialForms.Pallet:
                {
                    int wood = empty ? LtGray : Orange;
                    double stringer = Math.Min(0.1, W / 4), deck = Math.Min(0.03, H / 3), boardW = Math.Min(0.12, W / 6);
                    for (int i = 0; i < 3; i++)
                        parts.Add(P("Stringer" + (i + 1), wood, 0, -W / 2 + stringer / 2 + i * (W - stringer) / 2, 0, L, stringer, H - deck));
                    int n = (int)Math.Max(3, Math.Min(15, Math.Round(W / (boardW * 1.5))));
                    for (int i = 0; i < n; i++)
                        parts.Add(P("Board" + (i + 1), wood, 0, -W / 2 + boardW / 2 + i * (W - boardW) / (n - 1), H - deck, L, boardW, deck));
                    break;
                }
            case MaterialForms.Trolley:
                {
                    int frame = empty ? LtGray : Purple;
                    double caster = Math.Min(0.1, H / 2), deckT = Math.Min(0.03, H / 4), rail = Math.Min(0.04, W / 8);
                    parts.Add(P("Deck", frame, 0, 0, caster, L, W, deckT));
                    parts.Add(P("RailL", frame, 0, -W / 2 + rail / 2, caster + deckT, L, rail, Math.Max(0.01, H - caster - deckT)));
                    parts.Add(P("RailR", frame, 0, W / 2 - rail / 2, caster + deckT, L, rail, Math.Max(0.01, H - caster - deckT)));
                    double cx = L / 2 - caster / 2 - 0.02, cy = W / 2 - caster / 2 - 0.02;
                    parts.Add(P("Caster1", DkGray, cx, cy, 0, caster, caster, caster));
                    parts.Add(P("Caster2", DkGray, -cx, cy, 0, caster, caster, caster));
                    parts.Add(P("Caster3", DkGray, cx, -cy, 0, caster, caster, caster));
                    parts.Add(P("Caster4", DkGray, -cx, -cy, 0, caster, caster, caster));
                    break;
                }
            default: // Box
                if (!empty)
                {
                    parts.Add(P("Box", Brown, 0, 0, 0, L, W, H));
                    parts.Add(P("Tape", LtGray, 0, 0, H, Math.Min(0.06, L / 5), W, 0.005));
                }
                else
                {
                    double t = Math.Min(0.02, Math.Min(L, W) / 6);
                    parts.Add(P("Floor", Brown, 0, 0, 0, L, W, t));
                    parts.Add(P("WallF", Brown, 0, W / 2 - t / 2, t, L, t, H - t));
                    parts.Add(P("WallB", Brown, 0, -W / 2 + t / 2, t, L, t, H - t));
                    parts.Add(P("WallL", Brown, -L / 2 + t / 2, 0, t, t, Math.Max(0.01, W - 2 * t), H - t));
                    parts.Add(P("WallR", Brown, L / 2 - t / 2, 0, t, t, Math.Max(0.01, W - 2 * t), H - t));
                }
                break;
        }
        var res = new List<string> { "700 17", "2 2 0 1 1 none", parts.Count.ToString(CultureInfo.InvariantCulture) };
        foreach (var p in parts) res.AddRange(p);
        return res;
    }
}
