using ASH.Core.Film;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using ASH.Core.Runner;

namespace ASH.Tests.Film;

public class SceneFinderTests
{
    static ASH.Core.Layout.Layout Lay() => new()
    {
        Cps = { new LayoutCp { Name = "C1", Position = new Point2(0, 0) }, new LayoutCp { Name = "C2", Position = new Point2(40, 20) }, new LayoutCp { Name = "U1", Position = new Point2(10, 30) } }
    };
    static readonly Dictionary<string, string> Unload = new() { ["M004"] = "U1", ["M1"] = "U1", ["M2"] = "C1" };
    static SimEvent Ev(double t, string kind, string mat, string cp, double v) => new(t, kind, mat, cp, v);
    static IReadOnlyList<SceneCandidate> Find(params SimEvent[] e) => SceneFinder.Find(e, Lay(), Unload, 7200, 600);

    [Fact]
    public void Stockout_Window_With_Margin_At_Unload_Cp()
    {
        var s = Find(Ev(1200, "stockout", "M004", "", 0), Ev(2100, "stockin", "M004", "", 900)).Single(c => c.Kind == SceneKind.Stockout);
        Assert.Equal("자재 품절: M004", s.Title);
        Assert.Equal(1140, s.StartSec); Assert.Equal(2160, s.EndSec);
        Assert.Equal((10, 30), (s.CenterX, s.CenterY));
        Assert.Equal(900, s.Severity);
        Assert.Equal("00:20~00:35 재고 0 (900초)", s.Detail);
    }

    [Fact]
    public void Stockin_Without_Stockout_Starts_Inside_Warmup()
    {
        // began at 400, before the 600 warmup: the window starts at warmup
        var s = Find(Ev(900, "stockin", "M004", "", 500)).Single(c => c.Kind == SceneKind.Stockout);
        Assert.Equal(540, s.StartSec); Assert.Equal(960, s.EndSec); Assert.Equal(300, s.Severity);
        Assert.Equal("00:10~00:15 재고 0 (300초)", s.Detail);
    }

    [Fact]
    public void Stockout_Without_Stockin_Runs_To_SimSec()
    {
        var s = Find(Ev(7000, "stockout", "M004", "", 0)).Single(c => c.Kind == SceneKind.Stockout);
        Assert.Equal(6940, s.StartSec); Assert.Equal(7200, s.EndSec); Assert.Equal(200, s.Severity);
    }

    [Fact]
    public void Delay_Spike_Uses_3x_Mean()
    {
        var e = Enumerable.Range(0, 20).Select(i => Ev(1000 + i * 100, "deliver", "M004", "U1", 90)).ToList();
        e.Add(Ev(5000, "deliver", "M004", "U1", 410));
        e.Add(Ev(5200, "deliver", "M2", "C1", 200)); // a lone sample is never 3x its own mean
        var s = Assert.Single(Find(e.ToArray()), c => c.Kind == SceneKind.DelaySpike);
        Assert.Equal(4560, s.StartSec); Assert.Equal(5030, s.EndSec);
        Assert.Equal((10, 30), (s.CenterX, s.CenterY));
        Assert.Equal("평균 90초 → 410초", s.Detail);
    }

    [Fact]
    public void Bottleneck_Picks_Max_Wait_Window()
    {
        // C2 cluster straddles the 1200 s boundary; C1 has more wait in total (450) than C2 inside the winning window (400)
        var e = new List<SimEvent> { Ev(1000, "wait", "M1", "C1", 10) };
        foreach (var t in new[] { 1150, 1180, 1210, 1240 }) e.Add(Ev(t, "wait", "M1", "C2", 100));
        foreach (var t in new[] { 3000, 4000, 5000, 6000, 6900 }) e.Add(Ev(t, "wait", "M1", "C1", 90));
        var s = Find(e.ToArray()).Single(c => c.Kind == SceneKind.Bottleneck);
        Assert.Equal(660, s.StartSec); Assert.Equal(1260, s.EndSec); // first of the tied windows that hold the whole cluster
        Assert.Equal(410, s.Severity);
        Assert.Equal("병목: C2", s.Title);
        Assert.Equal((40, 20), (s.CenterX, s.CenterY));
    }

    [Fact]
    public void Lone_Spike_Among_Three_Deliveries_Is_Found()
    {
        var s = Assert.Single(Find(Ev(2000, "deliver", "M004", "U1", 90), Ev(3000, "deliver", "M004", "U1", 90), Ev(4000, "deliver", "M004", "U1", 410)), c => c.Kind == SceneKind.DelaySpike);
        Assert.Equal("평균 90초 → 410초", s.Detail);
    }

    [Fact]
    public void All_Zero_Deliveries_Give_No_Spike()
    {
        var r = Find(Enumerable.Range(0, 5).Select(i => Ev(1000 + i, "deliver", "M004", "U1", 0)).ToArray());
        Assert.DoesNotContain(r, c => c.Kind == SceneKind.DelaySpike);
    }

    [Fact]
    public void Spikes_Are_Per_Material()
    {
        var e = new List<SimEvent>();
        for (int i = 0; i < 200; i++) { e.Add(Ev(1000 + i, "deliver", "M1", "U1", 100)); e.Add(Ev(1000 + i, "deliver", "M2", "C1", 2000)); }
        e.Add(Ev(5000, "deliver", "M1", "U1", 400));
        e.Add(Ev(5000, "deliver", "M2", "C1", 10000));
        var titles = Find(e.ToArray()).Where(c => c.Kind == SceneKind.DelaySpike).Select(c => c.Title).OrderBy(t => t).ToList();
        Assert.Equal(new[] { "지연: M1", "지연: M2" }, titles); // a global top 1% would drop M1's 400
    }

    [Fact]
    public void Non_Finite_Values_Are_Ignored()
    {
        var bad = new[]
        {
            Ev(2000, "deliver", "M004", "U1", double.NaN), Ev(2001, "deliver", "M004", "U1", double.PositiveInfinity),
            Ev(double.NaN, "deliver", "M004", "U1", 50), Ev(3000, "wait", "M1", "C1", double.PositiveInfinity), Ev(3001, "wait", "M1", "C1", -5),
            Ev(900, "stockin", "M004", "", double.NaN), Ev(901, "stockin", "M004", "", -3),
        };
        Assert.Equal(SceneKind.Whole, Assert.Single(Find(bad)).Kind);
        var s = Assert.Single(Find(bad.Concat(new[] { Ev(2000, "deliver", "M004", "U1", 90), Ev(3000, "deliver", "M004", "U1", 90), Ev(4000, "deliver", "M004", "U1", 410) }).ToArray()),
            c => c.Kind == SceneKind.DelaySpike);
        Assert.Equal("평균 90초 → 410초", s.Detail);
    }

    [Fact]
    public void No_Events_Gives_Only_Whole()
    {
        var s = Assert.Single(Find());
        Assert.Equal(SceneKind.Whole, s.Kind);
        Assert.Equal((600, 7200), (s.StartSec, s.EndSec));
        Assert.Equal((20, 15), (s.CenterX, s.CenterY));
    }

    [Fact]
    public void At_Most_Five_Per_Kind()
    {
        var e = new List<SimEvent>();
        for (int i = 1; i <= 8; i++) { e.Add(Ev(1000 + i, "stockout", "S" + i, "", 0)); e.Add(Ev(1100 + i * 10, "stockin", "S" + i, "", 100 + i * 9)); }
        for (int i = 0; i < 2000; i++) e.Add(Ev(1000 + i * 3, "deliver", "M1", "U1", 90));
        for (int i = 0; i < 8; i++) e.Add(Ev(2000 + i * 500, "deliver", "M1", "U1", 900 + i));
        var r = Find(e.ToArray());
        Assert.Equal(5, r.Count(c => c.Kind == SceneKind.Stockout));
        Assert.Equal(5, r.Count(c => c.Kind == SceneKind.DelaySpike));
        Assert.Single(r, c => c.Kind == SceneKind.Whole);
        var so = r.Where(c => c.Kind == SceneKind.Stockout).Select(c => c.Severity).ToList();
        Assert.Equal(so.OrderByDescending(x => x), so);
    }
}
