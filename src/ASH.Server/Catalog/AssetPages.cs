using System.Globalization;
using System.Text;
using ASH.Core.Catalog;
using ASH.Core.Cel;
using ASH.Server.Catalog;
using static ASH.Server.JobPages;

namespace ASH.Server;

/// <summary>The 에셋 section of /admin/types. CatalogType.Assets holds asset IDs; the Excel Asset column uses the name (AssetStore.FindByName).</summary>
public static class AssetPages
{
    static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    static string Sel(string name, string cur, params (string V, string L)[] opts) =>
        $"<select name=\"{name}\">" + string.Concat(opts.Select(o => $"<option value=\"{o.V}\"{(o.V == cur ? " selected" : "")}>{o.L}</option>")) + "</select>";

    static string Unit(double u) => u == 1 ? "m" : u == 0.0254 ? "in" : "mm";

    static string Options(SimplifyOptions o) =>
        "<div class=\"aopts\">" + $"<label>최소 부품 크기(%) <input name=\"minPartPct\" value=\"{N(o.MinPartPct)}\" size=\"4\"></label> " +
        $"<label>최대 상자 수 <input name=\"maxBoxes\" value=\"{o.MaxBoxes}\" size=\"4\"></label> " +
        $"<label>진행 방향 {Sel("heading", o.Heading.ToString(), ("PlusX", "+X"), ("PlusY", "+Y"), ("MinusX", "-X"), ("MinusY", "-Y"))}</label> " +
        $"<label>원점 {Sel("origin", o.Origin.ToString(), ("Front", "앞"), ("Center", "중심"), ("Back", "뒤"))}</label> " +
        $"<label>회전 {Sel("rotateDeg", o.RotateDeg.ToString(CultureInfo.InvariantCulture), ("0", "0°"), ("90", "90°"), ("180", "180°"), ("270", "270°"))}</label> " +
        $"<label>원본 단위 {Sel("sourceUnit", Unit(o.SourceUnitToM), ("mm", "mm"), ("m", "m"), ("in", "inch"))}</label> " +
        $"<label>격자 분할 <input name=\"grid\" value=\"{o.Grid}\" size=\"3\"></label>" + "</div>";

    static string DirSel(int cur) => Sel("conveyorDir", cur.ToString(CultureInfo.InvariantCulture), ("0", "0°"), ("90", "90°"), ("180", "180°"), ("270", "270°"));

    public static string Section(IReadOnlyList<CatalogType> types, AssetStore assets)
    {
        var sb = new StringBuilder("<section class=\"box\" id=\"assets\"><h2>에셋</h2>");
        foreach (var t in types)
        {
            var list = assets.ForType(t.Name);
            if (list.Count == 0) continue;
            sb.Append($"<h3>{H(t.Name)}</h3><div style=\"overflow-x:auto\"><table><thead><tr><th>이름</th><th>상자</th><th>크기 L×W×H (m)</th><th>원본</th><th>컨베이어 방향</th><th>메모</th><th>수정</th><th></th></tr></thead><tbody>");
            foreach (var a in list)
            {
                var svg = ""; // preview.svg is server-generated; inline it as is
                try { svg = File.ReadAllText(assets.Path(a.Id, "preview.svg")); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
                sb.Append($"<tr id=\"asset-{a.Id}\"><td>{H(a.Name)}<br><span class=\"muted\">{a.Id}</span></td><td class=\"num\">{a.Boxes}</td><td>{N(a.LengthM)}×{N(a.WidthM)}×{N(a.HeightM)}</td><td>{H(a.Source)}</td><td class=\"num\">{a.ConveyorDir}°</td><td>{H(a.Note)}</td><td>{a.UpdatedUtc:yyyy-MM-dd HH:mm}</td>");
                sb.Append($"<td><form method=\"post\" action=\"/admin/assets/{a.Id}/delete\" onsubmit=\"return confirm('이 에셋을 삭제할까요?')\"><button type=\"submit\">삭제</button></form></td></tr>");
                sb.Append($"<tr><td colspan=\"8\"><div style=\"max-width:900px\">{svg}</div>");
                sb.Append($"<details><summary>재변환</summary><form method=\"post\" action=\"/admin/assets/{a.Id}/reconvert\"><p><label>이름 <input name=\"name\" value=\"{H(a.Name)}\"></label> <label>메모 <input name=\"note\" value=\"{H(a.Note)}\" size=\"30\"></label> <label>컨베이어 방향 {DirSel(a.ConveyorDir)}</label></p>{Options(a.Options)}<p><button type=\"submit\" class=\"primary\">재변환</button></p></form></details></td></tr>");
            }
            sb.Append("</tbody></table></div>");
        }
        sb.Append("<h3 style=\"margin-top:16px\">에셋 올리기</h3><p class=\"t4\" style=\"margin-top:0\">STEP은 CAD에서 STL로 내보내 올리세요. STL/OBJ만 받습니다.</p>");
        if (types.Count == 0) sb.Append("<p class=\"muted\">먼저 타입을 만드세요.</p>");
        else
        {
            sb.Append("<form method=\"post\" action=\"/admin/assets\" enctype=\"multipart/form-data\"><p><label>타입 <select name=\"type\">");
            foreach (var t in types) sb.Append($"<option value=\"{H(t.Name)}\">{H(t.Name)}</option>");
            sb.Append("</select></label> <label>이름 <input name=\"name\" placeholder=\"agv_a\"></label> <label>메모 <input name=\"note\" size=\"30\"></label> ");
            sb.Append($"<label>컨베이어 방향 {DirSel(0)}</label></p><p><label>파일 <input type=\"file\" name=\"file\" accept=\".stl,.obj\"></label></p>{Options(new SimplifyOptions())}");
            sb.Append("<p><button type=\"submit\" class=\"primary\">올리기</button></p></form>");
        }
        return sb.Append("</section>").ToString();
    }
}
