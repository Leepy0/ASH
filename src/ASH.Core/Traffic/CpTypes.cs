using System;
using System.Globalization;

namespace ASH.Core.Traffic;

/// <summary>CP types with their default capacity and display colour, and the CAP text rules ("INF" = unlimited).</summary>
public static class CpTypes
{
    public static readonly string[] All = { "traffic", "load", "unload", "rtnload", "rtnunload", "standby", "buffer", "finish" };

    /// <summary>AutoMod's "infinite" capacity (2147483647 in .asy files).</summary>
    public const int Infinity = int.MaxValue;

    /// <summary>A traffic CP claims one vehicle at a time; every other CP is a place vehicles queue or stop at, so it is unlimited unless told otherwise.</summary>
    public static int DefaultCap(string? type) => string.Equals(type, "traffic", StringComparison.OrdinalIgnoreCase) ? 1 : Infinity;

    /// <summary>AutoCAD colour index of the CP's layer: red traffic, green load, blue unload, orange rtnload, magenta rtnunload, yellow standby, cyan buffer.</summary>
    public static short ColorIndex(string? type)
    {
        switch ((type ?? "").ToLowerInvariant())
        {
            case "traffic": return 1;
            case "load": return 3;
            case "unload": return 5;
            case "rtnload": return 30;
            case "rtnunload": return 6;
            case "standby": return 2;
            case "buffer": return 4;
            case "finish": return 8;
            default: return 7;
        }
    }

    public static string FormatCap(int cap) => cap == Infinity ? "INF" : (cap > 0 ? cap : 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>"INF", "inf", "infinity", "무한" or "∞" is unlimited; a positive whole number is itself. Anything else is not a capacity.</summary>
    public static bool TryParseCap(string? text, out int cap)
    {
        cap = 1;
        var t = (text ?? "").Trim();
        if (t.Equals("INF", StringComparison.OrdinalIgnoreCase) || t.Equals("INFINITY", StringComparison.OrdinalIgnoreCase) || t == "무한" || t == "∞") { cap = Infinity; return true; }
        if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0) { cap = n; return true; }
        return false; // cap stays 1
    }
}
