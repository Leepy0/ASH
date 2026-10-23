using System.Text;
using System.Text.RegularExpressions;
using ASH.Server;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ASH.Server.Tests;

public class ConclusionTests
{
    [Fact] public async Task Conclusion_Renders_One_Map_And_Cluster_Bars_For_20_Groups()
    {
        using var env = new JobEnv(); var id = await env.SendFiled("라인 증설", "A안"); await env.WaitFinished(id);
        // 묶음 4개 × 그룹 5개(G1..G20); 입력에 없는 그룹(G2..)은 묶음 제목만 나오고 막대 줄은 G1 하나
        env.Store.Update(id, x => x.Clusters = Enumerable.Range(0, 4).Select(b => Enumerable.Range(1, 5).Select(i => $"G{b * 5 + i}").ToList()).ToList());
        var j = env.Store.Get(id)!; var v = JobView.For(env.Store, j);
        var sb = new StringBuilder(); JobPages.ClusterBars(sb, j, v);
        Assert.Equal(4, Regex.Matches(sb.ToString(), "<h3>묶음:").Count); Assert.Equal(1, Regex.Matches(sb.ToString(), "<span class=\"bar\">").Count);
        var page = new StringBuilder(); JobPages.Conclusion(page, j, v, small: false);
        Assert.Equal(1, Regex.Matches(page.ToString(), "<svg").Count);
        foreach (var s in new[] { "권장 총 대수", "최대 가동률", "최대 병목", "상세 보기", "?tab=detail", "/files/report.xlsx", "class=\"grid2\"" }) Assert.Contains(s, page.ToString());
    }

    [Fact] public async Task Conclusion_Without_Clusters_Is_Flat()
    {
        using var env = new JobEnv(); var id = await env.SendFiled("라인 증설", "A안"); await env.WaitFinished(id);
        env.Store.Update(id, x => x.Clusters = null);
        var j = env.Store.Get(id)!; var sb = new StringBuilder(); JobPages.ClusterBars(sb, j, JobView.For(env.Store, j));
        Assert.DoesNotContain("<h3>묶음:", sb.ToString()); Assert.Contains("<b>권장 2대</b>", sb.ToString());
    }

    [Fact] public async Task Conclusion_Tab_Defaults_To_Selected_Version_And_Lists_Per_Plan()
    {
        using var env = new JobEnv(); var a = await env.WaitFinished(await env.SendFiled("라인 증설", "A안")); var b = await env.WaitFinished(await env.SendFiled("라인 증설", "B안"));
        var st = env.Factory.Services.GetRequiredService<AnalysisStore>(); var aid = st.All().Single().Id; st.SetSelected(env.Store, a.Id, true);
        var one = await env.Http.GetStringAsync($"/analyses/{aid}?tab=conclusion");
        Assert.Contains($"job={a.Id}\" class=\"chip sel\"", one); Assert.Equal(1, Regex.Matches(one, "권장 총 대수").Count);
        var plans = await env.Http.GetStringAsync($"/analyses/{aid}?tab=conclusion&view=plans");
        Assert.Equal(2, Regex.Matches(plans, "권장 총 대수").Count); Assert.Equal(2, Regex.Matches(plans, "<svg").Count);
    }
}
