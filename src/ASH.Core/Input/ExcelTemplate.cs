using System;
using System.Collections.Generic;
using ClosedXML.Excel;

namespace ASH.Core.Input;

public static class ExcelTemplate
{
    // sheet -> (columns, descriptions, example row). Single source of truth; ExcelReader uses the same names.
    public static readonly (string Sheet, string[] Cols, string[] Desc, string[] Example)[] Sheets =
    {
        ("Settings", new[] { "Key", "Value", "Description" }, new[] { "키", "값", "설명" }, new string[0]),
        ("AGV_Types",
            new[] { "Type", "Group", "SpeedLoaded", "SpeedEmpty", "Accel", "Decel", "Capacity", "LoadTime", "UnloadTime", "MTBF", "MTTR", "Length", "Width", "Color", "Kind" },
            new[] { "타입명", "그룹명", "적재 속도(m/s)", "공차 속도(m/s)", "가속(m/s²)", "감속(m/s²)", "용기 수", "로딩(초)", "언로딩(초)", "MTBF(초, 0=없음)", "MTTR(초)", "길이(m)", "폭(m)", "색상", "형상(Standard/Conveyor/Ant/Magnet)" },
            // SpeedEmpty/Capacity/MTBF/MTTR are not modelled yet (v2); Accel/Decel are (vehicle accel and decel lines): the example keeps their neutral values
            new[] { "T_EX", "G_EX", "1.0", "1.0", "0.3", "0.3", "1", "20", "20", "0", "0", "1.2", "0.8", "Blue", "Standard" }),
        ("AGV_Groups",
            new[] { "Group", "Type", "MinCount", "MaxCount", "StandbyCPs", "Dispatch" },
            new[] { "그룹명", "AGV 타입(하나)", "최소 대수", "최대 대수", "대기 CP(쉼표 구분)", "Nearest / FIFO" },
            new[] { "G_EX", "T_EX", "1", "3", "CP_STBY", "Nearest" }),
        ("Materials",
            new[] { "Material", "Station", "Group", "TaktTime", "UnitsPerCycle", "UnitsPerContainer", "CallThreshold", "UnloadThreshold", "InitStock", "SafetyStock", "EmptyReturn", "Form" },
            new[] { "자재명", "공정명(선택·메모)", "그룹명", "T/T(초)", "T/T당 소비 원수", "용기당 원수", "호출 임계(용기)", "언로딩 임계(용기)", "초기재고(용기, 비우면 Settings)", "안전재고(용기, 비우면 Settings)", "빈 용기 회수 Y/N", "형태(Box/Pallet/Trolley)" },
            new[] { "M001", "", "G_EX", "60", "1", "10", "4", "1", "", "", "Y", "Box" }), // Call 4 > Settings SafetyStock 2
        ("Routes",
            new[] { "Material", "SupplyFromCP", "SupplyToCP", "BufferCP", "ReturnFromCP", "ReturnToCP", "ExtraTime" },
            new[] { "자재명", "load CP", "unload CP", "buffer CP(선택)", "rtnload CP(선택)", "rtnunload CP(선택)", "추가 시간(초)" },
            new[] { "M001", "CP_LOAD", "CP_UNLOAD", "", "", "", "0" }),
    };

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
            var types = new List<string[]>(); var gs = new List<string[]>();
            foreach (var g in groups)
            {
                var t = (string[])Ex("AGV_Types").Clone(); t[0] = "T_" + g; t[1] = g; types.Add(t);
                var r = (string[])Ex("AGV_Groups").Clone(); r[0] = g; r[1] = "T_" + g; r[4] = string.Join(",", Names("standby", g)); gs.Add(r);
            }
            seed["AGV_Types"] = types; seed["AGV_Groups"] = gs;
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
            var head = ws.Range(1, 1, 1, cols.Length);
            head.Style.Font.Bold = true;
            head.Style.Fill.BackgroundColor = XLColor.LightBlue;
            ws.SheetView.FreezeRows(2);
            // Widths are worked out from the text, not with AdjustToContents: that measures with the SixLabors font engine, which throws a
            // TypeInitializationException (TableLoader) inside ZWCAD's process.
            for (int c = 1; c <= cols.Length; c++)
            {
                var texts = new List<string>();
                for (int r = 1; r <= lastRow; r++) texts.Add(ws.Cell(r, c).GetString());
                ws.Column(c).Width = ColumnWidth(texts);
            }
        }
        // saved beside the target and moved over it: a failure half way must not leave a truncated input.xlsx that the next run takes for a real one
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
