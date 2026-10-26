using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Geometry;

namespace ASH.Core.Cause;

/// <summary>Deadlocks the model logged: when, where, who waited for whom, and what to change. One item per episode (DeadlockEvent).</summary>
public static class DeadlockCauses
{
    public static IEnumerable<CauseItem> Find(CauseContext c)
    {
        var res = new List<CauseItem>();
        foreach (var e in c.Result?.DeadlockEvents ?? Array.Empty<DeadlockEvent>())
        {
            if (e.Links.Count == 0) continue;
            // the blocks of the lock (waited for or held); a lock of CP waits only (no block) is named by its CPs
            var known = new HashSet<string>(c.Layout.Blocks.Select(b => b.Name));
            var blocks = e.Links.Select(k => k.WaitsFor).Concat(e.Links.SelectMany(k => k.Holds)).Where(known.Contains).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
            var where = blocks.Count > 0 ? blocks : e.Links.Select(k => k.WaitsFor).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
            var names = string.Join("·", where);
            var detail = string.Join(" / ", e.Links.Select(k => $"{k.Vehicle} 대기 {k.WaitsFor}" + (k.Holds.Count > 0 ? " · 점유 " + string.Join("·", k.Holds) : "")));
            var (x, y) = DeadlockRisk.Centre(c.Layout, blocks);
            if (x == null)
            {
                var at = e.Links.Where(k => !double.IsNaN(k.X) && !double.IsNaN(k.Y)).ToList();
                if (at.Count > 0) (x, y) = (at.Average(k => k.X), at.Average(k => k.Y));
            }
            // a vehicle standing at a stop CP and holding a block another one waits for is the first suspect
            var waited = new HashSet<string>(e.Links.Select(k => k.WaitsFor));
            var holders = e.Links.Where(k => !double.IsNaN(k.X) && !double.IsNaN(k.Y))
                .SelectMany(k => k.Holds.Where(waited.Contains).Select(b => (new Point2(k.X, k.Y), b)));
            var fix = blocks.Count > 0 ? DeadlockRisk.Fix(c, blocks, e.Links.Select(k => k.WaitsFor).FirstOrDefault(known.Contains), holders) : "";
            res.Add(new CauseItem(CauseKinds.Deadlock, 2, "교착 " + names, names, detail, fix, e.AtSec, x, y,
                e.Links.SelectMany(k => k.Holds.Append(k.WaitsFor)).Concat(blocks).Distinct().ToList()));
        }
        return res;
    }
}
