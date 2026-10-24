using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using ASH.Server;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ASH.Server.Tests;

/// <summary>More than JobPages.ManyGroups groups: summary + notable groups + one compact figure; up to that, the per-group display stays as it was.</summary>
public class ManyGroupsTests
{
    // JobView's lists have private setters; the cached view of a finished job is patched (as in ConclusionTests).
    static void SetGroups(JobView v, IEnumerable<GroupView> gs) => typeof(JobView).GetProperty("Groups")!.SetValue(v, gs.ToList());
    static GroupView G(string name, double util, int count = 2, bool ok = true) => new(name, "T", 1, 6, count, util, 1, ok, ok ? null : "교착 1건");

    static async Task<(JobEnv Env, JobInfo J, JobView V)> One(int groups, Func<int, double>? util = null)
    {
        var env = new JobEnv(); var id = await env.SendFiled("라인 증설", "A안"); await env.WaitFinished(id);
        var j = env.Store.Get(id)!; var v = JobView.For(env.Store, j);
        SetGroups(v, Enumerable.Range(1, groups).Select(i => G($"G{i:00}", util?.Invoke(i) ?? 0.3 + i * 0.02)));
        return (env, j, v);
    }

    [Fact] public async Task Threshold_Is_Three_Groups()
    {
        var (e3, j3, v3) = await One(3); using var _ = e3;
        var three = new StringBuilder(); JobPages.Conclusion(three, j3, v3, false); var s3 = three.ToString();
        Assert.Contains("<span class=\"bar\">", s3); Assert.Contains("<b>권장 2대</b>", s3);
        foreach (var x in new[] { "주목 그룹", "분포 그림", "<details>", "상한 초과", "class=\"strip\"" }) Assert.DoesNotContain(x, s3);
        var (e4, j4, v4) = await One(4); using var __ = e4;
        var four = new StringBuilder(); JobPages.Conclusion(four, j4, v4, false); var s4 = four.ToString();
        foreach (var x in new[] { "<h3>주목 그룹</h3>", "<h3>분포 그림</h3>", "<details><summary>전체 4개 그룹 보기</summary>", "경고(상한 초과) 0개 · 판정 실패 0개 그룹", "class=\"strip\"" }) Assert.Contains(x, s4);
        Assert.Equal(4 + 4, Regex.Matches(s4, "<span class=\"bar\">").Count); // 4 notable + 4 in the closed details
    }

    [Fact] public async Task Notable_Is_Failed_Then_Over_Limit_Then_Busiest_At_Most_Five()
    {
        var (env, j, v) = await One(0); using var _ = env; var lim = v.UtilMax;
        SetGroups(v, new[] { G("G1", 0.5), G("G2", lim + 0.15), G("G3", 0.3, ok: false), G("G4", lim - 0.05), G("G5", 0.2), G("G6", lim + 0.05), G("G7", 0.2, ok: false), G("G8", 0.4) });
        Assert.Equal(new[] { "G3", "G7", "G2", "G6", "G4" }, JobPages.Notable(v).Select(g => g.Group));
        var sb = new StringBuilder(); JobPages.Conclusion(sb, j, v, false); var s = sb.ToString();
        Assert.Contains("경고(상한 초과) 2개 · 판정 실패 2개 그룹", s);
        var notable = s[s.IndexOf("<h3>주목 그룹</h3>", StringComparison.Ordinal)..s.IndexOf("<h3>분포 그림</h3>", StringComparison.Ordinal)];
        Assert.Equal(5, Regex.Matches(notable, "<span class=\"bar\">").Count); Assert.Contains("실패: 교착 1건", notable); Assert.Contains("⚠ 경고: 가동률 초과", notable);
    }

    [Fact] public async Task Strip_Has_One_Dot_Per_Group_And_A_Fixed_Height()
    {
        var (env, _, v) = await One(20, i => i == 7 ? 0.82 : 0.5); using var __ = env;
        SetGroups(v, v.Groups.Select(g => g.Group == "G07" ? g with { Decided = 5 } : g.Group == "G03" ? g with { AllOk = false } : g));
        var s = JobPages.Strip(v);
        Assert.Equal(20, Regex.Matches(s, "<circle").Count);
        Assert.Contains("<title>G07 · 가동률 82% · 5대</title>", s);
        Assert.Contains("fill=\"#a32d2d\"", s); Assert.Contains("stroke-dasharray", s);
        SetGroups(v, v.Groups.Take(4)); var small = JobPages.Strip(v);
        Assert.Equal(Regex.Match(s, "height=\"(\\d+)\"").Value, Regex.Match(small, "height=\"(\\d+)\"").Value);
        Assert.True(Regex.Matches(s, "cy=\"([\\d.]+)\"").Select(m => m.Groups[1].Value).Distinct().Count() > 1); // 19 dots at 50 % are spread vertically
    }

    static async Task<(JobEnv Env, string Aid, JobInfo A, JobInfo B)> Two(Func<int, GroupView> a, Func<int, GroupView> b, int n = 5)
    {
        var env = new JobEnv(); var ja = await env.WaitFinished(await env.SendFiled("라인 증설", "A안")); var jb = await env.WaitFinished(await env.SendFiled("라인 증설", "B안"));
        SetGroups(JobView.For(env.Store, ja), Enumerable.Range(1, n).Select(a));
        SetGroups(JobView.For(env.Store, jb), Enumerable.Range(1, n).Select(b));
        return (env, env.Factory.Services.GetRequiredService<AnalysisStore>().All().Single().Id, ja, jb);
    }

    static string Grid(string html) { var i = html.IndexOf("<table class=\"heat\"", StringComparison.Ordinal); return i < 0 ? "" : html[i..html.IndexOf("</table>", i, StringComparison.Ordinal)]; }

    [Fact] public async Task Compare_With_Many_Groups_Shows_A_Heat_Grid_Of_Changed_Groups()
    {
        // G2: count differs; G3: utilisation 0.06 apart; G4: 0.04 apart (unchanged); G5 failed in B with the same numbers (unchanged)
        var (env, aid, a, b) = await Two(i => G($"G{i}", 0.5), i => i switch { 2 => G("G2", 0.5, 3), 3 => G("G3", 0.56), 4 => G("G4", 0.54), 5 => G("G5", 0.5, ok: false), _ => G($"G{i}", 0.5) });
        using var _ = env;
        var html = await env.Http.GetStringAsync($"/analyses/{aid}?tab=compare&jobs={a.Id},{b.Id}");
        var table = html[html.IndexOf("<table>", StringComparison.Ordinal)..]; table = table[..table.IndexOf("</table>", StringComparison.Ordinal)];
        foreach (var x in new[] { "총 대수(대)", "최대 가동률(%)", "결품 합계(초)", "교착(회)", "최대 병목(block·초)" }) Assert.Contains(x, table);
        Assert.DoesNotContain("권장(대)", table); Assert.DoesNotContain("<svg", html); // no grouped bar chart
        Assert.Equal(2 * 2, Regex.Matches(Grid(html), "class=\"num hc").Count); // G2, G3 × 2 versions
        Assert.Contains("title=\"G2 · B안 v1 · 3대 · 가동률 50%\">3</td>", Grid(html));
        Assert.Contains($"jobs={a.Id},{b.Id}&amp;groups=all\" class=\"chip\">전체 그룹", html);
        var all = await env.Http.GetStringAsync($"/analyses/{aid}?tab=compare&jobs={a.Id},{b.Id}&groups=all");
        Assert.Equal(5 * 2, Regex.Matches(Grid(all), "class=\"num hc").Count);
        Assert.Contains("outline:2px solid #a32d2d", Grid(all));
        Assert.Contains($"jobs={a.Id},{b.Id}\" class=\"chip\">버전 간 달라진 그룹만", all);
        Assert.Contains("&amp;maps=1&amp;groups=all", all); // the maps link keeps the choice
    }

    [Fact] public async Task Compare_Says_When_No_Group_Changed()
    {
        var (env, aid, a, b) = await Two(i => G($"G{i}", 0.5), i => G($"G{i}", 0.52));
        using var _ = env;
        var html = await env.Http.GetStringAsync($"/analyses/{aid}?tab=compare&jobs={a.Id},{b.Id}");
        Assert.Contains($"버전 간 달라진 그룹이 없습니다. <a href=\"?tab=compare&amp;jobs={a.Id},{b.Id}&amp;groups=all\">전체 그룹 보기</a>", html);
        Assert.Equal("", Grid(html));
    }

    [Fact] public async Task Compare_With_Three_Groups_Is_Unchanged()
    {
        var (env, aid, a, b) = await Two(i => G($"G{i}", 0.5), i => G($"G{i}", 0.6), 3);
        using var _ = env;
        var html = await env.Http.GetStringAsync($"/analyses/{aid}?tab=compare&jobs={a.Id},{b.Id}");
        foreach (var x in new[] { "G1 권장(대)", "G3 가동률(%)", "<svg" }) Assert.Contains(x, html);
        Assert.DoesNotContain("class=\"heat\"", html); Assert.DoesNotContain("버전 간 달라진", html);
    }

    static JobInfo Partial(int groups, int confirmed, string? phase)
    {
        var j = new JobInfo { Id = "pt-" + Guid.NewGuid().ToString("N"), Phase = phase, Partial = Enumerable.Range(1, confirmed).ToDictionary(i => $"G{i:00}", _ => 3) };
        var cache = (System.Collections.IDictionary)typeof(AnalysisPages).GetField("GroupCache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        cache[j.Id] = Enumerable.Range(1, groups).Select(i => ($"G{i:00}", 6)).ToList();
        return j;
    }

    [Fact] public void Partial_Text_Is_Short_For_Many_Groups()
    {
        var j = Partial(20, 12, "탐색 G13,G14,G15,G16");
        Assert.Equal("확정 12 / 20 그룹 · 탐색 중: G13~G16", AnalysisPages.PartialText(null!, j));
        Assert.StartsWith("G01 3 · ", AnalysisPages.PartialDetail(null!, j)); Assert.Contains("G13 6 시험 중", AnalysisPages.PartialDetail(null!, j));
        Assert.Equal("확정 12 / 20 그룹 · 탐색 중: G13, G14, G15, G16 외 4개", AnalysisPages.PartialText(null!, Partial(20, 12, "확정 G12")));
        Assert.Equal("확정 2 / 4 그룹 · 탐색 중: G03, G04", AnalysisPages.PartialText(null!, Partial(4, 2, "탐색 2패스")));
        Assert.Equal("확정 4 / 4 그룹", AnalysisPages.PartialText(null!, Partial(4, 4, null)));
        var three = Partial(3, 1, "탐색 G02");
        Assert.Equal("G01 3 · G02 6 시험 중 · G03 탐색 중", AnalysisPages.PartialText(null!, three)); Assert.Null(AnalysisPages.PartialDetail(null!, three));
    }
}
