using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ASH.Core.Model;

namespace ASH.Core.Runner;

/// <summary>Owns <c>slots</c> work slots; each run takes a free slot, so at most <c>slots</c> runs are in flight.
/// <c>run</c> gets (slot 1..slots, scenario) and must not throw. Cancellation stops starting new runs; runs in flight finish.</summary>
public sealed class ParallelExecutor : IBatchRunExecutor
{
    readonly Func<int, RunScenario, IReadOnlyList<RunKpi>> run;
    readonly int slots;
    readonly SemaphoreSlim gate;
    readonly ConcurrentQueue<int> free = new();
    int count;

    public ParallelExecutor(int slots, Func<int, RunScenario, IReadOnlyList<RunKpi>> run)
    {
        this.slots = Math.Max(1, slots); this.run = run;
        gate = new SemaphoreSlim(this.slots);
        for (int i = 1; i <= this.slots; i++) free.Enqueue(i);
    }

    public int Runs => Volatile.Read(ref count);

    public IReadOnlyList<RunKpi> Execute(RunScenario s) => Execute(s, default);

    IReadOnlyList<RunKpi> Execute(RunScenario s, CancellationToken ct)
    {
        gate.Wait(ct);
        free.TryDequeue(out var slot);
        try { Interlocked.Increment(ref count); return run(slot, s); }
        finally { free.Enqueue(slot); gate.Release(); }
    }

    public IReadOnlyList<IReadOnlyList<RunKpi>> ExecuteMany(IReadOnlyList<RunScenario> scenarios, CancellationToken ct = default)
    {
        var res = new IReadOnlyList<RunKpi>[scenarios.Count];
        Parallel.For(0, scenarios.Count, new ParallelOptions { MaxDegreeOfParallelism = slots, CancellationToken = ct }, i => res[i] = Execute(scenarios[i], ct));
        return res;
    }
}
