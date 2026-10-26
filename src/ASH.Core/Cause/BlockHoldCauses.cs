using System;
using System.Collections.Generic;

namespace ASH.Core.Cause;

/// <summary>Blocks claimed too early or released too late: stop CPs near block edges (layout), hold per pass against the pure crossing time (run).</summary>
public static class BlockHoldCauses
{
    public static IEnumerable<CauseItem> Find(CauseContext c) => Array.Empty<CauseItem>(); // stub: filled by its own work package
}
