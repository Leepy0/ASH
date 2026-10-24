using System.Text.Json;
using ASH.Core.Film;
using Microsoft.Extensions.DependencyInjection;

namespace ASH.Server.Tests;

public class NotifyTests
{
    static FilmInfo Film(string analysisId, string state, DateTime end)
    {
        var id = JobStore.NewId();
        return new FilmInfo
        {
            Request = new FilmRequest(id, analysisId, new[] { "c1" }, Array.Empty<string>(), 1280, 720, 15, "joined", true, null, 0, null),
            AnalysisName = "내 분석", State = state, CreatedUtc = end.AddMinutes(-5), FinishedUtc = end, Done = 2, Error = state == JobState.Failed ? "렌더 실패" : null,
        };
    }

    static async Task<JsonElement> Events(HttpClient http, DateTime? since) =>
        JsonDocument.Parse(await http.GetStringAsync("/api/me/events" + (since is { } s ? "?since=" + s.ToString("o") : ""))).RootElement;

    [Fact]
    public async Task Returns_Only_My_Ended_Jobs_And_Films_Since()
    {
        using var env = new JobEnv(worker: false);
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        var done = await env.SendFiled("내 분석", "A안");
        var failed = await env.SendFiled("내 분석", "B안");
        var old = await env.SendFiled("내 분석", "C안");
        var running = await env.SendFiled("내 분석", "D안");
        var other = await env.SendFiled("남의 분석", "A안", submitter: "other");
        env.Store.Update(done, j => { j.State = JobState.Done; j.FinishedUtc = DateTime.UtcNow.AddMinutes(-1); j.Decided = new() { ["g"] = 7 }; });
        env.Store.Update(failed, j => { j.State = JobState.Failed; j.FinishedUtc = DateTime.UtcNow.AddMinutes(-2); j.Error = "교착 발생"; });
        env.Store.Update(old, j => { j.State = JobState.Done; j.FinishedUtc = t0.AddMinutes(-5); });
        env.Store.Update(running, j => j.State = JobState.Running);
        env.Store.Update(other, j => { j.State = JobState.Done; j.FinishedUtc = DateTime.UtcNow.AddMinutes(-1); j.Ip = "10.9.9.9"; });
        var fs = env.Factory.Services.GetRequiredService<FilmStore>();
        var mineFilm = Film(env.Store.Get(done)!.AnalysisId!, JobState.Done, DateTime.UtcNow.AddSeconds(-30));
        var otherFilm = Film(env.Store.Get(other)!.AnalysisId!, JobState.Done, DateTime.UtcNow.AddSeconds(-30));
        fs.Add(mineFilm); fs.Add(otherFilm);

        var d = await Events(env.Http, t0);
        var ev = d.GetProperty("events").EnumerateArray().ToList();
        Assert.Equal(new[] { failed, done, mineFilm.Id }, ev.Select(e => e.GetProperty("id").GetString()).ToArray()); // oldest first; no old, running, or other's
        Assert.Equal("⚠ 실패: 내 분석 › B안 v1", ev[0].GetProperty("title").GetString());
        Assert.Equal("교착 발생", ev[0].GetProperty("body").GetString());
        Assert.Equal("✓ 완료: 내 분석 › A안 v1", ev[1].GetProperty("title").GetString());
        Assert.Equal("권장 7대", ev[1].GetProperty("body").GetString());
        Assert.Equal("/jobs/" + done, ev[1].GetProperty("href").GetString());
        Assert.Equal("/films/" + mineFilm.Id, ev[2].GetProperty("href").GetString());
        Assert.Equal("film", ev[2].GetProperty("kind").GetString());

        var now = d.GetProperty("now").GetDateTime();
        Assert.Empty((await Events(env.Http, now)).GetProperty("events").EnumerateArray()); // nothing new after the returned time
    }

    [Fact]
    public async Task No_Since_Returns_Only_Now()
    {
        using var env = new JobEnv(worker: false);
        var id = await env.SendFiled("내 분석", "A안");
        env.Store.Update(id, j => { j.State = JobState.Done; j.FinishedUtc = DateTime.UtcNow; });
        var d = await Events(env.Http, null);
        Assert.Empty(d.GetProperty("events").EnumerateArray());
        Assert.True(d.TryGetProperty("now", out _));
    }

    [Fact]
    public void Page_Has_Notifier_And_Bell_But_No_Call_At_Load()
    {
        var html = JobPages.Page("t", "<p>b</p>", nav: "home");
        Assert.Contains("id=\"bell\"", html); Assert.Contains("/api/me/events", html); Assert.Contains("new Notification(", html);
        Assert.Contains("role=\"status\"", html); Assert.Contains("isSecureContext", html);
        Assert.True(html.IndexOf("id=\"bell\"", StringComparison.Ordinal) < html.IndexOf("href=\"/admin/settings\"", StringComparison.Ordinal)); // above 관리
        var script = html[html.IndexOf("<script>", StringComparison.Ordinal)..];
        // requestPermission only inside the click handler, never at the top level of the script
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(script, "requestPermission").Count);
        Assert.True(script.IndexOf("bell.addEventListener('click'", StringComparison.Ordinal) < script.IndexOf("requestPermission", StringComparison.Ordinal));
    }
}
