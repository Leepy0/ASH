using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Traffic;

namespace ASH.Core.Layout;

/// <param name="Kind">경로 / CP / block / 스테이션</param>
/// <param name="Info">Short text next to the name: path type and length, CP type/group/capacity, block capacity.</param>
/// <param name="At">Where to pan to (middle of a path, the CP, the middle of a block).</param>
/// <param name="Handle">Drawing handle, null when the object cannot be selected (stations).</param>
/// <param name="Tags">The finder categories this object belongs to.</param>
public sealed record FoundObject(string Kind, string Name, string Info, Point2 At, string? Handle, IReadOnlyList<string> Tags);

/// <summary>What the palette's finder lists: every path, CP, block and station of the drawing, with the categories it can be filtered by.</summary>
public static class ObjectIndex
{
    public const string All = "전체";
    public const string AllPaths = "경로 전체", AllCps = "CP 전체", Blocks = "block", Stations = "스테이션";
    public static string PathTag(string kind) => "경로 · " + kind;
    public static string CpTag(string type) => "CP · " + type;

    /// <summary>Entries of the category list, in display order.</summary>
    public static readonly string[] Categories = new[] { All, AllPaths, PathTag("단방향"), PathTag("양방향"), PathTag("지선"), PathTag("크랩"), AllCps }
        .Concat(CpTypes.All.Select(CpTag)).Concat(new[] { Blocks, Stations }).ToArray();

    public static List<FoundObject> Build(Layout l, double unitsPerMeter)
    {
        if (unitsPerMeter <= 0) unitsPerMeter = 1;
        var res = new List<FoundObject>();
        foreach (var p in l.Paths)
        {
            if (p.Segments.Count == 0) continue;
            var label = PathKinds.Label(p);
            var tags = new List<string> { AllPaths };
            tags.AddRange(label.Split('+').Select(PathTag));
            var len = (p.Length / unitsPerMeter).ToString("0.##", CultureInfo.InvariantCulture);
            res.Add(new FoundObject("경로", p.Name, $"{label} · {len} m", p.PointAt(p.Length / 2), p.Id, tags));
        }
        foreach (var c in l.Cps)
        {
            var type = string.IsNullOrWhiteSpace(c.Type) ? "?" : c.Type;
            var info = $"{type} · {(c.Group.Length > 0 ? c.Group : "-")} · CAP {CpTypes.FormatCap(c.Cap)}";
            res.Add(new FoundObject("CP", c.Name.Length > 0 ? c.Name : "(이름 없음)", info, c.Position, c.Id.Length > 0 ? c.Id : null, new[] { AllCps, CpTag(type) }));
        }
        foreach (var b in l.Blocks)
        {
            var mid = new Point2((b.Rect.Min.X + b.Rect.Max.X) / 2, (b.Rect.Min.Y + b.Rect.Max.Y) / 2);
            res.Add(new FoundObject("block", b.Name, "CAP " + CpTypes.FormatCap(b.Cap), mid, b.Id.Length > 0 ? b.Id : null, new[] { Blocks }));
        }
        foreach (var s in l.Stations)
            res.Add(new FoundObject("스테이션", s.Name, "", s.Position, null, new[] { Stations }));
        return res;
    }

    /// <summary>In the category (or "전체") and, when text is given, the name or info contains it (case-insensitive).</summary>
    public static bool Matches(FoundObject o, string category, string? text)
    {
        if (!string.IsNullOrEmpty(category) && category != All && !o.Tags.Contains(category)) return false;
        var t = (text ?? "").Trim();
        return t.Length == 0 || o.Name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0 || o.Info.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

/// <summary>Short Korean names of the check findings for the palette table.</summary>
public static class FindingText
{
    public static string Label(FindingCode code)
    {
        switch (code)
        {
            case FindingCode.DanglingEnd: return "끝점이 이어지지 않음";
            case FindingCode.DeadEndNode: return "들어오기만/나가기만 하는 점";
            case FindingCode.DuplicateCpName: return "CP 이름 중복";
            case FindingCode.UnnamedCp: return "CP 이름 없음";
            case FindingCode.CpOffPath: return "CP가 경로에서 벗어남";
            case FindingCode.BlockTouchesNoPath: return "block이 경로에 닿지 않음";
            case FindingCode.UnsplitIntersection: return "교차점이 나뉘지 않음";
            case FindingCode.RouteCpMissing: return "경로의 CP가 도면에 없음";
            case FindingCode.DuplicatePathName: return "경로 이름 중복";
            case FindingCode.InvalidName: return "이름이 올바르지 않음";
            case FindingCode.DuplicateBlockName: return "block 이름 중복";
            case FindingCode.InputError: return "입력 오류";
            case FindingCode.InputWarning: return "입력 경고";
            case FindingCode.StopCpInBlock: return "정차 CP가 block 안에 있음";
            default: return code.ToString();
        }
    }
}
