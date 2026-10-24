using System.Globalization;
using System.Text;
using System.Text.Json;
using ASH.Core.Catalog;
using ASH.Core.Input;

namespace ASH.Server;

public sealed record PriceRow(string Type, decimal UnitPrice, string Currency, string Note);
public sealed record PriceSave(int Changed, IReadOnlyList<(int Row, string Why)> Errors, string? Backup);

/// <summary>Vehicle type catalog: Jobs:Dir\catalog\types.json and prices.csv (Type,UnitPrice,Currency,Note). Overwrites keep a .bak.</summary>
public sealed class CatalogStore
{
    const string PricesHeader = "Type,UnitPrice,Currency,Note";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    readonly object _lock = new();
    readonly ILogger _log;
    string TypesFile => Path.Combine(Dir, "types.json");
    string PricesFile => Path.Combine(Dir, "prices.csv");
    string? _copied;
    public string Dir { get; }
    /// <summary>Why types.json cannot be read (null = fine). While set, Save refuses so the real file is never overwritten.</summary>
    public string? LoadError { get; private set; }

    public CatalogStore(string jobsDir, ILogger log)
    {
        _log = log;
        Dir = Path.Combine(jobsDir, "catalog");
        Directory.CreateDirectory(Dir);
    }

    List<CatalogType> Load()
    {
        LoadError = null;
        if (!File.Exists(TypesFile)) return new();
        try
        {
            var all = JsonSerializer.Deserialize<CatalogTypes>(File.ReadAllText(TypesFile, Encoding.UTF8), Json)?.Types ?? new();
            if (all.RemoveAll(t => t == null || string.IsNullOrEmpty(t.Name)) > 0) _log.LogWarning("types.json: entries without a name were dropped: {File}", TypesFile);
            return all;
        }
        catch (JsonException e)
        {
            LoadError = e.Message;
            _log.LogError(e, "types.json unreadable: {File}", TypesFile);
            if (_copied != TypesFile) // once per process
            {
                _copied = TypesFile;
                try { File.Copy(TypesFile, TypesFile + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); } catch (IOException) { }
            }
            return new();
        }
    }

    public IReadOnlyList<CatalogType> Types(bool includeHidden = false)
    {
        lock (_lock) return Load().Where(t => includeHidden || !t.Hidden).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public CatalogType? Get(string name)
    {
        lock (_lock) return Load().FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Why the type cannot be saved, or null. Save checks the same; the paste form checks every row with it before saving any.</summary>
    public static string? Validate(CatalogType t)
    {
        if (!InputValidator.IsAmIdentifier(t.Name)) return $"타입 이름 '{t.Name}'은(는) AutoMod에서 쓸 수 없습니다(영문·숫자·_만, 영문 또는 _로 시작).";
        if (VehicleKinds.Find(t.Kind) == null) return $"종류 '{t.Kind}'을(를) 알 수 없습니다.";
        var d = t.Defaults;
        if (new[] { d.SpeedLoadedStraight, d.SpeedLoadedCurve, d.SpeedEmptyStraight, d.SpeedEmptyCurve, d.Accel, d.Decel, d.LoadTime, d.UnloadTime, d.MTBF, d.MTTR, d.Length, d.Width }
                .Any(v => double.IsNaN(v) || v < 0)) return "수치는 0 이상이어야 합니다.";
        if (d.Capacity < 1) return "적재 수량은 1 이상이어야 합니다.";
        return null;
    }

    public string? Save(CatalogType t, string by)
    {
        if (Validate(t) is { } why) return why;
        lock (_lock)
        {
            var all = Load();
            if (LoadError != null) return $"types.json을 읽을 수 없어 저장하지 않습니다: {LoadError}";
            all.RemoveAll(x => x.Name.Equals(t.Name, StringComparison.OrdinalIgnoreCase));
            t.UpdatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            t.UpdatedBy = by;
            all.Add(t);
            Write(TypesFile, JsonSerializer.Serialize(new CatalogTypes { Types = all.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList() }, Json));
        }
        return null;
    }

    public IReadOnlyDictionary<string, PriceRow> Prices()
    {
        var res = new Dictionary<string, PriceRow>(StringComparer.OrdinalIgnoreCase);
        var csv = PricesCsv().Split('\n').Skip(1);
        foreach (var line in csv)
        {
            var c = Cells(line.TrimEnd('\r'));
            if (line.Trim().Length == 0) continue;
            if (c.Count == 4 && decimal.TryParse(c[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var p)) res[c[0]] = new PriceRow(c[0], p, c[2], c[3]);
            else _log.LogWarning("prices.csv: skipped malformed line {Line}", line);
        }
        return res;
    }

    public string PricesCsv()
    {
        lock (_lock) return File.Exists(PricesFile) ? File.ReadAllText(PricesFile, Encoding.UTF8) : PricesHeader + "\r\n";
    }

    public PriceSave SavePrices(string csv)
    {
        var errors = new List<(int, string)>();
        var names = Types(includeHidden: true).Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lines = csv.Replace("\r\n", "\n").Split('\n');
        if (lines[0].Trim().TrimStart('﻿') != PricesHeader) errors.Add((1, $"첫 줄은 {PricesHeader} 이어야 합니다."));
        var seen = new Dictionary<string, PriceRow>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0) continue;
            var c = Cells(lines[i]);
            if (c.Count != 4) { errors.Add((i + 1, "열이 4개가 아닙니다.")); continue; }
            var ok = true;
            if (c.Count == 4 && c[3].Contains((char)10)) { errors.Add((i + 1, "비고에 줄바꿈을 쓸 수 없습니다")); ok = false; }
            if (!names.Contains(c[0])) { errors.Add((i + 1, $"타입 {c[0]} 없음")); ok = false; }
            if (!decimal.TryParse(c[1].Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var p)) { errors.Add((i + 1, "숫자가 아닙니다")); ok = false; }
            else if (p < 0) { errors.Add((i + 1, "음수 단가")); ok = false; }
            if (c[2].Length > 0 && c[2] != "KRW" && c[2] != "원") { errors.Add((i + 1, "통화는 KRW(원)만 쓸 수 있습니다")); ok = false; }
            if (ok) seen[c[0]] = new PriceRow(c[0], p, "KRW", c[3]);
        }
        if (errors.Count > 0) return new PriceSave(0, errors, null);
        lock (_lock)
        {
            var sb = new StringBuilder(PricesHeader + "\r\n");
            foreach (var r in seen.Values) sb.Append(string.Join(",", Quote(r.Type), r.UnitPrice.ToString(CultureInfo.InvariantCulture), Quote(r.Currency), Quote(r.Note))).Append("\r\n");
            Write(PricesFile, sb.ToString());
            return new PriceSave(seen.Count, errors, PricesFile + ".bak");
        }
    }

    /// <summary>Adds (or removes) one entry of a type's Assets list; false when the type is missing, types.json is unreadable, or nothing changed.</summary>
    public bool AddAsset(string typeName, string asset) => EditAssets(typeName, l => { if (l.Contains(asset, StringComparer.OrdinalIgnoreCase)) return false; l.Add(asset); return true; });
    public bool RemoveAsset(string typeName, string asset) => EditAssets(typeName, l => l.RemoveAll(x => x.Equals(asset, StringComparison.OrdinalIgnoreCase)) > 0);

    bool EditAssets(string typeName, Func<List<string>, bool> edit)
    {
        lock (_lock)
        {
            var all = Load();
            var t = all.FirstOrDefault(x => x.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
            if (LoadError != null || t == null || !edit(t.Assets)) return false;
            Write(TypesFile, JsonSerializer.Serialize(new CatalogTypes { Types = all.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList() }, Json));
            return true;
        }
    }

    static string Quote(string s) => s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    // one line, RFC-4180 quotes; a quoted newline is not supported (shortcut: notes are single-line)
    static List<string> Cells(string line)
    {
        var res = new List<string>();
        var sb = new StringBuilder();
        var q = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (q) { if (ch == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(ch); }
            else if (ch == '"') q = true;
            else if (ch == ',') { res.Add(sb.ToString().Trim()); sb.Clear(); }
            else sb.Append(ch);
        }
        res.Add(sb.ToString().Trim());
        return res;
    }

    // tmp file then replace, keeping the previous one as .bak
    static void Write(string path, string text)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(tmp, path, path + ".bak"); else File.Move(tmp, path);
    }
}
