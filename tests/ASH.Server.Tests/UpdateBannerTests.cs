using System.Net;
using ASH.Server;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ASH.Server.Tests;

/// <summary>Server updates are only staged on their own; an admin applies them from the banner (now, or at the worker's next gap).</summary>
public class UpdateBannerTests
{
    const string Token = "test!Admin12";

    static (ServerUpdater U, List<string> Swaps) Staged(JobEnv env, string version = "9.9.9")
    {
        var u = env.Factory.Services.GetRequiredService<ServerUpdater>();
        var swaps = new List<string>();
        u.SwapAsync = (_, v) => { lock (swaps) swaps.Add(v); return Task.CompletedTask; };
        u.Stage("unused-stage", version);
        return (u, swaps);
    }

    static Task<HttpResponseMessage> Act(JobEnv env, string act, bool admin = true)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/admin/update") { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["act"] = act, ["back"] = "/films" }) };
        if (admin) req.Headers.Add(ReleaseStore.HeaderName, Token);
        return env.Http.SendAsync(req);
    }

    static async Task<string> Banner(JobEnv env, bool admin = true)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/admin/update-banner");
        if (admin) req.Headers.Add(ReleaseStore.HeaderName, Token);
        var r = await env.Http.SendAsync(req);
        return r.StatusCode == HttpStatusCode.NoContent ? "" : await r.Content.ReadAsStringAsync();
    }

    static async Task<string> Running(JobEnv env)
    {
        var id = await env.SendFiled("라인 증설", "A안");
        await JobEnv.Until(() => env.Store.Get(id) is { State: JobState.Running } x ? x : null);
        return id;
    }

    [Fact]
    public async Task A_Staged_Update_Is_Never_Applied_Without_An_Admin()
    {
        using var env = new JobEnv(adminToken: Token);
        await env.WaitFinished(await env.SendFiled("라인 증설", "A안")); // the worker loop runs
        var (u, swaps) = Staged(env);
        Assert.True(u.WorkerApplies);
        await env.WaitFinished(await env.SendFiled("라인 증설", "B안")); // a gap after this job: not armed, so nothing
        env.Store.Wake(); await Task.Delay(500); // and an idle worker does not apply it either
        Assert.Empty(swaps); Assert.Equal("9.9.9", u.Waiting);
        Assert.False(await u.ApplyPendingAsync());
    }

    [Fact]
    public async Task Armed_It_Waits_For_The_Running_Job_And_Applies_At_The_Gap()
    {
        using var env = new JobEnv(adminToken: Token); env.Runner.Gate.Reset();
        var id = await Running(env);
        var (u, swaps) = Staged(env);
        var r = await Act(env, "after");
        Assert.Equal("/films", r.RequestMessage!.RequestUri!.AbsolutePath); // back to the page the banner was on
        Assert.True(u.Armed);
        Assert.Contains("진행 탭이 비면 업데이트 9.9.9", await Banner(env));
        await Task.Delay(300);
        Assert.Empty(swaps); // the job is not cut off
        env.Runner.Gate.Set();
        Assert.Equal(JobState.Done, (await env.WaitFinished(id)).State);
        await JobEnv.Until(() => { lock (swaps) return swaps.Count > 0 ? "x" : null; });
        Assert.Equal(new[] { "9.9.9" }, swaps); Assert.Null(u.Waiting); Assert.False(u.Armed);
    }

    [Fact]
    public async Task Cancel_Disarms_And_The_Banner_Offers_Both_Again()
    {
        using var env = new JobEnv(adminToken: Token); env.Runner.Gate.Reset();
        var id = await Running(env);
        var (u, swaps) = Staged(env);
        await Act(env, "after"); await Act(env, "cancel");
        Assert.False(u.Armed);
        env.Runner.Gate.Set(); await env.WaitFinished(id); await Task.Delay(300);
        Assert.Empty(swaps);
        var b = await Banner(env);
        Assert.Contains("업데이트 9.9.9 준비됨", b); Assert.Contains("지금 업데이트", b); Assert.Contains("진행 탭 작업 모두 끝나고 업데이트", b);
    }

    [Fact]
    public async Task Apply_Now_Swaps_At_Once_And_Warns_While_Work_Runs()
    {
        using var env = new JobEnv(adminToken: Token); env.Runner.Gate.Reset();
        var id = await Running(env);
        var (u, swaps) = Staged(env);
        Assert.Contains("실행 중 작업 중단", await Banner(env)); // 지금 cuts the running job off
        var r = await Act(env, "now");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode); Assert.Contains("9.9.9 적용 중", await r.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "9.9.9" }, swaps); Assert.Null(u.Waiting);
        Assert.Equal("", await Banner(env)); // nothing staged any more
        Assert.Equal(HttpStatusCode.Conflict, (await Act(env, "now")).StatusCode);
        env.Runner.Gate.Set(); await env.WaitFinished(id); // (a fake swap: nothing stops it here)
        var next = await env.SendFiled("라인 증설", "B안"); await Task.Delay(500);
        Assert.Equal(JobState.Queued, env.Store.Get(next)!.State); // the server stops in a moment: nothing new starts
    }

    [Fact]
    public async Task A_Failed_Swap_Keeps_The_Update_For_Another_Try()
    {
        using var env = new JobEnv(worker: false, adminToken: Token);
        var (u, _) = Staged(env);
        u.SwapAsync = (_, _) => throw new InvalidOperationException("batch did not start");
        Assert.Equal(HttpStatusCode.InternalServerError, (await Act(env, "after")).StatusCode); // no worker: "after" is now
        Assert.Equal("9.9.9", u.Waiting); Assert.Contains("batch did not start", u.LastResult);
    }

    [Fact]
    public async Task Only_An_Admin_Sees_The_Banner_Or_Can_Apply()
    {
        using var env = new JobEnv(worker: false, adminToken: Token);
        Assert.Equal("", await Banner(env)); // nothing staged
        var (_, swaps) = Staged(env);
        Assert.Equal("", await Banner(env, admin: false));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Act(env, "now", admin: false)).StatusCode);
        Assert.Empty(swaps);
        var page = await env.Http.GetStringAsync("/films");
        Assert.Contains("<div id=\"upd\"></div>", page); Assert.Contains("fetch('/admin/update-banner'", page); // every page asks; the answer depends on the cookie
        Assert.DoesNotContain("준비됨", page);

        var login = new HttpRequestMessage(HttpMethod.Post, "/admin/login") { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = Token }) };
        login.Headers.Add("Origin", "http://localhost");
        Assert.True((await env.Http.SendAsync(login)).IsSuccessStatusCode);
        Assert.Contains("업데이트 9.9.9 준비됨", await Banner(env, admin: false)); // the session cookie is enough
    }

    [Fact]
    public async Task A_Job_Cut_Off_By_A_Stop_Is_Queued_Again_To_Resume()
    {
        using var env = new JobEnv(); env.Runner.Gate.Reset();
        var id = await Running(env);
        env.Factory.Dispose(); // the service stops (update, restart) while the job runs
        var j = new JobStore(Path.Combine(env.Dir, "jobs"), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance).Get(id)!;
        Assert.Equal(JobState.Queued, j.State); Assert.True(j.Resume); Assert.Null(j.Error);
    }

    [Fact]
    public void A_Film_Cut_Off_By_A_Stop_Is_Made_Again_From_The_Start()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash-films-restart-" + Guid.NewGuid().ToString("N"));
        try
        {
            var s1 = new FilmStore(dir);
            s1.Add(new FilmInfo { Request = new("20261023-000000-ffff", "x", ["c1"], ["j"], 640, 480, 10, "joined", false, null, 0, null), CreatedUtc = DateTime.UtcNow,
                Items = { new FilmItem { Ok = true }, new FilmItem { Error = "x" } } });
            Assert.NotNull(s1.TakeNext());
            s1.Update("20261023-000000-ffff", f => { f.Done = 1; f.Failed = 1; f.Phase = "컷 2"; });
            var f = new FilmStore(dir).Get("20261023-000000-ffff")!; // killed while running
            Assert.Equal(JobState.Queued, f.State); Assert.Equal((0, 0), (f.Done, f.Failed)); Assert.All(f.Items, i => Assert.False(i.Ok || i.Error != null)); Assert.Null(f.Phase);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_Job_Cut_Off_A_Third_Time_Fails_And_A_Retry_Starts_The_Count_Again()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash-jobs-cut3-" + Guid.NewGuid().ToString("N"));
        try
        {
            var log = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
            var s = new JobStore(dir, log);
            var id = JobStore.NewId();
            s.Add(new JobInfo { Id = id, Model = "m", CreatedUtc = DateTime.UtcNow });
            s.TakeNext(); s.RequeueCut(id); // a clean stop (the worker)
            Assert.Equal((JobState.Queued, 1), (s.Get(id)!.State, s.Get(id)!.Interrupts));
            s.TakeNext(); s = new JobStore(dir, log); // killed: counted at the next start, from job.json
            Assert.Equal((JobState.Queued, 2), (s.Get(id)!.State, s.Get(id)!.Interrupts));
            s.TakeNext(); s = new JobStore(dir, log);
            var j = s.Get(id)!;
            Assert.Equal(JobState.Failed, j.State); Assert.Equal(JobStore.CutTooOften, j.Error); Assert.NotNull(j.FinishedUtc);
            Assert.True(s.Requeue(id, resume: true)); // the admin's 이어서 하기: three more tries
            Assert.Equal(0, s.Get(id)!.Interrupts);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_Film_Cut_Off_A_Third_Time_Fails()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash-films-cut3-" + Guid.NewGuid().ToString("N"));
        try
        {
            const string id = "20261023-000000-eeee";
            var s = new FilmStore(dir);
            s.Add(new FilmInfo { Request = new(id, "x", ["c1"], ["j"], 640, 480, 10, "joined", false, null, 0, null), CreatedUtc = DateTime.UtcNow, Items = { new FilmItem() } });
            s.TakeNext(); s.RequeueCut(id);
            s.TakeNext(); s = new FilmStore(dir);
            Assert.Equal((JobState.Queued, 2), (s.Get(id)!.State, s.Get(id)!.Interrupts));
            s.TakeNext(); s.RequeueCut(id);
            var f = s.Get(id)!;
            Assert.Equal(JobState.Failed, f.State); Assert.Equal(JobStore.CutTooOften, f.Error); Assert.Equal(JobStore.CutTooOften, f.Items[0].Error);
            Assert.True(s.Retry(id)); // 재시도: queued again, three more tries, nothing of the failed run kept
            f = s.Get(id)!;
            Assert.Equal((JobState.Queued, 0), (f.State, f.Interrupts)); Assert.Null(f.Error); Assert.Null(f.Items[0].Error);
            Assert.False(s.Retry(id)); // a queued film is not retried
        }
        finally { Directory.Delete(dir, true); }
    }
}
