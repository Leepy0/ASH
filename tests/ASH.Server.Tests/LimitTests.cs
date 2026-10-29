using System.Net;
using ASH.Core.Model;
using ASH.Core.Runner;
using ASH.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ASH.Server.Tests;

public class LimitTests
{
    /// <summary>A finished filed job (G1 decided at 2), its analysis and the first material's takt as the input has it.</summary>
    static async Task<(JobInfo J, string Aid, double Takt)> Done(JobEnv env)
    {
        var j = await env.WaitFinished(await env.SendFiled("생산 증설", "A안"));
        var input = SearchJob.ReadInput(env.Store.ProjectDir(j.Id), TextWriter.Null, new List<string>(), out _)!;
        return (j, j.AnalysisId!, input.Materials[0].TaktTime);
    }

    /// <summary>The fake's result with no stop and full production (a pass), else its default one (45 s stopped: 자재 품절).</summary>
    static string Result(RunRequest r, bool pass)
    {
        var tsv = FakeResult.Tsv(r.Input.Materials.Select(m => m.Material));
        return pass ? tsv.Replace("\t15\t45\t12\t", "\t15\t0\t15\t") : tsv;
    }

    static List<RunRequest> LimitRuns(JobEnv env)
    {
        lock (env.Film.Runs) return env.Film.Runs.Where(r => r.RunsDir?.Contains(Path.Combine("limit", "p")) == true).ToList();
    }

    static Task<HttpResponseMessage> Post(JobEnv env, string url, string back) =>
        env.Http.PostAsync(url, new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("back", back) }));

    [Fact]
    public async Task Steps_Run_In_Order_Stop_After_Two_Failures_And_The_Result_Is_Kept_And_Shown()
    {
        using var env = new JobEnv();
        var (j, aid, takt) = await Done(env);
        // passes up to +20 % (takt / 1.2), stops out from +30 % on
        env.Film.Result = r => Result(r, r.Input.Materials[0].TaktTime >= takt / 1.25);
        var back = $"/analyses/{aid}?tab=conclusion&job={j.Id}";
        var before = await env.Http.GetStringAsync(back);
        Assert.Contains("<h3>생산량 한계 (T/T)</h3>", before);
        Assert.Contains($"action=\"/jobs/{j.Id}/limit\"", before);

        var page = await (await Post(env, $"/jobs/{j.Id}/limit", back)).Content.ReadAsStringAsync();
        Assert.Contains("<h3>생산량 한계 (T/T)</h3>", page); // back on the analysis page
        var info = await JobEnv.Until(() => LimitWorker.Read(env.Store, j.Id) is { Done: true } x ? x : null);

        Assert.Null(info.Error);
        Assert.Equal(new[] { 0, 10, 20, 30, 40 }, info.Steps.Select(s => s.Pct)); // +50 % never ran: two failures
        Assert.Equal(new[] { true, true, true, false, false }, info.Steps.Select(s => s.Ok));
        Assert.Equal(20, info.Passed);
        var mat = SearchJob.ReadInput(env.Store.ProjectDir(j.Id), TextWriter.Null, new List<string>(), out _)!.Materials[0].Material;
        Assert.Equal(("자재 품절", $"{mat} 45초"), (info.FirstFail!.Rule, info.FirstFail.Worst));
        Assert.Null(LimitWorker.Progress(env.Store, j.Id)); // the request is gone

        // in step order, each step at the decided counts with the settings' seeds and its own takt
        var runs = LimitRuns(env);
        var reps = ProductionLimit.Replications(runs[0].Input.Settings);
        Assert.Equal(5 * reps, runs.Count);
        Assert.Equal(new[] { "p0", "p10", "p20", "p30", "p40" }, runs.Select(r => Path.GetFileName(r.RunsDir!)).Distinct());
        Assert.All(runs, r => Assert.Equal(2, r.Scenario.ActiveByGroup["G1"]));
        Assert.All(runs, r => Assert.Equal(takt / (1 + int.Parse(Path.GetFileName(r.RunsDir!)[1..]) / 100.0), r.Input.Materials[0].TaktTime, 9));

        // kept in the job folder: a new store (a restarted server) reads it back
        Assert.Equal(20, LimitWorker.Read(new JobStore(env.Store.Dir, NullLogger.Instance), j.Id)!.Passed);
        page = await env.Http.GetStringAsync(back);
        Assert.Contains("<b>+20%까지 통과</b>", page);
        Assert.Contains($"+30% 자재 품절 · {mat} 45초", page);
        Assert.Contains(">다시 계산</button>", page);
        Assert.Contains("+20%까지 통과", await env.Http.GetStringAsync($"/jobs/{j.Id}"));
    }

    [Fact]
    public async Task Cancel_Stops_The_Running_Step_And_Keeps_What_Finished()
    {
        using var env = new JobEnv();
        var (j, aid, takt) = await Done(env);
        using var gate = new ManualResetEventSlim(false);
        // +0 % passes at once; +10 % waits at the gate
        env.Film.Result = r => { if (r.Input.Materials[0].TaktTime < takt) gate.Wait(TimeSpan.FromSeconds(30)); return Result(r, true); };
        var back = $"/analyses/{aid}?tab=conclusion&job={j.Id}";
        await Post(env, $"/jobs/{j.Id}/limit", back);
        await JobEnv.Until(() => LimitRuns(env).Any(r => r.RunsDir!.EndsWith("p10")) ? "" : null);
        Assert.Contains("+10% · ", LimitWorker.Progress(env.Store, j.Id));
        Assert.Contains($"action=\"/jobs/{j.Id}/limit/cancel\"", await env.Http.GetStringAsync(back));

        var res = await Post(env, $"/jobs/{j.Id}/limit/cancel", back);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        gate.Set();
        var fw = env.Factory.Services.GetRequiredService<FilmWorker>();
        await JobEnv.Until(() => fw.Busy == null ? "" : null);
        await Task.Delay(300); // a further step would have started by now

        Assert.Null(LimitWorker.Progress(env.Store, j.Id));
        var info = LimitWorker.Read(env.Store, j.Id)!;
        Assert.False(info.Done);
        Assert.Equal(new[] { 0 }, info.Steps.Select(s => s.Pct)); // the cut-short +10 % is not a result
        Assert.DoesNotContain(LimitRuns(env), r => r.RunsDir!.EndsWith("p20"));
        var page = await env.Http.GetStringAsync(back);
        Assert.Contains(">중단</span>", page);
        Assert.Contains("<b>+0%까지 통과</b>", page);
    }

    [Fact]
    public async Task A_Cycle_Group_Runs_The_Model_Of_Its_Decided_Count()
    {
        using var env = new JobEnv();
        var j = await env.WaitFinished(await env.SendFiled("생산 증설", "A안", addFiles: JobEnv.CycleG1));
        env.Film.Result = r => Result(r, false); // +0 % and +10 % fail: two steps
        await Post(env, $"/jobs/{j.Id}/limit", $"/analyses/{j.AnalysisId}?tab=conclusion&job={j.Id}");
        var info = await JobEnv.Until(() => LimitWorker.Read(env.Store, j.Id) is { Done: true } x ? x : null);
        Assert.Null(info.Error);
        Assert.Equal(new[] { 0, 10 }, info.Steps.Select(s => s.Pct));
        var runs = LimitRuns(env);
        Assert.NotEmpty(runs);
        Assert.All(runs, r => Assert.Equal("m_c2", r.ModelName)); // G1 decided at 2: never the base model with an idle third vehicle
        Assert.Contains("numveh 2", File.ReadAllText(Path.Combine(env.Store.ProjectDir(j.Id), "m_c2.arc", "agv.asy")));
    }

    [Fact]
    public void A_Follow_Failure_Is_Named_After_Deadlock_With_The_Station_Followed_Least()
    {
        Assert.Equal("뒤차 대기", LimitWorker.RuleName(RuleCode.Follow));
        Assert.Equal(new[] { RuleCode.Deadlock, RuleCode.Follow, RuleCode.Utilization },
            new[] { RuleCode.Utilization, RuleCode.Follow, RuleCode.Deadlock }.OrderBy(LimitWorker.Order));
        static MaterialResult M(string station, double swaps, double follow) => new("M001@" + station, "G2", 1, 2, 3, 4, 0, 4, SwapN: swaps, FollowN: follow);
        static ResultSet Rs(params MaterialResult[] m) => new(m, new[] { new GroupResult("G2", 2) }, new BlockResult[0], 0, 3600, new ResultRow[0]);
        // over two seeds: U001 18/20, U002 14/20 = 70 %, U003 no swaps (left out)
        var sets = new List<ResultSet> { Rs(M("U001", 10, 9), M("U002", 10, 6), M("U003", 0, 0)), Rs(M("U001", 10, 9), M("U002", 10, 8), M("U003", 0, 0)) };
        Assert.Equal("M001@U002 70%", LimitWorker.Worst(RuleCode.Follow, new List<string> { "G2" }, sets));
        Assert.Equal("", LimitWorker.Worst(RuleCode.Follow, new List<string> { "G2" }, new List<ResultSet> { Rs(M("U001", double.NaN, double.NaN)) }));
    }
}
