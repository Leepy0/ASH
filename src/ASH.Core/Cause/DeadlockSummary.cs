using System;
using System.Collections.Generic;
using System.Linq;

namespace ASH.Core.Cause;

/// <summary>One place deadlocks happened, over several runs: Name = the blocks (or CPs) of the lock, sorted and joined with "·" like the cause tab's title.
/// ByRun = run id -> episodes there. FirstSec = earliest episode (NaN when no episode has a time), FirstRun = the run it was in.</summary>
public sealed record DeadlockSpot(string Name, int Episodes, IReadOnlyDictionary<string, int> ByRun, double FirstSec, string FirstRun,
    IReadOnlyList<string> Groups, IReadOnlyList<string> Vehicles)
{
    public int Runs => ByRun.Count;
    /// <summary>The run with most episodes here (the first one on a tie): the one to open on the map.</summary>
    public string TopRun => ByRun.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault() ?? "";
}

/// <summary>Deadlock episodes of many runs counted per place. Never throws on odd data: events without links, empty or missing names and NaN times are skipped or kept as unknown.</summary>
public static class DeadlockSummary
{
    public const string Unknown = "위치 불명";

    /// <summary>Where one episode happened: the blocks it waited for or held (those <paramref name="isBlock"/> accepts, when given), else the CPs waited for.
    /// Sorted ordinally and joined with "·"; <see cref="Unknown"/> when no name is left.</summary>
    public static string Place(DeadlockEvent e, Func<string, bool>? isBlock = null)
    {
        var links = e?.Links ?? Array.Empty<DeadlockLink>();
        var waits = links.Select(k => k?.WaitsFor).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).ToList();
        var all = waits.Concat(links.Where(k => k?.Holds != null).SelectMany(k => k.Holds).Where(n => !string.IsNullOrWhiteSpace(n))).Distinct().ToList();
        var names = isBlock == null ? all : all.Where(isBlock).ToList();
        if (names.Count == 0) names = waits.Distinct().ToList();
        return names.Count == 0 ? Unknown : string.Join("·", names.OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>Spots by episodes (most first), then runs affected, then first time. Runs with no events add nothing.</summary>
    public static IReadOnlyList<DeadlockSpot> Build(IEnumerable<KeyValuePair<string, IReadOnlyList<DeadlockEvent>?>> runs, Func<string, bool>? isBlock = null)
    {
        var acc = new Dictionary<string, (Dictionary<string, int> ByRun, double First, string FirstRun, SortedSet<string> Groups, SortedSet<string> Vehicles)>(StringComparer.Ordinal);
        foreach (var run in runs ?? Array.Empty<KeyValuePair<string, IReadOnlyList<DeadlockEvent>?>>())
            foreach (var e in run.Value ?? Array.Empty<DeadlockEvent>())
            {
                if (e == null || e.Links == null || e.Links.Count == 0) continue;
                var name = Place(e, isBlock);
                if (!acc.TryGetValue(name, out var a)) acc[name] = a = (new Dictionary<string, int>(StringComparer.Ordinal), double.NaN, "", new SortedSet<string>(StringComparer.Ordinal), new SortedSet<string>(StringComparer.Ordinal));
                a.ByRun[run.Key] = (a.ByRun.TryGetValue(run.Key, out var n) ? n : 0) + 1;
                if (!double.IsNaN(e.AtSec) && !double.IsInfinity(e.AtSec) && (double.IsNaN(a.First) || e.AtSec < a.First)) { a.First = e.AtSec; a.FirstRun = run.Key; }
                foreach (var k in e.Links.Where(k => k != null))
                {
                    if (!string.IsNullOrWhiteSpace(k.Group)) a.Groups.Add(k.Group);
                    if (!string.IsNullOrWhiteSpace(k.Vehicle)) a.Vehicles.Add(k.Vehicle);
                }
                acc[name] = a;
            }
        return acc.Select(kv => new DeadlockSpot(kv.Key, kv.Value.ByRun.Values.Sum(), kv.Value.ByRun, kv.Value.First, kv.Value.FirstRun, kv.Value.Groups.ToList(), kv.Value.Vehicles.ToList()))
            .OrderByDescending(s => s.Episodes).ThenByDescending(s => s.Runs).ThenBy(s => double.IsNaN(s.FirstSec) ? double.MaxValue : s.FirstSec).ThenBy(s => s.Name, StringComparer.Ordinal).ToList();
    }
}
