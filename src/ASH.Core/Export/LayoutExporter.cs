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

    static string N(double d) => AsyWriter.Num(d);

    static Point2 Tx(Point2 p, ExportOptions o) => new Point2((p.X - o.Origin.X) * o.Scale, (p.Y - o.Origin.Y) * o.Scale);

    static void CheckName(string kind, string name)
    {
        if (!LayoutChecker.IsValidName(name)) throw new ArgumentException($"{kind} name '{name}' is empty, contains whitespace or is a reserved .asy word");
    }

    /// <summary>Template lines keep their own unit tokens (Feet); only UNITS, GPATHTYPE vel, CPOINTTYPE distance and the substituted speeds use o.Units.</summary>
    public static AgvsSystem ToAgvs(Layout.Layout l, ExportOptions o, ICollection<string>? warnings = null)
    {
        if (o.Groups == null || o.Groups.Count == 0) throw new InvalidOperationException("ExportOptions.Groups is required");
        var groups = o.Groups;
        warnings ??= new List<string>();
        foreach (var p in l.Paths) CheckName("path", p.Name);
        foreach (var c in l.Cps) CheckName("CP", c.Name);
        foreach (var b in l.Blocks) CheckName("block", b.Name);
        foreach (var g in groups) { CheckName("group", g.Group); CheckName("vehicle type", g.TypeName); if (!(g.RotateDegPerSec > 0)) throw new ArgumentException($"group {g.Group}: RotateDegPerSec must be > 0"); }

        var s = new AgvsSystem { Version = o.Version, UnitsTokens = new[] { o.Units, "Seconds" } };
        s.HeaderLines.AddRange(Template("agvs_header.txt").Select(x => o.ClaimBlockOnNextCp ? x.Replace("ignore ResumeSpeedWhenClaimed", "ignore OldBlockClaim ResumeSpeedWhenClaimed") : x));
        s.PathTypeLines.Add($"GPATHTYPE name {o.PathType} one normal attach rigid color -1 nav 1 vel 1 {N(groups.Max(g => g.Speed))} {o.Units} Seconds ");

        // GPATH names: single-segment paths keep their name, others get _s<k> skipping any name already emitted.
        var taken = new HashSet<string>(l.Paths.Select(p => p.Name));
        var segName = new Dictionary<(LayoutPath, int), string>();
        foreach (var p in l.Paths)
        {
            int k = 1;
            for (int i = 0; i < p.Segments.Count; i++)
            {
                var sg = p.Segments[i];
                if (sg.Length < l.Tolerance) { warnings.Add($"{p.Name}: 길이 0 세그먼트 #{i + 1}을 건너뜀"); continue; }
                string name = p.Name;
                if (p.Segments.Count > 1)
                {
                    while (!taken.Add(p.Name + "_s" + k)) k++;
                    name = p.Name + "_s" + k++;
                }
                segName[(p, i)] = name;
                s.Paths.Add(ToGuidePath(name, p, sg, o, l.Tolerance, warnings));
            }
        }

        var cpType = new SortedDictionary<int, string>();
        string CpTypeOf(int cap)
        {
            if (cap <= 0) cap = 1; // shortcut: Cap unset (0) exports as cap 1, change if a layout needs AM's cap 0
            if (!cpType.TryGetValue(cap, out var t)) cpType[cap] = t = cap == 1 ? o.CpType : o.CpType + "_cap" + cap;
            return t;
        }
        CpTypeOf(1); // header AGVSDEF references the base type
        foreach (var c in l.Cps)
        {
            var pr = LayoutChecker.Project(l, c.Position);
            if (pr == null) continue;
            var path = pr.Value.Path;
            double acc = 0, dist = pr.Value.Dist;
            int idx = 0;
            for (; idx < path.Segments.Count - 1 && dist > acc + path.Segments[idx].Length; idx++) acc += path.Segments[idx].Length;
            if (!segName.ContainsKey((path, idx)))
            {
                // CP fell on a skipped zero-length segment: move it to the end of the previous kept one, else the start of the next.
                int j = idx;
                while (j >= 0 && !segName.ContainsKey((path, j))) j--;
                if (j >= 0) { idx = j; acc = path.Segments.Take(j).Sum(x => x.Length); dist = acc + path.Segments[j].Length; }
                else
                {
                    j = idx;
                    while (j < path.Segments.Count && !segName.ContainsKey((path, j))) j++;
                    if (j == path.Segments.Count) { warnings.Add($"CP {c.Name}: 경로 {path.Name}에 유효 세그먼트가 없어 건너뜀"); continue; }
                    idx = j; acc = dist = path.Segments.Take(j).Sum(x => x.Length);
                }
            }
            s.ControlPoints.Add(new ControlPoint(c.Name, CpTypeOf(c.Cap), segName[(path, idx)], (dist - acc) * o.Scale, null));
            if ((c.Type == "standby" || c.Type == "buffer") && c.Group.Length > 0)
            {
                var key = "nl_" + c.Group;
                if (!s.NameLists.TryGetValue(key, out var list)) s.NameLists[key] = list = new List<string>();
                list.Add(c.Name);
            }
        }
        var cpTemplate = Template("agvs_cptype.txt").Single();
        foreach (var kv in cpType)
            s.CpTypeLines.Add(cpTemplate.Replace("DefaultControlPoint", kv.Value).Replace(" cap 1 ", " cap " + kv.Key + " ").Replace("Feet", o.Units));

        foreach (var g in groups)
            if (!s.NameLists.ContainsKey("nl_" + g.Group)) throw new ExportException(g.Group);

        if (o.VehicleOverrides != null)
            foreach (var k in o.VehicleOverrides.Keys.Where(k => !groups.Any(g => g.TypeName == k)))
                warnings.Add($"vehicle override '{k}' matches no group type; ignored");

        foreach (var g in groups)
        {
            // shortcut: vehicle graphics use demo template size, Length/Width only feed traffic params; upgrade when Module C defines AGVVEHSEG geometry
            var seg = "Seg_" + g.TypeName;
            VehicleTemplateOverride? ov = null; o.VehicleOverrides?.TryGetValue(g.TypeName, out ov);
            if (ov?.VehSegText != null)
                s.VehicleSegmentLines.AddRange(VehicleOverrideApplier.VehSeg(ov.VehSegText, seg));
            else s.VehicleSegmentLines.AddRange(Template("agvs_vehseg.txt").Select(x => x.Replace("DefSegment", seg)));
            if (ov?.VehicleBlockText != null)
            {
                s.VehicleLines.AddRange(VehicleOverrideApplier.Vehicle(ov.VehicleBlockText, g.TypeName, g.MaxCount, seg, "nl_" + g.Group));
                continue;
            }
            var speed = N(g.Speed);
            foreach (var line in Template("agvs_veh.txt"))
            {
                var t = line.Replace("DefVehicle", g.TypeName).Replace("numveh 10", "numveh " + g.MaxCount)
                    .Replace("start nlwork", "start nl_" + g.Group).Replace("vehsegs item DefSegment", "vehsegs item " + seg);
                t = Regex.Replace(t, @"^(\s*rotate\s+0 )2( Seconds)", "${1}" + N(g.RotateDegPerSec) + "${2}");
                s.VehicleLines.Add(Regex.Replace(t, @"^(\s*(?:vel|crvvel|sprvel|rvel|rcrvvel|rsprvel|crabvel)\s+0 )3 Feet( Seconds)", "${1}" + speed + " " + o.Units + "${2}"));
            }
        }
        return s;
    }

    static GuidePath ToGuidePath(string name, LayoutPath p, Segment s, ExportOptions o, double tol, ICollection<string> warnings)
    {
        PathPiece piece;
        switch (s)
        {
            case LineSeg ln: piece = new LinePiece(Tx(ln.Start, o), Tx(ln.End, o)); break;
            case ArcSeg a:
                int tenths = (int)Math.Round(a.SweepDeg * 10);
                double gap = Math.Abs(a.SweepDeg * 10 - tenths) / 10 * Math.PI / 180 * a.Radius;
                if (gap > tol)
                    warnings.Add($"{name}: 원호 각도 {a.SweepDeg.ToString("0.###", CultureInfo.InvariantCulture)}°를 0.1° 단위로 반올림하여 끝점이 {gap.ToString("0.####", CultureInfo.InvariantCulture)} 어긋남");
                piece = new ArcPiece(Tx(a.Center, o), Tx(a.Start, o), tenths);
                break;
            default: throw new NotSupportedException(s.GetType().Name);
        }
        return new GuidePath(name, o.PathType, p.IsSpur, p.IsCrab, piece, p.IsTwoWay);
    }
}
