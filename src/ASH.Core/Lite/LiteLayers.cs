using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ASH.Core.Lite;

public static class LiteLayers
{
    /// <summary>ASH's own layers (AGV_PATH, AGV_CP_LOAD ...) never go into the lite drawing: old paths copied into the background would be read as paths again by AGVCHECK.</summary>
    public const string AshPrefix = "AGV_";

    public static bool IsAsh(string layer) => (layer ?? "").StartsWith(AshPrefix, StringComparison.OrdinalIgnoreCase);

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
