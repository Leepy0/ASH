using System;
using System.Collections.Generic;
using System.Linq;

namespace ASH.Core.Traffic;

public static class AutoNamer
{
    public static string Next(string prefix, IEnumerable<string> existingNames)
    {
        long max = 0;
        foreach (var n in existingNames)
        {
            if (n == null || !n.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var rest = n.Substring(prefix.Length);
            if (rest.Length > 0 && rest.Length <= 15 && rest.All(c => c >= '0' && c <= '9')) max = Math.Max(max, long.Parse(rest));
        }
        return prefix + (max + 1).ToString("D3");
    }

    /// <summary>Name pattern shown to the user, e.g. "L###" for load.</summary>
    public static string PatternFor(string cpType) => PrefixFor(cpType) + "###";

    public static string PrefixFor(string cpType)
    {
        switch ((cpType ?? "").ToLowerInvariant())
        {
            case "traffic": return "T";
            case "load": return "L";
            case "unload": return "U";
            case "rtnload": return "RL";
            case "rtnunload": return "RU";
            case "standby": return "S";
            case "buffer": return "B";
            default: return cpType != null && cpType.Length > 0 ? cpType.Substring(0, 1).ToUpperInvariant() : "X";
        }
    }
}
