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
        var one = await env.Http.GetStringAsync($"/analyses/{aid}"); // a finished version: 결론 is the first tab shown
        Assert.Contains("?tab=conclusion\" class=\"on\"", one);
        Assert.Contains($"<option value=\"{a.Id}\" selected>A안 v1 · 선정</option>", one); Assert.Single(Regex.Matches(one, ">권장 대수<"));
        Assert.Equal(2, Regex.Matches(one, "class=\"pr( on)?\"").Count); // 안별 결론: one row per plan, the shown one outlined
        Assert.Contains($"class=\"pr on\" href=\"?tab=conclusion&amp;job={a.Id}\"", one);
        Assert.Contains($"href=\"/jobs/{a.Id}/files/report.xlsx\" download", one); Assert.Contains($"class=\"btn pri\" href=\"/analyses/{aid}/cuts\"", one); // next step: the film
        var other = await env.Http.GetStringAsync($"/analyses/{aid}?tab=conclusion&job={b.Id}");
        Assert.Contains($"<option value=\"{b.Id}\" selected>", other); Assert.Contains($"class=\"pr on\" href=\"?tab=conclusion&amp;job={b.Id}\"", other);
        Assert.Contains("A안 대비 동일", other); // both versions recommend the same count
        var plans = await env.Http.GetStringAsync($"/analyses/{aid}?tab=conclusion&view=plans");
        Assert.Equal(2, Regex.Matches(plans, "권장 총 대수").Count); Assert.Equal(2, Regex.Matches(plans, "<svg").Count);
    }

    static GroupView G(string n, double u, bool ok = true) => new(n, "T", 1, 6, 2, u, 1, ok, ok ? null : "가동률 초과");

    [Fact] public void Group_Class_Is_Over_Under_Or_Ok()
    {
        Assert.Equal("over", AnalysisPages.GroupClass(G("a", 0.9), 0.85));
        Assert.Equal("over", AnalysisPages.GroupClass(G("a", 0.5, ok: false), 0.85)); // judgement failed counts as a problem
        Assert.Equal("ok", AnalysisPages.GroupClass(G("a", 0.85), 0.85)); // at the limit is not over
        Assert.Equal("under", AnalysisPages.GroupClass(G("a", 0.05), 0.85));
        Assert.Equal("ok", AnalysisPages.GroupClass(G("a", 0.10), 0.85)); // 10 % itself is not under
        Assert.Equal("ok", AnalysisPages.GroupClass(G("a", double.NaN), 0.85));
    }

    [Fact] public void Problems_Come_First()
    {
        var gs = new[] { G("G1", 0.5), G("G2", 0.07), G("G3", 0.88), G("G4", 0.6), G("G5", 0.09), G("G6", 0.86), G("G7", 0.3, ok: false) };
        // over (busiest first, failed with it), under (idlest first), then the rest in input order
        Assert.Equal(new[] { "G3", "G6", "G7", "G2", "G5", "G1", "G4" }, AnalysisPages.ProblemsFirst(gs, 0.85).Select(g => g.Group));
    }

    [Fact] public void Zoom_Box_Holds_The_Hot_Blocks_Inside_The_Layout()
    {
        var all = (0.0, 1000.0, 0.0, 500.0);
        Assert.Equal(all, AnalysisPages.ZoomBox(all, Array.Empty<(double, double, double, double)>()));
        // a 20×10 block near the left edge: 30 % of the layout each way, pushed back inside
        Assert.Equal((0.0, 300.0, 30.0, 180.0), AnalysisPages.ZoomBox(all, new[] { (100.0, 120.0, 100.0, 110.0) }));
        // blocks spread over the whole layout: no zoom
        Assert.Equal(all, AnalysisPages.ZoomBox(all, new[] { (0.0, 10.0, 0.0, 10.0), (990.0, 1000.0, 490.0, 500.0) }));
    }

    [Fact] public async Task Conclusion_Page_Shows_Problem_Groups_First_And_A_Zoomed_Map_Sized_By_Its_Box()
    {
        using var env = new JobEnv(); var j = await env.WaitFinished(await env.SendFiled("라인 증설", "A안"));
        File.WriteAllLines(Path.Combine(env.Store.JobDir(j.Id), "blocks.tsv"), new[] { "name\tutil\twait\tpasses", "B_x1\t0.5\t12\t10" });
        var v = JobView.For(env.Store, j); var lim = v.UtilMax;
        Set(v, "Groups", new List<GroupView> { G("G1", 0.5), G("G2", 0.05), G("G3", lim + 0.1) });
        var aid = env.Factory.Services.GetRequiredService<AnalysisStore>().All().Single().Id;
        var html = await env.Http.GetStringAsync($"/analyses/{aid}");
        int over = html.IndexOf("class=\"gr g-over\"", StringComparison.Ordinal), under = html.IndexOf("class=\"gr g-under\"", StringComparison.Ordinal), ok = html.IndexOf("class=\"gr g-ok\"", StringComparison.Ordinal);
        Assert.True(over > 0 && over < under && under < ok);
        Assert.Contains("▼ 5%</span>", html); Assert.Contains($"▲ {Math.Round((lim + 0.1) * 100)}%</span>", html);
        Assert.Contains("초과 1</span>", html); Assert.Contains("적정 1</span>", html); Assert.Contains("10% 미만 1</span>", html);
        // the map fills its box: no pixel size from the drawing; zoomed to B_x1 (layout 11 m wide) with the minimap and the 전체 toggle
        double VbWidth(string page) => double.Parse(Regex.Match(page, "<svg class=\"bm\" viewBox=\"[^ ]+ [^ ]+ ([^ ]+) ").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.DoesNotContain("width=", Regex.Match(html, "<svg class=\"bm\"[^>]*>").Value);
        Assert.True(VbWidth(html) < 8); Assert.Contains("<svg class=\"mm\"", html); Assert.Contains("&amp;map=all\">전체</a>", html);
        Assert.Contains("B_x1 · 12초", html); Assert.Contains("<li><i>1</i><span title=\"B_x1\">B_x1</span><b>12초</b></li>", html);
        var all = await env.Http.GetStringAsync($"/analyses/{aid}?tab=conclusion&map=all");
        Assert.True(VbWidth(all) > 11); Assert.DoesNotContain("<svg class=\"mm\"", all);
    }
}
