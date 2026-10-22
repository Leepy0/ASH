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
            // path only: the .asy is not opened (its associated program would launch on every export)
            ed.WriteMessage($"\nasy: {r.AsyPath}\ntraffic: {r.TrafficTsvPath}{(r.BackupPath != null ? "\n백업: " + r.BackupPath : "")}");
            PathCommands.FinalStatus = "AGVEXPORT 완료";
            return false; // nothing in the drawing changed
        }, refreshArrows: false, unitsPerMeter: UnitScale.FromSettings(s));
    }

    /// <summary>Drawing units per meter for commands that don't otherwise need settings. Same load path as TryLoadSettings (template on first use, mm warning);
    /// 1, with a message, for an unsaved drawing or a bad file.</summary>
    /// <summary>ArrowSize (meters) from ash.settings.tsv without creating the file or printing anything; the default when it is unsaved, missing or unreadable.</summary>
    internal static double ArrowMeters()
    {
        var def = new ASH.Core.Settings.ProjectSettings().ArrowSize;
        try
        {
            if (Convert.ToInt32(Application.GetSystemVariable("DWGTITLED")) == 0) return def;
            var path = Path.Combine((string)Application.GetSystemVariable("DWGPREFIX"), SettingsFileName);
            return File.Exists(path) ? ASH.Core.Settings.SettingsFile.Load(path).ArrowSize : def;
        }
        catch (System.Exception) { return def; }
    }

    internal static double UnitsPerMeter(Editor ed)
    {
        if (Convert.ToInt32(Application.GetSystemVariable("DWGTITLED")) == 0)
        {
            ed.WriteMessage("\n도면을 먼저 저장하세요 (단위 설정)");
            return 1.0;
        }
        var path = Path.Combine((string)Application.GetSystemVariable("DWGPREFIX"), SettingsFileName);
        try
        {
            return UnitScale.FromSettings(LoadOrCreate(ed, path));
        }
        catch (System.Exception ex) when (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)
        {
            ed.WriteMessage($"\n설정 파일 오류 ({path}): {ex.Message} — 단위 배율 1로 진행합니다.");
            return 1.0;
        }
    }

    // A new settings file takes its Scale from the drawing units (INSUNITS: mm 0.001, cm 0.01, m 1; no unit set: mm), so nobody has to edit it.
    private static ProjectSettings NewSettings() => new ProjectSettings(Scale: UnitScale.FromInsUnits(Convert.ToInt32(Application.GetSystemVariable("INSUNITS"))));

    // Writes the default template when missing (prints the path once: next time the file exists) and warns on a mm drawing with Scale 1.
    private static ProjectSettings LoadOrCreate(Editor ed, string path)
    {
        if (!File.Exists(path))
        {
            var created = NewSettings();
            SettingsFile.Save(created, path);
            ed.WriteMessage($"\n설정 파일이 없어 기본값으로 만들었습니다 (Scale {created.Scale}: 도면 단위 기준): {path}");
        }
        var s = SettingsFile.Load(path);
        if (Convert.ToInt32(Application.GetSystemVariable("INSUNITS")) == 4 && s.Scale == 1.0)
            ed.WriteMessage("\n도면 단위가 mm인데 Scale=1 입니다. ash.settings.tsv의 Scale을 0.001로 설정하세요.");
        return s;
    }

    /// <summary>Opens &lt;drawing folder&gt;\ash.settings.tsv in Notepad, writing the default template first when missing. Palette button, not a command.</summary>
    internal static void OpenSettings()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        try // palette click handler: nothing may escape to WinForms
        {
            if (Convert.ToInt32(Application.GetSystemVariable("DWGTITLED")) == 0) { doc.Editor.WriteMessage("\n도면을 먼저 저장하세요. 설정 파일(" + SettingsFileName + ")은 도면 폴더에 둡니다."); return; }
            var path = Path.Combine((string)Application.GetSystemVariable("DWGPREFIX"), SettingsFileName);
            if (!File.Exists(path)) SettingsFile.Save(NewSettings(), path);
            Process.Start("notepad.exe", "\"" + path + "\"");
        }
        catch (System.Exception ex)
        {
            doc.Editor.WriteMessage("\nASH: " + ex.Message);
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
            settings = LoadOrCreate(ed, path);
            return true;
        }
        catch (System.Exception ex) when (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)
        {
            ed.WriteMessage($"\n설정 파일 오류 ({Path.Combine(dir, SettingsFileName)}): {ex.Message}");
            return false;
        }
    }
}
