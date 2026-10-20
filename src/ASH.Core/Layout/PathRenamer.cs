using System.Collections.Generic;
using System.Linq;
using ASH.Core.Traffic;

namespace ASH.Core.Layout;

public static class PathRenamer
{
    /// <summary>Path Id -> new name: every path after the first of a duplicate group, and every invalid name ("?"-prefixed untagged ones included), gets AutoNamer "P###".</summary>
    public static Dictionary<string, string> Plan(IEnumerable<LayoutPath> paths)
    {
        var list = paths.ToList();
        var taken = list.Select(p => p.Name).ToList();
        var seen = new HashSet<string>();
        var res = new Dictionary<string, string>();
        foreach (var p in list)
        {
            if (LayoutChecker.IsValidName(p.Name) && seen.Add(p.Name)) continue;
            var n = AutoNamer.Next("P", taken);
            taken.Add(n);
            res[p.Id] = n;
        }
        return res;
    }
}
