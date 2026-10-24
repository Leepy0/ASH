using System.Text.Json;
using ASH.Core.Runner;
using ASH.Server;
using ClosedXML.Excel;

namespace ASH.Server.Tests;

/// <summary>가동률 내역 막대: the vehicles' time split (value7..11 of the vehicle rows) per vehicle and summed per group, on the 결론 group table,
/// the 상세 vehicle table and the Excel 차량 sheet; results without it show "-" and no bar.</summary>
public class TimeSplitTests
{
    static string Id(string json) => JsonDocument.Parse(json).RootElement.GetProperty("id").GetString()!;

    static async Task<(JobEnv Env, JobInfo J)> Finished()
    {
        var env = new JobEnv();
        var id = Id(await (await env.Send(JobEnv.MakePackage(env.Dir))).Content.ReadAsStringAsync());
        return (env, await env.WaitFinished(id));
    }

    [Fact]
    public async Task Old_Results_Show_A_Dash_And_No_Bar()
    {
        var (env, j) = await Finished(); using var _ = env;
        var v = JobView.For(env.Store, j);
        Assert.All(v.Vehicles, x => Assert.Null(x.Split));
        Assert.Null(Assert.Single(v.VehicleGroups).Split);
        var summary = await env.Http.GetStringAsync("/jobs/" + j.Id);
        Assert.Contains("<th>시간 내역</th>", summary);
        Assert.Contains("<td>-</td><td>2</td><td>30</td>", summary); // no split, then 최소 재고/안전재고 and 공급 횟수
        Assert.DoesNotContain("<span class=\"sbar\"", summary);
        Assert.DoesNotContain("<div class=\"t4 lg\">", summary);
        var detail = await env.Http.GetStringAsync($"/jobs/{j.Id}?tab=detail");
        Assert.DoesNotContain("<th>시간 내역</th>", detail);
    }

    [Fact]
    public async Task Split_Per_Vehicle_And_Summed_Per_Group_On_The_Pages_And_In_The_Report()
    {
        var (env, j) = await Finished(); using var _ = env;
        var tsv = Path.Combine(env.Store.ProjectDir(j.Id), "runs", "n2_s1", "result.tsv");
        // value7..11: loaded, handling, buffer within / beyond the allowance, repair (TAB1 busy 6000, TAB2 busy 4000, window 10000 each)
        File.WriteAllLines(tsv, File.ReadAllLines(tsv).Select(l =>
            l.StartsWith("vehicle\tTAB1\t") ? string.Join("\t", l.Split('\t')[..^2]) + "\t2000\t1000\t500\t1500\t30" :
            l.StartsWith("vehicle\tTAB2\t") ? string.Join("\t", l.Split('\t')[..^2]) + "\t1500\t500\t0\t1000\t0" : l));
        var v = JobView.For(env.Store, j); // first read of this job: nothing cached yet

        var t1 = v.Vehicles.Single(x => x.Name == "TAB1").Split!;
        Assert.Equal(2470, t1.Empty); Assert.Equal(2500, t1.Idle);
        var g = Assert.Single(v.VehicleGroups);
        Assert.Equal(new TimeSplit(20000, 10000, 3500, 1500, 500, 2500, 30), g.Split);
        Assert.Equal(4470, g.Split!.Empty); Assert.Equal(7500, g.Split.Idle);
        Assert.Equal(g.Util, g.Split.Busy / g.Split.Window, 9); // the busy share is the group's utilization
        Assert.Equal(g.Split.Window, JobPages.Parts(g.Split).Sum(p => p.Sec), 9); // the parts make up the measured time

        var summary = await env.Http.GetStringAsync("/jobs/" + j.Id);
        Assert.Contains("<span class=\"sbar\" title=\"가동 50.0%\"><span><i class=\"u-ld\" style=\"width:17.5%\" title=\"적재 주행 3500초 · 17.5%\"></i>", summary);
        Assert.Contains("<i class=\"u-id\" style=\"width:37.5%\" title=\"유휴 7500초 · 37.5%\"></i></span><b style=\"left:75%\"></b></span></td><td>2</td>", summary);
        Assert.Contains("<i class=\"u-bx\"></i>버퍼 대기(허용 초과)", summary); // legend
        var detail = await env.Http.GetStringAsync($"/jobs/{j.Id}?tab=detail");
        Assert.Contains("<th>가동률(%)</th><th>시간 내역</th><th>작업 수(회)</th>", detail);
        Assert.Contains("title=\"공차 주행 2470초 · 24.7%\"", detail);
        Assert.Contains("title=\"고장 30초 · 0.3%\"", detail);

        // kept for purged run folders: the split rides along at the end of the vehicle rows of stats.tsv
        Assert.EndsWith("\t10000\t6000\t2000\t1000\t500\t1500\t30", JobView.StatsLines(v).First(l => l.StartsWith("vehicle\tTAB1\t")));

        var path = Path.Combine(Path.GetTempPath(), "ash-report-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            JobReport.Write(j, v, path);
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet("차량");
            var head = ws.Search("유휴(초)").First();
            Assert.Equal(7500, ws.Cell(head.Address.RowNumber + 1, head.Address.ColumnNumber).GetDouble()); // group: sum over its vehicles
            var vhead = ws.Search("유휴(초)").Skip(1).First();
            Assert.Equal(2500, ws.Cell(vhead.Address.RowNumber + 1, vhead.Address.ColumnNumber).GetDouble()); // TAB1
        }
        finally { File.Delete(path); }
    }
}
