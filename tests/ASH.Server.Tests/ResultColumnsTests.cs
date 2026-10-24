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
}
