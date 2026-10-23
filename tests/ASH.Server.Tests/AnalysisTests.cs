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
    public void Tags_Are_Saved_And_Listed()
    {
        var st = new AnalysisStore(_dir);
        var a = st.CreateAnalysis("P동 검토", "", "P법인", "냉장고", "1라인");
        st.CreateAnalysis("Q동 검토", "", "p법인", "", "2라인");
        var again = new AnalysisStore(_dir);
        Assert.Equal(("P법인", "냉장고", "1라인"), (again.Get(a.Id)!.Corp, again.Get(a.Id)!.ProductLine, again.Get(a.Id)!.Line));
        Assert.Equal(new[] { "P법인" }, again.TagValues().Corps);
        Assert.Equal(new[] { "1라인", "2라인" }, again.TagValues().Lines);
        again.SetTags(a.Id, "R법인", "", "");
        Assert.Equal("R법인", new AnalysisStore(_dir).Get(a.Id)!.Corp);
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
        st.SetSelected(js, "j0000000001", true);
        st.Move(js, "j0000000001", a.Id, pb.Id);
        Assert.Equal(2, js.Get("j0000000001")!.Version);
        Assert.Equal(pb.Id, js.Get("j0000000001")!.PlanId);
        Assert.True(js.Get("j0000000001")!.Selected); // same analysis: still compared
        var other = st.CreateAnalysis("다른 분석", ""); var po = st.CreatePlan(other.Id, "X안", "");
        st.Move(js, "j0000000001", other.Id, po.Id);
        Assert.False(js.Get("j0000000001")!.Selected); // another analysis: not in its comparison unasked
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
    public async Task Over_Long_Tag_Gets_The_Tag_Message()
    {
        using var env = new JobEnv(worker: false);
        var r = await Post(env, "/api/analyses", null, ("name", "t"), ("corp", new string('x', 101)));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("법인·제품군·라인은 100자까지입니다.", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Tags_Over_Api_And_Jobs()
    {
        using var env = new JobEnv(worker: false);
        await Post(env, "/api/analyses", null, ("name", "분석1"), ("corp", " P법인 "));
        Assert.Equal("P법인", Body(await (await env.Http.GetAsync("/api/analyses")).Content.ReadAsStringAsync())[0].GetProperty("corp").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(env, "/api/analyses", null, ("name", "t"), ("line", new string('x', 101)))).StatusCode);
        await Submit(env, JobEnv.MakePackage(env.Dir), ("analysisName", "새이름"), ("planName", "A"), ("corp", "X"));
        await Submit(env, JobEnv.MakePackage(env.Dir), ("analysisName", "새이름"), ("planName", "B"), ("corp", "Y"));
        var st = env.Factory.Services.GetRequiredService<AnalysisStore>();
        Assert.Equal("X", st.All().Single(a => a.Name == "새이름").Corp);
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
        // a browser always says where it came from: another site may not create analyses or plans, this site may
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(env, "/api/analyses", "http://evil.example", ("name", "z"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(env, $"/api/analyses/{aid}/plans", "http://evil.example", ("name", "Z안"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(env, "/api/analyses", "http://localhost", ("name", "같은 사이트"))).StatusCode);

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

public class AnalysisPageTests
{
    const string Ok = "http://localhost";

    static async Task<string> Get(JobEnv env, string path) => await (await env.Http.GetAsync(path)).Content.ReadAsStringAsync();

    static Task<HttpResponseMessage> Post(JobEnv env, string path, params (string, string)[] kv)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(kv.Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2))) };
        req.Headers.Add("Origin", Ok);
        return env.Http.SendAsync(req);
    }

    static async Task<string> Send(JobEnv env, params (string, string)[] extra)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent("Line A"), "project" }, { new StringContent("m"), "model" }, { new StringContent("tester"), "submitter" },
            { new ByteArrayContent(await File.ReadAllBytesAsync(JobEnv.MakePackage(env.Dir))), "package", "package.zip" },
        };
        foreach (var (k, v) in extra) form.Add(new StringContent(v), k);
        var r = await env.Http.PostAsync("/api/jobs", form);
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Analysis_Page_Shows_Plans_Versions_And_Selection()
    {
        using var env = new JobEnv(worker: false);
        await Send(env, ("analysisName", "라인 증설"), ("planName", "A안"), ("versionNote", "첫 시도"));
        await Send(env, ("analysisName", "라인 증설"), ("planName", "A안"), ("versionNote", "버퍼 늘림"));
        var a = env.Factory.Services.GetRequiredService<AnalysisStore>().All().Single();
        var html = await Get(env, "/analyses/" + a.Id);
        Assert.Contains("A안", html); Assert.Contains("v2", html); Assert.Contains("버퍼 늘림", html);
                Assert.Contains($"/analyses/{a.Id}/cuts", html);
        Assert.Contains("버전을 2개 이상 고르세요", await Get(env, "/analyses/" + a.Id + "?tab=compare"));
        Assert.Contains("아직 만든 영상이 없습니다", await Get(env, "/analyses/" + a.Id + "?tab=films"));
        Assert.Equal(HttpStatusCode.NotFound, (await env.Http.GetAsync("/analyses/a-nope")).StatusCode);
        Assert.Contains("라인 증설", await Get(env, "/analyses"));
    }

    [Fact]
    public async Task Compare_Table_Lists_The_Chosen_Versions_With_Group_Rows()
    {
        using var env = new JobEnv();
        var id1 = await Send(env, ("analysisName", "분석"), ("planName", "A안"));
        var id2 = await Send(env, ("analysisName", "분석"), ("planName", "B안"));
        await env.WaitFinished(id1); await env.WaitFinished(id2);
        var a = env.Factory.Services.GetRequiredService<AnalysisStore>().All().Single();
        Assert.True((int)(await Post(env, $"/jobs/{id2}/select", ("on", "1"), ("back", "/analyses/" + a.Id))).StatusCode < 400);
        var html = await Get(env, "/analyses/" + a.Id + "?tab=compare");
        Assert.Contains("버전을 2개 이상 고르세요", html); // only one version selected
        html = await Get(env, "/analyses/" + a.Id + "?tab=compare&jobs=" + id1 + "," + id2);
        var compare = html[html.IndexOf("버전 비교", StringComparison.Ordinal)..];
        compare = compare[..compare.IndexOf("</table>", StringComparison.Ordinal)];
        Assert.Contains("B안", compare); Assert.Contains("A안", compare);
        Assert.Contains("G1 권장(대)", compare); Assert.Contains("교착(회)", compare); Assert.Contains("결품 합계(초)", compare);
        Assert.Contains("<td class=\"num\">2", compare); // G1 recommended 2
        // the form returns to the analysis page
        var r = await env.Http.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/jobs/{id2}/select") { Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("on", "0"), new KeyValuePair<string, string>("back", "/analyses/" + a.Id) }), Headers = { { "Origin", Ok } } });
        Assert.StartsWith("/analyses/", r.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Unclassified_Lists_Jobs_Without_Analysis()
    {
        using var env = new JobEnv(worker: false);
        var loose = await Send(env);
        var filed = await Send(env, ("analysisName", "분석"), ("planName", "A안"));
        var html = await Get(env, "/analyses/unclassified");
        Assert.Contains(loose, html); Assert.DoesNotContain(filed, html);
        // moving it files it under the plan and it leaves the list
        var a = env.Factory.Services.GetRequiredService<AnalysisStore>().All().Single();
        Assert.True((int)(await Post(env, $"/jobs/{loose}/move", ("target", a.Id + "|" + a.Plans[0].Id), ("back", "/analyses/unclassified"))).StatusCode < 400);
        Assert.Equal(2, env.Store.Get(loose)!.Version);
        Assert.DoesNotContain(loose, await Get(env, "/analyses/unclassified"));
        Assert.Contains("› A안 › v1", await Get(env, "/jobs/" + filed)); // the job page shows where it is filed
    }

    [Fact]
    public async Task Names_Are_Escaped()
    {
        using var env = new JobEnv(worker: false);
        await Send(env, ("analysisName", "<테스트>"), ("planName", "<안>"), ("versionNote", "<메모>"));
        var a = env.Factory.Services.GetRequiredService<AnalysisStore>().All().Single();
        foreach (var path in new[] { "/analyses", "/analyses/" + a.Id, "/jobs/" + env.Store.All().Single().Id })
        {
            var html = await Get(env, path);
            Assert.Contains("&lt;테스트&gt;", html); Assert.DoesNotContain("<테스트>", html); Assert.DoesNotContain("<안>", html); Assert.DoesNotContain("<메모>", html);
        }
    }

    [Fact]
    public async Task New_Analysis_And_Plan_Forms_Work()
    {
        using var env = new JobEnv(worker: false);
        var r = await Post(env, "/analyses", ("name", "새 분석"), ("note", "메모"));
        var st = env.Factory.Services.GetRequiredService<AnalysisStore>();
        var a = st.All().Single();
        Assert.Equal("/analyses/" + a.Id, r.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.True((int)(await Post(env, $"/analyses/{a.Id}/plans", ("name", "A안"), ("note", ""))).StatusCode < 400);
        Assert.Equal("A안", st.Get(a.Id)!.Plans.Single().Name);
        var req = new HttpRequestMessage(HttpMethod.Post, "/analyses") { Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("name", "x") }) };
        Assert.Equal(HttpStatusCode.Forbidden, (await env.Http.SendAsync(req)).StatusCode);
        Assert.Contains("ZWCAD", await Get(env, "/analyses/unclassified")); // empty states say how jobs get here
    }

    [Fact]
    public async Task Back_With_Control_Character_Falls_Back()
    {
        using var env = new JobEnv(worker: false);
        var a = env.Factory.Services.GetRequiredService<AnalysisStore>().CreateAnalysis("분석", "");
        var http = env.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        async Task<string?> Location(HttpContent body)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"/analyses/{a.Id}/rename") { Content = body };
            req.Headers.Add("Origin", Ok);
            return (await http.SendAsync(req)).Headers.Location?.OriginalString;
        }
        static StringContent Raw(string s) => new(s, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
        var fallback = "/analyses/" + a.Id;
        Assert.Equal(fallback, await Location(Raw("name=x&back=/%09/evil.com")));
        Assert.Equal(fallback, await Location(new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("name", "x"), new KeyValuePair<string, string>("back", "/\t/evil.com") })));
        Assert.Equal(fallback, await Location(Raw("name=x&back=/%0d%0a/evil.com")));
        Assert.Equal(fallback, await Location(Raw("name=x&back=//evil.com")));
        Assert.Equal("/jobs", await Location(Raw("name=x&back=/jobs"))); // a plain path is still honored
    }
}
