using ASH.Core.Runner;
using ASH.Server;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;

namespace ASH.Server.Tests;

/// <summary>Where the confirm runs deadlocked: the box on the job page and the conclusion tab, the place in the warning text, the report sheet.
/// (Pages HTML-encode the middle dot as &amp;#183;.)</summary>
public class DeadlockSummaryPageTests
{
    // dlock row: vehicle, group, time, episode, x, y, the block (or CP) waited for, the blocks held
    static string Dl(string v, string g, double t, int ep, string waits, string hold) => $"dlock\t{v}\t{g}\t{t}\t{ep}\t1\t2\t{waits}\t{hold}\n";

    static string Ring(double t, int ep) => Dl("V1", "G1", t, ep, "B007", "B005") + Dl("V2", "G2", t, ep, "B005", "B007");

    /// <summary>The fake job with three confirm runs: n2_s1 (3 deadlocks: two at B005·B007 and one at CP L001), n2_s2 (2: one ring), n2_s3 (4, result.tsv gone).</summary>
    static async Task<(JobEnv Env, string Id, string Aid)> Deadlocked()
    {
        var env = new JobEnv();
        var id = await env.SendFiled("라인 증설", "A안"); await env.WaitFinished(id);
        var aid = env.Factory.Services.GetRequiredService<AnalysisStore>().All().Single().Id;
        var runs = Path.Combine(env.Store.ProjectDir(id), "runs");
        var sum = Path.Combine(runs, "summary.tsv"); File.Delete(sum);
        SummaryTsv.Append(sum, new SummaryRow("n2_s1", "G1", 2, 1, false, "교착 3건", 0.6, 2, 10, 10, 0, 3, 5, "G1=2"));
        SummaryTsv.Append(sum, new SummaryRow("n2_s2", "G1", 2, 2, false, "교착 2건", 0.6, 2, 10, 10, 0, 2, 5, "G1=2"));
        SummaryTsv.Append(sum, new SummaryRow("n2_s3", "G1", 2, 3, false, "교착 4건", 0.6, 2, 10, 10, 0, 4, 5, "G1=2"));
        env.Store.Update(id, x => { x.ConfirmRunIds = new() { "n2_s1", "n2_s2", "n2_s3" }; x.Warnings = new() { "G1: 확인 실패 (2대): 교착 9건", "다른 경고" }; });
        var fake = File.ReadAllText(Path.Combine(runs, "n2_s1", "result.tsv"));
        File.WriteAllText(Path.Combine(runs, "n2_s1", "result.tsv"), fake + Ring(100, 1) + Ring(900, 2) + Dl("V3", "G1", 500, 3, "agv:L001", ""));
        Directory.CreateDirectory(Path.Combine(runs, "n2_s2"));
        File.WriteAllText(Path.Combine(runs, "n2_s2", "result.tsv"), fake + Ring(50, 1)); // n2_s3 has no folder at all
        return (env, id, aid);
    }

    [Fact]
    public async Task Job_Page_Shows_The_Places_With_Links_To_The_Cause_Tab_And_The_Runs()
    {
        var (env, id, aid) = await Deadlocked(); using var _ = env;
        var page = await env.Http.GetStringAsync("/jobs/" + id);
        Assert.Contains("<h2>교착</h2>", page); Assert.Contains("확인 실행 3개 · 합계 9회", page);
        // places: B005·B007 first (3 episodes in 2 runs, first at 50 s = 0:00), then the CP at 500 s = 0:08
        var at = page.IndexOf("<td><b>B005&#183;B007</b></td><td class=\"num\">3</td><td class=\"num\">2</td><td class=\"num\">0:00</td><td>G1&#183;G2</td><td>V1&#183;V2</td>", StringComparison.Ordinal);
        Assert.True(at > 0);
        Assert.True(page.IndexOf("<td><b>L001</b></td><td class=\"num\">1</td><td class=\"num\">1</td><td class=\"num\">0:08</td>", at, StringComparison.Ordinal) > at);
        Assert.Contains($"<a href=\"/analyses/{aid}?tab=cause&amp;job={id}&amp;run=n2_s1\" title=\"원인 탭에서 이 실행의 지도 보기\">실행 1</a>", page);
        // runs: most deadlocks first with the scenario per group; the run whose result is gone is listed, without a place
        var r3 = page.IndexOf(">실행 3</a></td><td class=\"num\">4</td><td>G1=2</td>", StringComparison.Ordinal);
        Assert.True(r3 > 0);
        Assert.True(page.IndexOf(">실행 1</a></td><td class=\"num\">3</td><td>G1=2</td>", StringComparison.Ordinal) > r3);
    }

    [Fact]
    public async Task Warnings_And_Group_Reasons_Carry_The_Top_Place_While_The_Stored_Text_Stays()
    {
        var (env, id, _) = await Deadlocked(); using var _2 = env;
        var page = await env.Http.GetStringAsync("/jobs/" + id);
        Assert.Contains("<li>G1: 확인 실패 (2대): 교착 9건 &#183; B005&#183;B007 3건(2개 실행) 외</li>", page);
        Assert.Contains("<li>다른 경고</li>", page);
        Assert.Contains("실패: 교착 3건 &#183; B005&#183;B007 2건 외</td>", page); // the group's first failed run n2_s1: its own biggest place
        var v = JobView.For(env.Store, env.Store.Get(id)!);
        Assert.Equal("교착 2건", v.Annotate("교착 2건", "nope")); // a run without places: unchanged
        Assert.Equal("재고 부족", v.Annotate("재고 부족"));
        Assert.Contains(v.Rows, r => r.Reason == "교착 3건"); // summary.tsv keeps the plain reason
    }

    [Fact]
    public async Task Conclusion_Tab_Has_The_Box_Too_And_Report_Gets_A_Sheet()
    {
        var (env, id, aid) = await Deadlocked(); using var _ = env;
        var tab = await env.Http.GetStringAsync($"/analyses/{aid}?tab=conclusion&job={id}");
        Assert.Contains("<h2>교착</h2>", tab); Assert.Contains("<b>B005&#183;B007</b>", tab);

        var path = Path.Combine(Path.GetTempPath(), "ash-dl-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            var j = env.Store.Get(id)!;
            JobReport.Write(j, JobView.For(env.Store, j), path);
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet("교착");
            var row = ws.Search("B005·B007").First().Address.RowNumber;
            Assert.Equal((3, 2, 50), (ws.Cell(row, 2).GetValue<int>(), ws.Cell(row, 3).GetValue<int>(), ws.Cell(row, 4).GetValue<int>()));
            Assert.Equal("G1·G2", ws.Cell(row, 6).GetString()); Assert.Contains("n2_s1", ws.Cell(row, 8).GetString());
            Assert.Contains(wb.Worksheet("요약").CellsUsed(), c => c.GetString().Contains("교착 9건 · B005·B007 3건(2개 실행) 외"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task No_Deadlock_No_Section_And_No_Sheet()
    {
        using var env = new JobEnv();
        var id = await env.SendFiled("라인 증설", "A안"); var j0 = await env.WaitFinished(id);
        var page = await env.Http.GetStringAsync("/jobs/" + id);
        Assert.DoesNotContain("<h2>교착</h2>", page); Assert.Null(JobView.For(env.Store, j0).Deadlock);
        var path0 = Path.Combine(Path.GetTempPath(), "ash-dl0-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try { JobReport.Write(j0, JobView.For(env.Store, j0), path0); using var wb0 = new XLWorkbook(path0); Assert.False(wb0.Worksheets.Contains("교착")); }
        finally { File.Delete(path0); }
    }

    [Fact]
    public async Task A_Run_Without_Result_Gives_A_Note_Not_An_Error()
    {
        using var env = new JobEnv();
        var id = await env.SendFiled("라인 증설", "A안"); var j0 = await env.WaitFinished(id);
        // deadlocks counted, nowhere to read the places from (the view is not built before this)
        var sum = Path.Combine(env.Store.ProjectDir(id), "runs", "summary.tsv"); File.Delete(sum);
        SummaryTsv.Append(sum, new SummaryRow("n2_s1", "G1", 2, 1, false, "교착 3건", 0.6, 2, 10, 10, 0, 3, 5, "G1=2"));
        File.Delete(Path.Combine(env.Store.ProjectDir(id), "runs", "n2_s1", "result.tsv"));
        var aid = env.Factory.Services.GetRequiredService<AnalysisStore>().All().Single().Id;
        var v = JobView.For(env.Store, j0);
        Assert.NotNull(v.Deadlock); Assert.Empty(v.Deadlock!.Spots); Assert.True(v.Deadlock.MissingPlaces);
        var html = await env.Http.GetStringAsync($"/analyses/{aid}?tab=conclusion&job={id}");
        Assert.Contains("위치 기록 없음", html); Assert.Contains(">실행 1</a></td><td class=\"num\">3</td>", html);
        Assert.Equal("교착 3건", v.Annotate("교착 3건")); // nothing to add
    }
}
