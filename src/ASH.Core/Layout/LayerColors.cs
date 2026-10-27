using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ASH.Core.Traffic;

namespace ASH.Core.Layout;

/// <summary>One ASH layer: name, what it holds (Korean, for the colour dialog) and its default AutoCAD colour index.</summary>
public sealed record LayerColor(string Layer, string Purpose, short Default);

/// <summary>
/// The ASH layer colour table: defaults, and the per-PC overrides ("AGV_PATH=1;AGV_CP_STANDBY=30", only the colours the user changed) that
/// AGVINIT and every ASH command put on top of them. Layer names are the ones of ASH.Plugin.Conventions, which cannot be referenced from here.
/// </summary>
public static class LayerColors
{
    /// <summary>Table order = creation order = dialog order.</summary>
    public static readonly IReadOnlyList<LayerColor> Table = Build();

    static List<LayerColor> Build()
    {
        var t = new List<LayerColor>
        {
            new LayerColor("AGV_PATH", "경로 (단방향)", 3),
            new LayerColor("AGV_PATH_SPUR", "막다른 경로 (spur)", 2),
            new LayerColor("AGV_PATH_TWO", "양방향 경로", 4),
            new LayerColor("AGV_PATH_CRAB", "게걸음 경로 (crab)", 30),
            new LayerColor("AGV_DIR", "방향 화살표", 8),
            new LayerColor("AGV_CP", "CP (종류를 모를 때)", 1),
            new LayerColor("AGV_BLOCK", "block (교통제어 구간)", 5),
            new LayerColor("AGV_STATION", "스테이션", 6),
            new LayerColor("AGV_CONV", "컨베이어 구간", 94),
            new LayerColor("AGV_3D_CONV", "컨베이어 3D 그림", 96),
            new LayerColor("AGV_3D", "3D 장비 블록", 9),
            new LayerColor("AGV_3D_WALL", "3D 벽", 8),
            new LayerColor("AGV_3D_FENCE", "3D 펜스", 40),
            new LayerColor("AGV_CONV_USE", "컨베이어 소비 지점", 1),
            new LayerColor("AGV_CAUSE", "원인 표시 (서버 분석)", 6),
            new LayerColor("AGV_ROUTE", "루트 보기", 210),
            new LayerColor("AGV_PATH_GAP", "끊긴 경로 끝 표시", 1),
        };
        foreach (var type in CpTypes.All) t.Add(new LayerColor("AGV_CP_" + type.ToUpperInvariant(), type + " CP (" + CpTypes.Korean(type) + ")", CpDefault(type)));
        return t;
    }

    static short CpDefault(string type)
    {
        switch (type)
        {
            case "traffic": return 1;
            case "load": return 3;
            case "unload": return 5;
            case "rtnload": return 30;
            case "rtnunload": return 6;
            case "standby": return 2;
            case "buffer": return 4;
            case "finish": return 8;
            default: return 7;
        }
    }

    public static bool IsAci(int n) => n >= 1 && n <= 255;

    /// <summary>The table row of a layer (case-insensitive), or null.</summary>
    public static LayerColor? Find(string? layer) => Table.FirstOrDefault(c => string.Equals(c.Layer, (layer ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Default colour of an ASH layer; 7 for a name that is not in the table.</summary>
    public static short DefaultOf(string? layer) => Find(layer)?.Default ?? 7;

    /// <summary>"AGV_PATH=1;AGV_CP_STANDBY=30" to layer → colour. Unknown layers, colours outside 1-255 and malformed parts are skipped (a hand-edited file never breaks a command).</summary>
    public static Dictionary<string, short> Parse(string? text)
    {
        var d = new Dictionary<string, short>(StringComparer.Ordinal);
        foreach (var part in (text ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(new[] { '=' }, 2);
            if (kv.Length != 2) continue;
            var row = Find(kv[0]);
            if (row != null && int.TryParse(kv[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && IsAci(n)) d[row.Layer] = (short)n;
        }
        return d;
    }

    /// <summary>Only the colours that differ from the default, in table order; empty text = all defaults. Parse(Format(x)) is x without the default-valued entries.</summary>
    public static string Format(IReadOnlyDictionary<string, short> overrides) =>
        string.Join(";", Table.Where(c => overrides.TryGetValue(c.Layer, out var v) && v != c.Default && IsAci(v))
            .Select(c => c.Layer + "=" + overrides[c.Layer].ToString(CultureInfo.InvariantCulture)));

    /// <summary>Colour in force for a layer: the override, else the default.</summary>
    public static short Effective(IReadOnlyDictionary<string, short> overrides, string? layer) =>
        Find(layer) is { } c ? (overrides.TryGetValue(c.Layer, out var v) && IsAci(v) ? v : c.Default) : (short)7;

    /// <summary>Every ASH layer with its colour in force (defaults with the overrides on top), in table order.</summary>
    public static List<(string Layer, short Color)> Merge(IReadOnlyDictionary<string, short> overrides) =>
        Table.Select(c => (c.Layer, Effective(overrides, c.Layer))).ToList();
}
