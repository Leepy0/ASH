using System;
using System.Collections.Generic;
using System.Linq;
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
        ExportCommands.UnitsPerMeter(ed); // create ash.settings.tsv at init and show the unit warning early
        try
        {
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                int made = Init(doc.Database, tr, out var fixedDefs);
                int recolored = ByLayerOnAshLayers(doc.Database, tr);
                var missing = MissingCpTags(doc.Database, tr);
                tr.Commit();
                ed.WriteMessage($"\nASH 양식 초기화: 새로 만든 항목 {made}개, 레이어 색·블록 정의 복구 {fixedDefs}개, 색을 ByLayer로, 숨김을 표시로 되돌린 객체 {recolored}개.");
                Ui.PaletteStatus.Set($"AGVINIT 완료: 새 항목 {made}개" + (missing.Count > 0 ? ", CP 어트리뷰트 누락 경고" : ""));
                if (missing.Count > 0)
                    ed.WriteMessage($"\n경고: 기존 {Conventions.CpBlockName} 블록 정의에 어트리뷰트 {string.Join(", ", missing)}이(가) 없습니다. 블록 정의를 고치거나 지운 뒤 AGVINIT을 다시 실행하세요.");
            }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nAGVINIT 오류: {ex.Message}");
            Ui.PaletteStatus.Set($"AGVINIT 오류: {ex.Message}");
        }
    }

    /// <summary>ACI "ByLayer": ASH objects (block references, paths) take their colour from the layer.</summary>
    internal const short ByLayer = 256;

    /// <summary>ACI "ByBlock": the contents of ASH block definitions (on layer 0) take the colour of the reference. That is its layer's colour while the reference is ByLayer, and the grey of the group view (F1) when it sets one.</summary>
    internal const short ByBlock = 0;

    static List<(string Name, short Color)> AshLayers()
    {
        var layers = new List<(string Name, short Color)>
        {
            (Conventions.LayerPath, (short)3), (Conventions.LayerPathSpur, (short)2), (Conventions.LayerPathTwo, (short)4), (Conventions.LayerPathCrab, (short)30),
            (Conventions.LayerDir, (short)8),
            (Conventions.LayerCp, (short)1), (Conventions.LayerBlock, (short)5), (Conventions.LayerStation, (short)6),
            (Conventions.LayerConv, (short)94), (Conventions.Layer3dConv, (short)96),
            (Conventions.Layer3d, (short)9), (Conventions.Layer3dWall, (short)8), (Conventions.Layer3dFence, (short)40),
        };
        foreach (var t in ASH.Core.Traffic.CpTypes.All) layers.Add((Conventions.LayerCpFor(t), ASH.Core.Traffic.CpTypes.ColorIndex(t)));
        return layers;
    }

    /// <summary>Creates missing layers, block definitions and the ASH regapp. Returns how many items were created.</summary>
    public static int Init(Database db, Transaction tr) => Init(db, tr, out _);

    /// <summary>Also puts back what a drawing saved earlier may carry (<paramref name="fixedDefs"/>): an ASH layer with another colour, and ASH block
    /// definitions whose contents are not on layer 0 ByBlock (then the arrow, CP and block symbols ignore their layer's colour).</summary>
    public static int Init(Database db, Transaction tr, out int fixedDefs)
    {
        int made = 0;
        fixedDefs = 0;
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        foreach (var (name, color) in AshLayers())
        {
            if (lt.Has(name))
            {
                var l = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForRead);
                if (l.Color.IsByAci && l.Color.ColorIndex == color) continue;
                l.UpgradeOpen();
                l.Color = Color.FromColorIndex(ColorMethod.ByAci, color);
                fixedDefs++;
                continue;
            }
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

        made += Def(db, tr, bt, Conventions.DirTwoBlockName, b =>
        {
            // double-headed arrow along X for two-way paths, centred on the insertion point
            b(new Line(new Point3d(-0.3, 0, 0), new Point3d(0.3, 0, 0)));
            b(new Line(new Point3d(0.3, 0, 0), new Point3d(0.1, 0.15, 0)));
            b(new Line(new Point3d(0.3, 0, 0), new Point3d(0.1, -0.15, 0)));
            b(new Line(new Point3d(-0.3, 0, 0), new Point3d(-0.1, 0.15, 0)));
            b(new Line(new Point3d(-0.3, 0, 0), new Point3d(-0.1, -0.15, 0)));
        });

        foreach (var name in new[] { Conventions.CpBlockName, Conventions.StationBlockName, Conventions.BlockBlockName }.Concat(Conventions.DirBlockNames))
            if (bt.Has(name))
                foreach (ObjectId id in (BlockTableRecord)tr.GetObject(bt[name], OpenMode.ForRead))
                    if (tr.GetObject(id, OpenMode.ForRead) is Entity e && (e.Layer != "0" || e.ColorIndex != ByBlock))
                    {
                        e.UpgradeOpen();
                        e.Layer = "0";
                        e.ColorIndex = ByBlock;
                        fixedDefs++;
                    }

        made += Def(db, tr, bt, Conventions.DirSpurBlockName, b =>
        {
            // single arrow ending in a stop bar: a spur is a dead end
            b(new Line(new Point3d(-0.3, 0, 0), new Point3d(0.3, 0, 0)));
            b(new Line(new Point3d(0.3, 0, 0), new Point3d(0.1, 0.15, 0)));
            b(new Line(new Point3d(0.3, 0, 0), new Point3d(0.1, -0.15, 0)));
            b(new Line(new Point3d(0.3, 0.18, 0), new Point3d(0.3, -0.18, 0)));
        });
        made += Def(db, tr, bt, Conventions.DirCrabBlockName, b =>
        {
            // single arrow with two cross ticks on the shaft: crab travel, sideways without turning
            b(new Line(new Point3d(-0.3, 0, 0), new Point3d(0.3, 0, 0)));
            b(new Line(new Point3d(0.3, 0, 0), new Point3d(0.1, 0.15, 0)));
            b(new Line(new Point3d(0.3, 0, 0), new Point3d(0.1, -0.15, 0)));
            b(new Line(new Point3d(-0.2, 0.12, 0), new Point3d(-0.2, -0.12, 0)));
            b(new Line(new Point3d(-0.08, 0.12, 0), new Point3d(-0.08, -0.12, 0)));
        });

        var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
        if (!rat.Has(Conventions.AppName)) { XData.EnsureRegApp(db, tr); made++; }
        return made;
    }

    /// <summary>AGVINIT only (it walks model space): paths, arrows, CPs, blocks and their attributes on ASH layers with a fixed colour go back to ByLayer, and hidden ones become visible.</summary>
    private static int ByLayerOnAshLayers(Database db, Transaction tr)
    {
        var ash = new HashSet<string>(AshLayers().Select(l => l.Name), StringComparer.OrdinalIgnoreCase);
        int n = 0;
        // Visible too: the group view (AGVCPVIEW) hides CPs, and a restore must show them again
        void Fix(Entity e) { if (e.ColorIndex == ByLayer && e.Visible) return; e.UpgradeOpen(); e.ColorIndex = ByLayer; e.Visible = true; n++; }
        foreach (var id in DrawingReader.ModelSpace(db, tr, OpenMode.ForRead))
        {
            if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e) || !ash.Contains(e.Layer)) continue;
            Fix(e);
            if (e is BlockReference br)
                foreach (ObjectId aid in br.AttributeCollection)
                    if (tr.GetObject(aid, OpenMode.ForRead) is AttributeReference ar) Fix(ar);
        }
        return n;
    }

    // An AGV_CP definition made outside ASH may lack tags; Init never rewrites an existing definition's attributes.
    private static List<string> MissingCpTags(Database db, Transaction tr)
    {
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ObjectId id in (BlockTableRecord)tr.GetObject(bt[Conventions.CpBlockName], OpenMode.ForRead))
            if (tr.GetObject(id, OpenMode.ForRead) is AttributeDefinition ad) have.Add(ad.Tag);
        return Conventions.CpAttributes.Where(t => !have.Contains(t)).ToList();
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
            e.Layer = "0"; e.ColorIndex = ByBlock; // ByBlock takes the colour of the reference, which is ByLayer unless the group view greys it
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
