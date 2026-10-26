using System;
using System.Collections.Generic;

namespace ASH.Core.Cause;

/// <summary>Materials whose share of their group's driving is far above their share of the demand.</summary>
public static class LoadShareCauses
{
    public static IEnumerable<CauseItem> Find(CauseContext c) => Array.Empty<CauseItem>(); // stub: filled by its own work package
}
