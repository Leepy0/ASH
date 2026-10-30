using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Input;

namespace ASH.Core.Layout;

/// <summary>
/// A Routes row whose CPs carry another group than its material (the GROUP attribute of the CP blocks): the vehicles of the material's group would be sent
/// to another group's CPs. Usually an old AGVEXCEL seed that paired CPs across groups; deleting the wrong rows and running AGVEXCEL again re-adds them right.
/// CPs without a group, standby/finish of AGV_Groups and CPs missing from the drawing (the validator reports those) are not judged here.
/// </summary>
public static class RouteGroupCheck
{
    public static List<Finding> Check(ProjectInput input, Layout l)
    {
        var res = new List<Finding>();
        var cps = l.Cps.Where(c => c.Name.Length > 0).GroupBy(c => c.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var r in input.Routes)
        {
            var m = input.Materials.FirstOrDefault(x => x.Material == r.Material);
            if (m == null || m.Group.Length == 0) continue;
            foreach (var (col, name) in r.FromCps.Select(c => ("SupplyFromCP", (string?)c)).Concat(new[] { ("SupplyToCP", (string?)r.SupplyToCP), ("BufferCP", r.BufferCP), ("ReturnFromCP", r.ReturnFromCP), ("ReturnToCP", r.ReturnToCP) }))
            {
                if (string.IsNullOrEmpty(name) || !cps.TryGetValue(name!, out var cp) || cp.Group.Length == 0 || cp.Group == m.Group) continue;
                res.Add(new Finding(FindingCode.InputError,
                    $"{input.RouteSheet} {r.Row}행 {col}: {name}은(는) {cp.Group} 그룹 CP · 자재 {r.Material}은(는) {m.Group} 그룹. 같은 그룹 CP로 고치거나 행을 지우고 AGVEXCEL로 다시 만드세요.",
                    cp.Position, $"{input.RouteSheet}!{r.Row}")); // the Excel mark (AGVMODEL) colours that cell; the message names the column
            }
        }
        return res;
    }
}
