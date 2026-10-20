using System;
using ASH.Plugin.Cad;
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.Colors;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Runtime;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ASH.Plugin.Commands.InitCommands))]

namespace ASH.Plugin.Commands;

public sealed class InitCommands
{
    [CommandMethod("AGVINIT")]
    public void AgvInit()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;
        try
        {
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                int made = Init(doc.Database, tr);
                tr.Commit();
                ed.WriteMessage($"\nASH 양식 초기화: 새로 만든 항목 {made}개.");
            }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nAGVINIT 오류: {ex.Message}");
        }
    }

    /// <summary>Creates missing layers, block definitions and the ASH regapp. Returns how many items were created.</summary>
    public static int Init(Database db, Transaction tr)
    {
        int made = 0;
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        foreach (var (name, color) in new[]
        {
            (Conventions.LayerPath, (short)3), (Conventions.LayerPathSpur, (short)2), (Conventions.LayerDir, (short)8),
            (Conventions.LayerCp, (short)1), (Conventions.LayerBlock, (short)5), (Conventions.LayerStation, (short)6),
        })
        {
            if (lt.Has(name)) continue;
            if (!lt.IsWriteEnabled) lt.UpgradeOpen();
            var r = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, color) };
            lt.Add(r);
            tr.AddNewlyCreatedDBObject(r, true);
            made++;
        }

        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        made += Def(db, tr, bt, Conventions.CpBlockName, b =>
        {
            b(new Circle(Point3d.Origin, Vector3d.ZAxis, 0.3));
            for (int i = 0; i < Conventions.CpAttributes.Length; i++)
            {
                var tag = Conventions.CpAttributes[i];
                b(Attr(db, tag, tag == "CAP" ? "1" : "", new Point3d(0.4, 0.2 - i * 0.25, 0), invisible: tag != "NAME"));
            }
        });
        made += Def(db, tr, bt, Conventions.StationBlockName, b =>
        {
            b(Square(0.6));
            b(Attr(db, "NAME", "", new Point3d(0.4, 0.4, 0), invisible: false));
        });
        made += Def(db, tr, bt, Conventions.BlockBlockName, b =>
        {
            b(Square(0.2));
            b(Attr(db, "NAME", "", new Point3d(0.2, 0.1, 0), invisible: false));
            b(Attr(db, "CAP", "1", new Point3d(0.2, -0.2, 0), invisible: true));
        });
        made += Def(db, tr, bt, Conventions.DirBlockName, b =>
        {
            // arrow pointing +X, centred on the insertion point
            b(new Line(new Point3d(-0.3, 0, 0), new Point3d(0.3, 0, 0)));
            b(new Line(new Point3d(0.3, 0, 0), new Point3d(0.1, 0.15, 0)));
            b(new Line(new Point3d(0.3, 0, 0), new Point3d(0.1, -0.15, 0)));
        });

        var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
        if (!rat.Has(Conventions.AppName)) { XData.EnsureRegApp(db, tr); made++; }
        return made;
    }

    private static int Def(Database db, Transaction tr, BlockTable bt, string name, Action<Action<Entity>> fill)
    {
        if (bt.Has(name)) return 0;
        if (!bt.IsWriteEnabled) bt.UpgradeOpen();
        var b = new BlockTableRecord { Name = name, Origin = Point3d.Origin };
        bt.Add(b);
        tr.AddNewlyCreatedDBObject(b, true);
        fill(e =>
        {
            e.Layer = "0"; // ByLayer geometry on 0 takes the colour of the layer the reference is inserted on
            try { b.AppendEntity(e); } catch { e.Dispose(); throw; }
            tr.AddNewlyCreatedDBObject(e, true);
        });
        return 1;
    }

    private static Polyline Square(double size)
    {
        double h = size / 2;
        var pl = new Polyline { Closed = true };
        pl.AddVertexAt(0, new Point2d(-h, -h), 0, 0, 0);
        pl.AddVertexAt(1, new Point2d(h, -h), 0, 0, 0);
        pl.AddVertexAt(2, new Point2d(h, h), 0, 0, 0);
        pl.AddVertexAt(3, new Point2d(-h, h), 0, 0, 0);
        return pl;
    }

    private static AttributeDefinition Attr(Database db, string tag, string value, Point3d pos, bool invisible) =>
        new AttributeDefinition(pos, value, tag, tag, db.Textstyle) { Height = 0.15, Invisible = invisible, Layer = "0" };
}
