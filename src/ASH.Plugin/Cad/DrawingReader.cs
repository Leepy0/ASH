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
        var layout = new CoreLayout { Tolerance = 0.001 * DrawingUnits.PerMeter };
        var blockTags = new List<(Point2 Pos, Dictionary<string, string> Attrs, string Id)>();
        var rects = new List<(Polyline Pl, string Id)>();
        foreach (ObjectId id in ModelSpace(db, tr, OpenMode.ForRead))
        {
            if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e) || e.IsErased) continue;
            string layer = e.Layer;
            if (Conventions.PathLayers.Any(l => Eq(layer, l)))
            {
                var p = ReadPath(e, Conventions.KindOfLayer(layer));
                if (p != null) layout.Paths.Add(p);
            }
            else if (e is Polyline cpl && (Eq(layer, Conventions.LayerConv) || Eq(layer, Conventions.Layer3dConv)))
            {
                var conv = ReadConveyor(cpl, Eq(layer, Conventions.Layer3dConv));
                if (conv != null) layout.Conveyors.Add(conv);
            }
            else if (e is BlockReference br)
            {
                string name = BlockName(br, tr);
                if (Conventions.IsCpLayer(layer) && Eq(name, Conventions.CpBlockName))
                {
                    var a = Attributes(br, tr);
                    layout.Cps.Add(new LayoutCp
                    {
                        Name = Get(a, "NAME"), Type = Get(a, "TYPE").ToLowerInvariant(), Group = Get(a, "GROUP"),
                        Cap = ParseCap(Get(a, "CAP")),
                        Position = Xy(br.Position), Id = br.Handle.ToString(),
                    });
                }
                else if (Eq(name, Conventions.StationBlockName))
                    layout.Stations.Add(new LayoutStation { Name = Get(Attributes(br, tr), "NAME"), Position = Xy(br.Position) });
                else if (Eq(layer, Conventions.LayerBlock) && Eq(name, Conventions.BlockBlockName))
                    blockTags.Add((Xy(br.Position), Attributes(br, tr), br.Handle.ToString()));
            }
            else if (Eq(layer, Conventions.LayerBlock) && e is Polyline pl && pl.Closed && pl.NumberOfVertices >= 3)
                rects.Add((pl, pl.Handle.ToString()));
        }
        foreach (var (pl, id) in rects)
        {
            var pts = Enumerable.Range(0, pl.NumberOfVertices).Select(i => Xy(pl.GetPoint3dAt(i))).ToList();
            // shortcut: non-rectangular closed polylines become their bounding box; fine until polygon blocks are needed.
            var rect = new Rect2(new Point2(pts.Min(q => q.X), pts.Min(q => q.Y)), new Point2(pts.Max(q => q.X), pts.Max(q => q.Y)));
            // AddBlock puts the tag at the centre; take the nearest tag inside the rect so nested/adjacent rects don't steal each other's
            var centre = new Point2((rect.Min.X + rect.Max.X) / 2, (rect.Min.Y + rect.Max.Y) / 2);
            var tag = blockTags.Where(t => rect.Contains(t.Pos)).OrderBy(t => t.Pos.DistanceTo(centre)).FirstOrDefault();
            layout.Blocks.Add(new LayoutBlock
            {
                Id = id, Rect = rect, TagId = tag.Id ?? "",
                Name = tag.Attrs == null ? "" : Get(tag.Attrs, "NAME"),
                Cap = tag.Attrs == null ? 1 : ParseCap(Get(tag.Attrs, "CAP")),
            });
        }
        return layout;
    }

    /// <summary>Converts a path entity (Line/Arc/Polyline) to a LayoutPath in travel order; null for other entity types.</summary>
    public static LayoutPath? ReadPath(Entity e, (bool Spur, bool Two, bool Crab) layerKind)
    {
        var segs = Segments(e);
        if (segs == null || segs.Count == 0) return null;
        var x = XData.ReadPath(e);
        if (x?.Reversed == true)
        {
            segs.Reverse();
            segs = segs.Select(s => s.Reversed()).ToList();
        }
        return new LayoutPath(x?.Name ?? "?" + e.Handle)
        {
            Id = e.Handle.ToString(), Segments = segs,
            IsSpur = x?.IsSpur ?? layerKind.Spur, IsTwoWay = x?.IsTwoWay ?? layerKind.Two, IsCrab = x?.IsCrab ?? layerKind.Crab,
        };
    }

    /// <summary>A conveyor polyline in drawing order (= flow); segments in drawing units like paths, the width (first segment's start width,
    /// which the global width sets) in metres. AGV_CONV ones carry their AGVCONV name, AGV_3D_CONV ones are picture only.</summary>
    public static LayoutConveyor? ReadConveyor(Polyline pl, bool graphicOnly)
    {
        var segs = Segments(pl);
        if (segs == null || segs.Count == 0) return null;
        return new LayoutConveyor
        {
            Name = graphicOnly ? "" : XData.ReadConv(pl)?.Name ?? "", GraphicOnly = graphicOnly, Segments = segs, Id = pl.Handle.ToString(),
            WidthM = pl.NumberOfVertices > 0 ? Math.Abs(pl.GetStartWidthAt(0)) / DrawingUnits.PerMeter : 0,
        };
    }

    /// <summary>Line/Arc/Polyline geometry in drawing (vertex) order, XData ignored; null for other entity types.</summary>
    public static List<Segment>? Segments(Entity e)
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
                double flip = pl.Normal.Z < 0 ? -1 : 1;
                var v = Enumerable.Range(0, pl.NumberOfVertices).Select(i => (Xy(pl.GetPoint3dAt(i)), pl.GetBulgeAt(i) * flip)).ToList();
                return PathConvert.FromVertices(v, pl.Closed);
            }
            default:
                return null;
        }
        segs.RemoveAll(s => s.Length <= MinLen);
        return segs;
    }

    /// <summary>A simple (not fitted) old-style 2D POLYLINE, as <see cref="Segments(Entity)"/> does for LWPOLYLINE.</summary>
    public static List<Segment> Segments(Polyline2d p, Transaction tr)
    {
        double flip = p.Normal.Z < 0 ? -1 : 1;
        var v = new List<(Point2, double)>();
        int i = 0;
        foreach (ObjectId vid in p)
            if (tr.GetObject(vid, OpenMode.ForRead) is Vertex2d vx)
                v.Add((Xy(p.GetPointAtParameter(i++)), vx.Bulge * flip)); // vertex k sits at parameter k, in WCS
        return PathConvert.FromVertices(v, p.Closed);
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
    private static int ParseCap(string s) => ASH.Core.Traffic.CpTypes.TryParseCap(s, out var c) ? c : 1;
}
