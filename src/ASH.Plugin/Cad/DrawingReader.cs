using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using ZwSoft.ZwCAD.DatabaseServices;
using CoreLayout = ASH.Core.Layout.Layout;

namespace ASH.Plugin.Cad;

public static class DrawingReader
{
    private const double MinLen = 1e-9;

    public static CoreLayout Read(Database db, Transaction tr)
    {
        var layout = new CoreLayout();
        var blockTags = new List<(Point2 Pos, Dictionary<string, string> Attrs)>();
        var rects = new List<(Polyline Pl, string Id)>();
        foreach (ObjectId id in ModelSpace(db, tr, OpenMode.ForRead))
        {
            if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e) || e.IsErased) continue;
            string layer = e.Layer;
            if (Eq(layer, Conventions.LayerPath) || Eq(layer, Conventions.LayerPathSpur))
            {
                var p = ReadPath(e, Eq(layer, Conventions.LayerPathSpur));
                if (p != null) layout.Paths.Add(p);
            }
            else if (e is BlockReference br)
            {
                string name = BlockName(br, tr);
                if (Eq(layer, Conventions.LayerCp) && Eq(name, Conventions.CpBlockName))
                {
                    var a = Attributes(br, tr);
                    layout.Cps.Add(new LayoutCp
                    {
                        Name = Get(a, "NAME"), Type = Get(a, "TYPE").ToLowerInvariant(), Group = Get(a, "GROUP"),
                        Station = Get(a, "STATION"), Cap = ParseCap(Get(a, "CAP")),
                        Position = Xy(br.Position), Id = br.Handle.ToString(),
                    });
                }
                else if (Eq(name, Conventions.StationBlockName))
                    layout.Stations.Add(new LayoutStation { Name = Get(Attributes(br, tr), "NAME"), Position = Xy(br.Position) });
                else if (Eq(layer, Conventions.LayerBlock) && Eq(name, Conventions.BlockBlockName))
                    blockTags.Add((Xy(br.Position), Attributes(br, tr)));
            }
            else if (Eq(layer, Conventions.LayerBlock) && e is Polyline pl && pl.Closed && pl.NumberOfVertices >= 3)
                rects.Add((pl, pl.Handle.ToString()));
        }
        foreach (var (pl, id) in rects)
        {
            var pts = Enumerable.Range(0, pl.NumberOfVertices).Select(i => Xy(pl.GetPoint3dAt(i))).ToList();
            // shortcut: non-rectangular closed polylines become their bounding box; fine until polygon blocks are needed.
            var rect = new Rect2(new Point2(pts.Min(q => q.X), pts.Min(q => q.Y)), new Point2(pts.Max(q => q.X), pts.Max(q => q.Y)));
            var tag = blockTags.FirstOrDefault(t => rect.Contains(t.Pos));
            layout.Blocks.Add(new LayoutBlock
            {
                Id = id, Rect = rect,
                Name = tag.Attrs == null ? "" : Get(tag.Attrs, "NAME"),
                Cap = tag.Attrs == null ? 1 : ParseCap(Get(tag.Attrs, "CAP")),
            });
        }
        return layout;
    }

    /// <summary>Converts a path entity (Line/Arc/Polyline) to a LayoutPath in travel order; null for other entity types.</summary>
    public static LayoutPath? ReadPath(Entity e, bool spurLayer)
    {
        var segs = new List<Segment>();
        switch (e)
        {
            case Line ln:
                segs.Add(new LineSeg(Xy(ln.StartPoint), Xy(ln.EndPoint)));
                break;
            case Arc arc:
            {
                double sweep = arc.EndAngle - arc.StartAngle;
                while (sweep <= 0) sweep += 2 * Math.PI;
                if (arc.Normal.Z < 0) sweep = -sweep; // CCW about a -Z normal is CW in plan
                segs.Add(new ArcSeg(Xy(arc.Center), Xy(arc.StartPoint), sweep * 180 / Math.PI));
                break;
            }
            case Polyline pl:
            {
                int n = pl.NumberOfVertices, count = pl.Closed ? n : n - 1;
                double flip = pl.Normal.Z < 0 ? -1 : 1;
                for (int i = 0; i < count; i++)
                {
                    var s = Bulge.ToSegment(Xy(pl.GetPoint3dAt(i)), Xy(pl.GetPoint3dAt((i + 1) % n)), pl.GetBulgeAt(i) * flip);
                    if (s != null) segs.Add(s);
                }
                break;
            }
            default:
                return null;
        }
        segs.RemoveAll(s => s.Length <= MinLen);
        if (segs.Count == 0) return null;
        var x = XData.ReadPath(e);
        if (x?.Reversed == true)
        {
            segs.Reverse();
            segs = segs.Select(s => s.Reversed()).ToList();
        }
        return new LayoutPath(x?.Name ?? "?" + e.Handle) { Id = e.Handle.ToString(), IsSpur = x?.IsSpur ?? spurLayer, Segments = segs };
    }

    internal static IEnumerable<ObjectId> ModelSpace(Database db, Transaction tr, OpenMode mode)
    {
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], mode);
        return ms.Cast<ObjectId>().Where(i => !i.IsErased).ToList();
    }

    internal static string BlockName(BlockReference br, Transaction tr) =>
        ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name;

    internal static Dictionary<string, string> Attributes(BlockReference br, Transaction tr)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (ObjectId aid in br.AttributeCollection)
            if (tr.GetObject(aid, OpenMode.ForRead) is AttributeReference ar && !ar.IsErased) d[ar.Tag] = ar.TextString ?? "";
        return d;
    }

    internal static Point2 Xy(ZwSoft.ZwCAD.Geometry.Point3d p) => new Point2(p.X, p.Y);
    internal static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static string Get(Dictionary<string, string> a, string tag) => a.TryGetValue(tag, out var v) ? v.Trim() : "";
    private static int ParseCap(string s) => int.TryParse(s, out var c) && c > 0 ? c : 1;
}
