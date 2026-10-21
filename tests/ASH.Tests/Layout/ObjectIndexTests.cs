using System;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using ASH.Core.Traffic;

namespace ASH.Tests.Layout;

/// <summary>The palette finder: what is listed, the categories it can be filtered by, and the text filter.</summary>
public class ObjectIndexTests
{
    static ASH.Core.Layout.Layout Sample()
    {
        var one = L.Line("P001", 0, 0, 10, 0);
        var two = L.Line("P002", 0, 5, 10, 5); two.IsTwoWay = true;
        var spur = L.Spur("P003", 0, 9, 4, 9);
        var crabTwo = L.Line("P004", 0, 12, 10, 12); crabTwo.IsTwoWay = true; crabTwo.IsCrab = true;
        var l = L.Of(one, two, spur, crabTwo);
        l.Cps.Add(L.Cp("T001", 1, 0, "traffic")); l.Cps[0].Id = "A1";
        l.Cps.Add(L.Cp("L001", 2, 0, "load", "G1")); l.Cps[1].Cap = CpTypes.Infinity; l.Cps[1].Id = "A2";
        l.Cps.Add(L.Cp("L002", 3, 0, "load", "G2"));
        var b = L.Block("B001", 0, 0, 4, 2); b.Id = "B1";
        l.Blocks.Add(b);
        l.Stations.Add(new LayoutStation { Name = "ST1", Position = new Point2(7, 7) });
        return l;
    }

    static string[] Names(string category, string? text = null) =>
        ObjectIndex.Build(Sample(), 1).Where(o => ObjectIndex.Matches(o, category, text)).Select(o => o.Name).OrderBy(n => n).ToArray();

    [Fact] public void Every_object_of_the_drawing_is_listed_once()
        => Assert.Equal(new[] { "B001", "L001", "L002", "P001", "P002", "P003", "P004", "ST1", "T001" }, Names(ObjectIndex.All));

    [Fact] public void Paths_filter_by_type()
    {
        Assert.Equal(new[] { "P001", "P002", "P003", "P004" }, Names(ObjectIndex.AllPaths));
        Assert.Equal(new[] { "P001" }, Names(ObjectIndex.PathTag("단방향")));
        Assert.Equal(new[] { "P002", "P004" }, Names(ObjectIndex.PathTag("양방향")));
        Assert.Equal(new[] { "P003" }, Names(ObjectIndex.PathTag("지선")));
        Assert.Equal(new[] { "P004" }, Names(ObjectIndex.PathTag("크랩")));   // a combined path is found under each of its parts
    }

    [Fact] public void Cps_filter_by_type()
    {
        Assert.Equal(new[] { "L001", "L002", "T001" }, Names(ObjectIndex.AllCps));
        Assert.Equal(new[] { "L001", "L002" }, Names(ObjectIndex.CpTag("load")));
        Assert.Equal(new[] { "T001" }, Names(ObjectIndex.CpTag("traffic")));
        Assert.Empty(Names(ObjectIndex.CpTag("buffer")));
    }

    [Fact] public void Blocks_and_stations_have_their_own_category()
    {
        Assert.Equal(new[] { "B001" }, Names(ObjectIndex.Blocks));
        Assert.Equal(new[] { "ST1" }, Names(ObjectIndex.Stations));
    }

    [Fact] public void Text_filter_looks_at_name_and_info_and_ignores_case()
    {
        Assert.Equal(new[] { "L001" }, Names(ObjectIndex.AllCps, "inf"));          // CAP INF in the info
        Assert.Equal(new[] { "L002" }, Names(ObjectIndex.AllCps, "g2"));          // group in the info
        Assert.Equal(new[] { "P002", "P004" }, Names(ObjectIndex.AllPaths, "양방향"));
        Assert.Equal(new[] { "T001" }, Names(ObjectIndex.All, " t001 "));
    }

    [Fact] public void Each_category_is_in_the_list_and_nothing_is_tagged_with_an_unknown_one()
    {
        foreach (var o in ObjectIndex.Build(Sample(), 1))
            foreach (var t in o.Tags) Assert.Contains(t, ObjectIndex.Categories);
        Assert.Equal(ObjectIndex.Categories.Length, ObjectIndex.Categories.Distinct().Count());
        Assert.Contains(ObjectIndex.CpTag("rtnunload"), ObjectIndex.Categories);
    }

    [Fact] public void Where_to_pan_and_the_handle_are_carried()
    {
        var all = ObjectIndex.Build(Sample(), 1);
        var p = all.Single(o => o.Name == "P001");
        Assert.Equal(new Point2(5, 0), p.At);                    // middle of the path
        Assert.Equal(p.Handle, "" + p.Handle);
        var cp = all.Single(o => o.Name == "L001");
        Assert.Equal((new Point2(2, 0), "A2"), (cp.At, cp.Handle));
        Assert.Equal(new Point2(2, 1), all.Single(o => o.Name == "B001").At);   // centre of the block
        Assert.Null(all.Single(o => o.Name == "ST1").Handle);
        Assert.Null(all.Single(o => o.Name == "L002").Handle);   // no handle known: not selectable
    }

    [Fact] public void Info_shows_type_length_group_and_capacity()
    {
        var all = ObjectIndex.Build(Sample(), 1000);                 // a mm drawing: 10 units = 0.01 m
        Assert.Equal("단방향 · 0.01 m", all.Single(o => o.Name == "P001").Info);
        Assert.Equal("양방향+크랩 · 0.01 m", all.Single(o => o.Name == "P004").Info);
        Assert.Equal("load · G1 · CAP INF", all.Single(o => o.Name == "L001").Info);
        Assert.Equal("traffic · - · CAP 1", all.Single(o => o.Name == "T001").Info);
    }

    [Fact] public void Every_finding_code_has_a_korean_label()
    {
        foreach (var code in Enum.GetValues(typeof(FindingCode)).Cast<FindingCode>())
        {
            var label = FindingText.Label(code);
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.NotEqual(code.ToString(), label);
        }
    }
}
