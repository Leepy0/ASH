using ASH.Core;
using ASH.Core.Input;
using ClosedXML.Excel;

namespace ASH.Server;

/// <summary>The result pages as an Excel workbook: same numbers, as real cells so they can be sorted and charted.</summary>
public static class JobReport
{
    const string Pct = "0.0%";

    public static void Write(JobInfo job, JobView view, string path)
    {
        using var wb = ExcelReader.NewWorkbook();
        Summary(wb.AddWorksheet("요약"), job, view);
        Curves(wb.AddWorksheet("대수별 가동률"), job, view);
        Runs(wb.AddWorksheet("실행 목록"), view);
        Impact(wb.AddWorksheet("영향표"), view);
        Blocks(wb.AddWorksheet("block"), view);
        Vehicles(wb.AddWorksheet("차량"), view);
        MaterialStats(wb.AddWorksheet("자재 통계"), view);
        Input(wb.AddWorksheet("입력"), job, view);

        // saved beside the target and moved over it: a failure half way must not leave a truncated report
        var tmp = path + ".tmp";
        try
        {
            using (var fs = File.Create(tmp)) wb.SaveAs(fs);
            File.Move(tmp, path, true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    // cell helpers: numbers stay numbers; NaN (no data) stays an empty cell
    static void Put(IXLWorksheet ws, int r, int c, object? v, string? fmt = null)
    {
        var cell = ws.Cell(r, c);
        switch (v)
        {
            case null: return;
            case double d when double.IsNaN(d): return;
            case double d: cell.Value = d; break;
            case int i: cell.Value = i; break;
            case long l: cell.Value = l; break;
            case bool b: cell.Value = b ? "예" : "아니오"; break;
            default: cell.Value = v.ToString() ?? ""; break;
        }
        if (fmt != null) cell.Style.NumberFormat.Format = fmt;
    }

    static void Header(IXLWorksheet ws, int r, params string[] heads)
    {
        for (int c = 0; c < heads.Length; c++) ws.Cell(r, c + 1).Value = heads[c];
        var range = ws.Range(r, 1, r, heads.Length);
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.LightGray;
    }

    static void Title(IXLWorksheet ws, int r, string text) { ws.Cell(r, 1).Value = text; ws.Cell(r, 1).Style.Font.Bold = true; }

    /// <summary>Widths are set by hand: AdjustToContents needs fonts, which a service host may not have.</summary>
    static void Widths(IXLWorksheet ws)
    {
        int cols = ws.LastColumnUsed()?.ColumnNumber() ?? 0, rows = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (int c = 1; c <= cols; c++)
        {
            double widest = 0;
            for (int r = 1; r <= rows; r++)
            {
                double w = 0;
                foreach (var ch in ws.Cell(r, c).GetString()) w += ch >= 0x1100 ? 2 : 1;
                widest = Math.Max(widest, w);
            }
            ws.Column(c).Width = Math.Min(60, Math.Max(8, widest * 1.1 + 2));
        }
    }

    static void Note(IXLWorksheet ws, string text) { ws.Cell(1, 1).Value = text; Widths(ws); }

    static void Summary(IXLWorksheet ws, JobInfo j, JobView v)
    {
        int r = 1;
        void Kv(string k, object? val, string? fmt = null) { ws.Cell(r, 1).Value = k; ws.Cell(r, 1).Style.Font.Bold = true; Put(ws, r, 2, val, fmt); r++; }
        Kv("작업", j.Id); Kv("프로젝트/안", j.Project + (j.Plan.Length > 0 ? " / " + j.Plan : "")); Kv("모델", j.Model); Kv("보낸 사람", j.Submitter);
        Kv("상태", JobState.Korean(j.State));
        Kv("접수", SeoulTime.Text(j.CreatedUtc));
        if (j.StartedUtc is { } s) Kv("시작", SeoulTime.Text(s));
        if (j.FinishedUtc is { } f) Kv("끝", SeoulTime.Text(f));

        if (j.Decided == null || v.Input == null)
        {
            r++;
            ws.Cell(r++, 1).Value = j.Finished ? "결과가 없습니다. 로그를 확인하세요." : "아직 결과가 없습니다. 실행이 끝나면 다시 받으세요.";
            if (j.Error != null) Kv("오류", j.Error);
            if (v.InputError != null && j.Finished) Kv("입력 오류", v.InputError);
            Widths(ws);
            return;
        }

        Kv("권장 총 대수(대)", j.Decided.Values.Sum());
        Kv("시험 범위(대)", $"{v.Input.Groups.Sum(g => g.MinCount)}~{v.Input.Groups.Sum(g => g.MaxCount)}");
        Kv("확인 판정", !v.Groups.All(g => g.AllOk) ? "실패" : v.Groups.Any(g => JobPages.Over(g, v.UtilMax)) ? "경고: 가동률 초과" : "통과");
        Kv("모델 실행 수(회)", j.RunsDone); Kv("실패 수(회)", j.RunsFailed);

        r++; Title(ws, r++, "그룹별 권장 대수");
        Header(ws, r++, "그룹", "AGV 타입", "권장(대)", "최소(대)", "최대(대)", "가동률(%)", "최소 재고/안전재고(배)", "판정", "사유", "공급 횟수(회)", "총 이동 거리(km)", "공급당 이동 거리(m)");
        var byGroup = v.VehicleGroups.ToDictionary(x => x.Group, StringComparer.Ordinal);
        foreach (var g in v.Groups)
        {
            Put(ws, r, 1, g.Group); Put(ws, r, 2, g.Type); Put(ws, r, 3, g.Decided); Put(ws, r, 4, g.Min); Put(ws, r, 5, g.Max);
            Put(ws, r, 6, g.Util, Pct); Put(ws, r, 7, g.MinStockRatio, "0.00");
            Put(ws, r, 8, !g.AllOk ? "실패" : JobPages.Over(g, v.UtilMax) ? "경고: 가동률 초과" : "통과"); Put(ws, r, 9, g.Reason);
            if (byGroup.TryGetValue(g.Group, out var vg)) { Put(ws, r, 10, vg.Jobs * vg.Count, "0"); Put(ws, r, 11, vg.Distance * vg.Count / 1000, "0.0"); Put(ws, r, 12, vg.DistPerJob, "0.#"); }
            r++;
        }
        if (j.Warnings.Count > 0)
        {
            r++; Title(ws, r++, "경고");
            foreach (var w in j.Warnings) ws.Cell(r++, 1).Value = w;
        }
        Widths(ws);
    }

    static void Curves(IXLWorksheet ws, JobInfo j, JobView v)
    {
        if (v.Curves.Count == 0) { Note(ws, "결과가 없습니다."); return; }
        Header(ws, 1, "그룹", "대수(대)", "평균 가동률(%)", "최소 가동률(%)", "최대 가동률(%)", "실행 수(회)", "실패 수(회)", "권장 여부");
        int r = 2;
        foreach (var (group, points) in v.Curves)
            foreach (var p in points)
            {
                int? dec = j.Decided != null && j.Decided.TryGetValue(group, out var d) ? d : null;
                Put(ws, r, 1, group); Put(ws, r, 2, p.Active); Put(ws, r, 3, p.Mean, Pct); Put(ws, r, 4, p.Lo, Pct); Put(ws, r, 5, p.Hi, Pct);
                Put(ws, r, 6, p.Runs); Put(ws, r, 7, p.Failed); Put(ws, r, 8, dec == p.Active ? "권장" : "");
                r++;
            }
        ws.SheetView.FreezeRows(1); Widths(ws);
    }

    static void Runs(IXLWorksheet ws, JobView v)
    {
        Header(ws, 1, "실행", "그룹", "대수(대)", "시드", "판정", "사유", "가동률(%)", "최소 재고/안전재고(배)", "공급(회)", "기대 공급(회)", "결품 시간(초)", "교착(회)", "실행 시간(초)", "시나리오(그룹=대수)");
        int r = 2;
        foreach (var x in v.Rows)
        {
            Put(ws, r, 1, x.RunId); Put(ws, r, 2, x.Group); Put(ws, r, 3, x.Active); Put(ws, r, 4, x.Seed); Put(ws, r, 5, x.Ok ? "통과" : JobView.UtilOnly(x) ? "경고" : "실패"); Put(ws, r, 6, x.Reason);
            Put(ws, r, 7, x.Utilization, Pct); Put(ws, r, 8, x.MinStockRatio, "0.00"); Put(ws, r, 9, x.Supplies); Put(ws, r, 10, x.Expected, "0.##");
            Put(ws, r, 11, x.StopTimeSec, "0.#"); Put(ws, r, 12, x.Deadlocks); Put(ws, r, 13, x.DurationSec, "0.#"); Put(ws, r, 14, x.Scenario);
            r++;
        }
        ws.SheetView.FreezeRows(1); Widths(ws);
    }

    static void Impact(IXLWorksheet ws, JobView v)
    {
        if (v.Impact.Count == 0) { Note(ws, "영향표가 없습니다(끄고 보냈거나 결과가 없음)."); return; }
        Header(ws, 1, "+1대 한 그룹", "그 대수(대)", "영향 받은 그룹", "가동률 변화(%p)", "최소 재고/안전재고 변화(배)");
        int r = 2;
        foreach (var x in v.Impact)
        {
            Put(ws, r, 1, x.Group); Put(ws, r, 2, x.PlusOne); Put(ws, r, 3, x.OtherGroup);
            Put(ws, r, 4, x.UtilDelta * 100, "+0.0;-0.0;0.0"); Put(ws, r, 5, x.MinStockDelta, "+0.00;-0.00;0.00");
            r++;
        }
        ws.SheetView.FreezeRows(1); Widths(ws);
    }

    static void Blocks(IXLWorksheet ws, JobView v)
    {
        if (v.Blocks.Count == 0) { Note(ws, "block 결과가 없습니다(block이 없거나 확인 실행 결과가 없음)."); return; }
        Header(ws, 1, "block", "평균 대기(초)", "점유율(%)", "통과 수(회)");
        int r = 2;
        foreach (var b in v.Blocks) { Put(ws, r, 1, b.Name); Put(ws, r, 2, b.AvgWaitSec, "0.0"); Put(ws, r, 3, b.Utilization, Pct); Put(ws, r, 4, b.Passes, "0.#"); r++; }
        ws.SheetView.FreezeRows(1); Widths(ws);
    }

    static void Vehicles(IXLWorksheet ws, JobView v)
    {
        if (v.Vehicles.Count == 0) { Note(ws, "차량 결과가 없습니다(모델이 차량 통계를 쓰지 않았거나 확인 실행 결과가 없음)."); return; }
        int r = 1;
        // where the time went, one column per part (seconds; the group's is the sum over its vehicles); empty for results without it
        var parts = JobPages.Parts(new ASH.Core.Runner.TimeSplit(0, 0, 0, 0, 0, 0, 0)).Select(p => p.Name + "(초)").ToArray();
        void Split(int c, ASH.Core.Runner.TimeSplit? s) { if (s != null) foreach (var p in JobPages.Parts(s)) Put(ws, r, c++, p.Sec, "0"); }
        Title(ws, r++, "그룹별 요약 (확인 실행 평균, 권장 대수만큼 움직인 차량)");
        Header(ws, r++, new[] { "그룹", "대수(대)", "평균 가동률(%)", "작업/대(회)", "주행 거리/대(m)", "거리/작업(m)", "시간/작업(초)" }.Concat(parts).ToArray());
        foreach (var g in v.VehicleGroups)
        {
            Put(ws, r, 1, g.Group); Put(ws, r, 2, g.Count); Put(ws, r, 3, g.Util, Pct); Put(ws, r, 4, g.Jobs, "0.#"); Put(ws, r, 5, g.Distance, "0");
            Put(ws, r, 6, g.DistPerJob, "0.#"); Put(ws, r, 7, g.BusyPerJob, "0.#"); Split(8, g.Split);
            r++;
        }
        r++; Title(ws, r++, "차량별");
        Header(ws, r++, new[] { "차량", "그룹", "가동률(%)", "작업 수(회)", "주행 거리(m)", "거리/작업(m)", "시간/작업(초)", "고장 시간(초)" }.Concat(parts).ToArray());
        foreach (var x in v.Vehicles)
        {
            Put(ws, r, 1, x.Name); Put(ws, r, 2, x.Group); Put(ws, r, 3, x.Util, Pct); Put(ws, r, 4, x.Jobs, "0.#"); Put(ws, r, 5, x.Distance, "0");
            Put(ws, r, 6, x.DistPerJob, "0.#"); Put(ws, r, 7, x.BusyPerJob, "0.#"); Put(ws, r, 8, x.Down, "0"); Split(9, x.Split);
            r++;
        }
        Widths(ws);
    }

    static void MaterialStats(IXLWorksheet ws, JobView v)
    {
        if (v.Materials.Count == 0) { Note(ws, "자재 통계가 없습니다(모델이 쓰지 않았거나 확인 실행 결과가 없음)."); return; }
        Header(ws, 1, "자재", "그룹", "공급(회)", "기대 공급(회)", "결품 시간(초)", "호출→상차 평균(초)", "호출→상차 최대(초)", "호출→하차 평균(초)", "호출→하차 최대(초)",
            "재고 평균(용기)", "재고 표준편차(용기)", "재고 최소(용기)", "재고 최대(용기)", "안전재고(용기)", "주의",
            "공급 횟수(회)", "공급 빈도(초)", "공급당 이동 거리(m)", "재고 평균(개)", "재고 최소(개)", "재고 최대(개)");
        int r = 2;
        foreach (var m in v.Materials)
        {
            Put(ws, r, 1, m.Name); Put(ws, r, 2, m.Group); Put(ws, r, 3, m.Supplies, "0.#"); Put(ws, r, 4, m.Expected, "0.#"); Put(ws, r, 5, m.StopSec, "0.#");
            Put(ws, r, 6, m.PickAvg, "0.0"); Put(ws, r, 7, m.PickMax, "0.0"); Put(ws, r, 8, m.DelivAvg, "0.0"); Put(ws, r, 9, m.DelivMax, "0.0");
            Put(ws, r, 10, m.StockAvg, "0.0"); Put(ws, r, 11, m.StockStd, "0.0"); Put(ws, r, 12, m.StockMin, "0.#"); Put(ws, r, 13, m.StockMax, "0.#");
            Put(ws, r, 14, m.Safety, "0.##"); Put(ws, r, 15, m.Warn ? "주의" : "");
            var per = JobPages.PerContainer(v, m);
            Put(ws, r, 16, m.Trips, "0.#"); Put(ws, r, 17, JobPages.SupplyInterval(v, m), "0"); Put(ws, r, 18, m.DistPerTrip, "0.#");
            Put(ws, r, 19, m.StockAvg * per, "0"); Put(ws, r, 20, m.StockMin * per, "0"); Put(ws, r, 21, m.StockMax * per, "0");
            r++;
        }
        ws.SheetView.FreezeRows(1); Widths(ws);
    }

    static void Input(IXLWorksheet ws, JobInfo j, JobView v)
    {
        int r = 1;
        if (v.Input != null)
        {
            var s = v.Input.Settings;
            Title(ws, r++, "설정");
            void Kv(string k, object val, string? fmt = null) { ws.Cell(r, 1).Value = k; Put(ws, r, 2, val, fmt); r++; }
            Kv("반복(회)", s.Replications); Kv("시드 시작", s.SeedBase); Kv("시뮬레이션 시간(초)", s.SimLengthSec); Kv("워밍업(초)", s.WarmupSec);
            Kv("가동률 상한(%)", s.UtilMax, Pct); Kv("안전재고(용기)", s.SafetyStock);

            r++; Title(ws, r++, "그룹");
            Header(ws, r++, "그룹", "AGV 타입", "최소(대)", "최대(대)");
            foreach (var g in v.Input.Groups) { Put(ws, r, 1, g.Group); Put(ws, r, 2, g.Type); Put(ws, r, 3, g.MinCount); Put(ws, r, 4, g.MaxCount); r++; }

            r++; Title(ws, r++, "자재");
            Header(ws, r++, "자재", "그룹", "형태", "T/T(초)", "용기당 수량(개)", "호출 임계(용기)");
            foreach (var m in v.Input.Materials)
            {
                Put(ws, r, 1, m.Material); Put(ws, r, 2, m.Group); Put(ws, r, 3, m.Form); Put(ws, r, 4, m.TaktTime); Put(ws, r, 5, m.UnitsPerContainer); Put(ws, r, 6, m.CallThreshold);
                r++;
            }
        }
        else { ws.Cell(r++, 1).Value = "입력을 읽지 못했습니다."; }

        r++; Title(ws, r++, "올린 파일");
        Header(ws, r++, "파일", "크기(바이트)", "SHA-256");
        foreach (var f in j.Files) { Put(ws, r, 1, f.Path); Put(ws, r, 2, f.Size); Put(ws, r, 3, f.Sha256); r++; }

        if (j.Clusters is { Count: > 0 })
        {
            r++; Title(ws, r++, "군집(함께 정한 그룹)");
            foreach (var c in j.Clusters) ws.Cell(r++, 1).Value = string.Join(", ", c);
        }
        Widths(ws);
    }
}
