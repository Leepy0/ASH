using System.Text.Json;
using ASH.Server;
using ClosedXML.Excel;

namespace ASH.Server.Tests;

public class JobReportTests
{
    static string Id(string json) => JsonDocument.Parse(json).RootElement.GetProperty("id").GetString()!;

    static string Tmp() => Path.Combine(Path.GetTempPath(), "ash-report-" + Guid.NewGuid().ToString("N") + ".xlsx");

    [Fact]
    public async Task Finished_Job_Report_Has_The_Sheets_And_Numbers()
    {
        using var env = new JobEnv();
        var id = Id(await (await env.Send(JobEnv.MakePackage(env.Dir))).Content.ReadAsStringAsync());
        var job = await env.WaitFinished(id);
        var view = JobView.For(env.Store, job);
        var path = Tmp();
        try
        {
            JobReport.Write(job, view, path);
            Assert.False(File.Exists(path + ".tmp"));
            using var wb = new XLWorkbook(path);
            Assert.Equal(new[] { "요약", "대수별 가동률", "실행 목록", "영향표", "block", "입력" }, wb.Worksheets.Select(w => w.Name).ToArray());

            var sum = wb.Worksheet("요약");
            var total = sum.Search("권장 총 대수").First().Address.RowNumber;
            Assert.Equal(job.Decided!.Values.Sum(), sum.Cell(total, 2).GetDouble());
            Assert.Contains(sum.CellsUsed(), c => c.GetString() == "샘플 경고");
            var grp = sum.Search("그룹별 권장 대수").First().Address.RowNumber + 2; // first data row under the header
            Assert.Equal("G1", sum.Cell(grp, 1).GetString());
            var util = sum.Cell(grp, 6);
            Assert.Equal(XLDataType.Number, util.DataType);
            Assert.Equal("0.0%", util.Style.NumberFormat.Format);

            var runs = wb.Worksheet("실행 목록");
            Assert.Equal(view.Rows.Count, runs.LastRowUsed()!.RowNumber() - 1);
            Assert.Equal(XLDataType.Number, runs.Cell(2, 7).DataType);

            var curves = wb.Worksheet("대수별 가동률");
            Assert.Equal(3, curves.LastRowUsed()!.RowNumber() - 1); // counts 1..3 of G1
            Assert.Equal("권장", curves.Rows(2, 4).Single(r => r.Cell(2).GetDouble() == 2).Cell(8).GetString());

            var input = wb.Worksheet("입력");
            var asy = job.Files.Single(f => f.Path == "m.arc/agv.asy");
            Assert.Contains(input.CellsUsed(), c => c.GetString() == asy.Sha256);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Job_Without_Results_Still_Gets_A_Workbook()
    {
        using var env = new JobEnv(worker: false);
        var id = Id(await (await env.Send(JobEnv.MakePackage(env.Dir))).Content.ReadAsStringAsync());
        var job = env.Store.Get(id)!;
        Assert.Null(job.Decided);
        var path = Tmp();
        try
        {
            JobReport.Write(job, JobView.For(env.Store, job), path);
            using var wb = new XLWorkbook(path);
            Assert.Equal(6, wb.Worksheets.Count);
            Assert.Contains(wb.Worksheet("요약").CellsUsed(), c => c.GetString().Contains("결과"));
            Assert.Contains(wb.Worksheet("영향표").CellsUsed(), c => c.GetString().Contains("없습니다"));
        }
        finally { File.Delete(path); }
    }
}
