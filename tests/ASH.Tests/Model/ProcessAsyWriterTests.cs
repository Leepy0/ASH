using ASH.Core.Geometry;
using ASH.Core.Model;

namespace ASH.Tests.Model;

public class ProcessAsyWriterTests
{
    static string Render(ProcessModel m) { var w = new StringWriter(); ProcessAsyWriter.Write(m, w); return w.ToString(); }

    [Fact]
    public void Writes_Demo_Like_Entity_Lines()
    {
        var m = new ProcessModel { LogicSource = "x" };
        m.Procs.Add(new("Pinit")); m.LoadTypes.Add("Linit"); m.Queues.Add(new("Qtemp")); m.Orders.Add("OLforever");
        m.Tables.Add("Tthroughput"); m.Atts.Add(new("Alane", "Integer"));
        m.Vars.Add(new("Vn", "Integer", 0, "5")); m.Vars.Add(new("Vdone", "Integer", 1000)); m.Vars.Add(new("Vr", "Real"));
        m.RnStreams.Add(new("stream0", 12345)); m.Funcs.Add("Fincveh");
        var lines = Render(m).Split('\n');
        foreach (var expect in new[] {
            "PROCDEF", "PROC name Pinit 0 traf Infinite nextproc die", "LDTYPE name Linit 0", "QUEUE name Qtemp 0 cap 2147483647",
            "ORDER name OLforever 0", "TABLE name Tthroughput 0 bins 1 0 1", "ATT name Alane 0 type Integer",
            "VAR name Vn 0 type Integer 5", "VAR name Vdone 1 1000 type Integer", "VAR name Vr 0 type Real",
            "RNSTREAM stream0 0 type CMRG flags 1", "\tcmrgseed 1 12345 12345 12345 12345 12345 12345",
            "FUNC name Fincveh type Integer", "SFileBegin\tname code.m" })
            Assert.Contains(expect, lines);
        Assert.StartsWith("VERSION 12.6.1.19\nSYSTYPE Process\n", Render(m));
    }

    [Fact]
    public void Block_Rect_Maps_To_Center_And_Scale()
    {
        var m = new ProcessModel();
        m.Blocks.Add(new("B001", 1, new Rect2(new Point2(0, 0), new Point2(4, 2))));
        var t = Render(m);
        Assert.Contains("BLOCK name B001 0 cap 1\n dis 0 picpos begx 2 begy 1 endx 3 endy 1 scx 1 scy 0.5 scz 0.25\n\n\tUserDef\ttemplate Feet\n310 0\n4 4 4 4 4 0 0\nend\n", t);
    }

    [Fact]
    public void Ends_With_Code_Marker()
    {
        var m = new ProcessModel { LogicSource = "begin a function\nend\n" };
        Assert.EndsWith("SFileBegin\tname code.m\nbegin a function\nend\n\n#@!\n", Render(m));
    }

    [Fact]
    public void Writer_Uses_Model_Units()
    {
        var m = new ProcessModel { Units = "Meters" };
        m.LoadTypes.Add("L1"); m.Labels.Add("Lx"); m.Blocks.Add(new("B1", 1, new Rect2(new Point2(0, 0), new Point2(1, 1))));
        var t = Render(m);
        Assert.Contains("UNITS Meters Seconds", t);
        Assert.DoesNotContain("Feet", t);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(t, "template Meters").Count);
    }

    [Fact]
    public void Block_Rect_Normalized_And_Degenerate_Rejected()
    {
        var m = new ProcessModel();
        m.Blocks.Add(new("B1", 1, new Rect2(new Point2(4, 2), new Point2(0, 0))));
        Assert.Contains("begx 2 begy 1 endx 3 endy 1 scx 1 scy 0.5", Render(m));
        var z = new ProcessModel(); z.Blocks.Add(new("B2", 1, new Rect2(new Point2(0, 0), new Point2(0, 2))));
        Assert.Throws<ArgumentException>(() => Render(z));
    }

    [Fact]
    public void Invalid_Or_Duplicate_Names_Throw()
    {
        var a = new ProcessModel(); a.Procs.Add(new("bad name"));
        Assert.Throws<ArgumentException>(() => Render(a));
        var b = new ProcessModel(); b.Procs.Add(new("X")); b.Funcs.Add("X");
        Assert.Throws<ArgumentException>(() => Render(b));
    }

    [Fact]
    public void Sections_Are_Ordered()
    {
        var m = new ProcessModel { LogicSource = "x" };
        m.Procs.Add(new("P")); m.Funcs.Add("F");
        var t = Render(m);
        Assert.True(t.IndexOf("SYSTYPE") < t.IndexOf("PROCDEF") && t.IndexOf("PROCDEF") < t.IndexOf("PROC name P")
            && t.IndexOf("PROC name P") < t.IndexOf("FUNC name F") && t.IndexOf("FUNC name F") < t.IndexOf("SFileBegin"));
    }
}
