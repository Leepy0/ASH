using System.Net;
using ASH.Server;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ASH.Server.Tests;

public class RetryTests
{
    const string Token = "test!Admin12";

    static Task<HttpResponseMessage> Retry(JobEnv env, string id, string mode) =>
        env.Http.PostAsync($"/jobs/{id}/retry", new FormUrlEncodedContent(new Dictionary<string, string> { ["mode"] = mode }));

    [Fact]
    public async Task A_Failed_Job_Continues_As_Itself_And_Finishes()
    {
        using var env = new JobEnv();
        env.Runner.Script = (p, s, ct) => throw new InvalidOperationException("boom");
        var id = await env.SendFiled("라인 증설", "A안");
        var failed = await env.WaitFinished(id);
        Assert.Equal(JobState.Failed, failed.State);
        var page = await env.Http.GetStringAsync("/jobs/" + id);
        Assert.Contains("이어서 하기</button>", page); Assert.Contains("처음부터 다시</button>", page);

        env.Runner.Script = null; // the next attempt goes through
        Assert.True((await Retry(env, id, "resume")).IsSuccessStatusCode);
        var j = await env.WaitFinished(id);
        Assert.Equal(JobState.Done, j.State); Assert.NotNull(j.Decided); Assert.Null(j.Error);
        Assert.Equal(1, j.Retries); Assert.Equal(1, j.Version); // the same job and version, not a new one
        Assert.Equal(new[] { false, true }, env.Runner.ResumeSeen);
        Assert.Single(env.Store.All());
        Assert.Equal(HttpStatusCode.Conflict, (await Retry(env, id, "resume")).StatusCode); // a finished job is not run again this way
    }

    [Fact]
    public async Task With_A_Worker_A_Server_Update_Waits_For_It_And_Nothing_Waits_At_First()
    {
        using var env = new JobEnv();
        await env.WaitFinished(await env.SendFiled("라인 증설", "A안")); // the worker is running by now
        var u = env.Factory.Services.GetRequiredService<ServerUpdater>();
        Assert.True(u.WorkerApplies); // an update found while it works is staged, and the worker applies it between two pieces of work
        Assert.Null(u.Waiting);
        Assert.False(await u.ApplyPendingAsync());
        using var off = new JobEnv(worker: false);
        Assert.False(off.Factory.Services.GetRequiredService<ServerUpdater>().WorkerApplies); // no worker: applied at once, as before
    }

    [Fact]
    public async Task The_Admin_Queue_Restarts_A_Cancelled_Job_From_The_Beginning()
    {
        using var env = new JobEnv(worker: false, adminToken: Token);
        var id = await env.SendFiled("라인 증설", "A안");
        env.Store.Update(id, x => { x.State = JobState.Cancelled; x.FinishedUtc = DateTime.UtcNow; x.Error = "취소"; });
        var req = new HttpRequestMessage(HttpMethod.Get, "/admin/jobs"); req.Headers.Add(ReleaseStore.HeaderName, Token);
        Assert.Contains($"action=\"/admin/jobs/{id}/resume\"", await (await env.Http.SendAsync(req)).Content.ReadAsStringAsync());
        var post = new HttpRequestMessage(HttpMethod.Post, $"/admin/jobs/{id}/restart"); post.Headers.Add(ReleaseStore.HeaderName, Token);
        Assert.True((await env.Http.SendAsync(post)).IsSuccessStatusCode);
        var j = env.Store.Get(id)!;
        Assert.Equal(JobState.Queued, j.State); Assert.False(j.Resume); Assert.Null(j.Error); Assert.Null(j.FinishedUtc);
    }
}
