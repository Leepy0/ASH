using System;
using ASH.Core.Traffic;
using Xunit;

namespace ASH.Tests.Traffic;

public class AutoNamerTests
{
    [Fact] public void First_Name_Is_001() => Assert.Equal("T001", AutoNamer.Next("T", Array.Empty<string>()));
    [Fact] public void NextName_IgnoresNonNumericSuffix() => Assert.Equal("T011", AutoNamer.Next("T", new[]{"T9","T010","Tx","t3","B002"}));
    [Fact] public void Wide_Numbers_Keep_Width() => Assert.Equal("T1000", AutoNamer.Next("T", new[]{"T999"}));
    [Fact] public void Prefix_Per_Type() { Assert.Equal("RL", AutoNamer.PrefixFor("rtnload")); Assert.Equal("S", AutoNamer.PrefixFor("standby")); Assert.Equal("B", AutoNamer.PrefixFor("buffer")); }
}
