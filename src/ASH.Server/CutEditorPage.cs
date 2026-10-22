using System.Globalization;
using System.Text;
using System.Text.Json;
using ASH.Core.Analysis;
using static ASH.Server.JobPages;

namespace ASH.Server;

/// <summary>/analyses/{id}/cuts: the cut editor. The drawing (one version's drawing.json) on the left, the cut form on the right, the cut list,
/// scene candidates and export below. Everything runs in the page (no other host); the list is saved through PUT /api/analyses/{id}/cuts.</summary>
public static class CutEditorPage
{
    /// <summary>Layouts whose centres are further apart than this share of the layout size get a warning: a cut in metres would film another place.</summary>
    public const double FarShare = 0.2;

    static string Label(AnalysisInfo a, JobInfo j) => (a.Plans.FirstOrDefault(p => p.Id == j.PlanId)?.Name ?? "?") + " v" + j.Version;

    /// <summary>Paths, CPs and blocks of the job's AGV layout (not the DXF); null when the model folder cannot be read.</summary>
    static (double X0, double Y0, double X1, double Y1)? LayoutBounds(JobStore js, JobInfo j)
    {
        if (JobView.For(js, j).Layout is not { } l) return null;
        var pts = l.Paths.SelectMany(p => p.Segments.SelectMany(s => new[] { s.Start, s.End })).Concat(l.Cps.Select(c => c.Position))
            .Concat(l.Blocks.SelectMany(b => new[] { b.Rect.Min, b.Rect.Max })).ToList();
        if (pts.Count == 0) return null;
        return (pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y));
    }

    public static string Render(AnalysisInfo a, IReadOnlyList<JobInfo> all, JobStore js, string? jobId)
    {
        var jobs = all.Where(j => j.AnalysisId == a.Id).OrderBy(j => a.Plans.FindIndex(p => p.Id == j.PlanId)).ThenBy(j => j.Version).ToList();
        var top = $"<div class=\"ce-top\"><h1>컷 편집기</h1><span class=\"crumb\"><a href=\"/analyses\">분석</a> › <a href=\"/analyses/{H(a.Id)}\">{H(a.Name)}</a> › 컷 영상</span>";
        if (jobs.Count == 0)
            return Page("ASH 컷 편집기 " + a.Name, top + "</div><p class=\"muted\">이 분석에는 아직 보낸 작업이 없어 바탕 도면이 없습니다. ZWCAD의 ASH에서 이 분석으로 \"서버로 보내기\"를 하면 편집할 수 있습니다.</p>");

        // default base: the newest selected version, else the newest version
        var baseJob = jobs.FirstOrDefault(j => j.Id == jobId) ?? jobs.Where(j => j.Selected).MaxBy(j => j.CreatedUtc) ?? jobs.MaxBy(j => j.CreatedUtc)!;
        var settings = JobView.For(js, baseJob).Input?.Settings;

        var sb = new StringBuilder(top);
        sb.Append("<form method=\"get\" class=\"mini\"><label for=\"job\">바탕 도면</label><select id=\"job\" name=\"job\" onchange=\"this.form.submit()\">");
        foreach (var j in jobs)
            sb.Append($"<option value=\"{H(j.Id)}\"{(j.Id == baseJob.Id ? " selected" : "")}>{H(Label(a, j))}{(j.Selected ? " (선정)" : "")}</option>");
        sb.Append("</select><noscript><button>바꾸기</button></noscript></form>");
        sb.Append("<span class=\"muted\">컷은 분석에 속하고, 모든 버전에 같은 좌표(m)로 적용됩니다.</span></div>");

        // Review Focus 1: cuts are stored in metres, so a version whose layout sits elsewhere films another place
        if (LayoutBounds(js, baseJob) is { } b0)
        {
            var far = new List<string>();
            foreach (var j in jobs.Where(j => j.Selected && j.Id != baseJob.Id))
            {
                if (LayoutBounds(js, j) is not { } b) continue;
                var size = Math.Max(Math.Max(b0.X1 - b0.X0, b0.Y1 - b0.Y0), Math.Max(b.X1 - b.X0, b.Y1 - b.Y0));
                var d = Math.Sqrt(Math.Pow((b.X0 + b.X1 - b0.X0 - b0.X1) / 2, 2) + Math.Pow((b.Y0 + b.Y1 - b0.Y0 - b0.Y1) / 2, 2));
                if (d > FarShare * size) far.Add($"{H(Label(a, j))}({F(d, "0.0")} m)");
            }
            if (far.Count > 0)
                sb.Append($"<div class=\"msg warn\">레이아웃 위치가 버전마다 다릅니다. 바탕 {H(Label(a, baseJob))}과 비교해 {string.Join(", ", far)}의 레이아웃 중심이 레이아웃 크기의 {FarShare * 100:0}% 넘게 떨어져 있어, " +
                    "같은 좌표의 컷이 그 버전에서는 다른 곳을 찍습니다(자동 보정 없음). ZWCAD에서 Origin을 맞춰 다시 보내거나 그 버전은 따로 찍으세요.</div>");
        }

        var data = new
        {
            analysisId = a.Id, job = baseJob.Id,
            warmup = settings?.WarmupSec ?? 0, simLength = settings?.SimLengthSec ?? 0,
            // export films the selected versions (as the layout warning compares); without any, the base
            versions = (jobs.Any(j => j.Selected) ? jobs.Where(j => j.Selected) : new[] { baseJob })
                .Select(j => new { id = j.Id, label = Label(a, j), decided = j.Decided ?? new Dictionary<string, int>() }),
        };
        sb.Append("<script type=\"application/json\" id=\"cut-data\">").Append(JsonSerializer.Serialize(data)).Append("</script>"); // the default encoder escapes < and >
        var start = settings == null ? "0:00:00" : TimeSpan.FromSeconds(Math.Round(settings.WarmupSec)).ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
        sb.Append(Body.Replace("{START}", start)).Append(Script);
        return Page("ASH 컷 편집기 " + a.Name, sb.ToString());
    }

    const string Body = """
<style>
main{max-width:none;padding:12px 20px}[hidden]{display:none!important}
.noload #scenes button{opacity:.5;pointer-events:none}
.ce-top{display:flex;flex-wrap:wrap;align-items:center;gap:6px 18px;margin-bottom:10px}.ce-top h1{margin:0;font-size:20px}.ce-top .crumb{margin:0}
.ce-top select{width:auto;max-width:260px}.ce-top .mini label{font-size:14px;color:#5f5e5a}
.ce{display:grid;grid-template-columns:minmax(0,1fr) clamp(340px,24vw,430px);gap:12px;align-items:start}
.ce-bottom{display:grid;grid-template-columns:minmax(0,1.4fr) minmax(0,1fr) minmax(0,1fr);gap:12px;margin-top:12px;align-items:start}
.pn{background:#fff;border:1px solid #d3d1c7;border-radius:10px;padding:12px 14px;min-width:0}.pn h2{font-size:15px;margin:0 0 8px}
.stage{position:relative;height:70vh;min-height:320px;overflow:hidden;border:1px solid #d3d1c7;border-radius:8px;background:#fdfdfb;touch-action:none;perspective:1400px;cursor:crosshair;user-select:none}
.stage.flat{perspective:none}
.tilter{position:absolute;inset:0;transform-origin:50% 55%}.tilter canvas{position:absolute;left:0;top:0}
.hud{position:absolute;left:8px;top:8px;right:8px;display:flex;flex-wrap:wrap;gap:6px;z-index:2;pointer-events:none}
.chip{font:600 12px Consolas,monospace;background:#fff;border:1px solid #d3d1c7;border-radius:4px;padding:2px 6px}.chip.warn{background:#faeeda;color:#633806;border-color:#e8c48c}
.tb{display:flex;flex-wrap:wrap;gap:6px;margin-top:8px;align-items:center}.tb .gap{flex:1}
.ce button,.ce-bottom button{border:1px solid #b4b2a9;border-radius:6px;background:#fff;padding:5px 10px;font:inherit;font-size:14px;cursor:pointer;color:#2c2c2a}
.ce button.on{background:#e6f1fb;border-color:#185fa5;color:#0c447c;font-weight:600}
.ce button.primary,.ce-bottom button.primary{background:#1f3a5f;color:#fff;border-color:#1f3a5f;font-weight:600}
.ce button:disabled,.ce-bottom button:disabled{opacity:.5;cursor:default}
.ce button:focus-visible,.ce-bottom button:focus-visible,.ce input:focus-visible{outline:2px solid #185fa5;outline-offset:1px}
.f{display:grid;gap:7px}
.r{display:grid;grid-template-columns:6.2em minmax(0,1fr);align-items:center;gap:8px}.r>label,.r>.l{font-size:13px;color:#5f5e5a}
.in{display:flex;gap:6px;align-items:center;min-width:0;flex-wrap:wrap}.in input[type=range]{flex:1 1 90px;min-width:0}
.ce .in input[type=number]{width:5.2em;flex:0 0 auto}.ce .in input[type=text]{flex:1 1 7em;width:auto}
.u{font-size:13px;color:#5f5e5a}.in label{display:flex;gap:4px;align-items:center;font-size:14px}
.seg{display:flex;flex-wrap:wrap;gap:4px}.seg button{padding:4px 9px}
.calc{font:13px Consolas,monospace;background:#f1efe8;border-radius:6px;padding:6px 8px}
.ce fieldset{border:1px dashed #d3d1c7;border-radius:6px;padding:6px 8px;margin:0;display:grid;gap:6px;min-width:0}.ce legend{font-size:12px;color:#5f5e5a}
.st{font-size:13px;min-height:1.4em}.st.bad{color:#a32d2d}.st.ok{color:#3b6d11}
ol.cl{list-style:none;margin:0;padding:0;display:grid;gap:6px}
.it{display:grid;grid-template-columns:auto auto minmax(0,1fr) auto;gap:8px;align-items:center;border:1px solid #d3d1c7;border-radius:6px;padding:6px 8px}
.it.sel{border-color:#185fa5;background:#e6f1fb}.it .t{font-weight:600;font-size:14px;overflow-wrap:anywhere}.it .d{font:12px Consolas,monospace;color:#5f5e5a;overflow-wrap:anywhere}
.it .no{font:600 12px Consolas,monospace;color:#888780}.acts{display:flex;gap:4px;flex-wrap:wrap;justify-content:flex-end}.ce-bottom .acts button{padding:2px 7px;font-size:12px}
.tot{display:flex;justify-content:space-between;flex-wrap:wrap;gap:8px;font:13px Consolas,monospace;border-top:1px solid #d3d1c7;padding-top:8px;margin-top:8px}
.sc{display:grid;gap:6px}.ce-bottom .sc button{text-align:left;display:grid;gap:2px}.sc b{font-size:14px}.sc small{font:12px Consolas,monospace;color:#5f5e5a}
.k-Stockout{border-left:4px solid #a32d2d!important}.k-DelaySpike,.k-Bottleneck{border-left:4px solid #ba7517!important}.k-Whole{border-left:4px solid #3b6d11!important}
.layers{position:relative}.layers>summary{margin:0;list-style:none;border:1px solid #b4b2a9;border-radius:6px;padding:5px 10px;font-size:14px;color:#2c2c2a;background:#fff}
.layers .pop{position:absolute;bottom:115%;right:0;z-index:5;background:#fff;border:1px solid #d3d1c7;border-radius:8px;padding:8px 10px;max-height:50vh;overflow:auto;min-width:220px;box-shadow:0 4px 14px rgba(0,0,0,.12)}
.layers label,.vers label{display:flex;gap:6px;align-items:center;font-size:14px;white-space:nowrap}
.ce-bottom .r select{width:100%}
@media (min-width:1001px){#form{max-height:calc(70vh + 46px);overflow:auto}}
@media (max-width:1000px){.ce,.ce-bottom{grid-template-columns:minmax(0,1fr)}.stage{height:58vh}main{padding:10px 12px}}
@media (max-width:640px){.it{grid-template-columns:auto auto minmax(0,1fr)}.it .acts{grid-column:1/-1}}
</style>
<div class="ce">
 <section class="pn" aria-label="도면">
  <div class="stage" id="stage">
   <div class="hud"><span class="chip" id="hudTilt">기울기 0°</span><span class="chip" id="hudDir">방향 북</span><span class="chip" id="hudPersp">원근 켬</span><span class="chip" id="hudFlat" hidden>미리보기 끔(휠을 돌리면 다시 켜짐)</span><span class="chip warn" id="hudNote" hidden></span></div>
   <div class="tilter" id="tilter"><canvas id="bg"></canvas><canvas id="fg"></canvas></div>
  </div>
  <div class="tb" role="toolbar" aria-label="도면 도구">
   <button type="button" id="mA" class="on" aria-pressed="true">A 그리기</button><button type="button" id="mB" aria-pressed="false">B 그리기</button><button type="button" id="mPan" aria-pressed="false">이동(손)</button>
   <span class="gap"></span>
   <button type="button" id="zIn" aria-label="확대">＋</button><button type="button" id="zOut" aria-label="축소">－</button><button type="button" id="zFit">맞춤</button><button type="button" id="flat">평면 보기</button>
   <details class="layers"><summary>레이어</summary><div class="pop" id="layers"></div></details>
  </div>
 </section>
 <section class="pn f" id="form" aria-label="컷 설정">
  <h2 id="formTitle">새 컷</h2>
  <div class="r"><label for="name">이름</label><input type="text" id="name" maxlength="60" placeholder="예: 조립라인 입구 대기열"></div>
  <div class="r"><span class="l">종류</span><div class="seg" role="group" aria-label="컷 종류"><button type="button" data-k="Hold" class="on" aria-pressed="true">구역 고정</button><button type="button" data-k="Move" aria-pressed="false">A → B 이동</button><button type="button" data-k="Whole" aria-pressed="false">전체</button></div></div>
  <div class="r"><label for="start">시작 시각</label><div class="in"><input type="text" id="start" value="{START}" inputmode="numeric" aria-describedby="startEnd"><span class="u" id="startEnd"></span></div></div>
  <fieldset id="holdSet"><legend>촬영 시간(시뮬레이션 초)</legend>
   <div class="r"><label for="dur">길이</label><div class="in"><input type="number" id="dur" min="1" value="600"><span class="u">초</span></div></div></fieldset>
  <fieldset id="moveSet" hidden><legend>A → B 이동(시뮬레이션 초)</legend>
   <div class="r"><label for="holdA">A에서</label><div class="in"><input type="number" id="holdA" min="0" value="120"><span class="u">초</span></div></div>
   <div class="r"><label for="moveS">이동</label><div class="in"><input type="number" id="moveS" min="0" value="60"><span class="u">초</span></div></div>
   <div class="r"><label for="holdB">B에서</label><div class="in"><input type="number" id="holdB" min="0" value="120"><span class="u">초</span></div></div></fieldset>
  <div class="r"><label for="speedN">배속</label><div class="in"><input type="range" id="speed" min="0" max="1000" aria-label="배속 슬라이더"><input type="number" id="speedN" min="1" max="600" value="10"><span class="u">배</span></div></div>
  <div class="r"><label for="tiltN">기울기</label><div class="in"><input type="range" id="tilt" min="0" max="70" value="0" aria-label="기울기 슬라이더"><input type="number" id="tiltN" min="0" max="70" value="0"><span class="u">°</span></div></div>
  <div class="r"><label for="dirN">방향</label><div class="in"><span class="seg" id="dirs" role="group" aria-label="화면 위쪽 방향"><button type="button" data-d="0">북</button><button type="button" data-d="90">동</button><button type="button" data-d="180">남</button><button type="button" data-d="270">서</button></span><input type="number" id="dirN" min="0" max="359" value="0" aria-label="방향 각도"><span class="u">°</span></div></div>
  <div class="r"><span class="l">보기</span><div class="in"><label><input type="checkbox" id="persp" checked> 원근</label><label><input type="checkbox" id="cap" checked> 자막</label></div></div>
  <div class="r"><label for="vd">대수 변경</label><div class="in"><input type="number" id="vd" min="-20" max="20" value="0"><span class="u">대(0 = 결론 대수)</span></div></div>
  <div class="calc" id="calc" aria-live="polite"></div>
  <div class="tb" style="margin-top:0"><button type="button" class="primary" id="save">리스트에 추가</button><button type="button" id="cancel" hidden>편집 취소</button></div>
  <div class="st" id="msg" role="status"></div><div><button type="button" id="reload" hidden>컷 리스트 다시 불러오기</button></div>
  <p class="help" style="margin:0">도면을 끌어 구역을 그리고, 그린 구역은 끌어서 옮기거나 모서리로 크기를 바꿉니다. 휠 = 기울기, Ctrl+휠 = 확대. 방향은 화면 위쪽이 가리키는 쪽(0° = 북, +Y).</p>
 </section>
</div>
<div class="ce-bottom">
 <section class="pn" aria-label="컷 리스트"><h2>컷 리스트</h2><ol class="cl" id="list"></ol><div class="tot" id="total"></div></section>
 <section class="pn" aria-label="장면 후보"><h2>장면 후보</h2>
  <div class="tb" style="margin:0 0 8px"><select id="sd" aria-label="후보를 찾은 대수" style="width:auto"><option value="0">결론 대수</option><option value="-1">결론 −1대</option><option value="-2">결론 −2대</option><option value="-3">결론 −3대</option></select><button type="button" id="sfind">이 대수로 찾기</button></div>
  <div class="sc" id="scenes"></div><p class="help">누르면 그 시각·위치로 컷이 추가됩니다(구역 32×18 m, 영상 약 30초).</p></section>
 <section class="pn f" aria-label="내보내기"><h2>내보내기</h2>
  <div class="r"><span class="l">버전</span><div class="vers" id="vers"></div></div>
  <div class="r"><label for="res">해상도</label><select id="res"><option value="1280x720">1280 × 720</option><option value="1920x1080" selected>1920 × 1080</option></select></div>
  <div class="r"><label for="fps">프레임</label><select id="fps"><option value="15">15 fps</option><option value="30" selected>30 fps</option></select></div>
  <div class="r"><label for="out">파일</label><select id="out"><option value="each+joined">컷마다 따로 + 이어 붙인 1개</option><option value="joined">이어 붙인 1개만</option><option value="each">컷마다 따로</option></select></div>
  <div class="r"><span class="l">자막</span><div class="in"><label><input type="checkbox" id="capAll" checked> 컷 이름·시각·결론 수치</label></div></div>
  <div class="r"><label for="vdAll">대수 변경</label><div class="in"><input type="number" id="vdAll" min="-20" max="20" value="0" style="width:5.2em"><span class="u">대(모든 컷에 더함)</span></div></div>
  <div class="r"><span class="l">같은 대수</span><div><label class="in"><input type="checkbox" id="same"> 모든 버전 같은 대수(그룹별)</label><div class="in" id="sameBox" hidden></div></div></div>
  <div class="r"><label for="seed">시드</label><div class="in"><input type="number" id="seed" min="0" step="1" placeholder="비우면 기본" style="width:8em"><span class="u">기본 = 각 버전의 첫 시드(SeedBase), 모든 버전에 같은 값</span></div></div>
  <div><button type="button" class="primary" id="run">체크한 컷으로 영상 만들기</button></div>
  <div class="st" id="runMsg" role="status"></div></section>
</div>
""";

    const string Script = """
<script>
(function(){
'use strict';
var D=JSON.parse(document.getElementById('cut-data').textContent);
function $(id){return document.getElementById(id);}
function enc(s){return encodeURIComponent(s);}
var api='/api/analyses/'+enc(D.analysisId)+'/cuts';
var stage=$('stage'),tilter=$('tilter'),bg=$('bg'),fg=$('fg');
var W=0,H=0,dpr=1,view={cx:0,cy:0,s:10},dw=null,layerOn=[],show={paths:true,cps:true,texts:true};
var zones={A:null,B:null},mode='A',kind='Hold',tilt=0,dir=0,persp=true,preview=true,speed=10;
var cuts=[],loaded=false,editId=null,skip={},drag=null,pollT=null; // loaded: never save over a list we have not read
var KN={Hold:'구역 고정',Move:'A→B 이동',Whole:'전체'},COL={A:'#d9822b',B:'#7a4fbf'};

function hms(s){s=Math.max(0,Math.round(+s||0));var h=Math.floor(s/3600),m=Math.floor(s%3600/60),x=s%60;return h+':'+(m<10?'0':'')+m+':'+(x<10?'0':'')+x;}
function dur(s){s=Math.max(0,Math.round(s));return s>=3600?hms(s):Math.floor(s/60)+':'+(s%60<10?'0':'')+s%60;}
function parseT(t){t=String(t).trim();if(!t)return NaN;var p=t.split(':');if(p.length>3)return NaN;var s=0;for(var i=0;i<p.length;i++){if(!/^\d+(\.\d+)?$/.test(p[i]))return NaN;s=s*60+(+p[i]);}return s;}
function r2(v){return Math.round(v*100)/100;}
function num(id){var v=+$(id).value;return isFinite(v)?v:0;}
function dirName(d){return {0:'북',90:'동',180:'남',270:'서'}[d]||d+'°';}
function say(t,bad){var m=$('msg');m.textContent=t;m.className='st'+(bad?' bad':'');}
function el(tag,cls,text){var e=document.createElement(tag);if(cls)e.className=cls;if(text!=null)e.textContent=text;return e;}

// ---- drawing: model metres -> screen pixels (Y up)
function sx(x){return (x-view.cx)*view.s+W/2;} function sy(y){return H/2-(y-view.cy)*view.s;}
function mx(px){return (px-W/2)/view.s+view.cx;} function my(py){return view.cy-(py-H/2)/view.s;}
function fit(b){if(!b)return;var w=Math.max(b[2]-b[0],1),h=Math.max(b[3]-b[1],1);view.cx=(b[0]+b[2])/2;view.cy=(b[1]+b[3])/2;view.s=Math.min(W/w,H/h)*0.9;}
function resize(){var r=stage.getBoundingClientRect();W=r.width;H=r.height;dpr=window.devicePixelRatio||1;
  [bg,fg].forEach(function(c){c.width=Math.round(W*dpr);c.height=Math.round(H*dpr);c.style.width=W+'px';c.style.height=H+'px';});drawAll();}
function drawAll(){drawBg();drawFg();}
function drawBg(){
  var c=bg.getContext('2d');c.setTransform(dpr,0,0,dpr,0,0);c.clearRect(0,0,W,H);if(!dw)return;
  c.lineWidth=1;c.strokeStyle='#8f9a94';c.beginPath();
  for(var i=0,s;i<dw.segs.length;i++){s=dw.segs[i];if(!layerOn[s[4]])continue;c.moveTo(sx(s[0]),sy(s[1]));c.lineTo(sx(s[2]),sy(s[3]));}
  c.stroke();
  if(show.texts){c.fillStyle='#6b7570';dw.texts.forEach(function(t){if(!layerOn[t[4]])return;var h=t[2]*view.s;if(h<7)return;c.font=Math.min(h,48)+'px "Malgun Gothic",sans-serif';c.fillText(t[3],sx(t[0]),sy(t[1]));});}
  c.setLineDash([4,3]);c.strokeStyle='#b4b2a9';dw.blocks.forEach(function(b){var r=b.rect;c.strokeRect(sx(r[0]),sy(r[3]),(r[2]-r[0])*view.s,(r[3]-r[1])*view.s);});c.setLineDash([]);
  if(show.paths){c.strokeStyle='#8a63d2';c.lineWidth=2;c.beginPath();dw.paths.forEach(function(p){p.pts.forEach(function(q,i){if(i)c.lineTo(sx(q[0]),sy(q[1]));else c.moveTo(sx(q[0]),sy(q[1]));});});c.stroke();}
  if(show.cps){var lbl=view.s>6;c.font='11px Consolas,monospace';c.lineWidth=1.5;c.strokeStyle='#8a63d2';
    dw.cps.forEach(function(p){var x=sx(p.x),y=sy(p.y);c.fillStyle='#fff';c.beginPath();c.arc(x,y,3.5,0,7);c.fill();c.stroke();if(lbl){c.fillStyle='#5a3fa0';c.fillText(p.name,x+5,y-5);}});}
}
function corners(z){return [[z.x,z.y],[z.x+z.w,z.y],[z.x,z.y+z.h],[z.x+z.w,z.y+z.h]];}
function niceLen(v){var p=Math.pow(10,Math.floor(Math.log10(v))),n=v/p;return +((n<2?1:n<5?2:5)*p).toPrecision(2);}
function drawFg(){
  var c=fg.getContext('2d');c.setTransform(dpr,0,0,dpr,0,0);c.clearRect(0,0,W,H);
  ['A','B'].forEach(function(k){var z=zones[k];if(!z||(k==='B'&&kind!=='Move'))return;
    var x=sx(z.x),y=sy(z.y+z.h),w=z.w*view.s,h=z.h*view.s;
    c.globalAlpha=.14;c.fillStyle=COL[k];c.fillRect(x,y,w,h);c.globalAlpha=1;
    c.strokeStyle=COL[k];c.lineWidth=2.5;c.setLineDash([8,5]);c.strokeRect(x,y,w,h);c.setLineDash([]);
    c.font='bold 15px "Malgun Gothic",sans-serif';c.fillStyle=COL[k];c.fillText(k+'  '+z.w.toFixed(1)+' × '+z.h.toFixed(1)+' m',x+6,y+18);
    if(k===mode&&kind!=='Whole'){c.fillStyle='#fff';c.lineWidth=1.5;corners(z).forEach(function(p){c.fillRect(sx(p[0])-4,sy(p[1])-4,8,8);c.strokeRect(sx(p[0])-4,sy(p[1])-4,8,8);});}
  });
  var m=niceLen(110/view.s);c.fillStyle='#2c2c2a';c.fillRect(12,H-14,m*view.s,3);c.font='12px Consolas,monospace';c.fillText(m+' m',12,H-20);
}

// ---- view: tilt / direction preview (CSS 3D, approximate)
function applyView(){
  var on=preview&&(tilt>0||dir!==0);
  tilter.style.transform=on?'rotateX('+tilt+'deg) rotateZ('+(-dir)+'deg)':'none';
  stage.classList.toggle('flat',!persp);
  $('hudTilt').textContent='기울기 '+tilt+'°';$('hudDir').textContent='방향 '+dirName(dir);$('hudPersp').textContent=persp?'원근 켬':'원근 끔';
  $('hudFlat').hidden=preview||!(tilt>0||dir!==0);
}
function setPreview(on){preview=on;applyView();}
function setTilt(v){tilt=Math.max(0,Math.min(70,Math.round(+v||0)));$('tilt').value=tilt;$('tiltN').value=tilt;applyView();calc();}
function setDir(v){v=Math.round(+v||0);dir=((v%360)+360)%360;$('dirN').value=dir;
  Array.prototype.forEach.call($('dirs').children,function(b){var on=+b.dataset.d===dir;b.classList.toggle('on',on);b.setAttribute('aria-pressed',on);});applyView();}
function setSpeed(v){speed=Math.max(1,Math.min(600,Math.round(+v||1)));$('speedN').value=speed;$('speed').value=Math.round(1000*Math.log(speed)/Math.log(600));calc();}
function layoutRect(){
  if(!dw)return null;var x0=1e18,y0=1e18,x1=-1e18,y1=-1e18;function P(x,y){x0=Math.min(x0,x);y0=Math.min(y0,y);x1=Math.max(x1,x);y1=Math.max(y1,y);}
  dw.paths.forEach(function(p){p.pts.forEach(function(q){P(q[0],q[1]);});});dw.cps.forEach(function(c){P(c.x,c.y);});dw.blocks.forEach(function(b){P(b.rect[0],b.rect[1]);P(b.rect[2],b.rect[3]);});
  if(x0>x1){var b=dw.bounds;x0=b[0];y0=b[1];x1=b[2];y1=b[3];}
  return {x:r2(x0),y:r2(y0),w:r2(Math.max(x1-x0,1)),h:r2(Math.max(y1-y0,1))};
}
function setKind(k,keep){kind=k;
  Array.prototype.forEach.call(document.querySelectorAll('[data-k]'),function(b){var on=b.dataset.k===k;b.classList.toggle('on',on);b.setAttribute('aria-pressed',on);});
  $('holdSet').hidden=k==='Move';$('moveSet').hidden=k!=='Move';
  if(k==='Whole'&&(!keep||!zones.A))zones.A=layoutRect(); // an edited Whole cut keeps its saved area
  if(k==='Move'&&!zones.B&&zones.A)zones.B={x:r2(zones.A.x+zones.A.w*1.3),y:zones.A.y,w:zones.A.w,h:zones.A.h};
  if(k!=='Move'&&mode==='B')setMode('A');
  drawFg();calc();}
function setMode(m){mode=m;[['mA','A'],['mB','B'],['mPan','pan']].forEach(function(p){var b=$(p[0]),on=p[1]===m;b.classList.toggle('on',on);b.setAttribute('aria-pressed',on);});
  if(m==='B'&&kind!=='Move')setKind('Move');stage.style.cursor=m==='pan'?'grab':'crosshair';drawFg();}

// ---- pointer: draw / move / resize a zone, or pan
function local(e){var r=stage.getBoundingClientRect();return {x:e.clientX-r.left,y:e.clientY-r.top};}
stage.addEventListener('pointerdown',function(e){
  if(e.button===2)return;
  if(preview&&(tilt>0||dir!==0))setPreview(false); // drawing needs the flat plan; the cut keeps its tilt
  var p=local(e);stage.setPointerCapture(e.pointerId);
  if(mode==='pan'||e.button===1){drag={t:'pan',x:p.x,y:p.y,cx:view.cx,cy:view.cy};return;}
  if(kind==='Whole'){say('전체 컷은 구역을 레이아웃 전체로 자동으로 잡습니다. 구역을 그리려면 종류를 바꾸세요.');return;}
  var k=mode,z=zones[k],X=mx(p.x),Y=my(p.y),tol=9/view.s;
  if(z){var cs=corners(z);for(var i=0;i<4;i++)if(Math.abs(cs[i][0]-X)<tol&&Math.abs(cs[i][1]-Y)<tol){drag={t:'size',k:k,fx:cs[3-i][0],fy:cs[3-i][1],prev:z};return;}
    if(X>=z.x&&X<=z.x+z.w&&Y>=z.y&&Y<=z.y+z.h){drag={t:'move',k:k,ox:X-z.x,oy:Y-z.y};return;}}
  drag={t:'size',k:k,fx:X,fy:Y,prev:z};
});
stage.addEventListener('pointermove',function(e){if(!drag)return;var p=local(e);
  if(drag.t==='pan'){view.cx=drag.cx-(p.x-drag.x)/view.s;view.cy=drag.cy+(p.y-drag.y)/view.s;drawAll();return;}
  var X=mx(p.x),Y=my(p.y),z=zones[drag.k];
  zones[drag.k]=drag.t==='move'?{x:X-drag.ox,y:Y-drag.oy,w:z.w,h:z.h}:{x:Math.min(drag.fx,X),y:Math.min(drag.fy,Y),w:Math.abs(X-drag.fx),h:Math.abs(Y-drag.fy)};
  drawFg();calc();});
function endDrag(){if(drag&&drag.t==='size'){var z=zones[drag.k];if(!z||z.w*view.s<6||z.h*view.s<6)zones[drag.k]=drag.prev||null;}drag=null;drawFg();calc();}
stage.addEventListener('pointerup',endDrag);stage.addEventListener('pointercancel',endDrag);
function zoom(f,p){p=p||{x:W/2,y:H/2};var X=mx(p.x),Y=my(p.y);view.s=Math.max(1e-4,Math.min(1e4,view.s*f));view.cx=X-(p.x-W/2)/view.s;view.cy=Y+(p.y-H/2)/view.s;drawAll();}
stage.addEventListener('wheel',function(e){e.preventDefault();
  if(e.ctrlKey){zoom(e.deltaY<0?1.25:0.8,preview&&(tilt>0||dir!==0)?null:local(e));return;}
  setTilt(tilt+(e.deltaY>0?3:-3));setPreview(true);},{passive:false});
$('zIn').onclick=function(){zoom(1.25);};$('zOut').onclick=function(){zoom(0.8);};$('zFit').onclick=function(){if(dw){fit(dw.bounds);drawAll();}};
$('flat').onclick=function(){setPreview(!preview);};
$('mA').onclick=function(){setMode('A');};$('mB').onclick=function(){setMode('B');};$('mPan').onclick=function(){setMode('pan');};

// ---- form
Array.prototype.forEach.call(document.querySelectorAll('[data-k]'),function(b){b.onclick=function(){setKind(b.dataset.k);};});
Array.prototype.forEach.call($('dirs').children,function(b){b.onclick=function(){setDir(b.dataset.d);setPreview(true);};});
$('dirN').onchange=function(){setDir(this.value);setPreview(true);};
$('tilt').oninput=function(){setTilt(this.value);setPreview(true);};$('tiltN').onchange=function(){setTilt(this.value);setPreview(true);};
$('speed').oninput=function(){setSpeed(Math.pow(600,this.value/1000));};$('speedN').onchange=function(){setSpeed(this.value);};
$('persp').onchange=function(){persp=this.checked;applyView();};
['dur','holdA','moveS','holdB','start'].forEach(function(id){$(id).oninput=calc;});
function simSec(){return kind==='Move'?num('holdA')+num('moveS')+num('holdB'):num('dur');}
function calc(){var s=simSec(),z=zones.A,b=zones.B,st=parseT($('start').value);
  $('calc').textContent='시뮬레이션 '+dur(s)+' → 영상 '+dur(s/speed)+(z?' · A '+z.w.toFixed(1)+'×'+z.h.toFixed(1)+' m':' · 구역 A 없음')+(kind==='Move'&&b?' · B '+b.w.toFixed(1)+'×'+b.h.toFixed(1)+' m':'');
  $('startEnd').textContent=isNaN(st)?'시:분:초로 적어 주세요':'~ '+hms(st+s)+(D.warmup?' · 워밍업 '+hms(D.warmup):'')+(D.simLength&&st+s>D.simLength?' · 시뮬레이션 끝('+hms(D.simLength)+')을 넘음':'');}
function rect(z){return z&&{x:r2(z.x),y:r2(z.y),w:r2(z.w),h:r2(z.h)};}
function newId(){return 'c'+Math.random().toString(16).slice(2,10);}
function fromForm(){
  var st=parseT($('start').value);
  if(isNaN(st)){say('시작 시각을 시:분:초로 적어 주세요(예: 2:30:00).',true);return null;}
  if(!zones.A){say('도면 위를 끌어 구역 A를 그려 주세요.',true);return null;}
  if(kind==='Move'&&!zones.B){say('구역 B를 그려 주세요.',true);return null;}
  var mv=kind==='Move';
  return {id:editId||newId(),name:$('name').value.trim()||('컷 '+(cuts.length+1)),kind:kind,startSec:st,holdSec:mv?0:num('dur'),a:rect(zones.A),b:mv?rect(zones.B):null,
    holdASec:mv?num('holdA'):0,moveSec:mv?num('moveS'):0,holdBSec:mv?num('holdB'):0,speed:speed,tiltDeg:tilt,dirDeg:dir,perspective:persp,caption:$('cap').checked,vehicleDelta:Math.round(num('vd'))};
}
// the server checks every list; a rejected list is not kept here either
function commit(next,ok){
  if(!loaded){say('저장된 컷 리스트를 아직 불러오지 못해 저장하지 않았습니다. "컷 리스트 다시 불러오기"를 누르세요.',true);return;}
  say('저장 중…');
  fetch(api,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify(next)}).then(function(r){return r.text().then(function(t){
    if(!r.ok){say(t||('저장하지 못했습니다(HTTP '+r.status+').'),true);return;}
    cuts=JSON.parse(t);if(ok)ok();render();var m=$('msg');m.textContent='저장했습니다 '+new Date().toLocaleTimeString('ko-KR');m.className='st ok';});})
  .catch(function(){say('서버에 연결할 수 없어 저장하지 못했습니다.',true);});}
$('save').onclick=function(){var c=fromForm();if(!c)return;var next=cuts.slice(),i=next.findIndex(function(x){return x.id===c.id;});
  if(i>=0)next[i]=c;else next.push(c);commit(next,function(){cancelEdit();$('name').value='';});};
$('cancel').onclick=function(){cancelEdit();say('');};
function cancelEdit(){editId=null;$('formTitle').textContent='새 컷';$('save').textContent='리스트에 추가';$('cancel').hidden=true;render();}
function edit(i){var c=cuts[i];editId=c.id;$('formTitle').textContent=(i+1)+'번 컷 수정';$('name').value=c.name;$('start').value=hms(c.startSec);
  zones.A=rect(c.a);zones.B=c.b?rect(c.b):null;setKind(c.kind,true);
  if(c.kind!=='Move')$('dur').value=c.holdSec;else{$('holdA').value=c.holdASec;$('moveS').value=c.moveSec;$('holdB').value=c.holdBSec;}
  setSpeed(c.speed);setTilt(c.tiltDeg);setDir(c.dirDeg);persp=!!c.perspective;$('persp').checked=persp;$('cap').checked=!!c.caption;$('vd').value=c.vehicleDelta||0;
  $('save').textContent='수정 저장';$('cancel').hidden=false;setPreview(true);render();calc();say('');
  if(window.innerWidth<=1000)$('form').scrollIntoView({block:'start'});}

// ---- list
function sim(c){return c.kind==='Move'?c.holdASec+c.moveSec+c.holdBSec:c.holdSec;}
function render(){var ol=$('list');ol.textContent='';var all=0,inc=0,n=0;
  cuts.forEach(function(c,i){var v=sim(c)/c.speed,on=!skip[c.id];all+=v;if(on){inc+=v;n++;}
    var li=el('li','it'+(c.id===editId?' sel':''));
    var cb=el('input');cb.type='checkbox';cb.checked=on;cb.setAttribute('aria-label',(i+1)+'번 컷을 영상에 넣기');cb.onchange=function(){if(this.checked)delete skip[c.id];else skip[c.id]=1;render();};
    var body=el('div');body.appendChild(el('div','t',c.name));
    body.appendChild(el('div','d',KN[c.kind]+' · '+hms(c.startSec)+'부터 '+dur(sim(c))+' · '+c.speed+'배 · 기울기 '+c.tiltDeg+'° · '+dirName(c.dirDeg)+(c.perspective?'':' · 원근 끔')+(c.vehicleDelta?' · '+(c.vehicleDelta>0?'+':'')+c.vehicleDelta+'대':'')+' → 영상 '+dur(v)));
    var acts=el('div','acts');
    [['↑','위로',function(){move(i,-1);}],['↓','아래로',function(){move(i,1);}],['수정','수정',function(){edit(i);}],['삭제','삭제',function(){if(confirm('"'+c.name+'" 컷을 지울까요?'))del(i);}]].forEach(function(a){
      var b=el('button',null,a[0]);b.type='button';b.setAttribute('aria-label',(i+1)+'번 컷 '+a[1]);b.onclick=a[2];acts.appendChild(b);});
    li.appendChild(cb);li.appendChild(el('span','no',String(i+1).padStart(2,'0')));li.appendChild(body);li.appendChild(acts);ol.appendChild(li);});
  if(!loaded)ol.appendChild(el('li','muted','저장된 컷 리스트를 불러오지 못했거나 불러오는 중입니다.'));
  else if(!cuts.length)ol.appendChild(el('li','muted','아직 컷이 없습니다. 도면에 구역을 그리고 오른쪽에서 "리스트에 추가"를 누르거나, 장면 후보를 누르세요.'));
  var t=$('total');t.textContent='';t.appendChild(el('span',null,'컷 '+cuts.length+'개 · 영상에 넣을 컷 '+n+'개'));t.appendChild(el('span',null,'영상 합계 '+dur(inc)+(n<cuts.length?' (전체 '+dur(all)+')':'')));}
function move(i,d){var j=i+d;if(j<0||j>=cuts.length)return;var next=cuts.slice();next.splice(j,0,next.splice(i,1)[0]);commit(next);}
function del(i){var next=cuts.slice(),c=next.splice(i,1)[0];commit(next,function(){if(c.id===editId)cancelEdit();});}

// ---- scene candidates (scenes.json from the conclusion run; other counts on request)
var KINDS=['Stockout','DelaySpike','Bottleneck','Whole'],KL={Stockout:'결품',DelaySpike:'지연 급증',Bottleneck:'병목',Whole:'전체'};
function g(o,k){return o[k]!==undefined?o[k]:o[k.charAt(0).toUpperCase()+k.slice(1)];}
function scenesUrl(){return '/api/jobs/'+enc(D.job)+'/scenes?delta='+$('sd').value;}
function loadScenes(){clearTimeout(pollT);var box=$('scenes'),d=+$('sd').value;box.textContent='불러오는 중…';
  fetch(scenesUrl()).then(function(r){return r.ok?r.json():r.text().then(function(t){throw new Error(t);});}).then(function(x){box.textContent='';
    if(Array.isArray(x)){
      if(!x.length){box.appendChild(el('p','muted',d===0?'아직 찾은 장면이 없습니다. 대수 탐색이 끝나면 서버가 결론 대수로 한 번 더 돌려 찾습니다.':'이 대수로 찾은 장면이 없습니다. "이 대수로 찾기"를 누르세요.'));return;}
      x.forEach(function(c){var k=g(c,'kind');if(typeof k==='number')k=KINDS[k];var t=g(c,'title')||KL[k]||String(k);
        var b=el('button','k-'+k);b.type='button';b.appendChild(el('b',null,'+ '+t));
        b.appendChild(el('small',null,(KL[k]||k)+' · '+hms(g(c,'startSec'))+' ~ '+hms(g(c,'endSec'))+(g(c,'detail')?' · '+g(c,'detail'):'')));
        b.onclick=function(){addScene(c,k,t,d);};box.appendChild(b);});
    } else if(x.pending){box.appendChild(el('p','muted',x.pending));pollT=setTimeout(loadScenes,15000);}
    else box.appendChild(el('p','muted',x.none||x.error||'후보를 읽을 수 없습니다.'));
  }).catch(function(e){box.textContent='';box.appendChild(el('p','bad',e.message||'후보를 불러오지 못했습니다.'));});}
function addScene(c,k,t,d){var s=+g(c,'startSec')||0,e=+g(c,'endSec')||s,whole=k==='Whole',len=Math.max(60,Math.round(e-s)),cx=+g(c,'centerX')||0,cy=+g(c,'centerY')||0;
  var a=whole?layoutRect():{x:r2(cx-16),y:r2(cy-9),w:32,h:18};if(!a){say('도면을 불러온 뒤에 다시 누르세요.',true);return;}
  commit(cuts.concat([{id:newId(),name:String(t).slice(0,60),kind:whole?'Whole':'Hold',startSec:Math.max(0,Math.floor(s)),holdSec:len,a:a,b:null,holdASec:0,moveSec:0,holdBSec:0,
    speed:Math.max(1,Math.min(600,Math.round(len/30))),tiltDeg:whole?35:30,dirDeg:0,perspective:true,caption:true,vehicleDelta:d}]));}
$('sd').onchange=loadScenes;
$('sfind').onclick=function(){var box=$('scenes');
  fetch(scenesUrl(),{method:'POST'}).then(function(r){return r.text().then(function(t){
    if(r.status===202){box.textContent='';box.appendChild(el('p','muted','요청했습니다. 작업 대기열에서 차례가 오면 모델을 한 번 돌려 찾습니다(실행 1회 시간). 이 화면은 15초마다 다시 확인합니다.'));pollT=setTimeout(loadScenes,15000);}
    else{box.textContent='';box.appendChild(el('p','bad',t));}});}).catch(function(){box.textContent='서버에 연결할 수 없습니다.';});};

// ---- export (the film endpoint comes with the filming step)
// versions = the selected ones (or the base when none is selected); groups for "same counts" come from their conclusions
var groups={};
D.versions.forEach(function(v){var l=el('label'),cb=el('input');cb.type='checkbox';cb.value=v.id;cb.checked=true;
  l.appendChild(cb);l.appendChild(document.createTextNode(v.label));$('vers').appendChild(l);
  Object.keys(v.decided||{}).forEach(function(g){if(!(g in groups))groups[g]=v.decided[g];});});
Object.keys(groups).forEach(function(g){var l=el('label'),i=el('input');i.type='number';i.min='1';i.max='999';i.value=groups[g];i.dataset.g=g;i.style.width='4.5em';i.setAttribute('aria-label',g+' 대수');
  l.appendChild(document.createTextNode(g));l.appendChild(i);l.appendChild(el('span','u','대'));$('sameBox').appendChild(l);});
if(!Object.keys(groups).length){$('same').disabled=true;$('same').parentNode.appendChild(el('span','u',' (결론 대수가 있는 버전이 없음)'));}
$('same').onchange=function(){$('sameBox').hidden=!this.checked;};
$('run').onclick=function(){var m=$('runMsg');function out(t,bad){m.textContent=t;m.className='st'+(bad?' bad':'');}
  if(!loaded){out('컷 리스트를 불러온 뒤에 만들 수 있습니다.',true);return;}
  var ids=cuts.filter(function(c){return !skip[c.id];}).map(function(c){return c.id;}),vs=Array.prototype.filter.call($('vers').querySelectorAll('input'),function(i){return i.checked;}).map(function(i){return i.value;});
  if(!ids.length){out('영상에 넣을 컷이 없습니다. 컷 리스트에서 하나 이상 체크하세요.',true);return;}
  if(!vs.length){out('찍을 버전을 하나 이상 고르세요.',true);return;}
  var same=null,bad=false;
  if($('same').checked){same={};Array.prototype.forEach.call($('sameBox').querySelectorAll('input'),function(i){var n=+i.value;if(!(n>=1&&n===Math.round(n)))bad=true;same[i.dataset.g]=n;});}
  if(bad){out('같은 대수는 그룹마다 1 이상의 정수로 적어 주세요.',true);return;}
  var sd=$('seed').value.trim(),seed=null;
  if(sd){seed=+sd;if(!(seed>=0&&seed===Math.round(seed))){out('시드는 0 이상의 정수로 적거나 비워 두세요.',true);return;}}
  var res=$('res').value.split('x');
  fetch('/api/analyses/'+enc(D.analysisId)+'/films',{method:'POST',headers:{'Content-Type':'application/json'},
    body:JSON.stringify({cutIds:ids,jobIds:vs,width:+res[0],height:+res[1],fps:+$('fps').value,output:$('out').value,caption:$('capAll').checked,vehicleDelta:Math.round(num('vdAll')),sameCounts:same,seed:seed})})
  .then(function(r){return r.text().then(function(t){
    if(r.status===404||r.status===405){out('영상 촬영은 다음 단계에서 연결됩니다. 컷 리스트는 저장되어 있습니다.');return;}
    if(!r.ok){out(t||('HTTP '+r.status),true);return;}
    location.href='/films/'+enc(JSON.parse(t).id);});}).catch(function(){out('서버에 연결할 수 없습니다.',true);});};

// ---- layers
function layerUi(){var box=$('layers');box.textContent='';
  function add(text,on,f){var l=el('label'),cb=el('input');cb.type='checkbox';cb.checked=on;cb.onchange=function(){f(this.checked);drawBg();};l.appendChild(cb);l.appendChild(document.createTextNode(text));box.appendChild(l);}
  add('AGV 경로',show.paths,function(v){show.paths=v;});add('CP 이름',show.cps,function(v){show.cps=v;});add('도면 글자',show.texts,function(v){show.texts=v;});
  if(!dw.layers.length)box.appendChild(el('p','muted','도면(DXF) 레이어 없음'));
  dw.layers.forEach(function(n,i){add(n,true,function(v){layerOn[i]=v;});});}

// ---- start
setSpeed(10);setDir(0);setTilt(0);setKind('Hold');resize();render();
window.addEventListener('resize',resize);
fetch('/jobs/'+enc(D.job)+'/drawing.json').then(function(r){return r.ok?r.json():r.text().then(function(t){throw new Error(t);});}).then(function(d){
  dw=d;layerOn=d.layers.map(function(){return true;});layerUi();fit(d.bounds);
  var note=d.drawingError||(!d.segs.length?'도면(DXF) 없음 · AGV 경로만 표시':'')+(d.truncated?' 선이 많아 일부만 표시':'');
  if(note){$('hudNote').textContent=note;$('hudNote').hidden=false;}
  drawAll();calc();
}).catch(function(e){$('hudNote').textContent='도면을 불러오지 못했습니다: '+(e.message||'');$('hudNote').hidden=false;});
function setLoaded(on){loaded=on;$('save').disabled=!on;document.body.classList.toggle('noload',!on);$('reload').hidden=on;}
function loadCuts(){setLoaded(false);$('reload').hidden=true;say('컷 리스트를 불러오는 중…');
  fetch(api).then(function(r){return r.text().then(function(t){if(!r.ok)throw new Error(t||('HTTP '+r.status));return JSON.parse(t);});})
  .then(function(x){if(!Array.isArray(x))throw new Error('형식이 다릅니다');cuts=x;setLoaded(true);render();say('');})
  .catch(function(e){say('컷 리스트를 불러오지 못했습니다'+(e&&e.message&&!(e instanceof TypeError)?': '+e.message:'(서버 연결 실패)')+'. 불러오기 전에는 저장하지 않습니다.',true);$('reload').hidden=false;});}
$('reload').onclick=loadCuts;
loadCuts();
loadScenes();
})();
</script>
""";
}
