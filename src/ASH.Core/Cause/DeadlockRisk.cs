using System;
using System.Collections.Generic;

namespace ASH.Core.Cause;

/// <summary>Layout-only: chains of blocks that can lock each other (no waiting room between them).</summary>
public static class DeadlockRisk
{
    public static IEnumerable<CauseItem> Find(CauseContext c) => Array.Empty<CauseItem>(); // stub: filled by its own work package
}
