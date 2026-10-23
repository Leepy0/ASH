using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ASH.Core.Export;

namespace ASH.Core.Cel;

/// <summary>Boxes (metres, picture frame) to AutoMod cell text. The box lines are VehicleGeometry's (310 flag 5, own colour, size line y first).</summary>
public static class CelWriter
{
    const double Ft = VehicleGeometry.FtPerM;

    /// <summary>A stand-alone cell file (10000 / 7000 / 700 group / 310 boxes), feet, LF line ends; the AutoMod editor opens it.</summary>
    public static string Cel(string cellName, IReadOnlyList<Box> boxes)
    {
        if (boxes == null) throw new ArgumentNullException(nameof(boxes));
        var l = new List<string>();
        foreach (var type in new[] { "10000", "7000" })          // cube.cel: both groups carry a name and an identity transform, one child each
        {
            l.Add(type + " 21");
            l.Add("9 9 0 1 0 " + cellName);
            l.Add("0  0  0");
            l.Add("0\t0.00\t0.00\t0.00");
            l.Add("\t1.00\t1.00\t1.00");
            l.Add("1");
        }
        l.Add("700 17");
        l.Add("9 9 0 1 0 " + cellName);
        l.Add(boxes.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var b in boxes) l.AddRange(Prim(b, 0));           // the stand-alone cell's floor is z = 0
        return string.Join("\n", l) + "\n";
    }

    /// <summary>The AGVVEHSEG text: the template (agvs_vehseg.txt) with Geom1's children replaced by the boxes (feet), the lift joint on the top of the
    /// highest box and the CONTAINER there (loadTopM = max(Z0 + H)); same layout as VehicleGeometry.Build. A Magnet tows: its CONTAINER is on the floor
    /// behind the rear (+y) edge of the boxes, as in VehicleGeometry.Build.</summary>
    public static string VehSeg(IReadOnlyList<string> template, IReadOnlyList<Box> boxes, string kind = "")
    {
        if (boxes == null || boxes.Count == 0) throw new ArgumentException("no boxes", nameof(boxes));
        double top = boxes.Max(b => b.Z0 + b.H);
        var parts = boxes.Select(b => Prim(b, VehicleGeometry.FloorRel)).ToList();
        bool magnet = string.Equals(kind, "Magnet", StringComparison.OrdinalIgnoreCase);
        double rear = boxes.Max(b => b.Y + b.Dy / 2);
        return string.Join("\n", VehicleGeometry.Assemble(template, parts, top, magnet ? -(rear + 0.10 + 0.5) : 0, magnet ? 0 : top));
    }

    static IEnumerable<string> Prim(Box b, double floorFt) =>
        VehicleGeometry.Prim(b.Name, b.Color, b.X * Ft, b.Y * Ft, floorFt + b.Z0 * Ft, b.Dx * Ft, b.Dy * Ft, b.Dx * Ft, b.Dy * Ft, b.H * Ft);
}

public static class CelReader
{
    /// <summary>The 310 boxes of a cell written by CelWriter (feet → metres); other types are skipped. FormatException on a short block, a non-finite number or a size ≤ 0.</summary>
    public static IReadOnlyList<Box> ReadBoxes(string celText)
    {
        if (celText == null) throw new ArgumentNullException(nameof(celText));
        var l = celText.Replace("\r\n", "\n").Split('\n');
        double[] Nums(string s) => s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v / VehicleGeometry.FtPerM
                : throw new FormatException("bad number: " + x)).ToArray(); // TryParse: net48 throws OverflowException on 1e999, net10 gives Infinity
        var res = new List<Box>();
        for (int i = 0; i < l.Length; i++)
        {
            if (l[i].Trim() != "310 5") continue;                  // CelWriter writes only flag 5 boxes
            if (i + 5 >= l.Length) throw new FormatException("short 310 block at line " + (i + 1));
            var name = l[i + 1].Split(new[] { ' ' }, 6);
            var pos = Nums(l[i + 2]);
            var size = Nums(l[i + 5]);
            if (name.Length < 6 || pos.Length < 3 || size.Length < 5) throw new FormatException("short 310 block at line " + (i + 1));
            if (pos.Take(3).Concat(size.Take(5)).Any(v => double.IsNaN(v) || double.IsInfinity(v)) || !(size[0] > 0 && size[1] > 0 && size[4] > 0))
                throw new FormatException("bad number in 310 block at line " + (i + 1));
            res.Add(new Box(name[5], pos[0], pos[1], pos[2], size[1], size[0], size[4], int.Parse(name[0], CultureInfo.InvariantCulture)));
            i += 5;
        }
        return res;
    }
}
