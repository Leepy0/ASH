using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ASH.Core.Dxf;

/// <summary>The drawing-to-model frame that travels with drawing.dxf: model = (drawing - Origin) x Scale (drawing unit -> metre) x model units per metre.
/// Only these four values are written; nothing of the PC (AmHome etc.) goes to the server.</summary>
public sealed record ModelFrame(string Units, double OriginX, double OriginY, double Scale)
{
    public const string FileName = "ash.frame.tsv";
    static readonly CultureInfo C = CultureInfo.InvariantCulture;

    public void Write(string path) => File.WriteAllLines(path, new[]
    {
        $"{nameof(Units)}\t{Units}", $"OriginX\t{OriginX.ToString("R", C)}", $"OriginY\t{OriginY.ToString("R", C)}", $"{nameof(Scale)}\t{Scale.ToString("R", C)}",
    });

    /// <summary>Reads the file; FormatException when a key is missing or a number is bad.</summary>
    public static ModelFrame Read(string path)
    {
        var kv = File.ReadAllLines(path).Select(l => l.Split(new[] { '\t' }, 2)).Where(a => a.Length == 2).ToDictionary(a => a[0].Trim(), a => a[1].Trim(), StringComparer.OrdinalIgnoreCase);
        string S(string k) => kv.TryGetValue(k, out var v) ? v : throw new FormatException($"{FileName}: {k} 값이 없습니다.");
        double N(string k) => double.TryParse(S(k), NumberStyles.Float, C, out var d) ? d : throw new FormatException($"{FileName}: {k} 값이 숫자가 아닙니다.");
        return new ModelFrame(S(nameof(Units)), N("OriginX"), N("OriginY"), N(nameof(Scale)));
    }
}
