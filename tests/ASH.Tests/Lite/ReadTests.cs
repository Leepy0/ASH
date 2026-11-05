using System.Diagnostics;
using System.Text;
using ASH.Core.Cel;
using ASH.Core.Dxf;
using ASH.Core.Lite;

namespace ASH.Tests.Lite;

/// <summary>What the AGVLITE plugin leaves to Core: MINSERT cells, mesh edges, the per-object limit, AGVLITESTAT's block table, cancelling, and the run after the drawing is read.</summary>
public class ReadTests
{
    static readonly Vec3 Up = new(0, 0, 1);

    static Line3 L3(double x1, double y1, double x2, double y2, string layer = "A") => new(layer, new Vec3(x1, y1, 0), new Vec3(x2, y2, 0));
    static Affine3 Move(double x, double y = 0) => Affine3.Identity with { M03 = x, M13 = y };

    static LiteBlocks Drawing(int insUnits, params Geo3[][] defs)
    {
        var d = new LiteBlocks { InsUnits = insUnits };
        foreach (var g in defs) d.Defs.Add(new BlockDef { Geo = g.ToList() });
        return d;
    }

    static void Offset(Affine3 m, double x, double y, double z)
    {
        Assert.Equal(x, m.M03, 9);
        Assert.Equal(y, m.M13, 9);
        Assert.Equal(z, m.M23, 9);
    }

    // --- MINSERT

    [Fact]
    public void Minsert_Offset_Follows_The_Rotation_And_Ignores_The_Scale()
    {
        // scale 2, rotated 90 degrees, at (100, 0)
        var xf = new Affine3(0, -2, 0, 100, 2, 0, 0, 0, 0, 0, 2, 0);
        Offset(LiteRead.Minsert(xf, Up, Math.PI / 2, 10, 0), 100, 10, 0);
        Offset(LiteRead.Minsert(xf, Up, Math.PI / 2, 0, 5), 95, 0, 0);
        Assert.Equal(xf.Linear, LiteRead.Minsert(xf, Up, Math.PI / 2, 10, 5).Linear);
    }

    [Fact]
    public void Minsert_Offset_Lies_In_The_Inserts_Own_Plane()
    {
        Offset(LiteRead.Minsert(Affine3.Identity, new Vec3(0, 0, -1), 0, 10, 0), -10, 0, 0); // the OCS x of -Z points at -X
        Offset(LiteRead.Minsert(Affine3.Identity, new Vec3(0, 0, -1), 0, 0, 10), 0, 10, 0);
        Offset(LiteRead.Minsert(Affine3.Identity, new Vec3(1, 0, 0), 0, 10, 0), 0, 10, 0);   // the OCS of +X: x = +Y, y = +Z
        Offset(LiteRead.Minsert(Affine3.Identity, new Vec3(1, 0, 0), 0, 0, 10), 0, 0, 10);
        Offset(LiteRead.Minsert(Move(1, 2) with { M23 = 3 }, new Vec3(1, 0, 0), Math.PI / 2, 10, 0), 1, 2, 13);
    }

    [Fact]
    public void Minsert_Cells_Reach_The_File_Apart()
    {
        // a real MINSERT: scale 2, rotated 90 degrees, at (1000, 1000). The line of 500 stands 1000 tall
        var d = Drawing(4, Array.Empty<Geo3>(), new Geo3[] { L3(0, 0, 500, 0) });
        var xf = new Affine3(0, -2, 0, 1000, 2, 0, 0, 1000, 0, 0, 2, 0);
        for (int r = 0; r < 2; r++)
            for (int c = 0; c < 3; c++)
                d.Defs[0].Uses.Add(new BlockUse(1, LiteRead.Minsert(xf, Up, Math.PI / 2, c * 2000, r * 2000), "0"));
        var w = new StringWriter();
        var res = new LiteBuild(d).Write(w, 0);
        Assert.Equal(6, res.Entities);
        Assert.Equal(0, res.Deduped);
        var ext = res.Extents;
        Assert.Equal(2 * 2000 + 1000, ext.MaxY - ext.MinY, 6); // three columns up the rotated x, 2000 apart: unscaled
        Assert.Equal(2000, ext.MaxX - ext.MinX, 6); // two rows back along the rotated y
    }

    // --- meshes and the limit

    [Fact]
    public void Face_Edges_Come_Once_Per_Shared_Edge()
    {
        // a quad 0 1 2 3 and a triangle sharing 1-2
        var e = LiteRead.FaceEdges(new[] { 4, 0, 1, 2, 3, 3, 2, 1, 4 });
        Assert.Equal(new[] { (0, 1), (1, 2), (2, 3), (3, 0), (1, 4), (4, 2) }, e);
    }

    [Fact]
    public void Face_Records_Are_One_Based_And_Skip_Hidden_Edges()
    {
        Assert.Equal(new[] { (0, 1), (1, 2), (2, 0) }, LiteRead.RecordEdges(new[] { new[] { 1, 2, 3, 0 } })); // 0: a triangle
        Assert.Equal(new[] { (0, 1), (2, 3), (3, 0) }, LiteRead.RecordEdges(new[] { new[] { 1, -2, 3, 4 } })); // the edge leaving the second corner is hidden, also when the next corner is negative
        Assert.Equal(new[] { (0, 1), (2, 0) }, LiteRead.RecordEdges(new[] { new[] { 1, -2, 3, 0 } }));
    }

    [Theory]
    [InlineData(2, 3, false, false, 7)]
    [InlineData(2, 3, false, true, 9)]
    [InlineData(3, 3, true, true, 18)]
    [InlineData(2, 2, true, true, 4)] // a two-row wrap adds nothing
    public void Grid_Edges_Count(int m, int n, bool mClosed, bool nClosed, int edges) => Assert.Equal(edges, LiteRead.GridEdges(m, n, mClosed, nClosed).Count);

    [Fact]
    public void A_Mesh_Over_The_Limit_Throws_So_The_Reader_Falls_Back_To_The_Box()
    {
        Assert.Throws<InvalidOperationException>(() => LiteRead.GridEdges(10_000, 10_000, false, false));
    }

    [Fact]
    public void The_Limit_Counts_Points()
    {
        var line = (Geo3)L3(0, 0, 1, 1);
        Assert.False(LiteRead.TooBig(new[] { line }));
        var poly = new Points3("A", Enumerable.Repeat(new Vec3(0, 0, 0), LiteRead.MaxPointsPerObject).ToList(), false);
        Assert.False(LiteRead.TooBig(new Geo3[] { poly }));
        Assert.True(LiteRead.TooBig(new Geo3[] { poly, line }));
    }

    [Theory]
    [InlineData(4, 5)]
    [InlineData(6, 0.005)]
    public void Reading_Tolerance_Is_Level_0_In_Drawing_Units(int insUnits, double units) => Assert.Equal(units, LiteRead.Tolerance(insUnits), 9);

    [Fact]
    public void Sampling_Sees_A_Bend_That_Is_Only_In_Z()
    {
        var pts = CurveApprox.Adaptive(t => new Vec3(t, 0, 100 * Math.Sin(t / 30)), 0, 600, 0.5, 8);
        Assert.True(pts.Count > 50);
    }

    // --- AGVLITESTAT block table

    [Fact]
    public void Block_Rows_Multiply_Instances_Down_And_Add_Lines_Up()
    {
        // model space -> A twice, A -> B three times, A -> frozen C, a definition nobody uses
        var d = Drawing(6, Array.Empty<Geo3>(), new[] { (Geo3)L3(0, 0, 1, 0) }, new Geo3[] { L3(0, 0, 2, 0), L3(0, 1, 2, 1), L3(0, 0, 9, 9, "OFF") }, new[] { (Geo3)L3(0, 0, 1, 1) }, new[] { (Geo3)L3(0, 0, 1, 1) });
        d.Layers["OFF"] = new LiteLayerState(7, true, false);
        d.Layers["ICE"] = new LiteLayerState(7, false, true);
        d.Defs[0].Uses.Add(new BlockUse(1, Move(0), "0"));
        d.Defs[0].Uses.Add(new BlockUse(1, Move(50), "0"));
        for (int i = 0; i < 3; i++) d.Defs[1].Uses.Add(new BlockUse(2, Move(3 * i), "0"));
        d.Defs[1].Uses.Add(new BlockUse(3, Move(0), "ICE"));

        var rows = new LiteBuild(d).BlockRows(new[] { "*Model_Space", "A", "B", "C", "UNUSED" }, new[] { 0, 1, 2, 5, 7 }).ToDictionary(r => r.Name);

        Assert.Equal(new[] { "A", "B" }, rows.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal((6, 2, 2), (rows["B"].Instances, rows["B"].Lines, rows["B"].Solids3d));
        Assert.Equal(Math.Sqrt(4 + 1) * 1000, rows["B"].DiagMm, 6); // the hidden line left no mark
        Assert.Equal(2, rows["A"].Instances);
        Assert.Equal(1 + 3 * 2, rows["A"].Lines);
        Assert.Equal(1 + 3 * 2, rows["A"].Solids3d);
        Assert.Equal(Math.Sqrt(8 * 8 + 1) * 1000, rows["A"].DiagMm, 6); // the last B ends at 6 + 2
    }

    [Fact]
    public void Block_Rows_Cut_A_Cycle()
    {
        var d = Drawing(4, Array.Empty<Geo3>(), new[] { (Geo3)L3(0, 0, 1, 0) }, new[] { (Geo3)L3(0, 0, 2, 0) });
        d.Defs[0].Uses.Add(new BlockUse(1, Move(0), "0"));
        d.Defs[1].Uses.Add(new BlockUse(2, Move(0), "0"));
        d.Defs[2].Uses.Add(new BlockUse(1, Move(0), "0"));
        var rows = new LiteBuild(d).BlockRows(new[] { "M", "A", "B" }, new[] { 0, 0, 0 }).ToDictionary(r => r.Name);
        Assert.Equal((1, 2), (rows["A"].Instances, rows["A"].Lines));
        Assert.Equal((1, 1), (rows["B"].Instances, rows["B"].Lines));
    }

    [Fact]
    public void Histogram_Counts_Its_Lines_Not_Its_Visits()
    {
        var d = Drawing(4, new[] { (Geo3)L3(0, 0, 1000, 0) }, new[] { (Geo3)L3(0, 0, 500, 0) });
        d.Defs[0].Uses.Add(new BlockUse(1, Move(0, 5000), "0"));
        Assert.Equal(2, new LiteBuild(d).Histogram().Lines);
    }

    // --- cancel

    static LiteBlocks Plain() => Drawing(4, new[] { (Geo3)L3(0, 0, 10_000, 0, "WALL") });

    [Fact]
    public void A_Cancelled_Build_Stops_Cleaning_Measuring_And_The_Histogram()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var d = Plain();
        d.Defs.Add(new BlockDef { Geo = { L3(0, 0, 1, 1) } });
        d.Defs[0].Uses.Add(new BlockUse(1, Move(0), "0"));
        var b = new LiteBuild(d) { Cancel = cts.Token };
        Assert.ThrowsAny<OperationCanceledException>(() => b.ChooseLevel(LiteLevels.TargetBytes));
        Assert.ThrowsAny<OperationCanceledException>(() => b.Histogram());
    }

    [Fact]
    public void Choose_Level_Tells_Each_Level_Before_It_Measures_It()
    {
        var seen = new List<int>();
        var level = new LiteBuild(Plain()).ChooseLevel(1, seen.Add); // nothing fits one byte
        Assert.Equal(LiteLevels.Max, level);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, seen);
    }

    // --- the run

    sealed class Folder : IDisposable
    {
        public readonly string Dir = Path.Combine(Path.GetTempPath(), "ash-lite-" + Guid.NewGuid().ToString("N"));
        public LiteFiles Files => new(@"D:\p\a.dwg", 1234, Path.Combine(Dir, "lite", "a_lite.dxf"), Path.Combine(Dir, "lite", "a_lite.report.tsv"), Path.Combine(Dir, "lite", "a_lite.settings.tsv"));
        public void Dispose() { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); }
    }

    static LiteOutcome Job(LiteFiles f, LiteSettings? settings = null, bool save = true, CancellationToken ct = default, Action<int>? measuring = null, LiteReadLog? log = null) =>
        LiteJob.Run(new LiteBuild(Plain(), settings ?? new LiteSettings()), log ?? new LiteReadLog(), f, save, Stopwatch.StartNew(), ct, measuring ?? (_ => { }));

    [Fact]
    public void A_Run_Writes_The_Dxf_The_Report_And_The_Settings()
    {
        using var t = new Folder();
        var log = new LiteReadLog { Failures = 2, ExplodeFailed = 3 };
        log.Before["Line"] = 7;
        log.Removed["해치"] = 4;
        log.Solids.Add(5);
        var o = Job(t.Files, log: log);

        Assert.False(o.Cancelled);
        Assert.Null(o.Failed);
        Assert.Equal(0, o.Result!.Level);
        Assert.Equal(new FileInfo(t.Files.Dxf).Length, o.Bytes);
        Assert.Single(DxfLite.Read(new StringReader(File.ReadAllText(t.Files.Dxf))).Segs);
        var report = File.ReadAllText(t.Files.Report, Encoding.UTF8);
        foreach (var row in new[] { "[원본 엔티티 (모델 공간 최상위)]", "Line\t7", "합계\t7", "[제거한 항목 (정의 단위)]", "해치\t4", "3D 객체\t5", "폭발 실패 (범위 상자로 대체)\t3", "읽지 못한 엔티티\t2", "원본 크기 (DWG)" })
            Assert.Contains(row, report);
        Assert.Contains("UsedLevel\t0", File.ReadAllText(t.Files.Settings));
    }

    [Fact]
    public void A_Saved_Level_Is_Used_As_It_Is()
    {
        using var t = new Folder();
        var seen = new List<int>();
        var o = Job(t.Files, new LiteSettings { Level = 3 }, measuring: seen.Add);
        Assert.Equal(3, o.Result!.Level);
        Assert.Equal(new[] { 3 }, seen);
        Assert.Contains("Level\t3", File.ReadAllLines(t.Files.Settings));
    }

    [Fact]
    public void Esc_While_Cleaning_Leaves_Nothing_Written()
    {
        using var t = new Folder();
        using var cts = new CancellationTokenSource();
        var o = Job(t.Files, ct: cts.Token, measuring: _ => cts.Cancel());
        Assert.True(o.Cancelled);
        Assert.Null(o.Result);
        Assert.False(Directory.Exists(t.Dir));
    }

    [Fact]
    public void A_Settings_File_That_Could_Not_Be_Read_Is_Not_Replaced()
    {
        using var t = new Folder();
        Directory.CreateDirectory(Path.GetDirectoryName(t.Files.Settings)!);
        File.WriteAllText(t.Files.Settings, "Level\t2\n");
        Job(t.Files, save: false);
        Assert.Equal("Level\t2\n", File.ReadAllText(t.Files.Settings));
    }

    [Fact]
    public void An_Unwritable_File_Is_Named()
    {
        using var t = new Folder();
        Directory.CreateDirectory(t.Files.Dxf); // a folder in its place: nothing can replace it on any system
        var o = Job(t.Files);
        Assert.Equal(t.Files.Dxf, o.Failed);
        Assert.False(string.IsNullOrEmpty(o.Why));

        Directory.Delete(t.Files.Dxf);
        Directory.CreateDirectory(t.Files.Report);
        Assert.Equal(t.Files.Report, Job(t.Files).Failed);
    }

    /// <summary>A previous good run, kept to compare bytes.</summary>
    static (byte[] Dxf, byte[] Report) OldRun(LiteFiles f)
    {
        Assert.Null(Job(f).Failed);
        return (File.ReadAllBytes(f.Dxf), File.ReadAllBytes(f.Report));
    }

    static void Unchanged(LiteFiles f, (byte[] Dxf, byte[] Report) old)
    {
        Assert.Equal(old.Dxf, File.ReadAllBytes(f.Dxf));
        Assert.Equal(old.Report, File.ReadAllBytes(f.Report));
    }

    static LiteBlocks Other() => Drawing(4, new[] { (Geo3)L3(0, 0, 20_000, 7_000, "OTHER") });

    static LiteOutcome NewJob(LiteFiles f) => LiteJob.Run(new LiteBuild(Other()), new LiteReadLog(), f, true, Stopwatch.StartNew(), default, _ => { });

    [Fact]
    public void A_Replaced_Pair_Leaves_No_Tmp_And_Both_Files_New()
    {
        using var t = new Folder();
        var old = OldRun(t.Files);
        Assert.Null(NewJob(t.Files).Failed);
        Assert.NotEqual(old.Dxf, File.ReadAllBytes(t.Files.Dxf));
        Assert.NotEqual(old.Report, File.ReadAllBytes(t.Files.Report));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(t.Files.Dxf)!, "*.tmp"));
    }

    [Fact]
    public void A_Failing_Write_Leaves_The_Old_Pair_And_No_Tmp()
    {
        using var t = new Folder();
        var old = OldRun(t.Files);
        Directory.CreateDirectory(t.Files.Report + ".tmp"); // the report cannot start: the DXF's tmp is whole by then
        var o = NewJob(t.Files);
        Assert.Equal(t.Files.Report, o.Failed);
        Assert.Null(o.Result);
        Unchanged(t.Files, old);
        Assert.False(File.Exists(t.Files.Dxf + ".tmp"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_Held_File_Is_Named_And_Neither_Old_File_Moves(bool dxf)
    {
        using var t = new Folder();
        var old = OldRun(t.Files);
        string held = dxf ? t.Files.Dxf : t.Files.Report;
        LiteOutcome o;
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None)) o = NewJob(t.Files); // ZWCAD or Excel has it open
        Assert.Equal(held, o.Failed);
        Assert.False(string.IsNullOrEmpty(o.Why));
        Unchanged(t.Files, old);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(t.Files.Dxf)!, "*.tmp"));
    }
}
