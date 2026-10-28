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

    // Begin..End (one command): a hidden Excel and the files it opened stay up between Read and Apply, so a command that reads, edits, reads again and marks
    // starts Excel and decrypts the file once instead of every time. Commands run on ZWCAD's main thread only.
    static int _depth;
    static dynamic? _app;
    static int _appPid;
    static readonly System.Collections.Generic.Dictionary<string, object> _books = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Keeps the hidden Excel of the following Read/Apply calls until the matching <see cref="End"/>. Nested pairs share the outer one.</summary>
    public static void Begin() => _depth++;

    /// <summary><see cref="Begin"/> now, <see cref="End"/> on Dispose (`using var`).</summary>
    public static IDisposable Session() { Begin(); return new Ender(); }

    sealed class Ender : IDisposable { bool done; public void Dispose() { if (!done) { done = true; End(); } } }

    /// <summary>Closes what <see cref="Begin"/> kept (unsaved: Apply saves itself) once the outermost pair ends. Must run before anything else opens the file
    /// (AGVEXCEL hands it to the user's Excel next). Never throws.</summary>
    public static void End()
    {
        if (_depth == 0 || --_depth > 0) return;
        foreach (var b in _books.Values) try { ((dynamic)b).Close(false); } catch (COMException) { }
        try { _app?.Quit(); } catch (COMException) { }
        foreach (var o in new System.Collections.Generic.List<object?>(_books.Values) { _app })
            try { if (o != null && Marshal.IsComObject(o)) Marshal.FinalReleaseComObject(o); } catch (ArgumentException) { }
        _books.Clear(); _app = null;
        GC.Collect(); GC.WaitForPendingFinalizers();
        if (_appPid != 0) KillIfStillThere(_appPid);
        _appPid = 0;
    }

    /// <summary>Cell values of every sheet. If Excel already has the file open (the usual case: the user is still editing it) that copy is read, unsaved edits
    /// included, and <paramref name="note"/> says so; otherwise the file is opened read-only in a hidden Excel that is closed afterwards.</summary>
    public static XLWorkbook Read(string path, out string note)
    {
        string said = "";
        var result = With(path, readOnly: true, (wb, open) =>
        {
            if (open && !(bool)wb.Saved) said = "Excel에서 저장하지 않은 변경까지 포함해 읽었습니다.";
            return Copy(wb);
        });
        note = said;
        return result;
    }

    /// <summary>Makes <paramref name="edits"/> (ExcelTemplate.Update's log, made on what <see cref="Read"/> returned) to the file through Excel and saves it,
    /// so the document encryption stays on. A copy the user has open in Excel is edited and saved in place (they see the changes); otherwise a hidden Excel does it.</summary>
    public static void Apply(string path, System.Collections.Generic.IReadOnlyList<ExcelEdit> edits) => With(path, readOnly: false, (wb, open) =>
    {
        // every call crosses into Excel's process: no repaint, events or recalculation per edit (the user's open window repaints once at the end)
        dynamic app = wb.Application;
        bool screen = true, events = true; object? calc = null;
        try { screen = app.ScreenUpdating; events = app.EnableEvents; calc = app.Calculation; app.ScreenUpdating = false; app.EnableEvents = false; app.Calculation = -4135; } // xlCalculationManual
        catch (COMException) { }
        try { Edit(wb, edits); }
        finally
        {
            try { if (calc != null) app.Calculation = calc; app.EnableEvents = events; app.ScreenUpdating = screen; } catch (COMException) { }
        }
        wb.Save();
        return 0;
    });

    static object Cell(string v) => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var num) ? num : (object)v;

    static void Edit(dynamic wb, System.Collections.Generic.IReadOnlyList<ExcelEdit> edits)
    {
        for (int i = 0; i < edits.Count; i++)
        {
            var e = edits[i];
            if (e is AddSheetEdit ase) { dynamic last = wb.Worksheets[wb.Worksheets.Count]; dynamic ws = wb.Worksheets.Add(Type.Missing, last); ws.Name = e.Sheet; if (ase.Hidden) ws.Visible = 0; continue; }
            dynamic sheet = wb.Worksheets[e.Sheet];
            switch (e)
            {
                case DeleteColumnEdit d: sheet.Columns[d.Col].Delete(); break;
                case CellEdit c:
                    // a run of edits on the next cells of the same row (an appended row) goes in one Range.Value2
                    int n = 1;
                    while (i + n < edits.Count && edits[i + n] is CellEdit x && x.Sheet == c.Sheet && x.Row == c.Row && x.Col == c.Col + n) n++;
                    if (n == 1) { sheet.Cells[c.Row, c.Col].Value2 = Cell(c.Value); break; }
                    var row = new object[1, n];
                    for (int k = 0; k < n; k++) row[0, k] = Cell(((CellEdit)edits[i + k]).Value);
                    sheet.Range[sheet.Cells[c.Row, c.Col], sheet.Cells[c.Row, c.Col + n - 1]].Value2 = row;
                    i += n - 1;
                    break;
                case FormulaEdit f: sheet.Range[sheet.Cells[f.FirstRow, f.Col], sheet.Cells[f.LastRow, f.Col]].FormulaR1C1 = f.FormulaR1C1; break;
                case FillEdit fe: // Interior.Color is BGR
                    sheet.Range[sheet.Cells[fe.FirstRow, fe.Col], sheet.Cells[fe.LastRow, fe.Col]].Interior.Color = ((fe.Rgb & 0xFF) << 16) | (fe.Rgb & 0xFF00) | (fe.Rgb >> 16);
                    break;
                case AlignEdit a: sheet.Range[sheet.Columns[a.FirstCol], sheet.Columns[a.LastCol]].HorizontalAlignment = -4108; break; // xlCenter
                case ListEdit l:
                    dynamic v = sheet.Range[sheet.Cells[l.FirstRow, l.Col], sheet.Cells[l.LastRow, l.Col]].Validation;
                    v.Delete();
                    if (l.Formula.Length == 0) break; // no drop-down
                    v.Add(3, l.Warn ? 2 : 1, 1, l.Formula); // xlValidateList, xlValidAlertWarning/Stop, xlBetween
                    v.IgnoreBlank = true; v.InCellDropdown = true; v.ShowError = true; v.ErrorTitle = l.Title; v.ErrorMessage = l.Message;
                    break;
                case ClearMarksEdit:
                    // Excel does its own search: a copy read through Excel carries no comments. Newest first, as deleting shifts the index.
                    for (int k = (int)sheet.Comments.Count; k >= 1; k--)
                    {
                        dynamic cm = sheet.Comments.Item(k);
                        if (((string)cm.Text()).StartsWith(ExcelMarks.Prefix, StringComparison.Ordinal)) { cm.Parent.Interior.ColorIndex = -4142; cm.Delete(); } // xlColorIndexNone
                    }
                    break;
                case MarkEdit m:
                    dynamic cell = sheet.Cells[m.Row, m.Col];
                    cell.Interior.Color = ((m.Rgb & 0xFF) << 16) | (m.Rgb & 0xFF00) | (m.Rgb >> 16);
                    if (cell.Comment == null) cell.AddComment(m.Text); // the user's own comment stays
                    break;
            }
        }
    }

    /// <summary>Runs <paramref name="work"/> on the workbook: the copy a running Excel has open (open = true), else the file opened in a hidden Excel that is
    /// closed afterwards, unsaved.</summary>
    static T With<T>(string path, bool readOnly, Func<dynamic, bool, T> work)
    {
        var full = Path.GetFullPath(path);
        dynamic? running = null, app = null, wb = null;
        bool ownApp = false, ownBook = false;
        int pid = 0;
        try
        {
            // the file this command already opened in the kept hidden Excel: straight to it (asking the running Excel below could hand back that same Excel)
            if (_depth > 0 && _books.TryGetValue(full, out var already)) return work((dynamic)already, false);
            try { running = Marshal.GetActiveObject("Excel.Application"); } catch (COMException) { running = null; }
            // the kept hidden Excel may be the one the system lists as running: it is not the user's, and releasing it below would cut it off for the rest of the command
            if (running != null && _app != null && ReferenceEquals((object)running, (object)_app)) running = null;
            if (running != null)
                foreach (dynamic b in running.Workbooks)
                {
                    string name = b.FullName;
                    if (string.Equals(name, full, StringComparison.OrdinalIgnoreCase)) { wb = b; app = running; break; }
                }
            if (wb == null && _depth > 0)
            {
                // inside Begin..End: the kept hidden Excel and its copy of the file (opened for writing, so Read and Apply share it); End closes them
                var sw = Stopwatch.StartNew();
                if (!_books.TryGetValue(full, out var kept))
                {
                    if (_app == null) _app = NewHidden(out _appPid);
                    _books[full] = kept = _app!.Workbooks.Open(full, 0, false);
                    PluginLog.Write($"ExcelCom: {Path.GetFileName(full)} opened in {sw.ElapsedMilliseconds} ms (kept for this command)");
                }
                return work((dynamic)kept, false);
            }
            if (wb == null)
            {
                app = NewHidden(out pid); ownApp = true;
                wb = app.Workbooks.Open(full, 0, readOnly); ownBook = true; // UpdateLinks 0
            }
            return work(wb, !ownBook);
        }
        finally
        {
            try { if (ownBook && wb != null) wb.Close(false); } catch (COMException) { }
            try { if (ownApp && app != null) app.Quit(); } catch (COMException) { }
            foreach (object? o in new object?[] { wb, app, running })
                try { if (o != null && Marshal.IsComObject(o)) Marshal.FinalReleaseComObject(o); } catch (ArgumentException) { }
            wb = null; app = null; running = null;
            GC.Collect(); GC.WaitForPendingFinalizers(); // the sheet and range wrappers made while working still hold Excel
            if (pid != 0) KillIfStillThere(pid);
        }
    }

    static dynamic NewHidden(out int pid)
    {
        var type = Type.GetTypeFromProgID("Excel.Application") ?? throw new InvalidOperationException("Excel이 설치되어 있지 않습니다(Excel.Application을 찾을 수 없음).");
        dynamic app = Activator.CreateInstance(type)!;
        app.Visible = false; app.DisplayAlerts = false; app.AskToUpdateLinks = false;
        try { GetWindowThreadProcessId(new IntPtr((int)app.Hwnd), out var p); pid = (int)p; } catch (Exception e) { pid = 0; PluginLog.Write("ExcelCom: no Excel process id", e); } // to make sure it is gone afterwards
        return app;
    }

    static XLWorkbook Copy(dynamic wb)
    {
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
