using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Traffic;

namespace ASH.Core.Layout;

/// <summary>
/// The CAD-free part of AGVCONVERT (ordinary lines/polylines -> paths) and of AGVPATH's tagging:
/// which entities convert, polyline vertices -> segments, one path per segment, and the command-line texts.
/// </summary>
public static class PathConvert
{
    /// <summary>Entity type names the plugin passes for what converts. POLYLINE2D = a 2D POLYLINE without curve/spline fitting.</summary>
    public static readonly string[] Types = { "LINE", "ARC", "LWPOLYLINE", "POLYLINE2D" };

    /// <summary>A 2D POLYLINE smoothed with PEDIT Fit/Spline: its vertices are not the drawn shape, so it is not converted.</summary>
    public const string FittedPolyline2d = "POLYLINE2D_FIT";

    /// <summary>Why an entity is left alone (shown in the summary), or null when it converts.</summary>
    /// <param name="type">One of <see cref="Types"/>, <see cref="FittedPolyline2d"/>, or any other name (e.g. the DXF name "SPLINE").</param>
    public static string? SkipReason(string type, string layer, bool layerLocked, IEnumerable<string> pathLayers)
    {
        if (pathLayers.Any(l => string.Equals(l, layer, StringComparison.OrdinalIgnoreCase))) return "이미 AGV 경로";
        // AGV_BLOCK rectangles, result layers ...: never turn ASH's own objects into paths
        if (layer.StartsWith("AGV_", StringComparison.OrdinalIgnoreCase)) return "AGV 레이어 " + layer;
        if (layerLocked) return "잠긴 레이어 " + layer;
        if (type == FittedPolyline2d) return "곡선·스플라인 맞춤 폴리선";
        return Array.IndexOf(Types, type) >= 0 ? null : type + "(지원 안 함)";
    }

    /// <summary>Segments of a polyline in vertex order: a bulge becomes an arc (bulge = tan(sweep/4)), zero-length pieces are dropped.</summary>
    public static List<Segment> FromVertices(IReadOnlyList<(Point2 P, double Bulge)> v, bool closed)
    {
        var segs = new List<Segment>();
        int n = v.Count, count = closed ? n : n - 1;
        for (int i = 0; i < count; i++)
        {
            var s = Bulge.ToSegment(v[i].P, v[(i + 1) % n].P, v[i].Bulge);
            if (s != null && s.Length > 1e-9) segs.Add(s);
        }
        return segs;
    }

    /// <summary>One path per segment, as AGVPATH makes them, so fillet, join and split work on each piece.
    /// Named P### after <paramref name="names"/>, which receives every new name.</summary>
    public static List<LayoutPath> Pieces(IEnumerable<Segment> segs, (bool Spur, bool Two, bool Crab) kind, ICollection<string> names)
    {
        var pieces = new List<LayoutPath>();
        foreach (var seg in segs)
        {
            var name = AutoNamer.Next("P", names);
            names.Add(name);
            var p = new LayoutPath(name) { IsSpur = kind.Spur, IsTwoWay = kind.Two, IsCrab = kind.Crab };
            p.Segments.Add(seg);
            pieces.Add(p);
        }
        return pieces;
    }

    /// <summary>"객체 n개 → 단방향 경로 m개", plus a second line listing what was skipped (largest group first).</summary>
    public static string Summary(int entities, int paths, IReadOnlyDictionary<string, int> skipped)
    {
        var s = $"변환: 선·폴리선 {entities}개 → 단방향 경로 {paths}개 (AGV_PATH 레이어)";
        if (skipped.Count > 0)
            s += "\n건너뜀: " + string.Join(", ", skipped.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key} {k.Value}개"));
        return s;
    }

    /// <summary>Printed at the end of AGVCONVERT: how to change direction and type, and what comes next.</summary>
    public static readonly string Guidance = string.Join("\n",
        "──── 다음 단계 ────",
        "모든 경로가 그린 방향(시작점 → 끝점)의 단방향이 되었습니다. 화살표로 방향을 확인하세요.",
        "· 방향 바꾸기: AGVREVERSE (팔레트 경로 탭 '방향 반전') → 뒤집을 경로를 선택하고 Enter. 경로를 먼저 선택한 뒤 실행해도 됩니다.",
        "· 종류 바꾸기: AGVPTYPE (팔레트 '경로 종류 변경') → 경로 선택 → 단방향(O)/양방향(T)/지선(S)/크랩(C). 양방향은 방향과 무관합니다.",
        "  속성 창에서 레이어만 바꾸면 종류가 바뀌지 않습니다(종류는 경로 데이터에 저장됨). 반드시 AGVPTYPE를 쓰세요.",
        "· 끊긴 곳 잇기 AGVJOIN, 모서리 둥글리기 AGVFILLET, 나중에 추가한 선은 다시 AGVCONVERT.",
        "· 다음: AGVCP로 CP 배치 → AGVCHECK로 검사.",
        "· 되돌리기: Ctrl+Z 한 번이면 변환과 분할이 모두 취소됩니다.");
}
