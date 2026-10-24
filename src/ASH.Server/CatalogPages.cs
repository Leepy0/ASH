using System.Globalization;
using System.Text;
using ASH.Core.Catalog;
using ASH.Core.Input;
using static ASH.Server.JobPages;

namespace ASH.Server;

/// <summary>/admin/types: master-detail (type list | grouped edit form), the Excel import panel with a preview, the prices.csv editor. Same shell and CSS as the other pages, nothing loaded from outside.</summary>
public static class CatalogPages
{
    /// <summary>One row of an import preview: State is "새로", "변경" or "동일"; Old/OldPrice are what the server has now (null for a new type).</summary>
    public sealed record PreviewRow(string State, CatalogType Type, decimal? Price, CatalogType? Old, decimal? OldPrice);

    /// <summary>What a paste would do, shown without saving anything.</summary>
    public sealed record PastePreview(IReadOnlyList<PreviewRow> Rows, IReadOnlyList<string> Errors);

    const string Css = @"<style>
.tb{display:flex;align-items:center;gap:8px;margin-bottom:10px;position:relative;flex-wrap:wrap}.tb .sp{flex:1}
.md{display:grid;grid-template-columns:300px minmax(0,1fr);gap:16px;align-items:start}
.mlist{background:#fff;border:1px solid var(--line);border-radius:12px;overflow:auto;max-height:calc(100vh - 240px)}
.trow{display:flex;align-items:center;gap:8px;min-height:52px;padding:6px 14px 6px 10px;border-top:1px solid var(--line);border-left:4px solid transparent;color:var(--ink);text-decoration:none}
.trow:first-child{border-top:0}.trow:hover{background:#f7f7f9}.trow .t{flex:1;min-width:0}.trow .t b{display:block;overflow-wrap:anywhere}.trow small{color:var(--sub2);font-size:13px}
.trow .pr{font-variant-numeric:tabular-nums;color:var(--sub2);font-size:13px;white-space:nowrap}
.trow.on{background:var(--blue-b);border-left-color:var(--blue)}.trow.on b{color:var(--blue-d)}
.dh{display:flex;align-items:center;gap:8px;margin-bottom:2px}.dh .sp{flex:1}
.md .sgrp{max-width:none}.md .srow .ctl{flex-basis:340px}.md .srow .ctl input.n{width:120px}
.imp>summary{list-style:none;margin:0}.imp>summary::-webkit-details-marker{display:none}
.imp-p{position:absolute;right:0;top:42px;width:min(780px,92vw);background:#fff;border:1px solid var(--line2);border-radius:14px;box-shadow:0 12px 40px rgba(0,0,0,.22);padding:18px;z-index:20}
.imp-p h3{margin:0 0 4px}.imp-p textarea{width:100%;max-width:none;font-family:Consolas,monospace;white-space:pre;margin-top:10px}
.imp-p table{width:100%;margin-top:8px}.imp-p .pvt{display:flex;gap:6px;margin-top:10px}.imp-p ul{margin:8px 0 0;padding-left:20px;color:#8f000e}
.s-new{color:var(--green);font-weight:600}.s-chg{color:var(--blue-d);font-weight:600}.s-same{color:var(--sub2)}
.aopts{display:flex;flex-wrap:wrap;gap:8px 14px}.aopts label{display:inline-flex;align-items:center;gap:6px}.aopts input,.aopts select{width:auto;min-height:30px}
@media(max-width:900px){.md{grid-template-columns:1fr}.mlist{max-height:260px}}
</style>";

    static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>List price, short: 12,000,000 becomes 1,200만.</summary>
    static string Short(decimal p) => p >= 10000 ? (p / 10000).ToString("#,0.##", CultureInfo.InvariantCulture) + "만" : p.ToString("#,0.##", CultureInfo.InvariantCulture);

    static string KindLabel(string kind) => VehicleKinds.All.FirstOrDefault(k => k.Name.Equals(kind, StringComparison.OrdinalIgnoreCase))?.Label ?? kind;

    /// <param name="editing">the type shown in the detail pane: null = the first type; a type with an empty name = a new (or cloned) one</param>
    /// <param name="paste">text to put back in the paste box after a rejected paste</param>
    /// <param name="preview">the result of "미리보기": counts and rows, nothing saved yet</param>
    public static string Types(IReadOnlyList<CatalogType> types, IReadOnlyDictionary<string, PriceRow> prices, string pricesCsv, CatalogType? editing, string? msgHtml, ASH.Server.Catalog.AssetStore assets, string? paste = null, PastePreview? preview = null)
    {
        var sb = new StringBuilder(Css).Append(ServerPages.AdminHead("catalog")).Append("<div class=\"panel\">");
        if (msgHtml != null) sb.Append(msgHtml);
        var e = editing ?? types.FirstOrDefault() ?? new CatalogType { Kind = "Standard", Defaults = { Capacity = 1 } };

        // toolbar: 양식 받기 · 엑셀에서 가져오기 (a panel, open after a preview or a rejected paste) · + new type
        sb.Append($"<div class=\"tb\"><div class=\"t2\">차량 타입 <span class=\"t4\">· {types.Count}</span></div><span class=\"sp\"></span>")
          .Append("<a class=\"btn\" href=\"/admin/types/template.xlsx\">양식 받기</a>")
          .Append($"<details class=\"imp\"{(paste != null || preview != null ? " open" : "")}><summary class=\"btn\">엑셀에서 가져오기</summary><div class=\"imp-p\">");
        ImportPanel(sb, paste, preview);
        sb.Append("</div></details><a class=\"btn\" href=\"/admin/types?new=1\" aria-label=\"새 타입\" title=\"새 타입\">+</a></div>");

        sb.Append("<div class=\"md\"><nav class=\"mlist\" aria-label=\"타입 목록\">");
        foreach (var t in types)
        {
            var on = t.Name.Equals(e.Name, StringComparison.OrdinalIgnoreCase) && e.Name.Length > 0;
            sb.Append($"<a class=\"trow{(on ? " on" : "")}\" href=\"/admin/types?edit={Uri.EscapeDataString(t.Name)}\"{(on ? " aria-current=\"true\"" : "")}><div class=\"t\"><b>{H(on ? "✓ " + t.Name : t.Name)}</b>")
              .Append($"<small>{H(t.Label.Length > 0 ? t.Label + " · " : "")}{H(KindLabel(t.Kind))}{(t.Hidden ? " · 숨김" : "")}</small></div>")
              .Append($"<span class=\"pr\">{(prices.TryGetValue(t.Name, out var p) ? Short(p.UnitPrice) : "—")}</span></a>");
        }
        if (types.Count == 0) sb.Append("<div class=\"empty\" style=\"margin:10px\">등록된 타입이 없습니다.</div>");
        sb.Append("</nav><div>");
        Detail(sb, prices, e);
        sb.Append("</div></div>");

        sb.Append($"<section class=\"box\"><details style=\"margin:0\"><summary style=\"margin:0\">투자비 CSV 직접 고치기(prices.csv)</summary><form method=\"post\" action=\"/admin/types/prices\"><textarea name=\"csv\" rows=\"6\" style=\"width:100%;max-width:none;font-family:monospace\">{H(pricesCsv)}</textarea><p><button type=\"submit\">투자비 저장</button> <span class=\"muted\">Type,UnitPrice,Currency,Note</span></p></form></details></section>");
        sb.Append(AssetPages.Section(types, assets)).Append("</div>");
        return Page("ASH 기준 정보", sb.ToString(), nav: "admin");
    }

    static void ImportPanel(StringBuilder sb, string? paste, PastePreview? pv)
    {
        sb.Append("<form method=\"post\" action=\"/admin/types/paste\"><h3>엑셀에서 가져오기</h3>")
          .Append("<div class=\"t4\">양식(지금 타입이 들어 있음)을 고치거나 줄을 더한 뒤, 머리글 줄까지 복사해 붙여넣으세요. 단가(원)를 비우면 투자비는 그대로입니다. 이름이 같으면 고치고 없으면 새로 만들며, 한 줄이라도 틀리면 아무것도 저장하지 않습니다.</div>");
        if (pv != null)
        {
            int nNew = pv.Rows.Count(r => r.State == "새로"), nChg = pv.Rows.Count(r => r.State == "변경"), nSame = pv.Rows.Count(r => r.State == "동일");
            sb.Append($"<div class=\"pvt\"><span class=\"tag green\">새로 {nNew}</span><span class=\"tag blue\">변경 {nChg}</span>{(nSame > 0 ? $"<span class=\"tag gray\">동일 {nSame}</span>" : "")}<span class=\"tag {(pv.Errors.Count > 0 ? "red" : "gray")}\">{(pv.Errors.Count > 0 ? "▲ " : "")}오류 {pv.Errors.Count}</span></div>");
            if (pv.Rows.Count > 0)
            {
                sb.Append("<div class=\"scroll\"><table><thead><tr><th></th><th>이름</th><th>라벨</th><th>형상</th><th class=\"num\">적재 직선</th><th class=\"num\">투자비(원)</th></tr></thead><tbody>");
                foreach (var r in pv.Rows)
                {
                    var (t, o) = (r.Type, r.Old);
                    var cls = r.State == "새로" ? "s-new" : r.State == "변경" ? "s-chg" : "s-same";
                    var price = r.Price ?? r.OldPrice;
                    sb.Append($"<tr><td class=\"{cls}\">{r.State}</td><td>{H(t.Name)}</td><td>{H(t.Label)}</td><td>{H(KindLabel(t.Kind))}</td>")
                      .Append($"<td class=\"num\">{N(t.Defaults.SpeedLoadedStraight)}{(o != null && o.Defaults.SpeedLoadedStraight != t.Defaults.SpeedLoadedStraight ? $" <span class=\"muted\">← {N(o.Defaults.SpeedLoadedStraight)}</span>" : "")}</td>")
                      .Append($"<td class=\"num\">{(price is { } pr ? pr.ToString("#,0.##", CultureInfo.InvariantCulture) : "")}{(r.Price != null && r.OldPrice != null && r.Price != r.OldPrice ? $" <span class=\"muted\">← {r.OldPrice.Value.ToString("#,0.##", CultureInfo.InvariantCulture)}</span>" : "")}</td></tr>");
                }
                sb.Append("</tbody></table></div>");
            }
            if (pv.Errors.Count > 0)
            {
                sb.Append("<ul role=\"alert\">");
                foreach (var er in pv.Errors) sb.Append($"<li>{H(er)}</li>");
                sb.Append("</ul>");
            }
        }
        sb.Append($"<textarea name=\"rows\" rows=\"7\" spellcheck=\"false\" aria-label=\"붙여넣기\" placeholder=\"{H(CatalogSheet.HeaderLine.Replace("	", "  "))}\">{H(paste ?? "")}</textarea>")
          .Append("<div class=\"actions\" style=\"justify-content:flex-end\"><button type=\"submit\" class=\"btn\" formaction=\"/admin/types/paste?preview=1\">미리보기</button>")
          .Append($"<button type=\"submit\" class=\"btn pri\"{(pv != null && (pv.Errors.Count > 0 || pv.Rows.Count == 0) ? " disabled" : "")}>가져오기</button></div></form>");
    }

    /// <summary>One grouped row "label ……… [input] unit"; <paramref name="tip"/> goes into the (i) title.</summary>
    static void Row(StringBuilder sb, string name, string label, string value, string? unit = null, string? tip = null, bool wide = false) =>
        sb.Append($"<div class=\"srow\"><label for=\"{name}\">{label}{(tip == null ? "" : $" <span class=\"info\" tabindex=\"0\" role=\"img\" title=\"{H(tip)}\" aria-label=\"{H(tip)}\">i</span>")}</label>")
          .Append($"<div class=\"ctl\"><input type=\"text\" name=\"{name}\" id=\"{name}\" value=\"{value}\"{(wide ? "" : " class=\"n\" inputmode=\"decimal\"")} spellcheck=\"false\">{(unit == null ? "" : $"<span class=\"unit\">{unit}</span>")}</div></div>");

    static void Group(StringBuilder sb, string title, Action body)
    {
        sb.Append($"<div class=\"sec\">{title}</div><div class=\"sgrp\">");
        body();
        sb.Append("</div>");
    }

    static void Detail(StringBuilder sb, IReadOnlyDictionary<string, PriceRow> prices, CatalogType e)
    {
        var ed = e.Defaults;
        var isNew = e.Name.Length == 0;
        prices.TryGetValue(e.Name, out var ep);
        sb.Append("<form method=\"post\" action=\"/admin/types\"><div class=\"dh\">")
          .Append($"<div class=\"t2\">{(isNew ? "새 타입" : H(e.Name))}</div><span class=\"sp\"></span>")
          .Append(isNew ? "" : $"<a class=\"btn\" href=\"/admin/types?clone={Uri.EscapeDataString(e.Name)}\">복제</a>")
          .Append("<button type=\"submit\" class=\"primary\">저장</button></div>");
        Group(sb, "기본", () =>
        {
            Row(sb, "name", "이름", H(e.Name), tip: "AutoMod 타입 이름. 영문·숫자·_만. 이름을 바꿔 저장하면 새 타입이 만들어집니다.", wide: true);
            Row(sb, "label", "라벨", H(e.Label), wide: true);
            Row(sb, "category", "분류", H(e.Category), wide: true);
            sb.Append("<div class=\"srow\"><label for=\"kind\">형상</label><div class=\"ctl\"><select name=\"kind\" id=\"kind\">");
            foreach (var k in VehicleKinds.All) sb.Append($"<option value=\"{k.Name}\"{(k.Name.Equals(e.Kind, StringComparison.OrdinalIgnoreCase) ? " selected" : "")}>{H(k.Label)} ({k.Name})</option>");
            sb.Append("</select></div></div>");
            sb.Append($"<div class=\"srow\"><label for=\"hidden\">숨김 <span class=\"info\" tabindex=\"0\" role=\"img\" title=\"플러그인 목록에서 제외\" aria-label=\"플러그인 목록에서 제외\">i</span></label><div class=\"ctl\"><span class=\"sw\"><input type=\"checkbox\" role=\"switch\" name=\"hidden\" value=\"1\" id=\"hidden\"{(e.Hidden ? " checked" : "")}></span></div></div>");
        });
        Group(sb, "속도 (m/s)", () =>
        {
            Row(sb, "speedLoadedStraight", "적재 직선", N(ed.SpeedLoadedStraight), "m/s");
            Row(sb, "speedLoadedCurve", "적재 곡선", N(ed.SpeedLoadedCurve), "m/s");
            Row(sb, "speedEmptyStraight", "공차 직선", N(ed.SpeedEmptyStraight), "m/s");
            Row(sb, "speedEmptyCurve", "공차 곡선", N(ed.SpeedEmptyCurve), "m/s");
            Row(sb, "accel", "가속", N(ed.Accel), "m/s²");
            Row(sb, "decel", "감속", N(ed.Decel), "m/s²");
        });
        Group(sb, "작업", () =>
        {
            Row(sb, "capacity", "적재 수량", ed.Capacity.ToString(CultureInfo.InvariantCulture), "개", "한 번에 싣는 용기 수(1 이상의 정수)");
            Row(sb, "loadTime", "상차 시간", N(ed.LoadTime), "초");
            Row(sb, "unloadTime", "하차 시간", N(ed.UnloadTime), "초");
        });
        Group(sb, "크기", () =>
        {
            Row(sb, "length", "길이", N(ed.Length), "m");
            Row(sb, "width", "폭", N(ed.Width), "m");
            Row(sb, "color", "색", H(ed.Color), tip: "AutoMod 색 이름(비워도 됨)", wide: true);
        });
        Group(sb, "고장", () =>
        {
            Row(sb, "mtbf", "MTBF", N(ed.MTBF), "초", "0 = 고장 없음");
            Row(sb, "mttr", "MTTR", N(ed.MTTR), "초");
        });
        Group(sb, "투자비", () => Row(sb, "unitPrice", "단가", ep == null ? "" : ep.UnitPrice.ToString("0.##", CultureInfo.InvariantCulture), "원", "비우면 그대로"));
        sb.Append("</form>");
    }
}
