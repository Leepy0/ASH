using ASH.Server;
using Xunit;

namespace ASH.Server.Tests;

public class FitRunTests
{
    [Fact]
    public async Task Runs_At_The_Recommended_Counts_That_Passed_Are_Marked_With_Their_Model()
    {
        using var env = new JobEnv();
        var j = await env.WaitFinished(await env.SendFiled("라인 증설", "A안"));
        Assert.Equal(2, j.Decided!["G1"]);
        var page = await env.Http.GetStringAsync($"/jobs/{j.Id}?tab=detail");
        // the fake search ran G1 = 3, 2 and 1; 2 is the recommendation and passed, 1 failed (utilisation over is only a warning, 0.95 here fails nothing else)
        Assert.Contains("적정 대수 실행", page);
        Assert.Contains($"href=\"/jobs/{j.Id}/runs/n2_s1/model.zip\" download title=\"n2_s1\">시드 1 모델 받기</a>", page);
        Assert.DoesNotContain("runs/n3_s1/model.zip\" download title=", page);
        Assert.Contains(">적정</span>", page);
    }

    [Fact]
    public async Task With_Path_Totals_The_Busiest_Paths_Are_Coloured_Widened_And_Ranked()
    {
        using var env = new JobEnv();
        var j = await env.WaitFinished(await env.SendFiled("라인 증설", "A안"));
        var v = JobView.For(env.Store, j);
        var names = v.Layout!.Paths.Select(p => p.Name).ToList();
        Assert.True(names.Count >= 2);
        // the busiest gets the full heat colour and the widest line; the rest are scaled to it
        var traffic = names.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i == 0 ? 40.0 : 10.0);
        var sb = new System.Text.StringBuilder();
        AnalysisPages.BottleneckMap(sb, v, b => b.AvgWaitSec, x => x.ToString("0") + "대", true, "#z", "#a", "#i", null, traffic);
        var html = sb.ToString();
        Assert.Contains($"stroke=\"{JobPages.Heat(1)}\" stroke-width=\"10\"", html);
        Assert.Contains($"stroke=\"{JobPages.Heat(0.1)}\" stroke-width=\"3.7\"", html); // the quietest path with traffic: min..max scale, not value / max
        Assert.Contains("통행량", html); Assert.Contains("10대", html); Assert.Contains("통행 없음", html); // legend with the range
        Assert.Equal(0.1, AnalysisPages.TrafficT(10, 10, 40), 6); Assert.Equal(1, AnalysisPages.TrafficT(40, 10, 40), 6); Assert.Equal(0.5, AnalysisPages.TrafficT(7, 7, 7), 6);
        Assert.Contains($"<li><i>1</i><span title=\"{names[0]}\">{names[0]}</span><b>40대</b>", html);
        Assert.Contains("전체 " + (40 + 10 * (names.Count - 1)) + "대", html);
    }

    [Fact]
    public async Task The_Traffic_View_Of_The_Map_Says_When_There_Is_No_Path_Data()
    {
        using var env = new JobEnv();
        await env.WaitFinished(await env.SendFiled("라인 증설", "A안"));
        var aid = env.Store.All().Single().AnalysisId;
        var page = await env.Http.GetStringAsync($"/analyses/{aid}?tab=conclusion&metric=traffic");
        Assert.Contains(">통행량</a>", page);
        Assert.Contains("통행량 자료 없음", page); // the fake runs leave no AutoMod report
    }

    [Fact]
    public void Legend_Helpers_Show_Swatches_And_Scale_Range()
    {
        var l = JobPages.Legend(("#fff", "통과"), ("red", "미통과"));
        Assert.Contains("통과", l); Assert.Contains("background:red", l);
        var h = JobPages.HeatLegend("통행량", "12대", "340대");
        Assert.Contains("12대", h); Assert.Contains("340대", h); Assert.Contains(JobPages.Heat(1), h);
    }
}
