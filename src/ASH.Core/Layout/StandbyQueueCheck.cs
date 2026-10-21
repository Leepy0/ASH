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
        var graph = PathGraph.Build(l);
        // the path ends that meet at each point, by node
        var nodeAt = new Dictionary<(LayoutPath, bool), PathNode>();
        foreach (var n in graph.Nodes) foreach (var e in n.Ends) nodeAt[(e.Path, e.AtStart)] = n;
        foreach (var g in input.Groups)
        {
            var type = input.AgvTypes.FirstOrDefault(t => t.Type == g.Type);
            if (type == null || g.StandbyCPs.Count == 0) continue;
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

    // free path length behind (path, dist), walking back through plain joins, until a junction, a block, or enough room (layout units)
    static double Room(Layout l, Dictionary<(LayoutPath, bool), PathNode> nodeAt, LayoutPath path, double dist, double enough)
    {
        double room = 0;
        var seen = new HashSet<LayoutPath>();
        while (room < enough && seen.Add(path))
        {
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
