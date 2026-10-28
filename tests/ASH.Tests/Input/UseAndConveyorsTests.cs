using System.Collections.Generic;
using System.IO;
using System.Linq;
using ASH.Core.Input;
using ASH.Core.Layout;
using ClosedXML.Excel;
using Xunit;

namespace ASH.Tests.Input;

/// <summary>Materials.Use, the Conveyors sheet and the centred cells of the template.</summary>
[Collection("ExcelTemplateStatics")]
public class UseAndConveyorsTests
{
    static string Tmp(string ext = ".xlsx") => Path.Combine(Path.GetTempPath(), "ash_" + System.Guid.NewGuid().ToString("N") + ext);

    static LayoutCp Cp(string name, string type, string group) => new LayoutCp { Name = name, Type = type, Group = group };

    /// <summary>Template with three materials M001..M003 (rows 3..5), a route each; <paramref name="edit"/> changes the workbook before it is read.</summary>
    static ProjectInput ReadThree(System.Action<XLWorkbook>? edit = null)
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path);
            using (var wb = ExcelReader.Open(path))
            {
                var m = wb.Worksheet("Materials"); // the route is on the material's row
                for (int i = 1; i <= 2; i++)
                {
                    m.Row(3).CopyTo(m.Row(3 + i));
                    m.Cell(3 + i, 1).Value = "M00" + (i + 1);
                }
                edit?.Invoke(wb);
                wb.Save();
            }
            return ExcelReader.Read(path);
        }
        finally { File.Delete(path); }
    }

    static int Col(IXLWorksheet ws, string header) => Enumerable.Range(1, 40).First(c => ws.Cell(1, c).GetString() == header);

    static List<Finding> Check(ProjectInput p) => InputValidator.Validate(p, null);

    // ---- centring ----

    static void AssertCentered(XLWorkbook wb)
    {
        foreach (var (sheet, cols, _, _) in ExcelTemplate.Sheets)
        {
            var ws = wb.Worksheet(sheet);
            foreach (var cell in ws.CellsUsed(XLCellsUsedOptions.Contents).Where(x => !x.HasFormula)) // every typed value, appended rows included
                Assert.True(cell.Style.Alignment.Horizontal == XLAlignmentHorizontalValues.Center, $"{sheet} {cell.Address}");
            for (int c = 1; c <= cols.Length; c++)
            {
                foreach (var r in new[] { 1, 2 }) // header, description
                    Assert.True(ws.Cell(r, c).Style.Alignment.Horizontal == XLAlignmentHorizontalValues.Center, $"{sheet} ({r},{c})");
                // the column's own style, which a cell typed in later takes
                Assert.True(ws.Column(c).Style.Alignment.Horizontal == XLAlignmentHorizontalValues.Center, $"{sheet} column {c}");
            }
        }
    }

    [Fact]
    public void Template_Cells_Are_Centered_On_Every_Sheet()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path, null);
            using var wb = ExcelReader.Open(path);
            AssertCentered(wb);
            Assert.Equal(60, wb.Worksheet("Materials").Cell(3, Col(wb.Worksheet("Materials"), "TaktTime")).GetDouble()); // values are untouched
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Update_Centers_Added_Columns_Rows_And_Sheets_And_Logs_It()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path);
            using (var wb = ExcelReader.Open(path)) // an older workbook: left-aligned, no Use column, no Conveyors sheet
            {
                foreach (var ws in wb.Worksheets) ws.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                wb.Worksheet("Materials").Column(Col(wb.Worksheet("Materials"), "Use")).Delete();
                wb.Worksheet("Conveyors").Delete();
                wb.Save();
            }
            var l = new ASH.Core.Layout.Layout();
            l.Cps.Add(Cp("L009", "load", "G_EX")); l.Cps.Add(Cp("U009", "unload", "G_EX")); // a new load CP: a material and a route are appended
            var log = new List<ExcelEdit>();
            using (var wb = ExcelReader.Open(path))
            {
                var changes = ExcelTemplate.Update(wb, l, log);
                Assert.Contains(changes, c => c.Contains("Materials: Use 열 추가"));
                Assert.Contains(changes, c => c.Contains("Conveyors 시트 추가"));
                Assert.Contains(changes, c => c.Contains("L009"));
                AssertCentered(wb);
                Assert.Contains(log, e => e is AlignEdit a && a.Sheet == "Materials" && a.FirstCol == 1 && a.LastCol >= 20);
                wb.SaveAs(path);
            }
            using var back = ExcelReader.Open(path);
            AssertCentered(back);
        }
        finally { File.Delete(path); }
    }

    // ---- Use ----

    [Fact]
    public void Template_Has_Use_Column_With_Description_And_Y_N_List()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path);
            using var wb = ExcelReader.Open(path);
            var m = wb.Worksheet("Materials");
            int c = Col(m, "Use");
            Assert.Equal("사용 여부", m.Cell(2, c).GetString());
            Assert.Equal("\"Y,N\"", m.Cell(3, c).GetDataValidation().Value);
            Assert.Equal("\"Y,N\"", m.Cell(1000, c).GetDataValidation().Value);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("", true)] [InlineData("Y", true)] [InlineData("y", true)] [InlineData("예", true)] [InlineData("1", true)]
    [InlineData("N", false)] [InlineData("n", false)] [InlineData("아니오", false)] [InlineData("0", false)]
    public void Use_Values_Are_Read(string value, bool used)
    {
        var p = ReadThree(wb => wb.Worksheet("Materials").Cell(4, Col(wb.Worksheet("Materials"), "Use")).Value = value);
        Assert.Equal(used, p.Materials.Any(m => m.Material == "M002"));
        Assert.Equal(used, p.Routes.Any(r => r.Material == "M002"));
        Assert.Equal(used ? 0 : 1, p.DisabledMaterials.Count);
    }

    [Fact]
    public void Use_Number_Cell_Is_Read()
    {
        var p = ReadThree(wb => wb.Worksheet("Materials").Cell(4, Col(wb.Worksheet("Materials"), "Use")).Value = 0);
        Assert.Equal(new[] { "M002" }, p.DisabledMaterials);
    }

    [Fact]
    public void Use_Other_Value_Names_The_Cell()
    {
        var ex = Assert.Throws<ExcelDataException>(() => ReadThree(wb => wb.Worksheet("Materials").Cell(5, Col(wb.Worksheet("Materials"), "Use")).Value = "maybe"));
        Assert.Contains("Materials 시트 5행 'Use' 열", ex.Message);
        Assert.Contains("maybe", ex.Message);
    }

    [Fact]
    public void Disabled_Material_Is_Removed_With_Its_Route_And_Reported_By_The_Validator()
    {
        var p = ReadThree(wb =>
        {
            var m = wb.Worksheet("Materials"); int u = Col(m, "Use");
            m.Cell(3, u).Value = "N"; m.Cell(5, u).Value = "N";
            m.Cell(3, Col(m, "TaktTime")).Value = "garbage"; // a switched-off row is not parsed
        });
        Assert.Equal(new[] { "M002" }, p.Materials.Select(m => m.Material));
        Assert.Equal(new[] { "M002" }, p.Routes.Select(r => r.Material));
        Assert.Equal(new[] { "M001", "M003" }, p.DisabledMaterials);
        var f = Check(p);
        Assert.Contains(f, x => x.Code == FindingCode.InputWarning && x.Message == "미사용 자재 2개 제외: M001, M003");
        Assert.DoesNotContain(f, x => x.Code == FindingCode.InputError);
    }

    [Fact]
    public void All_Materials_Disabled_Is_A_Validation_Error()
    {
        var p = ReadThree(wb => { var m = wb.Worksheet("Materials"); int u = Col(m, "Use"); for (int r = 3; r <= 5; r++) m.Cell(r, u).Value = "N"; });
        Assert.Empty(p.Materials);
        Assert.Contains(Check(p), x => x.Code == FindingCode.InputError && x.Message.Contains("모든 자재의 Use가 N"));
    }

    [Fact]
    public void Workbook_Without_Use_Column_Uses_Everything_And_Update_Adds_The_Column()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path);
            using (var wb = ExcelReader.Open(path)) { var m = wb.Worksheet("Materials"); m.Column(Col(m, "Use")).Delete(); wb.Save(); }
            var p = ExcelReader.Read(path);
            Assert.Single(p.Materials); Assert.Empty(p.DisabledMaterials);
            Assert.Contains(ExcelTemplate.Update(path, null), c => c == "Materials: Use 열 추가");
            using var wb2 = ExcelReader.Open(path);
            Assert.Equal("Use", wb2.Worksheet("Materials").Cell(1, ExcelTemplate.Sheets.First(x => x.Sheet == "Materials").Cols.Length).GetString()); // appended last
            Assert.Single(ExcelReader.Read(path).Materials);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Disabled_Materials_Also_Apply_When_The_Server_Reads_The_Cell_Snapshot()
    {
        var xlsx = Tmp(); var tsv = Tmp(".tsv");
        try
        {
            ExcelTemplate.Write(xlsx);
            using (var wb = ExcelReader.Open(xlsx)) { var m = wb.Worksheet("Materials"); m.Cell(3, Col(m, "Use")).Value = "N"; CellDump.Write(wb, tsv); }
            using var back = CellDump.Load(tsv);
            var p = ExcelReader.Read(back, out _);
            Assert.Equal(new[] { "M001" }, p.DisabledMaterials);
            Assert.Empty(p.Materials); Assert.Empty(p.Routes);
        }
        finally { File.Delete(xlsx); File.Delete(tsv); }
    }

    // ---- Conveyors ----

    static void AddConveyor(XLWorkbook wb, int row, params string[] values)
    {
        var ws = wb.Worksheet("Conveyors");
        for (int c = 0; c < values.Length; c++) if (values[c].Length > 0) ws.Cell(row, c + 1).Value = values[c];
    }

    [Fact]
    public void Template_Has_An_Empty_Conveyors_Sheet_With_Headers_And_Material_List()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path);
            using var wb = ExcelReader.Open(path);
            var ws = wb.Worksheet("Conveyors");
            Assert.Equal(new[] { "Conveyor", "Material", "ProductTakt", "Speed", "Capacity", "ConsumeAt" }, Enumerable.Range(1, 6).Select(c => ws.Cell(1, c).GetString()));
            Assert.All(Enumerable.Range(1, 6), c => Assert.NotEqual("", ws.Cell(2, c).GetString()));
            Assert.All(Enumerable.Range(1, 6), c => Assert.Equal("", ws.Cell(3, c).GetString()));
            Assert.Contains("Materials", ws.Cell(3, 2).GetDataValidation().Value);
            Assert.Empty(ExcelReader.Read(path).Conveyors);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Conveyor_Row_Defaults_And_Overrides_The_Materials_Takt()
    {
        var p = ReadThree(wb =>
        {
            AddConveyor(wb, 3, "CV001", "M002");
            AddConveyor(wb, 4, "CV002", "M003", "45", "0.5", "6", "12.5");
            var m = wb.Worksheet("Materials");
            m.Cell(5, Col(m, "TaktTime")).Value = ""; // blank T/T is fine for a conveyor-fed material
            m.Cell(5, Col(m, "UnitsPerCycle")).Value = 2; // one product takes the material's UnitsPerCycle
        });
        Assert.Equal(new ConveyorInfo("CV001", "M002", 60, 1, 0.3, null, null, 3), p.Conveyors[0]);
        Assert.Equal(new ConveyorInfo("CV002", "M003", 45, 2, 0.5, 6, 12.5, 4), p.Conveyors[1]);
        var m1 = p.Materials.Single(m => m.Material == "M001");
        var m3 = p.Materials.Single(m => m.Material == "M003");
        Assert.False(m1.ConveyorFed); Assert.Equal(60, m1.TaktTime);
        Assert.True(m3.ConveyorFed); Assert.Equal(45, m3.TaktTime); Assert.Equal(2, m3.UnitsPerCycle);
        Assert.True(p.Materials.Single(m => m.Material == "M002").ConveyorFed);
        var info = Check(p).Where(f => f.Code == FindingCode.InputWarning && f.Message.Contains("컨베이어")).ToList();
        Assert.Equal(2, info.Count);
        Assert.Contains(info, f => f.Message.Contains("'M003'") && f.Message.Contains("CV002") && f.Message.Contains("무시"));
    }

    [Fact]
    public void Conveyor_Validation()
    {
        var p = ReadThree(wb =>
        {
            AddConveyor(wb, 3, "CV001", "NOPE");
            AddConveyor(wb, 4, "CV001", "M001");          // duplicate name
            AddConveyor(wb, 5, "CV002", "M001", "-5");    // second conveyor for M001, bad takt
            AddConveyor(wb, 6, "CV003", "M002", "", "0", "0", "-1");
        });
        var err = Check(p).Where(f => f.Code == FindingCode.InputError).ToList();
        string At(string sheetRow) => string.Join("|", err.Where(e => e.EntityId == sheetRow).Select(e => e.Message));
        Assert.Contains("Materials에 없습니다", At("Conveyors!3"));
        Assert.Contains("중복", At("Conveyors!4"));
        Assert.Contains("함께 소비", At("Conveyors!5")); Assert.Contains("ProductTakt", At("Conveyors!5"));
        var last = At("Conveyors!6");
        foreach (var w in new[] { "Speed", "Capacity", "ConsumeAt" }) Assert.Contains(w, last);
    }

    [Fact]
    public void Conveyor_Of_A_Disabled_Material_Is_Dropped()
    {
        var p = ReadThree(wb =>
        {
            var m = wb.Worksheet("Materials"); m.Cell(4, Col(m, "Use")).Value = "N";
            AddConveyor(wb, 3, "CV001", "M002");
            AddConveyor(wb, 4, "CV002", "M003");
        });
        Assert.Equal("CV002", Assert.Single(p.Conveyors).Conveyor);
        Assert.DoesNotContain(Check(p), f => f.Code == FindingCode.InputError);
    }

    [Fact]
    public void Conveyor_Capacity_Must_Be_A_Whole_Number()
    {
        var ex = Assert.Throws<ExcelDataException>(() => ReadThree(wb => AddConveyor(wb, 3, "CV001", "M001", "", "", "2.5")));
        Assert.Contains("Capacity", ex.Message);
    }

    [Fact]
    public void Workbook_Without_Conveyors_Sheet_Reads_Fine()
    {
        var p = ReadThree(wb => wb.Worksheet("Conveyors").Delete());
        Assert.Empty(p.Conveyors); Assert.False(p.Materials[0].ConveyorFed);
    }
}
