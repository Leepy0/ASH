using ASH.Core.Input;
using ClosedXML.Excel;
using InSettings = ASH.Core.Input.Settings;

namespace ASH.Tests.Input;

[Xunit.Collection("ExcelTemplateStatics")]
public class ExcelRoundTripTests
{
    static string Tmp() => Path.Combine(Path.GetTempPath(), "ash_" + Guid.NewGuid().ToString("N") + ".xlsx");

    static readonly string[] TypeCols = { "Type", "Category", "SpeedLoaded", "SpeedLoadedCurve", "SpeedEmpty", "SpeedEmptyCurve", "Accel", "Decel", "Capacity", "LoadTime", "UnloadTime", "MTBF", "MTTR", "Length", "Width", "Color", "Kind", "Asset" };

    [Fact]
    public void Template_Has_Curve_Category_Asset_Columns_In_Order()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path);
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet("AGV_Types");
            for (int c = 0; c < TypeCols.Length; c++) Assert.Equal(TypeCols[c], ws.Cell(1, c + 1).GetString());
            Assert.Equal("", ws.Cell(1, TypeCols.Length + 1).GetString());
            Assert.NotEqual("", ws.Cell(2, TypeCols.Length).GetString());
            Assert.Equal("", ws.Cell(2, TypeCols.Length + 1).GetString());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_Without_Curve_Columns_Gives_Null_Curve()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path);
            using (var wb = new XLWorkbook(path))
            {
                var ws = wb.Worksheet("AGV_Types");
                foreach (var name in new[] { "Asset", "SpeedEmptyCurve", "SpeedLoadedCurve", "Category" })
                    ws.Column(Array.IndexOf(TypeCols, name) + 1).Delete();
                wb.Save();
            }
            var t = ExcelReader.Read(path).AgvTypes[0];
            Assert.Null(t.SpeedLoadedCurve); Assert.Null(t.SpeedEmptyCurve);
            Assert.Equal("", t.Category); Assert.Equal("", t.Asset);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_Curve_Dash_Or_Blank_Is_Null_And_Number_Is_Kept()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path);
            using (var wb = new XLWorkbook(path))
            {
                var ws = wb.Worksheet("AGV_Types");
                ws.Cell(3, 4).Value = "-"; ws.Cell(3, 6).Value = "0.7";
                wb.Save();
            }
            var t = ExcelReader.Read(path).AgvTypes[0];
            Assert.Null(t.SpeedLoadedCurve); Assert.Equal(0.7, t.SpeedEmptyCurve);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Template_Then_Read_RoundTrips_Defaults()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path);
            using (var wb = new XLWorkbook(path))
            {
                Assert.Equal(5, wb.Worksheets.Count);
                foreach (var (sheet, cols, _, _) in ExcelTemplate.Sheets)
                    for (int c = 0; c < cols.Length; c++)
                        Assert.Equal(cols[c], wb.Worksheet(sheet).Cell(1, c + 1).GetString());
            }
            var p = ExcelReader.Read(path);
            Assert.Equal(new InSettings(), p.Settings);
            Assert.Equal(28800, p.Settings.SimLengthSec);
            Assert.Single(p.Materials);
            Assert.Equal("M001", p.Materials[0].Material);
            Assert.Empty(InputValidator.Validate(p, new[] { "CP_STBY", "CP_LOAD", "CP_UNLOAD" }));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_Parses_Sample_Rows()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path);
            using (var wb = new XLWorkbook(path))
            {
                SetSetting(wb, "SimLength", "2.5");
                var m = wb.Worksheet("Materials");
                m.Cell(3, 1).Value = "M9"; m.Cell(3, 4).Value = "45.5"; m.Cell(3, 13).Value = 3; m.Cell(3, 14).Value = "-";
                wb.Worksheet("AGV_Groups").Cell(3, 5).Value = "A, B";
                wb.Worksheet("Routes").Cell(3, 4).Value = "CP_BUF";
                wb.Save();
            }
            var p = ExcelReader.Read(path);
            Assert.Equal(9000, p.Settings.SimLengthSec);
            Assert.Equal("M9", p.Materials[0].Material);
            Assert.Equal(45.5, p.Materials[0].TaktTime);
            Assert.Equal(3.0, p.Materials[0].InitStock);
            Assert.Null(p.Materials[0].SafetyStock);
            Assert.False(p.Materials[0].EmptyReturn);
            Assert.Equal(new[] { "A", "B" }, p.Groups[0].StandbyCPs);
            Assert.Equal("CP_BUF", p.Routes[0].BufferCP);
            Assert.Null(p.Routes[0].ReturnFromCP);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_Ignores_Unknown_Columns()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path);
            using (var wb = new XLWorkbook(path))
            {
                var m = wb.Worksheet("Materials");
                m.Column(1).InsertColumnsBefore(1); // shifts every known column right
                m.Cell(1, 1).Value = "Corp"; m.Cell(3, 1).Value = "ACME";
                int last = m.LastColumnUsed()!.ColumnNumber();
                m.Cell(1, last + 1).Value = "ProductLine"; m.Cell(3, last + 1).Value = "P1";
                var g = wb.Worksheet("AGV_Groups");
                g.Cell(1, 7).Value = "Line"; g.Cell(3, 7).Value = "L1";
                wb.Save();
            }
            var p = ExcelReader.Read(path);
            Assert.Equal("M001", p.Materials[0].Material);
            Assert.Equal(new InSettings(), p.Settings);
            Assert.Equal("G_EX", p.Groups[0].Group);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_Bad_Number_Reports_Sheet_Row_Column()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path);
            using (var wb = new XLWorkbook(path)) { wb.Worksheet("Materials").Cell(3, 4).Value = "abc"; wb.Save(); }
            var ex = Assert.Throws<ExcelDataException>(() => ExcelReader.Read(path));
            Assert.Contains("Materials", ex.Message);
            Assert.Contains("3행", ex.Message);
            Assert.Contains("TaktTime", ex.Message);
        }
        finally { File.Delete(path); }
    }

    static void SetSetting(XLWorkbook wb, string key, string value)
    {
        var ws = wb.Worksheet("Settings");
        for (int r = 3; r < 40; r++)
            if (ws.Cell(r, 1).GetString() == key) { ws.Cell(r, 2).Value = value; return; }
        throw new InvalidOperationException(key);
    }

    [Fact]
    public void Read_Skips_Blank_Rows_And_Records_Source_Row()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path);
            using (var wb = new XLWorkbook(path))
            {
                var ws = wb.Worksheet("Routes");
                ws.Cell(5, 1).Value = "M002"; ws.Cell(5, 2).Value = "CP2"; ws.Cell(5, 3).Value = "CP3"; // row 4 blank
                wb.Save();
            }
            var p = ExcelReader.Read(path);
            Assert.Equal(2, p.Routes.Count);
            Assert.Equal(new[] { 3, 5 }, p.Routes.Select(s => s.Row));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("", "", false)] [InlineData("RL1", "", true)] [InlineData("", "RU1", true)] [InlineData("RL1", "RU1", true)]
    public void EmptyReturn_Follows_The_Route_Return_CPs(string from, string to, bool expected)
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path);
            using (var wb = new XLWorkbook(path))
            {
                var r = wb.Worksheet("Routes"); r.Cell(3, 5).Value = from; r.Cell(3, 6).Value = to;
                var m = wb.Worksheet("Materials"); m.Cell(1, 19).Value = "EmptyReturn"; m.Cell(3, 19).Value = "Y"; // an old column is ignored
                wb.Save();
            }
            Assert.Equal(expected, ExcelReader.Read(path).Materials[0].EmptyReturn);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("4", "", 4.0)] [InlineData("", "25", 2.5)] [InlineData("1", "5", 1.5)] [InlineData("-", "30", 3.0)]
    public void Threshold_Adds_Containers_And_Units(string boxes, string units, double expected)
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path); // UnitsPerContainer 10
            using (var wb = new XLWorkbook(path))
            {
                var m = wb.Worksheet("Materials"); m.Cell(3, 7).Value = boxes; m.Cell(3, 8).Value = units; m.Cell(3, 10).Value = boxes; m.Cell(3, 11).Value = units;
                wb.Save();
            }
            var mat = ExcelReader.Read(path).Materials[0];
            Assert.Equal(expected, mat.CallThreshold, 9); Assert.Equal(expected, mat.UnloadThreshold, 9);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Threshold_With_Both_Parts_Blank_Names_The_Column()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path);
            using (var wb = new XLWorkbook(path)) { wb.Worksheet("Materials").Cell(3, 7).Value = ""; wb.Save(); }
            Assert.Contains("CallThreshold", Assert.Throws<ExcelDataException>(() => ExcelReader.Read(path)).Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_Missing_Sheet_Throws_With_Name()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path);
            using (var wb = new XLWorkbook(path)) { wb.Worksheet("Routes").Delete(); wb.Save(); }
            Assert.Contains("Routes", Assert.Throws<FormatException>(() => ExcelReader.Read(path)).Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_Warns_On_Unknown_And_Duplicate_Settings_Keys_And_Rejects_Fractional_Int()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path);
            using (var wb = new XLWorkbook(path))
            {
                var ws = wb.Worksheet("Settings");
                ws.Cell(30, 1).Value = "Bogus"; ws.Cell(30, 2).Value = "1";
                ws.Cell(31, 1).Value = "SeedBase"; ws.Cell(31, 2).Value = 7;
                wb.Save();
            }
            ExcelReader.Read(path, out var w);
            Assert.Equal(2, w.Count);
            Assert.All(w, x => Assert.Equal(ASH.Core.Layout.FindingCode.InputWarning, x.Code));
            using (var wb = new XLWorkbook(path)) { SetSetting(wb, "Replications", "2.5"); wb.Save(); }
            Assert.Contains("Replications", Assert.Throws<FormatException>(() => ExcelReader.Read(path)).Message);
        }
        finally { File.Delete(path); }
    }
}
