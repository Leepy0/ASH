using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ASH.Core.Layout;
using ClosedXML.Excel;

namespace ASH.Core.Input;

/// <summary>What the checks make of the input as it would be saved. <see cref="Marks"/> are the cells to colour (red = Error, orange = warning, text = the message);
/// <see cref="Messages"/> are all findings, including those that name no row (Settings values). <see cref="Fatal"/> is set when the cells could not even be read
/// (a text where a number belongs): the reader stops at the first such cell, so nothing else was checked.</summary>
public sealed record InputCheckResult(IReadOnlyList<ExcelMark> Marks, int Errors, int Warnings, string? Fatal, IReadOnlyList<string> Messages);

/// <summary>The input table's live check: the original workbook with the user's edits laid over it, read by <see cref="ExcelReader"/> and checked by
/// <see cref="InputValidator"/> against the drawing's CP names, with the findings located on cells the way the Excel marks do (<see cref="ExcelMarks.Plan"/>).</summary>
public static class InputCheck
{
    // ExcelDataException: "{sheet} 시트 {row}행 '{col}' 열: {why}"
    static readonly Regex DataError = new Regex(@"^(?<sheet>\S+) 시트 (?<row>\d+)행 '(?<col>[^']*)' 열: ", RegexOptions.Compiled);

    public static InputCheckResult Run(XLWorkbook original, IEnumerable<CellEdit> edits, IReadOnlyCollection<string>? cpNames)
    {
        using var wb = InputTable.Overlay(original, edits);
        var findings = new List<Finding>();
        try
        {
            var input = ExcelReader.Read(wb, out var readWarnings);
            findings.AddRange(readWarnings);
            findings.AddRange(InputValidator.Validate(input, cpNames));
        }
        catch (Exception ex) when (ex is FormatException || ex is InvalidOperationException || ex is ArgumentException)
        {
            var marks = new List<ExcelMark>();
            var m = DataError.Match(ex.Message);
            if (m.Success && wb.TryGetWorksheet(m.Groups["sheet"].Value, out var ws))
            {
                int row = int.Parse(m.Groups["row"].Value), col = 1;
                for (int c = 1, last = ws.LastColumnUsed()?.ColumnNumber() ?? 0; c <= last; c++)
                    if (ws.Cell(1, c).GetString().Trim() == m.Groups["col"].Value) { col = c; break; }
                marks.Add(new ExcelMark(ws.Name, row, col, true, ex.Message.Substring(m.Length)));
            }
            return new InputCheckResult(marks, 1, 0, ex.Message, new[] { ex.Message });
        }
        return new InputCheckResult(ExcelMarks.Plan(wb, findings),
            findings.Count(f => f.Code == FindingCode.InputError), findings.Count(f => f.Code == FindingCode.InputWarning),
            null, findings.Select(f => f.Message).ToList());
    }
}
