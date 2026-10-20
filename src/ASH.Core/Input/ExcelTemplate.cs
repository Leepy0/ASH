using ClosedXML.Excel;

namespace ASH.Core.Input;

public static class ExcelTemplate
{
    // sheet -> (columns, descriptions, example row). Single source of truth; ExcelReader uses the same names.
    public static readonly (string Sheet, string[] Cols, string[] Desc, string[] Example)[] Sheets =
    {
        ("Settings", new[] { "Key", "Value", "Description" }, new[] { "키", "값", "설명" }, new string[0]),
        ("AGV_Types",
            new[] { "Type", "Group", "SpeedLoaded", "SpeedEmpty", "Accel", "Decel", "Capacity", "LoadTime", "UnloadTime", "MTBF", "MTTR", "Length", "Width", "Color" },
            new[] { "타입명", "그룹명", "적재 속도(m/s)", "공차 속도(m/s)", "가속(m/s²)", "감속(m/s²)", "용기 수", "로딩(초)", "언로딩(초)", "MTBF(초, 0=없음)", "MTTR(초)", "길이(m)", "폭(m)", "색상" },
            new[] { "T_EX", "G_EX", "1.0", "1.2", "0.5", "0.5", "2", "20", "20", "0", "0", "1.2", "0.8", "Blue" }),
        ("AGV_Groups",
            new[] { "Group", "Type", "MinCount", "MaxCount", "StandbyCPs", "Dispatch" },
            new[] { "그룹명", "AGV 타입(하나)", "최소 대수", "최대 대수", "대기 CP(쉼표 구분)", "Nearest / FIFO" },
            new[] { "G_EX", "T_EX", "1", "3", "CP_STBY", "Nearest" }),
        ("Materials",
            new[] { "Material", "Station", "Group", "TaktTime", "UnitsPerCycle", "UnitsPerContainer", "CallThreshold", "UnloadThreshold", "InitStock", "SafetyStock", "EmptyReturn" },
            new[] { "자재명", "공정명", "그룹명", "T/T(초)", "T/T당 소비 원수", "용기당 원수", "호출 임계(용기)", "언로딩 임계(용기)", "초기재고(용기, 비우면 Settings)", "안전재고(용기, 비우면 Settings)", "빈 용기 회수 Y/N" },
            new[] { "M001", "S_EX", "G_EX", "60", "1", "10", "2", "1", "", "", "Y" }),
        ("Routes",
            new[] { "Material", "SupplyFromCP", "SupplyToCP", "BufferCP", "ReturnFromCP", "ReturnToCP", "ExtraTime" },
            new[] { "자재명", "load CP", "unload CP", "buffer CP(선택)", "rtnload CP(선택)", "rtnunload CP(선택)", "추가 시간(초)" },
            new[] { "M001", "CP_LOAD", "CP_UNLOAD", "", "", "", "0" }),
        ("Stations",
            new[] { "Station", "CPName", "BufferCapacity" },
            new[] { "공정명", "CP 이름", "적치 용기 수" },
            new[] { "S_EX", "CP_UNLOAD", "4" }),
        ("Ext",
            new[] { "Name", "Enabled", "Description" },
            new[] { "훅 이름", "사용 Y/N", "설명" },
            new[] { "FExtOnCall", "N", "호출 조건 변경" }),
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
    };

    public static void Write(string path)
    {
        using var wb = new XLWorkbook();
        foreach (var (sheet, cols, desc, example) in Sheets)
        {
            var ws = wb.AddWorksheet(sheet);
            for (int c = 0; c < cols.Length; c++)
            {
                ws.Cell(1, c + 1).Value = cols[c];
                ws.Cell(2, c + 1).Value = desc[c];
            }
            if (sheet == "Settings")
                for (int r = 0; r < SettingsDefaults.Length; r++)
                {
                    ws.Cell(r + 3, 1).Value = SettingsDefaults[r].Key;
                    ws.Cell(r + 3, 2).Value = SettingsDefaults[r].Value;
                    ws.Cell(r + 3, 3).Value = SettingsDefaults[r].Desc;
                }
            else
                for (int c = 0; c < example.Length; c++)
                    if (example[c].Length > 0) { var cell = ws.Cell(3, c + 1); if (double.TryParse(example[c], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) cell.Value = d; else cell.Value = example[c]; } // example row: replace or delete
            var head = ws.Range(1, 1, 1, cols.Length);
            head.Style.Font.Bold = true;
            head.Style.Fill.BackgroundColor = XLColor.LightGray;
            ws.Columns().AdjustToContents();
        }
        wb.SaveAs(path);
    }
}
