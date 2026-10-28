using System;
using System.Collections.Generic;
using System.Text;

namespace ASH.Core.Input;

/// <summary>The grid's clipboard text, in the form Excel copies and pastes: cells separated by a tab, rows by a line break, a cell holding a tab, line break or quote
/// wrapped in quotes (quotes doubled).</summary>
public static class GridText
{
    public static string Format(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            for (int c = 0; c < row.Count; c++)
            {
                if (c > 0) sb.Append('\t');
                var v = row[c] ?? "";
                if (v.IndexOfAny(new[] { '\t', '\r', '\n', '"' }) >= 0) sb.Append('"').Append(v.Replace("\"", "\"\"")).Append('"');
                else sb.Append(v);
            }
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>The rows of clipboard text (any of \r\n, \n, \r ends a row; one final line break adds no row). Empty text gives no rows.</summary>
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false, pending = false; // pending: something of the current row has been read
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quoted)
            {
                if (ch != '"') cell.Append(ch);
                else if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else quoted = false;
                continue;
            }
            pending = true;
            if (ch == '"' && cell.Length == 0) quoted = true;
            else if (ch == '\t') { row.Add(cell.ToString()); cell.Clear(); }
            else if (ch == '\r' || ch == '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Clear();
                rows.Add(row.ToArray()); row.Clear(); pending = false;
            }
            else cell.Append(ch);
        }
        if (pending || cell.Length > 0 || row.Count > 0 || quoted) { row.Add(cell.ToString()); rows.Add(row.ToArray()); }
        return rows;
    }

    /// <summary>One value to put into the grid.</summary>
    public sealed record PasteCell(int Row, int Col, string Value);

    /// <summary>Where pasted <paramref name="data"/> lands: its first cell on grid row <paramref name="startRow"/>, the column at <paramref name="startPos"/> of
    /// <paramref name="cols"/> (the grid's visible columns, left to right, as column indexes), the next ones on the following rows and columns. A value for a column
    /// that is not <paramref name="writable"/> (an Auto column) is skipped, not moved over; a value beyond the last column is dropped. <c>RowsNeeded</c> is how many
    /// rows the grid must have (the caller adds the missing ones at the bottom).</summary>
    public static (List<PasteCell> Cells, int RowsNeeded, int Skipped) Plan(IReadOnlyList<string[]> data, int startRow, int startPos, IReadOnlyList<int> cols, Func<int, bool> writable)
    {
        var cells = new List<PasteCell>();
        int skipped = 0;
        for (int r = 0; r < data.Count; r++)
            for (int c = 0; c < data[r].Length; c++)
            {
                int pos = startPos + c;
                if (pos >= cols.Count || !writable(cols[pos])) { skipped++; continue; }
                cells.Add(new PasteCell(startRow + r, cols[pos], data[r][c]));
            }
        return (cells, startRow + data.Count, skipped);
    }
}
