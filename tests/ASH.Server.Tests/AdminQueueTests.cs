using System.Net;
using ASH.Server;
using Xunit;

namespace ASH.Server.Tests;

public class AdminQueueTests
{
    const string Token = "test!Admin12";

    static async Task<HttpResponseMessage> Admin(JobEnv env, HttpMethod m, string url)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Add(ReleaseStore.HeaderName, Token);
        return await env.Http.SendAsync(req);
    }

    static async Task<string> Page(JobEnv env, string url = "/admin/jobs") => await (await Admin(env, HttpMethod.Get, url)).Content.ReadAsStringAsync();

    [Fact]
    public async Task Finished_Jobs_Are_Named_Like_The_Analysis_And_Can_Be_Archived_And_Deleted()
    {
        using var env = new JobEnv(adminToken: Token);
        var j = await env.WaitFinished(await env.SendFiled("라인 증설", "A안"));
        var page = await Page(env);
        Assert.Contains("라인 증설 › A안 v1</a>", page); // the analysis name, not project / plan / model
        Assert.DoesNotContain("<th>모델</th>", page);
        Assert.Contains($"action=\"/admin/jobs/{j.Id}/archive\"", page); Assert.Contains($"action=\"/admin/jobs/{j.Id}/delete\"", page);

        Assert.True((await Admin(env, HttpMethod.Post, $"/admin/jobs/{j.Id}/archive")).IsSuccessStatusCode); // redirected back to the list
        Assert.True(env.Store.Get(j.Id)!.Archived);
        Assert.DoesNotContain($"/jobs/{j.Id}\"", await Page(env));
        Assert.Contains($"/admin/jobs/{j.Id}/unarchive?archived=1", await Page(env, "/admin/jobs?archived=1"));

        await Admin(env, HttpMethod.Post, $"/admin/jobs/{j.Id}/delete");
        Assert.Null(env.Store.Get(j.Id));
        Assert.False(Directory.Exists(env.Store.JobDir(j.Id)));
    }

    [Fact]
    public async Task A_Queued_Job_Is_Cancelled_And_Cannot_Be_Deleted_Before()
    {
        using var env = new JobEnv(worker: false, adminToken: Token);
        var id = await env.SendFiled("라인 증설", "A안");
        Assert.Equal(HttpStatusCode.Conflict, (await Admin(env, HttpMethod.Post, $"/admin/jobs/{id}/delete")).StatusCode);
        Assert.Contains($"action=\"/admin/jobs/{id}/cancel\"", await Page(env));
        await Admin(env, HttpMethod.Post, $"/admin/jobs/{id}/cancel");
        Assert.Equal(JobState.Cancelled, env.Store.Get(id)!.State);
    }

    [Fact]
    public async Task Without_The_Admin_Password_Nothing_Changes()
    {
        using var env = new JobEnv(worker: false, adminToken: Token);
        var id = await env.SendFiled("라인 증설", "A안");
        var r = await env.Http.PostAsync($"/admin/jobs/{id}/cancel", null);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal(JobState.Queued, env.Store.Get(id)!.State);
    }
}
