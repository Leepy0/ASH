using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ASH.Core.Layout;
using ClosedXML.Excel;

namespace ASH.Core.Input;

/// <summary>One cell to colour and annotate: a validation finding that names a row ("Materials!3") and, when its message names a column, that column.</summary>
public sealed record ExcelMark(string Sheet, int Row, int Col, bool Error, string Text);

/// <summary>Validation results written into the workbook: error cells red, warning cells orange, each with a cell comment holding the message. The comments
/// start with <see cref="Prefix"/>, which is how the next run finds and clears its own marks (a comment the user wrote is left alone).</summary>
public static class ExcelMarks
{
    public const string Prefix = "ASH 검증";
    public const int ErrorRgb = 0xFF9B9B, WarnRgb = 0xFFD27F;

    static readonly Regex Ref = new Regex(@"^(?<sheet>[A-Za-z_]+)!(?<row>\d+)$", RegexOptions.Compiled);

    // a column header in a message may be written without the plural s ("StandbyCP 'X'")
    static readonly Dictionary<string, string> Alias = new() { ["StandbyCP"] = "StandbyCPs" };

    /// <summary>The cells the findings point at. Findings without a "Sheet!row" reference, or for a sheet the workbook lacks, are left out. The column is the
    /// header of that sheet named first in the message (as a whole word, e.g. SupplyToCP); else the first column. Several findings on a cell share it: its
    /// colour is the worst one's and its comment lists them all.</summary>
    public static List<ExcelMark> Plan(XLWorkbook wb, IEnumerable<Finding> findings)
    {
        var cells = new Dictionary<(string, int, int), ExcelMark>();
        var order = new List<(string, int, int)>();
        foreach (var f in findings)
        {
            if (f.Code != FindingCode.InputError && f.Code != FindingCode.InputWarning) continue;
            var m = f.EntityId == null ? null : Ref.Match(f.EntityId);
            if (m == null || !m.Success || !int.TryParse(m.Groups["row"].Value, out var row) || row < 1) continue;
            if (!wb.TryGetWorksheet(m.Groups["sheet"].Value, out var ws)) continue;
            int col = ColumnIn(ws, f.Message);
            var key = (ws.Name, row, col);
            bool err = f.Code == FindingCode.InputError;
            if (cells.TryGetValue(key, out var old))
                cells[key] = old with { Error = old.Error || err, Text = old.Text + "\n" + f.Message };
            else { cells[key] = new ExcelMark(ws.Name, row, col, err, f.Message); order.Add(key); }
        }
        return order.Select(k => cells[k]).ToList();
    }

    static int ColumnIn(IXLWorksheet ws, string message)
    {
        int last = ws.LastColumnUsed()?.ColumnNumber() ?? 0, best = 1, bestAt = int.MaxValue;
        for (int c = 1; c <= last; c++)
        {
            var h = ws.Cell(1, c).GetString().Trim();
            if (h.Length == 0) continue;
            foreach (var name in Alias.Where(a => a.Value == h).Select(a => a.Key).Append(h))
            {
                var at = Regex.Match(message, @"(?<![A-Za-z0-9_])" + Regex.Escape(name) + @"(?![A-Za-z0-9_])");
                if (at.Success && at.Index < bestAt) { bestAt = at.Index; best = c; }
            }
        }
        return best;
    }

    /// <summary>Clears the marks of an earlier run from every template sheet and marks the findings' cells. <paramref name="log"/> gets the edits for a file only
    /// Excel can write: the clear is always logged (a copy read through Excel carries no comments to find), so Excel does its own search. Returns the number
    /// of marks cleared plus cells marked: 0 means the file needs no save.</summary>
    public static int Apply(XLWorkbook wb, IEnumerable<Finding> findings, List<ExcelEdit>? log = null)
    {
        int changed = 0;
        foreach (var (sheet, _, _, _) in ExcelTemplate.Sheets)
        {
            if (!wb.TryGetWorksheet(sheet, out var ws)) continue;
            foreach (var cell in ws.CellsUsed(XLCellsUsedOptions.All).Where(c => c.HasComment && c.GetComment().Text.StartsWith(Prefix, StringComparison.Ordinal)).ToList())
            {
                cell.GetComment().Delete();
                cell.Style.Fill.PatternType = XLFillPatternValues.None;
                changed++;
            }
            log?.Add(new ClearMarksEdit(sheet));
        }
        ExcelTemplate.ApplyAutoFill(wb, log); // the clear above may have taken the "no input needed" green with it
        foreach (var m in Plan(wb, findings))
        {
            var cell = wb.Worksheet(m.Sheet).Cell(m.Row, m.Col);
            var text = $"{Prefix}({(m.Error ? "오류" : "경고")}): {m.Text}";
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(m.Error ? ErrorRgb : WarnRgb);
            if (!cell.HasComment) cell.CreateComment().AddText(text);
            log?.Add(new MarkEdit(m.Sheet, m.Row, m.Col, m.Error ? ErrorRgb : WarnRgb, text));
            changed++;
        }
        return changed;
    }

    /// <summary>Marks the file in place (opened directly, saved atomically); false and nothing written when there was nothing to clear or mark. Throws like
    /// opening and saving a workbook do (encrypted or locked file): the caller falls back to Excel.</summary>
    public static bool MarkFile(string path, IEnumerable<Finding> findings)
    {
        using var wb = ExcelReader.Open(path);
        if (Apply(wb, findings) == 0) return false;
        ExcelTemplate.Save(wb, path);
        return true;
    }
}

/// <summary>Removes the marks of an earlier run (comments starting with <see cref="ExcelMarks.Prefix"/> and their fill) from every cell of the sheet.</summary>
public sealed record ClearMarksEdit(string Sheet) : ExcelEdit(Sheet);
/// <summary>A cell filled with Rgb and given the comment Text (a comment already there is kept).</summary>
public sealed record MarkEdit(string Sheet, int Row, int Col, int Rgb, string Text) : ExcelEdit(Sheet);
