using System;
using System.Collections.Generic;

namespace ASH.Core.Cause;

/// <summary>Routes far longer than their undirected distance (broken links, wrong directions).</summary>
public static class DetourCauses
{
    public static IEnumerable<CauseItem> Find(CauseContext c) => Array.Empty<CauseItem>(); // stub: filled by its own work package
}
