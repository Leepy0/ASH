using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ASH.Core.Lite;

/// <summary>One row of the cleanup levels, in mm. DenseMmPerM2 = 0: blob outlines off, MaxBlobMm means nothing then.</summary>
public sealed record LiteLevel(double ToleranceMm, double MinSizeMm, double DenseMmPerM2, double MaxBlobMm);

/// <summary>The cleanup levels of AGVLITE (plan §3): the program raises the level until the drawing fits, nobody picks numbers.</summary>
public static class LiteLevels
{
    public const int Max = 4;
    /// <summary>The size AGVLITE aims for, and the size past which the report warns.</summary>
    public const long TargetBytes = 5L * 1048576, WarnBytes = 10L * 1048576;

    public static readonly IReadOnlyList<LiteLevel> Table = new[]
    {
        new LiteLevel(5, 0, 0, 0),
        new LiteLevel(10, 30, 0, 0),
        new LiteLevel(10, 50, 20_000, 6_000),   // 가늠값: docs/lite-baseline.md 측정 뒤 조정
        new LiteLevel(20, 100, 10_000, 30_000), // 가늠값: docs/lite-baseline.md 측정 뒤 조정
        new LiteLevel(30, 200, 5_000, 100_000), // 가늠값: docs/lite-baseline.md 측정 뒤 조정
    };

    /// <summary>The thresholds of a level for a drawing's unit; noBlobs turns the outlines off whatever the level.</summary>
    public static LiteThresholds Get(int level, int insUnits, bool noBlobs = false)
    {
        if (level < 0 || level > Max) throw new ArgumentOutOfRangeException(nameof(level));
        var l = Table[level];
        return new LiteThresholds(l.ToleranceMm, l.MinSizeMm, insUnits, noBlobs ? 0 : l.DenseMmPerM2, l.MaxBlobMm);
    }
}

/// <summary>lite\&lt;name&gt;_lite.settings.tsv: what a person fixed with AGVLITEX, plus the level the last run used, for the record. "key\tvalue" lines, UTF-8 with BOM; lines this version does not
/// know are kept on rewrite; a missing file or a bad value means auto.</summary>
public sealed class LiteSettings
{
    const string UsedPrefix = "Used";
    static readonly CultureInfo C = CultureInfo.InvariantCulture;

    /// <summary>Null = auto, else 0..LiteLevels.Max.</summary>
    public int? Level { get; set; }
    /// <summary>Original layer names, left out before cleaning (R8).</summary>
    public List<string> ExcludeLayers { get; set; } = new();
    public bool NoBlobs { get; set; }
    readonly List<string> _other = new();

    public static LiteSettings Load(string path) => File.Exists(path) ? Parse(File.ReadAllLines(path, Encoding.UTF8)) : new LiteSettings();

    public void Save(string path) => File.WriteAllLines(path, Lines(), new UTF8Encoding(true));

    public static LiteSettings Parse(IEnumerable<string> lines)
    {
        var s = new LiteSettings();
        foreach (var line in lines)
        {
            var cells = line.Split('\t');
            string key = cells[0].Trim(), value = cells.Length > 1 ? cells[1].Trim() : "";
            if (key.Equals(nameof(Level), StringComparison.OrdinalIgnoreCase))
                s.Level = int.TryParse(value, NumberStyles.None, C, out int l) && l <= LiteLevels.Max ? l : null;
            else if (key.Equals(nameof(ExcludeLayers), StringComparison.OrdinalIgnoreCase))
                s.ExcludeLayers = cells.Skip(1).Where(c => c.Length > 0).ToList(); // a layer per cell, exact: a name may hold spaces
            else if (key.Equals(nameof(NoBlobs), StringComparison.OrdinalIgnoreCase))
                s.NoBlobs = value.Equals("1", StringComparison.Ordinal);
            else if (line.Length > 0)
                s._other.Add(line);
        }
        return s;
    }

    /// <summary>The file's lines; Parse(Lines()) keeps everything.</summary>
    public string[] Lines() => new[]
    {
        nameof(Level) + "\t" + (Level is int l ? l.ToString(C) : "auto"),
        nameof(ExcludeLayers) + "\t" + string.Join("\t", ExcludeLayers),
        nameof(NoBlobs) + "\t" + (NoBlobs ? "1" : "0"),
    }.Concat(_other).ToArray();

    /// <summary>Leaves the level and thresholds written in the file (plan §1 item 5). They are a record, not a choice: Level stays as it was.</summary>
    public void Record(int level, LiteThresholds t)
    {
        _other.RemoveAll(l => l.StartsWith(UsedPrefix, StringComparison.Ordinal));
        string N(double v) => v.ToString("R", C);
        _other.Add($"{UsedPrefix}Level\t{level.ToString(C)}");
        _other.Add($"{UsedPrefix}ToleranceMm\t{N(t.ToleranceMm)}");
        _other.Add($"{UsedPrefix}MinSizeMm\t{N(t.MinSizeMm)}");
        _other.Add($"{UsedPrefix}DenseMmPerM2\t{N(t.DenseMmPerM2)}");
        _other.Add($"{UsedPrefix}MaxBlobMm\t{N(t.MaxBlobMm)}");
    }
}
