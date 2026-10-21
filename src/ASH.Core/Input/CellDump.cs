using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ClosedXML.Excel;

namespace ASH.Core.Input;

/// <summary>
/// The cells of a workbook as plain text, one line per non-empty cell: sheet, row, column, kind (N number, S text) and the value, tab separated, with
/// backslash escapes for tab, newline and backslash. Company document encryption (NASCA) leaves text files alone, so this is what ash-run can still read
/// after the plugin has had Excel read the encrypted input.xlsx.
/// </summary>
public static class CellDump
{
    public const string FileName = "input.cells.tsv";
    const string Header = "#ASH-CELLS 1";

    // the backslash is spelled (char)92 on purpose: escape sequences with doubled backslashes are easy to get wrong when this file is edited
    const char Bs = (char)92;

    static string Esc(string s) => s.Replace(Bs.ToString(), new string(Bs, 2)).Replace("\t", Bs + "t").Replace("\r", Bs + "r").Replace("\n", Bs + "n");

    static string Unesc(string s)
    {
        if (s.IndexOf(Bs) < 0) return s;
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != Bs || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
            char n = s[++i];
            sb.Append(n == 't' ? '\t' : n == 'n' ? '\n' : n == 'r' ? '\r' : n);
        }
        return sb.ToString();
    }

    public static void Write(XLWorkbook wb, string path)
    {
        var tmp = path + ".tmp";
        try
        {
            using (var w = new StreamWriter(tmp, false, new UTF8Encoding(false)))
            {
                w.Write(Header + "\n");
                foreach (var ws in wb.Worksheets)
                    foreach (var cell in ws.CellsUsed())
                    {
                        string kind, text;
                        if (cell.DataType == XLDataType.Number) { kind = "N"; text = cell.GetDouble().ToString("R", CultureInfo.InvariantCulture); }
                        else { kind = "S"; text = cell.GetString(); }
                        if (kind == "S" && text.Length == 0) continue;
                        w.Write(Esc(ws.Name) + "\t" + cell.Address.RowNumber.ToString(CultureInfo.InvariantCulture) + "\t" + cell.Address.ColumnNumber.ToString(CultureInfo.InvariantCulture)
                            + "\t" + kind + "\t" + Esc(text) + "\n");
                    }
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    /// <summary>Rebuilds a workbook for <see cref="ExcelReader.Read(XLWorkbook, out List{ASH.Core.Layout.Finding})"/>. Throws FormatException for a file that is not a dump.</summary>
    public static XLWorkbook Load(string path)
    {
        FontFreeGraphicEngine.Install();
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length == 0 || lines[0] != Header) throw new FormatException(path + " 은(는) ASH 셀 스냅샷이 아닙니다.");
        var wb = new XLWorkbook();
        var sheets = new Dictionary<string, IXLWorksheet>(StringComparer.Ordinal);
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) continue;
            var t = lines[i].Split('\t');
            if (t.Length != 5 || !int.TryParse(t[1], NumberStyles.None, CultureInfo.InvariantCulture, out var row) || !int.TryParse(t[2], NumberStyles.None, CultureInfo.InvariantCulture, out var col))
                throw new FormatException($"{path} {i + 1}행이 올바르지 않습니다.");
            var name = Unesc(t[0]);
            if (!sheets.TryGetValue(name, out var ws)) sheets[name] = ws = wb.AddWorksheet(name);
            if (t[3] == "N" && double.TryParse(t[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) ws.Cell(row, col).Value = d;
            else ws.Cell(row, col).Value = Unesc(t[4]);
        }
        return wb;
    }
}
