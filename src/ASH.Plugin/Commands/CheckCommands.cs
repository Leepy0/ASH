using System.Collections.Generic;
using System.Linq;
using ASH.Core.Layout;
using ASH.Plugin.Cad;
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
            if (findings.Any(f => f.Code == FindingCode.UnsplitIntersection) &&
                PathCommands.Keyword(ed, "\n지금 분할할까요? [Y/N] <Y>", "Y", "Y", "N") == "Y")
            {
                changed = PathCommands.SplitPaths(db, tr, ed, layout.Paths, layout.Tolerance);
                ed.WriteMessage("\n재검사:");
                findings = Report(ed, DrawingReader.Read(db, tr));
            }
            ed.WriteMessage($"\n검사 결과 {findings.Count}건");
            return changed;
        });
    }

    /// <summary>Prints every finding as "[code] message (x, y)" and zooms to the first one.</summary>
    internal static List<Finding> Report(Editor ed, CoreLayout layout)
    {
        var findings = LayoutChecker.Check(layout, new CheckOptions());
        foreach (var f in findings) ed.WriteMessage($"\n[{f.Code}] {f.Message} ({f.Where.X:0.###}, {f.Where.Y:0.###})");
        if (findings.Count > 0) ZoomTo(ed, findings[0].Where.X, findings[0].Where.Y, 10);
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
