using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ASH.Server;
using Microsoft.Extensions.Logging.Abstractions;

namespace ASH.Server.Tests;

public class AnalysisTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ash-analysis-test-" + Guid.NewGuid().ToString("N"));
    JobStore Jobs() => new(_dir, NullLogger.Instance);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    static string AddJob(JobStore js, string id)
    {
        js.Add(new JobInfo { Id = id, CreatedUtc = DateTime.UtcNow });
        return id;
    }

    [Fact]
    public void Assign_Gives_Next_Version_Per_Plan()
    {
        var js = Jobs(); var st = new AnalysisStore(_dir);
        var a = st.CreateAnalysis("분석", ""); var pa = st.CreatePlan(a.Id, "A안", ""); var pb = st.CreatePlan(a.Id, "B안", "");
        Assert.Matches("^a-[0-9a-f]{8}$", a.Id); Assert.Matches("^p-[0-9a-f]{8}$", pa.Id);
        var v = new[] { "j0000000001", "j0000000002", "j0000000003" }.Select(id => st.Assign(js, AddJob(js, id), a.Id, pa.Id, "메모").Version).ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, v);
        Assert.Equal(1, st.Assign(js, AddJob(js, "j0000000004"), a.Id, pb.Id, "").Version);
        Assert.Equal("메모", js.Get("j0000000001")!.VersionNote);
        st.Move(js, "j0000000001", a.Id, pb.Id);
        Assert.Equal(2, js.Get("j0000000001")!.Version);
        Assert.Equal(pb.Id, js.Get("j0000000001")!.PlanId);
    }

    [Fact]
    public void Assign_Is_Unique_Under_Parallel_Calls()
    {
        var js = Jobs(); var st = new AnalysisStore(_dir);
        var a = st.CreateAnalysis("분석", ""); var p = st.CreatePlan(a.Id, "A안", "");
        var ids = Enumerable.Range(0, 20).Select(i => AddJob(js, $"j{i:D10}")).ToArray();
        var got = new System.Collections.Concurrent.ConcurrentBag<int>();
        Parallel.For(0, 20, i => got.Add(st.Assign(js, ids[i], a.Id, p.Id, "").Version));
        Assert.Equal(Enumerable.Range(1, 20), got.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Survives_Reload()
    {
        var st = new AnalysisStore(_dir);
        var a = st.CreateAnalysis("분석1", "설명"); var p = st.CreatePlan(a.Id, "A안", "n");
        st.Rename(a.Id, p.Id, "바뀐 안");
        var again = new AnalysisStore(_dir).Get(a.Id)!;
        Assert.Equal("분석1", again.Name); Assert.Equal("설명", again.Note);
        Assert.Equal(p.Id, again.Plans.Single().Id); Assert.Equal("바뀐 안", again.Plans.Single().Name);
    }

    [Fact]
    public void DeleteIfEmpty_Refuses_When_A_Job_Points_At_It()
    {
        var js = Jobs(); var st = new AnalysisStore(_dir);
        var a = st.CreateAnalysis("분석", ""); var p = st.CreatePlan(a.Id, "A안", "");
        st.Assign(js, AddJob(js, "j0000000001"), a.Id, p.Id, "");
        Assert.False(st.DeleteIfEmpty(a.Id, js));
        Assert.NotNull(st.Get(a.Id));
        var b = st.CreateAnalysis("빈 분석", "");
        Assert.True(st.DeleteIfEmpty(b.Id, js));
        Assert.Null(new AnalysisStore(_dir).Get(b.Id));
    }

    [Fact]
    public void Duplicate_Plan_Name_In_Analysis_Throws_ArgumentException()
    {
        var st = new AnalysisStore(_dir);
        var a = st.CreateAnalysis("분석", ""); var other = st.CreateAnalysis("다른 분석", "");
        st.CreatePlan(a.Id, "A안", "");
        Assert.Throws<ArgumentException>(() => st.CreatePlan(a.Id, "A안", ""));
        st.CreatePlan(other.Id, "A안", ""); // same name in another analysis is fine
    }

    [Fact]
    public void Corrupt_File_Is_Moved_Aside_And_Store_Starts_Empty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "analyses.json"), "{garbage");
        var st = new AnalysisStore(_dir, NullLogger.Instance);
        Assert.Empty(st.All());
        Assert.Equal("{garbage", File.ReadAllText(Directory.GetFiles(_dir, "analyses.json.bad-*").Single()));
        var a = st.CreateAnalysis("새 분석", "");
        Assert.Equal(a.Id, new AnalysisStore(_dir).All().Single().Id);
        Assert.Throws<ArgumentException>(() => st.CreatePlan("a-00000000", "A안", "")); // a job's dangling analysis id is simply not found
    }

    [Fact]
    public void Unreadable_But_Valid_File_Is_Not_Moved_Aside()
    {
        var st = new AnalysisStore(_dir);
        st.CreateAnalysis("분석", "");
        var path = Path.Combine(_dir, "analyses.json");
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<IOException>(() => new AnalysisStore(_dir, NullLogger.Instance));
        Assert.Empty(Directory.GetFiles(_dir, "analyses.json.bad-*"));
        Assert.Single(new AnalysisStore(_dir).All());
    }

    [Fact]
    public void Old_Job_Json_Loads_As_Unclassified()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "20260101-000000-abcd"));
        File.WriteAllText(Path.Combine(_dir, "20260101-000000-abcd", "job.json"), "{\"Id\":\"20260101-000000-abcd\",\"State\":\"done\"}");
        var j = Jobs().Get("20260101-000000-abcd")!;
        Assert.Equal(0, j.Version); Assert.Null(j.AnalysisId); Assert.False(j.Selected);
    }
}

public class AnalysisApiTests
{
    static async Task<HttpResponseMessage> Submit(JobEnv env, string zip, params (string, string)[] extra)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent("Line A"), "project" }, { new StringContent("도면안"), "plan" }, { new StringContent("m"), "model" },
            { new StringContent("tester"), "submitter" }, { new StringContent("1"), "impact" },
            { new ByteArrayContent(await File.ReadAllBytesAsync(zip)), "package", "package.zip" },
        };
        foreach (var (k, v) in extra) form.Add(new StringContent(v), k);
        return await env.Http.PostAsync("/api/jobs", form);
    }

    static JsonElement Body(string s) => JsonDocument.Parse(s).RootElement;

    static Task<HttpResponseMessage> Post(JobEnv env, string path, string? origin, params (string, string)[] kv)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(kv.Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2))) };
        if (origin != null) req.Headers.Add("Origin", origin);
        return env.Http.SendAsync(req);
    }

    [Fact]
    public async Task Submit_With_Names_Creates_Analysis_And_Plan_And_Returns_Version_1()
    {
        using var env = new JobEnv(worker: false);
        var st = env.Factory.Services.GetRequiredService<AnalysisStore>();
        var r1 = await Submit(env, JobEnv.MakePackage(env.Dir), ("analysisName", "라인 증설"), ("planName", "A안"), ("versionNote", "첫 시도"));
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var b1 = Body(await r1.Content.ReadAsStringAsync());
        Assert.Equal(1, b1.GetProperty("version").GetInt32());
        var a = Assert.Single(st.All());
        Assert.Equal("라인 증설", a.Name); Assert.Equal("A안", Assert.Single(a.Plans).Name);
        var j = env.Store.Get(b1.GetProperty("id").GetString()!)!;
        Assert.Equal(a.Id, j.AnalysisId); Assert.Equal(a.Plans[0].Id, j.PlanId); Assert.Equal("첫 시도", j.VersionNote);

        // the same names again find the existing analysis and plan, and the version counts up
        var r2 = await Submit(env, JobEnv.MakePackage(env.Dir), ("analysisName", "라인 증설"), ("planName", "a안"));
        Assert.Equal(2, Body(await r2.Content.ReadAsStringAsync()).GetProperty("version").GetInt32());
        Assert.Single(st.All()); Assert.Single(st.All()[0].Plans);

        // ids win over names
        var r3 = await Submit(env, JobEnv.MakePackage(env.Dir), ("analysisId", a.Id), ("planId", a.Plans[0].Id));
        Assert.Equal(3, Body(await r3.Content.ReadAsStringAsync()).GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Submit_Without_Analysis_Fields_Is_Unclassified()
    {
        using var env = new JobEnv(worker: false);
        var r = await Submit(env, JobEnv.MakePackage(env.Dir));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var b = Body(await r.Content.ReadAsStringAsync());
        Assert.Equal(0, b.GetProperty("version").GetInt32());
        var j = env.Store.Get(b.GetProperty("id").GetString()!)!;
        Assert.Null(j.AnalysisId); Assert.Null(j.PlanId); Assert.Equal(0, j.Version);
        Assert.Empty(env.Factory.Services.GetRequiredService<AnalysisStore>().All());
    }

    [Fact]
    public async Task Submit_Rejects_Bad_Analysis_Fields_Before_Anything_Is_Stored()
    {
        using var env = new JobEnv(worker: false);
        var zip = JobEnv.MakePackage(env.Dir);
        foreach (var extra in new[]
        {
            new[] { ("analysisName", new string('가', 101)), ("planName", "A") },
            new[] { ("analysisName", "x"), ("planName", new string('a', 101)) },
            new[] { ("analysisName", "x"), ("planName", "A"), ("versionNote", new string('a', 201)) },
            new[] { ("analysisName", "x") },           // plan missing
            new[] { ("planName", "A") },               // analysis missing
            new[] { ("analysisId", "a-nope"), ("planId", "p-nope") },
        })
        {
            var r = await Submit(env, zip, extra);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }
        Assert.Empty(env.Factory.Services.GetRequiredService<AnalysisStore>().All());
        Assert.Empty(env.Store.All());
    }

    [Fact]
    public async Task Api_Analyses_Lists_Next_Version()
    {
        using var env = new JobEnv(worker: false);
        var c = await Post(env, "/api/analyses", null, ("name", "분석1"), ("note", "메모"));
        var aid = Body(await c.Content.ReadAsStringAsync()).GetProperty("id").GetString()!;
        var p = await Post(env, $"/api/analyses/{aid}/plans", null, ("name", "A안"), ("note", ""));
        var pid = Body(await p.Content.ReadAsStringAsync()).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(env, $"/api/analyses/{aid}/plans", null, ("name", "a안"))).StatusCode); // duplicate
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(env, "/api/analyses", null, ("name", new string('x', 101)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(env, "/api/analyses", null, ("name", "y"), ("note", new string('x', 201)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(env, "/api/analyses/a-nope/plans", null, ("name", "B"))).StatusCode);

        async Task<int> Next() => Body(await (await env.Http.GetAsync("/api/analyses")).Content.ReadAsStringAsync())[0].GetProperty("plans")[0].GetProperty("nextVersion").GetInt32();
        Assert.Equal(1, await Next());
        await Submit(env, JobEnv.MakePackage(env.Dir), ("analysisId", aid), ("planId", pid));
        Assert.Equal(2, await Next());
        var list = Body(await (await env.Http.GetAsync("/api/analyses")).Content.ReadAsStringAsync())[0];
        Assert.Equal(aid, list.GetProperty("id").GetString()); Assert.Equal("분석1", list.GetProperty("name").GetString()); Assert.Equal("메모", list.GetProperty("note").GetString());
    }

    [Fact]
    public async Task Move_And_Select_Require_Same_Origin()
    {
        using var env = new JobEnv(worker: false);
        var st = env.Factory.Services.GetRequiredService<AnalysisStore>();
        var a = st.CreateAnalysis("분석", ""); var pa = st.CreatePlan(a.Id, "A", ""); var pb = st.CreatePlan(a.Id, "B", "");
        var id = Body(await (await Submit(env, JobEnv.MakePackage(env.Dir), ("analysisId", a.Id), ("planId", pa.Id))).Content.ReadAsStringAsync()).GetProperty("id").GetString()!;

        foreach (var origin in new[] { "http://evil.example", null })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Post(env, $"/jobs/{id}/move", origin, ("analysisId", a.Id), ("planId", pb.Id))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Post(env, $"/jobs/{id}/select", origin, ("on", "1"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Post(env, $"/analyses/{a.Id}/rename", origin, ("name", "x"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Post(env, $"/analyses/{a.Id}/plans/{pa.Id}/rename", origin, ("name", "x"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Post(env, $"/analyses/{a.Id}/delete", origin)).StatusCode);
        }
        var j = env.Store.Get(id)!;
        Assert.Equal(pa.Id, j.PlanId); Assert.False(j.Selected); Assert.Equal("분석", st.Get(a.Id)!.Name);

        var ok = "http://localhost";
        Assert.True((int)(await Post(env, $"/jobs/{id}/move", ok, ("analysisId", a.Id), ("planId", pb.Id))).StatusCode < 400);
        Assert.True((int)(await Post(env, $"/jobs/{id}/select", ok, ("on", "1"))).StatusCode < 400);
        j = env.Store.Get(id)!;
        Assert.Equal(pb.Id, j.PlanId); Assert.Equal(1, j.Version); Assert.True(j.Selected);
        Assert.True((int)(await Post(env, $"/analyses/{a.Id}/rename", ok, ("name", "새 이름"))).StatusCode < 400);
        Assert.True((int)(await Post(env, $"/analyses/{a.Id}/plans/{pa.Id}/rename", ok, ("name", "A2"))).StatusCode < 400);
        Assert.Equal("새 이름", st.Get(a.Id)!.Name); Assert.Equal("A2", st.Get(a.Id)!.Plans[0].Name);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(env, $"/analyses/{a.Id}/delete", ok)).StatusCode); // a job still points at it
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(env, $"/jobs/{id}/move", ok, ("analysisId", a.Id), ("planId", "p-nope"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(env, "/jobs/20260101-000000-abcd/select", ok, ("on", "1"))).StatusCode);
        var empty = st.CreateAnalysis("빈 분석", "");
        Assert.True((int)(await Post(env, $"/analyses/{empty.Id}/delete", ok)).StatusCode < 400);
        Assert.Null(st.Get(empty.Id));
    }
}
