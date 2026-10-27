using System.Collections.Generic;
using System.Linq;

namespace ASH.Core.Layout;

/// <summary>One AGVCPROUTE sequence as found in the drawing: the CPs that share a <see cref="LayoutCp.RouteId"/>, by type in drawing (placement) order.</summary>
public sealed class CpRoute
{
    public string Id { get; set; } = "";
    public string Group { get; set; } = "";
    public List<string> Standby { get; } = new();
    public List<string> Load { get; } = new();
    public List<string> Unload { get; } = new();
    public List<string> RtnLoad { get; } = new();
    public List<string> RtnUnload { get; } = new();
    public List<string> Finish { get; } = new();

    /// <summary>The CP of a per-load list that goes with load <paramref name="i"/>: the same index, else the last one (two loads, one unload), else "".</summary>
    public static string Pair(List<string> list, int i) => list.Count == 0 ? "" : list[System.Math.Min(i, list.Count - 1)];

    /// <summary>"대기 SB1 → 로딩 L1,L2 → …" for a log line; empty steps are left out.</summary>
    public string Describe()
    {
        var parts = new List<string>();
        void Add(string label, List<string> l) { if (l.Count > 0) parts.Add(label + " " + string.Join(",", l)); }
        Add("대기", Standby); Add("로딩", Load); Add("언로딩", Unload); Add("복귀 로딩", RtnLoad); Add("복귀 언로딩", RtnUnload); Add("완료", Finish);
        return string.Join(" → ", parts);
    }
}

public static class CpRoutes
{
    /// <summary>The recorded sequences of a drawing, in the order their first CP appears. A CP whose type is not a route step (traffic, buffer) is ignored.</summary>
    public static List<CpRoute> From(Layout layout)
    {
        var map = new Dictionary<string, CpRoute>();
        var list = new List<CpRoute>();
        foreach (var c in layout.Cps)
        {
            if (c.RouteId.Length == 0 || c.Name.Length == 0) continue;
            if (!map.TryGetValue(c.RouteId, out var r)) { r = new CpRoute { Id = c.RouteId, Group = c.Group }; map[c.RouteId] = r; list.Add(r); }
            switch (c.Type)
            {
                case "standby": r.Standby.Add(c.Name); break;
                case "load": r.Load.Add(c.Name); break;
                case "unload": r.Unload.Add(c.Name); break;
                case "rtnload": r.RtnLoad.Add(c.Name); break;
                case "rtnunload": r.RtnUnload.Add(c.Name); break;
                case "finish": r.Finish.Add(c.Name); break;
            }
            if (r.Group.Length == 0) r.Group = c.Group;
        }
        return list;
    }

    /// <summary>The CPs of the recorded sequences (by name): kept out of the one-by-one pairing in the template seed.</summary>
    public static HashSet<string> Names(Layout layout) => new(layout.Cps.Where(c => c.RouteId.Length > 0).Select(c => c.Name));
}
