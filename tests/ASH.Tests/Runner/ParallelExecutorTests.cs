#if NET10_0_OR_GREATER
using ASH.Core.Model;
using ASH.Core.Runner;
using ASH.Runner;

namespace ASH.Tests.Runner;

public class ParallelExecutorTests
{
    static RunScenario S(int seed) => new(seed, new Dictionary<string, int> { ["G1"] = 1 });

    [Fact]
    public void Runs_At_Most_N_Concurrently_Each_On_Its_Own_Slot_And_Maps_Results_In_Order()
    {
        int now = 0, peak = 0;
        var inUse = new HashSet<int>();
        var ex = new ParallelExecutor(3, (slot, s) =>
        {
            lock (inUse) { Assert.True(inUse.Add(slot), "slot used twice at once"); peak = Math.Max(peak, ++now); }
            Thread.Sleep(30 + (s.Seed % 3) * 20);                    // finish out of order
            lock (inUse) { now--; inUse.Remove(slot); }
            return new[] { new RunKpi("G1", 1, s.Seed / 100.0, 2, s.Seed, 0, 0, 0, 0) };
        });
        var res = ex.ExecuteMany(Enumerable.Range(1, 12).Select(S).ToList());
        Assert.Equal(Enumerable.Range(1, 12), res.Select(r => r[0].Supplies));
        Assert.Equal(3, peak);
        Assert.Equal(12, ex.Runs);
    }

    [Fact]
    public void Single_Slot_Is_Sequential_And_Execute_Works_Alone()
    {
        int now = 0, peak = 0;
        var ex = new ParallelExecutor(1, (slot, s) =>
        {
            Assert.Equal(1, slot);
            peak = Math.Max(peak, Interlocked.Increment(ref now)); Thread.Sleep(10); Interlocked.Decrement(ref now);
            return new[] { new RunKpi("G1", 1, 0, 2, s.Seed, 0, 0, 0, 0) };
        });
        Assert.Equal(7, ex.Execute(S(7))[0].Supplies);
        ex.ExecuteMany(Enumerable.Range(1, 5).Select(S).ToList());
        Assert.Equal(1, peak);
    }
}
#endif
