using System.IO;
using System.Linq;
using ASH.Core.Input;
using ClosedXML.Excel;
using Xunit;

namespace ASH.Tests.Input;

[Collection("ExcelTemplateStatics")]
public class AssetColumnTests
{
    static string Tmp() => Path.Combine(Path.GetTempPath(), "asset_" + System.Guid.NewGuid().ToString("N") + ".xlsx");

    static int AssetCol(IXLWorksheet ws) => Enumerable.Range(1, 40).First(c => ws.Cell(1, c).GetString() == "Asset");

    [Fact]
    public void Reader_Reads_Asset_When_Column_Exists()
    {
        var p = Tmp();
        ExcelTemplate.Write(p);
        using (var wb = new XLWorkbook(p))
        {
            var ws = wb.Worksheet("AGV_Types");
            ws.Cell(3, 1).Value = "T1"; ws.Cell(3, AssetCol(ws)).Value = "agv_a";
            wb.Save();
        }
        Assert.Equal("agv_a", ExcelReader.Read(p).AgvTypes[0].Asset);
        File.Delete(p);
    }

    [Fact]
    public void Reader_Without_Column_Is_Empty()
    {
        var p = Tmp();
        ExcelTemplate.Write(p);
        using (var wb = new XLWorkbook(p))
        {
            var ws = wb.Worksheet("AGV_Types");
            ws.Cell(3, 1).Value = "T1";
            ws.Column(AssetCol(ws)).Delete();
            wb.Save();
        }
        Assert.Equal("", ExcelReader.Read(p).AgvTypes[0].Asset);
        File.Delete(p);
    }

    [Fact]
    public void Template_Adds_Asset_Validation_When_List_Set()
    {
        var p = Tmp();
        try
        {
            ExcelTemplate.AssetListFormula = "agv_a,agv_b";
            ExcelTemplate.Write(p);
            using var wb = new XLWorkbook(p);
            var ws = wb.Worksheet("AGV_Types");
            var c = AssetCol(ws);
            Assert.Contains(ws.DataValidations, v => v.Ranges.Any(r => r.RangeAddress.FirstAddress.ColumnNumber == c) && v.Value.Contains("agv_a,agv_b"));

            ExcelTemplate.AssetListFormula = "";
            ExcelTemplate.Write(p);
            using var wb2 = new XLWorkbook(p);
            var ws2 = wb2.Worksheet("AGV_Types");
            Assert.DoesNotContain(ws2.DataValidations, v => v.Ranges.Any(r => r.RangeAddress.FirstAddress.ColumnNumber == c));
        }
        finally { ExcelTemplate.AssetListFormula = ""; File.Delete(p); }
    }
}
