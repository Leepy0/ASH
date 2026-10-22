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
        var ex = Assert.Throws<ExportException>(() => LayoutExporter.ToAgvs(L.Of(L.Line("a",0,0,10,0)), new ExportOptions(Groups: new[]{ new VehicleGroupSpec("G1","Tug",5,1.2,2.0,1.0) })));
        Assert.Contains("group G1 has no standby/buffer CP", ex.Message); Assert.Equal("G1", ex.Group);
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
        Assert.NotNull(r.BackupPath); Assert.Matches(@"agv\.asy\.bak\.\d{8}_\d{6}_\d{3}(_\d+)?$", r.BackupPath!); Assert.True(File.Exists(r.TrafficTsvPath));
        Assert.Contains("GPATH name a ", File.ReadAllText(r.AsyPath));
    }
    [Fact] public void Runner_Writes_Blocks_Tsv()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash_exp_" + Guid.NewGuid().ToString("N"), "m.arc");
        var l = Of(L.Line("a", 1000, 2000, 5000, 2000));
        l.Blocks.Add(new LayoutBlock { Name = "B1", Cap = 2, Rect = new Rect2(new Point2(1500, 1500), new Point2(3000, 2500)) });
        var r = ExportRunner.Run(l, G with { Origin = new(1000, 2000), Scale = 0.001 }, dir);
        Assert.Equal(Path.Combine(dir, "blocks.tsv"), r.BlocksTsvPath);
        Assert.Equal("name\tcap\tminx\tminy\tmaxx\tmaxy\nB1\t2\t0.5\t-0.5\t2\t0.5\n", File.ReadAllText(r.BlocksTsvPath));
    }
    const string Hdr = "VERSION 12.6.1.19\nSYSTYPE AGVS\nUNITS Meters Seconds\n";
    [Fact] public void Two_Runs_Make_Two_Distinct_Backups()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash_exp_" + Guid.NewGuid().ToString("N"), "m.arc"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "agv.asy"), Hdr);
        var r1 = ExportRunner.Run(Of(L.Line("a",0,0,10,0)), G, dir);
        var r2 = ExportRunner.Run(Of(L.Line("a",0,0,10,0)), G, dir);
        var r3 = ExportRunner.Run(Of(L.Line("a",0,0,10,0)), G, dir);
        Assert.Equal(3, new[]{ r1.BackupPath, r2.BackupPath, r3.BackupPath }.Distinct().Count());
        Assert.Equal(Hdr, File.ReadAllText(r1.BackupPath!));
        Assert.Equal(3, Directory.GetFiles(dir, "agv.asy.bak.*").Length);
    }
    [Fact] public void Diff_Tolerates_Duplicate_Names_In_Previous_File()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash_exp_" + Guid.NewGuid().ToString("N"), "m.arc"); Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "agv.asy"), new[]{ Hdr.TrimEnd(),
            "GPATH name a type DefaultGuidePath piece begx 0 begy 0 endx 10 endy 0 upz 1",
            "GPATH name a type DefaultGuidePath piece begx 0 begy 0 endx 5 endy 0 upz 1",
            "CPOINT name S1 type DefaultControlPoint at a 0", "CPOINT name S1 type DefaultControlPoint at a 1" });
        var r = ExportRunner.Run(Of(L.Line("a",0,0,10,0)), G, dir);
        Assert.Equal(new ExportSummary(0,0,0,0,0,0), r.Summary);
    }
    [Fact] public void Failed_Export_Leaves_Files_Untouched()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash_exp_" + Guid.NewGuid().ToString("N"), "m.arc"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "agv.asy"), "old");
        Assert.Throws<ExportException>(() => ExportRunner.Run(L.Of(L.Line("a",0,0,10,0)), G, dir));
        Assert.Equal(new[]{ "agv.asy" }, Directory.GetFiles(dir).Select(Path.GetFileName));
        Assert.Equal("old", File.ReadAllText(Path.Combine(dir, "agv.asy")));
    }
    static string[] Lines(ExportOptions o)
    {
        var l = L.Of(L.Line("a",0,0,10,0)); l.Cps.Add(L.Cp("S001", 2, 0, "standby", group: "G1"));
        var w = new StringWriter(); AsyWriter.Write(LayoutExporter.ToAgvs(l, o), w); return w.ToString().Split('\n');
    }
    [Fact] public void Export_Writes_OldBlockClaim_By_Default() =>
        Assert.Contains(Lines(G), x => x.Trim() == "ALTERNATE NONE ignore OldBlockClaim ResumeSpeedWhenClaimed");
    [Fact] public void Export_Without_ClaimBlockOnNextCp_Omits_OldBlockClaim() =>
        Assert.Contains(Lines(G with { ClaimBlockOnNextCp = false }), x => x.Trim() == "ALTERNATE NONE ignore ResumeSpeedWhenClaimed");
    [Fact] public void Vehicle_Rotate_Rate_Comes_From_Spec() =>
        Assert.Contains(Lines(new ExportOptions(Groups: new[]{ new VehicleGroupSpec("G1","Tug",1,1.0,2,1, 45) })), x => x.Trim() == "rotate\t0 45 Seconds");
    [Fact] public void Empty_Load_Rotate_Line_Stays_Inherit_Form() =>
        Assert.Contains(Lines(new ExportOptions(Groups: new[]{ new VehicleGroupSpec("G1","Tug",1,1.0,2,1, 45) })), x => x.Trim() == "rotate	1 0 Seconds");
    [Fact] public void Zero_Rotate_Rate_Throws() =>
        Assert.Throws<ArgumentException>(() => Lines(new ExportOptions(Groups: new[]{ new VehicleGroupSpec("G1","Tug",1,1.0,2,1, 0) })));
    [Fact] public void Vehicle_Accel_And_Decel_Come_From_Spec() =>
        Assert.Equal(new[]{ "accel\t0 0.3 Meters Seconds Seconds", "decel\t0 0.25 Meters Seconds Seconds" },
            Lines(new ExportOptions(Groups: new[]{ new VehicleGroupSpec("G1","Tug",1,1.0,2,1, 30, 0.3, 0.25) })).Select(x => x.Trim())
                .Where(x => x.StartsWith("accel\t0 ") || x.StartsWith("decel\t0 ")).ToArray());
    [Fact] public void Zero_Accel_Keeps_The_Template_Value() =>
        Assert.Contains(Lines(G), x => x.Trim() == "accel\t0 1 Feet Seconds Seconds");
    [Fact] public void Brake_Distance_Is_Stopping_Distance_Plus_Gap_And_Empty_Line_Gets_SpeedEmpty()
    {
        // v = max(1.0, 1.5) m/s, decel 0.25: 1.5^2 / 0.5 = 4.5 m, + 0.5 m gap
        var lines = Lines(new ExportOptions(Groups: new[]{ new VehicleGroupSpec("G1","Tug",1,1.0,2,1, 30, 0.3, 0.25, SpeedEmpty: 1.5) })).Select(x => x.Trim()).ToList();
        Assert.Contains("brakedist\t0 5 Meters", lines);
        Assert.Contains("stopdist\t0 0.5 Meters", lines);
        Assert.Contains("vel\t0 1.5 Meters Seconds", lines);   // the Empty line
        Assert.Contains("vel\t0 1 Meters Seconds", lines);     // the loaded line
        Assert.Contains("brakedist\t1 0 Feet", lines);         // Empty brake/stop distances inherit
        // no decel: the template's distances stay, and SpeedEmpty = Speed keeps the Empty line inheriting
        var plain = Lines(G).Select(x => x.Trim()).ToList();
        Assert.Contains("brakedist\t0 4.5 Feet", plain);
        Assert.Contains("vel\t1 0 Feet Seconds", plain);
    }
    // a mm drawing (Scale 0.001: drawing -> meters) gives the same model in Meters and in Millimeters, only every length is x1000 in Millimeters;
    // the vehicle picture is in feet either way (its own `template Feet`)
    static (List<string> Lines, AgvsSystem Sys, List<string> Warnings) MmDrawing(string units, double scale = 0.001)
    {
        var l = L.Of(L.Line("a",0,0,10000,0)); l.Tolerance = 1; l.Cps.Add(L.Cp("S001", 2000, 0, "standby", group: "G1"));
        var warnings = new List<string>();
        var s = LayoutExporter.ToAgvs(l, new ExportOptions(Units: units, Scale: scale, Groups: new[]{ new VehicleGroupSpec("G1","Tug",1,1.0,1.6,1.0, 30, 0.3, 0.25, "Standard", SpeedEmpty: 1.5) }), warnings);
        var w = new StringWriter(); AsyWriter.Write(s, w);
        return (w.ToString().Split('\n').Select(x => x.Trim()).ToList(), s, warnings);
    }
    [Fact] public void Mm_Drawing_In_Meters_Model()
    {
        var (lines, s, warnings) = MmDrawing("Meters");
        Assert.Equal(new LinePiece(new(0,0), new(10,0)), s.Paths[0].Piece);
        Assert.Equal(2, s.ControlPoints.Single(c => c.Name == "S001").Dist, 9);
        Assert.Contains("vel\t0 1 Meters Seconds", lines); Assert.Contains("vel\t0 1.5 Meters Seconds", lines);
        Assert.Contains("accel\t0 0.3 Meters Seconds Seconds", lines); Assert.Contains("brakedist\t0 5 Meters", lines);
        Assert.Empty(warnings);
    }
    [Fact] public void Mm_Drawing_In_Millimeters_Model_Scales_Every_Length_Not_The_Picture()
    {
        var (mm, s, warnings) = MmDrawing("Millimeters");
        Assert.Equal(new LinePiece(new(0,0), new(10000,0)), s.Paths[0].Piece);                 // paths: 10 m = 10000 mm, not 10 mm
        Assert.Equal(2000, s.ControlPoints.Single(c => c.Name == "S001").Dist, 9);
        Assert.Contains("UNITS Millimeters Seconds", mm);
        Assert.Contains("vel\t0 1000 Millimeters Seconds", mm); Assert.Contains("vel\t0 1500 Millimeters Seconds", mm); // m/s -> mm/s
        Assert.Contains("accel\t0 300 Millimeters Seconds Seconds", mm); Assert.Contains("decel\t0 250 Millimeters Seconds Seconds", mm);
        Assert.Contains("brakedist\t0 5000 Millimeters", mm); Assert.Contains("stopdist\t0 500 Millimeters", mm);
        Assert.Contains(mm, x => x.StartsWith("GPATHTYPE ") && x.Contains("vel 1 1500 Millimeters Seconds"));
        Assert.Empty(warnings);
        // the vehicle segment (picture in feet, 1.6 m x 1.0 m) is the same text as in a Meters model
        static IEnumerable<string> Seg(List<string> x) => x.SkipWhile(l => !l.StartsWith("AGVVEHSEG")).TakeWhile(l => !l.StartsWith("AGVSVEH"));
        Assert.Equal(Seg(MmDrawing("Meters").Lines), Seg(mm));
        Assert.Contains("template Feet", Seg(mm));
    }
    [Fact] public void Unknown_Units_Throw_And_A_Scale_Off_By_1000_Warns()
    {
        Assert.Throws<ArgumentException>(() => MmDrawing("Furlongs"));
        Assert.Contains(MmDrawing("Meters", scale: 1).Warnings, x => x.Contains("10000 m") && x.Contains("Scale"));          // mm drawing exported with Scale 1
        Assert.Contains(MmDrawing("Millimeters", scale: 0.000001).Warnings, x => x.Contains("Scale"));
    }
    [Fact] public void Empty_Load_Accel_Line_Stays_Inherit_Form() =>
        Assert.Contains(Lines(new ExportOptions(Groups: new[]{ new VehicleGroupSpec("G1","Tug",1,1.0,2,1, 30, 0.3, 0.3) })), x => x.Trim() == "accel\t1 0 Feet Seconds Seconds");
}
