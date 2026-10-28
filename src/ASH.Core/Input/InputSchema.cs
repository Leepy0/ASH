using System;
using System.Collections.Generic;
using System.Linq;

namespace ASH.Core.Input;

/// <summary>
/// The input form's columns as the plugin's input table (AGVINPUT) needs them, read from <see cref="ExcelTemplate"/>'s definitions so the table and the
/// workbook never disagree. Columns are matched by <see cref="Column.Name"/> (row 1 of a sheet), never by position: an older workbook may order them otherwise.
/// </summary>
public static class InputSchema
{
    /// <summary>One column. List: a fixed list ("a,b") or a sheet whose first column holds the choices; null = free text.
    /// CpType: the cell takes a CP name of that type from the drawing ("load", "standby", …); null = not a CP column.
    /// Auto: computed (formula or description), not typed. Advanced: grouped away in the template (E1).</summary>
    public sealed record Column(string Sheet, string Name, string Label, string? List, string? CpType, bool Auto, bool Advanced);

    /// <summary>Sheets the input table shows, in tab order.</summary>
    public static readonly string[] TableSheets = { "Materials", "AGV_Groups", "AGV_Types", "Conveyors" };

    /// <summary>The columns of <paramref name="sheet"/> in template order; empty for a sheet the template does not have.</summary>
    public static IReadOnlyList<Column> Of(string sheet)
    {
        var s = ExcelTemplate.Sheets.FirstOrDefault(x => x.Sheet == sheet);
        if (s.Sheet == null) return Array.Empty<Column>();
        return s.Cols.Select((c, i) => new Column(sheet, c, i < s.Desc.Length ? s.Desc[i] : c,
            ExcelTemplate.Lists.Where(l => l.Sheet == sheet && l.Col == c).Select(l => l.Source).FirstOrDefault(),
            ExcelTemplate.CpLists.Where(l => l.Sheet == sheet && l.Col == c).Select(l => l.CpType).FirstOrDefault(),
            ExcelTemplate.AutoCols.Any(a => a.Sheet == sheet && a.Col == c),
            ExcelTemplate.Advanced.Any(a => a.Sheet == sheet && a.Col == c))).ToList();
    }

    /// <summary>First data row of every input sheet (row 1 = column names, row 2 = Korean labels).</summary>
    public const int FirstDataRow = 3;
}
