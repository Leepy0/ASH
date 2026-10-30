using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ASH.Core.Input;

/// <summary>
/// The CP names held by one Excel cell: a comma list whose items are plain names or numeric ranges (L001-L010). A "-" only makes a range when both sides have the
/// same non-empty prefix ending in a non-digit followed by digits, so names that merely contain a dash (A1-B2, L001-1) stay one name.
/// </summary>
public static class CpList
{
    const int MaxRange = 1000;

    // ECMAScript: \d and \D mean ASCII digits only, so full-width digits never turn a name into a range
    static readonly Regex RangeRx = new Regex(@"^(?<p>.*\D)(?<a>\d+)-(?<q>.*\D)(?<b>\d+)$", RegexOptions.ECMAScript);

    static bool IsRange(Match m) => m.Success && m.Groups["p"].Value == m.Groups["q"].Value;

    /// <summary>The cell holds more than one plain name: a comma, or a range (a reversed or too long one included).</summary>
    public static bool IsList(string? cell)
    {
        var s = (cell ?? "").Trim();
        return s.IndexOf(',') >= 0 || IsRange(RangeRx.Match(s));
    }

    /// <summary>
    /// Splits the cell on commas, trims each item, drops empty items and expands ranges (the first number's digit count is kept: L008-L010 gives L008, L009, L010).
    /// A name seen before is dropped and added to <paramref name="duplicates"/>; a reversed range or one over 1000 names is added to <paramref name="errors"/> as typed.
    /// </summary>
    public static List<string> Parse(string? cell, List<string> errors, List<string> duplicates)
    {
        var names = new List<string>();
        var seen = new HashSet<string>();
        foreach (var raw in (cell ?? "").Split(','))
        {
            var token = raw.Trim();
            if (token.Length == 0) continue;

            var m = RangeRx.Match(token);
            if (!IsRange(m))
            {
                Add(token);
                continue;
            }

            var digits = m.Groups["a"].Value.Length;
            // a number too long for long cannot be a valid range either
            if (!long.TryParse(m.Groups["a"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var from)
                || !long.TryParse(m.Groups["b"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var to)
                || from > to || to - from >= MaxRange)
            {
                errors.Add(token);
                continue;
            }
            for (var n = from; n <= to; n++) Add(m.Groups["p"].Value + n.ToString("D" + digits, CultureInfo.InvariantCulture));
        }
        return names;

        void Add(string name)
        {
            if (seen.Add(name)) names.Add(name);
            else duplicates.Add(name);
        }
    }
}
