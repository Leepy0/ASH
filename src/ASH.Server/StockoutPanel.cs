using System.Globalization;
using System.Text;
using ASH.Core.Cause;
using static ASH.Server.JobPages;

namespace ASH.Server;

/// <summary>The 자재 품절 원인 box of a result: per material that ran out in a confirm run, stop time over the runs and, from the run with the most stop,
/// coverage at the call, the free-speed lead, the observed leads, dispatch wait, standing share, verdict and fix, linking to the 원인 tab with that run chosen.
/// Drawn from <see cref="JobView.Stockout"/>; nothing when no confirm run stood for a material.</summary>
public static class StockoutPanel
{
    static string Ms(double s) => StockoutCauses.Ms(s);
    static string Pc(double f) => double.IsNaN(f) ? "-" : Math.Round(f * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>sortable: header buttons sort (the page must carry <see cref="JobPages.SortScript"/>).</summary>
    public static void Section(StringBuilder sb, JobInfo j, JobView v, bool sortable = false)
    {
        if (v.Stockout is not { Rows.Count: > 0 } s) return;
        string T(string t) => sortable ? Th(t) : $"<th>{t}</th>";
        sb.Append($"<section class=\"box\"><div class=\"box-head\"><h2>자재 품절 원인</h2><span class=\"t4\">자재 {s.Rows.Count}개 · 확인 실행 {s.RunsRead}개 기준</span></div>");
        sb.Append("<div class=\"scroll\">").Append(sortable ? "<table class=\"sort\">" : "<table>").Append("<thead><tr>")
            .Append(T("자재")).Append(T("품절 합계/평균(m:ss)")).Append(T("호출 시 남은 시간")).Append(T("최소 리드타임")).Append(T("리드타임 평균/최대"))
            .Append(T("배차 대기")).Append(T("이동 중 정지")).Append(T("판정")).Append("<th>처방</th><th>지도</th></tr></thead><tbody>");
        foreach (var r in s.Rows)
        {
            var k = r.Top;
            var url = JobView.CauseUrl(j, r.TopRun);
            var link = url == null ? $"실행 {r.TopNumber}" : $"<a href=\"{H(url)}\" title=\"원인 탭에서 이 실행 보기\">실행 {r.TopNumber}</a>";
            var tip = StockoutCauses.Detail(k);
            sb.Append($"<tr title=\"{H(tip)}\"><td><b>{H(r.Material)}</b> <span class=\"t4\">{H(r.Group)}</span></td><td class=\"num\">{Ms(r.StopSum)} / {Ms(r.StopAvg)}</td>" +
                $"<td class=\"num\">{Ms(k.CoverageSec)}</td><td class=\"num\">{Ms(k.MinLeadSec)}</td><td class=\"num\">{Ms(k.DelivAvg)} / {Ms(k.DelivMax)}</td>" +
                $"<td class=\"num\">{Ms(k.DispatchWaitSec)}</td><td class=\"num\">{Pc(k.StopShare)}</td><td><b>{H(k.Verdict)}</b></td><td>{H(k.Fix)}</td><td>{link}</td></tr>");
        }
        sb.Append("</tbody></table></div>");
        sb.Append("<div class=\"t4\" title=\"호출 시 남은 시간 = CallThreshold × 용기 1개 소비 시간. 최소 리드타임 = 대기 CP→상차 CP 공차 + 상차 + 적재 주행 + 추가 + 하차, 대기 없이. " +
            "배차 대기 = 호출→상차 CP 도착 평균 − 공차 주행. 이동 중 정지 = 그룹 작업 시간 중 정지 비율. 판정 순서: 구조적(최소 리드 ≥ 남은 시간) → 배차 대기 → 교통·교착 → 변동(평균 &lt; 남은 시간 ≤ 최대).\">" +
            "리드타임·판정·처방 = 품절 가장 긴 실행 · 구조적 = 최소 리드타임 ≥ 호출 시 남은 시간(대수 무관)</div>");
        sb.Append("</section>");
    }
}
