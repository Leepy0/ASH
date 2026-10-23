using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Asy;
using ASH.Core.Geometry;

namespace ASH.Core.Export;

public static class ExportDiff
{
    const double Eps = 1e-6;

    static bool Near(Point2 a, Point2 b) => Math.Abs(a.X - b.X) <= Eps && Math.Abs(a.Y - b.Y) <= Eps;

    static bool SamePiece(GuidePath a, GuidePath b) => (a.Piece, b.Piece) switch
    {
        (LinePiece x, LinePiece y) => Near(x.Beg, y.Beg) && Near(x.End, y.End),
        (ArcPiece x, ArcPiece y) => Near(x.Center, y.Center) && Near(x.Beg, y.Beg) && x.AngleTenths == y.AngleTenths,
        _ => false,
    };

    public static ExportSummary Compare(AgvsSystem? previous, AgvsSystem current)
    {
        // a hand-edited previous file may repeat names: first one wins
        var pp = previous?.Paths.GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.First()) ?? new Dictionary<string, GuidePath>();
        // start-only CPs follow the standby lane and the vehicle count; the summary counts the drawing's CPs
        var pc = previous?.ControlPoints.Where(c => !LayoutExporter.IsStartCp(c)).GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First()) ?? new Dictionary<string, ControlPoint>();
        var cc = current.ControlPoints.Where(c => !LayoutExporter.IsStartCp(c)).ToDictionary(c => c.Name);
        var cp = current.Paths.ToDictionary(p => p.Name);
        return new ExportSummary(
            cp.Keys.Count(k => !pp.ContainsKey(k)), pp.Keys.Count(k => !cp.ContainsKey(k)),
            cp.Count(kv => pp.TryGetValue(kv.Key, out var o) && !SamePiece(o, kv.Value)),
            cc.Keys.Count(k => !pc.ContainsKey(k)), pc.Keys.Count(k => !cc.ContainsKey(k)),
            cc.Count(kv => pc.TryGetValue(kv.Key, out var o) && (o.PathName != kv.Value.PathName || Math.Abs(o.Dist - kv.Value.Dist) > Eps)));
    }
}
