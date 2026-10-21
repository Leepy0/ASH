using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using ClosedXML.Excel;

namespace ASH.Core.Input;

public static class ExcelReader
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    sealed class Row
    {
        readonly IXLWorksheet _ws; readonly Dictionary<string, int> _cols; readonly int _row;
        public int RowNum => _row;
        public Row(IXLWorksheet ws, Dictionary<string, int> cols, int row) { _ws = ws; _cols = cols; _row = row; }

        FormatException Err(string col, string why) => new ExcelDataException($"{_ws.Name} 시트 {_row}행 '{col}' 열: {why}");
        IXLCell Cell(string col) => _cols.TryGetValue(col, out var c) ? _ws.Cell(_row, c) : throw new FormatException($"{_ws.Name} 시트에 '{col}' 열이 없습니다.");
        public string Str(string col) => Cell(col).GetString().Trim();
        public string? OptStr(string col) { var s = Str(col); return s.Length == 0 ? null : s; }

        public double? OptNum(string col)
        {
            var cell = Cell(col);
            var s = Str(col);
            if (s.Length == 0) return null;
            if (cell.DataType == XLDataType.Number) return cell.GetDouble();
            return double.TryParse(s, NumberStyles.Float, Inv, out var d) ? d : throw Err(col, $"숫자가 아닙니다 ('{s}')");
        }
        public double Num(string col, double? def = null) => OptNum(col) ?? def ?? throw Err(col, "값이 비어 있습니다");
        public int Int(string col, int? def = null)
        {
            double d = Num(col, def);
            return d == Math.Floor(d) ? (int)d : throw Err(col, $"정수가 아닙니다 ({d.ToString(Inv)})");
        }
        public bool Bool(string col)
        {
            var s = Str(col).ToUpperInvariant();
            if (s == "Y" || s == "TRUE" || s == "1") return true;
            if (s == "N" || s == "FALSE" || s == "0" || s.Length == 0) return false;
            throw Err(col, $"Y/N이 아닙니다 ('{Str(col)}')");
        }
    }

    // Data rows (row 3+), fully blank rows skipped.
    static List<Row> Rows(XLWorkbook wb, string sheet, bool optional = false)
    {
        if (!wb.TryGetWorksheet(sheet, out var ws)) return optional ? new List<Row>() : throw new FormatException($"'{sheet}' 시트가 없습니다.");
        var cols = new Dictionary<string, int>(StringComparer.Ordinal);
        int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (int c = 1; c <= lastCol; c++) { var h = ws.Cell(1, c).GetString().Trim(); if (h.Length > 0) cols[h] = c; }
        var list = new List<Row>();
        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (int r = 3; r <= lastRow; r++)
            if (Enumerable.Range(1, lastCol).Any(c => ws.Cell(r, c).GetString().Trim().Length > 0)) list.Add(new Row(ws, cols, r));
        return list;
    }

    public static ProjectInput Read(string path) => Read(path, out _);

    /// <summary>Opens the workbook with the font-free engine installed (the default one parses system fonts and fails inside ZWCAD).</summary>
    public static XLWorkbook Open(string path)
    {
        FontFreeGraphicEngine.Install();
        return new XLWorkbook(path);
    }

    /// <summary>An empty workbook, created with the font-free engine installed.</summary>
    public static XLWorkbook NewWorkbook()
    {
        FontFreeGraphicEngine.Install();
        return new XLWorkbook();
    }

    public static ProjectInput Read(string path, out List<Finding> warnings)
    {
        using var wb = Open(path);
        return Read(wb, out warnings);
    }

    /// <summary>The workbook may come from a file, from <see cref="CellDump"/>, or from cells read out of a running Excel.</summary>
    public static ProjectInput Read(XLWorkbook wb, out List<Finding> warnings)
    {
        var p = new ProjectInput();

        var kv = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
        var w = new List<Finding>();
        warnings = w;
        foreach (var r in Rows(wb, "Settings"))
        {
            var key = r.Str("Key");
            if (!Array.Exists(ExcelTemplate.SettingsDefaults, d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase)))
                w.Add(new Finding(FindingCode.InputWarning, $"알 수 없는 Settings 키 '{key}'은(는) 무시됩니다.", default(Point2), $"Settings!{r.RowNum}"));
            else if (kv.ContainsKey(key))
                w.Add(new Finding(FindingCode.InputWarning, $"Settings 키 '{key}'이(가) 중복되어 마지막 값을 사용합니다.", default(Point2), $"Settings!{r.RowNum}"));
            kv[key] = r;
        }
        T Key<T>(string k, Func<Row, T> get, T d) { if (!kv.TryGetValue(k, out var r)) return d; try { return get(r); } catch (FormatException e) { throw new FormatException($"{e.Message} (키 {k})"); } }
        int I(string k, int d) => Key(k, r => r.Int("Value", d), d);
        double N(string k, double d) => Key(k, r => r.Num("Value", d), d);
        string S(string k, string d) => kv.TryGetValue(k, out var r) && r.Str("Value").Length > 0 ? r.Str("Value") : d;
        var def = new Settings();
        p.Settings = new Settings
        {
            Units = S("Units", def.Units),
            SimLengthSec = N("SimLength", def.SimLengthSec / 3600) * 3600,
            WarmupSec = N("Warmup", def.WarmupSec / 3600) * 3600,
            Replications = I("Replications", def.Replications),
            SeedBase = I("SeedBase", def.SeedBase),
            UtilMax = N("UtilMax", def.UtilMax),
            SafetyStock = N("SafetyStock", def.SafetyStock),
            InitStockMode = S("InitStockMode", def.InitStockMode),
            InitStockSpread = N("InitStockSpread", def.InitStockSpread),
            TrafficMode = S("TrafficMode", def.TrafficMode),
            WorkDefinition = S("WorkDefinition", def.WorkDefinition),
            DeadlockSec = N("DeadlockSec", def.DeadlockSec),
        };

        foreach (var r in Rows(wb, "AGV_Types"))
            p.AgvTypes.Add(new AgvType(r.Str("Type"), r.Str("Group"), r.Num("SpeedLoaded"), r.Num("SpeedEmpty"), r.Num("Accel"), r.Num("Decel"),
                r.Int("Capacity"), r.Num("LoadTime", 0), r.Num("UnloadTime", 0), r.Num("MTBF", 0), r.Num("MTTR", 0), r.Num("Length"), r.Num("Width"), r.Str("Color"), r.RowNum));
        foreach (var r in Rows(wb, "AGV_Groups"))
            p.Groups.Add(new AgvGroup(r.Str("Group"), r.Str("Type"), r.Int("MinCount"), r.Int("MaxCount"),
                r.Str("StandbyCPs").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList(), r.Str("Dispatch"), r.RowNum));
        foreach (var r in Rows(wb, "Materials"))
            p.Materials.Add(new MaterialInfo(r.Str("Material"), r.Str("Station"), r.Str("Group"), r.Num("TaktTime"), r.Num("UnitsPerCycle"),
                r.Num("UnitsPerContainer"), r.Num("CallThreshold"), r.Num("UnloadThreshold"), r.OptNum("InitStock"), r.OptNum("SafetyStock"), r.Bool("EmptyReturn"), r.RowNum));
        foreach (var r in Rows(wb, "Routes"))
            p.Routes.Add(new Route(r.Str("Material"), r.Str("SupplyFromCP"), r.Str("SupplyToCP"), r.OptStr("BufferCP"),
                r.OptStr("ReturnFromCP"), r.OptStr("ReturnToCP"), r.Num("ExtraTime", 0), r.RowNum));
        foreach (var r in Rows(wb, "Stations", optional: true))
            p.Stations.Add(new StationInfo(r.Str("Station"), r.Str("CPName"), r.Num("BufferCapacity", 0), r.RowNum));
        foreach (var r in Rows(wb, "Ext", optional: true))
            p.Ext.Add(new ExtHook(r.Str("Name"), r.Bool("Enabled"), r.Str("Description"), r.RowNum));
        return p;
    }
}

/// <summary>A value in the workbook that cannot be used; the message names sheet, row and column. Other failures (a damaged file) are explained by ExcelDiagnosis.</summary>
public sealed class ExcelDataException : FormatException
{
    public ExcelDataException(string message) : base(message) { }
}
