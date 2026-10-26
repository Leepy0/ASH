using System.Text;
using ASH.Core.Runner;
using static ASH.Server.JobPages;

namespace ASH.Server;

/// <summary>The "생산량 한계" box of the 결론 tabs (analysis and job): the limit reached and what fails next, one bar per step
/// (length = production, green passed, red failed, gray not run), and the button that queues or cancels it.</summary>
public static class LimitPage
{
    const string Css = @"
.lim .lim-h{display:flex;flex-wrap:wrap;gap:4px 14px;align-items:baseline;margin:2px 0 8px}.lim .lim-h b{font-size:18px}
.lim .lr{display:grid;grid-template-columns:48px 1fr minmax(90px,auto);align-items:center;gap:8px;min-height:26px;font-size:13px}
.lim .lb{height:10px}.lim .lb i{display:block;height:100%;border-radius:5px;background:var(--gray-b)}
.lim .ok i{background:var(--green)}.lim .no i{background:var(--red)}.lim .no .lt,.lim .lbad{color:var(--red)}
.lim .lact{display:flex;align-items:center;gap:8px;margin-top:8px}.lim form{margin:0}
";

    /// <summary>Appends the box for a finished job with counts; <paramref name="back"/> is where the buttons return to. Reloads the page while it runs.</summary>
    public static void Section(StringBuilder sb, JobStore js, JobInfo j, string back)
    {
        if (j.State != JobState.Done || j.Decided == null) return;
        var info = LimitWorker.Read(js, j.Id);
        var running = LimitWorker.Progress(js, j.Id);
        var state = running ?? (info == null ? "" : info.Error != null ? "오류" : info.Done ? "완료" : "중단");
        sb.Append($"<section class=\"box lim\"><style>{Css}</style><div class=\"box-head\"><h3>생산량 한계</h3><span class=\"t4\" role=\"status\">{H(state)}</span></div>");

        var fail = info?.FirstFail;
        sb.Append("<div class=\"lim-h\">");
        if (info == null || info.Steps.Count == 0) sb.Append($"<span class=\"t4\">결론 대수 · 생산량 +10%씩 · 최대 +{ProductionLimit.Pcts[^1]}%</span>");
        else
        {
            sb.Append(info.Passed is { } p ? $"<b>+{p}%까지 통과</b>" : "<b class=\"lbad\">기준 미통과</b>");
            if (fail != null) sb.Append($"<span class=\"lbad\" title=\"{H(fail.Reason)}\">+{fail.Pct}% {H(fail.Rule)}{(fail.Worst.Length > 0 ? " · " + H(fail.Worst) : "")}</span>");
        }
        if (info?.Error is { } e) sb.Append($"<span class=\"lbad\">{H(e)}</span>");
        sb.Append("</div>");

        var top = ProductionLimit.Pcts[^1];
        foreach (var pct in ProductionLimit.Pcts)
        {
            var s = info?.Steps.FirstOrDefault(x => x.Pct == pct);
            var text = s == null ? "-" : s.Ok ? "통과" : s.Rule + (s.Worst.Length > 0 ? " · " + s.Worst : "");
            sb.Append($"<div class=\"lr{(s == null ? "" : s.Ok ? " ok" : " no")}\"{(s is { Ok: false } ? $" title=\"{H(s.Reason)}\"" : "")}><span>+{pct}%</span>" +
                $"<span class=\"lb\"><i style=\"width:{F(100.0 * (100 + pct) / (100 + top), "0.#")}%\"></i></span><span class=\"lt\">{H(text)}</span></div>");
        }

        var id = H(j.Id);
        var bk = $"<input type=\"hidden\" name=\"back\" value=\"{H(back)}\">";
        sb.Append("<div class=\"lact\">");
        if (running != null) sb.Append($"<form method=\"post\" action=\"/jobs/{id}/limit/cancel\">{bk}<button class=\"btn\">취소</button></form>");
        else sb.Append($"<form method=\"post\" action=\"/jobs/{id}/limit\">{bk}<button class=\"btn{(info == null ? " pri" : "")}\">{(info == null ? "계산" : "다시 계산")}</button></form>");
        sb.Append("<span class=\"t4\">보낸 PC와 관리자만</span></div></section>");
        if (running != null) sb.Append("<script>setTimeout(function(){location.reload();},10000);</script>");
    }
}
