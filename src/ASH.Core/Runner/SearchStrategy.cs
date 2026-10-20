using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Input;
using ASH.Core.Model;

namespace ASH.Core.Runner;

public interface IRunExecutor { IReadOnlyList<RunKpi> Execute(RunScenario s); }

public sealed record SearchOptions(int InitialReplications = 1, bool ImpactTable = true, int MaxConfirmRounds = 3);
public sealed record SearchStep(RunScenario Scenario, IReadOnlyDictionary<string, Verdict> VerdictsByGroup, bool ClusterOk, string Phase);
/// <summary>PlusOne = the group's new count; deltas are the other group's mean KPI in the +1 run minus the confirmed baseline.</summary>
public sealed record ImpactRow(string Group, int PlusOne, string OtherGroup, double UtilDelta, double MinStockDelta);
public sealed record SearchResult(IReadOnlyDictionary<string, int> Decided, List<SearchStep> Trace, List<ImpactRow> Impact, List<string> Warnings);

public static class SearchStrategy
{
    public static SearchResult Search(ProjectInput input, IRunExecutor exec, GroupClusters clusters, SearchOptions opts)
    {
        var cache = new Dictionary<string, IReadOnlyList<RunKpi>>();
        var trace = new List<SearchStep>();
        var warnings = new List<string>();
        var cur = input.Groups.ToDictionary(g => g.Group, g => g.MaxCount);
        var byName = input.Groups.ToDictionary(g => g.Group);
        int initReps = Math.Max(1, opts.InitialReplications), reps = Math.Max(1, input.Settings.Replications);

        List<RunKpi> Runs(Dictionary<string, int> counts, int n)
        {
            var all = new List<RunKpi>();
            for (int i = 0; i < n; i++)
            {
                int seed = input.Settings.SeedBase + i;
                var key = seed + "|" + string.Join(",", input.Groups.Select(g => counts[g.Group]));
                if (!cache.TryGetValue(key, out var k))
                    cache[key] = k = exec.Execute(new RunScenario(seed, new Dictionary<string, int>(counts)));
                all.AddRange(k);
            }
            return all;
        }
        Dictionary<string, Verdict> Eval(IEnumerable<string> judged, int n, string phase)
        {
            var kpis = Runs(cur, n);
            var v = judged.ToDictionary(g => g, g => Judge.Evaluate(kpis.Where(k => k.Group == g), input.Settings));
            trace.Add(new SearchStep(new RunScenario(input.Settings.SeedBase, new Dictionary<string, int>(cur)), v, v.Values.All(x => x.Ok), phase));
            return v;
        }
        bool Passes(IReadOnlyList<string> cluster, int n, string phase) => Eval(cluster, n, phase).Values.All(x => x.Ok);

        foreach (var cluster in clusters.Clusters)
        {
            // coordinate descent: each group's binary search judges the whole cluster; repeat until a full pass changes nothing
            for (bool changed = true; changed;)
            {
                changed = false;
                foreach (var g in cluster)
                {
                    int hi = cur[g], lo = byName[g].MinCount, orig = hi;
                    while (lo < hi)
                    {
                        int mid = (lo + hi) / 2;
                        cur[g] = mid;
                        if (Passes(cluster, initReps, "search")) hi = mid; else lo = mid + 1;
                    }
                    cur[g] = hi;
                    if (hi < orig) changed = true;
                }
            }
            Passes(cluster, reps, "decided");
            for (bool improved = true; improved;)
            {
                improved = false;
                foreach (var g in cluster)
                {
                    if (cur[g] <= byName[g].MinCount) continue;
                    cur[g]--;
                    if (Passes(cluster, reps, "minus1")) improved = true; else cur[g]++;
                }
            }
        }

        var all = input.Groups.Select(g => g.Group).ToList();
        for (int round = 0; ; round++)
        {
            var v = Eval(all, reps, "confirm");
            if (v.Values.All(x => x.Ok)) break;
            var bump = v.Where(x => !x.Value.Ok && cur[x.Key] < byName[x.Key].MaxCount).Select(x => x.Key).ToList();
            if (round >= opts.MaxConfirmRounds || bump.Count == 0)
            {
                foreach (var f in v.Where(x => !x.Value.Ok)) warnings.Add($"{f.Key}: 확인 실패 ({cur[f.Key]}대): {f.Value.Reason}");
                break;
            }
            foreach (var g in bump) cur[g]++;
        }

        var impact = new List<ImpactRow>();
        if (opts.ImpactTable)
        {
            var baseline = Runs(cur, reps);
            static double Mean(IEnumerable<RunKpi> k, string g, Func<RunKpi, double> f) { var l = k.Where(x => x.Group == g).Select(f).ToList(); return l.Count == 0 ? double.NaN : l.Average(); }
            foreach (var g in input.Groups.Where(g => cur[g.Group] < g.MaxCount))
            {
                var plus = new Dictionary<string, int>(cur) { [g.Group] = cur[g.Group] + 1 };
                var kp = Runs(plus, reps);
                trace.Add(new SearchStep(new RunScenario(input.Settings.SeedBase, plus), new Dictionary<string, Verdict>(), true, "impact"));
                foreach (var o in input.Groups.Where(x => x.Group != g.Group))
                    impact.Add(new ImpactRow(g.Group, plus[g.Group], o.Group,
                        Mean(kp, o.Group, k => k.Utilization) - Mean(baseline, o.Group, k => k.Utilization),
                        Mean(kp, o.Group, k => k.MinStockRatio) - Mean(baseline, o.Group, k => k.MinStockRatio)));
            }
        }
        warnings.AddRange(clusters.Warnings);
        return new SearchResult(cur, trace, impact, warnings);
    }
}
