using System.Net;
using ASH.Core.Film;
using Microsoft.Extensions.DependencyInjection;

namespace ASH.Server.Tests;

/// <summary>The admin delete of an analysis (/admin/analyses/{id}/delete): jobs stay as unclassified, cuts and films go, a queued or running film stops it.</summary>
public class AnalysisDeleteTests
{
    const string Token = "test-admin-token-1234567890", Ok = "http://localhost";

    sealed record Fixture(string AnalysisId, string JobId, string FilmId, string FilmDir, string CutsFile);

    static T Svc<T>(JobEnv env) where T : notnull => env.Factory.Services.GetRequiredService<T>();

    static FilmInfo MakeFilm(string id, string analysisId, string state) => new()
    {
        Request = new FilmRequest(id, analysisId, new[] { "c1" }, Array.Empty<string>(), 1280, 720, 15, "each+joined", true, null, 0, null),
        State = state, CreatedUtc = DateTime.UtcNow,
    };

    /// <summary>One analysis "분석" with plan "A안", one selected job v1 with a note, one cut and one finished film with a file.</summary>
    static Fixture Make(JobEnv env)
    {
        var st = Svc<AnalysisStore>(env); var js = env.Store;
        var a = st.CreateAnalysis("분석", ""); var p = st.CreatePlan(a.Id, "A안", "");
        js.Add(new JobInfo { Id = "20260101-000000-aaaa", CreatedUtc = DateTime.UtcNow, State = JobState.Done });
        st.Assign(js, "20260101-000000-aaaa", a.Id, p.Id, "메모"); st.SetSelected(js, "20260101-000000-aaaa", true);
        var cs = Svc<CutStore>(env);
        cs.Save(a.Id, new[] { new CutSpec("c1", "입구", CutKind.Hold, 7200, 600, new Rect(1, 2, 30, 18), null, 0, 0, 0, 10, 30, 0, true, true) });
        var fs = Svc<FilmStore>(env);
        fs.Add(MakeFilm("20260101-000001-bbbb", a.Id, JobState.Done));
        File.WriteAllText(Path.Combine(fs.FilmDir("20260101-000001-bbbb"), "cut.mp4"), "x");
        return new Fixture(a.Id, "20260101-000000-aaaa", "20260101-000001-bbbb", fs.FilmDir("20260101-000001-bbbb"), Path.Combine(env.Dir, "jobs", "analyses", a.Id, "cuts.json"));
    }

    static Task<HttpResponseMessage> Post(JobEnv env, string id, string name, string? origin = Ok)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/admin/analyses/{id}/delete") { Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("name", name) }) };
        if (origin != null) req.Headers.Add("Origin", origin);
        return env.Http.SendAsync(req);
    }

    static async Task Login(JobEnv env, string? back = null)
    {
        var form = new List<KeyValuePair<string, string>> { new("token", Token) };
        if (back != null) form.Add(new("back", back));
        var req = new HttpRequestMessage(HttpMethod.Post, "/admin/login") { Content = new FormUrlEncodedContent(form) };
        req.Headers.Add("Origin", Ok);
        Assert.True((int)(await env.Http.SendAsync(req)).StatusCode < 400);
    }

    [Fact]
    public async Task Needs_Admin_Login_Same_Origin_And_The_Name()
    {
        using var env = new JobEnv(worker: false, adminToken: Token);
        var f = Make(env);

        var page = await env.Http.GetAsync($"/admin/analyses/{f.AnalysisId}/delete");
        Assert.Equal(HttpStatusCode.Unauthorized, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("관리자 토큰", html); Assert.Contains($"name=\"back\" value=\"/admin/analyses/{f.AnalysisId}/delete\"", html);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(env, f.AnalysisId, "분석")).StatusCode);
        Assert.NotNull(Svc<AnalysisStore>(env).Get(f.AnalysisId));

        await Login(env, $"/admin/analyses/{f.AnalysisId}/delete");
        var confirm = await env.Http.GetStringAsync($"/admin/analyses/{f.AnalysisId}/delete");
        Assert.Contains("보낸 작업 1건은 그대로 남고", confirm); Assert.Contains("컷 목록(1개)", confirm); Assert.Contains("영상 1건", confirm);
        Assert.Contains("분석 지우기", confirm);

        Assert.Equal(HttpStatusCode.Forbidden, (await Post(env, f.AnalysisId, "분석", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(env, f.AnalysisId, "분석", "http://evil.example")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(env, f.AnalysisId, "다른 이름")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(env, "a-00000000", "분석")).StatusCode);
        Assert.NotNull(Svc<AnalysisStore>(env).Get(f.AnalysisId)); Assert.True(File.Exists(f.CutsFile)); Assert.True(Directory.Exists(f.FilmDir)); // nothing was touched

        Assert.True((int)(await Post(env, f.AnalysisId, "분석")).StatusCode < 400);
        Assert.Null(Svc<AnalysisStore>(env).Get(f.AnalysisId));
        Assert.Null(new AnalysisStore(Path.Combine(env.Dir, "jobs")).Get(f.AnalysisId)); // saved
        var j = env.Store.Get(f.JobId)!;
        Assert.Equal((null, null, 0, false, "메모"), (j.AnalysisId, j.PlanId, j.Version, j.Selected, j.VersionNote));
        Assert.Equal(JobState.Done, j.State);
        Assert.Contains(f.JobId, await env.Http.GetStringAsync("/analyses/unclassified"));
        Assert.False(File.Exists(f.CutsFile)); Assert.False(Directory.Exists(Path.GetDirectoryName(f.CutsFile)));
        Assert.Null(Svc<FilmStore>(env).Get(f.FilmId)); Assert.False(Directory.Exists(f.FilmDir));
        Assert.Equal(HttpStatusCode.NotFound, (await env.Http.GetAsync($"/analyses/{f.AnalysisId}")).StatusCode);
    }

    [Fact]
    public async Task Queued_Or_Running_Film_Refuses_With_409_And_Keeps_Everything()
    {
        using var env = new JobEnv(worker: false, adminToken: Token);
        var f = Make(env);
        var fs = Svc<FilmStore>(env);
        await Login(env);
        foreach (var state in new[] { JobState.Running, JobState.Queued })
        {
            fs.Add(MakeFilm("20260101-000002-cccc", f.AnalysisId, state));
            var r = await Post(env, f.AnalysisId, "분석");
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Contains("취소", await r.Content.ReadAsStringAsync());
            var page = await env.Http.GetStringAsync($"/admin/analyses/{f.AnalysisId}/delete");
            Assert.DoesNotContain("분석 지우기", page); Assert.Contains("먼저 취소", page);
            Assert.NotNull(Svc<AnalysisStore>(env).Get(f.AnalysisId)); Assert.Equal(f.AnalysisId, env.Store.Get(f.JobId)!.AnalysisId);
            Assert.True(File.Exists(f.CutsFile)); Assert.True(Directory.Exists(f.FilmDir)); Assert.NotNull(fs.Get(f.FilmId));
        }
        fs.Update("20260101-000002-cccc", x => x.State = JobState.Cancelled); // cancelled: the delete goes through
        Assert.True((int)(await Post(env, f.AnalysisId, "분석")).StatusCode < 400);
        Assert.Null(Svc<AnalysisStore>(env).Get(f.AnalysisId)); Assert.Empty(fs.All());
    }

    [Fact]
    public async Task Header_Token_Works_Like_The_Login_And_Without_A_Token_Setting_It_Is_Off()
    {
        using (var env = new JobEnv(worker: false, adminToken: Token))
        {
            var f = Make(env);
            var req = new HttpRequestMessage(HttpMethod.Post, $"/admin/analyses/{f.AnalysisId}/delete") { Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("name", "분석") }) };
            req.Headers.Add(ReleaseStore.HeaderName, Token);
            Assert.True((int)(await env.Http.SendAsync(req)).StatusCode < 400);
            Assert.Null(Svc<AnalysisStore>(env).Get(f.AnalysisId));
        }
        using (var env = new JobEnv(worker: false))
        {
            var f = Make(env);
            Assert.Equal(HttpStatusCode.Forbidden, (await env.Http.GetAsync($"/admin/analyses/{f.AnalysisId}/delete")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Post(env, f.AnalysisId, "분석")).StatusCode);
            Assert.NotNull(Svc<AnalysisStore>(env).Get(f.AnalysisId));
        }
    }

    [Fact]
    public async Task Detail_Page_Links_To_The_Confirm_Page_For_Everyone()
    {
        using var env = new JobEnv(worker: false, adminToken: Token);
        var f = Make(env);
        Assert.Contains($"href=\"/admin/analyses/{f.AnalysisId}/delete\">관리자: 분석 삭제…", await env.Http.GetStringAsync("/analyses/" + f.AnalysisId));
    }
}
