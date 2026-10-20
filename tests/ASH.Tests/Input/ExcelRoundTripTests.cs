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
                wb.Worksheet("Settings").Cell(4, 2).Value = "2.5"; // SimLength is row 4 (Units row 3)
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
}
