using ASH.Core.Analysis;

namespace ASH.Tests.Analysis;

public class SafeFileNameTests
{
    [Fact] public void Forbidden_characters_and_spaces_become_underscores()
        => Assert.Equal("A_B안__테스트_", AnalysisNames.SafeFileName("A/B안 <테스트>"));

    [Fact] public void Long_names_are_cut_to_60()
        => Assert.Equal(60, AnalysisNames.SafeFileName(new string('가', 100)).Length);
}
