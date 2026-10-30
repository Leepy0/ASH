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
            Assert.Equal(new[] { "요약", "대수별 가동률", "실행 목록", "자재 품절 원인", "영향표", "block", "차량", "자재 통계", "입력" }, wb.Worksheets.Select(w => w.Name).ToArray());

            var veh = wb.Worksheet("차량");
            var gr = veh.Search("그룹별 요약").First().Address.RowNumber + 2;
            Assert.Equal("G1", veh.Cell(gr, 1).GetString());
            Assert.Equal(2, veh.Cell(gr, 2).GetDouble());
            Assert.Equal(0.5, veh.Cell(gr, 3).GetDouble(), 6);
            Assert.Equal("0.0%", veh.Cell(gr, 3).Style.NumberFormat.Format);
            var t1 = veh.Search("TAB1").First().Address.RowNumber;
            Assert.Equal(0.6, veh.Cell(t1, 3).GetDouble(), 6);
            Assert.Equal("0.0%", veh.Cell(t1, 3).Style.NumberFormat.Format);
            Assert.Equal(60, veh.Cell(t1, 6).GetDouble(), 6);
            Assert.Equal(30, veh.Cell(t1, 8).GetDouble(), 6);
            Assert.Empty(veh.Search("TAB3"));

            var mat = wb.Worksheet("자재 통계");
            var mn = view.Materials[0].Name;
            Assert.Equal(mn, mat.Cell(2, 1).GetString());
            Assert.Equal(12, mat.Cell(2, 3).GetDouble());
            Assert.Equal(100.5, mat.Cell(2, 8).GetDouble(), 6);
            Assert.Equal(1.5, mat.Cell(2, 11).GetDouble(), 6);
            Assert.Equal("주의", mat.Cell(2, 15).GetString());

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

    static int Col(IXLWorksheet ws, int headerRow, string head) => ws.Row(headerRow).CellsUsed().Single(c => c.GetString() == head).Address.ColumnNumber;

    [Fact]
    public async Task Old_Results_Leave_The_Wait_Cells_Empty_And_Add_No_Cycle_Columns()
    {
        using var env = new JobEnv();
        var job = await env.WaitFinished(Id(await (await env.Send(JobEnv.MakePackage(env.Dir))).Content.ReadAsStringAsync()));
        var path = Tmp();
        try
        {
            JobReport.Write(job, JobView.For(env.Store, job), path);
            using var wb = new XLWorkbook(path);
            var mat = wb.Worksheet("자재 통계");
            foreach (var h in new[] { "공급 대기 평균", "공급 대기 최대", "대기 차량" }) Assert.True(mat.Cell(2, Col(mat, 1, h)).IsEmpty(), h);
            Assert.Equal("주의", mat.Cell(2, 15).GetString());
            Assert.Null(wb.Worksheet("요약").Search("뒤차 대기율").FirstOrDefault()); Assert.False(wb.Worksheets.Contains("순환 대수별"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Cycle_Job_Report_Has_Wait_And_Swap_Columns_And_A_Per_Count_Sheet()
    {
        var (env, job) = await SupplyFixture.Cycle(); using var _ = env;
        var view = JobView.For(env.Store, job);
        var path = Tmp();
        try
        {
            JobReport.Write(job, view, path);
            using var wb = new XLWorkbook(path);

            var mat = wb.Worksheet("자재 통계");
            Assert.Equal(1.5, mat.Cell(2, Col(mat, 1, "공급 대기 평균")).GetDouble(), 6);
            Assert.Equal(3, mat.Cell(2, Col(mat, 1, "공급 대기 최대")).GetDouble(), 6);
            Assert.Equal(0.8, mat.Cell(2, Col(mat, 1, "대기 차량")).GetDouble(), 6);
            Assert.Equal("0.0", mat.Cell(2, Col(mat, 1, "공급 대기 평균")).Style.NumberFormat.Format);
            Assert.Equal("주의", mat.Cell(2, 15).GetString());
            // the per-part stock copy still sits under its header, wherever the wait columns went
            var m = view.Materials[0];
            Assert.Equal(m.StockAvg * JobPages.PerContainer(view, m), mat.Cell(2, Col(mat, 1, "재고 평균(개)")).GetDouble(), 6);

            var sum = wb.Worksheet("요약");
            var head = sum.Search("그룹별 권장 대수").First().Address.RowNumber + 1;
            Assert.Equal(0.96, sum.Cell(head + 1, Col(sum, head, "뒤차 대기율")).GetDouble(), 6);
            Assert.Equal("0.0%", sum.Cell(head + 1, Col(sum, head, "뒤차 대기율")).Style.NumberFormat.Format);
            Assert.Equal(12, sum.Cell(head + 1, Col(sum, head, "교체 평균(초)")).GetDouble(), 6);
            Assert.Equal(40, sum.Cell(head + 1, Col(sum, head, "교체 최대(초)")).GetDouble(), 6);
            Assert.True(sum.Cell(head + 1, Col(sum, head, "가동률(%)")).IsEmpty()); // utilization is not shown for a cycle group
            Assert.Equal("통과", sum.Cell(head + 1, Col(sum, head, "판정")).GetString());

            var pc = wb.Worksheet("순환 대수별");
            Assert.Equal(new[] { "그룹", "대수(대)", "뒤차 대기율", "교체 평균(초)", "교체 최대(초)", "실행 수(회)", "권장 여부" }, pc.Row(1).CellsUsed().Select(c => c.GetString()).ToArray());
            Assert.Equal(3, pc.LastRowUsed()!.RowNumber() - 1); // counts 1..3
            var two = pc.Rows(2, 4).Single(r => r.Cell(2).GetDouble() == 2);
            Assert.Equal("G1", two.Cell(1).GetString());
            Assert.Equal(0.94, two.Cell(3).GetDouble(), 6); Assert.Equal("0.0%", two.Cell(3).Style.NumberFormat.Format);
            Assert.Equal(13, two.Cell(4).GetDouble(), 6); Assert.Equal(60, two.Cell(5).GetDouble(), 6);
            Assert.Equal(2, two.Cell(6).GetDouble()); Assert.Equal("권장", two.Cell(7).GetString());
            Assert.Equal("", pc.Rows(2, 4).Single(r => r.Cell(2).GetDouble() == 1).Cell(7).GetString());
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
            Assert.Equal(8, wb.Worksheets.Count);
            Assert.Contains(wb.Worksheet("요약").CellsUsed(), c => c.GetString().Contains("결과"));
            Assert.Contains(wb.Worksheet("영향표").CellsUsed(), c => c.GetString().Contains("없습니다"));
        }
        finally { File.Delete(path); }
    }
}
