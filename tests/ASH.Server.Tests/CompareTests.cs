using System.Text.RegularExpressions;
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
        Assert.Contains("<small", html); // same result so delta 0: no class
        Assert.DoesNotContain("class=\"bad\">+", html);
        var maps = await env.Http.GetStringAsync($"/analyses/{aid}?tab=compare&maps=1");
        Assert.Equal(3, Regex.Matches(maps, "<svg").Count); // chart 1 + maps 2
        Assert.Contains("버전을 2개 이상 고르세요", await env.Http.GetStringAsync($"/analyses/{aid}?tab=compare&jobs={a.Id}"));
    }
}
