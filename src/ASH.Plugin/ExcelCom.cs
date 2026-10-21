using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using ASH.Core.Input;
using ClosedXML.Excel;

namespace ASH.Plugin;

/// <summary>
/// Reads input.xlsx through Excel itself. Company document encryption (NASCA) encrypts the file when the user saves it, and only Excel is let through to
/// decrypt it, so ClosedXML sees noise. The cells are copied into an in-memory workbook that the normal reader parses, so the checks and error messages
/// are the same as for a plain file. Late bound (dynamic): no Office assemblies to ship, any installed Excel version works.
/// </summary>
internal static class ExcelCom
{
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>Cell values of every sheet. If Excel already has the file open (the usual case: the user is still editing it) that copy is read, unsaved edits
    /// included, and <paramref name="note"/> says so; otherwise the file is opened read-only in a hidden Excel that is closed afterwards.</summary>
    public static XLWorkbook Read(string path, out string note)
    {
        note = "";
        var full = Path.GetFullPath(path);
        dynamic? running = null, opened = null, app = null, wb = null;
        bool ownApp = false, ownBook = false;
        int pid = 0;
        try
        {
            try { running = Marshal.GetActiveObject("Excel.Application"); } catch (COMException) { running = null; }
            if (running != null)
            {
                foreach (dynamic b in running.Workbooks)
                {
                    string name = b.FullName;
                    if (string.Equals(name, full, StringComparison.OrdinalIgnoreCase)) { wb = b; app = running; break; }
                }
                if (wb != null && !(bool)wb.Saved) note = "Excel에서 저장하지 않은 변경까지 포함해 읽었습니다.";
            }
            if (wb == null)
            {
                var type = Type.GetTypeFromProgID("Excel.Application") ?? throw new InvalidOperationException("Excel이 설치되어 있지 않습니다(Excel.Application을 찾을 수 없음).");
                app = Activator.CreateInstance(type); ownApp = true;
                app.Visible = false; app.DisplayAlerts = false; app.AskToUpdateLinks = false;
                try { GetWindowThreadProcessId(new IntPtr((int)app.Hwnd), out var p); pid = (int)p; } catch (Exception e) { pid = 0; PluginLog.Write("ExcelCom: no Excel process id", e); } // to make sure it is gone afterwards
                wb = app.Workbooks.Open(full, 0, true); ownBook = true; // UpdateLinks 0, ReadOnly
            }
            var result = ExcelReader.NewWorkbook();
            foreach (dynamic sheet in wb.Worksheets)
            {
                var ws = result.AddWorksheet((string)sheet.Name);
                object? used = sheet.UsedRange.Value2;
                if (used == null) continue;
                if (used is object[,] cells)
                {
                    int r0 = cells.GetLowerBound(0), c0 = cells.GetLowerBound(1);
                    dynamic start = sheet.UsedRange;
                    int top = (int)start.Row, left = (int)start.Column;
                    for (int r = 0; r < cells.GetLength(0); r++)
                        for (int c = 0; c < cells.GetLength(1); c++)
                            Put(ws.Cell(top + r, left + c), cells[r0 + r, c0 + c]);
                }
                else
                {
                    dynamic start = sheet.UsedRange;
                    Put(ws.Cell((int)start.Row, (int)start.Column), used);
                }
            }
            return result;
        }
        finally
        {
            try { if (ownBook && wb != null) wb.Close(false); } catch (COMException) { }
            try { if (ownApp && app != null) app.Quit(); } catch (COMException) { }
            foreach (object? o in new object?[] { wb, app, running })
                try { if (o != null && Marshal.IsComObject(o)) Marshal.FinalReleaseComObject(o); } catch (ArgumentException) { }
            wb = null; app = null; running = null;
            GC.Collect(); GC.WaitForPendingFinalizers(); // the sheet and range wrappers made while reading still hold Excel
            if (pid != 0) KillIfStillThere(pid);
        }
    }

    // Excel that was started here must not stay behind as an invisible process, whatever state Quit left it in
    static void KillIfStillThere(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            if (!proc.WaitForExit(3000) && string.Equals(proc.ProcessName, "EXCEL", StringComparison.OrdinalIgnoreCase))
            {
                proc.Kill();
                PluginLog.Write("ExcelCom: Excel (pid " + pid + ") did not exit after Quit, ended it");
            }
        }
        catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is System.ComponentModel.Win32Exception) { }
    }

    static void Put(IXLCell cell, object? v)
    {
        switch (v)
        {
            case null: return;
            case double d: cell.Value = d; return;
            case bool b: cell.Value = b ? "TRUE" : "FALSE"; return;
            case string s: if (s.Length > 0) cell.Value = s; return;
            case int e: cell.Value = ErrorText(e); return; // Value2 gives a cell error as its numeric code
            default: cell.Value = Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""; return;
        }
    }

    static string ErrorText(int code)
    {
        switch (code)
        {
            case -2146826281: return "#DIV/0!";
            case -2146826246: return "#N/A";
            case -2146826259: return "#NAME?";
            case -2146826288: return "#NULL!";
            case -2146826252: return "#NUM!";
            case -2146826265: return "#REF!";
            case -2146826273: return "#VALUE!";
            default: return "#ERROR(" + code.ToString(CultureInfo.InvariantCulture) + ")";
        }
    }
}
