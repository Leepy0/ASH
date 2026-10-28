using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ASH.Core.Layout;
using ClosedXML.Excel;

namespace ASH.Core.Input;

public static class ExcelTemplate
{
    // sheet -> (columns, descriptions, example row). Single source of truth; ExcelReader uses the same names. Basic columns first, then the Advanced ones.
    public static readonly (string Sheet, string[] Cols, string[] Desc, string[] Example)[] Sheets =
    {
        ("Settings", new[] { "Key", "Value", "Description" }, new[] { "키", "값", "설명 (연녹색 칸은 입력 불필요)" }, new string[0]),
        // the example keeps neutral values: SpeedEmpty = SpeedLoaded, one container per trip, no breakdowns
        S("AGV_Types",
            ("Type", "타입명", "T_EX"), ("SpeedLoaded", "적재 속도(m/s)", "1.0"), ("SpeedEmpty", "공차 속도(m/s)", "1.0"), ("Capacity", "용기 수", "1"),
            ("LoadTime", "로딩(초)", "20"), ("UnloadTime", "언로딩(초)", "20"), ("Length", "길이(m)", "1.2"), ("Width", "폭(m)", "0.8"), ("Kind", "형상(목록에서 선택)", "Standard"),
            ("Category", "분류(메모)", ""), ("SpeedLoadedCurve", "적재 곡선 속도(m/s, 빈칸=직선)", ""), ("SpeedEmptyCurve", "공차 곡선 속도(m/s, 빈칸=직선)", ""),
            ("Accel", "가속(m/s²)", "0.3"), ("Decel", "감속(m/s²)", "0.3"), ("MTBF", "MTBF(초, 0=없음)", "0"), ("MTTR", "MTTR(초)", "0"), ("Color", "색상", "Blue"), ("Asset", "차량 그림(에셋 목록, 빈칸=기본)", "")),
        ("AGV_Groups",
            new[] { "Group", "Type", "MinCount", "MaxCount", "StandbyCPs", "Dispatch", "FinishCP" },
            new[] { "그룹명", "AGV 타입", "최소 대수", "최대 대수", "대기 CP", "호출 기준", "작업 완료 CP" },
            new[] { "G_EX", "T_EX", "1", "3", "CP_STBY", "Nearest", "" }),
        // thresholds are containers + units (each may be blank: ExcelReader adds them up in containers); EmptyReturn follows the route's return CPs.
        // The route (an older template's Routes sheet) is on the material's row: one route per material.
        S("Materials",
            ("Material", "자재명", "M001"), ("Group", "운반 그룹", "G_EX"), ("TaktTime", "T/T(초)", "60"), ("UnitsPerCycle", "소요 원수", "1"), ("UnitsPerContainer", "용기당 자재 수량", "10"),
            ("CallThreshold", "호출 기준 (용기)", "4"), // Call 4 > Settings SafetyStock 2
            ("Form", "형태", "Box"), ("SupplyFromCP", "load CP", "CP_LOAD"), ("SupplyToCP", "unload CP", "CP_UNLOAD"),
            ("ReturnFromCP", "rtnload CP(선택)", ""), ("ReturnToCP", "rtnunload CP(선택)", ""),
            ("Station", "공정명(메모)", ""), ("CallThresholdQty", "호출 기준 (자재)", ""), ("CallThresholdTotal", "호출 기준 합계 (용기, 자동)", ""),
            ("UnloadThreshold", "언로딩 기준 (용기)", "1"), ("UnloadThresholdQty", "언로딩 기준 (자재)", ""), ("UnloadThresholdTotal", "언로딩 기준 합계 (용기, 자동)", ""),
            ("InitStock", "초기 재고", ""), ("SafetyStock", "안전재고", ""),
            ("LoadLength", "적재물 길이(m, - : 기본값)", ""), ("LoadWidth", "적재물 폭(m, - : 기본값)", ""), ("LoadHeight", "적재물 높이(m, - : 기본값)", ""),
            ("Flow", "흐름(Supply 공급 / Output 빈 용기 공급+풀 용기 반출)", "Supply"), ("Use", "사용 여부", "Y"),
            ("BufferCP", "buffer CP(선택)", ""), ("ExtraTime", "추가 시간(초)", "0"), ("BufferWaitSec", "버퍼 대기 허용(초, 넘는 시간만 비가동)", "0")),
        // optional sheet, no example row: a material named here is consumed by the conveyor's products instead of the takt clock
        ("Conveyors",
            new[] { "Conveyor", "Material", "ProductTakt", "Speed", "Capacity", "ConsumeAt" },
            new[] { "컨베이어 이름(CAD)", "소비 자재(Materials의 이름)", "제품 투입 간격(초, 빈칸=60)", "속도(m/s, 빈칸=0.3)", "동시 제품 수(빈칸=길이로 자동)", "소비 지점(폴리선 시작에서 m, 빈칸=끝)" },
            new string[0]),
    };

    static (string, string[], string[], string[]) S(string sheet, params (string Col, string Desc, string Example)[] c) =>
        (sheet, c.Select(x => x.Col).ToArray(), c.Select(x => x.Desc).ToArray(), c.Select(x => x.Example).ToArray());

    /// <summary>Columns most inputs never touch: grouped (Excel outline) and collapsed in the template, behind "+" (E1). <see cref="InputSchema"/> reports them.</summary>
    internal static readonly (string Sheet, string Col)[] Advanced =
        new[] { "Station", "CallThresholdQty", "CallThresholdTotal", "UnloadThreshold", "UnloadThresholdQty", "UnloadThresholdTotal", "InitStock", "SafetyStock",
            "LoadLength", "LoadWidth", "LoadHeight", "Flow", "Use", "BufferCP", "ExtraTime", "BufferWaitSec" }.Select(c => ("Materials", c))
        .Concat(new[] { "Category", "SpeedLoadedCurve", "SpeedEmptyCurve", "Accel", "Decel", "MTBF", "MTTR", "Color", "Asset" }.Select(c => ("AGV_Types", c))).ToArray();

    /// <summary>The route columns of Materials: an older workbook has them on a Routes sheet of their own (with Material first), which Update merges in.</summary>
    internal static readonly string[] RouteCols = { "SupplyFromCP", "SupplyToCP", "BufferCP", "ReturnFromCP", "ReturnToCP", "ExtraTime", "BufferWaitSec" };

    /// <summary>Cells a named row cannot do without; <see cref="NeedSummary"/> counts the blank ones. CallThreshold also counts as filled by CallThresholdQty.</summary>
    static readonly (string Sheet, string Col)[] Required =
        new[] { "Group", "TaktTime", "UnitsPerCycle", "UnitsPerContainer", "CallThreshold", "SupplyFromCP", "SupplyToCP" }.Select(c => ("Materials", c))
        .Concat(new[] { "Type", "MinCount", "MaxCount", "StandbyCPs" }.Select(c => ("AGV_Groups", c)))
        .Concat(new[] { "SpeedLoaded", "SpeedEmpty", "Capacity", "LoadTime", "UnloadTime" }.Select(c => ("AGV_Types", c))).ToArray();

    /// <summary>The start of the change line that sums up the blank required cells (always the last line of Write's and Update's list).</summary>
    public const string NeedPrefix = "입력 필요";

    /// <summary>Columns picked from a drop-down: a fixed list ("a,b") or the names in another sheet's first column (sheet name). Re-applied on every update.</summary>
    internal static readonly (string Sheet, string Col, string Source)[] Lists =
    {
        ("AGV_Types", "Kind", string.Join(",", VehicleKinds.All.Select(k => k.Name))),
        ("AGV_Groups", "Type", "AGV_Types"),
        ("AGV_Groups", "Dispatch", "Nearest,FIFO"),
        ("Materials", "Group", "AGV_Groups"),
        ("Materials", "Form", string.Join(",", MaterialForms.All)),
        ("Materials", "Flow", string.Join(",", MaterialFlows.All)),
        ("Materials", "Use", "Y,N"),
        ("Conveyors", "Material", "Materials"),
    };

    /// <summary>Columns of older templates that conflict with the current ones (the group's type lives in AGV_Groups only, an empty return follows the
    /// route); an update deletes them.</summary>
    static readonly (string Sheet, string Col, string Why)[] Obsolete =
    {
        ("AGV_Types", "Group", "그룹의 타입은 AGV_Groups의 Type으로만 정합니다"),
        ("Materials", "EmptyReturn", "빈 용기 회수는 rtnload/rtnunload CP로 정합니다"),
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
    internal static readonly (string Sheet, string Col)[] AutoCols ={ ("Materials", "CallThresholdTotal"), ("Materials", "UnloadThresholdTotal"), ("Settings", "Description") };

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

    internal static Dictionary<string, List<string[]>> Seed(ASH.Core.Layout.Layout? layout, IReadOnlyList<ASH.Core.Catalog.CatalogType>? catalog = null)
    {
        var l = layout ?? new ASH.Core.Layout.Layout();
        var seed = new Dictionary<string, List<string[]>>();
        bool cat = catalog != null && catalog.Count > 0;
        string typeName = cat ? catalog![0].Name : SeedType;
        var groups = new List<string>();
        foreach (var c in l.Cps) if (c.Group.Length > 0 && !groups.Contains(c.Group)) groups.Add(c.Group);
        string[] Ex(string sheet) { foreach (var x in Sheets) if (x.Sheet == sheet) return x.Example; return new string[0]; }
        // CPs of an AGVCPROUTE sequence get their rows from the recorded route below, not from the one-by-one pairing
        var recorded = CpRoutes.Names(l);
        var routesOf = CpRoutes.From(l);
        List<string> Names(string type, string? group = null)
        {
            var r = new List<string>();
            foreach (var c in l.Cps) if (c.Type == type && (group == null || c.Group == group) && (type == "standby" || !recorded.Contains(c.Name))) r.Add(c.Name);
            return r;
        }
        if (groups.Count > 0)
        {
            // one type for every group: a type is shared until the user adds another
            var t = (string[])Ex("AGV_Types").Clone(); t[0] = typeName;
            var gs = new List<string[]>();
            foreach (var g in groups)
            {
                var r = (string[])Ex("AGV_Groups").Clone(); r[0] = g; r[1] = typeName; r[4] = string.Join(",", Names("standby", g));
                r[6] = routesOf.Where(x => x.Group == g).SelectMany(x => x.Finish).FirstOrDefault() ?? ""; gs.Add(r);
            }
            seed["AGV_Types"] = new List<string[]> { t }; seed["AGV_Groups"] = gs;
        }
        if (cat)
        {
            seed["AGV_Types"] = catalog!.Select(ASH.Core.Catalog.CatalogType.ToTemplateRow).ToList();
            // no drawing group: the example group must point at a type that exists
            if (groups.Count == 0) { var g = (string[])Ex("AGV_Groups").Clone(); g[1] = catalog![0].Name; seed["AGV_Groups"] = new List<string[]> { g }; }
        }
        // one-by-one CPs: within each group (a route never mixes groups) the n-th load by name pairs with the n-th unload / buffer / return CP by name
        // (L001-U001-RL001-RU001, L002-U002-…). As many routes as the longest of load, unload and the returns: a shorter list repeats its last CP
        // (L1 with U1,U2 = L1-U1, L1-U2; RU1 serves both); a group without a type leaves it blank. A buffer without an n-th stays blank
        var mats = new List<string[]>();
        string At(List<string> list, int i) => i < list.Count ? list[i] : "";
        int groupCol = MatCol("Group");
        void Add(string group, string from, string to, string buffer, string rl, string ru)
        {
            var m = (string[])Ex("Materials").Clone();
            m[MatCol("Material")] = "M" + (mats.Count + 1).ToString("000");
            m[groupCol] = group.Length > 0 ? group : (groups.Count > 0 ? groups[0] : m[groupCol]);
            m[MatCol("SupplyFromCP")] = from; m[MatCol("SupplyToCP")] = to; m[MatCol("BufferCP")] = buffer; m[MatCol("ReturnFromCP")] = rl; m[MatCol("ReturnToCP")] = ru;
            mats.Add(m);
        }
        foreach (var g in l.Cps.Where(c => c.Type == "load" && !recorded.Contains(c.Name)).Select(c => c.Group).Distinct().ToList())
        {
            var loads = ByName(Names("load", g));
            var unload = ByName(Names("unload", g)); var buffer = ByName(Names("buffer", g)); var rl = ByName(Names("rtnload", g)); var ru = ByName(Names("rtnunload", g));
            int n = new[] { loads.Count, unload.Count, rl.Count, ru.Count }.Max();
            for (int i = 0; i < n; i++) Add(g, CpRoute.Pair(loads, i), CpRoute.Pair(unload, i), At(buffer, i), CpRoute.Pair(rl, i), CpRoute.Pair(ru, i));
        }
        // one material per load CP of a recorded sequence, M### like the one-by-one rows
        foreach (var r in routesOf)
            for (int i = 0; i < r.Load.Count; i++) Add(r.Group, r.Load[i], CpRoute.Pair(r.Unload, i), "", CpRoute.Pair(r.RtnLoad, i), CpRoute.Pair(r.RtnUnload, i));
        if (mats.Count > 0)
        {
            // each material's Form: the first one its group's vehicle kind carries (the example's would draw the validator's "cannot carry" warning)
            var types = seed.TryGetValue("AGV_Types", out var ts) ? ts : new List<string[]> { Ex("AGV_Types") };
            var gs2 = seed.TryGetValue("AGV_Groups", out var g0) ? g0 : new List<string[]> { Ex("AGV_Groups") };
            int kindCol = Array.IndexOf(ColsOf("AGV_Types"), "Kind"), formCol = MatCol("Form");
            var kindOf = types.Where(r => r.Length > kindCol).GroupBy(r => r[0]).ToDictionary(x => x.Key, x => x.First()[kindCol]);
            var typeOf = gs2.GroupBy(r => r[0]).ToDictionary(x => x.Key, x => x.First()[1]);
            foreach (var m in mats) m[formCol] = FormFor(m[groupCol], typeOf, kindOf) ?? m[formCol];
            seed["Materials"] = mats;
        }
        var conv = ConveyorRows(l, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        if (conv.Count > 0) seed["Conveyors"] = conv;
        return seed;
    }

    /// <summary>One Conveyors row per named AGV_CONV conveyor of the drawing not in <paramref name="have"/>: the name only. The material is the user's to pick
    /// (the validator asks for it); takt, speed and capacity stay blank = their defaults; the use point comes from the drawing's AGVCONVUSE marker.</summary>
    static List<string[]> ConveyorRows(ASH.Core.Layout.Layout l, HashSet<string> have) =>
        l.Conveyors.Where(c => !c.GraphicOnly && c.Name.Length > 0).Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(have.Add).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Select(n => new[] { n, "", "", "", "", "", "" }).ToList();

    /// <summary>CP names in natural order: prefix, then the trailing number as a number (L2 before L10), then the name.</summary>
    internal static List<string> ByName(IEnumerable<string> names) =>
        names.OrderBy(n => n.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9'), StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => { var d = n.Substring(n.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').Length); return d.Length == 0 ? -1 : long.TryParse(d, out var v) ? v : long.MaxValue; })
            .ThenBy(n => n, StringComparer.Ordinal).ToList();

    public static List<string> Write(string path) => Write(path, null);

    /// <summary>The template; with a layout, its CP names and groups fill the rows that would otherwise hold the example. <paramref name="scale"/> (drawing
    /// units to metres, 0 = unknown) lets the seeded rows get a suggested CallThreshold. Returns the suggestions, then the <see cref="NeedPrefix"/> line.</summary>
    public static List<string> Write(string path, ASH.Core.Layout.Layout? layout, IReadOnlyList<ASH.Core.Catalog.CatalogType>? catalog = null, double scale = 0)
    {
        var seed = layout == null && (catalog == null || catalog.Count == 0) ? new Dictionary<string, List<string[]>>() : Seed(layout, catalog);
        var changes = new List<string>();
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
                var rows = seed.TryGetValue(sheet, out var made) ? made : example.Length == 0 ? new List<string[]>() : new List<string[]> { example };
                for (int i = 0; i < rows.Count; i++)
                    for (int c = 0; c < rows[i].Length; c++)
                        if (rows[i][c].Length > 0) Put(ws.Cell(3 + i, c + 1), rows[i][c]);
                lastRow = 2 + rows.Count;
                if (!seed.ContainsKey(sheet) && example.Length > 0) // example row: replace or delete
                {
                    var ex = ws.Range(3, 1, 3, cols.Length);
                    ex.Style.Font.Italic = true;
                    ex.Style.Fill.BackgroundColor = XLColor.LightGray;
                }
            }
            StyleHeader(ws, cols.Length);
            for (int c = 1; c <= cols.Length; c++) FitWidth(ws, c, lastRow);
        }
        ApplyLists(wb, null, layout);
        ApplyTotals(wb);
        ApplyAutoFill(wb);
        ApplyCenter(wb);
        ApplyOutline(wb);
        // the seeded rows hold the example's threshold, not a typed one: a suggestion may replace it
        if (layout != null && seed.TryGetValue("Materials", out var sm)) SuggestCalls(wb, layout, scale, Enumerable.Range(3, sm.Count).ToList(), changes, null);
        changes.Add(NeedSummary(wb));
        Save(wb, path);
        return changes;
    }

    /// <summary>Brings an existing input up to date without touching what is typed in it: missing sheets, columns and Settings keys are added, obsolete
    /// columns deleted, the description row refreshed, and with a layout the drawing's new groups, their new standby CPs and new load CPs (each as a material
    /// with its route) are appended. Existing rows are matched by group name and by SupplyFromCP, never overwritten. An older workbook's Routes sheet is
    /// merged into Materials. Blank CallThresholds get a suggestion (with a layout and <paramref name="scale"/>, drawing units to metres; 0 = unknown).
    /// Returns one line per change, the <see cref="NeedPrefix"/> line last.</summary>
    public static List<string> Update(string path, ASH.Core.Layout.Layout? layout, IReadOnlyList<ASH.Core.Catalog.CatalogType>? catalog = null, double scale = 0)
    {
        using var wb = ExcelReader.Open(path);
        var changes = Update(wb, layout, null, catalog, scale);
        Save(wb, path);
        return changes;
    }

    /// <summary>Update on a workbook in memory, not saved. <paramref name="log"/> gets every change in order, for a file only Excel can write (see ExcelEdit).</summary>
    public static List<string> Update(XLWorkbook wb, ASH.Core.Layout.Layout? layout, List<ExcelEdit>? log = null, IReadOnlyList<ASH.Core.Catalog.CatalogType>? catalog = null, double scale = 0)
    {
        var changes = new List<string>();
        var seed = layout == null && (catalog == null || catalog.Count == 0) ? new Dictionary<string, List<string[]>>() : Seed(layout, catalog);
        // an older workbook keeps its routes on a Routes sheet: they move onto the Materials rows, unless that would not read back the same (legacy)
        List<(string Material, string[] Values)>? moving = null;
        bool legacy = false;
        if (wb.TryGetWorksheet("Routes", out var oldRoutes) && !(wb.TryGetWorksheet("Materials", out var om) && Columns(om).ContainsKey("SupplyFromCP")))
        {
            moving = RouteRows(oldRoutes, om, out var why);
            if (moving == null) { legacy = true; changes.Add($"Routes 시트 유지(Materials에 합치지 않음): {why}"); }
        }
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
                bool route = sheet == "Materials" && RouteCols.Contains(cols[i]);
                if (route && legacy) continue; // a SupplyFromCP column would make the reader take the routes from Materials
                Set(ws, 1, next, cols[i], log); Set(ws, 2, next, desc[i], log);
                ws.Cell(1, next).Style.Font.Bold = true; ws.Cell(1, next).Style.Fill.BackgroundColor = XLColor.LightBlue;
                FitWidth(ws, next, 2);
                next++;
                if (!(route && moving != null)) changes.Add($"{sheet}: {cols[i]} 열 추가"); // the merge says it in one line
            }
            if (described) changes.Add($"{sheet}: 2행 설명 갱신");
        }
        if (moving != null)
        {
            var mws = wb.Worksheet("Materials"); var at = Columns(mws);
            foreach (var (mat, vals) in moving)
            {
                int row = RowOf(mws, "Material", mat);
                for (int k = 0; k < RouteCols.Length; k++)
                    if (vals[k].Length > 0) Set(mws, row, at[RouteCols[k]], vals[k], log);
            }
            wb.Worksheet("Routes").Delete(); log?.Add(new DeleteSheetEdit("Routes"));
            changes.Add($"Routes 시트를 Materials에 합침 {moving.Count}행");
        }
        var set = wb.Worksheet("Settings");
        var keys = new HashSet<string>(Values(set, "Key"), StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v, d) in SettingsDefaults)
            if (!keys.Contains(k)) { Append(set, ColsOf("Settings"), new[] { k, v, d }, log); changes.Add($"Settings: {k} = {v} 추가"); }

        if (catalog != null && catalog.Count > 0)
        {
            var tws = wb.Worksheet("AGV_Types");
            var have = new HashSet<string>(Values(tws, "Type"), StringComparer.OrdinalIgnoreCase);
            foreach (var t in catalog)
                if (have.Add(t.Name)) { Append(tws, ColsOf("AGV_Types"), ASH.Core.Catalog.CatalogType.ToTemplateRow(t), log); changes.Add($"AGV_Types: 타입 {t.Name} 추가(카탈로그)"); }
        }
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
                if (gc.TryGetValue("StandbyCPs", out var sc))
                {
                    var listed = Split(gws.Cell(row, sc).GetString());
                    var add = Split(g[4]).Where(x => !listed.Contains(x)).ToList();
                    if (add.Count > 0)
                    {
                        Set(gws, row, sc, string.Join(",", listed.Concat(add)), log);
                        changes.Add($"AGV_Groups: 그룹 {g[0]} 대기 CP {string.Join(",", add)} 추가");
                    }
                }
                // the finish CP of a recorded sequence fills a blank cell only
                if (g[6].Length > 0 && gc.TryGetValue("FinishCP", out var fc) && gws.Cell(row, fc).GetString().Trim().Length == 0)
                {
                    Set(gws, row, fc, g[6], log);
                    changes.Add($"AGV_Groups: 그룹 {g[0]} 완료 CP {g[6]} 추가");
                }
            }
        }
        var fresh = new List<int>(); // Materials rows added here: their CallThreshold is the example's, not typed
        if (seed.TryGetValue("Materials", out var sm))
        {
            // the routes are on the material rows, or on a Routes sheet kept for a workbook that could not be merged
            var mws = wb.Worksheet("Materials"); var rws = legacy ? wb.Worksheet("Routes") : mws;
            int fromCol = MatCol("SupplyFromCP"), toCol = MatCol("SupplyToCP");
            // (load, unload) of the rows already there: a load CP with more seed routes than rows (U2 added for L1) gets the missing ones
            var have = new List<(string From, string To)>();
            var rc = Columns(rws);
            if (rc.TryGetValue("SupplyFromCP", out var fcol))
                for (int r = 3, last = LastDataRow(rws); r <= last; r++)
                {
                    var f = rws.Cell(r, fcol).GetString().Trim();
                    if (f.Length > 0) have.Add((f, rc.TryGetValue("SupplyToCP", out var tcol) ? rws.Cell(r, tcol).GetString().Trim() : ""));
                }
            var names = new HashSet<string>(Values(mws, "Material").Concat(legacy ? Values(rws, "Material") : new List<string>()));
            var recordedOf = CpRoutes.From(layout!).SelectMany(r => r.Load.Select(l => (Load: l, Route: r))).GroupBy(x => x.Load).ToDictionary(x => x.Key, x => x.First().Route);
            var where = legacy ? "Materials·Routes" : "Materials";
            for (int i = 0; i < sm.Count; i++)
            {
                var key = (sm[i][fromCol], sm[i][toCol]);
                // a row the user edited (other unload) counts for its load: only routes beyond the rows there are added
                if (have.Contains(key) || have.Count(h => h.From == key.Item1) >= sm.Count(x => x[fromCol] == key.Item1)) continue;
                have.Add(key);
                // the next free M###
                recordedOf.TryGetValue(key.Item1, out var rec);
                string name; int n = 1;
                do name = "M" + (n++).ToString("000"); while (names.Contains(name));
                names.Add(name);
                var m = (string[])sm[i].Clone(); m[MatCol("Material")] = name;
                // the Form the group's vehicle carries, by the groups and types already in this workbook (the seed's are the drawing's defaults)
                if (wb.TryGetWorksheet("AGV_Groups", out var gws2) && wb.TryGetWorksheet("AGV_Types", out var tws2)
                    && FormFor(m[MatCol("Group")], Pairs(gws2, "Group", "Type"), Pairs(tws2, "Type", "Kind")) is { } form) m[MatCol("Form")] = form;
                fresh.Add(Append(mws, ColsOf("Materials"), m, log)); // legacy: Materials has no route columns, so only the material's cells
                if (legacy) Append(rws, new[] { "Material" }.Concat(RouteCols).ToArray(), new[] { name }.Concat(RouteCols.Select(c => m[MatCol(c)])).ToArray(), log);
                changes.Add(rec != null
                    ? $"{where}: 경로 {rec.Group} ({rec.Describe()})의 자재 {name} 추가(T/T·임계값 확인 필요)"
                    : $"{where}: load CP {key.Item1}의 자재 {name} 추가(T/T·임계값·unload CP 확인 필요)");
            }
        }
        // named AGV_CONV conveyors without a Conveyors row get one (the sheet itself was added above when the workbook lacked it)
        if (layout != null && wb.TryGetWorksheet("Conveyors", out var cws))
            foreach (var row in ConveyorRows(layout, new HashSet<string>(Values(cws, "Conveyor"), StringComparer.OrdinalIgnoreCase)))
            {
                Append(cws, ColsOf("Conveyors"), row, log);
                changes.Add($"Conveyors: 컨베이어 {row[0]} 추가(소비 자재 입력 필요)");
            }
        ApplyLists(wb, log, layout);
        ApplyTotals(wb, log);
        ApplyAutoFill(wb, log);
        ApplyCenter(wb, log);
        ApplyOutline(wb, log);
        if (layout != null) SuggestCalls(wb, layout, scale, fresh, changes, log);
        changes.Add(NeedSummary(wb));
        return changes;
    }

    /// <summary>The rows of an older workbook's Routes sheet as (material, <see cref="RouteCols"/> values); null with the reason when moving them onto the
    /// Materials rows would not read back the same: a material with several routes, a route row without a material or with one Materials lacks, a
    /// material name on two Materials rows.</summary>
    static List<(string Material, string[] Values)>? RouteRows(IXLWorksheet rws, IXLWorksheet? mws, out string why)
    {
        why = "";
        var at = Columns(rws);
        var rows = new List<(string Material, string[] Values)>();
        for (int r = 3, last = LastDataRow(rws); r <= last; r++)
        {
            string Get(string col) => at.TryGetValue(col, out var c) ? Text(rws.Cell(r, c)) : "";
            var vals = RouteCols.Select(Get).ToArray();
            var mat = Get("Material");
            if (mat.Length == 0 && vals.All(v => v.Length == 0)) continue;
            if (mat.Length == 0) { why = $"{r}행 자재 이름 없음"; return null; }
            rows.Add((mat, vals));
        }
        var mats = mws == null ? new List<string>() : Values(mws, "Material");
        string? Names(IEnumerable<string> n) => n.Any() ? string.Join(",", n.Distinct()) : null;
        if (Names(rows.GroupBy(x => x.Material).Where(g => g.Count() > 1).Select(g => g.Key)) is { } multi) why = $"자재당 경로 여러 행({multi}), 모델은 자재당 1개";
        else if (Names(rows.Select(x => x.Material).Where(m => !mats.Contains(m))) is { } orphan) why = $"Materials에 없는 자재({orphan})";
        else if (Names(mats.GroupBy(m => m).Where(g => g.Count() > 1).Select(g => g.Key)) is { } dup) why = $"Materials 자재 이름 중복({dup})";
        return why.Length > 0 ? null : rows;
    }

    /// <summary>A cell as text: a number in invariant digits (what a CellEdit and the reader both take), other values trimmed.</summary>
    static string Text(IXLCell c) => c.DataType == XLDataType.Number ? c.GetDouble().ToString("R", CultureInfo.InvariantCulture) : c.GetString().Trim();

    static double? Num(IXLCell c)
    {
        if (c.DataType == XLDataType.Number) return c.GetDouble();
        return double.TryParse(c.GetString().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>The suggested CallThreshold (containers) for a material: the containers one supply cycle uses, rounded up, plus one; at least the safety stock
    /// plus one and one takt's use (the validator warns below either). The cycle is <see cref="InputValidator.SupplyCapacity"/>'s: route length (m) /
    /// SpeedLoaded + LoadTime + UnloadTime + ExtraTime; a container lasts TaktTime × UnitsPerContainer / UnitsPerCycle. Null when a number cannot be used.</summary>
    internal static int? SuggestCall(double lengthM, double speedLoaded, double loadTime, double unloadTime, double extraTime, double takt, double unitsPerCycle, double unitsPerContainer, double safety)
    {
        if (!(speedLoaded > 0) || !(takt > 0) || !(unitsPerCycle > 0) || !(unitsPerContainer > 0) || !(lengthM >= 0)) return null;
        double cycle = lengthM / speedLoaded + loadTime + unloadTime + Math.Max(0, extraTime), lasts = takt * unitsPerContainer / unitsPerCycle;
        int n = (int)Math.Ceiling(cycle / lasts - 1e-9) + 1;
        return Math.Max(n, Math.Max((int)Math.Floor(safety) + 1, (int)Math.Ceiling(unitsPerCycle / unitsPerContainer - 1e-9)));
    }

    /// <summary>E2: <see cref="SuggestCall"/> into each named Materials row whose CallThreshold is blank (and its unit part too) or that this run added
    /// (<paramref name="fresh"/>: the example's value). The route length comes from the drawing (as ModelBuilder gets it: GroupClusters' SupplyLength ×
    /// scale), the vehicle from the row's group. A row whose length, vehicle or numbers are not known keeps its cell; nothing without a scale.</summary>
    static void SuggestCalls(XLWorkbook wb, ASH.Core.Layout.Layout layout, double scale, ICollection<int> fresh, List<string> changes, List<ExcelEdit>? log)
    {
        if (!(scale > 0) || !wb.TryGetWorksheet("Materials", out var ws) || !wb.TryGetWorksheet("AGV_Groups", out var gws) || !wb.TryGetWorksheet("AGV_Types", out var tws)) return;
        var at = Columns(ws);
        if (!at.TryGetValue("CallThreshold", out var cc)) return;
        // the route cells: on the row itself, or on the Routes sheet an older workbook kept
        var rws = at.ContainsKey("SupplyFromCP") ? ws : wb.TryGetWorksheet("Routes", out var old) ? old : null;
        if (rws == null) return;
        var rat = Columns(rws); var tat = Columns(tws);
        string S(IXLWorksheet s, Dictionary<string, int> a, int r, string col) => a.TryGetValue(col, out var c) ? s.Cell(r, c).GetString().Trim() : "";
        double? N(IXLWorksheet s, Dictionary<string, int> a, int r, string col) => a.TryGetValue(col, out var c) ? Num(s.Cell(r, c)) : null;
        var rows = new List<(int Row, string Name, int RouteRow)>();
        var input = new ProjectInput();
        for (int r = 3, last = LastDataRow(ws); r <= last; r++)
        {
            var name = S(ws, at, r, "Material");
            if (name.Length == 0 || !fresh.Contains(r) && (S(ws, at, r, "CallThreshold").Length > 0 || S(ws, at, r, "CallThresholdQty").Length > 0)) continue;
            int rr = rws == ws ? r : RowOf(rws, "Material", name);
            string from = rr == 0 ? "" : S(rws, rat, rr, "SupplyFromCP"), to = rr == 0 ? "" : S(rws, rat, rr, "SupplyToCP");
            if (from.Length == 0 || to.Length == 0) continue; // a lone CP would measure 0
            rows.Add((r, name, rr));
            input.Routes.Add(new Route(name, from, to, S(rws, rat, rr, "BufferCP") is { Length: > 0 } b ? b : null, null, null, 0));
        }
        if (rows.Count == 0) return;
        var len = ASH.Core.Runner.GroupClusters.Build(layout, input, new ASH.Core.Traffic.TrafficTable()).SupplyLength; // no groups: the traffic blocks are not used
        var typeOf = Pairs(gws, "Group", "Type");
        double safetyDefault = wb.TryGetWorksheet("Settings", out var set) && RowOf(set, "Key", "SafetyStock") is var sr && sr > 0 ? N(set, Columns(set), sr, "Value") ?? 2 : 2;
        foreach (var (r, name, rr) in rows)
        {
            if (!len.TryGetValue(name, out var length) || !typeOf.TryGetValue(S(ws, at, r, "Group"), out var type)) continue;
            int tr = RowOf(tws, "Type", type);
            double? speed = tr == 0 ? null : N(tws, tat, tr, "SpeedLoaded"), takt = N(ws, at, r, "TaktTime"), perCycle = N(ws, at, r, "UnitsPerCycle"), perBox = N(ws, at, r, "UnitsPerContainer");
            if (speed == null || takt == null || perCycle == null || perBox == null) continue;
            var n = SuggestCall(length * scale, speed.Value, N(tws, tat, tr, "LoadTime") ?? 0, N(tws, tat, tr, "UnloadTime") ?? 0, N(rws, rat, rr, "ExtraTime") ?? 0,
                takt.Value, perCycle.Value, perBox.Value, N(ws, at, r, "SafetyStock") ?? safetyDefault);
            if (n == null) continue;
            Set(ws, r, cc, n.Value.ToString(CultureInfo.InvariantCulture), log);
            changes.Add($"Materials: {name} 호출 임계 {n} (제안)");
        }
    }

    /// <summary>E5: one line of the blank <see cref="Required"/> cells on rows that have a name, per sheet and column ("입력 필요: Materials T/T 3칸 · …").
    /// A Use N row needs nothing; a conveyor-fed material takes TaktTime and UnitsPerCycle from Conveyors; CallThresholdQty fills CallThreshold.</summary>
    static string NeedSummary(XLWorkbook wb)
    {
        var fed = new HashSet<string>(wb.TryGetWorksheet("Conveyors", out var cv) ? Values(cv, "Material") : new List<string>());
        var parts = new List<string>();
        foreach (var sheet in Required.Select(x => x.Sheet).Distinct())
        {
            if (!wb.TryGetWorksheet(sheet, out var ws)) continue;
            var at = Columns(ws); var cols = ColsOf(sheet); var desc = Sheets.First(x => x.Sheet == sheet).Desc;
            string S(int r, string col) => at.TryGetValue(col, out var c) ? ws.Cell(r, c).GetString().Trim() : "";
            var named = Enumerable.Range(3, Math.Max(0, LastDataRow(ws) - 2)).Where(r => S(r, cols[0]).Length > 0 && S(r, "Use").ToUpperInvariant() is not ("N" or "아니오" or "0")).ToList();
            var counts = new List<string>();
            foreach (var (_, col) in Required.Where(x => x.Sheet == sheet))
            {
                if (!at.ContainsKey(col)) continue;
                int n = named.Count(r => S(r, col).Length == 0 && !(col == "CallThreshold" && S(r, "CallThresholdQty").Length > 0)
                    && !((col == "TaktTime" || col == "UnitsPerCycle") && fed.Contains(S(r, cols[0]))));
                var label = desc[Array.IndexOf(cols, col)];
                if (n > 0) counts.Add($"{(label.IndexOf('(') > 0 ? label.Substring(0, label.IndexOf('(')) : label).Trim()} {n}칸");
            }
            if (counts.Count > 0) parts.Add($"{sheet} {string.Join(", ", counts)}");
        }
        return parts.Count == 0 ? NeedPrefix + " 없음" : $"{NeedPrefix}: {string.Join(" · ", parts)}";
    }

    /// <summary>E1: each run of side-by-side <see cref="Advanced"/> columns is grouped (Excel outline) and collapsed, behind "+". Columns are never moved, so
    /// an older workbook may get several runs; a run already grouped is left as it is (grouping again would add a level).</summary>
    static void ApplyOutline(XLWorkbook wb, List<ExcelEdit>? log = null)
    {
        foreach (var sheet in Advanced.Select(a => a.Sheet).Distinct())
        {
            if (!wb.TryGetWorksheet(sheet, out var ws)) continue;
            var adv = new HashSet<int>(Columns(ws).Where(kv => Advanced.Contains((sheet, kv.Key))).Select(kv => kv.Value));
            foreach (var first in adv.Where(c => !adv.Contains(c - 1)).OrderBy(c => c).ToList())
            {
                int last = first; while (adv.Contains(last + 1)) last++;
                if (Enumerable.Range(first, last - first + 1).Any(c => ws.Column(c).OutlineLevel > 0)) continue;
                var run = ws.Columns(first, last);
                run.Group(); run.Collapse(); // Group(true) does not hide them (ClosedXML 0.102)
                log?.Add(new GroupColumnsEdit(sheet, first, last));
            }
        }
    }

    static void Set(IXLWorksheet ws, int r, int c, string v, List<ExcelEdit>? log)
    {
        Put(ws.Cell(r, c), v);
        log?.Add(new CellEdit(ws.Name, r, c, v));
    }

    static string[] ColsOf(string sheet) => Sheets.First(x => x.Sheet == sheet).Cols;

    /// <summary>The index of a Materials column in the template's (and a seed row's) order.</summary>
    static int MatCol(string col) => Array.IndexOf(ColsOf("Materials"), col);

    /// <summary>The first form the vehicle kind of <paramref name="group"/>'s type carries; null when the group, type or kind is not known.</summary>
    internal static string? FormFor(string group, IReadOnlyDictionary<string, string> typeOfGroup, IReadOnlyDictionary<string, string> kindOfType) =>
        typeOfGroup.TryGetValue(group, out var t) && kindOfType.TryGetValue(t, out var k) && VehicleKinds.Find(k) is { Carries.Length: > 0 } kind ? kind.Carries[0] : null;

    /// <summary>Column <paramref name="key"/> → column <paramref name="value"/> of a sheet's data rows (first row wins); empty when either column is missing.</summary>
    static Dictionary<string, string> Pairs(IXLWorksheet ws, string key, string value)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        var c = Columns(ws);
        if (!c.TryGetValue(key, out var kc) || !c.TryGetValue(value, out var vc)) return d;
        for (int r = 3, last = LastDataRow(ws); r <= last; r++)
        {
            var k = ws.Cell(r, kc).GetString().Trim();
            if (k.Length > 0 && !d.ContainsKey(k)) d[k] = ws.Cell(r, vc).GetString().Trim();
        }
        return d;
    }

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

    /// <summary>A row after the last used one, each value under its template column wherever that column is in this sheet. Returns the row.</summary>
    static int Append(IXLWorksheet ws, string[] cols, string[] values, List<ExcelEdit>? log)
    {
        var at = Columns(ws);
        int r = Math.Max(3, LastDataRow(ws) + 1);
        for (int i = 0; i < cols.Length && i < values.Length; i++)
            if (values[i].Length > 0 && at.TryGetValue(cols[i], out var c)) Set(ws, r, c, values[i], log);
        return r;
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

    internal static void ApplyAutoFill(XLWorkbook wb, List<ExcelEdit>? log = null)
    {
        foreach (var (sheet, col) in AutoCols)
        {
            if (!wb.TryGetWorksheet(sheet, out var ws) || !Columns(ws).TryGetValue(col, out var c)) continue;
            ws.Range(3, c, LastListRow, c).Style.Fill.BackgroundColor = XLColor.FromArgb(AutoFillRgb);
            log?.Add(new FillEdit(sheet, c, 3, LastListRow, AutoFillRgb));
        }
    }

    /// <summary>Every cell of the template's sheets is centred horizontally. Set on the whole column, so rows typed in later follow too.</summary>
    static void ApplyCenter(XLWorkbook wb, List<ExcelEdit>? log = null)
    {
        foreach (var (sheet, _, _, _) in Sheets)
        {
            if (!wb.TryGetWorksheet(sheet, out var ws)) continue;
            int last = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (last == 0) continue;
            ws.Columns(1, last).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            log?.Add(new AlignEdit(sheet, 1, last));
        }
    }

    /// <summary>The comma list of the Asset drop-down (asset names; empty = no drop-down; Excel caps a list at 255 chars). Set by the plugin from the catalog before Write/Update.</summary>
    public static string AssetListFormula { get; set; } = "";

    const string AssetMsg = "카탈로그에 없는 에셋 이름입니다. 그대로 쓰려면 예";

    /// <summary>Hidden helper sheet. Column A: asset names; columns C.. : the drawing's CP names per type (<see cref="CpListTypes"/>), one name per row from row 1.</summary>
    const string ListsSheet = "_lists";

    /// <summary>CP types in the order of their columns in the helper sheet (C = 3 ...), and the cells that drop down one of them.</summary>
    static readonly string[] CpListTypes = { "load", "unload", "buffer", "rtnload", "rtnunload", "standby", "finish" };
    internal static readonly (string Sheet, string Col, string CpType)[] CpLists =
    {
        ("Materials", "SupplyFromCP", "load"), ("Materials", "SupplyToCP", "unload"), ("Materials", "BufferCP", "buffer"),
        ("Materials", "ReturnFromCP", "rtnload"), ("Materials", "ReturnToCP", "rtnunload"),
        ("AGV_Groups", "StandbyCPs", "standby"), ("AGV_Groups", "FinishCP", "finish"),
    };
    const string CpMsg = "도면에 없는 CP 이름입니다. 그대로 쓰려면 예";

    static IXLWorksheet ListsSheetOf(XLWorkbook wb, List<ExcelEdit>? log)
    {
        if (wb.TryGetWorksheet(ListsSheet, out var ls)) { ls.Visibility = XLWorksheetVisibility.Hidden; return ls; }
        ls = wb.AddWorksheet(ListsSheet); log?.Add(new AddSheetEdit(ListsSheet, true));
        ls.Visibility = XLWorksheetVisibility.Hidden;
        return ls;
    }

    /// <summary>Names down a column of the helper sheet from row 1; rows left over from a longer earlier list are cleared.</summary>
    static void PutColumn(IXLWorksheet ls, int col, IReadOnlyList<string> names, List<ExcelEdit>? log)
    {
        int prev = ls.Column(col).LastCellUsed()?.Address.RowNumber ?? 0;
        for (int i = 0; i < names.Count; i++) Set(ls, i + 1, col, names[i], log);
        for (int i = names.Count + 1; i <= prev; i++) { ls.Cell(i, col).Clear(); log?.Add(new CellEdit(ListsSheet, i, col, "")); }
    }

    /// <summary>Drop-downs of the drawing's CP names on the route and group columns, filled from the hidden helper sheet. A warning, not a stop: a name
    /// typed ahead of the drawing is allowed after a yes (AGVEXCELDIFF lists it). StandbyCPs takes a comma list, so it drops down only while no row has one.</summary>
    static void ApplyCpLists(XLWorkbook wb, List<ExcelEdit>? log, ASH.Core.Layout.Layout layout)
    {
        var ls = ListsSheetOf(wb, log);
        var counts = new int[CpListTypes.Length];
        for (int t = 0; t < CpListTypes.Length; t++)
        {
            var names = layout.Cps.Where(c => c.Type == CpListTypes[t] && c.Name.Length > 0).Select(c => c.Name).Distinct().ToList();
            counts[t] = names.Count;
            PutColumn(ls, 3 + t, names, log);
        }
        foreach (var (sheet, col, type) in CpLists)
        {
            if (!wb.TryGetWorksheet(sheet, out var ws) || !Columns(ws).TryGetValue(col, out var c)) continue;
            var range = ws.Range(3, c, LastListRow, c);
            if (type == "standby" && Values(ws, col).Any(v => v.IndexOf(',') >= 0))
            {
                range.Clear(XLClearOptions.DataValidation);
                log?.Add(new ListEdit(sheet, c, 3, LastListRow, "", col, "", true)); // empty formula = no drop-down
                continue;
            }
            int t = Array.IndexOf(CpListTypes, type), lc = 3 + t, n = Math.Max(1, counts[t]);
            var v = range.CreateDataValidation();
            v.List(ls.Range(1, lc, n, lc), true);
            v.IgnoreBlanks = true; v.ErrorStyle = XLErrorStyle.Warning; v.ErrorTitle = col; v.ErrorMessage = CpMsg; v.ShowErrorMessage = true;
            var letter = XLHelper.GetColumnLetterFromNumber(lc);
            log?.Add(new ListEdit(sheet, c, 3, LastListRow, $"={ListsSheet}!${letter}$1:${letter}${n}", col, CpMsg, true));
        }
    }

    static void ApplyLists(XLWorkbook wb, List<ExcelEdit>? log = null, ASH.Core.Layout.Layout? layout = null)
    {
        var all = AssetListFormula.Length == 0 ? Lists : Lists.Concat(new[] { ("AGV_Types", "Asset", AssetListFormula) }).ToArray();
        foreach (var (sheet, col, source) in all)
        {
            if (!wb.TryGetWorksheet(sheet, out var ws) || !Columns(ws).TryGetValue(col, out var c)) continue;
            bool isAsset = sheet == "AGV_Types" && col == "Asset";
            var v = ws.Range(3, c, LastListRow, c).CreateDataValidation();
            if (wb.TryGetWorksheet(source, out var src))
            {
                v.List(src.Range(3, 1, LastListRow, 1), true);
                v.ErrorMessage = $"{source} 시트의 이름 중에서 고르세요.";
                log?.Add(new ListEdit(sheet, c, 3, LastListRow, $"={source}!$A$3:$A${LastListRow}", col, v.ErrorMessage));
            }
            else if (isAsset && source.Length > 253) // Excel caps an inline list at 255 chars: names go to a hidden sheet
            {
                var names = source.Split(',');
                var ls = ListsSheetOf(wb, log);
                PutColumn(ls, 1, names, log); // stale rows below the new list are cleared
                v.List(ls.Range(1, 1, names.Length, 1), true);
                v.ErrorMessage = AssetMsg;
                log?.Add(new ListEdit(sheet, c, 3, LastListRow, $"=_lists!$A$1:$A${names.Length}", col, v.ErrorMessage, true));
            }
            else
            {
                v.List("\"" + source + "\"", true);
                v.ErrorMessage = isAsset ? AssetMsg : source.Replace(",", " / ") + " 중에서 고르세요.";
                log?.Add(new ListEdit(sheet, c, 3, LastListRow, source, col, v.ErrorMessage, isAsset));
            }
            v.IgnoreBlanks = true;
            v.ErrorStyle = isAsset ? XLErrorStyle.Warning : XLErrorStyle.Stop;
            v.ErrorTitle = col;
            v.ShowErrorMessage = true;
        }
        if (layout != null) ApplyCpLists(wb, log, layout);
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
    internal static void Save(XLWorkbook wb, string path)
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

/// <summary>One change <see cref="ExcelTemplate.Update(XLWorkbook, ASH.Core.Layout.Layout?, List{ExcelEdit}?, IReadOnlyList{ASH.Core.Catalog.CatalogType}?, double)"/> made, in order, so that Excel itself can
/// repeat it on a file only Excel can write (company document encryption). Columns and rows are 1-based, as they were when the edit was made.</summary>
public abstract record ExcelEdit(string Sheet);
public sealed record AddSheetEdit(string Sheet, bool Hidden = false) : ExcelEdit(Sheet);
public sealed record DeleteColumnEdit(string Sheet, int Col) : ExcelEdit(Sheet);
/// <summary>The whole sheet deleted (an older workbook's Routes sheet, once merged into Materials).</summary>
public sealed record DeleteSheetEdit(string Sheet) : ExcelEdit(Sheet);
/// <summary>Columns FirstCol..LastCol grouped (Excel outline) and collapsed; left alone when any of them is grouped already.</summary>
public sealed record GroupColumnsEdit(string Sheet, int FirstCol, int LastCol) : ExcelEdit(Sheet);
public sealed record CellEdit(string Sheet, int Row, int Col, string Value) : ExcelEdit(Sheet);
/// <summary>One R1C1 formula over rows FirstRow..LastRow of a column.</summary>
public sealed record FormulaEdit(string Sheet, int Col, int FirstRow, int LastRow, string FormulaR1C1) : ExcelEdit(Sheet);
/// <summary>A background colour (RGB, 0xRRGGBB) over rows FirstRow..LastRow of a column: the cells that need no input.</summary>
public sealed record FillEdit(string Sheet, int Col, int FirstRow, int LastRow, int Rgb) : ExcelEdit(Sheet);
/// <summary>Columns FirstCol..LastCol centred horizontally.</summary>
public sealed record AlignEdit(string Sheet, int FirstCol, int LastCol) : ExcelEdit(Sheet);
/// <summary>A drop-down over rows FirstRow..LastRow: Formula is a sheet range ("=AGV_Types!$A$3:$A$1000") or a comma list ("Box,Pallet").</summary>
public sealed record ListEdit(string Sheet, int Col, int FirstRow, int LastRow, string Formula, string Title, string Message, bool Warn = false) : ExcelEdit(Sheet);
