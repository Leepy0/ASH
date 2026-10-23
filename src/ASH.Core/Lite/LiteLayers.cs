using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ASH.Core.Lite;

public static class LiteLayers
{
    /// <summary>A layer whose name contains one of these (case-insensitive) starts unchecked in the AGVLITE dialog: annotation, not plan geometry.
    /// Everything else (walls, columns, equipment, aisles, grids, unknown names) starts checked.</summary>
    public static readonly string[] RemoveKeywords =
    {
        "DIM", "TEXT", "TXT", "HATCH", "ANNO", "DEFPOINTS", "NOTE", "TITLE", "LEGEND", "BORDER", "LEADER", "VPORT", "VIEWPORT", "TBLK",
        "치수", "문자", "글자", "텍스트", "해치", "주석", "표제", "도곽", "범례", "인출",
    };

    /// <summary>ASH's own layers (AGV_PATH, AGV_CP_LOAD ...) also start unchecked: old paths copied into the background would be read as paths again by AGVCHECK.</summary>
    public const string AshPrefix = "AGV_";

    public static bool KeepByDefault(string layer)
    {
        var u = (layer ?? "").ToUpperInvariant();
        return !u.StartsWith(AshPrefix, StringComparison.Ordinal) && !RemoveKeywords.Any(k => u.Contains(k));
    }

    /// <summary>R12 layer names: at most 31 characters of A-Z 0-9 $ - _ (R12 stores names upper-case). Other characters (Korean, spaces, the xref '|') become '_';
    /// a name left with no letter or digit becomes LAYER_n (n = position in sorted order); clashes get _2, _3 ... Same input set, same mapping.</summary>
    public static Dictionary<string, string> R12Names(IEnumerable<string> names)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal) { "0" };
        map["0"] = "0";
        int n = 0;
        foreach (var name in names.Where(x => x != null && x != "0").Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
        {
            n++;
            var sb = new StringBuilder();
            foreach (var ch in name.ToUpperInvariant()) sb.Append(ch < 128 && (char.IsLetterOrDigit(ch) || ch == '$' || ch == '-' || ch == '_') ? ch : '_');
            var b = sb.ToString();
            if (!b.Any(char.IsLetterOrDigit)) b = "LAYER_" + n;
            if (b.Length > 31) b = b.Substring(0, 31);
            var cand = b;
            for (int k = 2; !used.Add(cand); k++) cand = (b.Length + 1 + k.ToString().Length > 31 ? b.Substring(0, 31 - 1 - k.ToString().Length) : b) + "_" + k;
            map[name] = cand;
        }
        return map;
    }
}
