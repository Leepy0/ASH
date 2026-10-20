using ASH.Core.Asy;
using ASH.Core.Export;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using ASH.Tests.Layout;

namespace ASH.Tests.Export;

public class LayoutExporterTests
{
    [Fact] public void Line_Path_Maps_To_Single_GPATH()
    {
        var s = LayoutExporter.ToAgvs(L.Of(L.Line("a",0,0,10,0)), new ExportOptions());
        var p = Assert.Single(s.Paths); Assert.Equal("a", p.Name); Assert.Equal(new LinePiece(new(0,0), new(10,0)), p.Piece);
        Assert.Equal(new[]{"Meters","Seconds"}, s.UnitsTokens); Assert.Equal("12.6.1.19", s.Version);
    }
    [Fact] public void Ccw_Arc_Positive_Angle()
    {
        var arc = new LayoutPath("r"){ Segments = { new ArcSeg(new(0,0), new(1,0), 90) } };
        var piece = Assert.IsType<ArcPiece>(LayoutExporter.ToAgvs(L.Of(arc), new()).Paths[0].Piece); Assert.Equal(900, piece.AngleTenths);
    }
    [Fact] public void Export_ReversedArc_FlipsAngleSign()
    {
        var arc = new LayoutPath("r"){ Segments = { new ArcSeg(new(0,0), new(1,0), 90).Reversed() } };
        var piece = Assert.IsType<ArcPiece>(LayoutExporter.ToAgvs(L.Of(arc), new()).Paths[0].Piece);
        Assert.Equal(-900, piece.AngleTenths); Assert.Equal(0, piece.Beg.X, 9); Assert.Equal(1, piece.Beg.Y, 9);
    }
    [Fact] public void MultiSegment_Path_Gets_Suffixed_Pieces()
    {
        var p = new LayoutPath("m"){ Segments = { new LineSeg(new(0,0), new(5,0)), new LineSeg(new(5,0), new(5,5)) } };
        Assert.Equal(new[]{"m_s1","m_s2"}, LayoutExporter.ToAgvs(L.Of(p), new()).Paths.Select(x => x.Name));
    }
    [Fact] public void Origin_And_Scale_Applied()
    {
        var s = LayoutExporter.ToAgvs(L.Of(L.Line("a",1000,2000,2000,2000)), new ExportOptions(Origin: new(1000,2000), Scale: 0.001));
        Assert.Equal(new LinePiece(new(0,0), new(1,0)), s.Paths[0].Piece);
    }
    [Fact] public void Cp_Projects_To_Path_And_Distance()
    {
        var l = L.Of(L.Line("a",0,0,10,0)); l.Cps.Add(L.Cp("T001", 4, 0.01));
        var c = Assert.Single(LayoutExporter.ToAgvs(l, new()).ControlPoints); Assert.Equal("a", c.PathName); Assert.Equal(4, c.Dist, 6); Assert.Equal("DefaultControlPoint", c.Type);
    }
    [Fact] public void Standby_Cps_Make_Group_NameList_And_Vehicle()
    {
        var l = L.Of(L.Line("a",0,0,10,0)); l.Cps.Add(L.Cp("S001", 2, 0, "standby", group: "G1"));
        var s = LayoutExporter.ToAgvs(l, new ExportOptions(Groups: new[]{ new VehicleGroupSpec("G1","Tug",5,1.2,2.0,1.0) }));
        Assert.Equal(new[]{"S001"}, s.NameLists["nl_G1"]);
        Assert.StartsWith("AGVSVEH type Tug numveh 5", s.VehicleLines[0]); Assert.Contains(s.VehicleLines, x => x.Trim() == "start nl_G1");
        Assert.StartsWith("AGVVEHSEG name Seg_Tug", s.VehicleSegmentLines[0]);
    }
    [Fact] public void Diff_Counts_Added_Removed_Moved()
    {
        var prev = LayoutExporter.ToAgvs(L.Of(L.Line("a",0,0,10,0), L.Line("b",0,0,0,10)), new());
        var cur  = LayoutExporter.ToAgvs(L.Of(L.Line("a",0,0,11,0), L.Line("c",5,5,6,6)), new());
        Assert.Equal(new ExportSummary(1,1,1,0,0,0), ExportDiff.Compare(prev, cur));
    }
    [Fact] public void Runner_Backs_Up_And_Writes_Traffic_Tsv()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash_exp_" + Guid.NewGuid().ToString("N"), "m.arc"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "agv.asy"), "VERSION 12.6.1.19\nSYSTYPE AGVS\nUNITS Meters Seconds\n");
        var r = ExportRunner.Run(L.Of(L.Line("a",0,0,10,0)), new(), dir);
        Assert.NotNull(r.BackupPath); Assert.Matches(@"agv\.asy\.bak\.\d{8}_\d{6}$", r.BackupPath!); Assert.True(File.Exists(r.TrafficTsvPath));
        Assert.Contains("GPATH name a ", File.ReadAllText(r.AsyPath));
    }
}
