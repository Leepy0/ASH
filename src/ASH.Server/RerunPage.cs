using System.Globalization;
using System.Text;

namespace ASH.Server;

/// <summary>The "대수 바꿔 다시 돌리기" box of a job's 결론 tab: ±1 steppers on the recommended counts and one button that posts them to /jobs/{id}/rerun.
/// Up to <see cref="ListMax"/> groups get a stepper row each; more groups share one row with a group picker, and the changed ones are listed below it.</summary>
public static class RerunPage
{
    public const int ListMax = 6;

    const string Css = @"
.rr .rr-row{display:flex;align-items:center;gap:8px;min-height:44px;padding:0 8px;border-radius:8px}.rr .rr-row.chg{background:var(--blue-b);font-weight:700}
.rr .rr-g{min-width:90px}.rr select{min-height:34px}.rr .rr-v{min-width:2.5em;text-align:center;font-size:18px;font-weight:700}
.rr .rr-s{width:34px;height:34px;padding:0}.rr .rr-r{color:var(--sub2);font-size:13px}
.rr .rr-sum{margin:8px 0;min-height:20px;font-size:14px;color:var(--blue-d)}.rr .rr-act{display:flex;align-items:center;gap:12px;margin-top:8px}.rr form{margin:0}
";

    const string Script = @"<script>(function(){document.querySelectorAll('form.rr-form').forEach(function(f){
var inp={};f.querySelectorAll('input[data-d]').forEach(function(i){inp[i.dataset.g]=i;});
var go=f.querySelector('button.pri'),sum=f.querySelector('.rr-sum');
function show(){var ch=[];Object.keys(inp).forEach(function(g){var i=inp[g];if(+i.value!==+i.dataset.d)ch.push(g+' '+i.dataset.d+'→'+i.value);});
f.querySelectorAll('.rr-row').forEach(function(r){var s=r.querySelector('select'),g=s?s.value:r.dataset.g,i=inp[g];
r.querySelector('.rr-v').textContent=i.value;r.querySelector('.rr-r').textContent=i.dataset.min+'~'+i.dataset.max+'대';r.classList.toggle('chg',+i.value!==+i.dataset.d);
r.querySelector('.rr-m').disabled=+i.value<=+i.dataset.min;r.querySelector('.rr-p').disabled=+i.value>=+i.dataset.max;});
sum.textContent=ch.length?'변경: '+ch.join(', '):'';go.disabled=!ch.length;}
f.addEventListener('click',function(e){var b=e.target.closest('button.rr-s');if(!b)return;var r=b.closest('.rr-row'),s=r.querySelector('select'),i=inp[s?s.value:r.dataset.g];
i.value=Math.max(+i.dataset.min,Math.min(+i.dataset.max,+i.value+(b.classList.contains('rr-p')?1:-1)));show();});
f.addEventListener('change',function(e){if(e.target.tagName==='SELECT')show();});show();});})();</script>";

    /// <summary>Appends the box for a finished job with counts; nothing when the viewer may not rerun it (same rule as cancelling).</summary>
    public static void Box(StringBuilder sb, JobInfo j, JobView v, bool allowed)
    {
        if (!allowed || j.State != JobState.Done || j.Decided == null || v.Groups.Count == 0) return;
        var gs = v.Groups.Select(g => (g, d: g.Decided ?? g.Min)).ToList();
        var id = JobPages.H(j.Id);
        sb.Append($"<div class=\"box rr\"><style>{Css}</style><h2>대수 바꿔 다시 돌리기</h2>");
        sb.Append($"<form class=\"rr-form\" method=\"post\" action=\"/jobs/{id}/rerun\">");
        foreach (var (g, d) in gs)
            sb.Append($"<input type=\"hidden\" name=\"{Routes.RerunPrefix}{JobPages.H(g.Group)}\" value=\"{d}\" data-g=\"{JobPages.H(g.Group)}\" data-d=\"{d}\" data-min=\"{g.Min}\" data-max=\"{g.Max}\">");
        string Stepper(string label, string attrs) =>
            $"<div class=\"rr-row\"{attrs}>{label}<button type=\"button\" class=\"btn rr-s rr-m\" aria-label=\"1대 줄이기\">−</button><span class=\"rr-v\" aria-live=\"polite\"></span>" +
            "<button type=\"button\" class=\"btn rr-s rr-p\" aria-label=\"1대 늘리기\">+</button><span class=\"rr-r\"></span></div>";
        if (gs.Count <= ListMax)
            foreach (var (g, _) in gs) sb.Append(Stepper($"<span class=\"rr-g\">{JobPages.H(g.Group)}</span>", $" data-g=\"{JobPages.H(g.Group)}\""));
        else
        {
            var opts = string.Concat(gs.Select(x => $"<option value=\"{JobPages.H(x.g.Group)}\">{JobPages.H(x.g.Group)} (권장 {x.d}대)</option>"));
            sb.Append(Stepper($"<label class=\"rr-g\">그룹 고르기 <select aria-label=\"그룹 고르기\">{opts}</select></label>", ""));
        }
        sb.Append("<div class=\"rr-sum\" role=\"status\"></div>");
        sb.Append("<div class=\"rr-act\"><button class=\"btn pri\" disabled title=\"같은 모델·설정으로 이 대수만 한 번 확인 실행합니다(탐색 없음). 새 버전으로 대기열에 들어갑니다.\">확인 실행 보내기</button>");
        sb.Append("<span class=\"t4\">권장 대수에서 시작 · 보낸 PC와 관리자만</span></div></form></div>").Append(Script);
    }
}
