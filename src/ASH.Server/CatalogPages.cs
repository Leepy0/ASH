using System.Globalization;
using System.Text;
using ASH.Core.Catalog;
using ASH.Core.Input;
using static ASH.Server.JobPages;

namespace ASH.Server;

/// <summary>/admin/types: the type table, the edit form, the prices.csv editor. Same shell and CSS as the other pages, nothing loaded from outside.</summary>
public static class CatalogPages
{
    static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public static string Types(IReadOnlyList<CatalogType> types, IReadOnlyDictionary<string, PriceRow> prices, string pricesCsv, CatalogType? editing, string? msgHtml, ASH.Server.Catalog.AssetStore assets)
    {
        var sb = new StringBuilder("<h1>타입·투자비</h1>");
        if (msgHtml != null) sb.Append(msgHtml);
        sb.Append("<section class=\"box\"><h2>타입</h2><p><a href=\"/admin/types?edit=\">새 타입</a></p><div style=\"overflow-x:auto\"><table><thead><tr>");
        foreach (var h in new[] { "이름", "라벨", "분류", "형상", "적재 직선", "적재 곡선", "공차 직선", "공차 곡선", "가속", "감속", "용기", "상차", "하차", "길이", "폭", "색", "단가", "에셋", "숨김", "" })
            sb.Append("<th>").Append(h).Append("</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var t in types)
        {
            var d = t.Defaults;
            var n = Uri.EscapeDataString(t.Name);
            sb.Append($"<tr><td><a href=\"/admin/types?edit={n}\">{H(t.Name)}</a></td><td>{H(t.Label)}</td><td>{H(t.Category)}</td><td>{H(t.Kind)}</td>");
            foreach (var v in new[] { d.SpeedLoadedStraight, d.SpeedLoadedCurve, d.SpeedEmptyStraight, d.SpeedEmptyCurve, d.Accel, d.Decel }) sb.Append($"<td class=\"num\">{N(v)}</td>");
            sb.Append($"<td class=\"num\">{d.Capacity}</td><td class=\"num\">{N(d.LoadTime)}</td><td class=\"num\">{N(d.UnloadTime)}</td><td class=\"num\">{N(d.Length)}</td><td class=\"num\">{N(d.Width)}</td><td>{H(d.Color)}</td>");
            sb.Append(prices.TryGetValue(t.Name, out var p) ? $"<td class=\"num\">{p.UnitPrice.ToString("0.##", CultureInfo.InvariantCulture)} {H(p.Currency)}</td>" : "<td class=\"muted\">-</td>");
            sb.Append($"<td class=\"num\">{t.Assets.Count}</td><td>{(t.Hidden ? "숨김" : "")}</td><td><a href=\"/admin/types?clone={n}\">복제</a></td></tr>");
        }
        if (types.Count == 0) sb.Append("<tr><td colspan=\"20\" class=\"muted\">등록된 타입이 없습니다.</td></tr>");
        sb.Append("</tbody></table></div></section>");

        var e = editing ?? new CatalogType { Kind = "Standard", Defaults = { Capacity = 1 } };
        var ed = e.Defaults;
        prices.TryGetValue(e.Name, out var ep);
        sb.Append($"<section class=\"box\"><h2>{(editing == null ? "새 타입" : H(e.Name.Length == 0 ? "복제" : e.Name + " 수정"))}</h2><form method=\"post\" action=\"/admin/types\">");
        Field(sb, "name", "이름", H(e.Name), "text", "영문·숫자·_만, AutoMod 타입 이름");
        Field(sb, "label", "라벨", H(e.Label));
        Field(sb, "category", "분류", H(e.Category));
        sb.Append("<div class=\"field\"><label for=\"kind\">형상</label><select name=\"kind\" id=\"kind\">");
        foreach (var k in VehicleKinds.All) sb.Append($"<option value=\"{k.Name}\"{(k.Name.Equals(e.Kind, StringComparison.OrdinalIgnoreCase) ? " selected" : "")}>{H(k.Label)} ({k.Name})</option>");
        sb.Append("</select></div>");
        foreach (var (n, l, v) in new[] { ("speedLoadedStraight", "적재 직선 속도", ed.SpeedLoadedStraight), ("speedLoadedCurve", "적재 곡선 속도", ed.SpeedLoadedCurve), ("speedEmptyStraight", "공차 직선 속도", ed.SpeedEmptyStraight),
                     ("speedEmptyCurve", "공차 곡선 속도", ed.SpeedEmptyCurve), ("accel", "가속", ed.Accel), ("decel", "감속", ed.Decel), ("capacity", "적재 수량", (double)ed.Capacity), ("loadTime", "상차 시간", ed.LoadTime),
                     ("unloadTime", "하차 시간", ed.UnloadTime), ("mtbf", "MTBF", ed.MTBF), ("mttr", "MTTR", ed.MTTR), ("length", "길이", ed.Length), ("width", "폭", ed.Width) })
            Field(sb, n, l, N(v));
        Field(sb, "color", "색", H(ed.Color));
        Field(sb, "unitPrice", "단가(비우면 그대로)", ep == null ? "" : ep.UnitPrice.ToString("0.##", CultureInfo.InvariantCulture));
        sb.Append($"<div class=\"field\"><label></label><span><input type=\"checkbox\" name=\"hidden\" value=\"1\" id=\"hidden\"{(e.Hidden ? " checked" : "")}> <label for=\"hidden\" style=\"display:inline\">숨김(플러그인 목록에서 제외)</label></span></div>");
        sb.Append("<p><button type=\"submit\" class=\"primary\">저장</button></p></form></section>");

        sb.Append(AssetPages.Section(types, assets));

        sb.Append($"<section class=\"box\"><h2>투자비(prices.csv)</h2><form method=\"post\" action=\"/admin/types/prices\"><textarea name=\"csv\" rows=\"10\" style=\"width:100%;font-family:monospace\">{H(pricesCsv)}</textarea><p><button type=\"submit\" class=\"primary\">투자비 저장</button> <span class=\"muted\">Type,UnitPrice,Currency,Note</span></p></form></section>");
        return Page("타입·투자비", sb.ToString());
    }

    static void Field(StringBuilder sb, string name, string label, string value, string type = "text", string? hint = null) =>
        sb.Append($"<div class=\"field\"><label for=\"{name}\">{label}</label><span><input type=\"{type}\" name=\"{name}\" id=\"{name}\" value=\"{value}\">{(hint == null ? "" : $" <span class=\"muted\">{hint}</span>")}</span></div>");
}
