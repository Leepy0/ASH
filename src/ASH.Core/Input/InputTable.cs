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

    string Cell(TableRow? r, int i) => r != null && i < r.Values.Length ? (r.Values[i] ?? "").Trim() : "";

    /// <summary>The Excel row each row of <paramref name="edited"/> has once saved (same order). A row of this table that <paramref name="edited"/> no longer holds is
    /// deleted: the rows below it move up (as Excel's Delete row does). A row added at the end (Row 0) that holds something goes after the last row left; an added row
    /// left empty gets 0 (it is not written).</summary>
    public int[] Positions(InputTable edited)
    {
        var kept = new HashSet<int>(edited.Rows.Where(r => r.Row > 0).Select(r => r.Row));
        var gone = Rows.Select(r => r.Row).Where(r => !kept.Contains(r)).OrderBy(r => r).ToArray();
        int next = NextRow - gone.Length;
        var pos = new int[edited.Rows.Count];
        for (int k = 0; k < pos.Length; k++)
        {
            var er = edited.Rows[k];
            if (er.Row > 0) { int below = 0; while (below < gone.Length && gone[below] < er.Row) below++; pos[k] = er.Row - below; }
            else pos[k] = Enumerable.Range(0, Columns.Count).Any(i => !Columns[i].Auto && Cell(er, i).Length > 0) ? next++ : 0;
        }
        return pos;
    }

    /// <summary>The edits that turn this table (as read) into <paramref name="edited"/>: a changed or cleared cell (cleared = ""), every non-empty row added at the end
    /// (Row 0) as its cells, and for a deleted row the rows below it written one row up (the last rows cleared). Only values are written: formulas, formats and
    /// lists stay where they are, so no row is removed from the sheet. Auto columns are never written. Sorted by row, then column, so that a run of cells in one
    /// row can be written in one go. A row added and left empty writes nothing. <see cref="Positions"/> tells which Excel row each row of <paramref name="edited"/> lands on.</summary>
    public List<CellEdit> Diff(InputTable edited)
    {
        var was = new Dictionary<int, TableRow>();
        foreach (var r in Rows) was[r.Row] = r;
        var pos = Positions(edited);
        var now = new Dictionary<int, TableRow>();
        for (int k = 0; k < pos.Length; k++) if (pos[k] > 0) now[pos[k]] = edited.Rows[k];
        int last = Math.Max(NextRow - 1, pos.DefaultIfEmpty(0).Max());
        var edits = new List<CellEdit>();
        for (int p = InputSchema.FirstDataRow; p <= last; p++)
        {
            was.TryGetValue(p, out var old); now.TryGetValue(p, out var cur);
            if (old == null && cur == null) continue;
            for (int i = 0; i < Columns.Count; i++)
            {
                if (Columns[i].Auto) continue;
                var before = old != null && i < old.Values.Length ? old.Values[i] : "";
                var after = Cell(cur, i);
                if (!Same(before, after)) edits.Add(new CellEdit(Sheet, p, Columns[i].Number, after));
            }
        }
        return edits;
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

    /// <summary>The tables as a plain workbook: a sheet each, the column names in row 1 and the labels in row 2 (the input.xlsx layout, columns packed from A), the
    /// rows from <see cref="InputSchema.FirstDataRow"/> as typed (numeric text as numbers). A row with nothing typed in the non-Auto columns is left out.</summary>
    public static XLWorkbook Export(IEnumerable<InputTable> tables)
    {
        var wb = ExcelReader.NewWorkbook();
        var edits = new List<CellEdit>();
        foreach (var t in tables)
        {
            var ws = wb.AddWorksheet(t.Sheet);
            for (int i = 0; i < t.Columns.Count; i++) { ws.Cell(1, i + 1).Value = t.Columns[i].Name; ws.Cell(2, i + 1).Value = t.Columns[i].Label; }
            int r = InputSchema.FirstDataRow;
            foreach (var row in t.Rows)
            {
                if (!Enumerable.Range(0, t.Columns.Count).Any(i => !t.Columns[i].Auto && t.Cell(row, i).Length > 0)) continue;
                for (int i = 0; i < t.Columns.Count; i++) if (t.Cell(row, i).Length > 0) edits.Add(new CellEdit(t.Sheet, r, i + 1, t.Cell(row, i)));
                r++;
            }
        }
        Apply(wb, edits);
        return wb;
    }

    /// <summary><see cref="Export"/> saved to <paramref name="path"/> (replaced), atomically.</summary>
    public static void ExportFile(string path, IEnumerable<InputTable> tables)
    {
        using var wb = Export(tables);
        ExcelTemplate.Save(wb, path);
    }

    /// <summary>What <see cref="Import"/> did: sheets taken (of the tables asked), rows put in, columns matched by name, columns of the table the file lacks (non-Auto),
    /// columns of the file the table does not know (ignored).</summary>
    public sealed record ImportReport(int Sheets, int SheetsAsked, int Rows, int ColumnsMatched, int ColumnsMissing, int ColumnsIgnored);

    /// <summary>The tables with the values of <paramref name="src"/> (an exported or an input.xlsx-like file): per table the sheet of the same name, its columns matched
    /// to the table's by their row-1 name. The file's rows replace the table's rows: the first ones take over the Excel rows the table's rows have (so they are
    /// edits of those rows), the rest are added, a table row beyond them is dropped (deleted on saving). A column the file lacks, and every Auto column, keeps the value
    /// the table row at the same place has (blank for an added row); a column of the file the table does not know is ignored. A table whose sheet is not in the file, or
    /// shares no column name with it, is returned as it is.</summary>
    public static (List<InputTable> Tables, ImportReport Report) Import(IReadOnlyList<InputTable> current, XLWorkbook src)
    {
        var result = new List<InputTable>();
        int sheets = 0, rowsIn = 0, matched = 0, missing = 0, ignored = 0;
        foreach (var t in current)
        {
            var s = Load(src, t.Sheet);
            var map = new int[t.Columns.Count];
            if (s != null)
                for (int i = 0; i < map.Length; i++) map[i] = s.Columns.ToList().FindIndex(c => c.Name == t.Columns[i].Name);
            if (s == null || map.All(m => m < 0)) { result.Add(t); continue; }
            sheets++;
            var rows = new List<TableRow>(s.Rows.Count);
            for (int j = 0; j < s.Rows.Count; j++)
            {
                var old = j < t.Rows.Count ? t.Rows[j] : null;
                var v = new string[t.Columns.Count];
                for (int i = 0; i < v.Length; i++)
                    v[i] = map[i] >= 0 && !t.Columns[i].Auto ? s.Rows[j].Values[map[i]] : old != null && i < old.Values.Length ? old.Values[i] : "";
                rows.Add(new TableRow(old?.Row ?? 0, v));
            }
            rowsIn += rows.Count;
            matched += map.Count(m => m >= 0);
            missing += Enumerable.Range(0, map.Length).Count(i => map[i] < 0 && !t.Columns[i].Auto);
            ignored += s.Columns.Count(c => t.Columns.All(x => x.Name != c.Name));
            result.Add(t.With(rows));
        }
        return (result, new ImportReport(sheets, current.Count, rowsIn, matched, missing, ignored));
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
