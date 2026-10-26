using ASH.Core.Traffic;

namespace ASH.Tests.Traffic;

public class CpTypesTests
{
    [Fact] public void PaletteOrder_Is_A_Permutation_Of_All() =>
        Assert.Equal(CpTypes.All.OrderBy(t => t), CpTypes.PaletteOrder.OrderBy(t => t));

    [Fact] public void PaletteOrder_Pairs_Per_Row() =>
        Assert.Equal(new[] { "standby", "finish", "load", "unload", "rtnload", "rtnunload", "buffer", "traffic" }, CpTypes.PaletteOrder);

    [Fact] public void All_Keeps_Its_Order_For_Export_And_Numbering() =>
        Assert.Equal(new[] { "traffic", "load", "unload", "rtnload", "rtnunload", "standby", "buffer", "finish" }, CpTypes.All);

    [Fact] public void Every_Type_Has_A_Korean_Name() =>
        Assert.All(CpTypes.All, t => Assert.NotEqual(t, CpTypes.Korean(t)));
}
