using System.Diagnostics;
using ASH.Core.Film;
using ASH.Server;
using Xunit;

namespace ASH.Server.Tests;

public class FilmHelperTests : IDisposable
{
    readonly string _jobs = Path.Combine(Path.GetTempPath(), "ash-helper-test-" + Guid.NewGuid().ToString("N"));
    readonly CancellationTokenSource _stop = new();

    readonly List<Task> _hosts = new();

    // the hosts write heartbeat.txt until they see the stop: wait for them before removing the folder
    public void Dispose()
    {
        _stop.Cancel();
        Task.WaitAll(_hosts.ToArray(), TimeSpan.FromSeconds(40));
        try { Directory.Delete(_jobs, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    FilmHelper.Client Client(TimeSpan? startWait = null) => new(FilmHelper.DirOf(_jobs), null, startWait ?? TimeSpan.FromSeconds(30)); // generous: under a full parallel test run the host task may start late

    Task Host(Func<FilmHelper.Request, CancellationToken, RenderResult> render)
    {
        var t = Task.Run(() => FilmHelper.RunHost(_jobs, render, _stop.Token));
        _hosts.Add(t);
        return t;
    }

    [Fact]
    public void No_Helper_Error_Carries_The_Diagnosis()
    {
        var dir = FilmHelper.DirOf(_jobs);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "helper.log"), "[2026-10-28 10:00:00.000] 시작: 세션 1\n");
        var why = FilmHelper.Diagnose(dir); // this PC has no ASH.FilmHelper task (or one of another test run): it must still answer, never throw
        Assert.Contains("신호 파일 없음", why);
        Assert.Contains("기록: [2026-10-28 10:00:00.000] 시작: 세션 1", why);
        var res = new FilmHelper.Client(dir, null, TimeSpan.FromMilliseconds(200), () => "진단 내용").Render(Path.Combine(_jobs, "w"), "agv", "x", TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.False(res.Ok);
        Assert.StartsWith(FilmHelper.NotRunning, res.Error);
        Assert.EndsWith("[진단] 진단 내용", res.Error);
    }

    [Fact]
    public async Task A_Render_Goes_To_The_Helper_And_Its_Result_Comes_Back()
    {
        var work = Path.Combine(_jobs, "films", "f1", "job1");
        FilmHelper.Request? seen = null;
        var host = Host((r, ct) => { seen = r; return new RenderResult(true, Path.Combine(r.WorkDir, "cal_on.mp4"), null, new[] { "Attention!" }); });
        var res = Client().Render(work, "agv", "camera description", TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.True(res.Ok, res.Error);
        Assert.Equal(Path.Combine(work, "cal_on.mp4"), res.Mp4);
        Assert.Equal(new[] { "Attention!" }, res.Dialogs);
        Assert.Equal("agv", seen!.Model); Assert.Equal(60, seen.TimeoutSec);
        Assert.Empty(Directory.GetFiles(FilmHelper.DirOf(_jobs), "re*-*")); // request and result are cleaned up
        _stop.Cancel(); await host;
    }

    [Fact]
    public void A_Work_Folder_Outside_Jobs_Is_Refused_Without_Rendering()
    {
        var rendered = false;
        _ = Host((r, ct) => { rendered = true; return new RenderResult(true, "x", null, Array.Empty<string>()); });
        var res = Client().Render(Path.GetTempPath(), "agv", "s", TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.False(res.Ok); Assert.Contains("Jobs:Dir 밖", res.Error); Assert.False(rendered);
    }

    [Fact]
    public void Cancel_Reaches_The_Running_Render()
    {
        _ = Host((r, ct) => { ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(30)); return new RenderResult(false, null, ct.IsCancellationRequested ? "취소되었습니다." : "늦음", Array.Empty<string>()); });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var sw = Stopwatch.StartNew();
        var res = Client().Render(Path.Combine(_jobs, "w"), "agv", "s", TimeSpan.FromMinutes(1), cts.Token);
        Assert.False(res.Ok); Assert.Equal("취소되었습니다.", res.Error);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), sw.Elapsed.ToString());
    }

    [Fact]
    public void No_Helper_Fails_With_The_Setup_Hint_And_The_Next_Render_Fails_At_Once()
    {
        var c = Client(TimeSpan.FromSeconds(1));
        var res = c.Render(Path.Combine(_jobs, "w"), "agv", "s", TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.False(res.Ok); Assert.False(res.TimedOut); Assert.Contains(FilmHelper.InstallArg, res.Error);
        var sw = Stopwatch.StartNew();
        Assert.Equal(res.Error, c.Render(Path.Combine(_jobs, "w"), "agv", "s", TimeSpan.FromMinutes(1), CancellationToken.None).Error);
        Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task The_Helper_Ends_When_The_Program_Was_Replaced()
    {
        var changed = false;
        var host = Task.Run(() => FilmHelper.RunHost(_jobs, (r, ct) => new RenderResult(true, "x", null, Array.Empty<string>()), _stop.Token, () => changed));
        await Task.Delay(300);
        Assert.True(FilmHelper.Alive(FilmHelper.DirOf(_jobs), DateTime.UtcNow));
        changed = true;
        Assert.True(host.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(FilmHelper.Alive(FilmHelper.DirOf(_jobs), DateTime.UtcNow));
    }
}
