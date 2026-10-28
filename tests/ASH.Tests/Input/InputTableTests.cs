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
        Assert.Equal(new[] { 3, 4, 6, 7, 0, 8 }, e.Rows.Select(r => r.Row)); // the empty added row stays unassigned
        Assert.Equal(edits, t.Diff(e));                                       // asking again changes nothing
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
}
