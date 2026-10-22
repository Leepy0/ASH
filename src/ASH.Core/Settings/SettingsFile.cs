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

    public static ProjectSettings Load(string path) => File.Exists(path) ? Parse(File.ReadAllLines(path)) : new ProjectSettings();

    /// <summary>"key\tvalue" lines as settings; FormatException names the bad key. The settings dialog checks its fields through this too.</summary>
    public static ProjectSettings Parse(IEnumerable<string> lines)
    {
        var s = new ProjectSettings();
        var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            int tab = line.IndexOf('\t');
            if (tab > 0) kv[line.Substring(0, tab).Trim()] = line.Substring(tab + 1).Trim();
        }
        string? Str(string k) => kv.TryGetValue(k, out var v) ? v : null;
        double Num(string k, double def) => Str(k) is string v ? Parse(k, v) : def;
        double? Opt(string k, double? def) => Str(k) is string v ? (v.Length == 0 ? null : Parse(k, v)) : def;
        var r = s with
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
            ModelName = string.IsNullOrEmpty(Str(nameof(s.ModelName))) ? s.ModelName : Str(nameof(s.ModelName))!,
            Groups = Str(nameof(s.Groups)) ?? s.Groups,
            AmHome = string.IsNullOrEmpty(Str(nameof(s.AmHome))) ? s.AmHome : Str(nameof(s.AmHome))!,
            ArrowSize = Num(nameof(s.ArrowSize), s.ArrowSize),
        };
        foreach (var (k, v) in new[] { (nameof(r.SafetyGap), r.SafetyGap), (nameof(r.VehicleLength), r.VehicleLength), (nameof(r.VehicleWidth), r.VehicleWidth), (nameof(r.Scale), r.Scale), (nameof(r.ArrowSize), r.ArrowSize) })
            if (!(v > 0)) throw new FormatException($"설정 {k} 값 {v.ToString(C)}은 0보다 커야 합니다.");
        return r;
    }

    public static void Save(ProjectSettings s, string path) => File.WriteAllLines(path, Lines(s));

    /// <summary>The file's lines in file order; Parse(Lines(s)) == s.</summary>
    public static string[] Lines(ProjectSettings s)
    {
        string N(double v) => v.ToString("R", C);
        return new[]
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
            $"{nameof(s.AmHome)}\t{s.AmHome}",
            $"{nameof(s.ArrowSize)}\t{N(s.ArrowSize)}",
        };
    }

    /// <summary>"G1:Tug:5:1.2:2.0:1.0;G2:..." -> one spec per ';' item (Group:TypeName:MaxCount:Speed:Length:Width).</summary>
    public static List<VehicleGroupSpec> ParseGroups(string groups)
    {
        var res = new List<VehicleGroupSpec>();
        foreach (var item in groups.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0))
        {
            var f = item.Split(':');
            if (f.Length != 6) throw new FormatException($"Groups 항목 '{item}'은 Group:TypeName:MaxCount:Speed:Length:Width 6칸이어야 합니다.");
            if (!int.TryParse(f[2].Trim(), NumberStyles.Integer, C, out var max) || max < 1)
                throw new FormatException($"Groups 항목 '{item}': MaxCount는 1 이상의 정수여야 합니다.");
            double Pos(string key, string v)
            {
                var d = double.TryParse(v.Trim(), NumberStyles.Float, C, out var x) ? x : double.NaN;
                if (!(d > 0)) throw new FormatException($"Groups 항목 '{item}': {key}는 0보다 큰 숫자여야 합니다.");
                return d;
            }
            res.Add(new VehicleGroupSpec(f[0].Trim(), f[1].Trim(), max, Pos("Speed", f[3]), Pos("Length", f[4]), Pos("Width", f[5])));
        }
        return res;
    }

    private static double Parse(string key, string v) =>
        double.TryParse(v.Trim(), NumberStyles.Float, C, out var d) ? d : throw new FormatException($"설정 {key} 값 '{v}'이 숫자가 아닙니다.");
}
