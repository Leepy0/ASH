using ASH.Core.Layout;
using ASH.Core.Traffic;

namespace ASH.Tests.Layout;

public class LayerColorsTests
{
    static readonly Dictionary<string, short> None = new();

    [Fact] public void Defaults_Keep_The_Old_Table()
    {
        var m = LayerColors.Merge(None).ToDictionary(c => c.Layer, c => c.Color);
        Assert.Equal(3, m["AGV_PATH"]); Assert.Equal(2, m["AGV_PATH_SPUR"]); Assert.Equal(4, m["AGV_PATH_TWO"]); Assert.Equal(30, m["AGV_PATH_CRAB"]);
        Assert.Equal(8, m["AGV_DIR"]); Assert.Equal(1, m["AGV_CP"]); Assert.Equal(5, m["AGV_BLOCK"]); Assert.Equal(6, m["AGV_STATION"]);
        Assert.Equal(94, m["AGV_CONV"]); Assert.Equal(96, m["AGV_3D_CONV"]); Assert.Equal(9, m["AGV_3D"]); Assert.Equal(8, m["AGV_3D_WALL"]); Assert.Equal(40, m["AGV_3D_FENCE"]);
        Assert.Equal(1, m["AGV_CP_TRAFFIC"]); Assert.Equal(3, m["AGV_CP_LOAD"]); Assert.Equal(5, m["AGV_CP_UNLOAD"]); Assert.Equal(30, m["AGV_CP_RTNLOAD"]);
        Assert.Equal(6, m["AGV_CP_RTNUNLOAD"]); Assert.Equal(2, m["AGV_CP_STANDBY"]); Assert.Equal(4, m["AGV_CP_BUFFER"]); Assert.Equal(8, m["AGV_CP_FINISH"]);
    }

    [Fact] public void Table_Has_One_Row_Per_Layer_And_A_CP_Row_Per_Type()
    {
        Assert.Equal(LayerColors.Table.Count, LayerColors.Table.Select(c => c.Layer).Distinct().Count());
        foreach (var t in CpTypes.All) Assert.Contains(LayerColors.Table, c => c.Layer == "AGV_CP_" + t.ToUpperInvariant());
        Assert.All(LayerColors.Table, c => { Assert.True(LayerColors.IsAci(c.Default)); Assert.NotEmpty(c.Purpose); });
    }

    [Fact] public void CpTypes_ColorIndex_Reads_The_Table()
    {
        foreach (var t in CpTypes.All) Assert.Equal(LayerColors.DefaultOf("AGV_CP_" + t.ToUpperInvariant()), CpTypes.ColorIndex(t));
        Assert.Equal(7, CpTypes.ColorIndex("nope"));
        Assert.Equal(7, CpTypes.ColorIndex(null));
    }

    [Fact] public void Parse_Reads_Pairs_Case_Insensitively_And_Uses_The_Table_Spelling()
    {
        var d = LayerColors.Parse("AGV_PATH=1; agv_cp_standby = 30 ;");
        Assert.Equal(2, d.Count);
        Assert.Equal(1, d["AGV_PATH"]);
        Assert.Equal(30, d["AGV_CP_STANDBY"]);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" ")] [InlineData(";;;")] [InlineData("AGV_PATH")] [InlineData("AGV_PATH=")] [InlineData("=5")]
    [InlineData("AGV_PATH=0")] [InlineData("AGV_PATH=256")] [InlineData("AGV_PATH=-3")] [InlineData("AGV_PATH=red")] [InlineData("AGV_PATH=1.5")]
    [InlineData("NOT_A_LAYER=5")] [InlineData("AGV_PATH=99999999999")]
    public void Parse_Skips_Bad_Input(string? text) => Assert.Empty(LayerColors.Parse(text));

    [Fact] public void Parse_Keeps_Good_Parts_Around_Bad_Ones() =>
        Assert.Equal("AGV_PATH=1;AGV_CP_LOAD=7", LayerColors.Format(LayerColors.Parse("junk;AGV_PATH=1;AGV_DIR=300;AGV_CP_LOAD=7;X=1")));

    [Fact] public void Format_Writes_Only_Changes_In_Table_Order()
    {
        var o = new Dictionary<string, short> { ["AGV_CP_STANDBY"] = 30, ["AGV_PATH"] = 1, ["AGV_DIR"] = 8 /* = default */ };
        Assert.Equal("AGV_PATH=1;AGV_CP_STANDBY=30", LayerColors.Format(o));
        Assert.Equal("", LayerColors.Format(None));
    }

    [Fact] public void Format_Parse_RoundTrip()
    {
        var o = new Dictionary<string, short> { ["AGV_3D_FENCE"] = 255, ["AGV_BLOCK"] = 1 };
        Assert.Equal(o, LayerColors.Parse(LayerColors.Format(o)));
    }

    [Fact] public void Merge_Puts_Overrides_On_Defaults_And_Keeps_Every_Layer()
    {
        var m = LayerColors.Merge(new Dictionary<string, short> { ["AGV_PATH"] = 1 });
        Assert.Equal(LayerColors.Table.Count, m.Count);
        Assert.Equal(1, m.Single(c => c.Layer == "AGV_PATH").Color);
        Assert.Equal(2, m.Single(c => c.Layer == "AGV_PATH_SPUR").Color);
        Assert.Equal(LayerColors.Table.Select(c => c.Layer), m.Select(c => c.Layer));
    }

    [Fact] public void Effective_Ignores_Out_Of_Range_Override_And_Unknown_Layer()
    {
        Assert.Equal(3, LayerColors.Effective(new Dictionary<string, short> { ["AGV_PATH"] = 999 }, "AGV_PATH"));
        Assert.Equal(7, LayerColors.Effective(None, "OTHER"));
        Assert.Equal(1, LayerColors.Effective(new Dictionary<string, short> { ["AGV_PATH"] = 1 }, "agv_path"));
    }

    [Theory]
    [InlineData(1, 255, 0, 0)] [InlineData(2, 255, 255, 0)] [InlineData(3, 0, 255, 0)] [InlineData(5, 0, 0, 255)] [InlineData(8, 128, 128, 128)]
    [InlineData(10, 255, 0, 0)] [InlineData(11, 255, 127, 127)] [InlineData(12, 204, 0, 0)] [InlineData(30, 255, 127, 0)] [InlineData(40, 255, 191, 0)]
    [InlineData(90, 0, 255, 0)] [InlineData(94, 0, 153, 0)] [InlineData(170, 0, 0, 255)] [InlineData(250, 51, 51, 51)] [InlineData(255, 255, 255, 255)]
    public void AciColors_Rgb_Matches_The_Standard_Palette(int aci, int r, int g, int b) => Assert.Equal((r, g, b), AciColors.Rgb(aci));

    [Fact] public void AciColors_OnWhite_Darkens_Bright_Colours_Only()
    {
        Assert.Equal((255, 0, 0), AciColors.OnWhite(1));
        Assert.Equal((0, 0, 0), AciColors.OnWhite(7));
        var (r, g, b) = AciColors.OnWhite(2);
        Assert.True(0.299 * r + 0.587 * g + 0.114 * b <= 0.55 * 255 + 1);
        Assert.Equal(r, g);
        Assert.Equal(0, b);
    }
}
