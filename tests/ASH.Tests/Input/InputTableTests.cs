using System.Globalization;
using ASH.Core.Input;
using ASH.Core.Layout;
using ClosedXML.Excel;

namespace ASH.Tests.Input;

/// <summary>The input table's logic (AGVINPUT): load a sheet by its own headers, diff an edited table into cell edits, lay edits over a workbook for the live check.
/// Columns are read from InputSchema / the workbook at run time, so none of this names a template column except where the check needs a known value.</summary>
[Collection("ExcelTemplateStatics")]
public class InputTableTests
{
    static string Tmp() => Path.Combine(Path.GetTempPath(), "ash_" + Guid.NewGuid().ToString("N") + ".xlsx");

    /// <summary>A workbook with the sheet's schema columns in row 1 and their labels in row 2.</summary>
    static IXLWorksheet Sheet(XLWorkbook wb, string sheet, params string[] extraHeaders)
    {
        var ws = wb.AddWorksheet(sheet);
        var cols = InputSchema.Of(sheet).Select(c => (Name: c.Name, Label: c.Label)).Concat(extraHeaders.Select(h => (Name: h, Label: h))).ToList();
        for (int i = 0; i < cols.Count; i++) { ws.Cell(1, i + 1).Value = cols[i].Name; ws.Cell(2, i + 1).Value = cols[i].Label; }
        return ws;
    }

    static void Put(IXLWorksheet ws, int row, string header, object value)
    {
        int c = 1;
        while (ws.Cell(1, c).GetString() != header) c++;
        switch (value)
        {
            case double d: ws.Cell(row, c).Value = d; break;
            default: ws.Cell(row, c).Value = (string)value; break;
        }
    }

    static int Idx(InputTable t, string name) => t.Columns.ToList().FindIndex(c => c.Name == name);

    static string[] Blank(InputTable t) => Enumerable.Repeat("", t.Columns.Count).ToArray();

    static InputTable Copy(InputTable t) => t.With(t.Rows.Select(r => new TableRow(r.Row, (string[])r.Values.Clone())).ToList());

    /// <summary>AGV_Groups with data on rows 3, 4 and 6 (row 5 blank).</summary>
    static XLWorkbook Groups()
    {
        var wb = ExcelReader.NewWorkbook();
        var ws = Sheet(wb, "AGV_Groups", "Extra");
        Put(ws, 3, "Group", "G1"); Put(ws, 3, "Type", "T1"); Put(ws, 3, "MinCount", 1.0); Put(ws, 3, "MaxCount", 3.0); Put(ws, 3, "Dispatch", "Nearest");
        Put(ws, 4, "Group", "G2"); Put(ws, 4, "Type", "T1"); Put(ws, 4, "MinCount", 1.0); Put(ws, 4, "MaxCount", 2.0); Put(ws, 4, "Extra", "memo");
        Put(ws, 6, "Group", "G3"); Put(ws, 6, "Type", "T2"); Put(ws, 6, "MinCount", 2.5); Put(ws, 6, "MaxCount", 4.0);
        return wb;
    }

    [Fact]
    public void Load_Takes_Headers_From_Row1_Skips_Blank_Rows_And_Keeps_Excel_Rows()
    {
        using var wb = Groups();
        var t = InputTable.Load(wb, "AGV_Groups")!;
        Assert.Equal(InputSchema.Of("AGV_Groups").Select(c => c.Name).Append("Extra"), t.Columns.Select(c => c.Name));
        Assert.Equal(new[] { 3, 4, 6 }, t.Rows.Select(r => r.Row));
        Assert.Equal(7, t.NextRow);
        Assert.Equal("G3", t.Rows[2].Values[Idx(t, "Group")]);
        Assert.Equal("2.5", t.Rows[2].Values[Idx(t, "MinCount")]);
        Assert.Equal("", t.Rows[0].Values[Idx(t, "Extra")]);
        // a header the schema knows has its label and flags; one it does not is a plain column named by itself
        Assert.Equal(InputSchema.Of("AGV_Groups").First(c => c.Name == "Group").Label, t.Columns[Idx(t, "Group")].Label);
        Assert.Null(t.Columns[Idx(t, "Extra")].Schema);
        Assert.Equal("Extra", t.Columns[Idx(t, "Extra")].Label);
        Assert.False(t.Columns[Idx(t, "Extra")].Auto);
    }

    [Fact]
    public void Load_Of_A_Missing_Sheet_Is_Null_And_LoadAll_Keeps_Tab_Order()
    {
        using var wb = ExcelReader.NewWorkbook();
        Sheet(wb, "Conveyors"); Sheet(wb, "Materials");
        Assert.Null(InputTable.Load(wb, "AGV_Types"));
        Assert.Equal(new[] { "Materials", "Conveyors" }, InputTable.LoadAll(wb).Select(t => t.Sheet));
    }

    [Fact]
    public void Numbers_Are_Invariant_Text_Whatever_The_Machine_Culture()
    {
        using var wb = Groups();
        var keep = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var t = InputTable.Load(wb, "AGV_Groups")!;
            Assert.Equal("2.5", t.Rows[2].Values[Idx(t, "MinCount")]);
            Assert.Equal("3", t.Rows[0].Values[Idx(t, "MaxCount")]);
        }
        finally { CultureInfo.CurrentCulture = keep; }
    }

    [Fact]
    public void Diff_Gives_Changed_And_Cleared_Cells_And_The_Rows_Added_At_The_End()
    {
        using var wb = Groups();
        var t = InputTable.Load(wb, "AGV_Groups")!;
        var e = Copy(t);
        e.Rows[0].Values[Idx(t, "Type")] = "T2";           // changed
        e.Rows[1].Values[Idx(t, "MaxCount")] = "";         // cleared
        e.Rows[2].Values[Idx(t, "MinCount")] = "2.50";     // the same number
        e.Rows[2].Values[Idx(t, "Group")] = " G3 ";        // the same text
        var added = Blank(t);
        var a1 = (string[])added.Clone(); a1[Idx(t, "Group")] = "G4"; a1[Idx(t, "MinCount")] = "1";
        var a2 = (string[])added.Clone(); a2[Idx(t, "Group")] = "G5";
        e.Rows.Add(new TableRow(0, a1)); e.Rows.Add(new TableRow(0, (string[])added.Clone())); e.Rows.Add(new TableRow(0, a2));

        var edits = t.Diff(e);

        int Col(string n) => t.Columns[Idx(t, n)].Number;
        Assert.Equal(new[]
        {
            new CellEdit("AGV_Groups", 3, Col("Type"), "T2"),
            new CellEdit("AGV_Groups", 4, Col("MaxCount"), ""),
            new CellEdit("AGV_Groups", 7, Col("Group"), "G4"),
            new CellEdit("AGV_Groups", 7, Col("MinCount"), "1"),
            new CellEdit("AGV_Groups", 8, Col("Group"), "G5"),
        }, edits);
        Assert.Equal(new[] { 3, 4, 6, 7, 0, 8 }, t.Positions(e)); // the empty added row lands nowhere
        Assert.Equal(edits, t.Diff(e));                           // asking again changes nothing
    }

    [Fact]
    public void Deleting_A_Row_Moves_The_Rows_Below_Up_And_Clears_The_Last_One()
    {
        using var wb = Groups();                 // data on rows 3, 4 and 6
        var t = InputTable.Load(wb, "AGV_Groups")!;
        var e = Copy(t);
        e.Rows.RemoveAt(0);                      // G1 (row 3) deleted: G2 -> row 3, G3 -> row 5
        var added = Blank(t); added[Idx(t, "Group")] = "G4";
        e.Rows.Add(new TableRow(0, added));      // an added row follows the last one left

        Assert.Equal(new[] { 3, 5, 6 }, t.Positions(e));
        using var saved = InputTable.Overlay(wb, t.Diff(e));
        var back = InputTable.Load(saved, "AGV_Groups")!;
        Assert.Equal(new[] { 3, 5, 6 }, back.Rows.Select(r => r.Row));
        Assert.Equal(new[] { "G2", "G3", "G4" }, back.Rows.Select(r => r.Values[Idx(t, "Group")]));
        Assert.Equal("memo", back.Rows[0].Values[Idx(t, "Extra")]);   // the whole row moved, its cells in other columns too
        Assert.Equal("", back.Rows[0].Values[Idx(t, "Dispatch")]);    // and what G1 had there is gone
        Assert.Equal("2.5", back.Rows[1].Values[Idx(t, "MinCount")]);
    }

    [Fact]
    public void Deleting_The_Last_Row_Or_All_Rows_Clears_Their_Cells()
    {
        using var wb = Groups();
        var t = InputTable.Load(wb, "AGV_Groups")!;
        var last = Copy(t); last.Rows.RemoveAt(2);
        var d = t.Diff(last);
        Assert.All(d, x => { Assert.Equal(6, x.Row); Assert.Equal("", x.Value); });
        Assert.NotEmpty(d);

        var none = t.With(new List<TableRow>());
        using var saved = InputTable.Overlay(wb, t.Diff(none));
        Assert.Empty(InputTable.Load(saved, "AGV_Groups")!.Rows);
        Assert.Empty(t.Diff(Copy(t)));                                  // nothing deleted, nothing written
    }

    [Fact]
    public void SaveFile_Of_A_Deleted_Row_Keeps_The_Formulas_Of_The_Rows()
    {
        var path = Tmp();
        try
        {
            using (var wb = Groups()) { for (int r = 3; r <= 6; r++) wb.Worksheet("AGV_Groups").Cell(r, 30).FormulaA1 = "=ROW()"; wb.SaveAs(path); }
            InputTable t; InputTable e;
            using (var wb = ExcelReader.Open(path)) { t = InputTable.Load(wb, "AGV_Groups")!; e = Copy(t); }
            e.Rows.RemoveAt(1);
            InputTable.SaveFile(path, t.Diff(e));
            using var after = ExcelReader.Open(path);
            for (int r = 3; r <= 6; r++) Assert.True(after.Worksheet("AGV_Groups").Cell(r, 30).HasFormula); // no row is removed from the sheet, only values move
            Assert.Equal(new[] { "G1", "G3" }, InputTable.Load(after, "AGV_Groups")!.Rows.Select(r => r.Values[Idx(t, "Group")]));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Export_Writes_Names_Labels_And_Values_And_Import_Reads_Them_Back_By_Name()
    {
        using var wb = Groups();
        var t = InputTable.Load(wb, "AGV_Groups")!;
        var e = Copy(t);
        e.Rows[0].Values[Idx(t, "Type")] = "TX";                          // an unsaved edit is exported
        e.Rows.Add(new TableRow(0, Blank(t)));                            // an empty added row is not
        using var x = InputTable.Export(new[] { e });
        var ws = x.Worksheet("AGV_Groups");
        Assert.Equal("Group", ws.Cell(1, 1).GetString());
        Assert.Equal(t.Columns[0].Label, ws.Cell(2, 1).GetString());
        Assert.Equal("TX", ws.Cell(3, Idx(t, "Type") + 1).GetString());
        Assert.Equal(XLDataType.Number, ws.Cell(3, Idx(t, "MaxCount") + 1).DataType);
        Assert.True(ws.Cell(6, 1).IsEmpty());                             // 3 rows -> 3..5

        // a table that was emptied takes the file's rows; the Excel rows of the old ones are kept for the first three
        var (tables, rep) = InputTable.Import(new[] { t }, x);
        Assert.Equal(new[] { 3, 4, 6 }, tables[0].Rows.Select(r => r.Row));
        Assert.Equal("TX", tables[0].Rows[0].Values[Idx(t, "Type")]);
        Assert.Equal(new InputTable.ImportReport(1, 1, 3, t.Columns.Count, 0, 0), rep);
    }

    [Fact]
    public void Import_Matches_By_Name_Keeps_Missing_Columns_Replaces_Rows_And_Ignores_Unknown_Columns()
    {
        using var wb = Groups();
        var t = InputTable.Load(wb, "AGV_Groups")!;     // G1,G2,G3 on rows 3,4,6; Extra column known to this table
        using var src = ExcelReader.NewWorkbook();
        var ws = src.AddWorksheet("AGV_Groups");
        string[] heads = { "Type", "Group", "Nope" };      // another order, no Extra, no MaxCount, one unknown
        for (int i = 0; i < heads.Length; i++) ws.Cell(1, i + 1).Value = heads[i];
        ws.Cell(3, 1).Value = "TA"; ws.Cell(3, 2).Value = "N1"; ws.Cell(3, 3).Value = "zz";
        ws.Cell(4, 1).Value = "TB"; ws.Cell(4, 2).Value = "N2";
        src.AddWorksheet("Materials").Cell(1, 1).Value = "Material";      // a sheet the dialog has no table for: not asked

        var (tables, rep) = InputTable.Import(new[] { t }, src);
        var r = tables[0].Rows;
        Assert.Equal(new[] { 3, 4 }, r.Select(x => x.Row));               // the third old row (6) is gone: deleted on saving
        Assert.Equal(("N1", "TA"), (r[0].Values[Idx(t, "Group")], r[0].Values[Idx(t, "Type")]));
        Assert.Equal("3", r[0].Values[Idx(t, "MaxCount")]);               // columns the file lacks stay
        Assert.Equal("memo", r[1].Values[Idx(t, "Extra")]);
        Assert.Equal(new InputTable.ImportReport(1, 1, 2, 2, t.Columns.Count - 2, 1), rep);
        Assert.Equal(new[] { 3, 4, 6 }, t.Rows.Select(x => x.Row));       // the table asked is not changed

        // saved: the diff writes the new values and clears row 6
        using var saved = InputTable.Overlay(wb, t.Diff(tables[0]));
        Assert.Equal(new[] { "N1", "N2" }, InputTable.Load(saved, "AGV_Groups")!.Rows.Select(x => x.Values[Idx(t, "Group")]));
    }

    [Fact]
    public void Import_Leaves_A_Table_Alone_When_The_File_Has_No_Such_Sheet_Or_No_Column_Of_It()
    {
        using var wb = Groups();
        var t = InputTable.Load(wb, "AGV_Groups")!;
        using var none = ExcelReader.NewWorkbook();
        none.AddWorksheet("Other").Cell(1, 1).Value = "x";
        var (a, ra) = InputTable.Import(new[] { t }, none);
        Assert.Same(t, a[0]);
        Assert.Equal(0, ra.Sheets);

        none.AddWorksheet("AGV_Groups").Cell(1, 1).Value = "Unknown";
        var (b, rb) = InputTable.Import(new[] { t }, none);
        Assert.Same(t, b[0]);
        Assert.Equal(0, rb.Sheets);
    }

    [Fact]
    public void Diff_Of_An_Untouched_Table_Is_Empty_And_A_Text_Number_That_Differs_Is_A_Change()
    {
        using var wb = ExcelReader.NewWorkbook();
        var ws = Sheet(wb, "AGV_Groups");
        Put(ws, 3, "Group", "007"); Put(ws, 3, "Type", "T1");
        var t = InputTable.Load(wb, "AGV_Groups")!;
        Assert.Empty(t.Diff(Copy(t)));
        var e = Copy(t); e.Rows[0].Values[Idx(t, "Group")] = "7";
        Assert.Single(t.Diff(e));
    }

    [SkippableFact]
    public void Auto_Columns_Are_Never_Written()
    {
        var auto = InputSchema.Of("Materials").FirstOrDefault(c => c.Auto);
        Skip.If(auto == null, "the template has no Auto column on Materials");
        using var wb = ExcelReader.NewWorkbook();
        var ws = Sheet(wb, "Materials");
        Put(ws, 3, InputSchema.Of("Materials")[0].Name, "M1");
        var t = InputTable.Load(wb, "Materials")!;
        var e = Copy(t);
        e.Rows[0].Values[Idx(t, auto!.Name)] = "99";
        var row = Blank(t);
        row[Idx(t, auto.Name)] = "5"; // a row that holds only an Auto value is empty
        e.Rows.Add(new TableRow(0, row));
        Assert.Empty(t.Diff(e));
    }

    [Fact]
    public void SaveFile_Writes_Numbers_As_Numbers_Clears_Cells_And_The_Table_Reads_Back_As_Edited()
    {
        var path = Tmp();
        try
        {
            using (var wb = Groups()) wb.SaveAs(path);
            InputTable t; InputTable e;
            using (var wb = ExcelReader.Open(path))
            {
                t = InputTable.Load(wb, "AGV_Groups")!;
                e = Copy(t);
            }
            e.Rows[0].Values[Idx(t, "Type")] = "T2";
            e.Rows[0].Values[Idx(t, "MinCount")] = "1.5";
            e.Rows[1].Values[Idx(t, "MaxCount")] = "";
            var added = Blank(t);
            added[Idx(t, "Group")] = "G4"; added[Idx(t, "StandbyCPs")] = "S1,S2"; added[Idx(t, "MaxCount")] = "5";
            e.Rows.Add(new TableRow(0, added));

            InputTable.SaveFile(path, t.Diff(e));

            using var after = ExcelReader.Open(path);
            var back = InputTable.Load(after, "AGV_Groups")!;
            Assert.Equal(e.Rows.Select(r => string.Join("|", r.Values)), back.Rows.Select(r => string.Join("|", r.Values)));
            Assert.Equal(new[] { 3, 4, 6, 7 }, back.Rows.Select(r => r.Row));
            var ws = after.Worksheet("AGV_Groups");
            Assert.Equal(XLDataType.Number, ws.Cell(3, t.Columns[Idx(t, "MinCount")].Number).DataType);
            Assert.True(ws.Cell(4, t.Columns[Idx(t, "MaxCount")].Number).IsEmpty());
            Assert.Equal(XLDataType.Text, ws.Cell(7, t.Columns[Idx(t, "StandbyCPs")].Number).DataType);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Overlay_Is_A_Copy_With_The_Edits_And_Leaves_The_Original_Alone()
    {
        using var wb = Groups();
        var t = InputTable.Load(wb, "AGV_Groups")!;
        var edits = new[] { new CellEdit("AGV_Groups", 3, t.Columns[Idx(t, "Type")].Number, "T9"), new CellEdit("AGV_Groups", 4, t.Columns[Idx(t, "Group")].Number, "") };
        using var over = InputTable.Overlay(wb, edits);
        var o = InputTable.Load(over, "AGV_Groups")!;
        Assert.Equal("T9", o.Rows[0].Values[Idx(t, "Type")]);
        Assert.Equal("", o.Rows.First(r => r.Row == 4).Values[Idx(t, "Group")]);
        Assert.Equal("T1", InputTable.Load(wb, "AGV_Groups")!.Rows[0].Values[Idx(t, "Type")]);
        Assert.Empty(t.Diff(InputTable.Load(wb, "AGV_Groups")!));
    }

    static (XLWorkbook Wb, string Path) Template()
    {
        var path = Tmp();
        ExcelTemplate.Write(path);
        return (ExcelReader.Open(path), path);
    }

    [Fact]
    public void Check_Marks_The_Cell_A_Finding_Names_And_A_Value_The_Reader_Cannot_Take()
    {
        var (wb, path) = Template();
        try
        {
            using (wb)
            {
                var t = InputTable.Load(wb, "AGV_Types")!;
                int cap = t.Columns[Idx(t, "Capacity")].Number;

                var clean = InputCheck.Run(wb, new CellEdit[0], null);
                Assert.Null(clean.Fatal);

                // a text in a number cell: the reader stops there and says where
                var text = InputCheck.Run(wb, new[] { new CellEdit("AGV_Types", 3, cap, "abc") }, null);
                Assert.NotNull(text.Fatal);
                var bad = Assert.Single(text.Marks);
                Assert.Equal(("AGV_Types", 3, cap, true), (bad.Sheet, bad.Row, bad.Col, bad.Error));

                // a readable value the checks object to: the finding names the column in its message
                var zero = InputCheck.Run(wb, new[] { new CellEdit("AGV_Types", 3, cap, "0") }, null);
                Assert.Null(zero.Fatal);
                Assert.True(zero.Errors >= 1);
                Assert.Contains(zero.Marks, m => m.Sheet == "AGV_Types" && m.Row == 3 && m.Col == cap && m.Error);

                Assert.Equal("1", InputTable.Load(wb, "AGV_Types")!.Rows[0].Values[Idx(t, "Capacity")]); // the original was not touched
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CpNamesByType_Is_In_Natural_Name_Order()
    {
        var l = new ASH.Core.Layout.Layout();
        foreach (var (n, ty) in new[] { ("L10", "load"), ("L2", "load"), ("S1", "standby"), ("", "load"), ("L2", "load") })
            l.Cps.Add(new LayoutCp { Name = n, Type = ty });
        var by = InputTable.CpNamesByType(l);
        Assert.Equal(new[] { "L2", "L10" }, by["load"]);
        Assert.Equal(new[] { "S1" }, by["standby"]);
    }

    [Fact]
    public void A_Cp_List_Cell_Reads_Back_As_Typed()
    {
        var path = Tmp();
        try
        {
            using (var wb = ExcelReader.NewWorkbook()) { var ws = Sheet(wb, "Materials"); Put(ws, 3, "Material", "M001"); Put(ws, 3, "SupplyFromCP", "L001"); wb.SaveAs(path); }
            InputTable t; InputTable e;
            using (var wb = ExcelReader.Open(path)) { t = InputTable.Load(wb, "Materials")!; e = Copy(t); }
            e.Rows[0].Values[Idx(t, "SupplyFromCP")] = "L001,L002";
            InputTable.SaveFile(path, t.Diff(e));
            using var after = ExcelReader.Open(path);
            Assert.Equal("L001,L002", InputTable.Load(after, "Materials")!.Rows[0].Values[Idx(t, "SupplyFromCP")]);
        }
        finally { File.Delete(path); }
    }
}
