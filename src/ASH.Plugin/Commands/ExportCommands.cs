using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ASH.Core.Export;
using ASH.Core.Layout;
using ASH.Core.Settings;
using ASH.Plugin.Cad;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Runtime;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ASH.Plugin.Commands.ExportCommands))]

namespace ASH.Plugin.Commands;

public sealed class ExportCommands
{
    public const string SettingsFileName = "ash.settings.tsv";

    private static readonly FindingCode[] Blocking =
    {
        FindingCode.DanglingEnd, FindingCode.UnsplitIntersection, FindingCode.DuplicateCpName, FindingCode.DuplicatePathName, FindingCode.InvalidName,
        FindingCode.CpOffPath, FindingCode.UnnamedCp, FindingCode.DuplicateBlockName,
    };

    [CommandMethod("AGVEXPORT")]
    public void AgvExport()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;
        if (!TryLoadSettings(ed, out var s, out var dir)) return;
        PathCommands.Run(doc, "AGVEXPORT", (db, tr) =>
        {
            var layout = DrawingReader.Read(db, tr);
            int errors = CheckCommands.Report(ed, layout).Count(f => Blocking.Contains(f.Code)); // DanglingEnd already skips spurs
            if (errors > 0 && PathCommands.Keyword(ed, $"\n오류 {errors}건이 있습니다. 계속할까요? [Y/N] <N>", "N", "Y", "N") != "Y") return false;
            ExportResult r;
            try
            {
                var options = new ExportOptions(Units: s.Units, Origin: s.Origin, Scale: s.Scale, Groups: SettingsFile.ParseGroups(s.Groups));
                r = ExportRunner.Run(layout, options, Path.Combine(dir, s.ModelName + ".arc"));
            }
            catch (ExportException ex)
            {
                ed.WriteMessage($"\n내보내기 중단: 그룹 {ex.Group}에 standby/buffer CP가 없습니다.");
                return false;
            }
            catch (System.Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is FormatException)
            {
                ed.WriteMessage($"\n내보내기 중단: {ex.Message}");
                return false;
            }
            var m = r.Summary;
            ed.WriteMessage($"\n경로 +{m.PathsAdded} −{m.PathsRemoved} ~{m.PathsMoved}, CP +{m.CpsAdded} −{m.CpsRemoved} ~{m.CpsMoved}");
            foreach (var w in r.Warnings) ed.WriteMessage($"\n경고: {w}");
            ed.WriteMessage($"\nasy: {r.AsyPath}\ntraffic: {r.TrafficTsvPath}{(r.BackupPath != null ? "\n백업: " + r.BackupPath : "")}");
            try { Process.Start(new ProcessStartInfo(r.AsyPath) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception ex) { ed.WriteMessage($"\nasy 파일을 열지 못했습니다(연결 프로그램 없음?): {ex.Message}"); }
            return false; // nothing in the drawing changed
        }, refreshArrows: false, unitsPerMeter: UnitScale.FromSettings(s));
    }

    /// <summary>Drawing units per meter from ash.settings.tsv, without messages or writing a template; 1 for an unsaved drawing or a missing/bad file
    /// (the commands that need settings report those through TryLoadSettings).</summary>
    internal static double QuietUnitsPerMeter()
    {
        try
        {
            if (Convert.ToInt32(Application.GetSystemVariable("DWGTITLED")) == 0) return 1.0;
            return UnitScale.FromSettings(SettingsFile.Load(Path.Combine((string)Application.GetSystemVariable("DWGPREFIX"), SettingsFileName)));
        }
        catch (System.Exception ex) when (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)
        {
            return 1.0;
        }
    }

    /// <summary>Loads &lt;drawing folder&gt;\ash.settings.tsv, writing a default template there when missing. False, with a message, for an unsaved drawing or a bad file.
    /// Warns (does not abort) when INSUNITS is mm but Scale is 1.</summary>
    internal static bool TryLoadSettings(Editor ed, out ProjectSettings settings, out string dir)
    {
        settings = new ProjectSettings();
        dir = "";
        if (Convert.ToInt32(Application.GetSystemVariable("DWGTITLED")) == 0)
        {
            ed.WriteMessage("\n도면을 먼저 저장하세요. 설정 파일(" + SettingsFileName + ")과 내보내기 결과를 도면 폴더에 둡니다.");
            return false;
        }
        dir = (string)Application.GetSystemVariable("DWGPREFIX");
        var path = Path.Combine(dir, SettingsFileName);
        try
        {
            if (!File.Exists(path))
            {
                SettingsFile.Save(new ProjectSettings(), path);
                ed.WriteMessage($"\n설정 파일이 없어 기본값으로 만들었습니다: {path}");
            }
            settings = SettingsFile.Load(path);
            if (Convert.ToInt32(Application.GetSystemVariable("INSUNITS")) == 4 && settings.Scale == 1.0)
                ed.WriteMessage("\n도면 단위가 mm인데 Scale=1 입니다. ash.settings.tsv의 Scale을 0.001로 설정하세요.");
            return true;
        }
        catch (System.Exception ex) when (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)
        {
            ed.WriteMessage($"\n설정 파일 오류 ({Path.Combine(dir, SettingsFileName)}): {ex.Message}");
            return false;
        }
    }
}
