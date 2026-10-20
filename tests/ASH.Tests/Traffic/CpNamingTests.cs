using ASH.Core.Traffic;
using Xunit;

namespace ASH.Tests.Traffic;

public class CpNamingTests
{
    [Fact] public void Counters_Are_Per_Prefix() { var n = new CpNaming(new[]{"T003","L001"}); Assert.Equal("T004", n.Next("T")); Assert.Equal("T005", n.Next("T")); Assert.Equal("L002", n.Next("L")); Assert.Equal("S001", n.Next("S")); }
}
