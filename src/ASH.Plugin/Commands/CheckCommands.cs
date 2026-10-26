using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ASH.Core.Cause;
using ASH.Core.Input;
using ASH.Core.Layout;
using ASH.Core.Settings;
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
                    XData.WritePath(e, rename[p.Id], x?.Reversed ?? false, (p.IsSpur, p.IsTwoWay, p.IsCrab), tr); // XData only: geometry, dir and ptype stay
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
                layout = DrawingReader.Read(db, tr);
                findings = Report(ed, layout);
            }
            var cause = CauseRows(ed, layout);
            ed.WriteMessage($"\n검사 결과 {findings.Count + cause.Count}건");
            Ui.PaletteStatus.SetCheck(findings.Count + cause.Count);
            // only problems exist in this list; the palette shows them in a table and a double-click goes to the spot
            Ui.PaletteStatus.SetFindings(findings.Select(f => new Ui.FindingRow(FindingText.Label(f.Code), f.EntityId ?? "", f.Message,
                f.Where.X, f.Where.Y, !(f.Where.X == 0 && f.Where.Y == 0))).Concat(cause).ToList());
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

    /// <summary>CauseAnalysis.Static findings of the drawing as palette rows (warnings). The layout goes to the analysers in model metres like the exporter makes it
    /// ((p - Origin) x Scale, ash.settings.tsv, else Scale from the drawing units); input.xlsx is read when it is there and readable, else the analysers get an empty input
    /// (so no detour findings). Zoom targets are converted back to drawing units. Never throws: a failing analyser only drops its findings from the check.</summary>
    internal static List<Ui.FindingRow> CauseRows(Editor ed, CoreLayout layout)
    {
        var rows = new List<Ui.FindingRow>();
        try
        {
            var s = new ProjectSettings(Scale: 1 / DrawingUnits.PerMeter);
            ProjectInput? input = null;
            if (System.Convert.ToInt32(Application.GetSystemVariable("DWGTITLED")) != 0)
            {
                var dir = (string)Application.GetSystemVariable("DWGPREFIX");
                var sf = Path.Combine(dir, ExportCommands.SettingsFileName);
                try { if (File.Exists(sf)) s = SettingsFile.Load(sf); }
                catch (System.Exception ex) when (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException) { }
                var xlsx = Path.Combine(dir, ModelCommands.ExcelFileName);
                try { if (File.Exists(xlsx)) input = ExcelReader.Read(xlsx); }
                catch (System.Exception ex) when (!(ex is OperationCanceledException)) { PluginLog.Write("AGVCHECK Excel read", ex); } // locked or encrypted: layout-only
            }
            foreach (var i in CauseFrame.StaticOf(layout, s, input))
            {
                var text = string.Join(" · ", new[] { i.Title, i.Detail, i.Fix.Length > 0 ? "조치 " + i.Fix : "" }.Where(t => t.Length > 0));
                bool at = i.X is double && i.Y is double;
                var p = at ? CauseFrame.ToDrawing(i.X!.Value, i.Y!.Value, s.Origin, s.Scale) : default;
                ed.WriteMessage($"\n[원인] {text}" + (at ? $" ({p.X:0.###}, {p.Y:0.###})" : ""));
                rows.Add(new Ui.FindingRow("원인", i.Where, text, p.X, p.Y, at, warning: true));
            }
        }
        catch (System.Exception ex) when (!(ex is OperationCanceledException))
        {
            PluginLog.Write("AGVCHECK cause", ex);
        }
        return rows;
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
