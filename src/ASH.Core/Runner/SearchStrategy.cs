using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using ASH.Core.Input;
using ASH.Core.Model;

namespace ASH.Core.Runner;

public interface IRunExecutor { IReadOnlyList<RunKpi> Execute(RunScenario s); }

/// <summary>Optional: an executor that can run a batch concurrently; results are in input order. Cancellation stops starting new runs.
/// Slots = runs it keeps in flight at once (the search sizes its rounds to it).</summary>
public interface IBatchRunExecutor : IRunExecutor
{
    int Slots { get; }
    IReadOnlyList<IReadOnlyList<RunKpi>> ExecuteMany(IReadOnlyList<RunScenario> scenarios, CancellationToken ct = default);
}

public static class RunExecutorExtensions
{
    /// <summary>Sequential fallback for executors without batch support (no default interface methods on netstandard2.0).</summary>
    public static IReadOnlyList<IReadOnlyList<RunKpi>> ExecuteMany(this IRunExecutor exec, IReadOnlyList<RunScenario> scenarios, CancellationToken ct = default) =>
        exec is IBatchRunExecutor b ? b.ExecuteMany(scenarios, ct) : scenarios.Select(s => { ct.ThrowIfCancellationRequested(); return exec.Execute(s); }).ToList();
}

/// <summary>ExpectedRuns is only passed through to SearchProgress (SearchEstimate.Expected).</summary>
public sealed record SearchOptions(int InitialReplications = 1, bool ImpactTable = false, int MaxConfirmRounds = 3, int ExpectedRuns = 0);
/// <summary>Scenario carries the first seed; Seeds lists all seeds run (SeedBase + i). ClusterOk is null for impact runs (not judged).</summary>
public sealed record SearchStep(RunScenario Scenario, IReadOnlyDictionary<string, Verdict> VerdictsByGroup, bool? ClusterOk, string Phase,
    int Replications, IReadOnlyList<int> Seeds);
/// <summary>PlusOne = the group's new count; deltas are the other group's mean KPI in the +1 run minus the confirmed baseline.</summary>
public sealed record ImpactRow(string Group, int PlusOne, string OtherGroup, double UtilDelta, double MinStockDelta);
public sealed record SearchResult(IReadOnlyDictionary<string, int> Decided, List<SearchStep> Trace, List<ImpactRow> Impact, List<string> Warnings);
/// <summary>What one cluster needs run before its next step: (counts, replications) requests and a log label.</summary>
sealed record SearchAsk(IReadOnlyList<(Dictionary<string, int> Counts, int N)> Runs, string Label);

public static class ImpactTsv
{
    public static void Write(string path, IEnumerable<ImpactRow> rows)
    {
        var inv = CultureInfo.InvariantCulture;
        using var w = new StreamWriter(path, false, new System.Text.UTF8Encoding(false)) { NewLine = "\n" };
        w.WriteLine("group\tplusOne\totherGroup\tutilDelta\tminStockDelta");
        foreach (var r in rows)
            w.WriteLine(string.Join("\t", r.Group, r.PlusOne.ToString(inv), r.OtherGroup, r.UtilDelta.ToString("R", inv), r.MinStockDelta.ToString("R", inv)));
    }
}

public static class SearchStrategy
{
    /// <summary>InitStockMode Fixed: seeds do not change the result, so one replication.</summary>
    internal static bool FixedStock(ASH.Core.Input.Settings s) => string.Equals((s.InitStockMode ?? "").Trim(), "Fixed", StringComparison.OrdinalIgnoreCase);

    /// <summary>Confirmation only (no search): runs exactly <paramref name="counts"/> (one per group, within Min..Max, else ArgumentException) at the
    /// settings' replications and judges every group, like the "confirm" round of <see cref="Search"/> without the bump. Failures become warnings.</summary>
    public static SearchResult Confirm(ProjectInput input, IRunExecutor exec, IReadOnlyDictionary<string, int> counts, CancellationToken ct = default)
    {
        foreach (var k in counts.Keys)
            if (input.Groups.All(g => g.Group != k)) throw new ArgumentException("그룹이 없습니다: " + k);
        var cur = new Dictionary<string, int>();
        foreach (var g in input.Groups)
        {
            if (!counts.TryGetValue(g.Group, out var n)) throw new ArgumentException($"{g.Group}: 대수가 없습니다.");
            if (n < g.MinCount || n > g.MaxCount) throw new ArgumentException($"{g.Group}: {n}대는 범위 {g.MinCount}~{g.MaxCount}대 밖입니다.");
            cur[g.Group] = n;
        }
        var warnings = new List<string>();
        int reps = Math.Max(1, input.Settings.Replications);
        if (FixedStock(input.Settings) && reps > 1)
        {
            warnings.Add($"InitStockMode Fixed: 시드가 결과를 바꾸지 않으므로 반복 {reps}회 대신 1회로 실행");
            reps = 1;
        }
        var seeds = Enumerable.Range(0, reps).Select(i => input.Settings.SeedBase + i).ToList();
        var kpis = exec.ExecuteMany(seeds.Select(s => new RunScenario(s, new Dictionary<string, int>(cur))).ToList(), ct).SelectMany(x => x).ToList();
        var v = input.Groups.ToDictionary(g => g.Group, g => Judge.Evaluate(kpis.Where(k => k.Group == g.Group), input.Settings));
        foreach (var f in v.Where(x => !x.Value.Ok))
            warnings.Add($"{f.Key}: {(f.Value.Rule == RuleCode.Utilization ? "확인 경고" : "확인 실패")} ({cur[f.Key]}대): {f.Value.Reason}");
        var trace = new List<SearchStep> { new(new RunScenario(input.Settings.SeedBase, new Dictionary<string, int>(cur)), v, v.Values.All(x => x.Ok), "confirm", reps, seeds) };
        return new SearchResult(cur, trace, new List<ImpactRow>(), warnings);
    }

    /// <summary>Evenly spread probe counts for the open interval [lo, hi) (hi is the count already taken): every count when at most k are left,
    /// else k counts lo + ⌊i·(hi−lo)/(k+1)⌋. k = 1 gives the binary-search midpoint (lo+hi)/2.</summary>
    internal static List<int> Points(int lo, int hi, int k)
    {
        int n = hi - lo;
        return n <= k ? Enumerable.Range(lo, n).ToList() : Enumerable.Range(1, k).Select(i => lo + (int)((long)i * n / (k + 1))).ToList();
    }

    /// <summary>New (lo, hi) after probing ascending <paramref name="pts"/>: hi goes to the lowest probe from which every higher probe passed,
    /// lo to just above the highest failing probe under it. Monotone verdicts: exactly the binary-search step. A pass below a fail is noise and ignored.</summary>
    internal static (int Lo, int Hi) Narrow(IReadOnlyList<int> pts, IReadOnlyList<bool> ok, int lo, int hi)
    {
        for (int j = pts.Count - 1; j >= 0; j--)
            if (ok[j]) hi = pts[j]; else return (pts[j] + 1, hi);
        return (lo, hi);
    }

    /// <summary>Per cluster: baseline at Max (initial reps) → pass 1 k-ary search per group → later passes try only cur−1 per group until a
    /// pass changes nothing → "decided" at Settings.Replications → "minus1" tries cur−1 per group at full reps. Groups failing at baseline
    /// are skipped after pass 1. Then "confirm" over all groups (bumps failing groups, up to MaxConfirmRounds), then optional impact runs.
    /// Pass 1 per group: each round probes <see cref="Points"/>(lo, hi, share / initial reps) as one batch and narrows with <see cref="Narrow"/>;
    /// share = the cluster's part of the executor's slots, so with one slot (or no batch executor) it is the binary search, and for monotone verdicts
    /// every slot count decides the same. Groups inside a cluster still go one after another (each sees the others' new counts).
    /// Batch executor with Slots ≥ 2: every cluster searches at once, each round's asks of all clusters run as one batch, the slots split evenly
    /// (at least 1 each); a cluster holds the other clusters' groups at their counts when it started (clusters do not interact), so the confirm
    /// round is the first run of all decided counts together. Otherwise clusters go one after another, each starting from the decided counts before it.
    /// Run budget per cluster with k groups and range R = Max−Min+1 (initial reps 1, one slot, nothing cached): 1 + k·⌈log2 R⌉ + k per later pass
    /// + reps (decided) + k·reps (minus1, cached when reps = 1), plus confirm and impact (k·reps). With InitStockMode Fixed reps is 1 (see FixedStock).
    /// <paramref name="onLog"/> gets one line per round that starts runs.</summary>
    public static SearchResult Search(ProjectInput input, IRunExecutor exec, GroupClusters clusters, SearchOptions opts, CancellationToken ct = default,
        Action<SearchProgress>? onProgress = null, Action<string>? onLog = null)
    {
        var cache = new Dictionary<string, IReadOnlyList<RunKpi>>();
        var trace = new List<SearchStep>();
        var warnings = new List<string>();
        var cur = input.Groups.ToDictionary(g => g.Group, g => g.MaxCount);
        var byName = input.Groups.ToDictionary(g => g.Group);
        int initReps = Math.Max(1, opts.InitialReplications), reps = Math.Max(1, input.Settings.Replications);
        if (FixedStock(input.Settings) && (initReps > 1 || reps > 1))
        {
            warnings.Add($"InitStockMode Fixed: 시드가 결과를 바꾸지 않으므로 반복 {reps}회 대신 1회로 실행");
            initReps = reps = 1;
        }
        IReadOnlyList<int> Seeds(int n) => Enumerable.Range(0, n).Select(i => input.Settings.SeedBase + i).ToList();

        string Key(int seed, Dictionary<string, int> counts) => seed + "|" + string.Join(",", input.Groups.Select(g => counts[g.Group]));
        // runs every uncached (counts, replications) request in one batch so a parallel executor can fan out
        void Fill(IEnumerable<(Dictionary<string, int> Counts, int N)> reqs)
        {
            ct.ThrowIfCancellationRequested();
            var todo = new Dictionary<string, RunScenario>();
            foreach (var (counts, n) in reqs)
                foreach (var seed in Seeds(n))
                {
                    var key = Key(seed, counts);
                    if (!cache.ContainsKey(key) && !todo.ContainsKey(key)) todo[key] = new RunScenario(seed, new Dictionary<string, int>(counts));
                }
            if (todo.Count == 0) return;
            var res = exec.ExecuteMany(todo.Values.ToList(), ct);
            int i = 0;
            foreach (var key in todo.Keys) cache[key] = res[i++];
        }
        bool Cached((Dictionary<string, int> Counts, int N) r) => Seeds(r.N).All(seed => cache.ContainsKey(Key(seed, r.Counts)));
        List<RunKpi> Runs(Dictionary<string, int> counts, int n)
        {
            Fill(new[] { (counts, n) });
            return Seeds(n).SelectMany(seed => cache[Key(seed, counts)]).ToList();
        }
        Dictionary<string, Verdict> Eval(Dictionary<string, int> counts, IEnumerable<string> judged, int n, string phase)
        {
            var kpis = Runs(counts, n);
            var v = judged.ToDictionary(g => g, g => Judge.Evaluate(kpis.Where(k => k.Group == g), input.Settings));
            trace.Add(new SearchStep(new RunScenario(input.Settings.SeedBase, new Dictionary<string, int>(counts)), v, v.Values.All(x => x.Ok), phase, n, Seeds(n)));
            return v;
        }
        // g itself must pass and every other group that passed at the cluster baseline must still pass
        bool Accept(IReadOnlyList<string> cluster, string g, HashSet<string> baselineOk, Dictionary<string, int> counts, int n, string phase)
        {
            var v = Eval(counts, cluster, n, phase);
            return v[g].Ok && cluster.All(o => o == g || !baselineOk.Contains(o) || v[o].Ok);
        }
        SearchAsk Ask(Dictionary<string, int> counts, int n, string label) => new(new[] { (counts, n) }, label);
        // one at−1 try per group; changed[0] = something moved
        IEnumerable<SearchAsk> MinusOnePass(IReadOnlyList<string> cluster, Dictionary<string, int> at, HashSet<string> baselineOk, int n, string phase, bool[] changed)
        {
            changed[0] = false;
            foreach (var g in cluster)
            {
                if (!baselineOk.Contains(g) || at[g] <= byName[g].MinCount) continue;
                var t = new Dictionary<string, int>(at) { [g] = at[g] - 1 };
                yield return Ask(t, n, $"그룹 {g} {at[g]}→{at[g] - 1}");
                if (Accept(cluster, g, baselineOk, t, n, phase)) { at[g]--; changed[0] = true; }
            }
        }

        var confirmed = new Dictionary<string, int>();
        int groupsLeft = clusters.Clusters.Sum(c => c.Count);
        void Report(string phase, IReadOnlyList<string>? current = null) =>
            onProgress?.Invoke(new SearchProgress(phase, new Dictionary<string, int>(confirmed),
                (current ?? new string[0]).ToDictionary(g => g, g => cur[g]), groupsLeft, opts.ExpectedRuns));

        int share = 1; // the slots the cluster being stepped may fill with its next ask (set by the driver below)
        // one cluster's search as a coroutine: it yields what it needs run, the driver runs the asks of all clusters together, then resumes it
        IEnumerable<SearchAsk> Cluster(IReadOnlyList<string> cluster)
        {
            var at = new Dictionary<string, int>(cur); // only this cluster's groups move; cur gets them when it is done
            var baselineOk = new HashSet<string>();
            string cname = string.Join(",", cluster);
            Report("탐색 " + cname, cluster);
            yield return Ask(at, initReps, cname + " 기준");
            foreach (var kv in Eval(at, cluster, initReps, "baseline"))
            {
                if (kv.Value.Ok) baselineOk.Add(kv.Key);
                else warnings.Add($"묶음 {cname}: 그룹 {kv.Key}는 최대 대수에서도 판정 실패 — 다른 그룹 탐색은 {kv.Key} 제외 기준으로 진행");
            }
            // pass 1: k-ary search per group (binary with one slot)
            var changed = new bool[1];
            foreach (var g in cluster)
            {
                int hi = at[g], lo = byName[g].MinCount, orig = hi;
                while (lo < hi)
                {
                    var pts = Points(lo, hi, Math.Max(1, share / initReps));
                    var tries = pts.Select(p => new Dictionary<string, int>(at) { [g] = p }).ToList();
                    yield return new SearchAsk(tries.Select(t => (t, initReps)).ToList(), $"그룹 {g} [{lo}..{hi}] → {string.Join(",", pts)}");
                    (lo, hi) = Narrow(pts, tries.Select(t => Accept(cluster, g, baselineOk, t, initReps, "search")).ToList(), lo, hi);
                }
                at[g] = hi;
                if (hi < orig) changed[0] = true;
            }
            // later passes: only cur−1 per group (another group's reduction can only have made room for a small step)
            while (changed[0]) foreach (var a in MinusOnePass(cluster, at, baselineOk, initReps, "search", changed)) yield return a;
            yield return Ask(at, reps, $"{cname} 반복 {reps}회");
            Eval(at, cluster, reps, "decided");
            do foreach (var a in MinusOnePass(cluster, at, baselineOk, reps, "minus1", changed)) yield return a;
            while (changed[0]);
            foreach (var g in cluster) confirmed[g] = cur[g] = at[g];
            groupsLeft -= cluster.Count;
            Report("확정 " + cname);
        }

        // driver: one slot (or no batch executor) = clusters one after another, as before; more slots = every cluster at once
        int slots = exec is IBatchRunExecutor bx ? Math.Max(1, bx.Slots) : 1;
        var waiting = new Queue<IReadOnlyList<string>>(clusters.Clusters);
        var live = new List<IEnumerator<SearchAsk>>();
        while (true)
        {
            while (waiting.Count > 0 && (slots > 1 || live.Count == 0)) live.Add(Cluster(waiting.Dequeue()).GetEnumerator());
            if (live.Count == 0) break;
            var asks = new List<SearchAsk>();
            int k = live.Count, i = 0;
            foreach (var it in live.ToList())
            {
                share = Math.Max(1, slots / k + (i++ < slots % k ? 1 : 0));
                bool more;
                while ((more = it.MoveNext()) && it.Current.Runs.All(Cached)) { } // an ask already in the cache needs no round
                if (more) asks.Add(it.Current); else live.Remove(it);
            }
            if (asks.Count == 0) continue;
            var reqs = asks.SelectMany(a => a.Runs).ToList();
            int fresh = reqs.SelectMany(r => Seeds(r.N).Select(seed => Key(seed, r.Counts))).Distinct().Count(key => !cache.ContainsKey(key));
            onLog?.Invoke($"탐색 라운드: {string.Join(" · ", asks.Select(a => a.Label))} ({fresh}회)");
            Fill(reqs);
        }

        var all = input.Groups.Select(g => g.Group).ToList();
        Report("확인");
        for (int round = 0; ; round++)
        {
            var v = Eval(cur, all, reps, "confirm");
            if (v.Values.All(x => x.Ok)) break;
            var bump = v.Where(x => !x.Value.Ok && cur[x.Key] < byName[x.Key].MaxCount).Select(x => x.Key).ToList();
            if (round >= opts.MaxConfirmRounds || bump.Count == 0)
            {
                foreach (var f in v.Where(x => !x.Value.Ok)) warnings.Add($"{f.Key}: {(f.Value.Rule == RuleCode.Utilization ? "확인 경고" : "확인 실패")} ({cur[f.Key]}대): {f.Value.Reason}");
                break;
            }
            foreach (var g in bump) cur[g]++;
        }

        var impact = new List<ImpactRow>();
        if (opts.ImpactTable)
        {
            Report("영향표");
            var plusOf = input.Groups.Where(g => cur[g.Group] < g.MaxCount)
                .ToDictionary(g => g.Group, g => new Dictionary<string, int>(cur) { [g.Group] = cur[g.Group] + 1 });
            Fill(plusOf.Values.Select(c => (c, reps)).Prepend((new Dictionary<string, int>(cur), reps)));
            var baseline = Runs(cur, reps);
            static double Mean(IEnumerable<RunKpi> k, string g, Func<RunKpi, double> f) { var l = k.Where(x => x.Group == g).Select(f).ToList(); return l.Count == 0 ? double.NaN : l.Average(); }
            foreach (var g in input.Groups.Where(g => cur[g.Group] < g.MaxCount))
            {
                var plus = plusOf[g.Group];
                var kp = Runs(plus, reps);
                trace.Add(new SearchStep(new RunScenario(input.Settings.SeedBase, plus), new Dictionary<string, Verdict>(), null, "impact", reps, Seeds(reps)));
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
