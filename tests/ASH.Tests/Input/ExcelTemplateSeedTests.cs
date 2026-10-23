using System.Collections.Generic;
using System.IO;
using System.Linq;
using ASH.Core.Input;
using ASH.Core.Layout;
using ClosedXML.Excel;
using Xunit;

namespace ASH.Tests.Input;

public class ExcelTemplateSeedTests
{
    static LayoutCp Cp(string name, string type, string group) => new LayoutCp { Name = name, Type = type, Group = group };

    static ASH.Core.Layout.Layout Drawing()
    {
        var l = new ASH.Core.Layout.Layout();
        l.Cps.Add(Cp("SB1", "standby", "A")); l.Cps.Add(Cp("SB2", "standby", "A")); l.Cps.Add(Cp("SB3", "standby", "B"));
        l.Cps.Add(Cp("L001", "load", "A")); l.Cps.Add(Cp("L002", "load", "B"));
        l.Cps.Add(Cp("U001", "unload", "A")); l.Cps.Add(Cp("U002", "unload", "B"));
        l.Cps.Add(Cp("T001", "traffic", ""));
        return l;
    }

    [Fact]
    public void Template_From_Drawing_Carries_Groups_Standby_Cps_And_Routes()
    {
        var path = Path.Combine(Path.GetTempPath(), "seed-" + System.Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            ExcelTemplate.Write(path, Drawing());
            var p = ExcelReader.Read(path);
            Assert.Equal(new[] { "A", "B" }, p.Groups.Select(g => g.Group));
            Assert.Equal(new[] { "SB1", "SB2" }, p.Groups[0].StandbyCPs);
            Assert.Equal("T_AGV", Assert.Single(p.AgvTypes).Type);              // one type shared by both groups
            Assert.All(p.Groups, g => Assert.Equal("T_AGV", g.Type));
            Assert.Equal(new[] { "L001", "L002" }, p.Routes.Select(r => r.SupplyFromCP));
            Assert.Equal(new[] { "U001", "U002" }, p.Routes.Select(r => r.SupplyToCP));
            Assert.Equal(new[] { "A", "B" }, p.Materials.Select(m => m.Group));
            Assert.Empty(InputValidator.Validate(p, new[] { "SB1", "SB2", "SB3", "L001", "L002", "U001", "U002", "T001" }).Where(f => f.Code == FindingCode.InputError));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Update_Keeps_Typed_Rows_And_Appends_What_The_Drawing_Added()
    {
        var path = Path.Combine(Path.GetTempPath(), "seed-" + System.Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            ExcelTemplate.Write(path, Drawing());
            using (var wb = ExcelReader.Open(path)) // the user's work: a takt time, and an old-template AGV_Types.Group column
            {
                wb.Worksheet("Materials").Cell(3, 4).Value = 99;
                var t = wb.Worksheet("AGV_Types"); t.Column(2).InsertColumnsBefore(1); t.Cell(1, 2).Value = "Group"; t.Cell(3, 2).Value = "A";
                wb.Save();
            }
            var l = Drawing();
            l.Cps.Add(Cp("SB4", "standby", "A")); l.Cps.Add(Cp("SB5", "standby", "C")); l.Cps.Add(Cp("L003", "load", "C")); l.Cps.Add(Cp("U003", "unload", "C"));
            var changes = ExcelTemplate.Update(path, l);
            var p = ExcelReader.Read(path);
            Assert.Equal(99, p.Materials[0].TaktTime);                                    // typed value kept
            Assert.Equal(new[] { "SB1", "SB2", "SB4" }, p.Groups[0].StandbyCPs);          // new standby CP appended
            Assert.Equal(new[] { "A", "B", "C" }, p.Groups.Select(g => g.Group));          // new group with the existing type
            Assert.Equal("T_AGV", p.Groups[2].Type);
            Assert.Equal(new[] { "M001", "M002", "M003" }, p.Materials.Select(m => m.Material));
            Assert.Equal("L003", p.Routes[2].SupplyFromCP);
            using (var wb = ExcelReader.Open(path)) Assert.DoesNotContain("Group", wb.Worksheet("AGV_Types").Row(1).CellsUsed().Select(c => c.GetString()));
            Assert.Contains(changes, c => c.Contains("Group 열 삭제"));
            Assert.Empty(ExcelTemplate.Update(path, l));                                  // nothing left to add
            Assert.Equal(3, ExcelReader.Read(path).Routes.Count);
        }
        finally { File.Delete(path); }
    }

    /// <summary>For a file only Excel can write, the plugin replays Update's log through Excel: the log alone must carry every change.</summary>
    [Fact]
    public void Update_Log_Replayed_On_The_Original_Gives_The_Same_Cells()
    {
        var path = Path.Combine(Path.GetTempPath(), "seed-" + System.Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            ExcelTemplate.Write(path, Drawing());
            using (var wb = ExcelReader.Open(path)) // an older workbook: EmptyReturn column, old description
            {
                var m = wb.Worksheet("Materials"); m.Cell(1, 19).Value = "EmptyReturn"; m.Cell(3, 19).Value = "Y"; m.Cell(2, 2).Value = "공정명(선택·메모)";
                wb.Worksheet("Routes").Delete();
                wb.Save();
            }
            var l = Drawing(); l.Cps.Add(Cp("SB4", "standby", "A")); l.Cps.Add(Cp("L003", "load", "C"));
            var log = new List<ExcelEdit>();
            using var updated = ExcelReader.Open(path);
            var changes = ExcelTemplate.Update(updated, l, log);
            Assert.Contains(changes, c => c.Contains("EmptyReturn 열 삭제"));
            using var replay = ExcelReader.Open(path);
            foreach (var e in log)
                switch (e)
                {
                    case AddSheetEdit a: replay.AddWorksheet(a.Sheet); break;
                    case DeleteColumnEdit d: replay.Worksheet(d.Sheet).Column(d.Col).Delete(); break;
                    case CellEdit c: replay.Worksheet(c.Sheet).Cell(c.Row, c.Col).Value = c.Value; break;
                    case FormulaEdit f: replay.Worksheet(f.Sheet).Range(f.FirstRow, f.Col, f.LastRow, f.Col).FormulaR1C1 = f.FormulaR1C1; break;
                    case FillEdit fe: replay.Worksheet(fe.Sheet).Range(fe.FirstRow, fe.Col, fe.LastRow, fe.Col).Style.Fill.BackgroundColor = XLColor.FromArgb(fe.Rgb); break;
                }
            Assert.Contains(log, e => e is ListEdit x && x.Sheet == "AGV_Groups" && x.Formula == "=AGV_Types!$A$3:$A$1000");
            Assert.Contains(log, e => e is FillEdit x && x.Sheet == "Materials" && x.Rgb == ExcelTemplate.AutoFillRgb);
            AssertAutoFill(replay);                                 // the replayed edit log gives the same colours
            foreach (var ws in updated.Worksheets)
            {
                var r = replay.Worksheet(ws.Name);
                foreach (var cell in ws.CellsUsed(XLCellsUsedOptions.Contents))
                {
                    var other = r.Cell(cell.Address.RowNumber, cell.Address.ColumnNumber);
                    if (cell.HasFormula) Assert.Equal(cell.FormulaA1, other.FormulaA1); // not evaluated: the totals are for Excel to compute
                    else Assert.Equal(cell.GetString(), other.GetString());
                }
                Assert.Equal(ws.CellsUsed(XLCellsUsedOptions.Contents).Count(), r.CellsUsed(XLCellsUsedOptions.Contents).Count());
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Template_Has_Drop_Down_Lists()
    {
        var path = Path.Combine(Path.GetTempPath(), "seed-" + System.Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            ExcelTemplate.Write(path, Drawing());
            ExcelTemplate.Update(path, Drawing()); // re-applied lists must not pile up
            using var wb = ExcelReader.Open(path);
            var form = wb.Worksheet("Materials").Cell(3, 15).GetDataValidation();
            Assert.Equal("\"Box,Pallet,Trolley\"", form.Value);
            Assert.Contains("AGV_Types", wb.Worksheet("AGV_Groups").Cell(5, 2).GetDataValidation().Value);
            Assert.Single(wb.Worksheet("Materials").DataValidations, v => v.Ranges.Any(r => r.RangeAddress.FirstAddress.ColumnNumber == 15));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Template_From_Empty_Drawing_Keeps_The_Example_Rows()
    {
        var path = Path.Combine(Path.GetTempPath(), "seed-" + System.Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            ExcelTemplate.Write(path, new ASH.Core.Layout.Layout());
            var p = ExcelReader.Read(path);
            Assert.Equal("G_EX", Assert.Single(p.Groups).Group);
            Assert.Equal("M001", Assert.Single(p.Routes).Material);
        }
        finally { File.Delete(path); }
    }

    static XLColor Auto => XLColor.FromArgb(ExcelTemplate.AutoFillRgb);

    /// <summary>Formula-only and memo cells carry the "no input needed" fill; the header and the typed cells next to them do not.</summary>
    static void AssertAutoFill(XLWorkbook wb)
    {
        var m = wb.Worksheet("Materials"); var s = wb.Worksheet("Settings");
        int total = Enumerable.Range(1, 30).First(c => m.Cell(1, c).GetString() == "CallThresholdTotal");
        int unload = Enumerable.Range(1, 30).First(c => m.Cell(1, c).GetString() == "UnloadThresholdTotal");
        foreach (var r in new[] { 3, 4, 500, 1000 }) { Assert.Equal(Auto, m.Cell(r, total).Style.Fill.BackgroundColor); Assert.Equal(Auto, m.Cell(r, unload).Style.Fill.BackgroundColor); }
        foreach (var r in new[] { 3, 14, 1000 }) Assert.Equal(Auto, s.Cell(r, 3).Style.Fill.BackgroundColor);   // Description, also below the last default key
        Assert.NotEqual(Auto, m.Cell(1, total).Style.Fill.BackgroundColor);          // the header keeps its blue
        Assert.NotEqual(Auto, m.Cell(3, 4).Style.Fill.BackgroundColor);             // a typed column (TaktTime)
        Assert.NotEqual(Auto, s.Cell(3, 2).Style.Fill.BackgroundColor);             // Settings Value is input
        Assert.NotEqual(XLColor.LightBlue, Auto); Assert.NotEqual(XLColor.LightGray, Auto);
    }

    [Fact]
    public void Cells_That_Need_No_Input_Are_Filled_After_Write_And_After_Update()
    {
        var path = Path.Combine(Path.GetTempPath(), "seed-" + System.Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            ExcelTemplate.Write(path, Drawing());
            using (var wb = ExcelReader.Open(path)) AssertAutoFill(wb);
            using (var wb = ExcelReader.Open(path)) // an older workbook: the fill is gone (and a column was added later)
            {
                wb.Worksheet("Materials").Range(3, 1, 1000, 30).Style.Fill.BackgroundColor = XLColor.NoColor;
                wb.Worksheet("Settings").Range(3, 1, 1000, 3).Style.Fill.BackgroundColor = XLColor.NoColor;
                wb.Save();
            }
            ExcelTemplate.Update(path, Drawing());
            using (var wb = ExcelReader.Open(path)) AssertAutoFill(wb);
            Assert.Equal(new[] { "A", "B" }, ExcelReader.Read(path).Groups.Select(g => g.Group));
        }
        finally { File.Delete(path); }
    }

    static ASH.Core.Catalog.CatalogType Cat(string name, double speed) => new ASH.Core.Catalog.CatalogType
    { Name = name, Defaults = new ASH.Core.Catalog.CatalogDefaults { SpeedLoadedStraight = speed, SpeedEmptyStraight = speed, Accel = 0.5, Decel = 0.5, Capacity = 1 } };

    [Fact]
    public void Write_Fills_Types_From_Catalog_And_Groups_Use_First()
    {
        var path = Path.Combine(Path.GetTempPath(), "seed-" + System.Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            ExcelTemplate.Write(path, Drawing(), new[] { Cat("T_A", 1), Cat("T_B", 2) });
            var p = ExcelReader.Read(path);
            Assert.Equal(new[] { "T_A", "T_B" }, p.AgvTypes.Select(t => t.Type));
            Assert.All(p.Groups, g => Assert.Equal("T_A", g.Type));
            ExcelTemplate.Write(path, Drawing());
            Assert.Equal("T_AGV", Assert.Single(ExcelReader.Read(path).AgvTypes).Type);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Update_Keeps_User_Values_And_Appends_Missing_Types()
    {
        var path = Path.Combine(Path.GetTempPath(), "seed-" + System.Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            ExcelTemplate.Write(path, Drawing(), new[] { Cat("T_A", 1) });
            using (var wb = ExcelReader.Open(path))
            {
                var ws = wb.Worksheet("AGV_Types");
                int col = Enumerable.Range(1, 30).First(c => ws.Cell(1, c).GetString() == "SpeedLoaded");
                ws.Cell(3, col).Value = 9.9; wb.Save();
            }
            var changes = ExcelTemplate.Update(path, Drawing(), new[] { Cat("T_A", 1.0), Cat("T_B", 2) });
            var p = ExcelReader.Read(path);
            Assert.Equal(new[] { "T_A", "T_B" }, p.AgvTypes.Select(t => t.Type));
            Assert.Equal(9.9, p.AgvTypes[0].SpeedLoaded);
            Assert.Single(changes, c => c.Contains("T_B 추가"));
            Assert.DoesNotContain(changes, c => c.Contains("T_A"));
        }
        finally { File.Delete(path); }
    }
}
