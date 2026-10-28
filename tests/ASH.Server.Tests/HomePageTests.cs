using System.Text.Json;
using ASH.Server;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ASH.Server.Tests;

public class HomePageTests
{
    const string PollMarker = "setInterval(tick,10000)"; // the 10 s page poll; the 30 s notifier on every page is not it
    [Fact] public async Task Poll_Script_Only_When_Active()
    {
        using var env = new JobEnv(); env.Runner.Gate.Reset();
        var id = await env.SendFiled("라인 증설", "A안");
        await JobEnv.Until(() => env.Store.Get(id) is { State: JobState.Running } x ? x : null);
        Assert.Contains("fetch('/api/home", await env.Http.GetStringAsync("/"));
        Assert.Contains("fetch('/api/jobs/" + id, await env.Http.GetStringAsync("/jobs/" + id));
        var api = JsonDocument.Parse(await env.Http.GetStringAsync("/api/home?me=1")).RootElement;
        Assert.True(api.GetProperty("active").GetBoolean()); Assert.Equal("running", api.GetProperty("fields").GetProperty("running.state").GetString());
        env.Runner.Gate.Set(); await env.WaitFinished(id);
        Assert.DoesNotContain(PollMarker, await env.Http.GetStringAsync("/")); Assert.DoesNotContain(PollMarker, await env.Http.GetStringAsync("/jobs/" + id));
    }

    [Fact] public async Task Home_Shows_A_Running_Film_When_No_Job_Runs()
    {
        using var env = new JobEnv();
        var id = await env.SendFiled("라인 증설", "A안"); await env.WaitFinished(id);
        var fs = env.Factory.Services.GetRequiredService<FilmStore>();
        fs.Add(new FilmInfo { Request = new("20261023-000000-aaaa", "x", ["c1"], [id], 640, 480, 10, "joined", false, null, 0, null), AnalysisName = "라인 증설",
            State = JobState.Running, StartedUtc = DateTime.UtcNow.AddMinutes(-2), CreatedUtc = DateTime.UtcNow, Done = 1, Items = { new FilmItem(), new FilmItem() } });
        var html = await env.Http.GetStringAsync("/");
        Assert.Contains("라인 증설</a><div class=\"t4\">영상</div>", html); Assert.Contains("href=\"/films/20261023-000000-aaaa\"", html); Assert.Contains("컷 1 / 2", html); Assert.DoesNotContain("실행 중인 작업 없음", html);
        Assert.Contains("fetch('/api/home", html);
        var api = JsonDocument.Parse(await env.Http.GetStringAsync("/api/home?me=0")).RootElement;
        Assert.Equal("running", api.GetProperty("watch").GetProperty("20261023-000000-aaaa").GetString()); Assert.Equal("50%", api.GetProperty("fields").GetProperty("running.progress").GetString());
        fs.Update("20261023-000000-aaaa", x => { x.State = JobState.Done; x.FinishedUtc = DateTime.UtcNow; });
        var after = JsonDocument.Parse(await env.Http.GetStringAsync("/api/home?me=0")).RootElement; // the key is gone: the script sees running -> missing and reloads
        Assert.False(after.GetProperty("watch").TryGetProperty("20261023-000000-aaaa", out _)); Assert.False(after.GetProperty("fields").TryGetProperty("running.state", out _));
        var film = await env.Http.GetStringAsync("/films/20261023-000000-aaaa");
        Assert.Contains("data-bar=\"film.bar\"", film);
        Assert.Equal("50", JsonDocument.Parse(await env.Http.GetStringAsync("/api/films/20261023-000000-aaaa")).RootElement.GetProperty("fields").GetProperty("film.bar").GetString());
    }

    [Fact] public async Task PartialText_Survives_A_Corrupt_Workbook()
    {
        using var env = new JobEnv();
        var id = await env.SendFiled("라인 증설", "A안"); await env.WaitFinished(id);
        foreach (var f in Directory.GetFiles(env.Store.ProjectDir(id), "*", SearchOption.AllDirectories).Where(f => f.EndsWith(".xlsx") || f.EndsWith(".xlsm") || f.Contains("cells"))) File.WriteAllText(f, "not a workbook");
        var j = env.Store.Get(id)!; j.Partial = new() { ["G1"] = 2 };
        Assert.Equal("G1 2", AnalysisPages.PartialText(env.Store, j));
    }

    [Fact] public async Task Home_Shows_Running_Card_And_My_Analyses()
    {
        using var env = new JobEnv(); env.Runner.Gate.Reset();
        var id = await env.SendFiled("라인 증설", "A안");
        await JobEnv.Until(() => env.Store.Get(id) is { State: JobState.Running } x ? x : null);
        var html = await env.Http.GetStringAsync("/");
        foreach (var s in new[] { "라인 증설</a><div class=\"t4\">A안 v1</div>", "data-field=\"running.phase\"", "data-bar=\"running.bar\"", "data-field=\"running.left\"", ">진행<", ">내 것<" }) Assert.Contains(s, html);
        Assert.DoesNotContain("http-equiv", html);
        env.Runner.Gate.Set(); await env.WaitFinished(id);
        var done = await env.Http.GetStringAsync("/");
        Assert.Contains("실행 중인 작업 없음", done); Assert.Contains("A안 v1 · ", done); Assert.Contains("2<small>대</small>", done); Assert.Contains("최대 가동률", done); Assert.Contains("/cuts?job=" + id, done);
    }

    [Fact] public async Task Home_Watch_Carries_The_Queued_Job()
    {
        using var env = new JobEnv(); env.Runner.Gate.Reset();
        var a = await env.SendFiled("라인 증설", "A안");
        await JobEnv.Until(() => env.Store.Get(a) is { State: JobState.Running } x ? x : null);
        var b = await env.SendFiled("라인 증설", "B안");
        var api = JsonDocument.Parse(await env.Http.GetStringAsync("/api/home?me=1")).RootElement;
        Assert.Equal("queued", api.GetProperty("watch").GetProperty(b).GetString());
        Assert.Equal("running", api.GetProperty("watch").GetProperty(a).GetString());
        var seeded = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { [a] = "running", [b] = "queued" });
        Assert.Contains("var prev=" + seeded.TrimEnd('}'), await env.Http.GetStringAsync("/"));
        env.Runner.Gate.Set(); await env.WaitFinished(a); await env.WaitFinished(b);
    }

    [Fact] public async Task Home_Tag_Filter_Applies_To_Poll_Api_And_Chips()
    {
        using var env = new JobEnv(); env.Runner.Gate.Reset();
        var a = await env.SendFiled("라인 증설", "A안");
        await JobEnv.Until(() => env.Store.Get(a) is { State: JobState.Running } x ? x : null);
        var b = await env.SendFiled("창고 이전", "A안");
        var an = env.Factory.Services.GetRequiredService<AnalysisStore>();
        foreach (var x in an.All()) an.SetTags(x.Id, x.Name == "라인 증설" ? "P법인" : "Q법인", "", "");
        var api = JsonDocument.Parse(await env.Http.GetStringAsync("/api/home?me=1&corp=P%EB%B2%95%EC%9D%B8")).RootElement.GetProperty("fields"); // the running card and queue are global; the analysis chips are what the filter drops
        Assert.True(api.TryGetProperty($"chip.{a}.text", out _)); Assert.False(api.TryGetProperty($"chip.{b}.text", out _));
        var html = await env.Http.GetStringAsync("/?corp=P%EB%B2%95%EC%9D%B8");
        Assert.Contains("fetch('/api/home?me=1&amp;corp=P%EB%B2%95%EC%9D%B8", html.Replace("&corp", "&amp;corp"));
        Assert.Contains("href=\"/?me=0&amp;corp=P%EB%B2%95%EC%9D%B8\"", html);
        env.Runner.Gate.Set(); await env.WaitFinished(a); await env.WaitFinished(b);
    }

    [Fact] public async Task Home_Falls_Back_To_All_When_Nothing_Is_Mine()
    {
        using var env = new JobEnv(); await env.WaitFinished(await env.SendFiled("남의 분석", "A안", submitter: "other"));
        env.Store.Update(env.Store.All()[0].Id, j => j.Ip = "10.9.9.9"); // 테스트 클라이언트 IP가 아님
        var html = await env.Http.GetStringAsync("/");
        Assert.Contains("남의 분석", html); Assert.Contains("class=\"on\" aria-current=\"true\">전체", html);
        var forced = await env.Http.GetStringAsync("/?me=1");
        Assert.Contains("남의 분석", forced); Assert.Contains("class=\"on\" aria-current=\"true\">전체", forced);
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

    [Fact] public async Task Progress_Tab_Lists_All_Waiting_Work_By_Kind()
    {
        using var env = new JobEnv(worker: false);
        Assert.Contains("data-panel=\"prog\" role=\"tabpanel\" hidden", await env.Http.GetStringAsync("/")); // nothing waits: 결과 first
        var a = await env.SendFiled("라인 증설", "A안"); var b = await env.SendFiled("창고 이전", "B안");
        File.WriteAllText(Path.Combine(env.Store.JobDir(a), FilmWorker.ScenesRequest), "{\"delta\":1}");
        File.WriteAllText(Path.Combine(env.Store.JobDir(b), LimitWorker.RequestFile), LimitWorker.Queued);
        env.Factory.Services.GetRequiredService<FilmStore>().Add(new FilmInfo { Request = new("20261023-000000-ffff", "x", ["c1"], [a], 640, 480, 10, "joined", false, null, 0, null),
            AnalysisName = "라인 증설", State = JobState.Queued, CreatedUtc = DateTime.UtcNow, Items = { new FilmItem() } });
        var html = await env.Http.GetStringAsync("/");
        Assert.Contains("data-panel=\"prog\" role=\"tabpanel\">", html); Assert.Contains("data-panel=\"board\" role=\"tabpanel\" hidden", html);
        Assert.Contains("data-tab=\"prog\" role=\"tab\" class=\"on\" aria-selected=\"true\">진행<em>5</em>", html);
        foreach (var s in new[] { $"class=\"row wk c-blue\" href=\"/jobs/{a}\"", $"class=\"row wk c-blue\" href=\"/jobs/{b}\"", "class=\"row wk c-orange\" href=\"/films/20261023-000000-ffff\"",
                     $"class=\"row wk c-indigo\" href=\"/jobs/{a}\"", $"class=\"row wk c-green\" href=\"/jobs/{b}\"", "대기 1번째", "대기 2번째", "<span class=\"tag c-gray\">미리보기</span>", "작업 실행 꺼짐" })
            Assert.Contains(s, html);
        Assert.DoesNotContain("결품", html);
        var board = await env.Http.GetStringAsync("/?tab=board");
        Assert.Contains("data-panel=\"prog\" role=\"tabpanel\" hidden", board); Assert.Contains("data-panel=\"board\" role=\"tabpanel\">", board);
        var api = JsonDocument.Parse(await env.Http.GetStringAsync("/api/home?me=0")).RootElement;
        Assert.Equal("queued", api.GetProperty("watch").GetProperty("장면:" + a).GetString()); // a poll reloads when it starts
    }
}
