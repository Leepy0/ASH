using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ASH.Core.Export;
using ASH.Core.Geometry;

namespace ASH.Core.Settings;

/// <summary>Reads/writes ProjectSettings as "key\tvalue" lines (InvariantCulture). Unknown keys are ignored.</summary>
public static class SettingsFile
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    public static ProjectSettings Load(string path)
    {
        var s = new ProjectSettings();
        if (!File.Exists(path)) return s;
        var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(path))
        {
            int tab = line.IndexOf('\t');
            if (tab > 0) kv[line.Substring(0, tab).Trim()] = line.Substring(tab + 1).Trim();
        }
        string? Str(string k) => kv.TryGetValue(k, out var v) ? v : null;
        double Num(string k, double def) => Str(k) is string v ? Parse(k, v) : def;
        double? Opt(string k, double? def) => Str(k) is string v ? (v.Length == 0 ? null : Parse(k, v)) : def;
        return s with
        {
            SafetyGap = Num(nameof(s.SafetyGap), s.SafetyGap),
            CpSpacing = Num(nameof(s.CpSpacing), s.CpSpacing),
            ClaimDist = Opt(nameof(s.ClaimDist), s.ClaimDist),
            BlockReach = Opt(nameof(s.BlockReach), s.BlockReach),
            VehicleLength = Num(nameof(s.VehicleLength), s.VehicleLength),
            VehicleWidth = Num(nameof(s.VehicleWidth), s.VehicleWidth),
            Units = Str(nameof(s.Units)) ?? s.Units,
            Scale = Num(nameof(s.Scale), s.Scale),
            Origin = new Point2(Num("OriginX", s.Origin.X), Num("OriginY", s.Origin.Y)),
            ModelName = Str(nameof(s.ModelName)) ?? s.ModelName,
            Groups = Str(nameof(s.Groups)) ?? s.Groups,
        };
    }

    public static void Save(ProjectSettings s, string path)
    {
        string N(double v) => v.ToString("R", C);
        File.WriteAllLines(path, new[]
        {
            $"{nameof(s.SafetyGap)}\t{N(s.SafetyGap)}",
            $"{nameof(s.CpSpacing)}\t{N(s.CpSpacing)}",
            $"{nameof(s.ClaimDist)}\t{(s.ClaimDist is double cd ? N(cd) : "")}",
            $"{nameof(s.BlockReach)}\t{(s.BlockReach is double br ? N(br) : "")}",
            $"{nameof(s.VehicleLength)}\t{N(s.VehicleLength)}",
            $"{nameof(s.VehicleWidth)}\t{N(s.VehicleWidth)}",
            $"{nameof(s.Units)}\t{s.Units}",
            $"{nameof(s.Scale)}\t{N(s.Scale)}",
            $"OriginX\t{N(s.Origin.X)}",
            $"OriginY\t{N(s.Origin.Y)}",
            $"{nameof(s.ModelName)}\t{s.ModelName}",
            $"{nameof(s.Groups)}\t{s.Groups}",
        });
    }

    /// <summary>"G1:Tug:5:1.2:2.0:1.0;G2:..." -> one spec per ';' item (Group:TypeName:MaxCount:Speed:Length:Width).</summary>
    public static List<VehicleGroupSpec> ParseGroups(string groups)
    {
        var res = new List<VehicleGroupSpec>();
        foreach (var item in groups.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0))
        {
            var f = item.Split(':');
            if (f.Length != 6) throw new FormatException($"Groups 항목 '{item}'은 Group:TypeName:MaxCount:Speed:Length:Width 6칸이어야 합니다.");
            res.Add(new VehicleGroupSpec(f[0].Trim(), f[1].Trim(), (int)Parse("MaxCount", f[2]), Parse("Speed", f[3]), Parse("Length", f[4]), Parse("Width", f[5])));
        }
        return res;
    }

    private static double Parse(string key, string v) =>
        double.TryParse(v.Trim(), NumberStyles.Float, C, out var d) ? d : throw new FormatException($"설정 {key} 값 '{v}'이 숫자가 아닙니다.");
}
