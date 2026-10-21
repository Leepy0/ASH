using System.Collections.Generic;

namespace ASH.Core.Layout;

/// <summary>
/// The four AutoMod path types and how they are stored. They are the GPATH keywords: one-way (no keyword), "two" (bidirectional), "spur", "crab".
/// A crab path can also be one-way, two-way or a spur, so the three flags are independent; the plugin offers the four common types
/// and reads whatever combination a drawing or .asy holds.
/// </summary>
public static class PathKinds
{
    /// <summary>"path" (one-way), "two", "spur", "crab" or a "+" join such as "spur+crab". Written to the drawing's XData.</summary>
    public static string Encode(bool spur, bool two, bool crab)
    {
        var t = new List<string>();
        if (two) t.Add("two");
        if (spur) t.Add("spur");
        if (crab) t.Add("crab");
        return t.Count == 0 ? "path" : string.Join("+", t);
    }

    /// <summary>Unknown text means one-way.</summary>
    public static (bool Spur, bool Two, bool Crab) Decode(string? code)
    {
        bool spur = false, two = false, crab = false;
        foreach (var part in (code ?? "").Split('+'))
            switch (part.Trim().ToLowerInvariant())
            {
                case "spur": spur = true; break;
                case "two": two = true; break;
                case "crab": crab = true; break;
            }
        return (spur, two, crab);
    }

    public static string Label(bool spur, bool two, bool crab)
    {
        var t = new List<string>();
        if (two) t.Add("양방향");
        if (spur) t.Add("지선");
        if (crab) t.Add("크랩");
        return t.Count == 0 ? "단방향" : string.Join("+", t);
    }

    public static string Label(LayoutPath p) => Label(p.IsSpur, p.IsTwoWay, p.IsCrab);
}
