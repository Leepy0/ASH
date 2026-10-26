using System;
using System.Collections.Generic;

namespace ASH.Core.Cause;

/// <summary>Per group: where one job's time goes (seconds per job, after warmup). Pure = the driving time at free speed; BlockWait = waits for blocks;
/// Queue = waits at CPs behind other vehicles; Handling = load, unload, extra time; Other = the rest (JobSec - the parts).</summary>
public sealed record TimeBreakdown(string Group, int Jobs, double JobSec, double PureSec, double BlockWaitSec, double QueueSec, double HandlingSec, double OtherSec);

/// <summary>Where the time of a job goes, and the groups that lose most of it to congestion.</summary>
public static class TimeLossCauses
{
    public static IEnumerable<CauseItem> Find(CauseContext c) => Array.Empty<CauseItem>(); // stub: filled by its own work package

    /// <summary>One row per group; empty when the run has no leg data (models built before it).</summary>
    public static IReadOnlyList<TimeBreakdown> Breakdown(CauseContext c) => Array.Empty<TimeBreakdown>(); // stub
}
