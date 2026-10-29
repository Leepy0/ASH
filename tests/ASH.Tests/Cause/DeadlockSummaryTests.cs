using ASH.Core.Cause;

namespace ASH.Tests.Cause;

public class DeadlockSummaryTests
{
    static DeadlockLink L(string v, string g, string waits, string[]? holds = null, double x = 0, double y = 0) => new(v, g, waits, holds ?? Array.Empty<string>(), x, y);
    static KeyValuePair<string, IReadOnlyList<DeadlockEvent>?> Run(string id, params DeadlockEvent[] ev) => new(id, ev);

    static readonly DeadlockEvent Ring = new(100, new[] { L("V1", "G1", "B007", new[] { "B005" }), L("V2", "G2", "B005", new[] { "B007" }) });

    [Fact]
    public void Counts_Episodes_Runs_First_Time_Groups_And_Vehicles_Per_Place_Most_First()
    {
        var cp = new DeadlockEvent(40, new[] { L("V9", "G1", "L001") });
        var spots = DeadlockSummary.Build(new[]
        {
            Run("r1", Ring, new DeadlockEvent(900, new[] { L("V3", "G1", "B005", new[] { "B007" }) }), cp),
            Run("r2", new DeadlockEvent(50, new[] { L("V1", "G1", "B007", new[] { "B005" }) })),
            Run("r3"),
        });
        Assert.Equal(new[] { "B005·B007", "L001" }, spots.Select(s => s.Name));
        var s = spots[0];
        Assert.Equal(3, s.Episodes); Assert.Equal(2, s.Runs);
        Assert.Equal(50, s.FirstSec); Assert.Equal("r2", s.FirstRun);
        Assert.Equal("r1", s.TopRun); // 2 episodes there against 1
        Assert.Equal(new[] { "G1", "G2" }, s.Groups); Assert.Equal(new[] { "V1", "V2", "V3" }, s.Vehicles);
        Assert.Equal((1, 1, 40.0), (spots[1].Episodes, spots[1].Runs, spots[1].FirstSec));
    }

    [Fact]
    public void Blocks_Are_Told_From_Cps_By_The_Predicate_Else_Every_Name_Counts()
    {
        var e = new DeadlockEvent(10, new[] { L("V1", "G", "L001", new[] { "B1", "B2" }), L("V2", "G", "B1") });
        Assert.Equal("B1·B2", DeadlockSummary.Place(e, n => n.StartsWith("B", StringComparison.Ordinal)));
        Assert.Equal("B1·B2·L001", DeadlockSummary.Place(e));
        Assert.Equal("B1", DeadlockSummary.Place(new DeadlockEvent(1, new[] { L("V", "G", "B1", new[] { "L9" }) }), n => n == "B1"));
        // no known block at all: the CPs waited for
        Assert.Equal("L001", DeadlockSummary.Place(new DeadlockEvent(1, new[] { L("V", "G", "L001", new[] { "X" }) }), _ => false));
    }

    [Fact]
    public void Odd_Data_Never_Throws()
    {
        Assert.Empty(DeadlockSummary.Build(Array.Empty<KeyValuePair<string, IReadOnlyList<DeadlockEvent>?>>()));
        Assert.Empty(DeadlockSummary.Build(new[] { new KeyValuePair<string, IReadOnlyList<DeadlockEvent>?>("r", null), Run("r2", new DeadlockEvent(5, Array.Empty<DeadlockLink>())) }));
        var odd = DeadlockSummary.Build(new[]
        {
            Run("r", new DeadlockEvent(double.NaN, new[] { L("", "", "", null, double.NaN, double.NaN) }), new DeadlockEvent(double.PositiveInfinity, new[] { L("V1", "G", " ") })),
        });
        var s = Assert.Single(odd);
        Assert.Equal(DeadlockSummary.Unknown, s.Name); Assert.Equal(2, s.Episodes); Assert.True(double.IsNaN(s.FirstSec)); // no usable time
        Assert.Equal(new[] { "V1" }, s.Vehicles); Assert.Equal(new[] { "G" }, s.Groups);
    }
}
