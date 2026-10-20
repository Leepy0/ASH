using System.Collections.Generic;
using System.Linq;
using ASH.Core.Layout;
using ASH.Plugin.Cad;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Runtime;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;
using CoreLayout = ASH.Core.Layout.Layout;

[assembly: CommandClass(typeof(ASH.Plugin.Commands.CheckCommands))]

namespace ASH.Plugin.Commands;

public sealed class CheckCommands
{
    [CommandMethod("AGVCHECK")]
    public void AgvCheck()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;
        PathCommands.Run(doc, "AGVCHECK", (db, tr) =>
        {
            var layout = DrawingReader.Read(db, tr);
            var findings = Report(ed, layout);
            bool changed = false;
            var rename = PathRenamer.Plan(layout.Paths); // non-empty exactly when paths have DuplicatePathName/InvalidName findings
            if (rename.Count > 0 && PathCommands.Keyword(ed, "\n경로 이름을 정리할까요? [Y/N] <Y>", "Y", "Y", "N") == "Y")
            {
                foreach (var p in layout.Paths.Where(p => rename.ContainsKey(p.Id)))
                {
                    var id = DrawingWriter.FromHandle(db, p.Id);
                    if (id.IsNull) continue;
                    var e = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                    var x = XData.ReadPath(e);
                    XData.WritePath(e, rename[p.Id], x?.Reversed ?? false, p.IsSpur, tr); // XData only: geometry, dir and ptype stay
                    ed.WriteMessage($"\n경로 {p.Name} → {rename[p.Id]}");
                    p.Name = rename[p.Id];
                }
                changed = true;
                ed.WriteMessage("\n재검사:");
                findings = Report(ed, layout);
            }
            if (findings.Any(f => f.Code == FindingCode.UnsplitIntersection) &&
                PathCommands.Keyword(ed, "\n지금 분할할까요? [Y/N] <Y>", "Y", "Y", "N") == "Y")
            {
                if (PathCommands.SplitPaths(db, tr, ed, layout.Paths, layout.Tolerance))
                {
                    changed = true;
                    DrawingWriter.RefreshDirectionArrows(db, tr); // Run skips the refresh for AGVCHECK
                }
                ed.WriteMessage("\n재검사:");
                findings = Report(ed, DrawingReader.Read(db, tr));
            }
            ed.WriteMessage($"\n검사 결과 {findings.Count}건");
            Ui.PaletteStatus.SetCheck(findings.Count);
            return changed;
        }, refreshArrows: false);
    }

    /// <summary>Prints every finding as "[code] message (x, y)" and zooms to the first one. Uses DrawingUnits.PerMeter for the CP offset limit and zoom width.</summary>
    internal static List<Finding> Report(Editor ed, CoreLayout layout)
    {
        double u = DrawingUnits.PerMeter;
        var findings = LayoutChecker.Check(layout, new CheckOptions(CpMaxOffset: new CheckOptions().CpMaxOffset * u));
        foreach (var f in findings) ed.WriteMessage($"\n[{f.Code}] {f.Message} ({f.Where.X:0.###}, {f.Where.Y:0.###})");
        if (findings.Count > 0) ZoomTo(ed, findings[0].Where.X, findings[0].Where.Y, 10 * u);
        return findings;
    }

    // shortcut: assumes a plan (WCS top) view, so DCS centre = WCS point; upgrade to a WCS->DCS transform if users work in rotated UCS/views.
    private static void ZoomTo(Editor ed, double x, double y, double width)
    {
        using (var v = ed.GetCurrentView())
        {
            double aspect = v.Width > 0 ? v.Height / v.Width : 1;
            v.CenterPoint = new Point2d(x, y);
            v.Width = width;
            v.Height = width * aspect;
            ed.SetCurrentView(v);
        }
    }
}
