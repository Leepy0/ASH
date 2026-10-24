using System.Net;
using ASH.Core.Runner;
using ASH.Server;

namespace ASH.Server.Tests;

public class JobRerunTests
{
    const string Token = "test!Admin12";

    static Task<HttpResponseMessage> Rerun(JobEnv env, string id, params (string K, string V)[] f) =>
        env.Http.PostAsync($"/jobs/{id}/rerun", new FormUrlEncodedContent(f.Select(x => new KeyValuePair<string, string>(x.K, x.V))));

    /// <summary>A filed job marked finished with G1 decided at 2 (what FakeRunner decides), without running the worker.</summary>
    static async Task<(string Id, int Min, int Max)> Done(JobEnv env)
    {
        var id = await env.SendFiled("A", "P1");
        env.Store.Update(id, j => { j.State = JobState.Done; j.Decided = new() { ["G1"] = 2 }; j.ConfirmRunIds = new() { "n2_s1" }; });
        var g = JobView.For(env.Store, env.Store.Get(id)!).Input!.Groups.Single();
        Assert.True(g.MinCount <= 2 && g.MaxCount >= 3);
        return (id, g.MinCount, g.MaxCount);
    }

    [Fact] public async Task Rerun_Queues_A_Confirm_Only_Version_With_A_Copied_Package()
    {
        using var env = new JobEnv(worker: false);
        var (id, _, _) = await Done(env);
        env.Store.Update(id, j => j.Impact = true);

        using var nr = new HttpClient(env.Factory.Server.CreateHandler()) { BaseAddress = env.Factory.Server.BaseAddress }; // does not follow the redirect
        var res = await nr.PostAsync($"/jobs/{id}/rerun", new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("n.G1", "3") }));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);

        var n = env.Store.All().Single(j => j.Id != id);
        Assert.Equal("/jobs/" + n.Id, res.Headers.Location!.OriginalString);
        Assert.Equal(JobState.Queued, n.State);
        Assert.Equal(3, n.FixedCounts!["G1"]); Assert.False(n.Impact);
        Assert.Equal("대수 변경 확인: G1 2→3", n.VersionNote);
        var old = env.Store.Get(id)!;
        Assert.Equal((old.AnalysisId, old.PlanId, 2), (n.AnalysisId, n.PlanId, n.Version));
        Assert.Equal((old.Project, old.Plan, old.Model), (n.Project, n.Plan, n.Model));
        Assert.True(File.Exists(env.Store.UploadPath(n.Id)));
        Assert.True(File.Exists(Path.Combine(env.Store.ProjectDir(n.Id), "m.arc", "agv.asy")));
        Assert.Equal(old.Files.Count, n.Files.Count);
        Assert.Null(old.FixedCounts); // the original is untouched
    }

    [Fact] public async Task Rerun_Copy_Leaves_Out_The_Run_Outputs_Of_A_Local_Package()
    {
        using var env = new JobEnv(worker: false);
        var (id, _, _) = await Done(env);
        using (var zip = System.IO.Compression.ZipFile.Open(env.Store.UploadPath(id), System.IO.Compression.ZipArchiveMode.Update))
        using (var w = new StreamWriter(zip.CreateEntry("runs/summary.tsv").Open())) w.Write("x");
        Assert.True((await Rerun(env, id, ("n.G1", "3"))).IsSuccessStatusCode);
        var n = env.Store.All().Single(j => j.Id != id);
        Assert.DoesNotContain(n.Files, f => f.Path.StartsWith("runs/"));
        Assert.False(Directory.Exists(Path.Combine(env.Store.ProjectDir(n.Id), "runs")));
        using var z = System.IO.Compression.ZipFile.OpenRead(env.Store.UploadPath(n.Id));
        Assert.DoesNotContain(z.Entries, e => e.FullName.StartsWith("runs/"));
    }

    [Fact] public async Task The_Worker_Runs_The_New_Job_With_Exactly_Those_Counts_And_The_Page_Shows_The_Tables()
    {
        using var env = new JobEnv();
        var id = await env.SendFiled("A", "P1");
        await env.WaitFinished(id);
        Assert.True((await Rerun(env, id, ("n.G1", "3"))).IsSuccessStatusCode);
        var n = await JobEnv.Until(() => env.Store.All().FirstOrDefault(j => j.Id != id && j.Finished));
        Assert.Equal(JobState.Done, n.State);
        Assert.Equal(3, n.Decided!["G1"]);
        lock (env.Runner.FixedSeen) Assert.Equal(new int?[] { null, 3 }, env.Runner.FixedSeen.Select(x => x?["G1"]));
        Assert.Contains("그룹별 권장 대수와 부하율", await env.Http.GetStringAsync("/jobs/" + n.Id));
    }

    [Fact] public async Task Counts_Outside_The_Range_Unknown_Groups_And_No_Change_Are_Rejected_And_Leave_No_Job()
    {
        using var env = new JobEnv(worker: false);
        var (id, min, max) = await Done(env);
        foreach (var f in new[] { ("n.G1", (max + 1).ToString()), ("n.G1", (min - 1).ToString()), ("n.G1", "abc"), ("n.G9", "1"), ("n.G1", "2") })
            Assert.Equal(HttpStatusCode.BadRequest, (await Rerun(env, id, f)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Rerun(env, id)).StatusCode); // no field: nothing changed
        Assert.Single(env.Store.All());
        Assert.Equal(HttpStatusCode.NotFound, (await Rerun(env, "20250101-000000-nope")).StatusCode);
    }

    [Fact] public async Task Only_A_Finished_Job_With_Counts_Can_Be_Rerun()
    {
        using var env = new JobEnv(worker: false);
        var id = await env.SendFiled("A", "P1");
        Assert.Equal(HttpStatusCode.Conflict, (await Rerun(env, id, ("n.G1", "3"))).StatusCode); // still queued
        env.Store.Update(id, j => j.State = JobState.Failed);
        Assert.Equal(HttpStatusCode.Conflict, (await Rerun(env, id, ("n.G1", "3"))).StatusCode); // no counts
        Assert.Single(env.Store.All());
    }

    [Fact] public async Task Only_The_Sending_Pc_Or_An_Admin_May_Rerun()
    {
        using var env = new JobEnv(worker: false, adminToken: Token);
        var (id, _, _) = await Done(env);
        env.Store.Update(id, j => j.Ip = "10.9.9.9");
        Assert.Equal(HttpStatusCode.Forbidden, (await Rerun(env, id, ("n.G1", "3"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Rerun(env, id, ("n.G1", "3"), ("token", "wrong"))).StatusCode);
        Assert.Single(env.Store.All());

        HttpRequestMessage Post(params (string, string)[] headers)
        {
            var m = new HttpRequestMessage(HttpMethod.Post, $"/jobs/{id}/rerun") { Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("n.G1", "3") }) };
            foreach (var (k, v) in headers) m.Headers.Add(k, v);
            return m;
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await env.Http.SendAsync(Post(("Origin", "http://evil.example"), (ReleaseStore.HeaderName, Token)))).StatusCode); // another site
        Assert.Single(env.Store.All());
        Assert.True((await env.Http.SendAsync(Post((ReleaseStore.HeaderName, Token)))).IsSuccessStatusCode);
        Assert.Equal(2, env.Store.All().Count);
    }

    [Fact] public async Task The_Box_Shows_For_The_Sender_Only()
    {
        using var env = new JobEnv(worker: false);
        var (id, _, _) = await Done(env);
        var html = await env.Http.GetStringAsync("/jobs/" + id);
        Assert.Contains("대수 바꿔 다시 돌리기", html); Assert.Contains("확인 실행 보내기", html); Assert.Contains("name=\"n.G1\"", html);
        Assert.DoesNotContain("그룹 고르기", html); // one group: a stepper row, no picker
        env.Store.Update(id, j => j.Ip = "10.9.9.9");
        Assert.DoesNotContain("대수 바꿔 다시 돌리기", await env.Http.GetStringAsync("/jobs/" + id));
    }

    [Fact] public void Many_Groups_Share_One_Picker_Row()
    {
        var groups = Enumerable.Range(1, 20).Select(i => new GroupView($"G{i:00}", "T", 1, 9, 5, 0.5, 1, true, null)).ToList();
        var j = new JobInfo { Id = "20250101-000000-abcd", State = JobState.Done, Decided = groups.ToDictionary(g => g.Group, g => 5) };
        var v = (JobView)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(JobView));
        typeof(JobView).GetProperty(nameof(JobView.Groups))!.SetValue(v, groups);
        var sb = new System.Text.StringBuilder();
        RerunPage.Box(sb, j, v, true);
        var html = sb.ToString();
        Assert.Contains("그룹 고르기", html);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(html, "class=\"rr-row\"").Count);
        Assert.Equal(20, System.Text.RegularExpressions.Regex.Matches(html, "type=\"hidden\" name=\"n\\.G").Count);
        sb.Clear(); RerunPage.Box(sb, j, v, false);
        Assert.Equal("", sb.ToString());
    }
}
