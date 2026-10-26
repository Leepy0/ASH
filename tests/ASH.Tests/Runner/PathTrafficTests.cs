using System;
using System.IO;
using ASH.Core.Runner;
using Xunit;

namespace ASH.Tests.Runner;

public class PathTrafficTests
{
    static string Fx(string n) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "runner", n);

    [Fact]
    public void Report_Gives_Each_Paths_Total_Of_Vehicles()
    {
        // Guide Path Statistics: Name Total ... (the type table above it is not a path)
        var t = ResultParser.ParseReport(Fx("ashrun.report")).PathTotals!;
        Assert.Equal(9, t["p1"]); Assert.Equal(6, t["p2"]); Assert.Equal(6, t["p3"]); Assert.Equal(8, t["p4"]);
        Assert.Equal(4, t.Count); Assert.False(t.ContainsKey("DefaultGuidePath"));
    }
}
