using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ASH.Core.Export;
using ASH.Core.Input;
using ASH.Core.Layout;
using ASH.Core.Model;
using ASH.Core.Settings;
using ASH.Plugin.Cad;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Runtime;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ASH.Plugin.Commands.ModelCommands))]

namespace ASH.Plugin.Commands;

public sealed class ModelCommands
{
    public const string ExcelFileName = "input.xlsx";

    private static readonly FindingCode[] Blocking =
    {
        FindingCode.DanglingEnd, FindingCode.UnsplitIntersection, FindingCode.DuplicateCpName, FindingCode.DuplicatePathName, FindingCode.InvalidName,
        FindingCode.CpOffPath, FindingCode.UnnamedCp, FindingCode.DuplicateBlockName,
    };

    [CommandMethod("AGVEXCEL")]
    public void AgvExcel()
    {
        var ed = Application.DocumentManager.MdiActiveDocument.Editor;
        try
        {
            if (!ExportCommands.TryLoadSettings(ed, out _, out var dir)) return;
            var path = Path.Combine(dir, ExcelFileName);
            bool made = !File.Exists(path);
            if (made) // a new file starts from what the drawing has now: group names, standby CPs, one route per load CP
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                ASH.Core.Layout.Layout? layout = null;
                PathCommands.Run(doc, "AGVEXCEL", (db, tr) => { layout = DrawingReader.Read(db, tr); return false; }, refreshArrows: false);
                ExcelTemplate.Write(path, layout);
            }
            ed.WriteMessage(made ? $"\nExcel 양식을 만들었습니다: {path}" : $"\n기존 Excel 입력을 엽니다: {path}");
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception ex) { ed.WriteMessage($"\nExcel 파일을 열지 못했습니다(연결 프로그램 없음?): {ex.Message}"); }
            Ui.PaletteStatus.Set(made ? "AGVEXCEL: 양식 생성" : "AGVEXCEL: 열기");
        }
        catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            ed.WriteMessage($"\nAGVEXCEL 오류: {ex.Message}");
            Ui.PaletteStatus.Set("AGVEXCEL 오류");
        }
    }

    [CommandMethod("AGVMODEL")]
    public void AgvModel()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;
        if (!ExportCommands.TryLoadSettings(ed, out var s, out var dir)) return;
        var xlsx = Path.Combine(dir, ExcelFileName);
        if (!File.Exists(xlsx)) { ed.WriteMessage($"\n{ExcelFileName}이 없습니다. 먼저 AGVEXCEL로 양식을 만들어 채우세요."); return; }
        PathCommands.Run(doc, "AGVMODEL", (db, tr) =>
        {
            ProjectInput input;
            try
            {
                input = ExcelReader.Read(xlsx, out var xw);
                foreach (var w in xw) ed.WriteMessage($"\nExcel 경고: {w.Message}");
            }
            catch (System.Exception ex) when (!(ex is OperationCanceledException))
            {
                // a bad value names its sheet, row and column; anything else is a damaged or locked file, so look into the file and say what is wrong with it
                ed.WriteMessage($"
Excel 읽기 오류: {ex.Message}");
                if (!(ex is ExcelDataException))
                {
                    foreach (var line in ExcelDiagnosis.Explain(xlsx, ex)) ed.WriteMessage("
  " + line);
                    PluginLog.Write("AGVMODEL Excel read failed", ex);
                    foreach (var line in ExcelDiagnosis.Inspect(xlsx)) PluginLog.Write("  " + line);
                }
                return Fail();
            }
            var layout = DrawingReader.Read(db, tr);
            int errors = CheckCommands.Report(ed, layout).Count(f => Blocking.Contains(f.Code));
            if (errors > 0 && PathCommands.Keyword(ed, $"\n오류 {errors}건이 있습니다. 계속할까요? [Y/N] <N>", "N", "Y", "N") != "Y") return false;
            ModelBuildResult r;
            try
            {
                // Excel wins over ash.settings.tsv Groups
                var groups = input.Groups.Count == 0 ? SettingsFile.ParseGroups(s.Groups) : input.Groups.Select(g =>
                {
                    var t = input.AgvTypes.FirstOrDefault(x => string.Equals(x.Type, g.Type, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException($"AGV_Groups {g.Group}: 유형 {g.Type}이 AGV_Types에 없습니다.");
                    return new VehicleGroupSpec(g.Group, g.Type, g.MaxCount, t.SpeedLoaded, t.Length, t.Width);
                }).ToList();
                var options = new ExportOptions(Units: s.Units, Origin: s.Origin, Scale: s.Scale, Groups: groups);
                Ui.PaletteStatus.Set("AGVMODEL 빌드 중…");
                ed.WriteMessage("\nAutoMod 빌드 중… 최대 10분");
                r = ModelBuilder.Build(new ModelBuildRequest(dir, s.ModelName, input, layout, options), s.AmHome, TimeSpan.FromMinutes(10));
            }
            catch (ExportException ex)
            {
                ed.WriteMessage($"\n모델 생성 중단: 그룹 {ex.Group}에 standby/buffer CP가 없습니다.");
                return Fail();
            }
            catch (System.Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)
            {
                ed.WriteMessage($"\n모델 생성 중단: {ex.Message}");
                return Fail();
            }
            foreach (var f in r.Findings.Where(f => f.Code != FindingCode.InputWarning)) ed.WriteMessage($"\n[{f.Code}] {f.Message}");
            foreach (var w in r.Warnings) ed.WriteMessage($"\n경고: {w}");
            ed.WriteMessage($"\narc: {r.ArcDir}");
            bool ok = r.Build?.Success ?? false;
            if (r.Build == null) ed.WriteMessage("\n빌드하지 않았습니다(입력 오류 또는 AutoMod 경로 없음).");
            else
            {
                ed.WriteMessage(ok ? "\n빌드 성공" : "\n빌드 실패");
                if (!ok)
                    foreach (var t in new[] { r.Build.MakeErrText, r.Build.Am2ErrText })
                        foreach (var line in (t ?? "").Split('\n').Take(20)) ed.WriteMessage("\n  " + line.TrimEnd());
            }
            PathCommands.FinalStatus = ok ? "AGVMODEL 완료" : "AGVMODEL 오류";
            return false; // nothing in the drawing changed
        }, refreshArrows: false, unitsPerMeter: UnitScale.FromSettings(s));
    }

    private static bool Fail() { PathCommands.FinalStatus = "AGVMODEL 오류"; return false; }
}
