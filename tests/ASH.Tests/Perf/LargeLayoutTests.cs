using System;
using System.Diagnostics;
using ASH.Core.Export;
using ASH.Core.Layout;
using ASH.Core.Traffic;
using ASH.Tests.Layout;
using Xunit;

namespace ASH.Tests.Perf;

public class LargeLayoutTests
{
    static ASH.Core.Layout.Layout Grid(int n /*n×n 격자, 가로·세로 통로*/)
    {
        var l = new ASH.Core.Layout.Layout();
        for (int i = 0; i <= n; i++) { l.Paths.Add(L.Line("h" + i, 0, i * 10, n * 10, i * 10)); l.Paths.Add(L.Line("v" + i, i * 10, 0, i * 10, n * 10)); }
        return l;
    }

    [Fact] public void Split_Check_Export_On_40x40_Grid_Under_5s()
    {
        var sw = Stopwatch.StartNew();
        var l = Grid(40);
        l.Paths = PathSplitter.SplitAll(l.Paths, 0.001).Paths;
        var blocks = BlockGenerator.Generate(l, new(2, 1)); l.Blocks.AddRange(blocks.Blocks);
        l.Cps.AddRange(TrafficCpGenerator.Generate(l, new(2, 1)).Cps);
        var findings = LayoutChecker.Check(l, new());
        var sys = LayoutExporter.ToAgvs(l, new());
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), sw.Elapsed.ToString());
        Assert.True(l.Cps.Count > 1000); Assert.Equal(l.Paths.Count, sys.Paths.Count);
    }
}
