using System.Net;
using System.Text;
using System.Text.Json;
using ASH.Core.Film;
using Microsoft.Extensions.DependencyInjection;

namespace ASH.Server.Tests;

public class CutTests
{
    const string Ok = "http://localhost";

    static CutSpec Hold(string id = "c1", double tilt = 30) =>
        new(id, "조립라인 입구", CutKind.Hold, 7200, 600, new Rect(1, 2, 30, 18), null, 0, 0, 0, 10, tilt, 0, true, true);

    static CutSpec Move(string id = "c2") =>
        new(id, "L001 → U001", CutKind.Move, 9000, 0, new Rect(0, 0, 10, 10), new Rect(20, 5, 12, 8), 120, 60, 90, 20, 45, 90, false, false, -1);

    static Task<HttpResponseMessage> Put(JobEnv env, string analysisId, string body, string? origin = Ok)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/analyses/{analysisId}/cuts") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (origin != null) req.Headers.Add("Origin", origin);
        return env.Http.SendAsync(req);
    }

    static async Task<string> Send(JobEnv env, string analysis, string plan, double dx = 0)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent("Line A"), "project" }, { new StringContent("m"), "model" }, { new StringContent("tester"), "submitter" },
            { new StringContent(analysis), "analysisName" }, { new StringContent(plan), "planName" },
            { new ByteArrayContent(await File.ReadAllBytesAsync(JobEnv.MakePackage(env.Dir, dx: dx))), "package", "package.zip" },
        };
        var r = await env.Http.PostAsync("/api/jobs", form);
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
    }

    [Fact]
    public void Save_And_Get_Round_Trip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash-cut-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var st = new CutStore(dir);
            Assert.Empty(st.Get("a-12345678"));
            st.Save("a-12345678", new[] { Hold(), Move() });
            var back = new CutStore(dir).Get("a-12345678");
            Assert.Equal(new[] { Hold(), Move() }, back);
            Assert.True(File.Exists(Path.Combine(dir, "analyses", "a-12345678", "cuts.json")));
            Assert.Equal(13.5, back[1].VideoSeconds, 6); // (120+60+90) simulation s / 20
            Assert.Throws<ArgumentException>(() => st.Save("a-12345678", new[] { Hold(tilt: 80) }));
            Assert.Equal(2, st.Get("a-12345678").Count); // a rejected list leaves the saved one alone
            Assert.Throws<ArgumentException>(() => st.Get("..\\x"));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void Corrupt_Cuts_File_Is_Set_Aside()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash-cut-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = Path.Combine(dir, "analyses", "a-12345678", "cuts.json");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "[{garbage");
            var st = new CutStore(dir);
            Assert.Empty(st.Get("a-12345678"));
            Assert.False(File.Exists(file));
            Assert.Equal("[{garbage", File.ReadAllText(Directory.GetFiles(Path.GetDirectoryName(file)!, "cuts.json.bad-*").Single()));
            File.WriteAllText(file, "null"); // a second bad file in the same second does not overwrite the first
            Assert.Empty(st.Get("a-12345678"));
            Assert.Equal(2, Directory.GetFiles(Path.GetDirectoryName(file)!, "cuts.json.bad-*").Length);
            st.Save("a-12345678", new[] { Hold() });
            Assert.Single(st.Get("a-12345678"));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Theory]
    [InlineData("tilt", "기울기는 0~70°")]
    [InlineData("speed", "배속은 1~600")]
    [InlineData("dir", "방향은 0~359°")]
    [InlineData("time", "0초 이상")]
    [InlineData("noB", "구역 B")]
    [InlineData("name", "이름은 1~60자")]
    [InlineData("dup", "id가 겹칩니다")]
    [InlineData("holdA0", "각각 0초보다")]
    [InlineData("move0", "각각 0초보다")]
    [InlineData("holdB0", "각각 0초보다")]
    public void Validate_Gives_Korean_Reasons(string what, string reason)
    {
        var c = Hold();
        CutSpec[] list = what switch
        {
            "holdA0" => new[] { Move() with { HoldASec = 0 } },
            "move0" => new[] { Move() with { MoveSec = 0 } },
            "holdB0" => new[] { Move() with { HoldBSec = 0 } },
            "tilt" => new[] { c with { TiltDeg = 80 } },
            "speed" => new[] { c with { Speed = 0.5 } },
            "dir" => new[] { c with { DirDeg = 360 } },
            "time" => new[] { c with { StartSec = -1 } },
            "noB" => new[] { Move() with { B = null } },
            "name" => new[] { c with { Name = new string('가', 61) } },
            _ => new[] { c, Move("c1") },
        };
        Assert.Contains(reason, CutStore.Validate(list));
        Assert.Null(CutStore.Validate(new[] { Hold(), Move() }));
    }

    [Fact]
    public async Task Invalid_Cut_Returns_400_With_Reason()
    {
        using var env = new JobEnv(worker: false);
        var a = env.Factory.Services.GetRequiredService<AnalysisStore>().CreateAnalysis("분석", "");
        var r = await Put(env, a.Id, JsonSerializer.Serialize(new[] { Hold(tilt: 80) }, CutStore.Json));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("기울기는 0~70°", await r.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(env, a.Id, "{\"not\":\"a list\"}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(env, a.Id, "[{\"kind\":\"Fly\"}]")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Put(env, "a-nope0000", "[]")).StatusCode);
    }

    [Fact]
    public async Task Put_Requires_Same_Origin()
    {
        using var env = new JobEnv(worker: false);
        var a = env.Factory.Services.GetRequiredService<AnalysisStore>().CreateAnalysis("분석", "");
        var body = JsonSerializer.Serialize(new[] { Hold(), Move() }, CutStore.Json);
        Assert.Equal(HttpStatusCode.Forbidden, (await Put(env, a.Id, body, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Put(env, a.Id, body, "http://evil.example")).StatusCode);
        Assert.Equal("[]", await env.Http.GetStringAsync($"/api/analyses/{a.Id}/cuts"));
        Assert.Equal(HttpStatusCode.OK, (await Put(env, a.Id, body)).StatusCode);
        var got = JsonSerializer.Deserialize<List<CutSpec>>(await env.Http.GetStringAsync($"/api/analyses/{a.Id}/cuts"), CutStore.Json)!;
        Assert.Equal(new[] { Hold(), Move() }, got);
        Assert.Contains("\"kind\":\"Move\"", await env.Http.GetStringAsync($"/api/analyses/{a.Id}/cuts")); // the page reads names, not numbers
    }

    [Fact]
    public async Task Editor_Renders_Without_Dxf()
    {
        using var env = new JobEnv(worker: false);
        var id = await Send(env, "<분석>", "A안");
        var a = env.Factory.Services.GetRequiredService<AnalysisStore>().All().Single();
        Assert.False(File.Exists(Path.Combine(env.Store.ProjectDir(id), "drawing.dxf")));
        var res = await env.Http.GetAsync($"/analyses/{a.Id}/cuts");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("컷 편집기", html);
        Assert.Contains(id, html); // the base drawing is this job's drawing.json
        Assert.Contains("&lt;분석&gt;", html); Assert.DoesNotContain("<분석>", html);
        Assert.DoesNotContain("레이아웃 위치가", html);
        Assert.DoesNotContain("https://", html); // nothing from another host
        Assert.Equal(HttpStatusCode.OK, (await env.Http.GetAsync($"/jobs/{id}/drawing.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await env.Http.GetAsync("/analyses/a-nope0000/cuts")).StatusCode);
    }

    [Fact]
    public async Task Editor_Warns_When_Version_Layouts_Differ()
    {
        using var env = new JobEnv(worker: false);
        var j1 = await Send(env, "분석", "A안");
        var j2 = await Send(env, "분석", "B안", dx: 5); // layout is 11 m wide: 5 m is over 20%
        var j3 = await Send(env, "분석", "C안", dx: 1); // 1 m is not
        var st = env.Factory.Services.GetRequiredService<AnalysisStore>();
        var a = st.All().Single();
        st.SetSelected(env.Store, j1, true); st.SetSelected(env.Store, j3, true);
        Assert.DoesNotContain("레이아웃 위치가", await env.Http.GetStringAsync($"/analyses/{a.Id}/cuts"));
        st.SetSelected(env.Store, j2, true);
        var html = await env.Http.GetStringAsync($"/analyses/{a.Id}/cuts");
        var warn = html[html.IndexOf("레이아웃 위치가", StringComparison.Ordinal)..];
        warn = warn[..warn.IndexOf("</div>", StringComparison.Ordinal)];
        Assert.Contains("바탕 C안 v1", warn); Assert.Contains("B안 v1(4", warn); Assert.DoesNotContain("A안", warn); // base = newest selected (C); B is 4 m away, A only 1 m
    }

    [Fact]
    public async Task Scenes_Pass_Through_And_Requests_Are_Recorded()
    {
        using var env = new JobEnv(worker: false);
        var id = await Send(env, "분석", "A안");
        Assert.Equal("[]", await env.Http.GetStringAsync($"/api/jobs/{id}/scenes"));
        File.WriteAllText(Path.Combine(env.Store.JobDir(id), "scenes.json"), "{\"none\":\"새 버전으로 다시 보내면 생깁니다\"}");
        Assert.Contains("새 버전으로", await env.Http.GetStringAsync($"/api/jobs/{id}/scenes"));
        Assert.Equal(HttpStatusCode.NotFound, (await env.Http.GetAsync("/api/jobs/20260101-000000-zzzz/scenes")).StatusCode);

        HttpRequestMessage Req(string q, string? origin) { var m = new HttpRequestMessage(HttpMethod.Post, $"/api/jobs/{id}/scenes{q}"); if (origin != null) m.Headers.Add("Origin", origin); return m; }
        Assert.Equal(HttpStatusCode.Forbidden, (await env.Http.SendAsync(Req("?delta=-1", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await env.Http.SendAsync(Req("?delta=-1", Ok))).StatusCode); // no conclusion yet
        env.Store.Update(id, j => { j.State = JobState.Done; j.Decided = new() { ["G1"] = 2 }; });
        var off = await env.Http.SendAsync(Req("?delta=-1", Ok));
        Assert.Equal(HttpStatusCode.Conflict, off.StatusCode); // nobody would ever run it
        Assert.Contains("작업을 실행하지 않습니다", await off.Content.ReadAsStringAsync());
        var film = new HttpRequestMessage(HttpMethod.Post, $"/api/analyses/{env.Factory.Services.GetRequiredService<AnalysisStore>().All().Single().Id}/films") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        film.Headers.Add("Origin", Ok);
        Assert.Equal(HttpStatusCode.Conflict, (await env.Http.SendAsync(film)).StatusCode);
        env.Factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["Jobs:WorkerEnabled"] = "true"; // the worker loop stays off: the request is only recorded
        Assert.Equal(HttpStatusCode.BadRequest, (await env.Http.SendAsync(Req("?delta=-99", Ok))).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await env.Http.SendAsync(Req("?delta=-1", Ok))).StatusCode);
        var req = JsonDocument.Parse(File.ReadAllText(Path.Combine(env.Store.JobDir(id), "scenes-request.json"))).RootElement;
        Assert.Equal(-1, req.GetProperty("delta").GetInt32());
        Assert.Contains("pending", await env.Http.GetStringAsync($"/api/jobs/{id}/scenes?delta=-1"));
        File.WriteAllText(Path.Combine(env.Store.JobDir(id), "scenes.delta-1.json"), "[]");
        Assert.Equal("[]", await env.Http.GetStringAsync($"/api/jobs/{id}/scenes?delta=-1"));

        // a request file that cannot be replaced: a Korean 503, not a 500
        var reqFile = Path.Combine(env.Store.JobDir(id), "scenes-request.json");
        File.SetAttributes(reqFile, FileAttributes.ReadOnly);
        try
        {
            var busy = await env.Http.SendAsync(Req("?delta=-2", Ok));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
            Assert.Contains("저장하지 못했습니다", await busy.Content.ReadAsStringAsync());
        }
        finally { File.SetAttributes(reqFile, FileAttributes.Normal); }
    }
}
