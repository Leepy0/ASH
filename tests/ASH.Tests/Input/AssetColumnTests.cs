using System.IO;
using System.Linq;
using ASH.Core.Input;
using ASH.Core.Layout;
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

    [Fact]
    public void Asset_Validation_Warns_Others_Stop()
    {
        var p = Tmp();
        try
        {
            ExcelTemplate.AssetListFormula = "agv_a,agv_b";
            ExcelTemplate.Write(p);
            using var wb = new XLWorkbook(p);
            var ws = wb.Worksheet("AGV_Types");
            var c = AssetCol(ws);
            var a = ws.DataValidations.Single(v => v.Ranges.Any(r => r.RangeAddress.FirstAddress.ColumnNumber == c));
            Assert.Equal(XLErrorStyle.Warning, a.ErrorStyle);
            Assert.Contains("카탈로그에 없는", a.ErrorMessage);
            Assert.Contains(wb.Worksheets.SelectMany(w => w.DataValidations), v => v.ErrorStyle == XLErrorStyle.Stop);
        }
        finally { ExcelTemplate.AssetListFormula = ""; File.Delete(p); }
    }

    [Fact]
    public void Long_Asset_List_Goes_To_Hidden_Sheet()
    {
        var p = Tmp();
        try
        {
            var names = Enumerable.Range(0, 40).Select(i => "asset_" + i.ToString("D4")).ToArray(); // 40 x 10 chars
            ExcelTemplate.AssetListFormula = string.Join(",", names);
            ExcelTemplate.Write(p);
            using var wb = new XLWorkbook(p);
            var ls = wb.Worksheet("_lists");
            Assert.Equal(XLWorksheetVisibility.Hidden, ls.Visibility);
            Assert.Equal("asset_0039", ls.Cell(40, 1).GetString());
            var ws = wb.Worksheet("AGV_Types");
            var c = AssetCol(ws);
            var v = ws.DataValidations.Single(x => x.Ranges.Any(r => r.RangeAddress.FirstAddress.ColumnNumber == c));
            Assert.Contains("_lists", v.Value);
            Assert.DoesNotContain("asset_0000,", v.Value);
        }
        finally { ExcelTemplate.AssetListFormula = ""; File.Delete(p); }
    }

    [Fact]
    public void CatalogDiff_Warns_On_Unknown_Asset_Only_When_Names_Given()
    {
        var pi = new ProjectInput();
        pi.AgvTypes.Add(new AgvType("T1", 1, 1, 0, 0, 1, 0, 0, 0, 0, 1, 1, "Blue", Row: 5, Asset: "nope"));
        pi.AgvTypes.Add(new AgvType("T2", 1, 1, 0, 0, 1, 0, 0, 0, 0, 1, 1, "Blue", Row: 6, Asset: "agv_a"));
        var cat = new System.Collections.Generic.List<ASH.Core.Catalog.CatalogType>();
        Assert.Empty(InputValidator.CatalogDiff(pi, cat));
        var w = Assert.Single(InputValidator.CatalogDiff(pi, cat, new[] { "agv_a" }));
        Assert.Equal("AGV_Types!5", w.EntityId);
        Assert.Equal(FindingCode.InputWarning, w.Code);
        Assert.Contains("'nope'", w.Message);
    }
}
