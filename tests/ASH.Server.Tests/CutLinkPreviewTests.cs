using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ASH.Core.Film;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ASH.Server.Tests;

/// <summary>"＋ 컷" from the bottleneck map (a prefilled new cut in the cut editor) and the one-frame cut preview.</summary>
public class CutLinkPreviewTests
{
    const string Ok = "http://localhost";

    static async Task<(JobEnv Env, JobInfo Job, string Aid)> Done()
    {
        var env = new JobEnv();
        var j = await env.WaitFinished(await env.SendFiled("라인 증설", "A안"));
        // scenes.json comes from the run after the search; wait so it does not overwrite what a test writes
        await JobEnv.Until(() => File.Exists(Path.Combine(env.Store.JobDir(j.Id), "scenes.json")) ? "ok" : null);
        return (env, env.Store.Get(j.Id)!, env.Factory.Services.GetRequiredService<AnalysisStore>().All().Single().Id);
    }

    static JsonElement CutData(string page) =>
        JsonDocument.Parse(Regex.Match(page, "<script type=\"application/json\" id=\"cut-data\">(.*?)</script>").Groups[1].Value).RootElement;

    [Fact]
    public async Task Each_Ranked_Bottleneck_Links_To_A_Prefilled_New_Cut_That_Is_Not_Saved()
    {
        var (env, j, aid) = await Done();
        using var _ = env;
        File.WriteAllLines(Path.Combine(env.Store.JobDir(j.Id), "blocks.tsv"), new[] { "name\tutil\twait\tpasses", "B_x1\t0.5\t12\t10" });
        var html = await env.Http.GetStringAsync($"/analyses/{aid}?tab=conclusion");
        var href = $"/analyses/{aid}/cuts?job={j.Id}&amp;add=block%3AB_x1";
        Assert.Equal(2, Regex.Matches(html, Regex.Escape($"href=\"{href}\"")).Count); // the map marker and the ranked list
        Assert.Contains("aria-label=\"B_x1 컷 만들기\">＋ 컷</a>", html);

        var page = await env.Http.GetStringAsync(WebUtility.HtmlDecode(href));
        var pending = CutData(page).GetProperty("pending");
        Assert.Equal("B_x1 정체", pending.GetProperty("name").GetString());
        Assert.Equal("Hold", pending.GetProperty("kind").GetString());
        Assert.Contains("새 컷 · 병목 지도에서", page);
        Assert.Empty(env.Factory.Services.GetRequiredService<CutStore>().Get(aid)); // only the form is filled; "리스트에 추가" saves it

        var bad = CutData(await env.Http.GetStringAsync($"/analyses/{aid}/cuts?job={j.Id}&add=block:nope"));
        Assert.Equal(JsonValueKind.Null, bad.GetProperty("pending").ValueKind);
        Assert.Contains("'nope'", bad.GetProperty("addError").GetString());
    }

    [Fact]
    public async Task Block_Cut_Is_Centred_On_The_Block_At_The_Bottleneck_Candidate_Time()
    {
        var (env, j, _) = await Done();
        using var __ = env;
        var warm = JobView.For(env.Store, j).Input!.Settings.WarmupSec;
        // B_x1 is 3 × 3 m at (8,-1)..(11,2): the zone is 32 × 18 m (16:9) around its centre (9.5, 0.5)
        var c = CutEditorPage.BlockCut(env.Store, j, "B_x1", out var err)!;
        Assert.Null(err);
        Assert.Equal(new Rect(-6.5, -8.5, 32, 18), c.A);
        Assert.Equal((CutKind.Hold, warm, 600.0, 20.0, 30.0, 0.0), (c.Kind, c.StartSec, c.HoldSec, c.Speed, c.TiltDeg, c.DirDeg)); // no bottleneck candidate: warm-up end
        Assert.Null(CutStore.Validate(new[] { c }));

        // the scene finder's bottleneck candidate whose CP lies in the block gives the time window (one elsewhere does not)
        var scenes = new[]
        {
            new SceneCandidate(SceneKind.Bottleneck, "병목: X", 100, 700, 50, 50, 900, ""),
            new SceneCandidate(SceneKind.Bottleneck, "병목: L001", 3000.4, 3900, 10, 0.5, 600, ""),
        };
        File.WriteAllText(Path.Combine(env.Store.JobDir(j.Id), "scenes.json"), JsonSerializer.Serialize(scenes, CutStore.Json));
        c = CutEditorPage.BlockCut(env.Store, j, "B_x1", out _)!;
        Assert.Equal((3000.0, 900.0, 30.0), (c.StartSec, c.HoldSec, c.Speed));
        Assert.Null(CutEditorPage.BlockCut(env.Store, j, "B_none", out err));
        Assert.Contains("B_none", err);
    }

    static HttpRequestMessage PreviewPost(string aid, string jobId, CutSpec c, string? origin = Ok)
    {
        var m = new HttpRequestMessage(HttpMethod.Post, $"/api/analyses/{aid}/cuts/{c.Id}/preview?job={jobId}") { Content = new StringContent(JsonSerializer.Serialize(c, CutStore.Json), Encoding.UTF8, "application/json") };
        if (origin != null) m.Headers.Add("Origin", origin);
        return m;
    }

    static CutSpec Hold(string id, double start = 7200, double roll = 0) => new(id, "입구", CutKind.Hold, start, 600, new Rect(1, 2, 30, 18), null, 0, 0, 0, 10, 30, 0, true, true, 0, roll);

    [Fact]
    public async Task Preview_Says_Clearly_When_AutoView_Is_Missing()
    {
        var (env, j, aid) = await Done();
        using var _ = env;
        env.Factory.Services.GetRequiredService<IConfiguration>()["Film:AviewExe"] = Path.Combine(env.Dir, "no", "aview64.exe");
        env.Factory.Services.GetRequiredService<CutStore>().Save(aid, new[] { Hold("c1") });
        var r = await env.Http.SendAsync(PreviewPost(aid, j.Id, Hold("c1")));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.StartsWith(Routes.NoAutoView, await r.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await env.Http.GetAsync($"/api/analyses/{aid}/cuts/c1/preview")).StatusCode); // nothing queued
    }

    [Fact]
    public async Task Preview_Is_Queued_Made_Between_Jobs_And_Reuses_Its_Model_For_Other_Angles()
    {
        var (env, j, aid) = await Done();
        using var _ = env;
        var aview = Path.Combine(env.Dir, "aview64.exe"); File.WriteAllText(aview, "x");
        env.Factory.Services.GetRequiredService<IConfiguration>()["Film:AviewExe"] = aview;
        env.Factory.Services.GetRequiredService<CutStore>().Save(aid, new[] { Hold("c1") });
        Assert.Contains("id=\"pvBtn\"", await env.Http.GetStringAsync($"/analyses/{aid}/cuts"));

        // checked before anything is queued
        Assert.Equal(HttpStatusCode.Forbidden, (await env.Http.SendAsync(PreviewPost(aid, j.Id, Hold("c1"), null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await env.Http.SendAsync(PreviewPost(aid, j.Id, Hold("unsaved")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await env.Http.SendAsync(PreviewPost(aid, "20260101-000000-zzzz", Hold("c1")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await env.Http.SendAsync(PreviewPost(aid, j.Id, Hold("c1") with { TiltDeg = 99 }))).StatusCode);

        async Task<JsonElement> Preview(CutSpec c)
        {
            var r = await env.Http.SendAsync(PreviewPost(aid, j.Id, c));
            Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
            Assert.Equal("queued", JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("state").GetString());
            env.Store.Wake();
            for (var sw = System.Diagnostics.Stopwatch.StartNew(); sw.Elapsed < TimeSpan.FromSeconds(60); await Task.Delay(100))
            {
                var e = JsonDocument.Parse(await env.Http.GetStringAsync($"/api/analyses/{aid}/cuts/c1/preview")).RootElement;
                if (e.GetProperty("state").GetString() is not ("done" or "failed")) continue;
                Assert.True(e.GetProperty("state").GetString() == "done", e.GetProperty("error").ToString());
                return e;
            }
            throw new TimeoutException("미리보기가 끝나지 않았습니다");
        }
        int PreviewBuilds() { lock (env.Film.Builds) return env.Film.Builds.Count(b => b.Dir.Contains(Path.DirectorySeparatorChar + "previews" + Path.DirectorySeparatorChar)); }

        var s1 = await Preview(Hold("c1", roll: 30));
        Assert.Equal(7201, s1.GetProperty("atSec").GetDouble()); // the cut's first second
        Assert.Equal(30, s1.GetProperty("cut").GetProperty("rollDeg").GetDouble()); // the cut as the form had it, for the page's stale check
        string last; lock (env.Film.Scripts) last = env.Film.Scripts.Last();
        Assert.Contains("\ntime 7201\n", last); Assert.Contains("\ntime 7202\n", last); Assert.Contains("width 1280", last);
        var img = await env.Http.GetAsync(s1.GetProperty("image").GetString());
        Assert.Equal(HttpStatusCode.OK, img.StatusCode); Assert.Equal("image/jpeg", img.Content.Headers.ContentType!.MediaType);
        Assert.Equal(1, PreviewBuilds());

        await Preview(Hold("c1", roll: 0)); // same time: only AutoView again
        Assert.Equal(1, PreviewBuilds());
        await Preview(Hold("c1", start: 9000)); // another time: a new film model
        Assert.Equal(2, PreviewBuilds());
        Assert.Null(JsonDocument.Parse(await env.Http.GetStringAsync("/api/jobs/queue")).RootElement.GetProperty("busy").GetString());
    }

    // the preview on this PC's AutoMod and AutoView (ASH_AM_HOME; ffmpeg on PATH): one cut at roll 0 and 30. ASH_PREVIEW_OUT=dir copies the two frames there.
    [SkippableFact]
    public async Task Real_AutoView_Preview_At_Roll_0_And_30()
    {
        var am = Environment.GetEnvironmentVariable("ASH_AM_HOME");
        Skip.If(am is null, "ASH_AM_HOME not set");
        using var env = new JobEnv(amHome: am);
        var j = await env.WaitFinished(await env.SendFiled("미리보기", "A안", addFiles: FilmTests.Drawing), 1800);
        Assert.True(j.State == JobState.Done, j.Error + "\n" + File.ReadAllText(env.Store.LogPath(j.Id)));
        var aid = env.Factory.Services.GetRequiredService<AnalysisStore>().All().Single().Id;
        var warm = JobView.For(env.Store, j).Input!.Settings.WarmupSec;
        var cut = new CutSpec("roll", "회전 확인", CutKind.Hold, warm + 600, 60, new Rect(-3, -3, 17, 16), null, 0, 0, 0, 10, 30, 0, true, true);
        env.Factory.Services.GetRequiredService<CutStore>().Save(aid, new[] { cut });
        var fw = env.Factory.Services.GetRequiredService<FilmWorker>();
        foreach (var roll in new[] { 0, 30 })
        {
            var r = await env.Http.SendAsync(PreviewPost(aid, j.Id, cut with { RollDeg = roll }));
            Assert.True(r.StatusCode == HttpStatusCode.Accepted, await r.Content.ReadAsStringAsync());
            var p = await JobEnv.Until(() => fw.Previews.Get(aid, "roll") is { State: "done" or "failed" } x ? x : null, 1800);
            Assert.True(p.State == "done", p.Error);
            if (Environment.GetEnvironmentVariable("ASH_PREVIEW_OUT") is { Length: > 0 } outDir)
            {
                Directory.CreateDirectory(outDir);
                File.Copy(fw.Previews.ImagePath(aid, "roll"), Path.Combine(outDir, $"roll{roll}.jpg"), true);
            }
        }
    }
}
