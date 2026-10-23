using System.Text.Json;
using ASH.Server;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ASH.Server.Tests;

public class AnalysisTabsTests
{
    [Fact] public async Task Status_Tab_Is_Default_And_Shows_Partial_While_Running()
    {
        using var env = new JobEnv(); env.Runner.Gate.Reset();
        var id = await env.SendFiled("라인 증설", "A안");
        await JobEnv.Until(() => env.Store.Get(id) is { State: JobState.Running, Phase: "탐색 시작" } x ? x : null); // the fake's first progress callback has run; the Gate holds it there
        env.Store.Update(id, j => { j.Partial = new() { ["G1"] = 2 }; j.Phase = "확정 G1"; });
        var aid = env.Factory.Services.GetRequiredService<AnalysisStore>().All().Single().Id;
        var html = await env.Http.GetStringAsync("/analyses/" + aid);
        foreach (var s in new[] { "class=\"tabs\"", "?tab=status\" class=\"on\"", "?tab=conclusion", "?tab=compare", "?tab=films", "G1 2", "data-field=\"partial.text\"", "fetch('/api/analyses/" + aid + "/status", "관리자: 분석 삭제" }) Assert.Contains(s, html);
        var api = JsonDocument.Parse(await env.Http.GetStringAsync($"/api/analyses/{aid}/status")).RootElement;
        Assert.Equal("G1 2", api.GetProperty("fields").GetProperty("partial.text").GetString());
        env.Runner.Gate.Set(); await env.WaitFinished(id);
        var done = await env.Http.GetStringAsync("/analyses/" + aid + "?tab=status");
        Assert.Contains("v1", done); Assert.Contains("2대", done); Assert.DoesNotContain("fetch(", done);
    }

    [Fact] public async Task Other_Tabs_Keep_Old_Content_And_Unknown_Tab_Is_Status()
    {
        using var env = new JobEnv(worker: false);
        await env.SendFiled("라인 증설", "A안");
        var aid = env.Factory.Services.GetRequiredService<AnalysisStore>().All().Single().Id;
        Assert.Contains("버전을 2개 이상 고르세요", await env.Http.GetStringAsync($"/analyses/{aid}?tab=compare"));
        Assert.Contains("아직 만든 영상이 없습니다", await env.Http.GetStringAsync($"/analyses/{aid}?tab=films"));
        Assert.Contains("?tab=status\" class=\"on\"", await env.Http.GetStringAsync($"/analyses/{aid}?tab=nope"));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await env.Http.GetAsync("/api/analyses/a-nope/status")).StatusCode);
    }
}
