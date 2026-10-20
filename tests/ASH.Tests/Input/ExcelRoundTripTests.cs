using ASH.Core.Input;
using ClosedXML.Excel;
using InSettings = ASH.Core.Input.Settings;

namespace ASH.Tests.Input;

public class ExcelRoundTripTests
{
    static string Tmp() => Path.Combine(Path.GetTempPath(), "ash_" + Guid.NewGuid().ToString("N") + ".xlsx");

    [Fact]
    public void Template_Then_Read_RoundTrips_Defaults()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path);
            using (var wb = new XLWorkbook(path))
            {
                Assert.Equal(7, wb.Worksheets.Count);
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
                m.Cell(3, 1).Value = "M9"; m.Cell(3, 4).Value = "45.5"; m.Cell(3, 9).Value = 3; m.Cell(3, 11).Value = "N";
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
    public void Read_Bad_Number_Reports_Sheet_Row_Column()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path);
            using (var wb = new XLWorkbook(path)) { wb.Worksheet("Materials").Cell(3, 4).Value = "abc"; wb.Save(); }
            var ex = Assert.Throws<FormatException>(() => ExcelReader.Read(path));
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
                var ws = wb.Worksheet("Stations");
                ws.Cell(5, 1).Value = "S2"; ws.Cell(5, 2).Value = "CP2"; ws.Cell(5, 3).Value = 1; // row 4 blank
                wb.Save();
            }
            var p = ExcelReader.Read(path);
            Assert.Equal(2, p.Stations.Count);
            Assert.Equal(new[] { 3, 5 }, p.Stations.Select(s => s.Row));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("Y", true)] [InlineData("y", true)] [InlineData("TRUE", true)] [InlineData("1", true)]
    [InlineData("N", false)] [InlineData("false", false)] [InlineData("", false)]
    public void Read_Parses_YesNo_Variants(string text, bool expected)
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path);
            using (var wb = new XLWorkbook(path)) { wb.Worksheet("Materials").Cell(3, 11).Value = text; wb.Save(); }
            Assert.Equal(expected, ExcelReader.Read(path).Materials[0].EmptyReturn);
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
