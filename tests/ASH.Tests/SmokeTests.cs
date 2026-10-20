using ASH.Core.Geometry;

namespace ASH.Tests;

public class SmokeTests
{
    [Fact]
    public void CoreAssemblyLoads() => Assert.NotNull(typeof(Point2).Assembly);
}
