using System;
using System.Globalization;

namespace ASH.Core.Traffic;

/// <summary>CP types with their default capacity and display colour, and the CAP text rules ("INF" = unlimited).</summary>
public static class CpTypes
{
    public static readonly string[] All = { "traffic", "load", "unload", "rtnload", "rtnunload", "standby", "buffer", "finish" };

    /// <summary>Order of the palette's CP buttons, two per row: standby|finish, load|unload, rtnload|rtnunload, buffer|traffic (work order, one pair per row). A permutation of <see cref="All"/>.</summary>
    public static readonly string[] PaletteOrder = { "standby", "finish", "load", "unload", "rtnload", "rtnunload", "buffer", "traffic" };

    /// <summary>Korean name of a type for buttons and the colour dialog; the type itself for an unknown one.</summary>
    public static string Korean(string? type)
    {
        switch ((type ?? "").ToLowerInvariant())
        {
            case "standby": return "대기";
            case "finish": return "완료";
            case "load": return "로딩";
            case "unload": return "언로딩";
            case "rtnload": return "복귀 로딩";
            case "rtnunload": return "복귀 언로딩";
            case "buffer": return "버퍼";
            case "traffic": return "교통";
            default: return type ?? "";
        }
    }

    /// <summary>AutoMod's "infinite" capacity (2147483647 in .asy files).</summary>
    public const int Infinity = int.MaxValue;

    /// <summary>Every CP, traffic included, is unlimited unless told otherwise: blocks do the one-at-a-time control.</summary>
    public static int DefaultCap(string? type) => Infinity;

    /// <summary>A traffic CP set to CAP 1 (one vehicle at a time): it goes on its own layer, <see cref="SingleTrafficLayer"/>, so it stands out.</summary>
    public static bool IsSingleTraffic(string? type, int cap) => cap == 1 && string.Equals((type ?? "").Trim(), "traffic", StringComparison.OrdinalIgnoreCase);

    public const string SingleTrafficLayer = "AGV_CP_TRAFFIC_CAP1";

    /// <summary>Default AutoCAD colour index of the CP's layer (the table is <see cref="ASH.Core.Layout.LayerColors"/>): red traffic, green load, blue unload, orange rtnload, magenta rtnunload, yellow standby, cyan buffer, grey finish.</summary>
    public static short ColorIndex(string? type) => ASH.Core.Layout.LayerColors.DefaultOf("AGV_CP_" + (type ?? "").Trim().ToUpperInvariant());

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
