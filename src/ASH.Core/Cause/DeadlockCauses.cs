using System;
using System.Collections.Generic;

namespace ASH.Core.Cause;

/// <summary>Deadlocks the model logged: when, where, who waited for whom, and what to change.</summary>
public static class DeadlockCauses
{
    public static IEnumerable<CauseItem> Find(CauseContext c) => Array.Empty<CauseItem>(); // stub: filled by its own work package
}
