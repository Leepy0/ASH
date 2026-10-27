using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using ASH.Core.Traffic;
using ASH.Plugin.Cad;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Runtime;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ASH.Plugin.Commands.ConvCommands))]

namespace ASH.Plugin.Commands;

public sealed class ConvCommands
{
    const double LabelHeightM = 0.4;

    /// <summary>Names the selected AGV_CONV polylines CV001, CV002 … (XData) and labels them. A polyline keeps its name on a re-run unless another
    /// polyline already has it (a copy carries its original's XData): then it gets a new one and its own label.</summary>
    [CommandMethod("AGVCONV", CommandFlags.UsePickSet)]
    public void AgvConv()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;
        PathCommands.Run(doc, "AGVCONV", (db, tr) =>
        {
            var ids = Select(ed, tr);
            if (ids.Count == 0) { ed.WriteMessage($"\n{Conventions.LayerConv} 레이어의 폴리선을 고르세요."); return false; }
            var chosen = new HashSet<ObjectId>(ids);
            var all = DrawingReader.ModelSpace(db, tr, OpenMode.ForRead).Select(i => tr.GetObject(i, OpenMode.ForRead)).OfType<Polyline>()
                .Where(p => DrawingReader.Eq(p.Layer, Conventions.LayerConv)).ToList();
            // names held by the polylines left alone stay theirs; the chosen ones keep theirs in selection order unless taken
            var taken = new HashSet<string>(all.Where(p => !chosen.Contains(p.ObjectId)).Select(p => XData.ReadConv(p)?.Name ?? "").Where(n => n.Length > 0), StringComparer.OrdinalIgnoreCase);
            var naming = new CpNaming(all.Select(p => XData.ReadConv(p)?.Name ?? ""));
            var labels = new HashSet<string>();
            int kept = 0, named = 0;
            foreach (var id in ids)
            {
                var pl = (Polyline)tr.GetObject(id, OpenMode.ForWrite);
                var x = XData.ReadConv(pl);
                var name = x?.Name ?? "";
                if (name.Length > 0 && LayoutChecker.IsValidName(name) && taken.Add(name)) kept++;
                else { name = naming.Next("CV"); taken.Add(name); named++; }
                // a copied polyline points at its original's label: that one is not its to move
                var label = x != null && labels.Add(x.Value.LabelHandle) ? Label(db, tr, x.Value.LabelHandle) : null;
                label = PlaceLabel(db, tr, pl, name, label);
                XData.WriteConv(pl, name, label.Handle.ToString(), tr);
            }
            ed.WriteMessage($"\n컨베이어 {ids.Count}개: 새 이름 {named}개, 이름 유지 {kept}개. 흐름 방향은 폴리선을 그린 방향입니다.");
            return true;
        }, refreshArrows: false);
    }

    /// <summary>Gives a new AGV_CONV polyline the next free CV name (like AGVCONV does) and its label. Returns the name.</summary>
    internal static string NameNew(Database db, Transaction tr, Polyline pl)
    {
        var names = DrawingReader.ModelSpace(db, tr, OpenMode.ForRead).Select(i => tr.GetObject(i, OpenMode.ForRead)).OfType<Polyline>()
            .Where(p => DrawingReader.Eq(p.Layer, Conventions.LayerConv)).Select(p => XData.ReadConv(p)?.Name ?? "").ToList();
        var name = new CpNaming(names).Next("CV");
        var label = PlaceLabel(db, tr, pl, name, null);
        XData.WriteConv(pl, name, label.Handle.ToString(), tr);
        return name;
    }

    static List<ObjectId> Select(Editor ed, Transaction tr)
    {
        bool Conv(ObjectId i) => tr.GetObject(i, OpenMode.ForRead) is Polyline p && DrawingReader.Eq(p.Layer, Conventions.LayerConv);
        var imp = ed.SelectImplied();
        if (imp.Status == PromptStatus.OK && imp.Value.Count > 0)
        {
            var pre = imp.Value.GetObjectIds().Where(Conv).ToList();
            ed.SetImpliedSelection(new ObjectId[0]);
            if (pre.Count > 0) return pre;
        }
        var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE"), new TypedValue((int)DxfCode.LayerName, Conventions.LayerConv) });
        var r = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = $"\n이름을 붙일 컨베이어({Conventions.LayerConv} 폴리선) 선택" }, filter);
        return r.Status == PromptStatus.OK ? r.Value.GetObjectIds().Where(Conv).ToList() : new List<ObjectId>();
    }

    static DBText? Label(Database db, Transaction tr, string handle)
    {
        if (handle.Length == 0) return null;
        var id = DrawingWriter.FromHandle(db, handle);
        return !id.IsNull && !id.IsErased && tr.GetObject(id, OpenMode.ForWrite) is DBText t ? t : null;
    }

    /// <summary>The name at the middle of the polyline, along it and beside it (outside the conveyor's width), on AGV_CONV.</summary>
    static DBText PlaceLabel(Database db, Transaction tr, Polyline pl, string name, DBText? t)
    {
        var segs = DrawingReader.Segments(pl) ?? new List<Segment>();
        double half = segs.Sum(s => s.Length) / 2, d = half, step = 0.01 * DrawingUnits.PerMeter;
        var seg = segs.FirstOrDefault(s => { if (d <= s.Length) return true; d -= s.Length; return false; }) ?? segs.LastOrDefault();
        Point2 mid = seg?.PointAt(d) ?? new Point2(pl.StartPoint.X, pl.StartPoint.Y), a = seg?.PointAt(d - step) ?? mid, b = seg?.PointAt(d + step) ?? mid;
        double ang = Math.Atan2(b.Y - a.Y, b.X - a.X);
        if (ang > Math.PI / 2 || ang <= -Math.PI / 2) ang += Math.PI; // keep the text readable
        double width = Math.Max(Math.Abs(pl.NumberOfVertices > 0 ? pl.GetStartWidthAt(0) : 0), 0.6 * DrawingUnits.PerMeter);
        double off = width / 2 + 0.2 * DrawingUnits.PerMeter;
        var at = new Point3d(mid.X - Math.Sin(ang) * off, mid.Y + Math.Cos(ang) * off, 0);
        if (t == null)
        {
            t = new DBText();
            var ms = (BlockTableRecord)tr.GetObject(PathCommands.ModelSpaceId(db, tr), OpenMode.ForWrite);
            ms.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);
        }
        t.Layer = Conventions.LayerConv;
        t.TextString = name;
        t.Height = LabelHeightM * DrawingUnits.PerMeter;
        t.Rotation = ang;
        t.HorizontalMode = TextHorizontalMode.TextCenter;
        t.VerticalMode = TextVerticalMode.TextBottom;
        t.AlignmentPoint = at;
        t.AdjustAlignment(db);
        return t;
    }
}
