using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ASH.Core.Asy;

/// <summary>Exact inverse of <see cref="AsyReader"/>. Lines end with '\n'.</summary>
public static class AsyWriter
{
    static string N(double d) => d.ToString("R", CultureInfo.InvariantCulture);

    public static void WriteFile(AgvsSystem s, string path)
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(false)) { NewLine = "\n" };
        Write(s, w);
    }

    public static void Write(AgvsSystem s, TextWriter w)
    {
        void L(string line) { w.Write(line); w.Write('\n'); }
        void All(IEnumerable<string> lines) { foreach (var l in lines) L(l); }

        L("VERSION " + s.Version);
        L("SYSTYPE AGVS");
        L("UNITS " + string.Join(" ", s.UnitsTokens));
        All(s.HeaderLines);
        All(s.PathTypeLines);
        foreach (var p in s.Paths) L(FormatPath(p));
        All(s.CpTypeLines);
        foreach (var c in s.ControlPoints) L(FormatCp(c));
        foreach (var kv in s.NameLists)
        {
            L("NAMELST name " + kv.Key);
            var sb = new StringBuilder("NAMELST name " + kv.Key + " ");
            foreach (var i in kv.Value) sb.Append("item ").Append(i).Append(' ');
            L(sb.ToString());
        }
        All(s.ParkListLines);
        All(s.WorkListLines);
        All(s.VehicleSegmentLines);
        All(s.VehicleLines);
    }

    public static string FormatPath(GuidePath p)
    {
        var sb = new StringBuilder("GPATH name ").Append(p.Name).Append(" type ").Append(p.Type);
        if (p.IsSpur) sb.Append(" spur");
        if (p.IsCrab) sb.Append(" crab");
        sb.Append(" piece ");
        switch (p.Piece)
        {
            case LinePiece l:
                sb.Append("begx ").Append(N(l.Beg.X)).Append(" begy ").Append(N(l.Beg.Y))
                  .Append(" endx ").Append(N(l.End.X)).Append(" endy ").Append(N(l.End.Y)).Append(" upz 1");
                break;
            case ArcPiece a:
                sb.Append("cenx ").Append(N(a.Center.X)).Append(" ceny ").Append(N(a.Center.Y))
                  .Append(" begx ").Append(N(a.Beg.X)).Append(" begy ").Append(N(a.Beg.Y))
                  .Append(" upz 1 angle ").Append(a.AngleTenths.ToString(CultureInfo.InvariantCulture));
                break;
            default: throw new NotSupportedException(p.Piece.GetType().Name);
        }
        return sb.ToString();
    }

    public static string FormatCp(ControlPoint c)
    {
        var sb = new StringBuilder("CPOINT name ").Append(c.Name).Append(" type ").Append(c.Type);
        if (c.Align != null) sb.Append(" align ").Append(c.Align);
        return sb.Append(" at ").Append(c.PathName).Append(' ').Append(N(c.Dist)).ToString();
    }
}
