using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ASH.Core.Cel;

public readonly struct Vec3
{
    public readonly double X, Y, Z;
    public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
}

public sealed record Triangle(Vec3 A, Vec3 B, Vec3 C);

/// <summary>A part: one `solid` of an ASCII STL, one `o`/`g` of an OBJ, or the whole file.</summary>
public sealed record MeshPart(string Name, IReadOnlyList<Triangle> Triangles);

public static class MeshReader
{
    /// <summary>By extension (.stl/.obj, case-insensitive); FormatException with the line number for a bad file, NotSupportedException for another extension.</summary>
    public static IReadOnlyList<MeshPart> Read(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".stl") return ReadStl(File.ReadAllBytes(path));
        if (ext == ".obj")
        {
            using var r = new StreamReader(path);
            return ReadObj(r);
        }
        throw new NotSupportedException("지원하지 않는 형식입니다 (STL/OBJ만): " + path);
    }

    /// <summary>Binary when bytes.Length == 84 + 50 * n (n from offset 80), else ASCII.</summary>
    public static IReadOnlyList<MeshPart> ReadStl(byte[] bytes)
    {
        if (bytes.Length >= 84 && bytes.Length == 84L + 50L * BitConverter.ToUInt32(bytes, 80))
            return ReadBinaryStl(bytes);
        return ReadAsciiStl(Encoding.ASCII.GetString(bytes));
    }

    static IReadOnlyList<MeshPart> ReadBinaryStl(byte[] b)
    {
        int n = (int)BitConverter.ToUInt32(b, 80);
        var tris = new List<Triangle>(n);
        for (int i = 0; i < n; i++)
        {
            int o = 84 + 50 * i + 12; // skip normal
            tris.Add(new Triangle(F(b, o), F(b, o + 12), F(b, o + 24)));
        }
        return new[] { new MeshPart("part1", tris) };
    }

    static Vec3 F(byte[] b, int o) => new Vec3(BitConverter.ToSingle(b, o), BitConverter.ToSingle(b, o + 4), BitConverter.ToSingle(b, o + 8));

    static IReadOnlyList<MeshPart> ReadAsciiStl(string text)
    {
        var parts = new List<MeshPart>();
        List<Triangle>? tris = null;
        string name = "";
        var vs = new List<Vec3>(3);
        int line = 0;

        void Close()
        {
            if (tris != null) parts.Add(new MeshPart(name.Length > 0 ? name : "part" + (parts.Count + 1), tris));
            tris = null;
        }

        foreach (var raw in text.Split('\n'))
        {
            line++;
            var s = raw.Trim();
            if (s.StartsWith("solid", StringComparison.OrdinalIgnoreCase))
            {
                Close();
                name = s.Substring(5).Trim();
                tris = new List<Triangle>();
                vs.Clear();
            }
            else if (s.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
            {
                var t = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (t.Length != 4) throw new FormatException($"STL {line}행: vertex 좌표가 3개가 아닙니다.");
                tris ??= new List<Triangle>();
                vs.Add(new Vec3(D(t[1], line), D(t[2], line), D(t[3], line)));
                if (vs.Count == 3) { tris.Add(new Triangle(vs[0], vs[1], vs[2])); vs.Clear(); }
            }
        }
        Close();
        if (parts.Count == 0) throw new FormatException("STL: 삼각형이 없습니다 (ASCII 'solid' 또는 길이가 맞는 binary 아님).");
        return parts;
    }

    static double D(string s, int line)
    {
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            throw new FormatException($"{line}행: 숫자가 아닙니다 '{s}'.");
        return d;
    }

    /// <summary>v, f (3+ vertices → fan), o/g start a part; vt/vn/mtl ignored; negative indices allowed.</summary>
    public static IReadOnlyList<MeshPart> ReadObj(TextReader reader)
    {
        var verts = new List<Vec3>();
        var parts = new List<MeshPart>();
        List<Triangle>? tris = null;
        string name = "";
        int line = 0;
        string? raw;

        void Close()
        {
            if (tris != null && tris.Count > 0) parts.Add(new MeshPart(name.Length > 0 ? name : "part" + (parts.Count + 1), tris));
            tris = null;
        }

        while ((raw = reader.ReadLine()) != null)
        {
            line++;
            var s = raw.Trim();
            if (s.Length == 0 || s[0] == '#') continue;
            var t = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (t[0])
            {
                case "v":
                    if (t.Length < 4) throw new FormatException($"OBJ {line}행: v 좌표가 3개 미만입니다.");
                    verts.Add(new Vec3(D(t[1], line), D(t[2], line), D(t[3], line)));
                    break;
                case "o":
                case "g":
                    Close();
                    name = t.Length > 1 ? string.Join(" ", t, 1, t.Length - 1) : "";
                    tris = new List<Triangle>();
                    break;
                case "f":
                    if (t.Length < 4) throw new FormatException($"OBJ {line}행: f 꼭짓점이 3개 미만입니다.");
                    var idx = new Vec3[t.Length - 1];
                    for (int i = 1; i < t.Length; i++)
                    {
                        var head = t[i].Split('/')[0];
                        if (!int.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out var k))
                            throw new FormatException($"OBJ {line}행: 면 인덱스가 숫자가 아닙니다 '{t[i]}'.");
                        int j = k > 0 ? k - 1 : verts.Count + k;
                        if (k == 0 || j < 0 || j >= verts.Count)
                            throw new FormatException($"OBJ {line}행: 면 인덱스 {k}이(가) 범위 밖입니다 (정점 {verts.Count}개).");
                        idx[i - 1] = verts[j];
                    }
                    tris ??= new List<Triangle>();
                    for (int i = 1; i + 1 < idx.Length; i++) tris.Add(new Triangle(idx[0], idx[i], idx[i + 1]));
                    break;
            }
        }
        Close();
        return parts;
    }
}
