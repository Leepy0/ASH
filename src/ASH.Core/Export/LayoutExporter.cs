using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ASH.Core.Asy;
using ASH.Core.Geometry;
using ASH.Core.Layout;

namespace ASH.Core.Export;

public static class LayoutExporter
{
    static string[] Template(string name)
    {
        using var s = typeof(LayoutExporter).Assembly.GetManifestResourceStream("ASH.Core.Export.Templates." + name)
            ?? throw new InvalidOperationException("missing template " + name);
        var text = new StreamReader(s).ReadToEnd().Replace("\r\n", "\n");
        if (text.EndsWith("\n")) text = text.Substring(0, text.Length - 1);
        return text.Split('\n');
    }

    static string N(double d) => d.ToString("R", CultureInfo.InvariantCulture);

    static Point2 Tx(Point2 p, ExportOptions o) => new Point2((p.X - o.Origin.X) * o.Scale, (p.Y - o.Origin.Y) * o.Scale);

    public static AgvsSystem ToAgvs(Layout.Layout l, ExportOptions o)
    {
        var s = new AgvsSystem { Version = o.Version, UnitsTokens = new[] { o.Units, "Seconds" } };
        var groups = o.Groups ?? new VehicleGroupSpec[0];

        s.HeaderLines.AddRange(Template("agvs_header.txt").Select(x => x.Replace("Feet", o.Units)));
        double maxSpeed = groups.Count > 0 ? groups.Max(g => g.Speed) : 1;
        s.PathTypeLines.Add($"GPATHTYPE name {o.PathType} one normal attach rigid color -1 nav 1 vel 1 {N(maxSpeed)} {o.Units} Seconds ");
        s.CpTypeLines.AddRange(Template("agvs_cptype.txt").Select(x => x.Replace("DefaultControlPoint", o.CpType).Replace("Feet", o.Units)));

        foreach (var p in l.Paths)
            for (int i = 0; i < p.Segments.Count; i++)
                s.Paths.Add(ToGuidePath(p, p.Segments[i], i + 1, o));

        foreach (var c in l.Cps)
        {
            var pr = LayoutChecker.Project(l, c.Position);
            if (pr == null) continue;
            var path = pr.Value.Path;
            double acc = 0, dist = pr.Value.Dist;
            int idx = 0;
            for (; idx < path.Segments.Count - 1 && dist > acc + path.Segments[idx].Length; idx++) acc += path.Segments[idx].Length;
            var piece = path.Segments.Count == 1 ? path.Name : path.Name + "_s" + (idx + 1);
            s.ControlPoints.Add(new ControlPoint(c.Name, o.CpType, piece, (dist - acc) * o.Scale, null));
            if ((c.Type == "standby" || c.Type == "buffer") && c.Group.Length > 0)
            {
                var key = "nl_" + c.Group;
                if (!s.NameLists.TryGetValue(key, out var list)) s.NameLists[key] = list = new List<string>();
                list.Add(c.Name);
            }
        }

        if (groups.Count == 0)
        {
            s.VehicleSegmentLines.AddRange(Template("agvs_vehseg.txt").Select(x => x.Replace("Feet", o.Units)));
            s.VehicleLines.AddRange(Template("agvs_veh.txt").Select(x => x.Replace("Feet", o.Units).Replace("numveh 10", "numveh 1")));
        }
        foreach (var g in groups)
        {
            var seg = "Seg_" + g.TypeName;
            s.VehicleSegmentLines.AddRange(Template("agvs_vehseg.txt").Select(x => x.Replace("DefSegment", seg).Replace("Feet", o.Units)));
            var speed = N(g.Speed);
            foreach (var line in Template("agvs_veh.txt"))
            {
                var t = line.Replace("Feet", o.Units).Replace("DefVehicle", g.TypeName).Replace("numveh 10", "numveh " + g.MaxCount)
                    .Replace("start nlwork", "start nl_" + g.Group).Replace("vehsegs item DefSegment", "vehsegs item " + seg);
                s.VehicleLines.Add(Regex.Replace(t, @"^(\s*(?:vel|crvvel|sprvel|rvel|rcrvvel|rsprvel|crabvel)\s+0 )3( )", "${1}" + speed + "${2}"));
            }
        }
        return s;
    }

    public static GuidePath ToGuidePath(LayoutPath p, Segment s, int index, ExportOptions o)
    {
        var name = p.Segments.Count == 1 ? p.Name : p.Name + "_s" + index;
        PathPiece piece = s switch
        {
            LineSeg ln => new LinePiece(Tx(ln.Start, o), Tx(ln.End, o)),
            ArcSeg a => new ArcPiece(Tx(a.Center, o), Tx(a.Start, o), (int)Math.Round(a.SweepDeg * 10)),
            _ => throw new NotSupportedException(s.GetType().Name),
        };
        return new GuidePath(name, o.PathType, p.IsSpur, p.IsSpur, piece);
    }
}
