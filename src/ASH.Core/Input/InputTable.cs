using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ClosedXML.Excel;

namespace ASH.Core.Input;

/// <summary>One column of an input table: a non-blank row-1 header of the sheet (<see cref="Number"/> = its column in the sheet) and what <see cref="InputSchema"/>
/// knows about it. A header the schema does not know (an older or edited workbook) is a plain free-text column.</summary>
public sealed record TableColumn(int Number, string Name, InputSchema.Column? Schema)
{
    public string Label => Schema?.Label ?? Name;
    public bool Auto => Schema?.Auto ?? false;
    public bool Advanced => Schema?.Advanced ?? false;
}

/// <summary>One row of an input table. <see cref="Row"/> is its Excel row; 0 = a row added in the table that is not in the file yet.</summary>
public sealed class TableRow
{
    public int Row;
    public string[] Values;
    public TableRow(int row, string[] values) { Row = row; Values = values; }
}

/// <summary>
/// One sheet of input.xlsx as the plugin's input table (AGVINPUT) shows it: the sheet's own row-1 headers as columns (never a fixed list: the template's columns
/// change between versions), the data rows from <see cref="InputSchema.FirstDataRow"/> as text, and the Excel row of each. Pure logic over a workbook in
/// memory, so the same code serves a file ClosedXML reads and a copy read out of a running Excel (company-encrypted files).
/// </summary>
public sealed class InputTable
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public string Sheet { get; }
    public IReadOnlyList<TableColumn> Columns { get; }
    public List<TableRow> Rows { get; }
    /// <summary>The Excel row where the first appended row goes: after the last typed value of the sheet (formula cells such as the helper totals do not count).</summary>
    public int NextRow { get; }

    public InputTable(string sheet, IReadOnlyList<TableColumn> columns, List<TableRow> rows, int nextRow)
    {
        Sheet = sheet; Columns = columns; Rows = rows; NextRow = nextRow;
    }

    /// <summary>The same columns with other rows (what the table holds after the user edited it).</summary>
    public InputTable With(List<TableRow> rows) => new InputTable(Sheet, Columns, rows, NextRow);

    /// <summary>The sheet as a table; null when the workbook has no such sheet. Rows with nothing typed in the non-Auto columns are left out.</summary>
    public static InputTable? Load(XLWorkbook wb, string sheet)
    {
        if (!wb.TryGetWorksheet(sheet, out var ws)) return null;
        var schema = new Dictionary<string, InputSchema.Column>(StringComparer.Ordinal);
        foreach (var c in InputSchema.Of(sheet)) if (!schema.ContainsKey(c.Name)) schema[c.Name] = c;
        var cols = new List<TableColumn>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (int c = 1; c <= lastCol; c++)
        {
            var h = ws.Cell(1, c).GetString().Trim();
            if (h.Length > 0 && seen.Add(h)) cols.Add(new TableColumn(c, h, schema.TryGetValue(h, out var s) ? s : null));
        }
        int last = LastDataRow(ws);
        var rows = new List<TableRow>();
        for (int r = InputSchema.FirstDataRow; r <= last; r++)
        {
            var values = new string[cols.Count];
            bool any = false;
            for (int i = 0; i < cols.Count; i++)
            {
                values[i] = Text(ws.Cell(r, cols[i].Number));
                if (values[i].Length > 0 && !cols[i].Auto) any = true;
            }
            if (any) rows.Add(new TableRow(r, values));
        }
        return new InputTable(sheet, cols, rows, Math.Max(InputSchema.FirstDataRow, last + 1));
    }

    /// <summary>The tables of <see cref="InputSchema.TableSheets"/> the workbook has, in tab order.</summary>
    public static List<InputTable> LoadAll(XLWorkbook wb) =>
        InputSchema.TableSheets.Select(s => Load(wb, s)).Where(t => t != null).Select(t => t!).ToList();

    /// <summary>A cell as the table shows it: numbers in invariant text (never the machine's decimal comma), a formula cell as blank (its value is Excel's to
    /// compute; reading it here would run ClosedXML's formula engine on a cell with no stored result).</summary>
    static string Text(IXLCell c)
    {
        if (c.HasFormula || c.IsEmpty()) return "";
        return c.DataType == XLDataType.Number ? c.GetDouble().ToString("R", Inv) : c.GetString().Trim();
    }

    static int LastDataRow(IXLWorksheet ws) =>
        ws.CellsUsed(XLCellsUsedOptions.Contents).Where(c => !c.HasFormula && c.GetString().Length > 0).Select(c => c.Address.RowNumber).DefaultIfEmpty(0).Max();

    /// <summary>The edits that turn this table (as read) into <paramref name="edited"/>: a changed or cleared cell (cleared = ""), and every non-empty row added at the
    /// end (Row 0) as its cells, on the rows after <see cref="NextRow"/>. Auto columns are never written. Sorted by row, then column, so that a run of cells in one
    /// row can be written in one go. A row added and left empty writes nothing. Appended rows that hold something get their Excel row in place (the caller maps
    /// them back to its grid); calling this again on the same <paramref name="edited"/> gives the same edits.</summary>
    public List<CellEdit> Diff(InputTable edited)
    {
        var was = new Dictionary<int, TableRow>();
        foreach (var r in Rows) was[r.Row] = r;
        var edits = new List<CellEdit>();
        int next = NextRow;
        foreach (var er in edited.Rows)
        {
            string Now(int i) => i < er.Values.Length ? (er.Values[i] ?? "").Trim() : "";
            if (er.Row > 0)
            {
                was.TryGetValue(er.Row, out var old);
                for (int i = 0; i < Columns.Count; i++)
                {
                    if (Columns[i].Auto) continue;
                    var before = old != null && i < old.Values.Length ? old.Values[i] : "";
                    if (!Same(before, Now(i))) edits.Add(new CellEdit(Sheet, er.Row, Columns[i].Number, Now(i)));
                }
                continue;
            }
            var filled = Enumerable.Range(0, Columns.Count).Where(i => !Columns[i].Auto && Now(i).Length > 0).ToList();
            if (filled.Count == 0) continue;
            er.Row = next++;
            foreach (var i in filled) edits.Add(new CellEdit(Sheet, er.Row, Columns[i].Number, Now(i)));
        }
        return edits.OrderBy(e => e.Row).ThenBy(e => e.Col).ToList();
    }

    // "1.0" typed over a number cell 1 is no change; "007" typed as "7" over a text cell is (the old text is not the number's own text)
    static bool Same(string before, string now) =>
        before == now || (double.TryParse(now, NumberStyles.Float, Inv, out var d) && before == d.ToString("R", Inv));

    /// <summary>Makes the cell edits to a workbook in memory (a numeric text becomes a number, "" clears the cell), the way <c>ExcelCom.Apply</c> makes them in Excel.</summary>
    public static void Apply(XLWorkbook wb, IEnumerable<CellEdit> edits)
    {
        foreach (var e in edits)
        {
            if (!wb.TryGetWorksheet(e.Sheet, out var ws)) continue;
            var cell = ws.Cell(e.Row, e.Col);
            if (e.Value.Length == 0) cell.Clear(XLClearOptions.Contents);
            else if (double.TryParse(e.Value, NumberStyles.Float, Inv, out var d) && !double.IsNaN(d) && !double.IsInfinity(d)) cell.Value = d;
            else cell.Value = e.Value;
        }
    }

    /// <summary>Opens <paramref name="path"/> with ClosedXML, makes the edits and saves it atomically (<see cref="ExcelTemplate.Save"/>). Throws like opening and
    /// saving a workbook do (encrypted file, or Excel holds it): the caller falls back to Excel.</summary>
    public static void SaveFile(string path, IReadOnlyList<CellEdit> edits)
    {
        using var wb = ExcelReader.Open(path);
        Apply(wb, edits);
        ExcelTemplate.Save(wb, path);
    }

    /// <summary>A workbook with the values of <paramref name="original"/> (every sheet, no formulas, formats or lists) and then the edits: what the file would hold
    /// after saving them. A copy, so the original stays as read and any number of what-if edits can be tried on it.</summary>
    public static XLWorkbook Overlay(XLWorkbook original, IEnumerable<CellEdit> edits)
    {
        var wb = ExcelReader.NewWorkbook();
        foreach (var src in original.Worksheets)
        {
            if (src.Name.StartsWith("_", StringComparison.Ordinal)) continue; // the hidden helper sheet: nothing reads it
            var ws = wb.AddWorksheet(src.Name);
            foreach (var c in src.CellsUsed(XLCellsUsedOptions.Contents))
            {
                if (c.HasFormula) continue;
                var to = ws.Cell(c.Address.RowNumber, c.Address.ColumnNumber);
                if (c.DataType == XLDataType.Number) to.Value = c.GetDouble();
                else { var s = c.GetString(); if (s.Length > 0) to.Value = s; }
            }
        }
        Apply(wb, edits);
        return wb;
    }

    /// <summary>The drawing's CP names by CP type (name order: L2 before L10), for the drop-down cells.</summary>
    public static Dictionary<string, List<string>> CpNamesByType(ASH.Core.Layout.Layout layout) =>
        layout.Cps.Where(c => c.Name.Length > 0).GroupBy(c => c.Type)
            .ToDictionary(g => g.Key, g => ExcelTemplate.ByName(g.Select(c => c.Name).Distinct(StringComparer.Ordinal)));
}
