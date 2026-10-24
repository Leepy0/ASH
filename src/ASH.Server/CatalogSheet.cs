using System.Globalization;
using ASH.Core.Catalog;
using ASH.Core.Input;
using ClosedXML.Excel;

namespace ASH.Server;

/// <summary>
/// The type table as a spreadsheet: an Excel template to download (the current types as rows) and the parser for rows copied out of it.
/// Excel copies a cell range as tab-separated lines, so the paste box reads exactly that; the header row names the columns, in any order.
/// </summary>
public static class CatalogSheet
{
    sealed record Col(string Key, string Header, string Help, Func<CatalogType, PriceRow?, object> Get, Action<CatalogType, string>? Set);

    static double Num(string s) => double.TryParse(s.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && !double.IsInfinity(v)
        ? v : throw new FormatException($"'{s}'은(는) 숫자가 아닙니다.");

    static Col D(string key, string header, string help, Func<CatalogDefaults, double> get, Action<CatalogDefaults, double> set) =>
        new(key, header, help, (t, _) => get(t.Defaults), (t, s) => set(t.Defaults, s.Length == 0 ? 0 : Num(s)));

    static readonly Col[] Cols =
    {
        new("name", "이름", "AutoMod 타입 이름. 영문·숫자·_만, 영문 또는 _로 시작. 이미 있는 이름이면 그 타입을 고칩니다", (t, _) => t.Name, null),
        new("label", "라벨", "화면에 보일 이름", (t, _) => t.Label, (t, s) => t.Label = s),
        new("category", "분류", "묶음 이름(자유)", (t, _) => t.Category, (t, s) => t.Category = s),
        new("kind", "형상", "Standard·Conveyor·Ant·Magnet(이름 또는 한글 이름)", (t, _) => t.Kind, (t, s) => t.Kind = VehicleKinds.All.FirstOrDefault(k => k.Name.Equals(s, StringComparison.OrdinalIgnoreCase) || k.Label == s)?.Name ?? s),
        D("speedLoadedStraight", "적재 직선 속도", "m/s", d => d.SpeedLoadedStraight, (d, v) => d.SpeedLoadedStraight = v),
        D("speedLoadedCurve", "적재 곡선 속도", "m/s", d => d.SpeedLoadedCurve, (d, v) => d.SpeedLoadedCurve = v),
        D("speedEmptyStraight", "공차 직선 속도", "m/s", d => d.SpeedEmptyStraight, (d, v) => d.SpeedEmptyStraight = v),
        D("speedEmptyCurve", "공차 곡선 속도", "m/s", d => d.SpeedEmptyCurve, (d, v) => d.SpeedEmptyCurve = v),
        D("accel", "가속", "m/s²", d => d.Accel, (d, v) => d.Accel = v),
        D("decel", "감속", "m/s²", d => d.Decel, (d, v) => d.Decel = v),
        new("capacity", "적재 수량", "한 번에 싣는 용기 수(1 이상의 정수, 비우면 1)", (t, _) => t.Defaults.Capacity,
            (t, s) => t.Defaults.Capacity = s.Length == 0 ? 1 : int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : throw new FormatException($"'{s}'은(는) 정수가 아닙니다.")),
        D("loadTime", "상차 시간", "초", d => d.LoadTime, (d, v) => d.LoadTime = v),
        D("unloadTime", "하차 시간", "초", d => d.UnloadTime, (d, v) => d.UnloadTime = v),
        D("mtbf", "MTBF", "초, 0 = 고장 없음", d => d.MTBF, (d, v) => d.MTBF = v),
        D("mttr", "MTTR", "초", d => d.MTTR, (d, v) => d.MTTR = v),
        D("length", "길이", "m", d => d.Length, (d, v) => d.Length = v),
        D("width", "폭", "m", d => d.Width, (d, v) => d.Width = v),
        new("color", "색", "AutoMod 색 이름(비워도 됨)", (t, _) => t.Defaults.Color, (t, s) => t.Defaults.Color = s),
        new("unitPrice", "단가(원)", "대당 투자비(원). 비우면 그 타입의 단가를 바꾸지 않음", (_, p) => p == null ? "" : p.UnitPrice, null),
        new("hidden", "숨김", "Y = 플러그인 목록에서 뺌", (t, _) => t.Hidden ? "Y" : "", (t, s) => t.Hidden = s.ToUpperInvariant() is "Y" or "YES" or "1" or "TRUE" or "예" or "숨김"),
    };

    /// <summary>The header line to put above pasted rows, tab-separated (also shown on the page).</summary>
    public static string HeaderLine => string.Join("\t", Cols.Select(c => c.Header));

    /// <summary>Sheet "타입": header plus every current type; sheet "도움말": what each column takes.</summary>
    public static byte[] Template(IReadOnlyList<CatalogType> types, IReadOnlyDictionary<string, PriceRow> prices)
    {
        using var wb = ExcelReader.NewWorkbook();
        var ws = wb.AddWorksheet("타입");
        for (int c = 0; c < Cols.Length; c++) ws.Cell(1, c + 1).Value = Cols[c].Header;
        ws.Row(1).Style.Font.Bold = true;
        ws.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F1EFE8");
        int r = 2;
        foreach (var t in types)
        {
            prices.TryGetValue(t.Name, out var p);
            for (int c = 0; c < Cols.Length; c++) ws.Cell(r, c + 1).Value = XLCellValue.FromObject(Cols[c].Get(t, p));
            r++;
        }
        var kinds = string.Join(",", VehicleKinds.All.Select(k => k.Name));
        ws.Range(2, 4, 500, 4).CreateDataValidation().List("\"" + kinds + "\"", true);
        ws.Column(Array.FindIndex(Cols, c => c.Key == "unitPrice") + 1).Style.NumberFormat.Format = "#,##0";
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(8.0, 30.0);

        var help = wb.AddWorksheet("도움말");
        help.Cell(1, 1).Value = "열"; help.Cell(1, 2).Value = "넣는 값";
        help.Row(1).Style.Font.Bold = true;
        for (int c = 0; c < Cols.Length; c++) { help.Cell(c + 2, 1).Value = Cols[c].Header; help.Cell(c + 2, 2).Value = Cols[c].Help; }
        int h = Cols.Length + 3;
        help.Cell(h, 1).Value = "입력 방법";
        help.Cell(h, 2).Value = "'타입' 시트에서 머리글 줄을 포함해 범위를 복사(Ctrl+C)한 뒤 서버의 기준 정보 탭 '엑셀에서 붙여넣기' 칸에 붙여넣고(Ctrl+V) 저장합니다. 고칠 타입만 골라 붙여도 됩니다.";
        help.Columns().AdjustToContents(8.0, 90.0);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public sealed record Row(CatalogType Type, decimal? Price);

    /// <summary>
    /// Rows from pasted text: the first non-empty line is the header (column names from the template, any order, 이름 required).
    /// A name that already exists starts from that type, so columns left out keep their value; a new name starts from Standard, capacity 1.
    /// Every row is checked; with any error, Rows is empty and nothing should be saved.
    /// </summary>
    public static (IReadOnlyList<Row> Rows, IReadOnlyList<string> Errors) Parse(string text, Func<string, CatalogType?> existing)
    {
        var errors = new List<string>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select((l, i) => (Line: l, No: i + 1)).Where(x => x.Line.Trim().Length > 0).ToList();
        if (lines.Count == 0) return (Array.Empty<Row>(), new[] { "붙여넣은 내용이 없습니다." });
        var head = lines[0].Line.Split('\t').Select(h => h.Trim()).ToList();
        var map = new Col?[head.Count];
        for (int i = 0; i < head.Count; i++)
        {
            if (head[i].Length == 0) continue;
            map[i] = Cols.FirstOrDefault(c => c.Header == head[i] || (c.Key == "unitPrice" && head[i] == "단가") || c.Key.Equals(head[i], StringComparison.OrdinalIgnoreCase));
            if (map[i] == null) errors.Add($"머리글 '{head[i]}'을(를) 알 수 없습니다. 양식의 머리글 줄을 같이 복사하세요.");
        }
        int nameAt = Array.FindIndex(map, c => c?.Key == "name"), priceAt = Array.FindIndex(map, c => c?.Key == "unitPrice");
        if (nameAt < 0) errors.Add("머리글에 '이름' 열이 없습니다. 양식의 머리글 줄을 같이 복사하세요.");
        if (errors.Count > 0) return (Array.Empty<Row>(), errors);
        if (lines.Count == 1) return (Array.Empty<Row>(), new[] { "머리글 아래에 타입 줄이 없습니다." });

        var rows = new List<Row>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (line, no) in lines.Skip(1))
        {
            var cells = line.Split('\t').Select(c => c.Trim().Trim('"')).ToList();
            string Cell(int i) => i < cells.Count ? cells[i] : "";
            var name = Cell(nameAt);
            if (!seen.Add(name)) { errors.Add($"{no}행: 이름 '{name}'이(가) 붙여넣은 내용에 두 번 있습니다."); continue; }
            var t = existing(name) is { } old ? Clone(old) : new CatalogType { Name = name, Kind = "Standard", Defaults = { Capacity = 1 } };
            t.Name = name;
            decimal? price = null;
            var rowOk = true;
            for (int i = 0; i < map.Length; i++)
            {
                if (map[i] is not { } c || c.Key == "name") continue;
                try
                {
                    if (i == priceAt) { if (Cell(i).Length > 0) price = decimal.TryParse(Cell(i).Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var p) && p >= 0 ? p : throw new FormatException($"'{Cell(i)}'은(는) 0 이상의 숫자가 아닙니다."); }
                    else c.Set!(t, Cell(i));
                }
                catch (FormatException e) { errors.Add($"{no}행 {c.Header}: {e.Message}"); rowOk = false; }
            }
            if (rowOk && CatalogStore.Validate(t) is { } why) errors.Add($"{no}행({(name.Length == 0 ? "이름 없음" : name)}): {why}");
            rows.Add(new Row(t, price));
        }
        return errors.Count > 0 ? (Array.Empty<Row>(), errors) : (rows, errors);
    }

    static CatalogType Clone(CatalogType s) => new()
    {
        Name = s.Name, Label = s.Label, Category = s.Category, Kind = s.Kind, Hidden = s.Hidden, Assets = s.Assets.ToList(),
        Defaults = new CatalogDefaults
        {
            SpeedLoadedStraight = s.Defaults.SpeedLoadedStraight, SpeedLoadedCurve = s.Defaults.SpeedLoadedCurve, SpeedEmptyStraight = s.Defaults.SpeedEmptyStraight,
            SpeedEmptyCurve = s.Defaults.SpeedEmptyCurve, Accel = s.Defaults.Accel, Decel = s.Defaults.Decel, Capacity = s.Defaults.Capacity, LoadTime = s.Defaults.LoadTime,
            UnloadTime = s.Defaults.UnloadTime, MTBF = s.Defaults.MTBF, MTTR = s.Defaults.MTTR, Length = s.Defaults.Length, Width = s.Defaults.Width, Color = s.Defaults.Color,
        },
    };
}
