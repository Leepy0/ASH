using ASH.Plugin.Cad;
using ASH.Plugin.Ui;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Runtime;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ASH.Plugin.Commands.Line3dCommands))]

namespace ASH.Plugin.Commands;

public sealed class Line3dCommands
{
    /// <summary>AGV3DLINE: pick points like PLINE (Enter ends, Close closes, Undo drops the last point); the polyline gets the kind's layer and a constant width.
    /// The kind comes from the palette button (Line3dSettings.Pending), otherwise it is asked.</summary>
    [CommandMethod("AGV3DLINE")]
    public void Agv3dLine()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;
        var kind = Line3dSettings.Pending;
        Line3dSettings.Pending = null;
        PathCommands.Run(doc, "AGV3DLINE", (db, tr) =>
        {
            if (kind == null)
            {
                var ko = new PromptKeywordOptions("\n종류 [Wall/Fence/Conv/Picture]");
                foreach (var k in new[] { "Wall", "Fence", "Conv", "Picture" }) ko.Keywords.Add(k);
                ko.Keywords.Default = "Wall";
                var kr = ed.GetKeywords(ko);
                if (kr.Status != PromptStatus.OK) return false;
                kind = kr.StringResult switch { "Fence" => Line3dSettings.Fence, "Conv" => Line3dSettings.Conv, "Picture" => Line3dSettings.ConvPicture, _ => Line3dSettings.Wall };
            }
            var ms = PathCommands.ModelSpaceId(db, tr);
            if (db.CurrentSpaceId != ms) { ed.WriteMessage("\n모델 공간에서만 그립니다."); return false; }
            bool closable = kind == Line3dSettings.Wall || kind == Line3dSettings.Fence;
            var (layer, label, widthM) = kind switch
            {
                Line3dSettings.Fence => (Conventions.Layer3dFence, "펜스", 0.0),
                Line3dSettings.Conv => (Conventions.LayerConv, "컨베이어", Line3dSettings.ConvM),
                Line3dSettings.ConvPicture => (Conventions.Layer3dConv, "컨베이어(그림)", Line3dSettings.ConvM),
                _ => (Conventions.Layer3dWall, "벽", Line3dSettings.WallM),
            };
            double w = widthM * DrawingUnits.PerMeter;
            var first = ed.GetPoint(new PromptPointOptions($"\n{label} 시작점"));
            if (first.Status != PromptStatus.OK) return false;
            var pl = new Polyline { Layer = layer };
            pl.AddVertexAt(0, new Point2d(first.Value.X, first.Value.Y), 0, w, w);
            var btr = (BlockTableRecord)tr.GetObject(ms, OpenMode.ForWrite);
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            bool closed = false;
            while (!closed)
            {
                var last = pl.GetPoint2dAt(pl.NumberOfVertices - 1);
                var po = new PromptPointOptions($"\n{label} 다음 점(Enter 종료)") { AllowNone = true, UseBasePoint = true, BasePoint = new Point3d(last.X, last.Y, 0), AppendKeywordsToMessage = true };
                if (closable && pl.NumberOfVertices >= 3) po.Keywords.Add("Close");
                if (pl.NumberOfVertices >= 2) po.Keywords.Add("Undo");
                var pr = ed.GetPoint(po);
                if (pr.Status == PromptStatus.None) break;
                if (pr.Status == PromptStatus.Keyword)
                {
                    if (pr.StringResult == "Close") closed = true;
                    else pl.RemoveVertexAt(pl.NumberOfVertices - 1);
                }
                else if (pr.Status == PromptStatus.OK) pl.AddVertexAt(pl.NumberOfVertices, new Point2d(pr.Value.X, pr.Value.Y), 0, w, w);
                else return false; // Esc: nothing is kept (the transaction is dropped)
                tr.TransactionManager.QueueForGraphicsFlush();
                doc.TransactionManager.FlushGraphics();
            }
            if (pl.NumberOfVertices < 2) return false;
            pl.Closed = closed;
            pl.ConstantWidth = w;
            string? name = kind == Line3dSettings.Conv ? ConvCommands.NameNew(db, tr, pl) : null;
            ed.WriteMessage($"\n{label}{(name != null ? " " + name : "")} 추가({layer}).");
            return true;
        }, refreshArrows: false);
    }
}
