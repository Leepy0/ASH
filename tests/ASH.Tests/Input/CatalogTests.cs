using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using ASH.Core.Catalog;
using ASH.Core.Input;
using ASH.Core.Layout;

namespace ASH.Tests.Input;

public class CatalogTests
{
    static CatalogType TA() => new CatalogType
    {
        Name = "T_A", Kind = "Standard",
        Defaults = { SpeedLoadedStraight = 1, SpeedLoadedCurve = 0.5, SpeedEmptyStraight = 1.5, SpeedEmptyCurve = 1.5, Capacity = 1, Color = "Blue" },
    };

    [Fact]
    public void ToTemplateRow_Matches_Sheet_Columns()
    {
        var cols = ExcelTemplate.Sheets.First(s => s.Sheet == "AGV_Types").Cols;
        var row = CatalogType.ToTemplateRow(TA());
        Assert.Equal(cols.Length, row.Length);
        Assert.Equal("T_A", row[0]); Assert.Equal("0.5", row[3]); Assert.Equal("", row[5]); Assert.Equal("", row[17]);
        Assert.Equal("Standard", row[Array.IndexOf(cols, "Kind")]);
    }

    [Fact]
    public void CatalogDiff_Warns_Only_On_Differing_Known_Types()
    {
        var cat = new List<CatalogType> { new CatalogType { Name = "T_A", Defaults = { SpeedLoadedStraight = 1.0, SpeedEmptyStraight = 1.0, Capacity = 1, Length = 1, Width = 1 } } };
        var p = new ProjectInput();
        p.AgvTypes.Add(new AgvType("T_A", 1.2, 1.0, 0, 0, 1, 0, 0, 0, 0, 1, 1, "Blue"));
        p.AgvTypes.Add(new AgvType("T_B", 9, 9, 0, 0, 1, 0, 0, 0, 0, 1, 1, "Blue"));
        var w = Assert.Single(InputValidator.CatalogDiff(p, cat));
        Assert.Contains("T_A", w.Message); Assert.Contains("SpeedLoaded", w.Message);
        Assert.Equal(FindingCode.InputWarning, w.Code);
    }

    [Fact]
    public void Validate_Curve_Speed_Rules()
    {
        ProjectInput With(double curve) { var p = new ProjectInput(); p.AgvTypes.Add(new AgvType("T1", 1, 1, 0, 0, 1, 0, 0, 0, 0, 1, 1, "Blue", SpeedLoadedCurve: curve)); return p; }
        Assert.Contains(InputValidator.Validate(With(-1), null), x => x.Code == FindingCode.InputError && x.Message.Contains("곡선"));
        Assert.Contains(InputValidator.Validate(With(2), null), x => x.Code == FindingCode.InputWarning && x.Message.Contains("곡선"));
    }

    [Fact]
    public void CatalogTypes_RoundTrips_Through_DataContractJson()
    {
        var json = "{\"types\":[{\"name\":\"T_A\",\"label\":\"표준\",\"category\":\"AGV\",\"kind\":\"Standard\",\"defaults\":{\"speedLoadedStraight\":1.0,\"capacity\":2},\"assets\":[\"a.zip\"]}]}";
        var ser = new DataContractJsonSerializer(typeof(CatalogTypes));
        var c = (CatalogTypes)ser.ReadObject(new MemoryStream(Encoding.UTF8.GetBytes(json)))!;
        Assert.Equal("T_A", c.Types[0].Name); Assert.Equal(2, c.Types[0].Defaults.Capacity); Assert.Equal("a.zip", c.Types[0].Assets[0]);
        var ms = new MemoryStream(); ser.WriteObject(ms, c);
        var back = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("\"name\":\"T_A\"", back); Assert.DoesNotContain("hidden", back);
#if NET
        var sj = System.Text.Json.JsonSerializer.Deserialize<CatalogTypes>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.Equal("T_A", sj!.Types[0].Name);
#endif
    }
}
