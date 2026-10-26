using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Runner;

namespace ASH.Core.Cause;

/// <summary>Per group: where one job's time goes (seconds per job, after warmup). Pure = the driving time at free speed; BlockWait = waits for blocks;
/// Queue = waits at CPs behind other vehicles; Handling = load, unload, extra time; Other = the rest (JobSec - the parts).</summary>
public sealed record TimeBreakdown(string Group, int Jobs, double JobSec, double PureSec, double BlockWaitSec, double QueueSec, double HandlingSec, double OtherSec);

/// <summary>Where the time of a job goes, and the groups that lose most of it to congestion.</summary>
public static class TimeLossCauses
{
    /// <summary>A group whose job, handling left out, takes 1.5x its pure driving or more (2x: 심각). Fix names the block the group waits
    /// at most (its claims x the block's average wait) when block waits outweigh the queues.</summary>
    public static IEnumerable<CauseItem> Find(CauseContext c)
    {
        foreach (var b in Breakdown(c))
        {
            double travel = b.JobSec - b.HandlingSec;
            if (b.PureSec <= 0 || travel < 1.5 * b.PureSec) continue;
            var detail = $"작업 1회 {b.JobSec:0}초 · 주행 {b.PureSec:0}초 · block 대기 {b.BlockWaitSec:0}초 · CP 대기 {b.QueueSec:0}초"
                         + (b.HandlingSec > 0 ? $" · 적재·하역 {b.HandlingSec:0}초" : "");
            var fix = "";
            if (b.BlockWaitSec > 0 && b.BlockWaitSec >= b.QueueSec)
            {
                var claims = c.Result!.GroupTimes!.First(x => x.Group == b.Group).BlockClaims;
                var worst = c.Result.Blocks.Where(x => claims.ContainsKey(x.Name) && x.AvgWaitSec > 0)
                    .OrderByDescending(x => x.AvgWaitSec * claims[x.Name]).FirstOrDefault();
                if (worst != null) fix = $"{worst.Name} block 점검 · 1회 대기 {worst.AvgWaitSec:0.#}초";
            }
            yield return new CauseItem(CauseKinds.TimeLoss, travel >= 2 * b.PureSec ? 2 : 1, $"{b.Group} 정체 {Math.Round(100 * (travel - b.PureSec) / b.JobSec):0}%",
                b.Group, detail, fix);
        }
    }

    /// <summary>One row per group with jobs after warmup (tsplit rows); empty when the run has none (models built before them).
    /// Pure = free-speed driving + turning on the spot. BlockWait = the group's claims of each block x AutoMod's average wait per claim of it
    /// (whole run), at most the measured standing; Queue = the rest of the standing. Other = accelerating, braking, curves, creeping behind
    /// a vehicle, buffer hold, repairs.</summary>
    public static IReadOnlyList<TimeBreakdown> Breakdown(CauseContext c)
    {
        var rows = new List<TimeBreakdown>();
        var wait = c.Result?.Blocks.ToDictionary(x => x.Name, x => x.AvgWaitSec, StringComparer.Ordinal);
        foreach (var t in c.Result?.GroupTimes ?? Array.Empty<GroupTime>())
        {
            if (t.Jobs <= 0) continue;
            double n = t.Jobs, job = t.JobSec / n, pure = (t.FreeSec + t.TurnSec) / n, stop = t.StopSec / n, hand = t.HandlingSec / n;
            double block = Math.Min(stop, t.BlockClaims.Sum(kv => kv.Value * (wait!.TryGetValue(kv.Key, out var w) && w > 0 ? w : 0)) / n);
            rows.Add(new TimeBreakdown(t.Group, t.Jobs, job, pure, block, stop - block, hand, job - pure - stop - hand));
        }
        return rows;
    }
}
