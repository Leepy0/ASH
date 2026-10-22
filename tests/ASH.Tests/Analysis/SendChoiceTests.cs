using ASH.Core.Analysis;
using ASH.Core.Runner;

namespace ASH.Tests.Analysis;

public class SendChoiceTests
{
    static AnalysisSummary A(string id, string name, params PlanSummary[] plans) => new() { Id = id, Name = name, Plans = new(plans) };
    static PlanSummary P(string id, string name, int next = 1) => new() { Id = id, Name = name, NextVersion = next };

    static readonly AnalysisSummary[] List =
    {
        A("a1", "공장A", P("p1", "기본안"), P("p2", "증설안", 3)),
        A("a2", "공장B", P("p3", "기본안")),
    };

    [Fact] public void Default_Prefers_Last_Choice_When_Still_On_Server()
    {
        var last = new SendChoice { AnalysisId = "a2", PlanId = "p3", AnalysisName = "공장B", PlanName = "기본안" };
        Assert.Equal(last, SendChoice.Default(List, "공장A", "증설안", last));
    }

    [Fact] public void Default_Matches_Folder_And_Drawing_Names()
    {
        var c = SendChoice.Default(List, "공장a", "증설안", null);
        Assert.Equal(new SendChoice { AnalysisId = "a1", PlanId = "p2", AnalysisName = "공장A", PlanName = "증설안" }, c);
    }

    [Fact] public void Default_Proposes_New_Names_When_Nothing_Matches()
    {
        var gone = new SendChoice { AnalysisId = "zz", PlanId = "yy", AnalysisName = "없는분석", PlanName = "없는안" };
        Assert.Equal(new SendChoice { AnalysisName = "새폴더", PlanName = "새도면" }, SendChoice.Default(List, "새폴더", "새도면", gone));
    }

    [Fact] public void Save_Load_Round_Trip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash-send-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(SendChoice.Load(dir));
            var c = new SendChoice { AnalysisId = "a1", PlanId = "", AnalysisName = "공장A", PlanName = "새\t안" };
            c.Save(dir);
            Assert.Equal(c with { PlanName = "새 안" }, SendChoice.Load(dir));
        }
        finally { Directory.Delete(dir, true); }
    }
}
