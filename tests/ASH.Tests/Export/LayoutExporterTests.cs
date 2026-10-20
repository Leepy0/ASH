using ASH.Core.Asy;
using ASH.Core.Export;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using ASH.Tests.Layout;

namespace ASH.Tests.Export;

public class LayoutExporterTests
{
    internal static readonly ExportOptions G = new(Groups: new[]{ new VehicleGroupSpec("G1","Tug",1,1.0,2,1) });

    /// <summary>Layout with a G1 standby CP at the first path's start (every group needs one).</summary>
    internal static ASH.Core.Layout.Layout Of(params LayoutPath[] paths)
    {
        var l = L.Of(paths); l.Cps.Add(L.Cp("S1", paths[0].Start.X, paths[0].Start.Y, "standby", "G1")); return l;
    }

    [Fact] public void Line_Path_Maps_To_Single_GPATH()
    {
        var s = LayoutExporter.ToAgvs(Of(L.Line("a",0,0,10,0)), G);
        var p = Assert.Single(s.Paths); Assert.Equal("a", p.Name); Assert.Equal(new LinePiece(new(0,0), new(10,0)), p.Piece);
        Assert.Equal(new[]{"Meters","Seconds"}, s.UnitsTokens); Assert.Equal("12.6.1.19", s.Version);
    }
    [Fact] public void Ccw_Arc_Positive_Angle()
    {
        var arc = new LayoutPath("r"){ Segments = { new ArcSeg(new(0,0), new(1,0), 90) } };
        var piece = Assert.IsType<ArcPiece>(LayoutExporter.ToAgvs(Of(arc), G).Paths[0].Piece); Assert.Equal(900, piece.AngleTenths);
    }
    [Fact] public void Export_ReversedArc_FlipsAngleSign()
    {
        var arc = new LayoutPath("r"){ Segments = { new ArcSeg(new(0,0), new(1,0), 90).Reversed() } };
        var piece = Assert.IsType<ArcPiece>(LayoutExporter.ToAgvs(Of(arc), G).Paths[0].Piece);
        Assert.Equal(-900, piece.AngleTenths); Assert.Equal(0, piece.Beg.X, 9); Assert.Equal(1, piece.Beg.Y, 9);
    }
    [Fact] public void MultiSegment_Path_Gets_Suffixed_Pieces()
    {
        var p = new LayoutPath("m"){ Segments = { new LineSeg(new(0,0), new(5,0)), new LineSeg(new(5,0), new(5,5)) } };
        Assert.Equal(new[]{"m_s1","m_s2"}, LayoutExporter.ToAgvs(Of(p), G).Paths.Select(x => x.Name));
    }
    [Fact] public void Segment_Suffix_Skips_Existing_Path_Name()
    {
        var p = new LayoutPath("m"){ Segments = { new LineSeg(new(0,0), new(5,0)), new LineSeg(new(5,0), new(5,5)) } };
        var names = LayoutExporter.ToAgvs(Of(p, L.Line("m_s1",20,0,30,0)), G).Paths.Select(x => x.Name).ToList();
        Assert.Equal(new[]{"m_s2","m_s3","m_s1"}, names);
    }
    [Fact] public void Origin_And_Scale_Applied()
    {
        var s = LayoutExporter.ToAgvs(Of(L.Line("a",1000,2000,2000,2000)), G with { Origin = new(1000,2000), Scale = 0.001 });
        Assert.Equal(new LinePiece(new(0,0), new(1,0)), s.Paths[0].Piece);
    }
    [Fact] public void Cp_Projects_To_Path_And_Distance()
    {
        var l = Of(L.Line("a",0,0,10,0)); l.Cps.Add(L.Cp("T001", 4, 0.01));
        var c = LayoutExporter.ToAgvs(l, G).ControlPoints.Single(x => x.Name == "T001"); Assert.Equal("a", c.PathName); Assert.Equal(4, c.Dist, 6); Assert.Equal("DefaultControlPoint", c.Type);
    }
    [Fact] public void Standby_Cps_Make_Group_NameList_And_Vehicle()
    {
        var l = L.Of(L.Line("a",0,0,10,0)); l.Cps.Add(L.Cp("S001", 2, 0, "standby", group: "G1"));
        var s = LayoutExporter.ToAgvs(l, new ExportOptions(Groups: new[]{ new VehicleGroupSpec("G1","Tug",5,1.2,2.0,1.0) }));
        Assert.Equal(new[]{"S001"}, s.NameLists["nl_G1"]);
        Assert.StartsWith("AGVSVEH type Tug numveh 5", s.VehicleLines[0]); Assert.Contains(s.VehicleLines, x => x.Trim() == "start nl_G1");
        Assert.StartsWith("AGVVEHSEG name Seg_Tug", s.VehicleSegmentLines[0]);
    }
    [Fact] public void Group_Without_Standby_Cps_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => LayoutExporter.ToAgvs(L.Of(L.Line("a",0,0,10,0)), new ExportOptions(Groups: new[]{ new VehicleGroupSpec("G1","Tug",5,1.2,2.0,1.0) })));
        Assert.Contains("group G1 has no standby/buffer CP", ex.Message);
    }
    [Fact] public void Missing_Groups_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => LayoutExporter.ToAgvs(Of(L.Line("a",0,0,10,0)), new ExportOptions()));
        Assert.Equal("ExportOptions.Groups is required", ex.Message);
    }
    [Fact] public void Meters_Export_Keeps_Template_Feet_Lines_But_Speed_In_Meters()
    {
        var l = L.Of(L.Line("a",0,0,10,0)); l.Cps.Add(L.Cp("S001", 2, 0, "standby", group: "G1"));
        var s = LayoutExporter.ToAgvs(l, new ExportOptions(Groups: new[]{ new VehicleGroupSpec("G1","Tug",5,1.2,2.0,1.0) }));
        var w = new StringWriter(); AsyWriter.Write(s, w); var lines = w.ToString().Split('\n');
        Assert.Contains(lines, x => x.Trim().StartsWith("brakedist") && x.EndsWith("Feet"));
        Assert.Contains(lines, x => x.Trim() == "vel\t0 1.2 Meters Seconds");
        Assert.Contains(lines, x => x.Trim() == "template Feet");
        Assert.DoesNotContain(lines, x => x.Contains(" 3 Meters"));
        Assert.Contains("UNITS Meters Seconds", lines);
        Assert.Contains(lines, x => x.StartsWith("GPATHTYPE ") && x.Contains("vel 1 1.2 Meters Seconds"));
        Assert.Contains(lines, x => x.StartsWith("CPOINTTYPE ") && x.Contains("distance 0 Meters"));
    }
    [Fact] public void Cap2_Cp_Gets_Own_Cp_Type()
    {
        var l = Of(L.Line("a",0,0,10,0)); var b = L.Cp("buf1", 5, 0, "buffer", "G1"); b.Cap = 2; l.Cps.Add(b);
        var s = LayoutExporter.ToAgvs(l, G);
        Assert.Contains(s.CpTypeLines, x => x.StartsWith("CPOINTTYPE name DefaultControlPoint_cap2 cap 2 "));
        Assert.Contains(s.CpTypeLines, x => x.StartsWith("CPOINTTYPE name DefaultControlPoint cap 1 "));
        Assert.StartsWith("CPOINT name buf1 type DefaultControlPoint_cap2 ", AsyWriter.FormatCp(s.ControlPoints.Single(c => c.Name == "buf1")));
        Assert.Equal("DefaultControlPoint", s.ControlPoints.Single(c => c.Name == "S1").Type);
    }
    [Fact] public void Non_Tenth_Arc_Angle_Warns()
    {
        var w = new List<string>();
        LayoutExporter.ToAgvs(Of(new LayoutPath("r"){ Segments = { new ArcSeg(new(0,0), new(5,0), 37.23) } }), G, w);
        Assert.Single(w, x => x.StartsWith("r:"));
        w.Clear();
        LayoutExporter.ToAgvs(Of(new LayoutPath("r"){ Segments = { new ArcSeg(new(0,0), new(5,0), 90) } }), G, w);
        Assert.Empty(w);
    }
    [Fact] public void Zero_Length_Segment_Skipped_With_Warning()
    {
        var p = new LayoutPath("m"){ Segments = { new LineSeg(new(0,0), new(5,0)), new LineSeg(new(5,0), new(5,0)), new LineSeg(new(5,0), new(5,5)) } };
        var w = new List<string>();
        var s = LayoutExporter.ToAgvs(Of(p), G, w);
        Assert.Equal(new[]{"m_s1","m_s2"}, s.Paths.Select(x => x.Name));
        Assert.Single(w);
    }
    [Theory, InlineData("a b"), InlineData(""), InlineData("Type"), InlineData("at")]
    public void Invalid_Path_Name_Throws(string name) =>
        Assert.Throws<ArgumentException>(() => LayoutExporter.ToAgvs(Of(L.Line(name,0,0,10,0)), G));
    [Fact] public void Invalid_Cp_Name_Throws()
    {
        var l = Of(L.Line("a",0,0,10,0)); l.Cps.Add(L.Cp("cap", 3, 0));
        Assert.Throws<ArgumentException>(() => LayoutExporter.ToAgvs(l, G));
    }
    [Fact] public void Tiny_Coordinates_Are_Written_Without_Exponent()
    {
        Assert.Equal("0", AsyWriter.Num(6.123233995736766E-16));
        Assert.Equal("0", AsyWriter.Num(-1e-17));
        Assert.Equal("1.10268372703412", AsyWriter.Num(1.10268372703412));
        var arc = new LayoutPath("r"){ Segments = { new ArcSeg(new(0,0), new(Math.Cos(Math.PI / 2) * 5, 5), 90) } };
        Assert.DoesNotContain("E", AsyWriter.FormatPath(LayoutExporter.ToAgvs(Of(arc), G).Paths[0]));
    }
    [Fact] public void Diff_Counts_Added_Removed_Moved()
    {
        var prev = LayoutExporter.ToAgvs(Of(L.Line("a",0,0,10,0), L.Line("b",0,0,0,10)), G);
        var cur  = LayoutExporter.ToAgvs(Of(L.Line("a",0,0,11,0), L.Line("c",5,5,6,6)), G);
        Assert.Equal(new ExportSummary(1,1,1,0,0,0), ExportDiff.Compare(prev, cur));
    }
    [Fact] public void Runner_Backs_Up_And_Writes_Traffic_Tsv()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash_exp_" + Guid.NewGuid().ToString("N"), "m.arc"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "agv.asy"), "VERSION 12.6.1.19\nSYSTYPE AGVS\nUNITS Meters Seconds\n");
        var r = ExportRunner.Run(Of(L.Line("a",0,0,10,0)), G, dir);
        Assert.NotNull(r.BackupPath); Assert.Matches(@"agv\.asy\.bak\.\d{8}_\d{6}$", r.BackupPath!); Assert.True(File.Exists(r.TrafficTsvPath));
        Assert.Contains("GPATH name a ", File.ReadAllText(r.AsyPath));
    }
}
