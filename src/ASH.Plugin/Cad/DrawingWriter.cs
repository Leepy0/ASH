using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.Geometry;

namespace ASH.Plugin.Cad;

public static class DrawingWriter
{
    public static ObjectId AddCp(Database db, Transaction tr, LayoutCp cp) =>
        InsertBlock(db, tr, Conventions.CpBlockName, Conventions.LayerCpFor(cp.Type), cp.Position, 0, new Dictionary<string, string>
        {
            ["NAME"] = cp.Name, ["TYPE"] = cp.Type, ["GROUP"] = cp.Group,
            ["CAP"] = ASH.Core.Traffic.CpTypes.FormatCap(cp.Cap),
        });

    /// <summary>Adds the closed rectangle (returned id) plus an AGV_BLOCK attribute tag at its centre.</summary>
    public static ObjectId AddBlock(Database db, Transaction tr, LayoutBlock b)
    {
        var pl = new Polyline { Layer = Conventions.LayerBlock, Closed = true, ColorIndex = Commands.InitCommands.ByLayer };
        var r = b.Rect;
        var corners = new[] { r.Min, new Point2(r.Max.X, r.Min.Y), r.Max, new Point2(r.Min.X, r.Max.Y) };
        for (int i = 0; i < 4; i++) pl.AddVertexAt(i, new Point2d(corners[i].X, corners[i].Y), 0, 0, 0);
        var id = Append(db, tr, pl);
        var c = new Point2((r.Min.X + r.Max.X) / 2, (r.Min.Y + r.Max.Y) / 2);
        InsertBlock(db, tr, Conventions.BlockBlockName, Conventions.LayerBlock, c, 0, new Dictionary<string, string>
        {
            ["NAME"] = b.Name, ["CAP"] = (b.Cap > 0 ? b.Cap : 1).ToString(CultureInfo.InvariantCulture),
        });
        return id;
    }

    /// <summary>Erases entities by handle and adds each path as a polyline (dir 0, vertices in travel order).</summary>
    public static List<ObjectId> ReplacePaths(Database db, Transaction tr, IEnumerable<string> removeIds, IEnumerable<LayoutPath> add)
    {
        foreach (var h in removeIds)
        {
            var id = FromHandle(db, h);
            if (!id.IsNull && !id.IsErased) tr.GetObject(id, OpenMode.ForWrite).Erase();
        }
        var added = new List<ObjectId>();
        foreach (var p in add)
        {
            var pl = ToPolyline(p);
            var id = Append(db, tr, pl);
            XData.WritePath(pl, p.Name, false, (p.IsSpur, p.IsTwoWay, p.IsCrab), tr);
            p.Id = pl.Handle.ToString();
            added.Add(id);
        }
        return added;
    }

    public static Polyline ToPolyline(LayoutPath p)
    {
        var pl = new Polyline { Layer = Conventions.LayerFor(p.IsSpur, p.IsTwoWay, p.IsCrab), ColorIndex = Commands.InitCommands.ByLayer };
        int i = 0;
        foreach (var s in p.Segments.SelectMany(SplitWideArc))
        {
            double bulge = s is ArcSeg a ? Bulge.FromSweepDeg(a.SweepDeg) : 0;
            pl.AddVertexAt(i++, new Point2d(s.Start.X, s.Start.Y), bulge, 0, 0);
        }
        pl.AddVertexAt(i, new Point2d(p.End.X, p.End.Y), 0, 0, 0);
        return pl;
    }

    // bulge = tan(sweep/4) blows up at 360°; halve anything over 180°.
    private static IEnumerable<Segment> SplitWideArc(Segment s)
    {
        if (s is ArcSeg a && Math.Abs(a.SweepDeg) > 180)
        {
            var h = new ArcSeg(a.Center, a.Start, a.SweepDeg / 2);
            return new Segment[] { h, new ArcSeg(a.Center, h.End, a.SweepDeg / 2) };
        }
        return new[] { s };
    }

    /// <summary>Deletes every direction arrow and inserts one at each path segment midpoint, shaped by the path type (<see cref="Conventions.DirBlockFor"/>).</summary>
    public static void RefreshDirectionArrows(Database db, Transaction tr)
    {
        foreach (var id in DrawingReader.ModelSpace(db, tr, OpenMode.ForRead))
            if (tr.GetObject(id, OpenMode.ForRead) is BlockReference br && !br.IsErased &&
                (DrawingReader.Eq(br.Layer, Conventions.LayerDir) || Conventions.DirBlockNames.Any(n => DrawingReader.Eq(DrawingReader.BlockName(br, tr), n))))
            {
                br.UpgradeOpen();
                br.Erase();
            }
        foreach (var p in DrawingReader.Read(db, tr).Paths)
            foreach (var s in p.Segments)
            {
                var mid = s.PointAt(s.Length / 2);
                double ang;
                if (s is ArcSeg a) ang = Math.Atan2(mid.Y - a.Center.Y, mid.X - a.Center.X) + (a.SweepDeg >= 0 ? Math.PI / 2 : -Math.PI / 2);
                else ang = Math.Atan2(s.End.Y - s.Start.Y, s.End.X - s.Start.X);
                // ArrowSize meters, but at most 80% of the piece so arrows on short arcs do not pile up
                double lenM = s.Length / DrawingUnits.PerMeter, sizeM = Math.Min(DrawingUnits.ArrowMeters, 0.8 * lenM);
                if (sizeM <= 0) continue;
                InsertBlock(db, tr, Conventions.DirBlockFor(p.IsSpur, p.IsTwoWay, p.IsCrab), Conventions.LayerDir, mid, ang, null, sizeM / Conventions.ArrowDefMeters);
            }
    }

    /// <summary>Block definitions are drawn for meters; the reference is scaled by DrawingUnits.PerMeter so symbols keep their real size in mm drawings.</summary>
    public static ObjectId InsertBlock(Database db, Transaction tr, string blockName, string layer, Point2 pos, double rotation, IDictionary<string, string>? attrs, double sizeFactor = 1.0)
    {
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        if (!bt.Has(blockName)) throw new InvalidOperationException($"블록 정의 '{blockName}'이 없습니다. AGVINIT을 먼저 실행하세요.");
        var defId = bt[blockName];
        var br = new BlockReference(new Point3d(pos.X, pos.Y, 0), defId) { Layer = layer, Rotation = rotation, ScaleFactors = new Scale3d(DrawingUnits.PerMeter * sizeFactor), ColorIndex = Commands.InitCommands.ByLayer };
        var id = Append(db, tr, br);
        if (attrs == null) return id;
        var def = (BlockTableRecord)tr.GetObject(defId, OpenMode.ForRead);
        foreach (ObjectId eid in def)
        {
            if (!(tr.GetObject(eid, OpenMode.ForRead) is AttributeDefinition ad) || ad.Constant) continue;
            var ar = new AttributeReference();
            ar.SetAttributeFromBlock(ad, br.BlockTransform);
            ar.Layer = br.Layer;
            ar.ColorIndex = Commands.InitCommands.ByLayer;
            ar.TextString = attrs.TryGetValue(ad.Tag, out var v) ? v ?? "" : ad.TextString;
            try { br.AttributeCollection.AppendAttribute(ar); } catch { ar.Dispose(); throw; }
            tr.AddNewlyCreatedDBObject(ar, true);
        }
        return id;
    }

    /// <summary>Sets one attribute of a block reference; false when it has no such tag.</summary>
    public static bool SetAttribute(Transaction tr, ObjectId brId, string tag, string value)
    {
        var br = (BlockReference)tr.GetObject(brId, OpenMode.ForRead);
        foreach (ObjectId aid in br.AttributeCollection)
            if (tr.GetObject(aid, OpenMode.ForRead) is AttributeReference ar && !ar.IsErased && string.Equals(ar.Tag, tag, StringComparison.OrdinalIgnoreCase))
            {
                ar.UpgradeOpen();
                ar.TextString = value;
                return true;
            }
        return false;
    }

    public static ObjectId FromHandle(Database db, string handle)
    {
        if (!long.TryParse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h)) return ObjectId.Null;
        return db.TryGetObjectId(new Handle(h), out var id) ? id : ObjectId.Null;
    }

    private static ObjectId Append(Database db, Transaction tr, Entity e)
    {
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        ObjectId id;
        try { id = ms.AppendEntity(e); } catch { e.Dispose(); throw; } // not database-resident yet: nobody else will dispose it
        tr.AddNewlyCreatedDBObject(e, true);
        return id;
    }
}
