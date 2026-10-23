using ASH.Server;
using Xunit;

namespace ASH.Server.Tests;

public class HomePageTests
{
    [Fact] public async Task Home_Shows_Running_Card_And_My_Analyses()
    {
        using var env = new JobEnv(); env.Runner.Gate.Reset();
        var id = await env.SendFiled("라인 증설", "A안");
        await JobEnv.Until(() => env.Store.Get(id) is { State: JobState.Running } x ? x : null);
        var html = await env.Http.GetStringAsync("/");
        foreach (var s in new[] { "라인 증설 › A안 v1", "data-field=\"running.phase\"", "data-bar=\"running.bar\"", "<h2>진행 중</h2>", "내 것만", "href=\"/analyses/unclassified\"" }) Assert.Contains(s, html);
        Assert.DoesNotContain("http-equiv", html);
        env.Runner.Gate.Set(); await env.WaitFinished(id);
        var done = await env.Http.GetStringAsync("/");
        Assert.Contains("실행 중인 작업 없음", done); Assert.Contains("<h2>완료</h2>", done); Assert.Contains("A안 v1 2대", done);
    }

    [Fact] public async Task Home_Falls_Back_To_All_When_Nothing_Is_Mine()
    {
        using var env = new JobEnv(); await env.WaitFinished(await env.SendFiled("남의 분석", "A안", submitter: "other"));
        env.Store.Update(env.Store.All()[0].Id, j => j.Ip = "10.9.9.9"); // 테스트 클라이언트 IP가 아님
        var html = await env.Http.GetStringAsync("/");
        Assert.Contains("남의 분석", html); Assert.Contains("class=\"chip sel\">전체", html);
        var forced = await env.Http.GetStringAsync("/?me=1");
        Assert.Contains("남의 분석", forced); Assert.Contains("class=\"chip sel\">전체", forced);
    }

    [Fact] public async Task Home_Toggle_Click_Handler_Comes_After_The_Links()
    {
        using var env = new JobEnv();
        var html = await env.Http.GetStringAsync("/");
        Assert.True(html.IndexOf("setItem('ashMe'", StringComparison.Ordinal) > html.LastIndexOf("data-me=\"0\"", StringComparison.Ordinal));
    }

    [Fact] public async Task Analyses_List_Searches_Name_And_Submitter()
    {
        using var env = new JobEnv();
        await env.WaitFinished(await env.SendFiled("라인 증설", "A안", submitter: "kim")); await env.WaitFinished(await env.SendFiled("창고 이전", "A안", submitter: "lee"));
        Assert.Contains("라인 증설", await env.Http.GetStringAsync("/analyses?q=kim"));
        Assert.DoesNotContain("창고 이전", await env.Http.GetStringAsync("/analyses?q=kim"));
        Assert.Contains("창고 이전", await env.Http.GetStringAsync("/analyses?q=창고"));
    }
}
