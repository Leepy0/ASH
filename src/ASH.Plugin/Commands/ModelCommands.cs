using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ASH.Core.AutoMod;
using ASH.Core.Cel;
using ASH.Core.Catalog;
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

    internal static readonly FindingCode[] Blocking =
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
            List<string>? cpNames = null;
            // the [..] part holds the bare keywords only: ZWCAD offers each item between the slashes as a clickable choice, text and spaces included
            // an existing input holds hours of typing: open it, update it in place (only what the drawing added), or replace it after a second yes
            var mode = made ? "W" : PathCommands.Keyword(ed, $"\n{ExcelFileName}이(가) 이미 있습니다. O=열기, U=갱신(입력 유지, 도면에 새로 생긴 것만 추가), W=덮어쓰기(새 양식) [O/U/W] <O>", "O", "O", "U", "W");
            if (mode == null) return;
            if (mode == "W" && !made && PathCommands.Keyword(ed, "\n입력한 내용이 모두 새 양식으로 바뀝니다(기존 파일은 백업). 덮어쓸까요? [Y/N] <N>", "N", "Y", "N") != "Y") return;
            if (mode != "O") // the file starts from what the drawing has now: group names, standby CPs, one route per load CP
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                ASH.Core.Layout.Layout? layout = null;
                PathCommands.Run(doc, "AGVEXCEL", (db, tr) => { layout = DrawingReader.Read(db, tr); return false; }, refreshArrows: false);
                cpNames = layout?.Cps.Select(c => c.Name).ToList();
                if (!made)
                {
                    var bak = Path.Combine(dir, $"input.bak.{ASH.Core.SeoulTime.Now:yyyyMMdd_HHmmss}.xlsx");
                    File.Copy(path, bak, true);
                    ed.WriteMessage($"\n기존 파일 백업: {bak}");
                }
                // both reads run at once, so a server that is down stalls the command once (≈5 s), not once per read
                var serverUrl = UserSettings.Load().ServerUrl;
                // one asset-name read serves the drop-down and the unknown-asset warning; null = server unset/down (no drop-down, no warning)
                var namesTask = System.Threading.Tasks.Task.Run(() => AssetClient.AssetNames(serverUrl));
                // 3 s cap, any failure = null: the template then keeps its one T_AGV row
                var catalog = CatalogFetch.TryGet(serverUrl);
                var assetNames = namesTask.GetAwaiter().GetResult();
                ExcelTemplate.AssetListFormula = assetNames == null ? "" : string.Join(",", assetNames);
                // said after the write succeeds, per mode: W builds a new form, U keeps the existing AGV_Types
                string Filled() => catalog != null ? $"\n카탈로그 타입 {catalog.Count}개로 AGV_Types를 채웠습니다."
                    : mode == "W" ? "\n서버 카탈로그를 읽지 못해 T_AGV 한 행으로 만들었습니다(AGVSERVER로 주소 확인)."
                    : "\n서버 카탈로그를 읽지 못해 AGV_Types는 그대로 둡니다(AGVSERVER로 주소 확인).";
                if (mode == "W") { ExcelTemplate.Write(path, layout, catalog); ed.WriteMessage(Filled()); }
                else
                {
                    List<string> changes;
                    try { changes = ExcelTemplate.Update(path, layout, catalog); }
                    catch (System.Exception ex) when (!(ex is OperationCanceledException))
                    {
                        // company document encryption (NASCA) lets only Excel read and write the file, and an Excel that has it open locks it:
                        // the same update is worked out on the cells Excel reads, then Excel makes the edits and saves (the encryption stays on)
                        ed.WriteMessage("\n파일을 직접 고치지 못해(" + ex.GetType().Name + ") Excel로 갱신합니다…");
                        PluginLog.Write("AGVEXCEL direct update failed", ex);
                        var log = new List<ExcelEdit>();
                        using (var wb = ExcelCom.Read(path, out _)) changes = ExcelTemplate.Update(wb, layout, log, catalog);
                        ExcelCom.Apply(path, log);
                    }
                    ed.WriteMessage(Filled());
                    foreach (var c in changes) ed.WriteMessage($"\n  {c}");
                    if (changes.Count == 0) ed.WriteMessage("\n  추가할 내용이 없습니다(드롭다운 목록만 다시 설정).");
                    if (catalog != null)
                    {
                        try { foreach (var w in InputValidator.CatalogDiff(ExcelReader.Read(path, out _), catalog, assetNames)) ed.WriteMessage($"\n  경고: {w.Message}"); }
                        catch (System.Exception ex) when (!(ex is OperationCanceledException)) { PluginLog.Write("AGVEXCEL catalog diff", ex); } // Excel may still hold the file: only the form was updated
                    }
                }
            }
            if (mode != "O" && cpNames != null) ValidateAndMark(ed, path, dir, cpNames); // red / orange cells for what the checks object to
            ed.WriteMessage(mode == "O" ? $"\n기존 Excel 입력을 엽니다: {path}" : mode == "U" ? $"\nExcel 입력을 갱신했습니다: {path}" : $"\nExcel 양식을 만들었습니다: {path}");
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception ex) { ed.WriteMessage($"\nExcel 파일을 열지 못했습니다(연결 프로그램 없음?): {ex.Message}"); }
            Ui.PaletteStatus.Set(mode == "O" ? "AGVEXCEL: 열기" : mode == "U" ? "AGVEXCEL: 갱신" : "AGVEXCEL: 양식 생성");
        }
        catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Runtime.InteropServices.COMException
            || ex is InvalidOperationException || ex is Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            PluginLog.Write("AGVEXCEL", ex);
            ed.WriteMessage($"\nAGVEXCEL 오류: {ex.Message} (Excel에서 셀을 편집 중이면 마치고, 그래도 안 되면 {ExcelFileName}을(를) 닫고 다시 하세요)");
            Ui.PaletteStatus.Set("AGVEXCEL 오류");
        }
    }

    /// <summary>Excel input against the drawing: CPs named in Excel but not drawn, drawn CPs no row uses, CP types and groups that do not fit. The list goes to the
    /// palette's problem table (double-click zooms to the CP).</summary>
    [CommandMethod("AGVEXCELDIFF")]
    public void AgvExcelDiff()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;
        if (!ExportCommands.TryLoadSettings(ed, out _, out var dir)) return;
        var xlsx = Path.Combine(dir, ExcelFileName);
        if (!File.Exists(xlsx)) { ed.WriteMessage($"\n{ExcelFileName}이 없습니다. 먼저 AGVEXCEL로 양식을 만드세요."); return; }
        PathCommands.Run(doc, "AGVEXCELDIFF", (db, tr) =>
        {
            var input = LoadInput(ed, xlsx, dir);
            if (input == null) return false;
            var diff = ExcelDrawingDiff.Compare(input, DrawingReader.Read(db, tr));
            foreach (var d in diff) ed.WriteMessage($"\n[{d.Kind}] {d.Finding.EntityId}: {d.Finding.Message}");
            ed.WriteMessage(diff.Count == 0 ? "\n엑셀과 도면이 일치합니다." : $"\n엑셀·도면 차이 {diff.Count}건");
            Ui.PaletteStatus.SetFindings(diff.Select(d => new Ui.FindingRow(d.Kind, d.Finding.EntityId ?? "", d.Finding.Message,
                d.Finding.Where.X, d.Finding.Where.Y, !(d.Finding.Where.X == 0 && d.Finding.Where.Y == 0), warning: d.Finding.Code == FindingCode.InputWarning)).ToList());
            Ui.PaletteStatus.Set(diff.Count == 0 ? "AGVEXCELDIFF: 일치" : $"AGVEXCELDIFF: 차이 {diff.Count}건");
            return false;
        }, refreshArrows: false);
    }

    /// <summary>Reads the input just written, checks it against the drawing's CP names and marks the cells (see <see cref="MarkExcel"/>).</summary>
    private static void ValidateAndMark(Editor ed, string xlsx, string dir, IReadOnlyCollection<string> cpNames)
    {
        var input = LoadInput(ed, xlsx, dir);
        if (input != null) MarkExcel(ed, xlsx, InputValidator.Validate(input, cpNames));
    }

    /// <summary>Colours the cells the findings name (red = error, orange = warning, the message as a cell comment) and clears the marks of the last run. The file
    /// is edited directly; when that fails (company document encryption, or Excel has it locked) Excel makes the same edits. Never throws.</summary>
    internal static void MarkExcel(Editor ed, string xlsx, IReadOnlyList<Finding> findings)
    {
        try
        {
            try { ExcelMarks.MarkFile(xlsx, findings); }
            catch (System.Exception ex) when (!(ex is OperationCanceledException))
            {
                PluginLog.Write("Excel marks direct failed", ex);
                var log = new List<ExcelEdit>();
                using (var wb = ExcelCom.Read(xlsx, out _)) ExcelMarks.Apply(wb, findings, log);
                ExcelCom.Apply(xlsx, log);
            }
            int e = findings.Count(f => f.Code == FindingCode.InputError && f.EntityId?.IndexOf('!') > 0), w = findings.Count(f => f.Code == FindingCode.InputWarning && f.EntityId?.IndexOf('!') > 0);
            ed.WriteMessage(e + w == 0 ? "\nExcel 표시: 없음" : $"\nExcel 표시: 오류 {e}건(빨강), 경고 {w}건(주황) — 칸의 메모에 내용이 있습니다.");
        }
        catch (System.Exception ex) when (!(ex is OperationCanceledException))
        {
            PluginLog.Write("Excel marks", ex);
            ed.WriteMessage("\nExcel 표시를 하지 못했습니다: " + ex.Message.Split('\n')[0].Trim());
        }
    }

    [CommandMethod("AGVMODEL")]
    public void AgvModel()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;
        if (!ExportCommands.TryLoadSettings(ed, out var s, out var dir)) return;
        var xlsx = Path.Combine(dir, ExcelFileName);
        if (!File.Exists(xlsx))
        {
            // the palette's one "모델 생성" button replaced "내보내기": without the Excel input it still writes what AGVEXPORT wrote
            ed.WriteMessage($"\n{ExcelFileName}이 없어 경로·CP만 내보냅니다(agv.asy, traffic.tsv). 모델을 만들려면 먼저 AGVEXCEL로 양식을 만들어 채우세요.");
            new ExportCommands().AgvExport();
            return;
        }
        // only the model files: even with AutoMod on this PC the build is left to AGVRUN (to watch it here) or the server (AGVSEND), whichever is wanted
        bool hasAutoMod = AmBuilder.FindBuildExe(s.AmHome) != null;
        bool toServer = false;
        PathCommands.Run(doc, "AGVMODEL", (db, tr) =>
        {
            var input = LoadInput(ed, xlsx, dir);
            if (input == null) return Fail();
            var layout = DrawingReader.Read(db, tr);
            CpViewer.WarnIfHidden(ed, db, tr);
            int errors = CheckCommands.Report(ed, layout).Count(f => Blocking.Contains(f.Code));
            if (errors > 0 && PathCommands.Keyword(ed, $"\n오류 {errors}건이 있습니다. 계속할까요? [Y/N] <N>", "N", "Y", "N") != "Y") return false;
            ModelBuildResult r;
            try
            {
                // the server's SourceBuild makes the model from the same rules: Excel groups win over ash.settings.tsv Groups, assets kept in <model>.arc\assets
                var options = SourceBuild.Options(input, s);
                Ui.PaletteStatus.Set("AGVMODEL 모델 생성 중…");
                var assetWarnings = new List<string>();
                var assetClient = AssetClient.Open(UserSettings.Load().ServerUrl); // one per run: its asset list is read once
                bool wantsAssets = ModelBuilder.IsValidModelName(s.ModelName) && input.AgvTypes.Any(t => t.Asset.Length > 0);
                if (wantsAssets && assetClient == null) assetWarnings.Add("서버 주소가 설정되지 않아 에셋 그림을 쓰지 않습니다(AGVSERVER). 기본 그림을 씁니다.");
                var ov = wantsAssets && assetClient != null
                    ? SourceBuild.Overrides(input, Path.Combine(dir, s.ModelName + ".arc"), assetClient.FetchCel, assetWarnings)
                    : new Dictionary<string, VehicleTemplateOverride>();
                foreach (var w in assetWarnings) ed.WriteMessage("\n경고: " + w);
                r = ModelBuilder.Build(new ModelBuildRequest(dir, s.ModelName, input, layout, options, VehicleOverrides: ov.Count > 0 ? ov : null), null, TimeSpan.FromMinutes(10));
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
            MarkExcel(ed, xlsx, r.Findings);
            if (r.Export != null) ExportCommands.Report(ed, r.Export);
            ed.WriteMessage($"\narc: {r.ArcDir}");
            if (r.Findings.Any(f => f.Code == FindingCode.InputError)) ed.WriteMessage("\n입력 오류가 있어 모델을 만들지 않았습니다.");
            else
            {
                ed.WriteMessage(hasAutoMod
                    ? "\n모델 파일을 만들었습니다(빌드는 하지 않음). 이 PC의 AutoMod로 보려면 '빌드·실행'(AGVRUN), 대수 탐색은 '서버로 보내기'로 서버에서 합니다."
                    : "\n모델 파일을 만들었습니다. 이 PC에는 AutoMod가 없어 빌드와 대수 탐색은 서버가 서버의 AutoMod로 합니다.");
                toServer = true;
            }
            PathCommands.FinalStatus = toServer ? "AGVMODEL 모델 생성 완료" : "AGVMODEL 오류";
            return false; // nothing in the drawing changed
        }, refreshArrows: false, unitsPerMeter: UnitScale.FromSettings(s));
        // asks before sending (with the server's queue); No keeps the files here. With AutoMod here and no server set, it just stays here
        if (toServer && (!hasAutoMod || UserSettings.TryNormalizeUrl(UserSettings.Load().ServerUrl, out _))) ServerCommands.Command("AGVMODEL", results: false);
    }

    private static void BuildFailed(Editor ed, AmBuildResult b)
    {
        ed.WriteMessage("\n빌드 실패");
        foreach (var t in new[] { b.MakeErrText, b.Am2ErrText })
            foreach (var line in (t ?? "").Split('\n').Take(20)) ed.WriteMessage("\n  " + line.TrimEnd());
    }

    /// <summary>Opens the model in AutoMod's simulation window to watch it run, building it first when &lt;Model&gt;.dir is missing or older than the .arc files.
    /// AutoMod runs on its own; the command returns as soon as it started. No drawing is read: the model is what the last AGVMODEL wrote.</summary>
    [CommandMethod("AGVRUN")]
    public void AgvRun()
    {
        var ed = Application.DocumentManager.MdiActiveDocument.Editor;
        if (!ExportCommands.TryLoadSettings(ed, out var s, out var dir)) return;
        void Say(string text) { ed.WriteMessage("\n" + text); Ui.PaletteStatus.Set(text); }
        if (AmBuilder.FindBuildExe(s.AmHome) == null)
        {
            ed.WriteMessage($"\nAutoMod를 찾지 못했습니다: {AmBuilder.BuildExeCandidates(s.AmHome)}");
            Say("이 PC에는 AutoMod가 없어 실행할 수 없습니다. '서버로 보내기'(AGVSEND)로 서버에서 빌드·대수 탐색을 하세요(AutoMod가 있으면 설정에서 AutoMod 설치 폴더를 고치세요).");
            return;
        }
        // the name ends up in a recursive delete of <Model>.dir: never let it point outside the drawing folder
        if (!ModelBuilder.IsValidModelName(s.ModelName)) { Say($"모델 이름 '{s.ModelName}'을(를) 쓸 수 없습니다. 설정에서 고치세요."); return; }
        if (!Directory.Exists(Path.Combine(dir, s.ModelName + ".arc"))) { Say($"{s.ModelName}.arc가 없습니다. 먼저 '모델 생성'(AGVMODEL)을 하세요."); return; }
        try
        {
            if (!AmBuilder.IsBuilt(dir, s.ModelName))
            {
                Ui.PaletteStatus.Set("AGVRUN 빌드 중…");
                ed.WriteMessage("\nAutoMod 빌드 중… 최대 10분");
                ModelBuilder.PrepareBuildDir(dir, s.ModelName);
                var b = AmBuilder.Build(s.AmHome, dir, s.ModelName, TimeSpan.FromMinutes(10));
                if (!b.Success) { BuildFailed(ed, b); Ui.PaletteStatus.Set("AGVRUN 빌드 실패"); return; }
            }
            AmBuilder.Launch(s.AmHome, dir, s.ModelName).Dispose(); // not waited for: ZWCAD stays usable while the model runs
            Say($"AutoMod에서 {s.ModelName}을(를) 엽니다. 도면이나 Excel을 고쳤으면 먼저 '모델 생성'을 다시 하세요.");
        }
        catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
        {
            Say($"AGVRUN 오류: {ex.Message} (AutoMod에서 이 모델이 실행 중이면 닫고 다시 하세요)");
            PluginLog.Write("AGVRUN", ex);
        }
    }

    /// <summary>input.xlsx as a ProjectInput; null (after saying why) when it cannot be had. The file is read directly; when that fails (company document encryption
    /// leaves only Excel able to read it, or Excel has it locked) Excel is asked for the cells. Whichever way it was read, the cells are also written to
    /// input.cells.tsv beside the workbook so ash-run can still read them.</summary>
    internal static ProjectInput? LoadInput(Editor ed, string xlsx, string dir)
    {
        ClosedXML.Excel.XLWorkbook? wb = null;
        try
        {
            try { wb = ExcelReader.Open(xlsx); }
            catch (System.Exception ex) when (!(ex is OperationCanceledException))
            {
                ed.WriteMessage("\nxlsx를 직접 읽지 못해(" + ex.GetType().Name + ") Excel로 읽습니다…");
                PluginLog.Write("AGVMODEL direct read failed", ex);
                try
                {
                    wb = ExcelCom.Read(xlsx, out var note);
                    if (note.Length > 0) ed.WriteMessage("\n" + note);
                }
                catch (System.Exception cex) when (!(cex is OperationCanceledException))
                {
                    ed.WriteMessage("\nExcel 읽기 오류: " + ex.Message);
                    ed.WriteMessage("\nExcel로 읽기도 실패했습니다: " + cex.GetType().Name + ": " + cex.Message.Split('\n')[0].Trim());
                    foreach (var line in ExcelDiagnosis.Explain(xlsx, ex)) ed.WriteMessage("\n  " + line);
                    PluginLog.Write("AGVMODEL Excel (COM) read failed", cex);
                    foreach (var line in ExcelDiagnosis.Inspect(xlsx)) PluginLog.Write("  " + line);
                    return null;
                }
            }
            ProjectInput input;
            try
            {
                input = ExcelReader.Read(wb, out var xw);
                foreach (var w in xw) ed.WriteMessage("\nExcel 경고: " + w.Message);
            }
            catch (System.Exception ex) when (ex is FormatException || ex is InvalidOperationException || ex is ArgumentException)
            {
                ed.WriteMessage("\nExcel 읽기 오류: " + ex.Message); // names sheet, row and column when a value is the problem
                return null;
            }
            try { CellDump.Write(wb, Path.Combine(dir, CellDump.FileName)); }
            catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { ed.WriteMessage("\n경고: 셀 스냅샷(" + CellDump.FileName + ")을 쓰지 못했습니다. ash-run이 암호화된 input.xlsx를 읽지 못할 수 있습니다: " + ex.Message); }
            return input;
        }
        finally { wb?.Dispose(); }
    }

    private static bool Fail() { PathCommands.FinalStatus = "AGVMODEL 오류"; return false; }
}
