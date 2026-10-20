using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using ASH.Core.Geometry;

namespace ASH.Core.Asy;

public static class AsyReader
{
    static readonly HashSet<string> Keywords = new HashSet<string>
    {
        "VERSION","SYSTYPE","UNITS","SYSDEF","FLAGS","AGVSDEF","AGVSTOL","GPATHTYPE","GPATH",
        "CPOINTTYPE","CPOINT","NAMELST","PARKLST","WORKLST","SRCHLST","AGVVEHSEG","AGVSVEH",
    };

    static readonly char[] Sep = { ' ', '\t' };

    public static AgvsSystem ReadFile(string path)
    {
        using var r = new StreamReader(path);
        return Read(r);
    }

    public static AgvsSystem Read(TextReader r)
    {
        var s = new AgvsSystem();
        string? kw = null;
        string? line;
        while ((line = r.ReadLine()) != null)
        {
            if (line.Length > 0 && line[0] != ' ' && line[0] != '\t')
            {
                var first = line.Split(Sep, 2)[0];
                if (Keywords.Contains(first)) { kw = first; Start(s, kw, line); continue; }
            }
            if (kw != null) Continue(s, kw, line);
        }
        return s;
    }

    static void Start(AgvsSystem s, string kw, string line)
    {
        var t = line.Split(Sep, StringSplitOptions.RemoveEmptyEntries);
        switch (kw)
        {
            case "VERSION": s.Version = t.Length > 1 ? t[1] : ""; break;
            case "UNITS": s.UnitsTokens = t.Skip(1).ToArray(); break;
            case "SYSDEF": case "FLAGS": case "AGVSDEF": case "AGVSTOL": s.HeaderLines.Add(line); break;
            case "GPATHTYPE": s.PathTypeLines.Add(line); break;
            case "GPATH": s.Paths.Add(ParsePath(t)); break;
            case "CPOINTTYPE": s.CpTypeLines.Add(line); break;
            case "CPOINT": s.ControlPoints.Add(ParseCp(t)); break;
            case "NAMELST":
                var name = Val(t, "name") ?? "";
                if (!s.NameLists.TryGetValue(name, out var l)) s.NameLists[name] = l = new List<string>();
                for (int i = 0; i + 1 < t.Length; i++) if (t[i] == "item") l.Add(t[i + 1]);
                break;
            case "PARKLST": s.ParkListLines.Add(line); break;
            case "WORKLST": s.WorkListLines.Add(line); break;
            case "SRCHLST": s.SearchListLines.Add(line); break;
            case "AGVVEHSEG": s.VehicleSegmentLines.Add(line); break;
            case "AGVSVEH": s.VehicleLines.Add(line); break;
        }
    }

    static void Continue(AgvsSystem s, string kw, string line)
    {
        switch (kw)
        {
            case "SYSDEF": case "FLAGS": case "AGVSDEF": case "AGVSTOL": s.HeaderLines.Add(line); break;
            case "AGVVEHSEG": s.VehicleSegmentLines.Add(line); break;
            case "AGVSVEH": s.VehicleLines.Add(line); break;
            case "GPATHTYPE": s.PathTypeLines.Add(line); break;
            case "CPOINTTYPE": s.CpTypeLines.Add(line); break;
            case "PARKLST": s.ParkListLines.Add(line); break;
            case "WORKLST": s.WorkListLines.Add(line); break;
            case "SRCHLST": s.SearchListLines.Add(line); break;
        }
    }

    static string? Val(string[] t, string key)
    {
        for (int i = 0; i + 1 < t.Length; i++) if (t[i] == key) return t[i + 1];
        return null;
    }

    static double Num(string[] t, string key) =>
        double.Parse(Val(t, key) ?? throw new FormatException($"missing '{key}'"), NumberStyles.Float, CultureInfo.InvariantCulture);

    static GuidePath ParsePath(string[] t)
    {
        int piece = Array.IndexOf(t, "piece");
        if (piece < 0) throw new FormatException("GPATH without 'piece'");
        var head = t.Take(piece).ToArray();
        var body = t.Skip(piece + 1).ToArray();
        PathPiece p = Val(body, "cenx") != null
            ? new ArcPiece(new Point2(Num(body, "cenx"), Num(body, "ceny")),
                           new Point2(Num(body, "begx"), Num(body, "begy")),
                           (int)System.Math.Round(Num(body, "angle")))
            : new LinePiece(new Point2(Num(body, "begx"), Num(body, "begy")),
                            new Point2(Num(body, "endx"), Num(body, "endy")));
        return new GuidePath(Val(head, "name") ?? "", Val(head, "type") ?? "",
            head.Contains("spur"), head.Contains("crab"), p);
    }

    static ControlPoint ParseCp(string[] t)
    {
        int at = Array.IndexOf(t, "at");
        if (at < 0 || at + 2 >= t.Length) throw new FormatException("CPOINT without 'at <path> <dist>'");
        return new ControlPoint(Val(t, "name") ?? "", Val(t, "type") ?? "", t[at + 1],
            double.Parse(t[at + 2], NumberStyles.Float, CultureInfo.InvariantCulture), Val(t, "align"));
    }
}
