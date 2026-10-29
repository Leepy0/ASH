using ASH.Server;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;

namespace ASH.Server.Tests;

/// <summary>The 자재 품절 원인 box on the job page and the conclusion tab, and its report sheet. The fake job's one confirm run n2_s1 stands 45 s for every
/// material, with leads call → load 20.5 s and call → unloaded 100.5 s (max 200 s).</summary>
public class StockoutPanelTests
{
    static async Task<(JobEnv Env, string Id, string Aid)> Job(Func<string, string>? edit = null)
    {
        var env = new JobEnv();
        var id = await env.SendFiled("라인 증설", "A안"); await env.WaitFinished(id);
        var aid = env.Factory.Services.GetRequiredService<AnalysisStore>().All().Single().Id;
        if (edit != null)
        {
            var tsv = Path.Combine(env.Store.ProjectDir(id), "runs", "n2_s1", "result.tsv");
            File.WriteAllText(tsv, edit(File.ReadAllText(tsv))); // before the first page: the view is cached once read
        }
        return (env, id, aid);
    }

    [Fact]
    public async Task Box_Shows_The_Numbers_And_Links_To_The_Cause_Tab_On_Both_Pages()
    {
        var (env, id, aid) = await Job(); using var _ = env;
        var page = await env.Http.GetStringAsync("/jobs/" + id);
        var v = JobView.For(env.Store, env.Store.Get(id)!);
        var row = v.Stockout!.Rows.First();
        Assert.Equal(("n2_s1", 1, 45.0, 45.0), (row.TopRun, row.TopNumber, row.StopSum, row.StopAvg));
        Assert.Contains("<h2>자재 품절 원인</h2>", page);
        Assert.Contains($"<b>{row.Material}</b>", page);
        Assert.Contains("<td class=\"num\">0:45 / 0:45</td>", page);   // stop sum / average
        Assert.Contains("<td class=\"num\">1:41 / 3:20</td>", page);   // lead 100.5 s / 200 s
        Assert.Contains($"<td><b>{row.Top.Verdict.Replace("·", "&#183;")}</b></td>", page); // pages encode the middle dot
        Assert.Contains($"<a href=\"/analyses/{aid}?tab=cause&amp;job={id}&amp;run=n2_s1\" title=\"원인 탭에서 이 실행 보기\">실행 1</a>", page);
        Assert.False(double.IsNaN(row.Top.CoverageSec)); Assert.False(double.IsNaN(row.Top.MinLeadSec)); // input and layout were read

        var tab = await env.Http.GetStringAsync($"/analyses/{aid}?tab=conclusion&job={id}");
        Assert.Contains("<h2>자재 품절 원인</h2>", tab); Assert.Contains("<td class=\"num\">1:41 / 3:20</td>", tab);
        // the cause tab lists the same material with the others
        Assert.Contains("<h3>자재 품절<span>", await env.Http.GetStringAsync($"/analyses/{aid}?tab=cause&job={id}&run=n2_s1"));
    }

    [Fact]
    public async Task No_Box_Or_Sheet_Without_Stop_Time()
    {
        var (env, id, aid) = await Job(t => t.Replace("\t45\t12\t", "\t0\t12\t")); using var _ = env;
        Assert.DoesNotContain("자재 품절 원인", await env.Http.GetStringAsync("/jobs/" + id));
        Assert.DoesNotContain("자재 품절 원인", await env.Http.GetStringAsync($"/analyses/{aid}?tab=conclusion&job={id}"));
        var j = env.Store.Get(id)!;
        Assert.Null(JobView.For(env.Store, j).Stockout);
        var path = Path.Combine(Path.GetTempPath(), "ash-so-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            JobReport.Write(j, JobView.For(env.Store, j), path);
            using var wb = new XLWorkbook(path);
            Assert.DoesNotContain(wb.Worksheets, w => w.Name == "자재 품절 원인");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Older_Model_Without_Lead_Rows_Still_Gets_The_Box_And_Says_What_Is_Missing()
    {
        var (env, id, _) = await Job(t => string.Join("\n", t.Split('\n').Where(l => !l.StartsWith("matstat") && !l.StartsWith("vehicle")))); using var _2 = env;
        var page = await env.Http.GetStringAsync("/jobs/" + id);
        Assert.Contains("<h2>자재 품절 원인</h2>", page);
        Assert.Contains("자료 부족: matstat", page); // the row's tooltip
        Assert.Contains("<td class=\"num\">- / -</td>", page); // no lead times
    }

    [Fact]
    public async Task Report_Gets_A_Sheet_Per_Material()
    {
        var (env, id, _) = await Job(); using var _2 = env;
        var j = env.Store.Get(id)!;
        var v = JobView.For(env.Store, j);
        var path = Path.Combine(Path.GetTempPath(), "ash-so-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            JobReport.Write(j, v, path);
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet("자재 품절 원인");
            Assert.Equal("자재", ws.Cell(1, 1).GetString()); Assert.Equal("판정", ws.Cell(1, 17).GetString());
            var row = v.Stockout!.Rows.First();
            Assert.Equal(row.Material, ws.Cell(2, 1).GetString());
            Assert.Equal(45, ws.Cell(2, 3).GetDouble());
            Assert.Equal("실행 1 (n2_s1)", ws.Cell(2, 6).GetString());
            Assert.Equal(100.5, ws.Cell(2, 9).GetDouble()); Assert.Equal(200, ws.Cell(2, 10).GetDouble());
            Assert.Equal(row.Top.Verdict, ws.Cell(2, 17).GetString());
            Assert.Equal(v.Stockout.Rows.Count + 1, ws.LastRowUsed()!.RowNumber());
        }
        finally { File.Delete(path); }
    }
}
