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
    /// <summary>Gap a vehicle keeps to the one ahead when it has stopped behind it (metres): the stop distance of its sensor field.</summary>
    public const double StopGapM = 0.5;

    static string[] Template(string name)
    {
        using var s = typeof(LayoutExporter).Assembly.GetManifestResourceStream("ASH.Core.Export.Templates." + name)
            ?? throw new InvalidOperationException("missing template " + name);
        var text = new StreamReader(s).ReadToEnd().Replace("\r\n", "\n");
        if (text.EndsWith("\n")) text = text.Substring(0, text.Length - 1);
        return text.Split('\n');
    }

    static string N(double d) => AsyWriter.Num(d);


    static void CheckName(string kind, string name)
    {
        if (!LayoutChecker.IsValidName(name)) throw new ArgumentException($"{kind} name '{name}' is empty, contains whitespace or is a reserved .asy word");
    }

    /// <summary>Template lines keep their own unit tokens (Feet); only UNITS, GPATHTYPE vel, CPOINTTYPE distance and the substituted speeds use o.Units.
    /// Coordinates, CP distances, speeds and accelerations are meters (drawing x Scale, m/s) times o.UnitsPerMeter.</summary>
    public static AgvsSystem ToAgvs(Layout.Layout l, ExportOptions o, ICollection<string>? warnings = null)
    {
        if (o.Groups == null || o.Groups.Count == 0) throw new InvalidOperationException("ExportOptions.Groups is required");
        var groups = o.Groups;
        warnings ??= new List<string>();
        double upm = o.UnitsPerMeter, toModel = o.Scale * upm;
        // a Scale that does not match the drawing unit makes a model 1000 times too big or too small (mm drawing with Scale 1)
        var ends = l.Paths.SelectMany(p => p.Segments).SelectMany(sg => new[] { sg.Start, sg.End }).ToList();
        if (ends.Count > 0)
        {
            double extentM = Math.Max(ends.Max(p => p.X) - ends.Min(p => p.X), ends.Max(p => p.Y) - ends.Min(p => p.Y)) * o.Scale;
            if (extentM > 5000 || (extentM > 0 && extentM < 0.5))
                warnings.Add($"경로 전체 크기가 {extentM.ToString("0.###", CultureInfo.InvariantCulture)} m입니다. ash.settings.tsv의 Scale(도면 단위→m, mm 도면은 0.001)이 도면 단위와 맞는지 확인하세요.");
        }
        foreach (var p in l.Paths) CheckName("path", p.Name);
        foreach (var c in l.Cps) CheckName("CP", c.Name);
        foreach (var b in l.Blocks) CheckName("block", b.Name);
        foreach (var g in groups) { CheckName("group", g.Group); CheckName("vehicle type", g.TypeName); if (!(g.RotateDegPerSec > 0)) throw new ArgumentException($"group {g.Group}: RotateDegPerSec must be > 0"); }

        var s = new AgvsSystem { Version = o.Version, UnitsTokens = new[] { o.Units, "Seconds" } };
        s.HeaderLines.AddRange(Template("agvs_header.txt").Select(x => o.ClaimBlockOnNextCp ? x.Replace("ignore ResumeSpeedWhenClaimed", "ignore OldBlockClaim ResumeSpeedWhenClaimed") : x));
        s.PathTypeLines.Add($"GPATHTYPE name {o.PathType} one normal attach rigid color -1 nav 1 vel 1 {N(groups.Max(g => Math.Max(g.Speed, g.SpeedEmpty)) * upm)} {o.Units} Seconds ");

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
            if (!cpType.TryGetValue(cap, out var t)) cpType[cap] = t = cap == 1 ? o.CpType : o.CpType + (cap == int.MaxValue ? "_capinf" : "_cap" + cap);
            return t;
        }
        CpTypeOf(1); // header AGVSDEF references the base type
        var cpTemplate = Template("agvs_cptype.txt").Single();
        // a CPOINT at the drawing point p: projected onto its path and placed on the GPATH segment that holds it
        ControlPoint? Place(string name, Point2 p, string type)
        {
            var pr = LayoutChecker.Project(l, p);
            if (pr == null) return null;
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
                    if (j == path.Segments.Count) { warnings.Add($"CP {name}: 경로 {path.Name}에 유효 세그먼트가 없어 건너뜀"); return null; }
                    idx = j; acc = dist = path.Segments.Take(j).Sum(x => x.Length);
                }
            }
            return new ControlPoint(name, type, segName[(path, idx)], (dist - acc) * toModel, null);
        }
        foreach (var c in l.Cps)
        {
            if (Place(c.Name, c.Position, CpTypeOf(c.Cap)) is not { } placed) continue;
            s.ControlPoints.Add(placed);
            if ((c.Type == "standby" || c.Type == "buffer") && c.Group.Length > 0)
            {
                var key = "nl_" + c.Group;
                if (!s.NameLists.TryGetValue(key, out var list)) s.NameLists[key] = list = new List<string>();
                list.Add(c.Name);
            }
        }
        foreach (var kv in cpType)
            s.CpTypeLines.Add(cpTemplate.Replace("DefaultControlPoint", kv.Value).Replace(" cap 1 ", " cap " + kv.Key + " ").Replace("Feet", o.Units));

        foreach (var g in groups)
            if (!s.NameLists.ContainsKey("nl_" + g.Group)) throw new ExportException(g.Group);
        AddStartLists(s, l, o, Place, cpTemplate, warnings);

        if (o.VehicleOverrides != null)
            foreach (var k in o.VehicleOverrides.Keys.Where(k => !groups.Any(g => g.TypeName == k)))
                warnings.Add($"vehicle override '{k}' matches no group type; ignored");

        foreach (var g in groups)
        {
            // the picture is built from Length x Width (VehicleGeometry; the picture is in feet whatever the model unit is)
            var seg = "Seg_" + g.TypeName;
            VehicleTemplateOverride? ov = null; o.VehicleOverrides?.TryGetValue(g.TypeName, out ov);
            if (ov?.VehSegText != null)
                s.VehicleSegmentLines.AddRange(VehicleOverrideApplier.VehSeg(ov.VehSegText, seg));
            else s.VehicleSegmentLines.AddRange(VehicleGeometry.Build(Template("agvs_vehseg.txt"), g.Kind, g.Length, g.Width).Select(x => x.Replace("DefSegment", seg)));
            if (ov?.VehicleBlockText != null)
            {
                s.VehicleLines.AddRange(VehicleOverrideApplier.Vehicle(ov.VehicleBlockText, g.TypeName, g.MaxCount, seg, StartList(g.Group)));
                continue;
            }
            var speed = N(g.Speed * upm);
            bool emptySpeed = g.SpeedEmpty > 0 && g.SpeedEmpty != g.Speed;
            // sensor field (AutoMod vehicle attributes: brake distance = line distance to the vehicle ahead at which braking starts, stop
            // distance = distance at which it stands; brake = v^2 / 2d + stop). Both are always written, for the Default AND the Empty line
            // (parked/retrieving vehicles use the Empty line, where the template's 0 gave 0 m and stacked vehicles on top of each other),
            // and never below StopGapM. Without a Decel the template's own 1 ft/s^2 applies.
            double dec = g.Decel > 0 ? g.Decel : 0.3048, vmax = Math.Max(g.Speed, g.SpeedEmpty);
            var brake = N((vmax * vmax / (2 * dec) + StopGapM) * upm); var stop = N(StopGapM * upm);
            foreach (var line in Template("agvs_veh.txt"))
            {
                var t = line.Replace("DefVehicle", g.TypeName).Replace("numveh 10", "numveh " + g.MaxCount)
                    .Replace("start nlwork", "start " + StartList(g.Group)).Replace("vehsegs item DefSegment", "vehsegs item " + seg);
                // accel and decel of the loaded (Default) line; the Empty line inherits it. 0 keeps the template value.
                if (g.Accel > 0) t = Regex.Replace(t, @"^(\s*accel\s+0 )1 Feet( Seconds Seconds)", "${1}" + N(g.Accel * upm) + " " + o.Units + "${2}");
                if (g.Decel > 0) t = Regex.Replace(t, @"^(\s*decel\s+0 )1 Feet( Seconds Seconds)", "${1}" + N(g.Decel * upm) + " " + o.Units + "${2}");
                t = Regex.Replace(t, @"^(\s*rotate\s+0 )2( Seconds)", "${1}" + N(g.RotateDegPerSec) + "${2}");
                t = Regex.Replace(t, @"^(\s*brakedist\s+[01] )\S+ Feet", "${1}" + brake + " " + o.Units);
                t = Regex.Replace(t, @"^(\s*stopdist\s+[01] )\S+ Feet", "${1}" + stop + " " + o.Units);
                // the Empty line (retrieving, parking) runs at SpeedEmpty instead of inheriting the loaded speed
                if (emptySpeed) t = Regex.Replace(t, @"^(\s*(?:vel|sprvel|rvel|rsprvel|crabvel)\s+)1 0 Feet( Seconds)", "${1}0 " + N(g.SpeedEmpty * upm) + " " + o.Units + "${2}");
                // arc lines (crvvel, rcrvvel) take the curve speed when set, else the straight rule above
                if (g.SpeedEmptyCurve > 0 || emptySpeed) t = Regex.Replace(t, @"^(\s*(?:crvvel|rcrvvel)\s+)1 0 Feet( Seconds)", "${1}0 " + N((g.SpeedEmptyCurve > 0 ? g.SpeedEmptyCurve : g.SpeedEmpty) * upm) + " " + o.Units + "${2}");
                t = Regex.Replace(t, @"^(\s*(?:crvvel|rcrvvel)\s+0 )3 Feet( Seconds)", "${1}" + N((g.SpeedCurve > 0 ? g.SpeedCurve : g.Speed) * upm) + " " + o.Units + "${2}");
                s.VehicleLines.Add(Regex.Replace(t, @"^(\s*(?:vel|sprvel|rvel|rsprvel|crabvel)\s+0 )3 Feet( Seconds)", "${1}" + speed + " " + o.Units + "${2}"));
            }
        }
        return s;
    }

    /// <summary>Name list a group's vehicles start on (one CP per vehicle, see AddStartLists).</summary>
    public static string StartList(string group) => "nls_" + group;

    /// <summary>A start-only CP (its own CPOINTTYPE): not a layout CP, so ArcLayout and the export diff skip it.</summary>
    public static bool IsStartCp(ControlPoint c) => c.Type.EndsWith(StartSuffix, StringComparison.Ordinal);
    const string StartSuffix = "_start";

    // AutoMod puts vehicle k on the k-th CP of its type's start list, one vehicle per CP; vehicles beyond the list wait off the path and drop onto a
    // start CP the moment it is free, on top of the vehicle just leaving it (seen in the AutoView .mdf). So each vehicle gets its own CP: the group's
    // standby CPs first, then start-only CPs SP_<group>_<k> back along the standby lane, vehicle length + StopGapM apart.
    static void AddStartLists(AgvsSystem s, Layout.Layout l, ExportOptions o, Func<string, Point2, string, ControlPoint?> place, string cpTemplate, ICollection<string> warnings)
    {
        var placed = new HashSet<string>(s.ControlPoints.Select(c => c.Name));
        var names = new HashSet<string>(placed.Concat(s.Paths.Select(p => p.Name)));
        Point2 At(string cp) => l.Cps.First(c => c.Name == cp).Position;
        double Spacing(VehicleGroupSpec g) => (g.Length + StopGapM) / o.Scale; // layout units
        var own = new Dictionary<string, List<string>>();
        foreach (var g in o.Groups!)
            own[g.Group] = new[]
            {
                g.StandbyCps ?? new string[0],
                l.Cps.Where(c => c.Type == "standby" && c.Group == g.Group).Select(c => c.Name),
                s.NameLists["nl_" + g.Group],
            }.Select(x => x.Where(placed.Contains).Distinct().Take(Math.Max(1, g.MaxCount)).ToList()).First(x => x.Count > 0);
        // CPs other vehicles start on, with their spacing: a lane spot keeps that far from them
        var taken = new List<(string Name, Point2 P, double Sp)>();
        foreach (var g in o.Groups!) foreach (var n in own[g.Group]) if (!taken.Any(t => t.Name == n)) taken.Add((n, At(n), Spacing(g)));
        string startType = o.CpType + StartSuffix;
        foreach (var g in o.Groups!)
        {
            var list = own[g.Group];
            double sp = Spacing(g);
            if (list.Count < g.MaxCount)
            {
                // spots are measured from the home CP along the lane, so only the other vehicles' CPs need the (shorter, straight-line) distance check
                var foreign = taken.Where(t => t.Name != list[0]).ToList();
                int spare = foreign.Sum(t => (int)Math.Ceiling(2 * Math.Max(t.Sp, sp) / sp)); // spots one of them can rule out
                foreach (var p in StandbyQueueCheck.LaneSpots(l, At(list[0]), sp, g.Length / o.Scale, g.MaxCount - list.Count + spare))
                {
                    if (foreign.Any(t => t.P.DistanceTo(p) < Math.Max(t.Sp, sp) * (1 - 1e-9))) continue;
                    if (l.Cps.Any(c => c.Position.DistanceTo(p) < 0.05 / o.Scale)) continue; // never two CPs on one spot
                    var name = "SP_" + g.Group + "_" + (list.Count + 1);
                    while (!names.Add(name)) name += "_";
                    if (place(name, p, startType) is not { } cp) continue;
                    s.ControlPoints.Add(cp);
                    list.Add(name);
                    taken.Add((name, p, sp));
                    if (list.Count == g.MaxCount) break;
                }
                if (list.Count < g.MaxCount)
                    warnings.Add($"그룹 {g.Group}: 대기 CP {list[0]} 뒤 대기 차선에 차량 {g.MaxCount}대를 띄워 세울 자리가 {list.Count}대분뿐입니다. " +
                                 $"나머지 {g.MaxCount - list.Count}대는 AutoMod가 시작 CP가 비는 순간 그 위에 겹쳐 넣습니다. " +
                                 $"분기점·block 없이 {(g.MaxCount * (g.Length + StopGapM)).ToString("0.#", CultureInfo.InvariantCulture)} m(최대 {g.MaxCount}대 × (차량 길이 {g.Length.ToString("0.##", CultureInfo.InvariantCulture)} m + 간격 {StopGapM} m)) 이상인 대기 차선을 만드세요.");
            }
            s.NameLists[StartList(g.Group)] = list;
        }
        if (s.ControlPoints.Any(IsStartCp))
            s.CpTypeLines.Add(cpTemplate.Replace("DefaultControlPoint", startType).Replace("Feet", o.Units));
    }

    static GuidePath ToGuidePath(string name, LayoutPath p, Segment s, ExportOptions o, double tol, ICollection<string> warnings)
    {
        PathPiece piece;
        switch (s)
        {
            case LineSeg ln: piece = new LinePiece(o.ToModel(ln.Start), o.ToModel(ln.End)); break;
            case ArcSeg a:
                int tenths = (int)Math.Round(a.SweepDeg * 10);
                double gap = Math.Abs(a.SweepDeg * 10 - tenths) / 10 * Math.PI / 180 * a.Radius;
                if (gap > tol)
                    warnings.Add($"{name}: 원호 각도 {a.SweepDeg.ToString("0.###", CultureInfo.InvariantCulture)}°를 0.1° 단위로 반올림하여 끝점이 {gap.ToString("0.####", CultureInfo.InvariantCulture)} 어긋남");
                piece = new ArcPiece(o.ToModel(a.Center), o.ToModel(a.Start), tenths);
                break;
            default: throw new NotSupportedException(s.GetType().Name);
        }
        return new GuidePath(name, o.PathType, p.IsSpur, p.IsCrab, piece, p.IsTwoWay);
    }
}
