using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ASH.Core.Asy;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using ASH.Core.Traffic;

namespace ASH.Core.Runner;

/// <summary>Rebuilds a Layout (meters, like blocks.tsv and traffic.tsv) from a model's agv.asy + blocks.tsv + traffic.tsv, for runs without the DWG.</summary>
public static class ArcLayout
{
    /// <summary>Throws FileNotFoundException when agv.asy is missing; missing blocks.tsv / traffic.tsv give empty lists.
    /// Every GPATH becomes a one-segment LayoutPath; traffic entries naming a split path (name_s1, name_s2, ...) are expanded to its parts.
    /// The agv.asy coordinates (model units) are divided by its UNITS per meter.</summary>
    public static (Layout.Layout Layout, TrafficTable Traffic) Load(string arcDir)
    {
        var asyPath = Path.Combine(arcDir, "agv.asy");
        if (!File.Exists(asyPath)) throw new FileNotFoundException("agv.asy not found in " + arcDir, asyPath);
        var sys = AsyReader.ReadFile(asyPath);
        double k = 1 / (Settings.UnitScale.ModelUnitsPerMeter(sys.UnitsTokens.FirstOrDefault()) ?? 1);
        Point2 M(Point2 q) => new Point2(q.X * k, q.Y * k);
        var l = new Layout.Layout();
        var byName = new Dictionary<string, LayoutPath>();
        foreach (var g in sys.Paths)
        {
            var p = new LayoutPath(g.Name) { IsSpur = g.IsSpur, IsTwoWay = g.IsTwoWay, IsCrab = g.IsCrab };
            switch (g.Piece)
            {
                case LinePiece lp: p.Segments.Add(new LineSeg(M(lp.Beg), M(lp.End))); break;
                case ArcPiece ap: p.Segments.Add(new ArcSeg(M(ap.Center), M(ap.Beg), ap.AngleTenths / 10.0)); break;
            }
            l.Paths.Add(p); byName[g.Name] = p;
        }
        foreach (var c in sys.ControlPoints)
            if (!Export.LayoutExporter.IsStartCp(c) && byName.TryGetValue(c.PathName, out var p))
                l.Cps.Add(new LayoutCp { Name = c.Name, Position = p.PointAt(c.Dist * k) });

        var blocks = Path.Combine(arcDir, "blocks.tsv");
        if (File.Exists(blocks))
            foreach (var line in File.ReadAllLines(blocks).Skip(1))
            {
                var t = line.TrimEnd('\r').Split('\t');
                if (t.Length < 6) continue;
                double D(int i) => double.Parse(t[i], System.Globalization.CultureInfo.InvariantCulture);
                l.Blocks.Add(new LayoutBlock { Name = t[0], Cap = (int)D(1), Rect = new Rect2(new Point2(D(2), D(3)), new Point2(D(4), D(5))) });
            }

        var traffic = new TrafficTable();
        var tsv = Path.Combine(arcDir, "traffic.tsv");
        if (File.Exists(tsv))
            foreach (var e in TrafficTable.ReadTsv(tsv).Entries)
            {
                if (byName.ContainsKey(e.PathName)) { traffic.Entries.Add(e); continue; }
                var split = new Regex("^" + Regex.Escape(e.PathName) + @"_s\d+$");
                foreach (var p in l.Paths.Where(p => split.IsMatch(p.Name))) traffic.Entries.Add(e with { PathName = p.Name });
            }
        return (l, traffic);
    }
}
