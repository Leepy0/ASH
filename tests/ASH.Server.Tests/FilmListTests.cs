using System.Text.Json;
using ASH.Core.Film;
using Microsoft.Extensions.DependencyInjection;

namespace ASH.Server.Tests;

public class FilmListTests
{
    static FilmInfo Make(string analysisId, string name, string jobId, string state)
    {
        var id = JobStore.NewId();
        var done = state == JobState.Done;
        return new FilmInfo
        {
            Request = new FilmRequest(id, analysisId, new[] { "c1" }, new[] { jobId }, 1280, 720, 15, "joined", true, null, 0, null),
            AnalysisName = name, State = state, CreatedUtc = DateTime.UtcNow, StartedUtc = done ? null : DateTime.UtcNow, FinishedUtc = done ? DateTime.UtcNow : null,
            Cuts = { new CutSpec("c1", "입구", CutKind.Hold, 7200, 600, new Rect(1, 2, 30, 18), null, 0, 0, 0, 10, 30, 0, true, true) }, // 600 s at 10x = 1:00
            Items = { new FilmItem { JobId = jobId, CutId = "c1", Label = "A안 v1 · 입구", Name = "part.mp4", Ok = done } },
            Joined = "joined.mp4", JoinedOk = done, Done = done ? 1 : 0, Phase = done ? null : "촬영",
        };
    }

    [Fact]
    public async Task Lists_Running_And_Finished_Films_With_Mine_Filter()
    {
        using var env = new JobEnv(worker: false);
        var mineJob = await env.SendFiled("내 분석", "A안");
        var otherJob = await env.SendFiled("남의 분석", "A안", submitter: "other");
        env.Store.Update(otherJob, j => j.Ip = "10.9.9.9"); // not the test client's IP
        var fs = env.Factory.Services.GetRequiredService<FilmStore>();
        var finished = Make(env.Store.Get(mineJob)!.AnalysisId!, "내 분석", mineJob, JobState.Done);
        var running = Make(env.Store.Get(otherJob)!.AnalysisId!, "남의 분석", otherJob, JobState.Running);
        fs.Add(finished); fs.Add(running);
        File.WriteAllBytes(Path.Combine(fs.FilmDir(finished.Id), "thumb.jpg"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(fs.FilmDir(finished.Id), "joined.mp4"), new byte[] { 1 });

        var all = await env.Http.GetStringAsync("/films?me=0");
        foreach (var s in new[] { "만드는 중", "완료", "내 분석", "남의 분석", $"/films/{finished.Id}/files/thumb.jpg", $"/films/{finished.Id}/files/joined.mp4", $"href=\"/films/{finished.Id}\"",
                     $"data-bar=\"film.{running.Id}.bar\"", "fetch('/api/films?me=0", "컷 1개 · 1:00", "href=\"/films\" class=\" on\"" })
            Assert.Contains(s, all);

        var mine = await env.Http.GetStringAsync("/films"); // mine by default
        Assert.Contains("내 분석", mine); Assert.DoesNotContain("남의 분석", mine);
        Assert.Contains("class=\"on\" aria-current=\"true\">내 것만", mine);
        Assert.Contains("지금 만드는 영상이 없습니다", mine); Assert.DoesNotContain("fetch('/api/films", mine); // nothing to poll

        var api = JsonDocument.Parse(await env.Http.GetStringAsync("/api/films?me=0")).RootElement;
        Assert.True(api.GetProperty("active").GetBoolean());
        Assert.Equal("running", api.GetProperty("watch").GetProperty(running.Id).GetString());
        Assert.Equal("컷 0 / 1", api.GetProperty("fields").GetProperty($"film.{running.Id}.done").GetString());
    }

    [Fact]
    public async Task Empty_List_Shows_Everything_And_Empty_States()
    {
        using var env = new JobEnv(worker: false);
        var html = await env.Http.GetStringAsync("/films?me=1"); // nothing mine: shows everything
        Assert.Contains("class=\"on\" aria-current=\"true\">전체", html);
        Assert.Contains("아직 다 만든 영상이 없습니다", html);
        Assert.DoesNotContain("https://", html);
    }
}
