using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using ASH.Core.Runner;

namespace ASH.Core.Cause;

/// <summary>A finding with a place, numbered for the drawing marker: <see cref="At"/> is in drawing units.</summary>
public sealed record CauseMark(int Number, CauseInfo Item, Point2 At);

/// <summary>What AGVCAUSE draws from the server's findings: the ones with a place, numbered in the order given (the 원인 tab's order), at their model-metre
/// place converted back to drawing units (the inverse of the export's (p - Origin) x Scale).</summary>
public static class CauseMarks
{
    public static readonly string[] SeverityText = { "참고", "주의", "심각" };

    public static IReadOnlyList<CauseMark> Plan(IEnumerable<CauseInfo> items, Point2 origin, double scale)
    {
        if (!(scale > 0) || double.IsInfinity(scale)) throw new ArgumentOutOfRangeException(nameof(scale), "Scale must be above 0.");
        var res = new List<CauseMark>();
        foreach (var i in items)
            if (i.X is double x && i.Y is double y && !double.IsNaN(x) && !double.IsInfinity(x) && !double.IsNaN(y) && !double.IsInfinity(y))
                res.Add(new CauseMark(res.Count + 1, i, CauseFrame.ToDrawing(x, y, origin, scale)));
        return res;
    }

    public static string SeverityName(int severity) => SeverityText[Math.Max(0, Math.Min(2, severity))];

    /// <summary>Handles of the drawing's paths, CPs and blocks a finding names (Refs, case-insensitive); names the drawing lacks are ignored.</summary>
    public static IReadOnlyList<string> RefHandles(IEnumerable<FoundObject> objects, IEnumerable<string>? refs)
    {
        var names = new HashSet<string>(refs ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0) return new string[0];
        return objects.Where(o => o.Handle != null && (o.Kind == "경로" || o.Kind == "CP" || o.Kind == "block") && names.Contains(o.Name)).Select(o => o.Handle!).Distinct().ToList();
    }

    /// <summary>The title cut to <paramref name="max"/> characters with a closing "…", for the text beside a marker.</summary>
    public static string ShortTitle(string title, int max = 20)
    {
        var t = (title ?? "").Trim();
        return t.Length <= max ? t : t.Substring(0, Math.Max(1, max - 1)) + "…";
    }
}
