using System.Text;
using System.Text.RegularExpressions;
using ASH.Server;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ASH.Server.Tests;

public class ConclusionTests
{
    // JobView's lists have private setters; the cached view of this env's own job is patched so the page has 20 groups without a 20-group workbook.
    static void Set(JobView v, string prop, object value) => typeof(JobView).GetProperty(prop)!.SetValue(v, value);

    [Fact] public async Task Conclusion_Renders_One_Map_And_Cluster_Bars_For_20_Groups()
    {
        using var env = new JobEnv(); var id = await env.SendFiled("라인 증설", "A안"); await env.WaitFinished(id);
        // 묶음 4개 × 그룹 5개(G1..G20); G1은 두 번째 묶음에도 들어 있어도 한 번만 그린다
        env.Store.Update(id, x => x.Clusters = Enumerable.Range(0, 4).Select(b => Enumerable.Range(1, 5).Select(i => $"G{b * 5 + i}").Concat(b == 1 ? new[] { "G1" } : Array.Empty<string>()).ToList()).ToList());
        var j = env.Store.Get(id)!; var v = JobView.For(env.Store, j);
        Set(v, "Groups", Enumerable.Range(1, 20).Select(i => new GroupView($"G{i}", "T", 1, 4, 2, 0.5, 1, i != 7, i == 7 ? "가동률 초과" : null)).ToList());
        var sb = new StringBuilder(); JobPages.ClusterBars(sb, j, v);
        // the 4 clusters hold 5/6/5/5 groups; no singleton here, so no "단독" heading
        Assert.Equal(4, Regex.Matches(sb.ToString(), "<h3>묶음:").Count); Assert.DoesNotContain("<h3>단독", sb.ToString()); Assert.Equal(20, Regex.Matches(sb.ToString(), "<span class=\"bar\">").Count);
        Assert.Contains("실패: 가동률 초과", sb.ToString()); Assert.Contains("background:var(--bad)", sb.ToString());
        var page = new StringBuilder(); JobPages.Conclusion(page, j, v, small: false);
        Assert.Equal(2, Regex.Matches(page.ToString(), "<svg").Count); // the distribution strip (more than three groups) and the map
        foreach (var s in new[] { "권장 총 대수", "최대 가동률", "최대 병목", "상세 보기", "?tab=detail", "/files/report.xlsx", "class=\"grid2\"" }) Assert.Contains(s, page.ToString());
    }

    [Fact] public async Task Singleton_And_Duplicate_Clusters_Get_No_Heading()
    {
        using var env = new JobEnv(); var id = await env.SendFiled("라인 증설", "A안"); await env.WaitFinished(id);
        env.Store.Update(id, x => x.Clusters = new() { new() { "G1", "G2" }, new() { "G1", "G2" }, new() { "G3" }, new() { "G4" } });
        var j = env.Store.Get(id)!; var v = JobView.For(env.Store, j);
        Set(v, "Groups", Enumerable.Range(1, 6).Select(i => new GroupView($"G{i}", "T", 1, 4, 2, 0.5, 1, true, null)).ToList());
        var sb = new StringBuilder(); JobPages.ClusterBars(sb, j, v); var s = sb.ToString();
        Assert.Single(Regex.Matches(s, "<h3>묶음:")); Assert.Single(Regex.Matches(s, "<h3>단독</h3>")); Assert.Equal(6, Regex.Matches(s, "<span class=\"bar\">").Count);
        Assert.Equal(2, Regex.Matches(s, "<h3>").Count);
    }

    [Fact] public async Task Bottleneck_Card_Follows_The_Metric()
    {
        using var env = new JobEnv(); var id = await env.SendFiled("라인 증설", "A안"); await env.WaitFinished(id);
        var j = env.Store.Get(id)!; var v = JobView.For(env.Store, j);
        Set(v, "Blocks", new List<BlockView> { new("B_wait", 0.1, 9, 5), new("B_occ", 0.9, 1, 5) });
        var wait = new StringBuilder(); JobPages.Conclusion(wait, j, v, false);
        var occ = new StringBuilder(); JobPages.Conclusion(occ, j, v, false, "occ");
        Assert.Contains("<div class=\"big\">B_wait</div><div class=\"sub\">평균 대기 9.0초", wait.ToString());
        Assert.Contains("<div class=\"big\">B_occ</div><div class=\"sub\">점유율 90.0%", occ.ToString());
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
