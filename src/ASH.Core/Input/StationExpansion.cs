using System.Collections.Generic;
using System.Linq;

namespace ASH.Core.Input;

/// <summary>A Materials row with several unload CPs supplies several stations: it becomes one station material per unload CP, named {Material}@{UnloadCP}.</summary>
public static class StationExpansion
{
    /// <summary>Replaces each material named in <paramref name="unloadLists"/> (unload CPs in the order typed, two or more) and its routes by one copy per
    /// unload CP, in their place. A copy keeps everything else, Row included, and has Source = the original name.</summary>
    public static void Expand(ProjectInput p, IDictionary<string, List<string>> unloadLists)
    {
        if (unloadLists.Count == 0) return;
        var materials = p.Materials.SelectMany(m => unloadLists.TryGetValue(m.Material, out var cps)
            ? cps.Select(cp => m with { Material = m.Material + "@" + cp, Source = m.Material }) : new[] { m }).ToList();
        var routes = p.Routes.SelectMany(r => unloadLists.TryGetValue(r.Material, out var cps)
            ? cps.Select(cp => r with { Material = r.Material + "@" + cp, SupplyToCP = cp }) : new[] { r }).ToList();
        p.Materials.Clear(); p.Materials.AddRange(materials);
        p.Routes.Clear(); p.Routes.AddRange(routes);
    }
}
