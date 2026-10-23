using System;
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;

namespace ASH.Core.Input;

public static class ExcelTemplate
{
    // sheet -> (columns, descriptions, example row). Single source of truth; ExcelReader uses the same names.
    public static readonly (string Sheet, string[] Cols, string[] Desc, string[] Example)[] Sheets =
    {
        ("Settings", new[] { "Key", "Value", "Description" }, new[] { "키", "값", "설명 (연녹색 칸은 입력 불필요)" }, new string[0]),
        ("AGV_Types",
            new[] { "Type", "Category", "SpeedLoaded", "SpeedLoadedCurve", "SpeedEmpty", "SpeedEmptyCurve", "Accel", "Decel", "Capacity", "LoadTime", "UnloadTime", "MTBF", "MTTR", "Length", "Width", "Color", "Kind", "Asset" },
            new[] { "타입명", "분류(메모)", "적재 속도(m/s)", "적재 곡선 속도(m/s, 빈칸=직선)", "공차 속도(m/s)", "공차 곡선 속도(m/s, 빈칸=직선)", "가속(m/s²)", "감속(m/s²)", "용기 수", "로딩(초)", "언로딩(초)", "MTBF(초, 0=없음)", "MTTR(초)", "길이(m)", "폭(m)", "색상", "형상(목록에서 선택)", "에셋(C에서 채움)" },
            // the example keeps neutral values: SpeedEmpty = SpeedLoaded, one container per trip, no breakdowns
            new[] { "T_EX", "", "1.0", "", "1.0", "", "0.3", "0.3", "1", "20", "20", "0", "0", "1.2", "0.8", "Blue", "Standard", "" }),
        ("AGV_Groups",
            new[] { "Group", "Type", "MinCount", "MaxCount", "StandbyCPs", "Dispatch", "FinishCP" },
            new[] { "그룹명", "AGV 타입", "최소 대수", "최대 대수", "대기 CP", "Nearest / FIFO", "작업 완료 CP" },
            new[] { "G_EX", "T_EX", "1", "3", "CP_STBY", "Nearest", "" }),
        // thresholds are containers + units (each may be blank: ExcelReader adds them up in containers); EmptyReturn follows the route's return CPs
        ("Materials",
            new[] { "Material", "Station", "Group", "TaktTime", "UnitsPerCycle", "UnitsPerContainer", "CallThreshold", "CallThresholdQty", "CallThresholdTotal", "UnloadThreshold", "UnloadThresholdQty", "UnloadThresholdTotal", "InitStock", "SafetyStock", "Form", "LoadLength", "LoadWidth", "LoadHeight" },
            new[] { "자재명", "공정명(메모)", "운반 그룹", "T/T(초)", "소요 원수", "용기당 자재 수량", "호출 임계(용기)", "호출 임계(자재)", "호출 임계 합계(용기, 자동)", "언로딩 임계(용기)", "언로딩 임계(자재)", "언로딩 임계 합계(용기, 자동)", "초기재고(- : 기본값)", "안전재고(- : 기본값)", "형태(목록에서 선택)", "적재물 길이(m, - : 기본값)", "적재물 폭(m, - : 기본값)", "적재물 높이(m, - : 기본값)" },
            new[] { "M001", "", "G_EX", "60", "1", "10", "4", "", "", "1", "", "", "", "", "Box", "", "", "" }), // Call 4 > Settings SafetyStock 2
        ("Routes",
            new[] { "Material", "SupplyFromCP", "SupplyToCP", "BufferCP", "ReturnFromCP", "ReturnToCP", "ExtraTime" },
            new[] { "자재명", "load CP", "unload CP", "buffer CP(선택)", "rtnload CP(선택)", "rtnunload CP(선택)", "추가 시간(초)" },
            new[] { "M001", "CP_LOAD", "CP_UNLOAD", "", "", "", "0" }),
    };

    /// <summary>Columns picked from a drop-down: a fixed list ("a,b") or the names in another sheet's first column (sheet name). Re-applied on every update.</summary>
    static readonly (string Sheet, string Col, string Source)[] Lists =
    {
        ("AGV_Types", "Kind", string.Join(",", VehicleKinds.All.Select(k => k.Name))),
        ("AGV_Groups", "Type", "AGV_Types"),
        ("AGV_Groups", "Dispatch", "Nearest,FIFO"),
        ("Materials", "Group", "AGV_Groups"),
        ("Materials", "Form", string.Join(",", MaterialForms.All)),
        ("Routes", "Material", "Materials"),
    };

    /// <summary>Columns of older templates that conflict with the current ones (the group's type lives in AGV_Groups only, an empty return follows the
    /// route); an update deletes them.</summary>
    static readonly (string Sheet, string Col, string Why)[] Obsolete =
    {
        ("AGV_Types", "Group", "그룹의 타입은 AGV_Groups의 Type으로만 정합니다"),
        ("Materials", "EmptyReturn", "빈 용기 회수는 Routes의 rtnload/rtnunload CP로 정합니다"),
    };

    const int LastListRow = 1000;

    /// <summary>Read-only helper columns: a threshold in containers as ExcelReader adds it up (boxes + units / units per container), as an Excel formula
    /// over rows 3..LastListRow. R1C1 with absolute columns, so one formula fits the whole range wherever the columns are. The reader ignores them.</summary>
    static readonly (string Sheet, string Col, string Box, string Qty)[] Totals =
    {
        ("Materials", "CallThresholdTotal", "CallThreshold", "CallThresholdQty"),
        ("Materials", "UnloadThresholdTotal", "UnloadThreshold", "UnloadThresholdQty"),
    };

    /// <summary>Cells that need no input get this light green fill (RGB E2EFDA; the header is light blue, the example row light gray): the formula
    /// columns above and the Settings description column. Rows 3..LastListRow, so rows typed in later are covered too.</summary>
    public const int AutoFillRgb = 0xE2EFDA;
    static readonly (string Sheet, string Col)[] AutoCols = { ("Materials", "CallThresholdTotal"), ("Materials", "UnloadThresholdTotal"), ("Settings", "Description") };

    public static readonly (string Key, string Value, string Desc)[] SettingsDefaults =
    {
        ("Units", "Meters", "AM 거리 단위"),
        ("SimLength", "8", "시뮬레이션 길이(시간 h)"),
        ("Warmup", "1", "통계 제외 구간(시간 h)"),
        ("Replications", "3", "대수마다 반복 횟수"),
        ("SeedBase", "1", "시드 시작값"),
        ("UtilMax", "0.75", "AGV 가동률 상한"),
        ("SafetyStock", "2", "기본 안전재고(용기)"),
        ("InitStockMode", "Uniform", "Fixed / Uniform / Staggered"),
        ("InitStockSpread", "10", "Uniform의 N(%) 또는 Staggered 오프셋(용기)"),
        ("TrafficMode", "Block", "Block / CPOnly"),
        ("WorkDefinition", "LoadToUnload", "LoadToUnload / CallToReturn"),
        ("DeadlockSec", "600", "한 구간 이동이 이 시간(초)을 넘으면 교착"),
    };

    static void Put(IXLCell cell, string v)
    {
        if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) cell.Value = d;
        else cell.Value = v;
    }

    /// <summary>Excel column width (in digits) for the widest text: Korean and other wide characters count double, 8..40.</summary>
    internal static double ColumnWidth(IEnumerable<string> texts)
    {
        double widest = 0;
        foreach (var t in texts)
        {
            double w = 0;
            foreach (var ch in t ?? "") w += ch >= 0x1100 ? 2 : 1;
            widest = Math.Max(widest, w);
        }
        return Math.Min(40, Math.Max(8, widest * 1.1 + 2));
    }

    /// <summary>Rows that replace the example rows when the drawing has something to offer, by sheet name. Names come from the CPs: the groups on their
    /// GROUP attribute, the standby CPs of each group, and one route per load CP paired in drawing order with the unload / buffer / return CPs.
    /// Numbers stay at the example's values: the drawing knows nothing about takt times or speeds.</summary>
    const string SeedType = "T_AGV";

    internal static Dictionary<string, List<string[]>> Seed(ASH.Core.Layout.Layout l)
    {
        var seed = new Dictionary<string, List<string[]>>();
        var groups = new List<string>();
        foreach (var c in l.Cps) if (c.Group.Length > 0 && !groups.Contains(c.Group)) groups.Add(c.Group);
        string[] Ex(string sheet) { foreach (var x in Sheets) if (x.Sheet == sheet) return x.Example; return new string[0]; }
        List<string> Names(string type, string? group = null)
        {
            var r = new List<string>();
            foreach (var c in l.Cps) if (c.Type == type && (group == null || c.Group == group)) r.Add(c.Name);
            return r;
        }
        if (groups.Count > 0)
        {
            // one type for every group: a type is shared until the user adds another
            var t = (string[])Ex("AGV_Types").Clone(); t[0] = SeedType;
            var gs = new List<string[]>();
            foreach (var g in groups)
            {
                var r = (string[])Ex("AGV_Groups").Clone(); r[0] = g; r[1] = SeedType; r[4] = string.Join(",", Names("standby", g)); gs.Add(r);
            }
            seed["AGV_Types"] = new List<string[]> { t }; seed["AGV_Groups"] = gs;
        }
        var loads = new List<LayoutCpRef>();
        foreach (var c in l.Cps) if (c.Type == "load") loads.Add(new LayoutCpRef(c.Name, c.Group));
        if (loads.Count > 0)
        {
            var unload = Names("unload"); var buffer = Names("buffer"); var rl = Names("rtnload"); var ru = Names("rtnunload");
            string At(List<string> list, int i) => i < list.Count ? list[i] : "";
            var mats = new List<string[]>(); var routes = new List<string[]>();
            for (int i = 0; i < loads.Count; i++)
            {
                var name = "M" + (i + 1).ToString("000");
                var m = (string[])Ex("Materials").Clone(); m[0] = name; m[2] = loads[i].Group.Length > 0 ? loads[i].Group : (groups.Count > 0 ? groups[0] : m[2]); mats.Add(m);
                routes.Add(new[] { name, loads[i].Name, At(unload, i), At(buffer, i), At(rl, i), At(ru, i), "0" });
            }
            seed["Materials"] = mats; seed["Routes"] = routes;
        }
        return seed;
    }

    private readonly struct LayoutCpRef
    {
        public LayoutCpRef(string name, string group) { Name = name; Group = group; }
        public string Name { get; }
        public string Group { get; }
    }

    public static void Write(string path) => Write(path, null);

    /// <summary>The template; with a layout, its CP names and groups fill the rows that would otherwise hold the example.</summary>
    public static void Write(string path, ASH.Core.Layout.Layout? layout)
    {
        var seed = layout == null ? new Dictionary<string, List<string[]>>() : Seed(layout);
        FontFreeGraphicEngine.Install(); // before the workbook: it creates the default (font parsing) engine right away
        using var wb = new XLWorkbook();
        foreach (var (sheet, cols, desc, example) in Sheets)
        {
            var ws = wb.AddWorksheet(sheet);
            for (int c = 0; c < cols.Length; c++)
            {
                ws.Cell(1, c + 1).Value = cols[c];
                ws.Cell(2, c + 1).Value = desc[c];
            }
            int lastRow = 2;
            if (sheet == "Settings")
            {
                for (int r = 0; r < SettingsDefaults.Length; r++)
                {
                    ws.Cell(r + 3, 1).Value = SettingsDefaults[r].Key;
                    Put(ws.Cell(r + 3, 2), SettingsDefaults[r].Value);
                    ws.Cell(r + 3, 3).Value = SettingsDefaults[r].Desc;
                }
                lastRow = 2 + SettingsDefaults.Length;
            }
            else
            {
                var rows = seed.TryGetValue(sheet, out var made) ? made : new List<string[]> { example };
                for (int i = 0; i < rows.Count; i++)
                    for (int c = 0; c < rows[i].Length; c++)
                        if (rows[i][c].Length > 0) Put(ws.Cell(3 + i, c + 1), rows[i][c]);
                lastRow = 2 + rows.Count;
                if (!seed.ContainsKey(sheet)) // example row: replace or delete
                {
                    var ex = ws.Range(3, 1, 3, cols.Length);
                    ex.Style.Font.Italic = true;
                    ex.Style.Fill.BackgroundColor = XLColor.LightGray;
                }
            }
            StyleHeader(ws, cols.Length);
            for (int c = 1; c <= cols.Length; c++) FitWidth(ws, c, lastRow);
        }
        ApplyLists(wb);
        ApplyTotals(wb);
        ApplyAutoFill(wb);
        Save(wb, path);
    }

    /// <summary>Brings an existing input up to date without touching what is typed in it: missing sheets, columns and Settings keys are added, obsolete
    /// columns deleted, the description row refreshed, and with a layout the drawing's new groups, their new standby CPs and new load CPs (each as a material
    /// with its route) are appended. Existing rows are matched by group name and by SupplyFromCP, never overwritten. Returns one line per change.</summary>
    public static List<string> Update(string path, ASH.Core.Layout.Layout? layout)
    {
        using var wb = ExcelReader.Open(path);
        var changes = Update(wb, layout);
        Save(wb, path);
        return changes;
    }

    /// <summary>Update on a workbook in memory, not saved. <paramref name="log"/> gets every change in order, for a file only Excel can write (see ExcelEdit).</summary>
    public static List<string> Update(XLWorkbook wb, ASH.Core.Layout.Layout? layout, List<ExcelEdit>? log = null)
    {
        var changes = new List<string>();
        var seed = layout == null ? new Dictionary<string, List<string[]>>() : Seed(layout);
        foreach (var (sheet, cols, desc, _) in Sheets)
        {
            if (!wb.TryGetWorksheet(sheet, out var ws))
            {
                ws = wb.AddWorksheet(sheet);
                log?.Add(new AddSheetEdit(sheet));
                for (int c = 0; c < cols.Length; c++) { Set(ws, 1, c + 1, cols[c], log); Set(ws, 2, c + 1, desc[c], log); FitWidth(ws, c + 1, 2); }
                StyleHeader(ws, cols.Length);
                changes.Add($"{sheet} 시트 추가");
            }
            foreach (var (os, oc, why) in Obsolete)
                if (os == sheet && Columns(ws).TryGetValue(oc, out var dc))
                {
                    ws.Column(dc).Delete(); log?.Add(new DeleteColumnEdit(sheet, dc));
                    changes.Add($"{sheet}: {oc} 열 삭제({why})");
                }
            var have = Columns(ws);
            int next = (ws.LastColumnUsed()?.ColumnNumber() ?? 0) + 1;
            bool described = false;
            for (int i = 0; i < cols.Length; i++)
            {
                if (have.TryGetValue(cols[i], out var c))
                {
                    if (ws.Cell(2, c).GetString() != desc[i]) { Set(ws, 2, c, desc[i], log); described = true; }
                    continue;
                }
                Set(ws, 1, next, cols[i], log); Set(ws, 2, next, desc[i], log);
                ws.Cell(1, next).Style.Font.Bold = true; ws.Cell(1, next).Style.Fill.BackgroundColor = XLColor.LightBlue;
                FitWidth(ws, next, 2);
                next++;
                changes.Add($"{sheet}: {cols[i]} 열 추가");
            }
            if (described) changes.Add($"{sheet}: 2행 설명 갱신");
        }
        var set = wb.Worksheet("Settings");
        var keys = new HashSet<string>(Values(set, "Key"), StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v, d) in SettingsDefaults)
            if (!keys.Contains(k)) { Append(set, ColsOf("Settings"), new[] { k, v, d }, log); changes.Add($"Settings: {k} = {v} 추가"); }

        if (seed.TryGetValue("AGV_Groups", out var sg))
        {
            var tws = wb.Worksheet("AGV_Types");
            var types = Values(tws, "Type");
            if (types.Count == 0) { Append(tws, ColsOf("AGV_Types"), seed["AGV_Types"][0], log); types.Add(SeedType); changes.Add($"AGV_Types: 타입 {SeedType} 추가(값 확인 필요)"); }
            var gws = wb.Worksheet("AGV_Groups");
            var gc = Columns(gws);
            foreach (var g in sg)
            {
                int row = RowOf(gws, "Group", g[0]);
                if (row == 0)
                {
                    var r = (string[])g.Clone(); r[1] = types[0];
                    Append(gws, ColsOf("AGV_Groups"), r, log);
                    changes.Add($"AGV_Groups: 그룹 {g[0]} 추가(Type {types[0]}, 대수 확인 필요)");
                    continue;
                }
                if (!gc.TryGetValue("StandbyCPs", out var sc)) continue;
                var listed = Split(gws.Cell(row, sc).GetString());
                var add = Split(g[4]).Where(x => !listed.Contains(x)).ToList();
                if (add.Count == 0) continue;
                Set(gws, row, sc, string.Join(",", listed.Concat(add)), log);
                changes.Add($"AGV_Groups: 그룹 {g[0]} 대기 CP {string.Join(",", add)} 추가");
            }
        }
        if (seed.TryGetValue("Routes", out var sr))
        {
            var mws = wb.Worksheet("Materials"); var rws = wb.Worksheet("Routes");
            var from = new HashSet<string>(Values(rws, "SupplyFromCP"));
            var names = new HashSet<string>(Values(mws, "Material").Concat(Values(rws, "Material")));
            var sm = seed["Materials"];
            for (int i = 0; i < sr.Count; i++)
            {
                if (from.Contains(sr[i][1])) continue;
                int n = 1; string name;
                do name = "M" + (n++).ToString("000"); while (names.Contains(name));
                names.Add(name);
                var m = (string[])sm[i].Clone(); m[0] = name;
                var route = (string[])sr[i].Clone(); route[0] = name;
                Append(mws, ColsOf("Materials"), m, log); Append(rws, ColsOf("Routes"), route, log);
                changes.Add($"Materials·Routes: load CP {sr[i][1]}의 자재 {name} 추가(T/T·임계값·unload CP 확인 필요)");
            }
        }
        ApplyLists(wb, log);
        ApplyTotals(wb, log);
        ApplyAutoFill(wb, log);
        return changes;
    }

    static void Set(IXLWorksheet ws, int r, int c, string v, List<ExcelEdit>? log)
    {
        Put(ws.Cell(r, c), v);
        log?.Add(new CellEdit(ws.Name, r, c, v));
    }

    static string[] ColsOf(string sheet) => Sheets.First(x => x.Sheet == sheet).Cols;

    static List<string> Split(string csv) => csv.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

    /// <summary>Header (row 1) -> column number.</summary>
    static Dictionary<string, int> Columns(IXLWorksheet ws)
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        int last = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (int c = 1; c <= last; c++) { var h = ws.Cell(1, c).GetString().Trim(); if (h.Length > 0 && !d.ContainsKey(h)) d[h] = c; }
        return d;
    }

    /// <summary>The non-blank values of a column's data rows (3+).</summary>
    static List<string> Values(IXLWorksheet ws, string col)
    {
        var r = new List<string>();
        if (!Columns(ws).TryGetValue(col, out var c)) return r;
        int last = LastDataRow(ws);
        for (int i = 3; i <= last; i++) { var v = ws.Cell(i, c).GetString().Trim(); if (v.Length > 0) r.Add(v); }
        return r;
    }

    static int RowOf(IXLWorksheet ws, string col, string value)
    {
        if (!Columns(ws).TryGetValue(col, out var c)) return 0;
        int last = LastDataRow(ws);
        for (int i = 3; i <= last; i++) if (ws.Cell(i, c).GetString().Trim() == value) return i;
        return 0;
    }

    /// <summary>A row after the last used one, each value under its template column wherever that column is in this sheet.</summary>
    static void Append(IXLWorksheet ws, string[] cols, string[] values, List<ExcelEdit>? log)
    {
        var at = Columns(ws);
        int r = Math.Max(3, LastDataRow(ws) + 1);
        for (int i = 0; i < cols.Length && i < values.Length; i++)
            if (values[i].Length > 0 && at.TryGetValue(cols[i], out var c)) Set(ws, r, c, values[i], log);
    }

    /// <summary>The last row holding a typed value (formula cells, such as the totals, do not count).</summary>
    static int LastDataRow(IXLWorksheet ws) =>
        ws.CellsUsed(XLCellsUsedOptions.Contents).Where(c => !c.HasFormula && c.GetString().Length > 0).Select(c => c.Address.RowNumber).DefaultIfEmpty(0).Max();

    static void ApplyTotals(XLWorkbook wb, List<ExcelEdit>? log = null)
    {
        foreach (var (sheet, col, box, qty) in Totals)
        {
            if (!wb.TryGetWorksheet(sheet, out var ws)) continue;
            var at = Columns(ws);
            if (!at.TryGetValue(col, out var c) || !at.TryGetValue(box, out var b) || !at.TryGetValue(qty, out var q)
                || !at.TryGetValue("Material", out var m) || !at.TryGetValue("UnitsPerContainer", out var per)) continue;
            // N() reads a blank or "-" as 0, like the reader; blank when the row has no material
            var f = $"=IF(RC{m}=\"\",\"\",N(RC{b})+N(RC{q})/IF(N(RC{per})>0,RC{per},1))";
            ws.Range(3, c, LastListRow, c).FormulaR1C1 = f;
            log?.Add(new FormulaEdit(sheet, c, 3, LastListRow, f));
        }
    }

    static void ApplyAutoFill(XLWorkbook wb, List<ExcelEdit>? log = null)
    {
        foreach (var (sheet, col) in AutoCols)
        {
            if (!wb.TryGetWorksheet(sheet, out var ws) || !Columns(ws).TryGetValue(col, out var c)) continue;
            ws.Range(3, c, LastListRow, c).Style.Fill.BackgroundColor = XLColor.FromArgb(AutoFillRgb);
            log?.Add(new FillEdit(sheet, c, 3, LastListRow, AutoFillRgb));
        }
    }

    static void ApplyLists(XLWorkbook wb, List<ExcelEdit>? log = null)
    {
        foreach (var (sheet, col, source) in Lists)
        {
            if (!wb.TryGetWorksheet(sheet, out var ws) || !Columns(ws).TryGetValue(col, out var c)) continue;
            var v = ws.Range(3, c, LastListRow, c).CreateDataValidation();
            if (wb.TryGetWorksheet(source, out var src))
            {
                v.List(src.Range(3, 1, LastListRow, 1), true);
                v.ErrorMessage = $"{source} 시트의 이름 중에서 고르세요.";
                log?.Add(new ListEdit(sheet, c, 3, LastListRow, $"={source}!$A$3:$A${LastListRow}", col, v.ErrorMessage));
            }
            else
            {
                v.List("\"" + source + "\"", true);
                v.ErrorMessage = source.Replace(",", " / ") + " 중에서 고르세요.";
                log?.Add(new ListEdit(sheet, c, 3, LastListRow, source, col, v.ErrorMessage));
            }
            v.IgnoreBlanks = true;
            v.ErrorStyle = XLErrorStyle.Stop;
            v.ErrorTitle = col;
            v.ShowErrorMessage = true;
        }
    }

    static void StyleHeader(IXLWorksheet ws, int ncols)
    {
        var head = ws.Range(1, 1, 1, ncols);
        head.Style.Font.Bold = true;
        head.Style.Fill.BackgroundColor = XLColor.LightBlue;
        ws.SheetView.FreezeRows(2);
    }

    // Widths are worked out from the text, not with AdjustToContents: that measures with the SixLabors font engine, which throws a
    // TypeInitializationException (TableLoader) inside ZWCAD's process.
    static void FitWidth(IXLWorksheet ws, int c, int lastRow)
    {
        var texts = new List<string>();
        for (int r = 1; r <= lastRow; r++) texts.Add(ws.Cell(r, c).GetString());
        ws.Column(c).Width = ColumnWidth(texts);
    }

    // saved beside the target and moved over it: a failure half way must not leave a truncated input.xlsx that the next run takes for a real one
    static void Save(XLWorkbook wb, string path)
    {
        var tmp = path + ".tmp";
        try
        {
            using (var fs = System.IO.File.Create(tmp)) wb.SaveAs(fs);
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            System.IO.File.Move(tmp, path);
        }
        finally { if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp); }
    }
}

/// <summary>One change <see cref="ExcelTemplate.Update(XLWorkbook, ASH.Core.Layout.Layout?, List{ExcelEdit}?)"/> made, in order, so that Excel itself can
/// repeat it on a file only Excel can write (company document encryption). Columns and rows are 1-based, as they were when the edit was made.</summary>
public abstract record ExcelEdit(string Sheet);
public sealed record AddSheetEdit(string Sheet) : ExcelEdit(Sheet);
public sealed record DeleteColumnEdit(string Sheet, int Col) : ExcelEdit(Sheet);
public sealed record CellEdit(string Sheet, int Row, int Col, string Value) : ExcelEdit(Sheet);
/// <summary>One R1C1 formula over rows FirstRow..LastRow of a column.</summary>
public sealed record FormulaEdit(string Sheet, int Col, int FirstRow, int LastRow, string FormulaR1C1) : ExcelEdit(Sheet);
/// <summary>A background colour (RGB, 0xRRGGBB) over rows FirstRow..LastRow of a column: the cells that need no input.</summary>
public sealed record FillEdit(string Sheet, int Col, int FirstRow, int LastRow, int Rgb) : ExcelEdit(Sheet);
/// <summary>A drop-down over rows FirstRow..LastRow: Formula is a sheet range ("=AGV_Types!$A$3:$A$1000") or a comma list ("Box,Pallet").</summary>
public sealed record ListEdit(string Sheet, int Col, int FirstRow, int LastRow, string Formula, string Title, string Message) : ExcelEdit(Sheet);
