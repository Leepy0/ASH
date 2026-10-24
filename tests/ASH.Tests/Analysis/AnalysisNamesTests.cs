using ASH.Core.Analysis;

namespace ASH.Tests.Analysis;

public class AnalysisNamesTests
{
    [Theory]
    [InlineData(new string[0], "A안")] [InlineData(new[] { "A안" }, "B안")] [InlineData(new[] { "A안", "C안", "기타" }, "B안")]
    public void NextPlanName_Takes_The_First_Free_Letter(string[] existing, string want) => Assert.Equal(want, AnalysisNames.NextPlanName(existing));

    [Fact] public void NextPlanName_After_Z_Is_Generic() => Assert.Equal("새 안", AnalysisNames.NextPlanName(Enumerable.Range(0, 26).Select(i => (char)('A' + i) + "안")));

    [Fact] public void NextPlanName_Ignores_NonPattern_Names_And_Null()
    {
        Assert.Equal("A안", AnalysisNames.NextPlanName(new[] { "1안", "현행안" }));
        Assert.Equal("A안", AnalysisNames.NextPlanName(null!));
    }

    [Fact] public void AnalysisInfo_Tags_Are_Never_Null_After_DataContract_Json_Without_The_Keys()
    {
        var ser = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(AnalysisInfo));
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"Id\":\"a\",\"Name\":\"n\"}"));
        var a = (AnalysisInfo)ser.ReadObject(ms)!;
        Assert.Equal("", a.Corp); Assert.Equal("", a.ProductLine); Assert.Equal("", a.Line);
    }

#if NET
    // the way AnalysisStore reads analyses.json
    [Fact] public void AnalysisInfo_Tags_Are_Never_Null_After_SystemTextJson_Without_The_Keys()
    {
        var a = System.Text.Json.JsonSerializer.Deserialize<AnalysisInfo>("{\"Id\":\"a\",\"Name\":\"n\"}")!;
        Assert.Equal("", a.Corp); Assert.Equal("", a.ProductLine); Assert.Equal("", a.Line);
        Assert.Equal("", System.Text.Json.JsonSerializer.Deserialize<AnalysisInfo>("{\"Corp\":null}")!.Corp);
    }
#endif
}
