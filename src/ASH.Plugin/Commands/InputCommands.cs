using System;
using System.Collections.Generic;
using System.IO;
using ASH.Core.Input;
using ASH.Plugin.Cad;
using ASH.Plugin.Ui;
using ClosedXML.Excel;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Runtime;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ASH.Plugin.Commands.InputCommands))]

namespace ASH.Plugin.Commands;

/// <summary>AGVINPUT (also AGVSUPPLY): the supply settings table, "공급 설정" (<see cref="InputDialog"/>), to fill input.xlsx inside the plugin. The drawing is read once for its CP names (drop-downs, checks);
/// the workbook is read once and written once per save. A plain file is read and written with ClosedXML (Excel holding it open sends the save to Excel's open copy);
/// a company-encrypted file only Excel can open, so one hidden Excel serves the whole time the table is up: one read when it opens, one write per save.</summary>
public sealed class InputCommands
{
    [CommandMethod("AGVINPUT")]
    public void AgvInput() => Open();

    /// <summary>The same table under the name the dialog goes by ("공급 설정").</summary>
    [CommandMethod("AGVSUPPLY")]
    public void AgvSupply() => Open();

    /// <summary>Opens the input table; also what a palette button calls.</summary>
    internal static void Open()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor;
        void Say(string text) { ed.WriteMessage("\n" + text); PaletteStatus.Set(text); }
        try
        {
            if (!ExportCommands.TryLoadSettings(ed, out _, out var dir)) return;
            var xlsx = Path.Combine(dir, ModelCommands.ExcelFileName);
            if (!File.Exists(xlsx)) { Say($"{ModelCommands.ExcelFileName}이 없습니다. 먼저 AGVEXCEL로 양식을 만드세요."); return; }

            ASH.Core.Layout.Layout? layout = null;
            PathCommands.Run(doc, "AGVINPUT", (db, tr) =>
            {
                using (PluginLog.Time("AGVINPUT 도면 읽기")) layout = DrawingReader.Read(db, tr);
                PathCommands.FinalStatus = "AGVINPUT: 도면 읽음";
                return false; // nothing in the drawing changed
            }, refreshArrows: false);
            if (layout == null) return; // Run said why

            using var excel = ExcelCom.Session(); // an encrypted file: the hidden Excel of the read stays up for every save, until the table closes
            XLWorkbook wb;
            string note;
            try { wb = ReadFile(xlsx, out note); }
            catch (System.Exception ex) when (!(ex is OperationCanceledException))
            {
                ed.WriteMessage("\nExcel 읽기 실패: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0].Trim());
                foreach (var line in ExcelDiagnosis.Explain(xlsx, ex)) ed.WriteMessage("\n  " + line);
                PluginLog.Write("AGVINPUT read failed", ex);
                PaletteStatus.Set("AGVINPUT 오류");
                return;
            }
            if (InputTable.LoadAll(wb).Count == 0)
            {
                wb.Dispose();
                Say($"{ModelCommands.ExcelFileName}에 입력 시트가 없습니다. AGVEXCEL의 U(갱신)로 양식을 채우세요.");
                return;
            }
            Say("AGVINPUT: 공급 설정");
            int saved;
            using (var dlg = new InputDialog(xlsx, wb, InputTable.CpNamesByType(layout), CpNames(layout), edits => Write(xlsx, edits), () => ReadFile(xlsx, out _), file => ReadFile(file, out _), note))
            {
                Application.ShowModalDialog(dlg);
                saved = dlg.SavedCells;
            }
            Say(saved > 0 ? $"AGVINPUT: {saved}칸 저장 ({ModelCommands.ExcelFileName})" : "AGVINPUT: 닫음");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nAGVINPUT 오류: {ex.GetType().Name}: {ex.Message}");
            PluginLog.Write("AGVINPUT failed", ex);
            PaletteStatus.Set("AGVINPUT 오류: " + ex.Message);
        }
    }

    static List<string> CpNames(ASH.Core.Layout.Layout layout)
    {
        var names = new List<string>();
        foreach (var c in layout.Cps) names.Add(c.Name);
        return names;
    }

    /// <summary>The workbook: a plain file directly; an encrypted one (or one the direct read fails on, such as an Excel that has it locked) through Excel, whose
    /// copy in this session is the one <see cref="Write"/> edits.</summary>
    static XLWorkbook ReadFile(string xlsx, out string note)
    {
        note = "";
        if (!ExcelCom.Encrypted(xlsx))
            try { using (PluginLog.Time("AGVINPUT 직접 읽기")) return ExcelReader.Open(xlsx); }
            catch (System.Exception ex) when (!(ex is OperationCanceledException)) { PluginLog.Write("AGVINPUT direct read failed, Excel reads", ex); }
        using (PluginLog.Time("AGVINPUT Excel로 읽기")) return ExcelCom.Read(xlsx, out note);
    }

    /// <summary>Saves the cell edits: a plain file with ClosedXML; when that fails (Excel has the file open and locked) or the file is encrypted, Excel makes
    /// the same edits in its copy and saves (the encryption stays on).</summary>
    static void Write(string xlsx, IReadOnlyList<CellEdit> edits)
    {
        if (!ExcelCom.Encrypted(xlsx))
            try { using (PluginLog.Time($"AGVINPUT 직접 저장({edits.Count}칸)")) InputTable.SaveFile(xlsx, edits); return; }
            catch (System.Exception ex) when (!(ex is OperationCanceledException)) { PluginLog.Write("AGVINPUT direct save failed, Excel saves", ex); }
        using (PluginLog.Time($"AGVINPUT Excel로 저장({edits.Count}칸)")) ExcelCom.Apply(xlsx, edits);
    }
}
