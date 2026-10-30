using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Input;

namespace ASH.Core.Layout;

/// <summary>
/// Every vehicle of a group comes home to the group's standby CP, so the ones that arrive while another waits there queue up on the path
/// before it. That queue needs MaxCount vehicles' worth of room (length + 0.5 m gap) before the path reaches a junction or a traffic block;
/// a queue that reaches back into one holds it and stops the vehicles that want to leave (seen in the crossing scenario).
/// </summary>
public static class StandbyQueueCheck
{
    const double Gap = 0.5; // metres between queued vehicles

    /// <summary>Warnings in Korean; <paramref name="scale"/> = model units (metres) per layout unit.</summary>
    public static List<string> Check(Layout l, ProjectInput input, double scale)
    {
        var res = new List<string>();
        if (!(scale > 0)) return res;
        var nodeAt = NodeAt(l);
        foreach (var g in input.Groups)
        {
            var type = input.AgvTypes.FirstOrDefault(t => t.Type == g.Type);
            if (type == null || g.IsCycle || g.StandbyCPs.Count == 0) continue; // a Cycle group's standby CPs are unused (the validator warns)
            var cp = l.Cps.FirstOrDefault(c => c.Name == g.StandbyCPs[0]);
            if (cp == null || LayoutChecker.Project(l, cp.Position) is not { } pr) continue;
            double need = g.MaxCount * (type.Length + Gap);          // metres
            double room = Room(l, nodeAt, pr.Path, pr.Dist, need / scale) * scale;
            if (room + 1e-6 < need)
                res.Add($"그룹 {g.Group}: 대기 CP {cp.Name} 앞에 줄 설 공간이 {room.ToString("0.#", CultureInfo.InvariantCulture)} m뿐입니다. " +
                        $"최대 {g.MaxCount}대가 줄 서려면 {need.ToString("0.#", CultureInfo.InvariantCulture)} m가 필요합니다(차량 길이 {type.Length.ToString("0.##", CultureInfo.InvariantCulture)} m + 간격 {Gap} m). " +
                        "줄이 분기점이나 block까지 닿으면 그 block을 잡은 채 서 있어 다른 차량이 막힙니다.");
        }
        return res;
    }

    // the path ends that meet at each point, by node
    static Dictionary<(LayoutPath, bool), PathNode> NodeAt(Layout l)
    {
        var nodeAt = new Dictionary<(LayoutPath, bool), PathNode>();
        foreach (var n in PathGraph.Build(l).Nodes) foreach (var e in n.Ends) nodeAt[(e.Path, e.AtStart)] = n;
        return nodeAt;
    }

    /// <summary>Points on the lane behind the CP at <paramref name="cp"/> (layout units), spacing, 2 x spacing, ... back along the path, the same
    /// walk as the queue check (through plain joins, stopping at a junction or a block), each with <paramref name="tail"/> of free lane behind it
    /// for the vehicle body; at most <paramref name="max"/> points, fewer when the lane ends.</summary>
    public static List<Point2> LaneSpots(Layout l, Point2 cp, double spacing, double tail, int max)
    {
        var res = new List<Point2>();
        if (!(spacing > 0) || max <= 0 || LayoutChecker.Project(l, cp) is not { } pr) return res;
        var trail = new List<(LayoutPath Path, double Dist, double Room)>();
        double room = Room(l, NodeAt(l), pr.Path, pr.Dist, max * spacing + tail, trail);
        for (int k = 1; k <= max && k * spacing + tail <= room + 1e-9; k++)
        {
            var t = trail.Last(x => x.Room <= k * spacing);
            res.Add(t.Path.PointAt(Math.Max(0, t.Dist - (k * spacing - t.Room))));
        }
        return res;
    }

    // free path length behind (path, dist), walking back through plain joins, until a junction, a block, or enough room (layout units);
    // trail gets each path walked with the dist it was entered at and the room before it
    static double Room(Layout l, Dictionary<(LayoutPath, bool), PathNode> nodeAt, LayoutPath path, double dist, double enough,
        List<(LayoutPath Path, double Dist, double Room)>? trail = null)
    {
        double room = 0;
        var seen = new HashSet<LayoutPath>();
        while (room < enough && seen.Add(path))
        {
            trail?.Add((path, dist, room));
            // back along this path from dist to its start, stopping at a block
            for (double d = dist; d > 0; d -= Math.Min(0.1, d))
            {
                var p = path.PointAt(Math.Max(0, d - 0.1));
                if (l.Blocks.Any(b => b.Rect.Contains(p))) return room;
                room += Math.Min(0.1, d);
                if (room >= enough) return room;
            }
            // the node at this path's start: continue only through a plain join (one path in, this one out)
            if (!nodeAt.TryGetValue((path, true), out var node)) return room;
            var ins = node.Ends.Where(e => !e.AtStart).ToList();
            if (node.Ends.Count > 2 || ins.Count != 1) return room;
            path = ins[0].Path; dist = path.Length;
        }
        return room;
    }
}
