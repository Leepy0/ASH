using System.Text.RegularExpressions;
using ASH.Core.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ASH.Server.Tests;

public class CompareTests
{
    [Fact] public async Task Compare_Tab_Shows_Deltas_Against_The_Base_And_Scrolls()
    {
        using var env = new JobEnv(); var a = await env.WaitFinished(await env.SendFiled("라인 증설", "A안")); var b = await env.WaitFinished(await env.SendFiled("라인 증설", "B안"));
        var st = env.Factory.Services.GetRequiredService<AnalysisStore>(); var aid = st.All().Single().Id;
        st.SetSelected(env.Store, a.Id, true); st.SetSelected(env.Store, b.Id, true);
        var html = await env.Http.GetStringAsync($"/analyses/{aid}?tab=compare");
        foreach (var s in new[] { "총 대수(대)", "최대 가동률(%)", "결품 합계(초)", "교착(회)", "최대 병목(block·초)", "G1 권장(대)", "G1 가동률(%)", "class=\"scroll\"", "<svg", "기준으로", "병목 지도 나란히" }) Assert.Contains(s, html);
        Assert.Contains("<small>0</small>", html); // same result so delta 0: no class
        Assert.DoesNotContain("class=\"bad\"", html.Substring(html.IndexOf("<small", StringComparison.Ordinal)));
        Assert.DoesNotContain("<small class=", html);
        var maps = await env.Http.GetStringAsync($"/analyses/{aid}?tab=compare&maps=1");
        Assert.Equal(3, Regex.Matches(maps, "<svg").Count); // chart 1 + maps 2
        Assert.Contains("버전을 2개 이상 고르세요", await env.Http.GetStringAsync($"/analyses/{aid}?tab=compare&jobs={a.Id}"));
    }

    [Fact] public async Task Compare_Tab_Has_An_Investment_Row_Only_With_Prices()
    {
        var (env, aid, ids) = await Three(new[] { 10.0, 10.0 });
        using var _ = env;
        var url = $"/analyses/{aid}?tab=compare&jobs={ids[0]},{ids[1]}";
        Assert.DoesNotContain("투자비(원)", await env.Http.GetStringAsync(url));
        var job = env.Store.Get(ids[0])!;
        var type = ASH.Server.JobView.For(env.Store, job).Input!.Groups[0].Type;
        var cat = env.Factory.Services.GetRequiredService<CatalogStore>();
        cat.Save(new CatalogType { Name = type, Label = type, Category = "AGV", Kind = "Standard", Defaults = new CatalogDefaults { SpeedLoadedStraight = 1.5, Capacity = 1 } }, "me"); // prices only for catalog types
        cat.SavePrices($"Type,UnitPrice,Currency,Note\n{type},500,KRW,\n");
        var html = await env.Http.GetStringAsync(url);
        var row = html[html.IndexOf("<th>투자비(원)</th>", StringComparison.Ordinal)..]; row = row[..row.IndexOf("</tr>", StringComparison.Ordinal)];
        Assert.Contains((job.Decided!.Values.Sum() * 500m).ToString("N0", System.Globalization.CultureInfo.InvariantCulture), row);
        Assert.Contains("<small>0</small>", row); // same counts, same cost
    }

    static async Task<(JobEnv env, string aid, string[] ids)> Three(double[] waits, double[]? ks = null)
    {
        var env = new JobEnv(); var ids = new List<string>();
        for (int i = 0; i < waits.Length; i++)
        {
            var j = await env.WaitFinished(await env.SendFiled("라인 증설", "안" + i, k: ks?[i] ?? 1));
            File.WriteAllLines(Path.Combine(env.Store.JobDir(j.Id), "blocks.tsv"), new[] { "name	util	wait	passes", $"B_x1	0.5	{waits[i].ToString(System.Globalization.CultureInfo.InvariantCulture)}	10" });
            ids.Add(j.Id);
        }
        return (env, env.Factory.Services.GetRequiredService<AnalysisStore>().All().Single().Id, ids.ToArray());
    }

    [Fact] public async Task Deltas_Are_Green_When_Better_And_Red_When_Worse_And_Unknown_Ids_Are_Ignored()
    {
        var (env, aid, ids) = await Three(new[] { 10.0, 20.0, 5.0 });
        using var _ = env;
        // base = first: the second has a longer wait (worse, +), the third a shorter one (better, -)
        var html = await env.Http.GetStringAsync($"/analyses/{aid}?tab=compare&jobs={ids[0]},{ids[1]},{ids[2]},j-nope");
        var table = html[html.IndexOf("<table", StringComparison.Ordinal)..]; table = table[..table.IndexOf("</table>", StringComparison.Ordinal)];
        var row = table[table.IndexOf("최대 병목(block·초)", StringComparison.Ordinal)..]; row = row[..row.IndexOf("</tr>", StringComparison.Ordinal)];
        Assert.Contains("<small class=\"bad\">+10</small>", row);
        Assert.Contains("<small class=\"ok\">-5</small>", row);
        Assert.Equal(3, Regex.Matches(table, "<th><a href=\"/jobs/").Count); // three columns, the unknown id is dropped
        Assert.Equal(2, Regex.Matches(table, "기준으로").Count);
        Assert.Contains("class=\"scroll\"", html[..html.IndexOf("<table", StringComparison.Ordinal)].Split("class=\"box\"").Last());
    }

    [Fact] public async Task Side_By_Side_Maps_Share_One_Scale_And_One_Colour_Maximum()
    {
        var (env, aid, ids) = await Three(new[] { 10.0, 20.0 }, new[] { 1.0, 2.0 }); // B's layout is twice as large
        using var _ = env;
        var html = await env.Http.GetStringAsync($"/analyses/{aid}?tab=compare&maps=1&jobs={ids[0]},{ids[1]}");
        var svgs = Regex.Matches(html, "<svg.*?</svg>", RegexOptions.Singleline).Select(m => m.Value).Skip(1).ToList(); // first is the chart
        Assert.Equal(2, svgs.Count);
        double EndX(string svg) { var pts = Regex.Match(svg, "<polyline points=\"([^\"]*)\"[^>]*><title>p1</title>").Groups[1].Value.Trim().Split(' '); return double.Parse(pts[^1].Split(',')[0], System.Globalization.CultureInfo.InvariantCulture); }
        Assert.Equal(2 * (EndX(svgs[0]) - 12), EndX(svgs[1]) - 12, 0.6); // same metres-to-pixels scale: 20 m is twice 10 m
        string Fill(string svg) => Regex.Match(svg, @"fill=""(rgb\([^""]*\))""").Groups[1].Value;
        Assert.NotEqual(Fill(svgs[0]), Fill(svgs[1])); // 10 s and 20 s on the same colour scale
        Assert.Equal("rgb(226,75,74)", Fill(svgs[1])); // the maximum over both versions is red
    }
}
