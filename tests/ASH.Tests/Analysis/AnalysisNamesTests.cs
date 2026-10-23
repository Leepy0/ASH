using ASH.Core.Analysis;

namespace ASH.Tests.Analysis;

public class AnalysisNamesTests
{
    [Theory]
    [InlineData(new string[0], "A안")] [InlineData(new[] { "A안" }, "B안")] [InlineData(new[] { "A안", "C안", "기타" }, "B안")]
    public void NextPlanName_Takes_The_First_Free_Letter(string[] existing, string want) => Assert.Equal(want, AnalysisNames.NextPlanName(existing));

    [Fact] public void NextPlanName_After_Z_Is_Generic() => Assert.Equal("새 안", AnalysisNames.NextPlanName(Enumerable.Range(0, 26).Select(i => (char)('A' + i) + "안")));
}
