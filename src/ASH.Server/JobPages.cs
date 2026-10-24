using System.Globalization;
using System.Net;
using System.Text;
using ASH.Core;
using ASH.Core.Geometry;

namespace ASH.Server;

/// <summary>The job pages: the queue (/jobs) and one job (/jobs/{id}) with the tabs 결론, 상세, 입력, 다운로드. Plain server-rendered HTML with inline SVG charts;
/// nothing is loaded from another host (intranet).</summary>
public static class JobPages
{
    internal static string H(string? t) => WebUtility.HtmlEncode(t ?? "");
    internal static string F(double d, string fmt = "0.##") => double.IsNaN(d) || double.IsInfinity(d) ? "-" : d.ToString(fmt, CultureInfo.InvariantCulture);
    static string Pct(double d) => double.IsNaN(d) ? "-" : P(d) + "%";
    internal static string Won(decimal d) => d.ToString("N0", CultureInfo.InvariantCulture);
    /// <summary>Investment for the pages: null (shown as "-") when the price table is empty, so Summary and Compare follow one rule.</summary>
    internal static InvestmentView? InvestmentOf(ASH.Core.Input.ProjectInput input, IReadOnlyDictionary<string, int> decided, IReadOnlyDictionary<string, PriceRow> prices) =>
        prices.Count == 0 ? null : JobView.Investment(input, decided, prices);
    internal static string Cost(InvestmentView? inv) => inv is not { AnyPriced: true } ? "-" : Won(inv.Total);
    /// <summary>Percent without the sign, for table cells whose header carries "(%)".</summary>
    internal static string P(double d) => double.IsNaN(d) ? "-" : (d * 100).ToString("0.0", CultureInfo.InvariantCulture);
    internal static string When(DateTime? utc) => utc is { } t ? SeoulTime.Text(t) : "-";
    internal static string Dur(TimeSpan? d) => d is { } x ? (x.TotalHours >= 1 ? $"{(int)x.TotalHours}시간 {x.Minutes}분" : x.TotalMinutes >= 1 ? $"{(int)x.TotalMinutes}분 {x.Seconds}초" : $"{x.Seconds}초") : "-";

    const string Css = @"
:root{--bg:#f5f5f7;--side:#ececee;--card:#fff;--ink:#1d1d1f;--sub:#6e6e73;--sub2:#636366;--line:#e5e5ea;--line2:#d1d1d6;--fill:#e8e8ed;
--blue:#007aff;--blue-d:#0062cc;--blue-b:#e5f0ff;--indigo:#5856d6;--indigo-b:#ececfb;--orange:#c45500;--orange-b:#fff1e0;--green:#248a3d;--green-b:#e3f6e8;--red:#d70015;--red-b:#ffe9e7;--gray:#8e8e93;--gray-b:#f2f2f4;
--navy:#1d1d1f;--link:#0062cc;--muted:#636366;--ok:#248a3d;--bad:#d70015;--chip:#f2f2f4}
*{box-sizing:border-box}
html,body{height:100%}
body{font:14px/1.45 -apple-system,'Segoe UI','Malgun Gothic',sans-serif;margin:0;color:var(--ink);background:var(--bg);display:flex}
a{color:var(--link)}code{font-family:Consolas,monospace;font-size:13px;color:var(--sub2)}
:focus-visible{outline:3px solid var(--blue);outline-offset:2px}
/* sidebar: 60px icons, widens over the page on hover/focus */
.side{position:fixed;left:0;top:0;bottom:0;width:60px;background:var(--side);border-right:1px solid var(--line2);padding:12px 10px;display:flex;flex-direction:column;gap:4px;z-index:50;overflow:hidden;transition:width .15s ease,box-shadow .15s}
.side:hover,.side:focus-within{width:220px;box-shadow:4px 0 18px rgba(0,0,0,.12)}
.side a{display:flex;align-items:center;gap:12px;height:40px;padding:0 6px;border-radius:8px;color:var(--ink);text-decoration:none;white-space:nowrap;font-size:14px}
.side a:hover{background:#dedee2}.side a.on{background:#d9d9de;font-weight:600}
.side a span{opacity:0;transition:opacity .1s}.side:hover a span,.side:focus-within a span{opacity:1}
.side a i{width:28px;height:28px;border-radius:7px;display:grid;place-items:center;flex:none;font-style:normal;color:#fff;font-size:15px;font-weight:700}
.side .logo{font-weight:800;color:var(--blue);font-size:13px;height:32px;margin-bottom:6px}
.side .end{margin-top:auto}
main{flex:1;min-width:0;margin-left:60px;height:100vh;overflow:auto;padding:18px 26px 22px}
main.fillpage{overflow:hidden;display:flex;flex-direction:column}
.fill{flex:1;min-height:0;display:flex;flex-direction:column}
h1{font-size:24px;font-weight:700;margin:0 0 14px}h2{font-size:16px;font-weight:700;margin:22px 0 10px}h3{font-size:15px;font-weight:700;margin:0 0 8px}
.head{display:flex;align-items:center;gap:12px;flex-wrap:wrap;margin-bottom:14px}.head h1{margin:0}.head .sp{flex:1}
/* type scale */
.t1{font-size:44px;font-weight:700;line-height:1.1;font-variant-numeric:tabular-nums}.t1 small{font-size:18px;font-weight:700}
.t2{font-size:19px;font-weight:700}.t3{font-size:14px;color:var(--sub2)}.t4{font-size:13px;color:var(--sub2)}
/* surfaces */
.box{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:16px 18px;margin-top:14px;min-width:0}.box>h2:first-child,.box>h3:first-child{margin-top:0}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:14px;margin-top:14px}.grid .box{margin-top:0}
.grid2{display:grid;grid-template-columns:1fr 1fr;gap:14px}
.box-head{display:flex;justify-content:space-between;align-items:center;gap:12px;flex-wrap:wrap;margin-bottom:8px}.box-head h2,.box-head h3{margin:0}
.card{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:16px;text-align:center;display:flex;flex-direction:column;align-items:center;min-width:0}
.card.hot{border:2px solid var(--c,var(--blue))}
.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:12px}
.card .big{font-size:32px;font-weight:700;line-height:1.15}.card .sub{color:var(--sub2);font-size:13px}
.kpis{display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:12px}
.kpi{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:14px;text-align:center}.kpi .t1{color:var(--c,var(--ink))}
/* lanes (home) */
.lanes{flex:1;min-height:0;display:grid;grid-auto-flow:column;grid-auto-columns:minmax(260px,1fr);gap:14px}
.lane{display:flex;flex-direction:column;min-height:0;background:rgba(255,255,255,.55);border:1px solid var(--line);border-radius:16px;overflow:hidden}
.lane-h{display:flex;align-items:center;justify-content:center;gap:8px;min-height:50px;border-top:4px solid var(--c);background:#fff;border-bottom:1px solid var(--line)}
.lane-h b{font-size:16px}.lane-h .cnt{min-width:24px;height:22px;border-radius:11px;background:var(--cb);color:var(--c);font-weight:700;font-size:13px;display:grid;place-items:center;padding:0 7px}.lane-h small{color:var(--sub2);font-size:13px}
.lane-b{flex:1;min-height:0;overflow:auto;padding:12px;display:flex;flex-direction:column;gap:12px}
.c-blue{--c:var(--blue);--cb:var(--blue-b)}.c-indigo{--c:var(--indigo);--cb:var(--indigo-b)}.c-orange{--c:var(--orange);--cb:var(--orange-b)}.c-green{--c:var(--green);--cb:var(--green-b)}.c-red{--c:var(--red);--cb:var(--red-b)}.c-gray{--c:var(--sub2);--cb:var(--gray-b)}
.empty{border:1.5px dashed var(--line2);border-radius:12px;padding:14px;text-align:center;color:var(--sub2);font-size:13px}
/* controls */
.segc{display:inline-flex;background:#e3e3e8;border-radius:8px;padding:2px;gap:2px}.segc a,.segc button,.segc span{height:28px;padding:0 14px;display:flex;align-items:center;gap:4px;border-radius:6px;font-size:13px;font-weight:500;color:var(--ink);text-decoration:none;border:0;background:none;font-family:inherit;cursor:pointer}
.segc .on{background:#fff;box-shadow:0 1px 3px rgba(0,0,0,.15);font-weight:600}.segc em{font-style:normal;color:var(--sub2);font-size:12px}
.btn,button,.button{display:inline-flex;align-items:center;justify-content:center;gap:6px;min-height:34px;padding:0 14px;border-radius:8px;background:var(--fill);color:var(--blue-d);font:600 14px -apple-system,'Segoe UI','Malgun Gothic',sans-serif;border:0;cursor:pointer;text-decoration:none}
.btn.pri,button.primary,.button.pri{background:var(--blue);color:#fff}.btn.danger{background:var(--red-b);color:var(--red)}
.btn:disabled,button:disabled,.btn.dis{background:var(--fill);color:var(--gray);cursor:default}
.acts,.actions{display:flex;gap:8px;align-items:center;flex-wrap:wrap;justify-content:center;margin-top:12px}.actions{justify-content:flex-start}
input[type=text],input[type=number],input[type=password],input[type=search],input[type=date],select,textarea{font:inherit;min-height:34px;padding:5px 9px;border:1px solid var(--line2);border-radius:8px;width:100%;max-width:420px;background:#fff;color:var(--ink)}
textarea{max-width:none}
.tag{display:inline-flex;align-items:center;gap:4px;height:24px;padding:0 10px;border-radius:12px;font-size:13px;font-weight:600;background:var(--cb,var(--gray-b));color:var(--c,var(--sub2))}
.tag.blue{--c:var(--blue-d);--cb:var(--blue-b)}.tag.indigo{--c:var(--indigo);--cb:var(--indigo-b)}.tag.orange{--c:var(--orange);--cb:var(--orange-b)}.tag.green{--c:var(--green);--cb:var(--green-b)}.tag.red{--c:var(--red);--cb:var(--red-b)}.tag.gray{--c:var(--sub2);--cb:var(--gray-b)}
.tag.rs{--c:var(--orange);--cb:var(--orange-b)}
.warn{display:inline-flex;align-items:center;gap:6px;background:var(--red-b);color:var(--red);border-radius:8px;min-height:32px;padding:0 12px;font-weight:600;font-size:13px}
.grp{display:flex;width:100%;margin-top:10px;background:var(--bg);border-radius:10px;padding:7px 0}.grp>div{flex:1;border-left:1px solid var(--line);text-align:center}.grp>div:first-child{border-left:0}
.grp b{display:block;font-size:16px}.grp small{font-size:13px;color:var(--sub2)}.grp .bad b{color:var(--red)}
.pv,.prog{height:8px;border-radius:4px;background:#e5e5ea;width:100%;overflow:hidden}.pv i,.prog i{display:block;height:100%;border-radius:4px;background:var(--c,var(--blue))}
.row{display:flex;align-items:center;gap:10px;background:#fff;border:1px solid var(--line);border-radius:12px;padding:10px 12px;min-height:44px;text-align:left;color:var(--ink);text-decoration:none}
.row .t{flex:1;min-width:0}.row .t>div{font-weight:600}.row .t small{font-size:13px;color:var(--sub2)}
/* legacy pieces kept working on every page */
.crumb{font-size:13px;color:var(--sub2);margin:0 0 6px}.crumb a{color:var(--sub2)}
dl.kv{display:grid;grid-template-columns:max-content 1fr;gap:6px 14px;margin:0;font-size:14px}dl.kv dt{color:var(--sub2)}dl.kv dd{margin:0;overflow-wrap:anywhere}
details summary{cursor:pointer;color:var(--blue-d);font-size:14px;margin-top:8px}
.msg{padding:10px 14px;border-radius:10px;margin:10px 0;font-size:14px}.msg.ok{background:var(--green-b);color:#1c5c2e}.msg.bad{background:var(--red-b);color:#8f000e}.msg.warn{background:var(--orange-b);color:#7a3500}
.field{display:grid;grid-template-columns:200px 1fr;gap:4px 16px;padding:10px 0;border-top:1px solid var(--line)}.field:first-of-type{border-top:none}
.field label{font-weight:600;font-size:14px;padding-top:6px}.help{font-size:13px;color:var(--sub2);margin-top:4px}
.scroll{overflow-x:auto;max-width:100%}.scroll td,.scroll th{white-space:nowrap}.grid>*{min-width:0}
table{border-collapse:collapse;background:#fff;width:auto}td,th{border-bottom:1px solid var(--line);padding:8px 10px;text-align:center;font-size:14px}
th{color:var(--sub2);font-weight:600;font-size:13px;background:#fafafa;border-bottom:1px solid var(--line2)}
.num{text-align:center;font-variant-numeric:tabular-nums}td.num,th.num{text-align:center}.muted{color:var(--sub2)}.bad{color:var(--red)}.ok{color:var(--green)}
.tabs{display:flex;gap:2px;background:#e3e3e8;border-radius:8px;padding:2px;width:max-content;margin:6px 0 12px}
.tabs a{height:28px;padding:0 14px;display:flex;align-items:center;border-radius:6px;font-size:13px;font-weight:500;color:var(--ink);text-decoration:none}.tabs a.on{background:#fff;box-shadow:0 1px 3px rgba(0,0,0,.15);font-weight:600}
.panel{background:transparent}
.admin-tabs{display:flex;align-items:center;gap:8px}.admin-tabs form{margin-left:auto}
.settings-grid .field,.fgrid .field{grid-template-columns:1fr;gap:2px}.settings-grid .field label{padding-top:0}
.fgrid{display:grid;grid-template-columns:repeat(auto-fill,minmax(170px,1fr));gap:0 14px}.fgrid .field>span{display:block}.fgrid .field input:not([type=checkbox]),.fgrid .field select{width:100%}
.bar{background:#e5e5ea;height:10px;border-radius:5px;position:relative;width:180px;display:inline-block;vertical-align:middle}.bar i{display:block;height:10px;border-radius:5px;background:var(--blue)}
.bar b{position:absolute;top:-3px;bottom:-3px;width:2px;background:var(--ink);opacity:.55}
.state{display:inline-flex;align-items:center;height:24px;padding:0 10px;border-radius:12px;font-size:13px;font-weight:600}
.s-queued{background:var(--gray-b);color:var(--sub2)}.s-running{background:var(--blue-b);color:var(--blue-d)}.s-done{background:var(--green-b);color:var(--green)}.s-failed{background:var(--red-b);color:var(--red)}.s-cancelled{background:var(--gray-b);color:var(--sub2)}
pre{background:var(--gray-b);padding:10px;border-radius:8px;overflow:auto;max-height:420px;font-size:12px}
th[aria-sort],table.sort td{white-space:nowrap}th button{all:unset;cursor:pointer;white-space:nowrap}th button:focus-visible{outline:2px solid var(--blue)}
th button::after{content:'▲';display:inline-block;width:1.2em;margin-left:.2em;text-align:center;visibility:hidden}
th[aria-sort=ascending] button::after,th[aria-sort=descending] button::after{visibility:visible}th[aria-sort=descending] button::after{content:'▼'}
.chip{display:inline-flex;align-items:center;height:26px;padding:0 10px;border-radius:13px;background:var(--fill);font-size:13px;margin:0 4px 4px 0;color:var(--ink);text-decoration:none}.chip.sel{background:var(--blue);color:#fff}
.alist{list-style:none;padding:0;margin:0}.alist li{padding:8px 0;border-top:1px solid var(--line)}
.thumb-none{width:320px;height:180px;background:var(--gray-b);border-radius:10px}
.mini{display:flex;gap:8px;align-items:center;flex-wrap:wrap}.mini input[type=text],.mini select{width:auto;flex:1 1 160px;min-width:0}
#reload-bar{display:none;position:sticky;top:0;z-index:5;background:var(--blue-b);color:var(--blue-d);padding:8px 14px;border-radius:10px;margin-bottom:10px;font-weight:600}#net{font-size:12px;color:var(--sub2)}
@media(max-width:1024px){.grid2{grid-template-columns:1fr}}
@media (max-width:640px){main{padding:14px}.grid,.lanes{grid-template-columns:1fr;grid-auto-flow:row}dl.kv,.field{grid-template-columns:1fr}.field label{padding-top:0}}";

    /// <summary>Click a header button of a table.sort to sort its rows; numbers as numbers ("-" last), text in Korean order with digits as numbers.</summary>
    internal const string SortScript = @"<script>
document.querySelectorAll('table.sort').forEach(function(t){
  var hs=t.tHead.rows[0].cells;
  Array.prototype.forEach.call(hs,function(th,i){
    var b=th.querySelector('button'); if(!b) return;
    b.addEventListener('click',function(){
      var up=th.getAttribute('aria-sort')!=='ascending', body=t.tBodies[0], rows=Array.prototype.slice.call(body.rows);
      Array.prototype.forEach.call(hs,function(h){ if(h.querySelector('button')) h.setAttribute('aria-sort','none'); });
      th.setAttribute('aria-sort',up?'ascending':'descending');
      function key(r){ var s=r.cells[i].textContent.trim(), n=parseFloat(s); return isNaN(n)?s:n; }
      rows.sort(function(a,c){
        var x=key(a), y=key(c);
        if(x==='-'||y==='-') return (x==='-')-(y==='-');
        var d=typeof x==='number'&&typeof y==='number'?x-y:String(x).localeCompare(String(y),'ko',{numeric:true});
        return up?d:-d;
      });
      rows.forEach(function(r){ body.appendChild(r); });
    });
  });
});
</script>";

    /// <summary>A header cell the sort script can sort by.</summary>
    internal static string Th(string label) => $"<th aria-sort=\"none\"><button type=\"button\">{label}</button></th>";

    // sidebar entries: key, label, link, icon colour, icon path (24px grid)
    static readonly (string Key, string Label, string Href, string Color, string Icon)[] Nav =
    {
        ("home", "홈", "/", "#007aff", "M3 13h8V3H3v10zm0 8h8v-6H3v6zm10 0h8V11h-8v10zm0-18v6h8V3h-8z"),
        ("analyses", "분석", "/analyses", "#5856d6", "M3 3v18h18V3H3zm8 16H5v-6h6v6zm0-8H5V5h6v6zm8 8h-6v-6h6v6zm0-8h-6V5h6v6z"),
        ("films", "영상", "/films", "#ff2d55", "M18 4l2 4h-3l-2-4h-2l2 4h-3l-2-4H8l2 4H7L5 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V4h-4z"),
        ("files", "보관함", "/files", "#ff9500", "M10 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z"),
    };
    const string AdminIcon = "M18 8h-1V6c0-2.76-2.24-5-5-5S7 3.24 7 6v2H6c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V10c0-1.1-.9-2-2-2zm-6 9c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2zm3.1-9H8.9V6c0-1.71 1.39-3.1 3.1-3.1 1.71 0 3.1 1.39 3.1 3.1v2z";

    // icons are CSS backgrounds (data: SVG), so pages keep their own <svg> count to themselves
    static readonly string NavCss = string.Concat(Nav.Append((Key: "admin", Label: "", Href: "", Color: "#8e8e93", Icon: AdminIcon)).Select(n =>
        $".side .i-{n.Key}{{background:{n.Color} url(\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='white'%3E%3Cpath d='{n.Icon}'/%3E%3C/svg%3E\") center/16px no-repeat}}"));

    static string NavLink(string key, string label, string href, string? on, string cls = "") =>
        $"<a href=\"{href}\" class=\"{cls}{(key == on ? " on" : "")}\"{(key == on ? " aria-current=\"page\"" : "")} title=\"{label}\"><i class=\"i-{key}\" aria-hidden=\"true\"></i><span>{label}</span></a>";

    /// <summary>The shared shell: the collapsing sidebar and &lt;main&gt;. <paramref name="script"/> (a whole &lt;script&gt; element) goes after &lt;/main&gt;;
    /// <paramref name="nav"/> marks the sidebar entry (home, analyses, films, files, admin); <paramref name="fill"/> makes main a full-height column (lanes, maps) instead of a scrolling page.</summary>
    public static string Page(string title, string body, string? script = null, string? nav = null, bool fill = false)
    {
        var side = new StringBuilder("<nav class=\"side\" aria-label=\"메뉴\"><a href=\"/\" class=\"logo\" aria-label=\"ASH 홈\">ASH</a>");
        foreach (var n in Nav) side.Append(NavLink(n.Key, n.Label, n.Href, nav));
        side.Append(NavLink("admin", "관리", "/admin/settings", nav, "end")).Append("</nav>");
        return "<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>" + H(title) + "</title>" +
            "<style>" + Css + NavCss + "</style></head><body>" + side + "<main" + (fill ? " class=\"fillpage\"" : "") + ">" + body + "</main>" + script + "</body></html>";
    }


    /// <summary>Polls <paramref name="url"/> every 10 s and sets the data-field/data-bar/data-state elements (textContent only); shows #reload-bar when a watched job/film ends.</summary>
    public static string PollScript(string url, IReadOnlyDictionary<string, string>? initialWatch = null) => @"<script>
(function(){
  var prev=" + (initialWatch == null ? "null" : System.Text.Json.JsonSerializer.Serialize(initialWatch)) + @", reloading=false, net=document.getElementById('net'), timer=null;
  function sel(a,k){ return '['+a+'=""'+k.replace(/[""\\]/g,'\\$&')+'""]'; }
  function tick(){
    fetch('" + url.Replace("\\", "\\\\").Replace("'", "\\'") + @"',{cache:'no-store'}).then(function(r){ if(!r.ok) throw 0; return r.json(); }).then(function(d){
      if(net) net.textContent='';
      var f=d.fields||{};
      Object.keys(f).forEach(function(k){
        var v=String(f[k]);
        if(/\.bar$/.test(k)) document.querySelectorAll(sel('data-bar',k)+' i').forEach(function(e){ e.style.width=v+'%'; });
        else if(/\.state$/.test(k)) document.querySelectorAll(sel('data-state',k)).forEach(function(e){ e.className='state s-'+v; });
        document.querySelectorAll(sel('data-field',k)).forEach(function(e){ e.textContent=v; });
      });
      var w=d.watch||{}, ended=false;
      if(prev) Object.keys(w).forEach(function(id){ if(prev[id]==='queued'&&w[id]==='running'){ location.reload(); return; } if((prev[id]==='queued'||prev[id]==='running')&&w[id]!=='queued'&&w[id]!=='running') ended=true; });
      if(prev) Object.keys(prev).forEach(function(id){ if(!(id in w)&&(prev[id]==='queued'||prev[id]==='running')) ended=true; });
      prev=w;
      if(ended&&!reloading){
        reloading=true;
        var b=document.getElementById('reload-bar');
        if(b){ b.style.display='block'; b.textContent='결과가 나왔습니다 — '; var a=document.createElement('a'); a.href='javascript:location.reload()'; a.textContent='새로 읽기'; b.appendChild(a); }
        setTimeout(function(){ location.reload(); },60000);
      }
      if(d.active===false) clearInterval(timer);
    }).catch(function(){ if(net) net.textContent='연결 끊김 · 다시 시도 중'; });
  }
  timer=setInterval(tick,10000); tick();
})();
</script>";

    /// <summary>Goes first in a page body: the "새로 읽기" bar and the connection note the polling script fills.</summary>
    internal const string PollSlots = "<div id=\"reload-bar\"></div><span id=\"net\"></span>";

    internal static string State(JobInfo j) => $"<span class=\"state s-{H(j.State)}\">{H(JobState.Korean(j.State))}</span>";

    /// <summary>Page-only CSS of the job pages (job page, queue). State symbols come from CSS so the polling script, which resets the class, keeps them.</summary>
    const string JobCss = @"
.jh{display:flex;align-items:center;gap:12px;flex-wrap:wrap;margin:0 0 4px}.jh h1{margin:0;font-size:22px}.jh .sp{flex:1}.jh form{margin:0}
.jm{font-size:13px;color:var(--sub2);margin:0 0 8px}
.jm .tag{margin-right:4px}
.state[data-state]::before{margin-right:4px}.state[data-state].s-queued::before{content:'○'}.state[data-state].s-running::before{content:'▶'}.state[data-state].s-done::before{content:'✓'}.state[data-state].s-failed::before{content:'▲'}.state[data-state].s-cancelled::before{content:'■'}
.kpis{margin-top:10px}.kpi .t3{margin-bottom:2px}.kpi .t4{margin-top:2px}.kpi.ok{--c:var(--green)}.kpi.bad{--c:var(--red)}.kpi.blue{--c:var(--blue)}.kpi .t2{color:var(--c,var(--ink))}
.kpi .sym{font-weight:700}
.sym.r{color:var(--red)}.sym.g{color:var(--gray)}
tr.over td{background:var(--red-b)}tr.under td{color:var(--sub2)}
.bar.r i{background:var(--red)}.bar.g i{background:var(--gray)}
/* warning (over the utilization limit only): orange, not the red of a failure */
.kpi.wn{--c:var(--orange)}.wn,.sym.o{color:var(--orange)}tr.ow td{background:var(--orange-b)}.bar.o i{background:var(--orange)}
.box table{width:100%}.box table.gt{width:auto}.gt th,.gt td{text-align:center}.gt th button{text-align:center}.box h2{margin-top:0}.box .t4{margin-top:6px}
.maps{display:flex;flex-wrap:wrap;gap:18px}
.curves{display:grid;grid-template-columns:repeat(auto-fill,minmax(340px,1fr));gap:14px}.curves svg{width:100%;height:auto;border-radius:10px}
.files{display:grid;grid-template-columns:repeat(auto-fill,minmax(340px,1fr));gap:8px}.files .row{font-size:14px}.files .row .t>div{font-family:Consolas,monospace;font-size:14px}
.jt td{padding:11px 10px}
";

    /// <summary>State tag with its symbol in the text (for tables that no script touches).</summary>
    static string Tag(JobInfo j)
    {
        var sym = j.State switch { JobState.Queued => "○", JobState.Running => "▶", JobState.Done => "✓", JobState.Failed => "▲", _ => "■" };
        return $"<span class=\"state s-{H(j.State)}\">{sym} {H(JobState.Korean(j.State))}</span>";
    }

    /// <summary>One KPI tile: label (t3), value (t1 or t2 for text), small line (t4). <paramref name="cls"/> picks the colour (ok, bad, blue).</summary>
    static string Kpi(string label, string value, string sub, string cls = "", string vcls = "t1", string? title = null) =>
        $"<div class=\"kpi {cls}\"{(title == null ? "" : $" title=\"{title}\"")}><div class=\"t3\">{label}</div><div class=\"{vcls}\">{value}</div><div class=\"t4\">{sub}</div></div>";

    public static string List(IReadOnlyList<JobInfo> jobs, (int Ahead, DateTime? Start, TimeSpan? Typical) q, bool workerEnabled)
    {
        var sb = new StringBuilder("<style>" + JobCss + "</style><div class=\"jh\"><h1>작업 대기열</h1></div>");
        if (!workerEnabled) sb.Append("<div class=\"msg bad\" role=\"alert\">▲ <b>이 서버는 작업을 실행하지 않습니다</b>(Jobs:WorkerEnabled=false)</div>");
        sb.Append("<div class=\"kpis\">")
          .Append(Kpi("대기", $"{q.Ahead}<small>건</small>", "먼저 온 순서로 하나씩", "blue"))
          .Append(Kpi("보통 한 작업", q.Typical is { } t ? H(Dur(t)) : "-", "&nbsp;", "", "t2"))
          .Append(Kpi("지금 보내면 시작 예상", q.Start is { } s ? H(SeoulTime.Text(s)) : "-", "&nbsp;", "", "t2"))
          .Append("</div>");
        sb.Append("<div class=\"t4\" style=\"margin:8px 0 0\" title=\"작업은 ZWCAD의 ASH에서 서버로 보내기로 올립니다. 먼저 온 순서로 하나씩 실행합니다.\">ZWCAD의 ASH에서 \"서버로 보내기\"로 올린 작업</div>");
        sb.Append("<div class=\"box\">");
        JobTable(sb, jobs, true);
        sb.Append("</div>");
        if (jobs.Count > 0) sb.Append(SortScript);
        return Page("ASH 작업 대기열", sb.ToString(), nav: "admin");
    }

    /// <summary>The job table of /jobs, also the "최근 작업" box of the home page. <paramref name="sort"/>: header buttons sort the rows (the page must carry <see cref="SortScript"/>).</summary>
    internal static void JobTable(StringBuilder sb, IReadOnlyList<JobInfo> jobs, bool sort = false)
    {
        sort &= jobs.Count > 0; // the "no jobs" row has no cells to sort by
        string Head(string label) => sort ? Th(label) : $"<th>{label}</th>";
        sb.Append(sort ? "<div class=\"scroll\"><table class=\"sort jt\"><thead><tr>" : "<div class=\"scroll\"><table><tr>")
          .Append(Head("작업")).Append(Head("상태")).Append(Head("프로젝트 / 안")).Append(Head("모델")).Append(Head("보낸 사람")).Append("<th>보낸 시각(서울)</th>").Append(Head("실행(회)")).Append(Head("권장 총 대수(대)"))
          .Append(sort ? "</tr></thead><tbody>" : "</tr>");
        foreach (var j in jobs)
        {
            var total = j.Decided?.Values.Sum();
            sb.Append($"<tr><td><a href=\"/jobs/{H(j.Id)}\">{H(j.Id)}</a></td><td>{Tag(j)}{(j.Phase != null ? " " + H(j.Phase) : "")}</td><td>{H(j.Project)} / {H(j.Plan)}</td><td>{H(j.Model)}</td>" +
                $"<td>{H(j.Submitter)} <span class=\"muted\">{H(j.Ip)}</span></td><td>{H(When(j.CreatedUtc))}</td><td class=\"num\">{j.RunsDone}{(j.RunsFailed > 0 ? $" <span class=\"bad\">▲ 실패 {j.RunsFailed}</span>" : "")}</td>" +
                $"<td class=\"num\">{(total is { } n ? n.ToString(CultureInfo.InvariantCulture) : "-")}</td></tr>");
        }
        if (jobs.Count == 0) sb.Append("<tr><td colspan=\"8\" class=\"muted\">아직 작업이 없습니다.</td></tr>");
        sb.Append(sort ? "</tbody></table></div>" : "</table></div>");
    }

    public static string Job(JobInfo j, JobView? v, string tab, string logTail, (int Ahead, DateTime? Start, TimeSpan? Typical) q, bool canCancel, IReadOnlyList<ASH.Core.Analysis.AnalysisInfo>? analyses = null,
        IReadOnlyList<FilmInfo>? films = null, string? busy = null, int parallel = 1, IReadOnlyDictionary<string, PriceRow>? prices = null)
    {
        var sb = new StringBuilder(PollSlots);
        sb.Append("<style>").Append(JobCss).Append("</style>");
        if (analyses != null) AnalysisPages.JobHeader(sb, j, analyses);
        // header: title + state tag + the one blue next action
        sb.Append($"<div class=\"jh\"><h1>{H(j.Project)} / {H(j.Plan)} <span class=\"muted\">· 모델 {H(j.Model)}</span></h1>");
        sb.Append($"<span class=\"state s-{H(j.State)}\" data-state=\"job.state\" data-field=\"job.statetext\">{H(JobState.Korean(j.State))}</span>");
        if (j.Source == JobSource.Local) sb.Append("<span class=\"tag gray\" title=\"ash-run으로 돌린 결과를 올린 것입니다. 서버에서 다시 실행하지 않았습니다.\">내 PC에서 실행한 결과</span>");
        sb.Append("<span class=\"sp\"></span>");
        if (j.Finished && j.Decided != null) sb.Append($"<a class=\"btn pri\" href=\"/jobs/{H(j.Id)}/files/report.xlsx\">Excel 보고서 받기</a>");
        else if (j.Finished) sb.Append($"<a class=\"btn pri\" href=\"/jobs/{H(j.Id)}?tab=files\">로그 보기</a>");
        else if (canCancel)
            sb.Append($"<form method=\"post\" action=\"/jobs/{H(j.Id)}/cancel\"><button title=\"보낸 PC와 관리자만 취소할 수 있습니다. 실행 중인 모델 실행은 끝까지 돌고 새 실행만 멈춥니다.\">{(j.State == JobState.Queued ? "대기 취소" : "실행 취소")}</button></form>");
        sb.Append("</div>");
        sb.Append($"<p class=\"jm\"><span data-field=\"job.phase\">{H(j.Phase)}</span><span data-field=\"job.sep\">{(j.Phase != null ? " · " : "")}</span>작업 {H(j.Id)} · 보낸 사람 {H(j.Submitter)} ({H(j.Ip)}) · 보낸 시각 {H(When(j.CreatedUtc))}");
        if (j.StartedUtc != null) sb.Append($" · 실행 시간 {Dur(j.Duration)}");
        sb.Append($" · 모델 실행 <span data-field=\"job.runs\">{j.RunsDone}{(j.ExpectedRuns is { } er ? "/" + er : "")}회</span>{(j.RunsFailed > 0 ? $" <span class=\"bad\">▲ 실패 {j.RunsFailed}</span>" : "")}</p>");
        if (j.State == JobState.Queued) sb.Append($"<p class=\"jm\">{(q.Ahead == 0 ? "다음 차례입니다" : $"앞에 {q.Ahead}건이 있습니다")}{(busy != null ? $"({H(busy)}: 끝나야 시작합니다)" : "")}{(q.Start is { } s ? $" · 시작 예상 {H(SeoulTime.Text(s))}" : "")}.</p>");
        if (!j.Finished)
        {
            var lf = LiveFields.OfJob(j, q.Typical, parallel).Fields; // same texts the poll sets, so first paint and live never drift
            sb.Append($"<div class=\"prog\"><i data-bar=\"job.bar\" style=\"width:{lf["job.bar"]}%\"></i></div>");
            sb.Append($"<p class=\"jm\" style=\"margin-top:6px\">진행률 <b data-field=\"job.progress\">{H(lf["job.progress"])}</b> · 끝날 예상 <span data-field=\"job.eta\">{H(lf["job.eta"])}</span> · 한 번 평균 <span data-field=\"job.avg\">{H(lf["job.avg"])}</span>" +
                $" · 마지막 출력: <span data-field=\"job.last\">{H(lf["job.last"])}</span></p>");
        }
        if (j.Error != null) sb.Append($"<div class=\"msg {(j.State == JobState.Done ? "warn" : "bad")}\" role=\"alert\">▲ <b>{(j.State == JobState.Done ? "주의" : "오류")}:</b> {H(j.Error)}</div>");

        var mine = (films ?? Array.Empty<FilmInfo>()).Where(f => f.Items.Any(i => i.JobId == j.Id && i.Ok)).ToList();
        if (mine.Count > 0)
        {
            sb.Append("<div class=\"box\" style=\"margin:0 0 14px\"><b>이 버전의 영상</b><ul>");
            foreach (var f in mine)
            {
                sb.Append($"<li><a href=\"/films/{H(f.Id)}\">{H(When(f.CreatedUtc))}</a>: ");
                sb.Append(f.KeepEach ? string.Join(" · ", f.Items.Where(i => i.JobId == j.Id && i.Ok).Select(i => $"<a href=\"{AnalysisPages.FileUrl(f, i.Name)}\">{H(i.Label)}</a>"))
                    : f.JoinedOk ? $"<a href=\"{AnalysisPages.FileUrl(f, f.Joined!)}\">이어 붙인 영상</a>" : "<span class=\"muted\">이어 붙인 영상만 만들었습니다</span>");
                sb.Append("</li>");
            }
            sb.Append("</ul></div>");
        }

        var tabs = new[] { ("summary", "결론"), ("detail", "상세"), ("input", "입력·추적"), ("files", "다운로드·로그") };
        sb.Append("<div class=\"tabs\">");
        foreach (var (k, name) in tabs) sb.Append($"<a href=\"/jobs/{H(j.Id)}?tab={k}\"{(k == tab ? " class=\"on\" aria-current=\"page\"" : "")}>{name}</a>");
        sb.Append("</div><div class=\"panel\">");
        switch (tab)
        {
            case "detail": Detail(sb, j, v); break;
            case "input": InputTab(sb, j, v); break;
            case "files": FilesTab(sb, j, logTail); break;
            default: Summary(sb, j, v, prices); break;
        }
        sb.Append("</div>");
        return Page($"ASH 작업 {j.Id}", sb.ToString(), j.Finished ? null : PollScript("/api/jobs/" + j.Id, new Dictionary<string, string> { [j.Id] = j.State }), nav: "analyses");
    }

    static void Summary(StringBuilder sb, JobInfo j, JobView? v, IReadOnlyDictionary<string, PriceRow>? prices)
    {
        if (j.Decided == null || v?.Input == null)
        {
            sb.Append(j.Finished ? "<div class=\"empty\">결과가 없습니다. 다운로드·로그 탭의 로그를 확인하세요.</div>" : "<div class=\"empty\">실행이 끝나면 결과가 나옵니다. 결과가 나오면 위 띠로 알립니다.</div>");
            if (v?.InputError != null && j.Finished) sb.Append($"<p class=\"bad\">{H(v.InputError)}</p>");
            return;
        }
        int total = j.Decided.Values.Sum(), min = v.Input.Groups.Sum(g => g.MinCount), max = v.Input.Groups.Sum(g => g.MaxCount);
        bool allOk = v.Groups.All(g => g.AllOk);
        var top = v.Groups.Where(g => !double.IsNaN(g.Util)).OrderByDescending(g => g.Util).FirstOrDefault();
        bool topOver = top != null && Over(top, v.UtilMax);
        double stop = v.Materials.Where(m => !double.IsNaN(m.StopSec)).Sum(m => m.StopSec);

        sb.Append("<div class=\"kpis\">");
        sb.Append(Kpi("권장 AGV 총 대수", $"{total}<small>대</small>", $"시험 범위 {min}~{max}대", "blue", title: $"권장 AGV 총 {total}대"));
        sb.Append(Kpi("최대 가동률", top == null ? "-" : $"{P(top.Util)}<small>%</small>",
            top == null ? "&nbsp;" : topOver ? $"<span class=\"sym o\">⚠ 경고: 상한 {Pct(v.UtilMax)} 초과</span> · {H(top.Group)}" : $"상한 {Pct(v.UtilMax)} · {H(top.Group)}", topOver ? "wn" : ""));
        sb.Append(Kpi("결품 시간", v.Materials.Count == 0 ? "-" : $"{F(stop, "0")}<small>초</small>", v.Materials.Count == 0 ? "&nbsp;" : stop > 0 ? "<span class=\"sym r\">▲ 결품 있음</span>" : "<span class=\"sym\" style=\"color:var(--green)\">✓ 없음</span>",
            v.Materials.Count == 0 ? "" : stop > 0 ? "bad" : "ok"));
        if (prices != null)
        {
            var inv = InvestmentOf(v.Input, j.Decided, prices);
            sb.Append(Kpi("투자비(원)", Cost(inv), inv is { Unpriced.Count: > 0 } ? "단가 없음: " + H(string.Join(", ", inv.Unpriced)) : "&nbsp;"));
        }
        sb.Append("</div>");
        bool anyOver = v.Groups.Any(g => Over(g, v.UtilMax));
        var (tagCls, tagText) = !allOk ? ("red", "▲ 확인 실행 실패") : anyOver ? ("orange", "⚠ 경고: 가동률 초과") : ("green", "✓ 확인 실행 통과");
        sb.Append($"<p class=\"jm\" style=\"margin-top:10px\"><span class=\"tag {tagCls}\" title=\"판정: 재고 · 공급 · 교착. 가동률 상한 {Pct(v.UtilMax)} 초과는 경고\">{tagText}</span> 모델 실행 {j.RunsDone}회 · 실패 {j.RunsFailed}회 · 경고 {j.Warnings.Count}건 · {Dur(j.Duration)}</p>");

        // units sit in the headers, so the cells are plain numbers (and JobReport uses the same headers); the vehicle numbers are sums over the group's vehicles
        var byGroup = v.VehicleGroups.ToDictionary(x => x.Group, StringComparer.Ordinal);
        sb.Append("<div class=\"box\"><h2>그룹별 권장 대수와 부하율</h2><div class=\"scroll\"><table class=\"sort gt\"><thead><tr>").Append(Th("그룹")).Append(Th("AGV 타입")).Append(Th("권장(대)")).Append(Th("범위(대)"))
          .Append(Th("가동률(%, 확인 실행 평균)")).Append(Th("최소 재고/안전재고(배)")).Append(Th("공급 횟수(회)")).Append(Th("총 이동 거리(km)")).Append(Th("공급당 이동 거리(m)"))
          .Append(Th("판정")).Append("</tr></thead><tbody>");
        // problems first: judgement failed or over the limit, then the rest in the given order
        foreach (var g in v.Groups.OrderBy(g => !g.AllOk ? 0 : Over(g, v.UtilMax) ? 1 : 2))
        {
            var w = double.IsNaN(g.Util) ? 0 : Math.Min(1, g.Util);
            bool over = Over(g, v.UtilMax), under = !double.IsNaN(g.Util) && g.Util < 0.10;
            var bc = over ? " o" : under ? " g" : "";
            var sym = over ? " <span class=\"sym o\" title=\"경고: 가동률 상한 초과\">▲</span>" : under ? " <span class=\"sym g\" title=\"10% 미만\">▼</span>" : "";
            var vg = byGroup.GetValueOrDefault(g.Group);
            var verdict = !g.AllOk ? $"<td class=\"bad\">▲ 실패: {H(g.Reason)}</td>" : over ? "<td class=\"wn\">⚠ 경고: 가동률 초과</td>" : "<td class=\"ok\">✓ 통과</td>";
            sb.Append($"<tr{(!g.AllOk ? " class=\"over\"" : over ? " class=\"ow\"" : under ? " class=\"under\"" : "")}><td>{H(g.Group)}</td><td>{H(g.Type)}</td><td><b>{(g.Decided?.ToString(CultureInfo.InvariantCulture) ?? "-")}</b></td><td>{g.Min}~{g.Max}</td>" +
                $"<td><span class=\"bar{bc}\"><i style=\"width:{F(w * 100, "0.#")}%\"></i><b style=\"left:{F(Math.Min(1, v.UtilMax) * 100, "0.#")}%\"></b></span> {P(g.Util)}{sym}</td>" +
                $"<td>{F(g.MinStockRatio)}</td><td>{(vg == null ? "-" : F(vg.Jobs * vg.Count, "0"))}</td><td>{(vg == null ? "-" : F(vg.Distance * vg.Count / 1000, "0.0"))}</td><td>{(vg == null ? "-" : F(vg.DistPerJob, "0.#"))}</td>{verdict}</tr>");
        }
        sb.Append($"</tbody></table></div><div class=\"t4\">▲ 상한 초과(경고) · ▼ 10% 미만 · 세로선 = 가동률 상한 {Pct(v.UtilMax)} · 공급·거리 = 권장 대수 차량의 합</div></div>");
        if (v.VehicleGroups.Count > 0)
        {
            sb.Append("<div class=\"box\"><h2>차량</h2><div class=\"scroll\"><table class=\"sort\"><thead><tr>").Append(Th("그룹")).Append(Th("대수(대)")).Append(Th("평균 가동률(%)")).Append(Th("작업/대(회)"))
              .Append(Th("주행 거리/대(m)")).Append(Th("거리/작업(m)")).Append(Th("시간/작업(초)")).Append("</tr></thead><tbody>");
            foreach (var g in v.VehicleGroups)
                sb.Append($"<tr><td>{H(g.Group)}</td><td class=\"num\">{g.Count}</td><td class=\"num\">{P(g.Util)}</td><td class=\"num\">{F(g.Jobs, "0.#")}</td>" +
                    $"<td class=\"num\">{F(g.Distance, "0")}</td><td class=\"num\">{F(g.DistPerJob, "0.#")}</td><td class=\"num\">{F(g.BusyPerJob, "0.#")}</td></tr>");
            sb.Append("</tbody></table></div><div class=\"t4\" title=\"가동률 = 작업 중 시간(호출 차량의 이동 + 작업) / 측정 시간\">확인 실행 평균 · 워밍업 이후 · 권장 대수만큼 움직인 차량만</div></div>");
        }
        if (v.Materials.Count > 0)
        {
            sb.Append("<div class=\"box\"><h2>자재별 리드타임·재고</h2><div class=\"scroll\"><table class=\"sort\"><thead><tr>").Append(Th("자재")).Append(Th("그룹")).Append(Th("공급/기대(회)")).Append(Th("결품 시간(초)"))
              .Append(Th("호출→상차 평균/최대(초)")).Append(Th("호출→하차 평균/최대(초)")).Append(Th("재고 평균(용기)")).Append(Th("표준편차(용기)")).Append(Th("최소(용기)")).Append(Th("최대(용기)")).Append(Th("주의")).Append("</tr></thead><tbody>");
            foreach (var m in v.Materials)
                sb.Append($"<tr><td>{H(m.Name)}</td><td>{H(m.Group)}</td><td class=\"num\">{F(m.Supplies, "0.#")} / {F(m.Expected, "0.#")}</td><td class=\"num\">{F(m.StopSec, "0")}</td>" +
                    $"<td class=\"num\">{F(m.PickAvg, "0.0")} / {F(m.PickMax, "0.0")}</td><td class=\"num\">{F(m.DelivAvg, "0.0")} / {F(m.DelivMax, "0.0")}</td>" +
                    $"<td class=\"num\">{F(m.StockAvg, "0.0")}</td><td class=\"num\">{F(m.StockStd, "0.0")}</td><td class=\"num\">{F(m.StockMin, "0.#")}</td><td class=\"num\">{F(m.StockMax, "0.#")}</td>" +
                    (m.Warn ? $"<td class=\"bad\" title=\"재고 평균 − 2σ {F(m.StockAvg - 2 * m.StockStd, "0.0")} &lt; 안전재고 {F(m.Safety)}\"><b>주의</b></td></tr>" : "<td></td></tr>"));
            sb.Append("</tbody></table></div><div class=\"t4\" title=\"호출→상차: 호출부터 상차 CP 도착까지. 호출→하차: 호출부터 하차 완료까지. 재고는 takt마다 한 번 잰 값, 최대·최소는 확인 실행 전체의 극값. 주의: 재고 평균 − 2σ가 안전재고보다 낮은 자재(경고일 뿐 판정에는 쓰이지 않음).\">" +
                "재고는 takt마다 한 번 잰 값 · 주의 = 평균 − 2σ가 안전재고 미만(판정 제외)</div></div>");
        }
        sb.Append(SortScript); // once for every sortable table above
        if (j.Warnings.Count > 0)
        {
            sb.Append($"<div class=\"box\"><h2>경고 <span class=\"muted\">{j.Warnings.Count}건</span></h2><ul>");
            foreach (var w in j.Warnings.Take(30)) sb.Append($"<li>{H(w)}</li>");
            sb.Append("</ul></div>");
        }
        sb.Append("<div class=\"box\"><h2>병목 지도 <span class=\"t4\">권장 대수 기준</span></h2>");
        if (v.Layout == null) { sb.Append("<p class=\"muted\">레이아웃을 읽지 못했습니다.</p></div>"); return; }
        if (v.Blocks.Count == 0) sb.Append("<p class=\"muted\">교통제어 block 결과가 없습니다. 경로만 그립니다.</p>");
        sb.Append("<div class=\"maps\">");
        sb.Append("<div>").Append(Map(v, "block 점유율(%)", b => b.Utilization, x => Pct(x))).Append("</div>");
        sb.Append("<div>").Append(Map(v, "block 평균 대기(초)", b => b.AvgWaitSec, x => F(x, "0.0") + "초")).Append("</div>");
        sb.Append("</div></div>");
        if (v.Blocks.Count > 0)
        {
            sb.Append("<div class=\"box\"><h2>대기가 긴 block</h2><div class=\"scroll\"><table><tr><th>block</th><th>평균 대기(초)</th><th>점유율(%)</th><th>통과 수(회)</th></tr>");
            foreach (var b in v.Blocks.Take(10)) sb.Append($"<tr><td>{H(b.Name)}</td><td class=\"num\">{F(b.AvgWaitSec, "0.0")}</td><td class=\"num\">{P(b.Utilization)}</td><td class=\"num\">{F(b.Passes, "0")}</td></tr>");
            sb.Append("</table></div></div>");
        }
    }

    /// <summary>The 결론 block of the analysis page: three numbers, group bars per cluster, one bottleneck map, the three strongest cross-group effects.
    /// <paramref name="small"/> = per-plan listing (smaller map). Plan B inserts its investment card at the marked point.</summary>
    public static void Conclusion(StringBuilder sb, JobInfo j, JobView v, bool small, string metric = "wait")
    {
        if (j.Decided == null || v.Input == null) { sb.Append("<p class=\"muted\">결과가 없습니다.</p>"); return; }
        int total = j.Decided.Values.Sum(), min = v.Input.Groups.Sum(g => g.MinCount), max = v.Input.Groups.Sum(g => g.MaxCount);
        var top = v.Groups.Where(g => !double.IsNaN(g.Util)).OrderByDescending(g => g.Util).FirstOrDefault();
        bool occ = metric == "occ";
        double Val(BlockView b) => occ ? b.Utilization : b.AvgWaitSec;
        var worst = v.Blocks.Where(b => !double.IsNaN(Val(b))).OrderByDescending(Val).FirstOrDefault();
        var id = H(j.Id);
        sb.Append("<div class=\"grid2\"><div>");
        sb.Append("<div class=\"cards\">");
        sb.Append($"<div class=\"card\"><div class=\"sub\">권장 총 대수</div><div class=\"big\">{total}대</div><div class=\"sub\">시험 범위 {min}~{max}대</div></div>");
        bool many = v.Groups.Count > ManyGroups;
        sb.Append($"<div class=\"card\"><div class=\"sub\">최대 가동률</div><div class=\"big\">{(top == null ? "-" : Pct(top.Util))}</div><div class=\"sub\">상한 {Pct(v.UtilMax)} · 그룹 {H(top?.Group ?? "-")}</div>" +
            (many ? $"<div class=\"sub\">경고(상한 초과) {v.Groups.Count(g => g.AllOk && Over(g, v.UtilMax))}개 · 판정 실패 {v.Groups.Count(g => !g.AllOk)}개 그룹</div>" : "") + "</div>");
        sb.Append($"<div class=\"card\"><div class=\"sub\">최대 병목</div><div class=\"big\">{H(worst?.Name ?? "-")}</div><div class=\"sub\">{(occ ? "점유율" : "평균 대기")} {(worst == null ? "-" : occ ? Pct(worst.Utilization) : F(worst.AvgWaitSec, "0.0") + "초")}</div></div>");
        sb.Append("</div>");
        if (many) ManyGroupsBlock(sb, j, v); else ClusterBars(sb, j, v);
        sb.Append("<h3>서로 영향이 큰 그룹</h3>");
        var effects = v.Impact.Where(r => r.OtherGroup != r.Group && !double.IsNaN(r.UtilDelta)).OrderByDescending(r => Math.Abs(r.UtilDelta)).Take(3).ToList();
        if (effects.Count == 0) sb.Append("<p class=\"muted\">영향표 없음</p>");
        else
        {
            sb.Append("<ol>");
            foreach (var r in effects) sb.Append($"<li>{H(r.Group)} +{r.PlusOne}대 → {H(r.OtherGroup)} 가동률 {F(r.UtilDelta * 100, "+0.0;-0.0;0")}%p</li>");
            sb.Append($"</ol><p><a href=\"/jobs/{id}?tab=detail\">영향표 전체 →</a></p>");
        }
        // plan B: investment card goes here
        sb.Append("</div><div>");
        if (v.Layout == null) sb.Append("<p class=\"muted\">레이아웃을 읽지 못했습니다.</p>");
        else
        {
            var (w, h) = small ? (360d, 250d) : (520d, 360d);
            var view = small ? "plans" : "one";
            sb.Append(occ ? Map(v, "block 점유율(%)", b => b.Utilization, x => Pct(x), w, h, true) : Map(v, "block 평균 대기(초)", b => b.AvgWaitSec, x => F(x, "0.0") + "초", w, h, true));
            sb.Append($"<p class=\"mini\">기준: <a href=\"?tab=conclusion&amp;view={view}&amp;job={id}&amp;metric=wait\"{(occ ? "" : " class=\"chip sel\"")}>평균 대기</a> <a href=\"?tab=conclusion&amp;view={view}&amp;job={id}&amp;metric=occ\"{(occ ? " class=\"chip sel\"" : "")}>점유율</a></p>");
        }
        sb.Append("</div></div>");
        sb.Append($"<p><a href=\"/jobs/{id}?tab=detail\">상세 보기</a> · <a href=\"/jobs/{id}/files/report.xlsx\">Excel 보고서</a> · <a href=\"/jobs/{id}\">이 버전의 영상</a></p>");
    }

    /// <summary>More groups than this: the 결론, 비교 and 현황 tabs show a summary, the notable groups and one compact figure instead of a line per group.</summary>
    public const int ManyGroups = 3;

    /// <summary>Over the utilization limit: the mean, or any confirm run (a warning, not a failure).</summary>
    internal static bool Over(GroupView g, double utilMax) => g.UtilOver || !double.IsNaN(g.Util) && g.Util > utilMax;

    /// <summary>At most five groups worth a look: judgement failed first, then over the limit, then the busiest.</summary>
    internal static List<GroupView> Notable(JobView v) =>
        v.Groups.OrderBy(g => !g.AllOk ? 0 : Over(g, v.UtilMax) ? 1 : 2).ThenByDescending(g => double.IsNaN(g.Util) ? -1 : g.Util).Take(5).ToList();

    /// <summary>결론 with many groups: 주목 그룹 bars, the distribution strip, and every group's bar in a closed details.</summary>
    static void ManyGroupsBlock(StringBuilder sb, JobInfo j, JobView v)
    {
        sb.Append("<h3>주목 그룹</h3>");
        foreach (var g in Notable(v)) GroupRow(sb, g, v.UtilMax);
        sb.Append("<h3>분포 그림</h3>").Append(Strip(v));
        sb.Append($"<details><summary>전체 {v.Groups.Count}개 그룹 보기</summary>");
        ClusterBars(sb, j, v);
        sb.Append("</details>");
    }

    /// <summary>One dot per group on a 0–100 % utilisation axis (red = judgement failed, amber = over the limit), dashed limit line. Fixed height:
    /// dots that would overlap step up and down a little. Groups without a utilisation get no dot.</summary>
    internal static string Strip(JobView v)
    {
        const double w = 520, h = 64, l = 16, r = 16, mid = 26;
        double X(double u) => l + (w - l - r) * Math.Clamp(u, 0, 1);
        var sb = new StringBuilder($"<svg class=\"strip\" width=\"{w}\" height=\"{h}\" viewBox=\"0 0 {w} {h}\" style=\"background:#fff;border:1px solid #d3d1c7;max-width:100%\">");
        sb.Append($"<line x1=\"{l}\" x2=\"{w - r}\" y1=\"{mid + 18}\" y2=\"{mid + 18}\" stroke=\"#888780\"/>");
        foreach (var t in new[] { 0, 50, 100 }) sb.Append($"<text x=\"{F(X(t / 100.0), "0.#")}\" y=\"{h - 4}\" font-size=\"10\" text-anchor=\"middle\" fill=\"#5f5e5a\">{t}%</text>");
        var lx = F(X(v.UtilMax), "0.#");
        sb.Append($"<line x1=\"{lx}\" x2=\"{lx}\" y1=\"4\" y2=\"{mid + 18}\" stroke=\"#a32d2d\" stroke-dasharray=\"4 3\"><title>가동률 상한 {F(v.UtilMax * 100, "0")}%</title></line>");
        var placed = new List<double>();
        foreach (var g in v.Groups.Where(g => !double.IsNaN(g.Util)).OrderBy(g => g.Util))
        {
            double x = X(g.Util); int k = placed.Count(px => Math.Abs(px - x) < 7); placed.Add(x);
            double y = mid + (k % 2 == 1 ? -1 : 1) * ((k + 1) / 2 % 4) * 5; // 0, -5, +5, -10, +10, -15, +15, then round again
            var fill = !g.AllOk ? "#a32d2d" : Over(g, v.UtilMax) ? "#ef9f27" : "#378add";
            sb.Append($"<circle cx=\"{F(x, "0.#")}\" cy=\"{F(y, "0.#")}\" r=\"5\" fill=\"{fill}\" fill-opacity=\"0.85\"><title>{H(g.Group)} · 가동률 {F(g.Util * 100, "0")}% · {g.Decided?.ToString(CultureInfo.InvariantCulture) ?? "-"}대</title></circle>");
        }
        return sb.Append("</svg><div class=\"muted\" style=\"font-size:12px\">점 하나 = 그룹 · <span style=\"color:#a32d2d\">●</span> 판정 실패 · <span style=\"color:#ef9f27\">●</span> 경고(상한 초과) · 빨간 점선은 상한</div>").ToString();
    }

    /// <summary>One group's bar line: utilisation vs the limit, recommended count in bold, the failure reason in red, over the limit as an orange warning.</summary>
    static void GroupRow(StringBuilder sb, GroupView g, double utilMax)
    {
        var w = double.IsNaN(g.Util) ? 0 : Math.Min(1, g.Util);
        bool warn = g.AllOk && Over(g, utilMax);
        sb.Append($"<div>{H(g.Group)} · {H(g.Type)} · <span class=\"bar\"><i style=\"width:{F(w * 100, "0.#")}%{(!g.AllOk ? ";background:var(--bad)" : warn ? ";background:var(--orange)" : "")}\"></i><b style=\"left:{F(Math.Min(1, utilMax) * 100, "0.#")}%\"></b></span> " +
            $"가동률 {Pct(g.Util)} · <b>권장 {(g.Decided?.ToString(CultureInfo.InvariantCulture) ?? "-")}대</b> · 범위 {g.Min}~{g.Max}대" +
            (!g.AllOk ? $" · <span class=\"bad\">실패: {H(g.Reason)}</span>" : warn ? " · <span style=\"color:var(--orange)\">⚠ 경고: 가동률 초과</span>" : "") + "</div>");
    }

    /// <summary>One bar line per group (utilisation vs the limit, recommended count in bold), under a "묶음: G1, G2" heading per cluster; flat when the job has none.</summary>
    internal static void ClusterBars(StringBuilder sb, JobInfo j, JobView v)
    {
        void Row(GroupView g) => GroupRow(sb, g, v.UtilMax);
        if (j.Clusters is not { Count: > 0 }) { foreach (var g in v.Groups) Row(g); return; }
        var shown = new HashSet<string>(StringComparer.Ordinal); var single = new List<GroupView>(); var headed = false;
        foreach (var c in j.Clusters)
        {
            var mine = v.Groups.Where(g => c.Contains(g.Group) && shown.Add(g.Group)).ToList();
            if (mine.Count == 1) { single.Add(mine[0]); continue; } // a cluster of one is no cluster: gathered under one heading below
            if (mine.Count == 0) continue;
            sb.Append($"<h3>묶음: {H(string.Join(", ", c))}</h3>"); headed = true;
            foreach (var g in mine) Row(g);
        }
        single.AddRange(v.Groups.Where(g => !shown.Contains(g.Group)));
        if (single.Count > 0) { if (headed) sb.Append("<h3>단독</h3>"); foreach (var g in single) Row(g); }
    }

    internal static string Heat(double t)
    {
        // light grey -> amber -> red
        t = double.IsNaN(t) ? 0 : Math.Clamp(t, 0, 1);
        (int r, int g, int b) a = (241, 239, 232), m = (239, 159, 39), z = (226, 75, 74);
        var (p, q, u) = t < 0.5 ? (a, m, t * 2) : (m, z, (t - 0.5) * 2);
        int L(int x, int y) => (int)Math.Round(x + (y - x) * u);
        return $"rgb({L(p.r, q.r)},{L(p.g, q.g)},{L(p.b, q.b)})";
    }

    /// <summary>Model-coordinate bounds (x0, x1, y0, y1) of a layout's paths, blocks and CPs; null when it has nothing to draw.</summary>
    internal static (double X0, double X1, double Y0, double Y1)? LayoutBounds(ASH.Core.Layout.Layout l)
    {
        var pts = l.Paths.SelectMany(p => new[] { p.Start, p.End }).Concat(l.Blocks.SelectMany(b => new[] { b.Rect.Min, b.Rect.Max })).Concat(l.Cps.Select(c => c.Position)).ToList();
        return pts.Count == 0 ? null : (pts.Min(p => p.X), pts.Max(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.Y));
    }

    /// <summary>Paths in grey, blocks coloured by a value (highest = red), CPs as dots. Model coordinates, y up.
    /// <paramref name="bounds"/> and <paramref name="hiValue"/> replace this view's own extent and colour maximum, so several maps can share one scale.</summary>
    internal static string Map(JobView v, string title, Func<BlockView, double> value, Func<double, string> label, double w = 520, double h = 360, bool topLabels = false,
        (double X0, double X1, double Y0, double Y1)? bounds = null, double? hiValue = null)
    {
        var l = v.Layout!;
        if ((bounds ?? LayoutBounds(l)) is not { } bb) return "<p class=\"muted\">그릴 경로가 없습니다.</p>";
        double x0 = bb.X0, x1 = bb.X1, y0 = bb.Y0, y1 = bb.Y1;
        double pad = 12, sx = (w - 2 * pad) / Math.Max(1e-6, x1 - x0), sy = (h - 2 * pad) / Math.Max(1e-6, y1 - y0), s = Math.Min(sx, sy);
        string X(double x) => F(pad + (x - x0) * s, "0.#");
        string Y(double y) => F(h - pad - (y - y0) * s, "0.#");
        var byName = v.Blocks.ToDictionary(b => b.Name, b => value(b), StringComparer.Ordinal);
        double hi = hiValue ?? byName.Values.Where(x => !double.IsNaN(x)).DefaultIfEmpty(0).Max();
        var sb = new StringBuilder($"<div style=\"font-weight:600;margin:6px 0\">{H(title)}</div><svg width=\"{w}\" height=\"{h}\" viewBox=\"0 0 {w} {h}\" style=\"background:#fff;border:1px solid #d3d1c7\">");
        foreach (var b in l.Blocks)
        {
            var val = byName.TryGetValue(b.Name, out var d) ? d : double.NaN;
            var fill = double.IsNaN(val) ? "#f1efe8" : Heat(hi > 0 ? val / hi : 0);
            double bx = Math.Min(b.Rect.Min.X, b.Rect.Max.X), by = Math.Max(b.Rect.Min.Y, b.Rect.Max.Y);
            sb.Append($"<rect x=\"{X(bx)}\" y=\"{Y(by)}\" width=\"{F(Math.Abs(b.Rect.Max.X - b.Rect.Min.X) * s, "0.#")}\" height=\"{F(Math.Abs(b.Rect.Max.Y - b.Rect.Min.Y) * s, "0.#")}\" fill=\"{fill}\" stroke=\"#888780\" stroke-width=\"0.5\"><title>{H(b.Name)}: {H(double.IsNaN(val) ? "결과 없음" : label(val))}</title></rect>");
        }
        foreach (var p in l.Paths)
        {
            var pl = new StringBuilder();
            int n = p.Segments.Any(sg => sg is ArcSeg) ? 16 : 1;
            double len = p.Length;
            for (int i = 0; i <= n; i++) { var q = p.PointAt(len * i / n); pl.Append(X(q.X)).Append(',').Append(Y(q.Y)).Append(' '); }
            sb.Append($"<polyline points=\"{pl}\" fill=\"none\" stroke=\"#5f5e5a\" stroke-width=\"1.2\"><title>{H(p.Name)}</title></polyline>");
        }
        if (topLabels)
            foreach (var b in l.Blocks.Where(b => byName.TryGetValue(b.Name, out var d) && !double.IsNaN(d) && d > 0).OrderByDescending(b => byName[b.Name]).Take(3))
                sb.Append($"<text x=\"{X((b.Rect.Min.X + b.Rect.Max.X) / 2)}\" y=\"{Y((b.Rect.Min.Y + b.Rect.Max.Y) / 2)}\" font-size=\"11\" font-weight=\"600\" text-anchor=\"middle\" fill=\"#2c2c2a\" stroke=\"#fff\" stroke-width=\"3\" paint-order=\"stroke\">{H(b.Name)} {H(label(byName[b.Name]))}</text>");
        foreach (var c in l.Cps) sb.Append($"<circle cx=\"{X(c.Position.X)}\" cy=\"{Y(c.Position.Y)}\" r=\"2\" fill=\"#185fa5\"><title>{H(c.Name)}</title></circle>");
        sb.Append("</svg>");
        sb.Append($"<div class=\"muted\" style=\"font-size:12px\">색: 연회색 0 → 주황 → 빨강 최대({H(label(hi))}). block에 마우스를 올리면 값이 보입니다.</div>");
        return sb.ToString();
    }

    static void Detail(StringBuilder sb, JobInfo j, JobView? v)
    {
        if (v?.Input == null || v.Rows.Count == 0) { sb.Append("<div class=\"empty\">아직 실행 결과가 없습니다.</div>"); return; }
        sb.Append("<div class=\"box\"><div class=\"box-head\"><h2 title=\"파란 선은 그 대수로 돌린 실행들의 평균, 띠는 최소~최대(다른 그룹 대수와 시드가 달라 생기는 폭), 빨간 점선은 가동률 상한, 초록 세로선은 권장 대수, 빨간 점은 판정 실패가 있었던 대수입니다.\">그룹별 대수에 따른 가동률</h2>" +
            "<span class=\"t4\">파란 선 평균 · 띠 최소~최대 · 빨간 점선 상한 · 초록 선 권장 · 빨간 점 판정 실패</span></div><div class=\"curves\">");
        foreach (var g in v.Input.Groups)
        {
            if (!v.Curves.TryGetValue(g.Group, out var pts) || pts.Count == 0) continue;
            int? dec = j.Decided != null && j.Decided.TryGetValue(g.Group, out var d) ? d : null;
            sb.Append("<div>").Append(Curve(g.Group, g.MinCount, g.MaxCount, pts, v.UtilMax, dec)).Append("</div>");
        }
        sb.Append("</div></div>");
        if (j.Clusters is { Count: > 0 })
        {
            sb.Append("<div class=\"box\"><h2 title=\"경로나 block을 함께 쓰는 그룹은 서로 영향을 주므로 한 묶음으로 대수를 정했습니다.\">함께 탐색한 묶음</h2>");
            foreach (var c in j.Clusters) sb.Append($"<span class=\"chip\">{H(string.Join(", ", c))}</span>");
            sb.Append("</div>");
        }
        if (v.Impact.Count > 0)
        {
            var groups = v.Input.Groups.Select(x => x.Group).ToList();
            sb.Append("<div class=\"box\"><div class=\"box-head\"><h2 title=\"행 그룹을 권장 대수에서 1대 늘렸을 때 열 그룹의 가동률 변화(%p). 진할수록 영향이 큽니다.\">영향표</h2><span class=\"t4\">행 그룹 +1대 → 열 그룹 가동률 변화(%p) · 진할수록 영향 큼</span></div>" +
                "<div class=\"scroll\"><table><tr><th>+1대 한 그룹 → 가동률 변화(%p)</th>");
            foreach (var g in groups) sb.Append($"<th>{H(g)}</th>");
            sb.Append("</tr>");
            double hi = v.Impact.Select(r => Math.Abs(r.UtilDelta)).Where(x => !double.IsNaN(x)).DefaultIfEmpty(0).Max();
            foreach (var row in v.Impact.GroupBy(r => r.Group))
            {
                sb.Append($"<tr><th>{H(row.Key)} ({row.First().PlusOne}대)</th>");
                foreach (var g in groups)
                {
                    var c = row.FirstOrDefault(r => r.OtherGroup == g);
                    if (c == null) { sb.Append("<td class=\"muted\">-</td>"); continue; }
                    var bg = Heat(hi > 0 ? Math.Abs(c.UtilDelta) / hi : 0);
                    sb.Append($"<td class=\"num\" style=\"background:{bg}\">{F(c.UtilDelta * 100, "+0.0;-0.0;0")}</td>");
                }
                sb.Append("</tr>");
            }
            sb.Append("</table></div></div>");
        }
        if (v.Vehicles.Count > 0)
        {
            bool down = v.Vehicles.Any(x => x.Down > 0);
            sb.Append("<div class=\"box\"><div class=\"box-head\"><h2>차량별</h2><span class=\"t4\">권장 대수만큼 움직인 차량의 확인 실행 평균</span></div><div class=\"scroll\"><table><tr><th>차량</th><th>그룹</th><th>가동률(%)</th><th>작업 수(회)</th><th>주행 거리(m)</th><th>거리/작업(m)</th><th>시간/작업(초)</th>" +
                (down ? "<th>고장 시간(초)</th>" : "") + "</tr>");
            foreach (var x in v.Vehicles)
                sb.Append($"<tr><td>{H(x.Name)}</td><td>{H(x.Group)}</td><td class=\"num\">{P(x.Util)}</td><td class=\"num\">{F(x.Jobs, "0.#")}</td><td class=\"num\">{F(x.Distance, "0")}</td>" +
                    $"<td class=\"num\">{F(x.DistPerJob, "0.#")}</td><td class=\"num\">{F(x.BusyPerJob, "0.#")}</td>" + (down ? $"<td class=\"num\">{F(x.Down, "0")}</td>" : "") + "</tr>");
            sb.Append("</table></div></div>");
        }
        if (v.Materials.Count > 0) MaterialTable(sb, v);
        // one row per run and group; every group's count of the run is a column, so the list sorts by any of them
        var gs = v.Input.Groups;
        var counts = v.RunCounts();
        sb.Append("<div class=\"box\"><div class=\"box-head\"><h2>실행 목록</h2><span class=\"t4\" title=\"받기: 그 실행의 모델과 조건(params.txt)을 zip으로 받아 AutoMod가 있는 PC에서 run.bat으로 창을 띄워 볼 수 있습니다.\">제목을 누르면 정렬 · 받기 = 모델 zip</span></div>");
        sb.Append("<div class=\"scroll\"><table class=\"sort\"><thead><tr>").Append(Th("실행"));
        foreach (var g in gs) sb.Append(Th(H(g.Group) + " 대수(대)"));
        sb.Append(Th("시드")).Append(Th("반복(회차)")).Append(Th("그룹")).Append(Th("판정")).Append(Th("가동률(%)")).Append(Th("최소 재고/안전재고(배)")).Append(Th("공급(회)"))
          .Append(Th("기대 공급(회)")).Append(Th("결품 시간(초)")).Append(Th("교착(회)")).Append(Th("실행 시간(초)")).Append("<th>모델</th></tr></thead><tbody>");
        foreach (var r in v.Rows.Take(1000))
        {
            // the id spells out every group's count, which the columns after it already show; keep only the tail (seed, flags) and put the full id in the tooltip
            var us = r.RunId.IndexOf('_');
            var warnOnly = JobView.UtilOnly(r);
            sb.Append($"<tr{(r.Ok ? "" : warnOnly ? " class=\"ow\"" : " class=\"over\"")}><td title=\"{H(r.RunId)}\">{(us > 0 ? "…" + H(r.RunId[us..]) : H(r.RunId))}</td>");
            var c = counts[r.RunId];
            foreach (var g in gs) sb.Append($"<td class=\"num\">{(c.TryGetValue(g.Group, out var n) ? n : g.MinCount)}</td>");
            sb.Append($"<td class=\"num\">{r.Seed}</td><td class=\"num\">{r.Seed - v.Input.Settings.SeedBase + 1}</td><td>{H(r.Group)}</td><td class=\"{(r.Ok ? "ok" : warnOnly ? "wn" : "bad")}\">{(r.Ok ? "통과" : warnOnly ? "⚠ 경고: " + H(r.Reason) : H(r.Reason))}</td>" +
                $"<td class=\"num\">{P(r.Utilization)}</td><td class=\"num\">{F(r.MinStockRatio)}</td><td class=\"num\">{r.Supplies}</td><td class=\"num\">{F(r.Expected, "0.#")}</td><td class=\"num\">{F(r.StopTimeSec, "0")}</td><td class=\"num\">{r.Deadlocks}</td><td class=\"num\">{F(r.DurationSec, "0")}</td>" +
                $"<td><a href=\"/jobs/{H(j.Id)}/runs/{Uri.EscapeDataString(r.RunId)}/model.zip\" download>받기</a></td></tr>");
        }
        sb.Append("</tbody></table></div>").Append(SortScript);
        if (v.Rows.Count > 1000) sb.Append($"<div class=\"t4\">처음 1000행만 보여 주고 그 안에서 정렬합니다(전체 {v.Rows.Count}행은 summary.tsv로 받으세요).</div>");
        sb.Append("</div>");
    }

    /// <summary>Mean interval between a material's supply jobs over the measured window; NaN without jobs.</summary>
    internal static double SupplyInterval(JobView v, MaterialView m) =>
        v.Input == null || !(m.Trips > 0) ? double.NaN : (v.Input.Settings.SimLengthSec - v.Input.Settings.WarmupSec) / m.Trips;

    /// <summary>Parts per container of a material from the input; NaN when unknown.</summary>
    internal static double PerContainer(JobView v, MaterialView m) => v.Input?.Materials.FirstOrDefault(x => x.Material == m.Name)?.UnitsPerContainer ?? double.NaN;

    /// <summary>상세 tab, one row per material: supply jobs, their interval and distance, lead time, stock in containers and in parts. Models built
    /// before the supply columns leave those cells "-".</summary>
    static void MaterialTable(StringBuilder sb, JobView v)
    {
        sb.Append("<div class=\"box\"><div class=\"box-head\"><h2>자재별</h2><span class=\"t4\" title=\"공급 빈도 = 측정 시간 ÷ 공급 횟수. 공급당 이동 거리 = 데리러 가는 길부터 대기 CP 복귀까지. 리드 타임 = 호출부터 하차 완료까지. 재고는 takt마다 잰 값, 최소·최대는 확인 실행 전체의 극값.\">확인 실행 평균 · 재고 평균 / 최소 / 최대</span></div>" +
            "<div class=\"scroll\"><table class=\"sort\"><thead><tr>").Append(Th("자재")).Append(Th("그룹")).Append(Th("공급 횟수(회)")).Append(Th("공급 빈도(초)")).Append(Th("공급당 이동 거리(m)"))
          .Append(Th("리드 타임 평균(초)")).Append(Th("리드 타임 최대(초)")).Append(Th("용기 수량(용기)")).Append(Th("자재 수량(개)")).Append("</tr></thead><tbody>");
        foreach (var m in v.Materials)
        {
            var per = PerContainer(v, m);
            string Three(double k, string f) => $"{F(m.StockAvg * k, f)} / {F(m.StockMin * k, f)} / {F(m.StockMax * k, f)}";
            sb.Append($"<tr><td>{H(m.Name)}</td><td>{H(m.Group)}</td><td class=\"num\">{F(m.Trips, "0.#")}</td><td class=\"num\">{F(SupplyInterval(v, m), "0")}</td><td class=\"num\">{F(m.DistPerTrip, "0.#")}</td>" +
                $"<td class=\"num\">{F(m.DelivAvg, "0.0")}</td><td class=\"num\">{F(m.DelivMax, "0.0")}</td><td class=\"num\">{Three(1, "0.#")}</td><td class=\"num\">{Three(per, "0")}</td></tr>");
        }
        sb.Append("</tbody></table></div></div>");
    }

    static string Curve(string group, int min, int max, IReadOnlyList<CountPoint> pts, double utilMax, int? decided)
    {
        double w = 360, h = 220, l = 44, r = 12, t = 22, b = 34;
        int lo = Math.Min(min, pts.Min(p => p.Active)), hi = Math.Max(max, pts.Max(p => p.Active));
        if (hi == lo) hi = lo + 1;
        string X(double a) => F(l + (a - lo) / (hi - lo) * (w - l - r), "0.#");
        string Y(double u) => F(h - b - Math.Clamp(double.IsNaN(u) ? 0 : u, 0, 1) * (h - t - b), "0.#");
        var sb = new StringBuilder($"<svg width=\"{w}\" height=\"{h}\" viewBox=\"0 0 {w} {h}\" style=\"background:#fff;border:1px solid var(--line)\">");
        sb.Append($"<text x=\"{l}\" y=\"15\" font-size=\"13\" font-weight=\"700\">{H(group)}</text>");
        foreach (var u in new[] { 0, 0.25, 0.5, 0.75, 1.0 })
            sb.Append($"<line x1=\"{l}\" x2=\"{w - r}\" y1=\"{Y(u)}\" y2=\"{Y(u)}\" stroke=\"#ececee\"/><text x=\"{l - 6}\" y=\"{Y(u)}\" font-size=\"10\" text-anchor=\"end\" dominant-baseline=\"middle\" fill=\"#636366\">{(u * 100):0}%</text>");
        for (int a = lo; a <= hi; a++)
            sb.Append($"<text x=\"{X(a)}\" y=\"{h - b + 14}\" font-size=\"10\" text-anchor=\"middle\" fill=\"#636366\">{a}</text>");
        sb.Append($"<text x=\"{F((l + w - r) / 2)}\" y=\"{h - 6}\" font-size=\"10\" text-anchor=\"middle\" fill=\"#636366\">대수</text>");
        var ok = pts.Where(p => !double.IsNaN(p.Mean)).ToList();
        if (ok.Count > 0)
        {
            var band = string.Join(" ", ok.Select(p => X(p.Active) + "," + Y(p.Hi)).Concat(ok.AsEnumerable().Reverse().Select(p => X(p.Active) + "," + Y(p.Lo))));
            sb.Append($"<polygon points=\"{band}\" fill=\"#cfe3ff\" opacity=\"0.7\"/>");
            sb.Append($"<polyline points=\"{string.Join(" ", ok.Select(p => X(p.Active) + "," + Y(p.Mean)))}\" fill=\"none\" stroke=\"#007aff\" stroke-width=\"2\"/>");
            foreach (var p in ok)
                sb.Append($"<circle cx=\"{X(p.Active)}\" cy=\"{Y(p.Mean)}\" r=\"{(p.Failed > 0 ? 4 : 2.5)}\" fill=\"{(p.Failed > 0 ? "#d70015" : "#007aff")}\"><title>{p.Active}대: 평균 {Pct(p.Mean)} ({p.Runs}회, 실패 {p.Failed}회)</title></circle>");
        }
        sb.Append($"<line x1=\"{l}\" x2=\"{w - r}\" y1=\"{Y(utilMax)}\" y2=\"{Y(utilMax)}\" stroke=\"#d70015\" stroke-dasharray=\"5 4\"/>");
        if (decided is { } d) sb.Append($"<line x1=\"{X(d)}\" x2=\"{X(d)}\" y1=\"{t}\" y2=\"{h - b}\" stroke=\"#248a3d\" stroke-width=\"2\"/><text x=\"{X(d)}\" y=\"{t - 4}\" font-size=\"11\" font-weight=\"600\" text-anchor=\"middle\" fill=\"#248a3d\">권장 {d}</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    static void InputTab(StringBuilder sb, JobInfo j, JobView? v)
    {
        sb.Append("<div class=\"box\"><h2>이 결과를 만든 입력</h2><div class=\"scroll\"><table>");
        sb.Append($"<tr><th>작업</th><td>{H(j.Id)}</td></tr><tr><th>프로젝트 / 안</th><td>{H(j.Project)} / {H(j.Plan)}</td></tr><tr><th>모델</th><td>{H(j.Model)}</td></tr>");
        sb.Append($"<tr><th>보낸 사람</th><td>{H(j.Submitter)} ({H(j.Ip)})</td></tr><tr><th>보낸 시각 / 시작 / 끝(서울)</th><td>{H(When(j.CreatedUtc))} / {H(When(j.StartedUtc))} / {H(When(j.FinishedUtc))}</td></tr>");
        if (v?.Input is { } inp)
        {
            var s = inp.Settings;
            sb.Append($"<tr><th>반복 / 시드 시작</th><td>{s.Replications}회 / {s.SeedBase} (탐색 중 반복 {j.InitialReps}회)</td></tr>");
            sb.Append($"<tr><th>시뮬레이션 / 워밍업</th><td>{F(s.SimLengthSec / 3600)}시간 / {F(s.WarmupSec / 3600)}시간</td></tr>");
            sb.Append($"<tr><th>가동률 상한 / 안전재고</th><td>{Pct(s.UtilMax)} / {F(s.SafetyStock)}용기</td></tr>");
            sb.Append($"<tr><th>영향표</th><td>{(j.Impact ? "켬" : "끔")}</td></tr>");
            sb.Append($"<tr><th>곡선 속도</th><td>{H(ASH.Core.Model.CurveSpeed.Note)}</td></tr>");
        }
        else if (v?.InputError != null) sb.Append($"<tr><th>입력</th><td class=\"bad\">▲ {H(v.InputError)}</td></tr>");
        sb.Append("</table></div></div>");
        sb.Append("<div class=\"box\"><h2>올린 파일 (SHA-256)</h2><div class=\"scroll\"><table><tr><th>파일</th><th>크기(바이트)</th><th>SHA-256</th></tr>");
        foreach (var f in j.Files) sb.Append($"<tr><td>{H(f.Path)}</td><td class=\"num\">{f.Size:N0}</td><td><code>{H(f.Sha256)}</code></td></tr>");
        sb.Append("</table></div></div>");
        if (v?.Input != null)
        {
            sb.Append("<div class=\"box\"><h2>그룹</h2><div class=\"scroll\"><table><tr><th>그룹</th><th>타입</th><th>최소(대)</th><th>최대(대)</th><th>대기 CP</th></tr>");
            foreach (var g in v.Input.Groups) sb.Append($"<tr><td>{H(g.Group)}</td><td>{H(g.Type)}</td><td class=\"num\">{g.MinCount}</td><td class=\"num\">{g.MaxCount}</td><td>{H(string.Join(", ", g.StandbyCPs))}</td></tr>");
            sb.Append("</table></div></div><div class=\"box\"><h2>AGV 타입</h2><div class=\"scroll\"><table><tr><th>타입</th><th>에셋</th></tr>");
            foreach (var t in v.Input.AgvTypes) sb.Append($"<tr><td>{H(t.Type)}</td><td>{H(string.IsNullOrWhiteSpace(t.Asset) ? "기본 그림" : t.Asset)}</td></tr>");
            var curved = v.Input.AgvTypes.Where(t => t.SpeedLoadedCurve > 0 || t.SpeedEmptyCurve > 0).ToList();
            sb.Append("</table></div>");
            if (curved.Count > 0)
            {
                sb.Append("<h2 style=\"margin-top:18px\">곡선 속도</h2><div class=\"scroll\"><table><tr><th>타입</th><th>적재 곡선(m/s)</th><th>공차 곡선(m/s)</th></tr>");
                foreach (var t in curved) sb.Append($"<tr><td>{H(t.Type)}</td><td class=\"num\">{(t.SpeedLoadedCurve > 0 ? F(t.SpeedLoadedCurve!.Value) : "직선과 같음")}</td><td class=\"num\">{(t.SpeedEmptyCurve > 0 ? F(t.SpeedEmptyCurve!.Value) : "직선과 같음")}</td></tr>");
                sb.Append("</table></div>");
            }
            sb.Append("</div><div class=\"box\"><h2>자재</h2><div class=\"scroll\"><table><tr><th>자재</th><th>그룹</th><th>형태</th><th>T/T(초)</th><th>용기당 수량(개)</th><th>호출 임계(용기)</th></tr>");
            foreach (var m in v.Input.Materials) sb.Append($"<tr><td>{H(m.Material)}</td><td>{H(m.Group)}</td><td>{H(m.Form.Length == 0 ? "Box" : m.Form)}</td><td class=\"num\">{F(m.TaktTime)}</td><td class=\"num\">{F(m.UnitsPerContainer)}</td><td class=\"num\">{F(m.CallThreshold)}</td></tr>");
            sb.Append("</table></div></div>");
        }
    }

    static void FilesTab(StringBuilder sb, JobInfo j, string logTail)
    {
        string Link(string name, string desc) => $"<a class=\"row\" href=\"/jobs/{H(j.Id)}/files/{name}\"><div class=\"t\"><div>{name}</div><small>{desc}</small></div><span class=\"btn\">받기</span></a>";
        sb.Append("<div class=\"box\"><h2>내려받기</h2><div class=\"files\">");
        sb.Append(Link("report.xlsx", "Excel 보고서(요약, 대수별 가동률, 실행 목록, 영향표, block, 입력)"));
        sb.Append(Link("summary.tsv", "실행별·그룹별 결과"));
        sb.Append(Link("impact.tsv", "영향표"));
        sb.Append(Link("runs.zip", j.RunsPurgedUtc is { } purged ? $"모든 실행 폴더(결과·보고서·메시지), {SeoulTime.Text(purged)}에 압축 보관" : "모든 실행 폴더(결과·보고서·메시지)"));
        sb.Append(Link("upload.zip", "올린 원본"));
        sb.Append(Link("job.log", "작업 로그 전체"));
        sb.Append("</div></div>");
        if (j.FailureMessages.Count > 0)
        {
            sb.Append("<div class=\"box\"><h2>실패한 실행</h2><ul>");
            foreach (var m in j.FailureMessages) sb.Append($"<li>▲ {H(m)}</li>");
            sb.Append("</ul></div>");
        }
        sb.Append("<div class=\"box\"><h2>로그 <span class=\"t4\">끝부분</span></h2><pre>").Append(H(logTail)).Append("</pre></div>");
    }
}
