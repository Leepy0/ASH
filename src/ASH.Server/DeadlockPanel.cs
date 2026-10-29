using System.Text;
using ASH.Core.Cause;
using static ASH.Server.JobPages;

namespace ASH.Server;

/// <summary>The 교착 box of a result: the confirm runs that deadlocked and, per place (blocks or CPs of the lock), episodes, runs, first time, groups and vehicles,
/// each place linking to the 원인 tab with its busiest run chosen. Drawn from <see cref="JobView.Deadlock"/>; nothing when no run deadlocked.</summary>
public static class DeadlockPanel
{
    const int Shown = 10;

    /// <summary>Sim time as h:mm; "-" for no time.</summary>
    internal static string Hm(double sec)
    {
        if (double.IsNaN(sec) || double.IsInfinity(sec)) return "-";
        long s = (long)Math.Round(Math.Clamp(sec, 0, 1e9));
        return $"{s / 3600}:{s % 3600 / 60:00}";
    }

    /// <summary>"V1·V2·V3 외 2대": the first few names.</summary>
    internal static string Names(IReadOnlyList<string> names, int max = 4) =>
        names.Count == 0 ? "-" : string.Join("·", names.Take(max)) + (names.Count > max ? $" 외 {names.Count - max}대" : "");

    static string RunLink(JobInfo j, DeadlockRun r)
    {
        var url = JobView.CauseUrl(j, r.RunId);
        return url == null ? $"실행 {r.Number}" : $"<a href=\"{H(url)}\" title=\"원인 탭에서 이 실행의 지도 보기\">실행 {r.Number}</a>";
    }

    /// <summary>sortable: header buttons sort (the page must carry <see cref="JobPages.SortScript"/>).</summary>
    public static void Section(StringBuilder sb, JobInfo j, JobView v, bool sortable = false)
    {
        if (v.Deadlock is not { } d) return;
        string T(string s) => sortable ? Th(s) : $"<th>{s}</th>";
        var Tbl = sortable ? "<table class=\"sort\">" : "<table>";
        sb.Append($"<section class=\"box\"><div class=\"box-head\"><h2>교착</h2><span class=\"t4\">확인 실행 {d.Runs.Count}개 · 합계 {d.Total}회</span></div>");

        // places first: where, how much
        if (d.Spots.Count > 0)
        {
            var number = d.Runs.ToDictionary(r => r.RunId, r => r, StringComparer.Ordinal);
            void Spots(IEnumerable<DeadlockSpot> spots)
            {
                sb.Append("<div class=\"scroll\">").Append(Tbl).Append("<thead><tr>").Append(T("위치")).Append(T("교착(건)")).Append(T("실행(개)")).Append(T("첫 발생(h:mm)")).Append(T("그룹")).Append(T("차량")).Append("<th>지도</th></tr></thead><tbody>");
                foreach (var s in spots)
                {
                    var top = number.GetValueOrDefault(s.TopRun);
                    sb.Append($"<tr><td><b>{H(s.Name)}</b></td><td class=\"num\">{s.Episodes}</td><td class=\"num\">{s.Runs}</td><td class=\"num\">{Hm(s.FirstSec)}</td><td>{H(string.Join("·", s.Groups))}</td><td>{H(Names(s.Vehicles))}</td>" +
                        $"<td>{(top == null ? "-" : RunLink(j, top))}</td></tr>");
                }
                sb.Append("</tbody></table></div>");
            }
            Spots(d.Spots.Take(Shown));
            if (d.Spots.Count > Shown) { sb.Append($"<details><summary class=\"t4\">나머지 위치 {d.Spots.Count - Shown}곳</summary>"); Spots(d.Spots.Skip(Shown)); sb.Append("</details>"); }
            sb.Append("<div class=\"t4\">위치 = 대기하거나 잡고 있던 block(없으면 CP) · 교착(건) = 횟수 · 지도 = 원인 탭에서 가장 많이 난 실행</div>");
        }
        else sb.Append("<p class=\"muted\">위치 기록 없음 (실행 결과가 지워졌거나 이전 모델)</p>");

        // then the runs
        void Runs(IEnumerable<DeadlockRun> runs)
        {
            sb.Append("<div class=\"scroll\">").Append(Tbl).Append("<thead><tr>").Append(T("실행")).Append(T("교착(회)")).Append(T("시나리오(그룹=대수)")).Append("</tr></thead><tbody>");
            foreach (var r in runs) sb.Append($"<tr><td>{RunLink(j, r)}</td><td class=\"num\">{r.Count}</td><td>{H(r.Scenario)}</td></tr>");
            sb.Append("</tbody></table></div>");
        }
        sb.Append("<h3 style=\"margin:12px 0 4px;font-size:14px\">실행별</h3>");
        Runs(d.Runs.Take(Shown));
        if (d.Runs.Count > Shown) { sb.Append($"<details><summary class=\"t4\">나머지 실행 {d.Runs.Count - Shown}개</summary>"); Runs(d.Runs.Skip(Shown)); sb.Append("</details>"); }
        if (d.Capped) sb.Append($"<div class=\"t4\">교착 실행 {d.Runs.Count}개 중 {JobView.DeadlockRunCap}개까지만 위치를 읽었습니다.</div>");
        sb.Append("</section>");
    }
}
