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
        Assert.Equal("결품: M004", s.Title);
        Assert.Equal(1140, s.StartSec); Assert.Equal(2160, s.EndSec);
        Assert.Equal((10, 30), (s.CenterX, s.CenterY));
        Assert.Equal(900, s.Severity);
        Assert.Equal("00:20~00:35 재고 0 (900초)", s.Detail);
    }

    [Fact]
    public void Stockin_Without_Stockout_Starts_Inside_Warmup()
    {
        var s = Find(Ev(900, "stockin", "M004", "", 300)).Single(c => c.Kind == SceneKind.Stockout);
        Assert.Equal(540, s.StartSec); Assert.Equal(960, s.EndSec); Assert.Equal(300, s.Severity);
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
        Assert.Equal("평균 105초 → 410초", s.Detail);
    }

    [Fact]
    public void Bottleneck_Picks_Max_Wait_Window()
    {
        var s = Find(Ev(1000, "wait", "M1", "C1", 10), Ev(3000, "wait", "M1", "C2", 100), Ev(3100, "wait", "M1", "C2", 100), Ev(3200, "wait", "M1", "C1", 100))
            .Single(c => c.Kind == SceneKind.Bottleneck);
        Assert.True(s.StartSec <= 3000 && s.EndSec > 3200);
        Assert.Equal(300, s.Severity);
        Assert.Equal("병목: C2", s.Title);
        Assert.Equal((40, 20), (s.CenterX, s.CenterY));
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
