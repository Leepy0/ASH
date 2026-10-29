using System.Collections.Generic;
using ASH.Core.Input;
using Xunit;

namespace ASH.Tests.Input;

public class CpListTests
{
    [Theory]
    [InlineData("L001", "L001")]
    [InlineData("L001-L003,L007", "L001|L002|L003|L007")]
    [InlineData("L008-L010", "L008|L009|L010")]
    [InlineData("L9-L11", "L9|L10|L11")]
    [InlineData("A1-B2", "A1-B2")]
    [InlineData("L001-1", "L001-1")]
    [InlineData("", "")]
    [InlineData("L001,", "L001")]
    [InlineData(" L001 , L003 ", "L001|L003")]
    public void Parse_expands_ranges_and_keeps_other_names(string cell, string expected)
    {
        var e = new List<string>(); var d = new List<string>();
        Assert.Equal(expected, string.Join("|", CpList.Parse(cell, e, d)));
        Assert.Empty(e); Assert.Empty(d);
    }

    [Fact] public void Null_cell_gives_empty_list()
    { var e = new List<string>(); Assert.Empty(CpList.Parse(null, e, new List<string>())); Assert.Empty(e); }

    [Fact] public void Reversed_range_is_an_error()
    { var e = new List<string>(); Assert.Empty(CpList.Parse("L010-L001", e, new List<string>())); Assert.Equal(new[] { "L010-L001" }, e); }

    [Fact] public void Range_over_1000_names_is_an_error()
    {
        var e = new List<string>();
        Assert.Empty(CpList.Parse("L0001-L1001", e, new List<string>()));
        Assert.Equal(new[] { "L0001-L1001" }, e);
        // exactly 1000 names is still fine
        Assert.Equal(1000, CpList.Parse("L0001-L1000", new List<string>(), new List<string>()).Count);
    }

    [Fact] public void Duplicates_are_dropped_and_reported()
    { var d = new List<string>(); Assert.Equal(new[] { "L001", "L002" }, CpList.Parse("L001, L001 ,L002", new List<string>(), d)); Assert.Equal(new[] { "L001" }, d); }

    [Fact] public void Duplicate_inside_a_range_is_reported()
    { var d = new List<string>(); Assert.Equal(new[] { "L001", "L002", "L003" }, CpList.Parse("L001,L001-L003", new List<string>(), d)); Assert.Equal(new[] { "L001" }, d); }
}
