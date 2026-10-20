using System.Collections.Concurrent;
using ASH.Core.Model;
using ASH.Core.Runner;

namespace ASH.Runner;

/// <summary>Owns <c>slots</c> work slots; each run takes a free slot, so at most <c>slots</c> runs are in flight.
/// <c>run</c> gets (slot 1..slots, scenario) and must not throw.</summary>
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

    public IReadOnlyList<RunKpi> Execute(RunScenario s)
    {
        gate.Wait();
        free.TryDequeue(out var slot);
        try { Interlocked.Increment(ref count); return run(slot, s); }
        finally { free.Enqueue(slot); gate.Release(); }
    }

    public IReadOnlyList<IReadOnlyList<RunKpi>> ExecuteMany(IReadOnlyList<RunScenario> scenarios)
    {
        var res = new IReadOnlyList<RunKpi>[scenarios.Count];
        Parallel.For(0, scenarios.Count, new ParallelOptions { MaxDegreeOfParallelism = slots }, i => res[i] = Execute(scenarios[i]));
        return res;
    }
}
