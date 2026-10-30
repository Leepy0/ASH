using System.Globalization;
using System.Text.Json;
using ASH.Core.Runner;
using ASH.Server;
using ClosedXML.Excel;

namespace ASH.Server.Tests;

/// <summary>The group table's supply columns, the 상세 tab's 자재별 table (old results without the supply columns show "-"), and a run over the
/// utilization limit only shown as a warning, not a failure.</summary>
public class ResultColumnsTests
{
    static string Id(string json) => JsonDocument.Parse(json).RootElement.GetProperty("id").GetString()!;

    static async Task<(JobEnv Env, JobInfo J)> Finished()
    {
        var env = new JobEnv();
        var id = Id(await (await env.Send(JobEnv.MakePackage(env.Dir))).Content.ReadAsStringAsync());
        return (env, await env.WaitFinished(id));
    }

    [Fact]
    public async Task Group_Table_Sums_Supplies_And_Distance_And_Old_Results_Show_Dashes_For_The_Material_Supply_Columns()
    {
        var (env, j) = await Finished(); using var _ = env;
        var summary = await env.Http.GetStringAsync("/jobs/" + j.Id);
        Assert.Contains("<table class=\"sort gt\">", summary); // sized to its content, cells centered
        Assert.Contains(".gt th,.gt td{text-align:center}", summary);
        foreach (var h in new[] { "공급 횟수(회)", "총 이동 거리(km)", "공급당 이동 거리(m)" }) Assert.Contains($"<button type=\"button\">{h}</button>", summary);
        // FakeResult: TAB1 20 jobs 1200 m + TAB2 10 jobs 800 m (TAB3 is not active) = 30 jobs, 2.0 km, 66.7 m per job
        Assert.Contains("<td>30</td><td>2.0</td><td>66.7</td><td class=\"ok\">✓ 통과</td>", summary);

        // FakeResult writes 0 in value7/value8 of the material rows, as models built before the supply columns do
        var v = JobView.For(env.Store, j);
        var m = v.Materials[0];
        Assert.True(double.IsNaN(m.Trips) && double.IsNaN(m.Distance));
        var per = v.Input!.Materials.Single(x => x.Material == m.Name).UnitsPerContainer;
        string P(double d) => (d * per).ToString("0", CultureInfo.InvariantCulture);
        var detail = await env.Http.GetStringAsync($"/jobs/{j.Id}?tab=detail");
        Assert.Contains("<h2>자재별</h2>", detail);
        Assert.Contains($"<tr><td>{m.Name}</td><td>G1</td><td class=\"num\">-</td><td class=\"num\">-</td><td class=\"num\">-</td><td class=\"num\">100.5</td><td class=\"num\">200.0</td>" +
            $"<td class=\"num\">3 / 1 / 6</td><td class=\"num\">{P(3)} / {P(1)} / {P(6)}</td></tr>", detail);
    }

    [Fact]
    public async Task Material_Supply_Columns_Come_From_Value7_And_Value8()
    {
        var (env, j) = await Finished(); using var _ = env;
        var tsv = Path.Combine(env.Store.ProjectDir(j.Id), "runs", "n2_s1", "result.tsv");
        // the model's row: ... stop, consumed, distance of the supply jobs (m), their count
        File.WriteAllLines(tsv, File.ReadAllLines(tsv).Select(l => l.StartsWith("material\t") ? string.Join("\t", l.Split('\t')[..^2]) + "\t3000\t15" : l));
        var v = JobView.For(env.Store, j); // first read of this job: nothing cached yet
        var m = v.Materials[0];
        Assert.Equal(15, m.Trips); Assert.Equal(3000, m.Distance); Assert.Equal(200, m.DistPerTrip);
        var window = v.Input!.Settings.SimLengthSec - v.Input.Settings.WarmupSec;
        var detail = await env.Http.GetStringAsync($"/jobs/{j.Id}?tab=detail");
        Assert.Contains($"<tr><td>{m.Name}</td><td>G1</td><td class=\"num\">15</td><td class=\"num\">{(window / 15).ToString("0", CultureInfo.InvariantCulture)}</td><td class=\"num\">200</td>", detail);

        // stats.tsv (kept after the run folders are purged) round-trips the new columns; an older stats.tsv without them reads as no data
        var lines = JobView.StatsLines(v);
        Assert.EndsWith("\t15\t3000", lines.First(l => l.StartsWith("material\t")));
    }

    [Fact]
    public async Task Over_The_Utilization_Limit_Only_Is_A_Warning_And_Real_Failures_Stay_Red()
    {
        var (env, j) = await Finished(); using var _ = env;
        var sum = Path.Combine(env.Store.ProjectDir(j.Id), "runs", "summary.tsv");
        File.Delete(sum);
        SummaryTsv.Append(sum, new SummaryRow("n3_s1", "G1", 3, 1, false, "교착 1건", 0.4, 2, 10, 10, 0, 1, 5, "G1=3"));
        SummaryTsv.Append(sum, new SummaryRow("n1_s1", "G1", 1, 1, false, "가동률 초과: 95% > 75%", 0.95, 2, 10, 10, 0, 0, 5, "G1=1"));
        SummaryTsv.Append(sum, new SummaryRow("n2_s1", "G1", 2, 1, false, "가동률 초과: 80% > 75%", 0.8, 2, 10, 10, 0, 0, 5, "G1=2")); // the confirm run
        var v = JobView.For(env.Store, j);
        var g = Assert.Single(v.Groups);
        Assert.True(g.AllOk); Assert.True(g.UtilOver); Assert.Null(g.Reason);
        Assert.Equal(0, v.Curves["G1"].Single(p => p.Active == 1).Failed); // a warning is no failed point on the curve
        Assert.Equal(1, v.Curves["G1"].Single(p => p.Active == 3).Failed);

        var summary = await env.Http.GetStringAsync("/jobs/" + j.Id);
        Assert.Contains("<span class=\"tag orange\"", summary); Assert.Contains("⚠ 경고: 가동률 초과</span>", summary);
        Assert.Contains("<td class=\"wn\">⚠ 경고: 가동률 초과</td>", summary);
        Assert.Contains("<tr class=\"ow\">", summary);
        Assert.Contains("<div class=\"kpi wn\"", summary);
        Assert.DoesNotContain("확인 실행 실패", summary); Assert.DoesNotContain("▲ 실패", summary);
        var detail = await env.Http.GetStringAsync($"/jobs/{j.Id}?tab=detail");
        Assert.Contains("<td class=\"wn\">⚠ 경고: 가동률 초과: 95% &gt; 75%</td>", detail);
        Assert.Contains("<td class=\"bad\">교착 1건</td>", detail); // a deadlock stays a red failure

        var path = Path.Combine(Path.GetTempPath(), "ash-report-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            JobReport.Write(j, v, path);
            using var wb = new XLWorkbook(path);
            var s = wb.Worksheet("요약");
            Assert.Equal("경고: 가동률 초과", s.Cell(s.Search("확인 판정").First().Address.RowNumber, 2).GetString());
            Assert.Equal("경고: 가동률 초과", s.Cell(s.Search("그룹별 권장 대수").First().Address.RowNumber + 2, 8).GetString());
            Assert.Contains(wb.Worksheet("실행 목록").CellsUsed(), c => c.GetString() == "경고");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Util_Only_Needs_The_Utilization_Reason_And_No_Deadlock()
    {
        SummaryRow R(bool ok, string reason, int dead = 0) => new("r", "G1", 1, 1, ok, reason, 0.9, 1, 1, 1, 0, dead, 1);
        Assert.True(JobView.UtilOnly(R(false, Judge.UtilPrefix + ": 90% > 75%")));
        Assert.False(JobView.UtilOnly(R(true, "")));
        Assert.False(JobView.UtilOnly(R(false, "재고 부족(공정 정지 10초)")));
        Assert.False(JobView.UtilOnly(R(false, Judge.UtilPrefix + ": 90% > 75%", dead: 1))); // older summaries put utilization before the deadlock rule
    }

    static readonly string[] WaitHeads = { "공급 대기 평균", "공급 대기 최대", "대기 차량", "재고+대기 평균" };

    [Fact]
    public async Task Old_Results_Show_Dashes_In_The_Wait_Columns_And_No_Cycle_Note()
    {
        var (env, j) = await Finished(); using var _ = env; // FakeResult has no value9..15, as models built before the supply modes
        var summary = await env.Http.GetStringAsync("/jobs/" + j.Id);
        foreach (var h in WaitHeads) Assert.Contains($"<button type=\"button\">{h}</button>", summary);
        // stock max, then wait avg / wait max / wait vehicles / stock + wait: all unknown
        Assert.Contains("<td class=\"num\">6</td><td class=\"num\">-</td><td class=\"num\">-</td><td class=\"num\">-</td><td class=\"num\">-</td>", summary);
        Assert.DoesNotContain("순환: 운행 시작 기준", summary); // no cycle group
        var detail = await env.Http.GetStringAsync($"/jobs/{j.Id}?tab=detail");
        Assert.DoesNotContain("공급 대기 평균", detail);
        Assert.DoesNotContain("순환: 운행 시작 기준", detail);
        Assert.DoesNotContain("뒤차 대기", summary + detail);
    }

    [Fact]
    public async Task Wait_Columns_Show_Avg_Max_Vehicles_And_Stock_Plus_Wait()
    {
        var (env, j) = await Finished(); using var _ = env;
        SupplyFixture.WaitResult(env, j);
        var v = JobView.For(env.Store, j);
        var m = v.Materials[0];
        Assert.Equal(1.5, m.WaitAvg); Assert.Equal(3, m.WaitMax); Assert.Equal(0.8, m.WaitVehAvg);
        var summary = await env.Http.GetStringAsync("/jobs/" + j.Id);
        // FakeResult stock mean 3 + wait 1.5 = 4.5
        Assert.Contains("<td class=\"num\">6</td><td class=\"num\">1.5</td><td class=\"num\">3</td><td class=\"num\">0.8</td><td class=\"num\">4.5</td>", summary);
        var detail = await env.Http.GetStringAsync($"/jobs/{j.Id}?tab=detail");
        Assert.Contains("<td class=\"num\">3 / 1 / 6 · 공급 대기 평균 1.5</td>", detail);
        // stats.tsv (kept after the run folders are purged) carries the wait values
        Assert.EndsWith("\t1.5\t3\t0.8", JobView.StatsLines(v).First(l => l.StartsWith("material\t")));
    }

    [Theory]
    [InlineData("\t1.5\t3\t0.8", true)]
    [InlineData("", false)] // a stats.tsv from before the wait columns
    public async Task Saved_Stats_Read_The_Wait_Columns_Or_No_Data(string extra, bool has)
    {
        var (env, j) = await Finished(); using var _ = env;
        File.Delete(Path.Combine(env.Store.ProjectDir(j.Id), "runs", "n2_s1", "result.tsv")); // run folders purged: stats.tsv is all that is left
        var line = string.Join("\t", "material", "M_x", "G1", "3", "4.5", "45", "20", "60", "100", "200", "3", "1.5", "1", "6", "2", "15", "3000") + extra;
        File.WriteAllLines(Path.Combine(env.Store.JobDir(j.Id), JobRetention.StatsFile), new[] { "kind\tname\tgroup\tvalues", line });
        var m = JobView.For(env.Store, j).Materials.Single();
        Assert.Equal(15, m.Trips);
        if (has) { Assert.Equal(1.5, m.WaitAvg); Assert.Equal(3, m.WaitMax); Assert.Equal(0.8, m.WaitVehAvg); }
        else Assert.True(double.IsNaN(m.WaitAvg) && double.IsNaN(m.WaitMax) && double.IsNaN(m.WaitVehAvg));
    }

    [Fact]
    public async Task Cycle_Group_Shows_Follow_Numbers_Instead_Of_Utilization_And_Gets_A_Per_Count_Table()
    {
        var (env, j) = await SupplyFixture.Cycle(); using var _ = env;
        var v = JobView.For(env.Store, j);
        var g = Assert.Single(v.Groups);
        Assert.True(g.Cycle); Assert.Equal(0.96, g.FollowRate, 6); Assert.Equal(12, g.SwapAvg, 6); Assert.Equal(40, g.SwapMax, 6);
        Assert.Equal(0.95, v.FollowMin, 6);
        Assert.True(g.AllOk); Assert.False(JobPages.Over(g, v.UtilMax)); // utilization 90 % is above the limit but a cycle group is not judged on it

        var summary = await env.Http.GetStringAsync("/jobs/" + j.Id);
        Assert.Contains("뒤차 대기 96%", summary); Assert.Contains("교체 평균 12초", summary); Assert.Contains("교체 최대 40초", summary);
        Assert.Contains("순환: 운행 시작 기준", summary);
        Assert.DoesNotContain("<span class=\"bar", summary); // no utilization bar for the cycle group
        Assert.DoesNotContain("가동률 초과", summary);
        Assert.Contains("✓ 확인 실행 통과", summary);

        var detail = await env.Http.GetStringAsync($"/jobs/{j.Id}?tab=detail");
        Assert.Contains("순환: 운행 시작 기준", detail);
        // one line per tested count: mean follow rate, mean swap seconds, longest swap (count 2 ran with two seeds)
        foreach (var h in new[] { "대수", "뒤차 대기율", "교체 평균(초)", "교체 최대(초)" }) Assert.Contains($"<th>{h}</th>", detail);
        Assert.Contains("<tr><td class=\"num\">1</td><td class=\"num\">60%</td><td class=\"num\">20</td><td class=\"num\">90</td></tr>", detail);
        Assert.Contains("<tr><td class=\"num\"><b>2</b></td><td class=\"num\">94%</td><td class=\"num\">13</td><td class=\"num\">60</td></tr>", detail);
        Assert.Contains("<tr><td class=\"num\">3</td><td class=\"num\">99%</td><td class=\"num\">10</td><td class=\"num\">30</td></tr>", detail);
        Assert.DoesNotContain("그룹별 대수에 따른 가동률", detail); // the utilization curve is not drawn for a cycle group
    }

    [Fact]
    public async Task Cycle_Group_Without_Follow_Values_Shows_Dashes()
    {
        var (env, j) = await SupplyFixture.Cycle(follow: false); using var _ = env; // summary.tsv from before the swap columns
        var summary = await env.Http.GetStringAsync("/jobs/" + j.Id);
        Assert.Contains("뒤차 대기 -", summary); Assert.Contains("교체 평균 -", summary); Assert.Contains("교체 최대 -", summary);
    }
}

/// <summary>A finished job with the supply-wait and cycle-swap values written the way the model and the runner leave them.</summary>
public static class SupplyFixture
{
    /// <summary>value9..15 of the confirm run's material rows: wait avg / max / vehicles, swaps (count, seconds, longest), swaps by a waiting vehicle.</summary>
    public static void WaitResult(JobEnv env, JobInfo j)
    {
        var tsv = Path.Combine(env.Store.ProjectDir(j.Id), "runs", "n2_s1", "result.tsv");
        File.WriteAllLines(tsv, File.ReadAllLines(tsv).Select(l => l.StartsWith("material\t") ? l + "\t1.5\t3\t0.8\t50\t600\t40\t48" : l));
    }

    /// <summary>A Cycle G1 whose confirm run (2 vehicles) follows 96 %: swaps 12 s mean, 40 s longest; counts 1 and 3 and a second seed of 2 give the per-count table.
    /// follow = false leaves the swap columns out, as a summary.tsv from before them.</summary>
    public static async Task<(JobEnv Env, JobInfo J)> Cycle(bool follow = true)
    {
        var env = new JobEnv();
        var j = await env.WaitFinished(await env.SendFiled("분석", "A안", addFiles: JobEnv.CycleG1));
        WaitResult(env, j);
        var sum = Path.Combine(env.Store.ProjectDir(j.Id), "runs", "summary.tsv");
        File.Delete(sum);
        SummaryRow R(string id, int n, int seed, bool ok, string reason, double util, double rate, double avg, double max) =>
            follow ? new(id, "G1", n, seed, ok, reason, util, 2, 10, 10, 0, 0, 5, "G1=" + n, rate, avg, max) : new(id, "G1", n, seed, ok, reason, util, 2, 10, 10, 0, 0, 5, "G1=" + n);
        SummaryTsv.Append(sum, R("n3_s1", 3, 1, true, "", 0.4, 0.99, 10, 30));
        SummaryTsv.Append(sum, R("n2_s1", 2, 1, true, "", 0.9, 0.96, 12, 40)); // the confirm run
        SummaryTsv.Append(sum, R("n2_s2", 2, 2, true, "", 0.9, 0.92, 14, 60));
        SummaryTsv.Append(sum, R("n1_s1", 1, 1, false, "뒤차 대기 60% < 95%", 0.95, 0.60, 20, 90));
        return (env, j);
    }
}
