using System.Collections.Generic;
using System.Linq;
using ASH.Core.Layout;
using ASH.Plugin.Cad;
using ASH.Plugin.Ui;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Runtime;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ASH.Plugin.Commands.FindCommands))]

namespace ASH.Plugin.Commands;

/// <summary>The palette's finder: AGVFIND fills its list from the drawing, AGVGOTO pans to a double-clicked row.</summary>
public sealed class FindCommands
{
    [CommandMethod("AGVFIND")]
    public void AgvFind()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        PathCommands.Run(doc, "AGVFIND", (db, tr) =>
        {
            var items = ObjectIndex.Build(DrawingReader.Read(db, tr), DrawingUnits.PerMeter);
            PaletteStatus.SetObjects(items);
            PathCommands.FinalStatus = $"AGVFIND: {items.Count}개 객체";
            return false; // nothing changes in the drawing
        }, refreshArrows: false);
    }

    /// <summary>Pans so the object is in the middle of the screen. The zoom is not touched. The object is selected so it shows.</summary>
    [CommandMethod("AGVGOTO")]
    public void AgvGoto()
    {
        var t = Navigator.Pending;
        Navigator.Pending = null;
        if (t == null) return;
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;
        try
        {
            PanTo(ed, t.X, t.Y);
            if (t.Handle != null)
            {
                var id = DrawingWriter.FromHandle(doc.Database, t.Handle);
                if (!id.IsNull && !id.IsErased) ed.SetImpliedSelection(new[] { id });
            }
        }
        catch (System.Exception ex)
        {
            PluginLog.Write("AGVGOTO failed", ex);
            ed.WriteMessage("\nASH: " + ex.Message);
        }
    }

    /// <summary>Moves the view centre to a world point. Width and Height stay, so the zoom is the same afterwards.</summary>
    internal static void PanTo(Editor ed, double x, double y)
    {
        using (var v = ed.GetCurrentView())
        {
            // CenterPoint is in the view's own coordinates: undo target, twist and direction so a world point lands right in rotated views too
            var dcsToWcs = Matrix3d.PlaneToWorld(v.ViewDirection) * Matrix3d.Displacement(v.Target - Point3d.Origin) * Matrix3d.Rotation(-v.ViewTwist, v.ViewDirection, v.Target);
            var p = new Point3d(x, y, 0).TransformBy(dcsToWcs.Inverse());
            v.CenterPoint = new Point2d(p.X, p.Y);
            ed.SetCurrentView(v);
        }
    }
}
