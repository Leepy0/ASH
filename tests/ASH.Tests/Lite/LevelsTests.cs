using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ASH.Core.Cel;
using ASH.Core.Dxf;
using ASH.Core.Lite;

namespace ASH.Tests.Lite;

/// <summary>§3 levels and their choice, per-unit precision, global dedup, the size measure (R20), settings and the report.</summary>
public class LevelsTests
{
    static readonly Vec3 Up = new(0, 0, 1);
    const string Mesh = "ROBOT";

    static Line3 L3(double x1, double y1, double x2, double y2, string layer = "A") => new(layer, new Vec3(x1, y1, 0), new Vec3(x2, y2, 0));
    static Affine3 Xf(double a, double b, double c, double d, double e, double f) => new(a, b, 0, e, c, d, 0, f, 0, 0, 1, 0);
    static Affine3 Move(double x, double y = 0) => Xf(1, 0, 0, 1, x, y);
    static IEnumerable<Geo3> Robot(double x = 0, double y = 0, int n = 100) => BlobsTests.Robot(x, y, Mesh, n).Cast<LiteLine>().Select(l => (Geo3)L3(l.X1, l.Y1, l.X2, l.Y2, l.Layer));

    static LiteBlocks Drawing(int insUnits, params IEnumerable<Geo3>[] defs)
    {
        var d = new LiteBlocks { InsUnits = insUnits };
        foreach (var g in defs) d.Defs.Add(new BlockDef { Geo = g.ToList() });
        return d;
    }

    static void Use(LiteBlocks d, int parent, int child, Affine3 xf, string layer = "0") => d.Defs[parent].Uses.Add(new BlockUse(child, xf, layer));

    static (string Dxf, LiteResult R) Written(LiteBuild b, LiteThresholds t)
    {
        var sw = new StringWriter();
        var r = b.Write(sw, t);
        return (sw.ToString(), r);
    }

    static string Entities(string dxf) => dxf.Substring(dxf.IndexOf("ENTITIES", StringComparison.Ordinal));

    /// <summary>Values of the group codes in the ENTITIES section, as written.</summary>
    static List<string> Values(string dxf, params int[] codes)
    {
        var lines = Entities(dxf).Split(new[] { "\r\n" }, StringSplitOptions.None);
        var o = new List<string>();
        for (int i = 1; i + 1 < lines.Length; i += 2)
            if (int.TryParse(lines[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int c) && codes.Contains(c)) o.Add(lines[i + 1]);
        return o;
    }

    static int Decimals(string s) => s.IndexOf('.') is var d && d < 0 ? 0 : s.Length - d - 1;

    // --- levels

    [Fact]
    public void Table_Is_Section_3()
    {
        Assert.Equal(5, LiteLevels.Table.Count);
        var want = new[] { (5.0, 0.0, 0.0, 0.0), (10, 30, 0, 0), (10, 50, 20_000, 6_000), (20, 100, 10_000, 30_000), (30, 200, 5_000, 100_000) };
        for (int i = 0; i < 5; i++)
        {
            var t = LiteLevels.Get(i, 4);
            Assert.Equal(want[i], (t.ToleranceMm, t.MinSizeMm, t.DenseMmPerM2, t.MaxBlobMm));
            Assert.Equal((4, 200.0), (t.InsUnits, t.CellMm));
        }
        Assert.Equal(BlobsTests.L3, LiteLevels.Get(3, 4));
    }

    [Fact]
    public void Every_Threshold_Moves_One_Way_With_The_Level()
    {
        double Dense(int i) => LiteLevels.Get(i, 4).DenseMmPerM2 is var d && d == 0 ? double.PositiveInfinity : d; // off = nothing is dense enough
        for (int i = 1; i < 5; i++)
        {
            var (a, b) = (LiteLevels.Get(i - 1, 4), LiteLevels.Get(i, 4));
            Assert.True(a.ToleranceMm <= b.ToleranceMm);
            Assert.True(a.MinSizeMm <= b.MinSizeMm);
            Assert.True(a.MaxBlobMm <= b.MaxBlobMm);
            Assert.True(Dense(i - 1) >= Dense(i));
        }
    }

    [Fact]
    public void Levels_Outside_The_Table_Are_Refused() => Assert.Throws<ArgumentOutOfRangeException>(() => LiteLevels.Get(5, 4));

    [Fact]
    public void NoBlobs_Turns_Dense_Off_At_Every_Level_And_In_The_Build()
    {
        for (int i = 0; i < 5; i++) Assert.Equal(0, LiteLevels.Get(i, 4, noBlobs: true).DenseMmPerM2);
        var d = Drawing(4, Robot());
        Assert.Equal(1, new LiteBuild(d).Run(new LiteBuild(d).Thresholds(2)).Stats.Blobs);
        var off = new LiteBuild(d, new LiteSettings { NoBlobs = true });
        Assert.Equal(0, off.Run(off.Thresholds(2)).Stats.Blobs);
    }

    [Fact]
    public void ChooseLevel_Stops_At_The_First_Level_That_Fits()
    {
        var d = Drawing(4, Robot());
        var sizes = new LiteBuild(d);
        long l0 = sizes.Total(0), l2 = sizes.Total(2);
        Assert.True(l2 < l0);

        Assert.Equal(0, new LiteBuild(d).ChooseLevel(l0));
        var b = new LiteBuild(d);
        Assert.Equal(2, b.ChooseLevel(l2));
        Assert.Equal(new[] { 0, 1, 2 }, b.LevelSizes.Keys);
        Assert.Equal(l0, b.LevelSizes[0]);
        Assert.Equal(l2, b.LevelSizes[2]);
    }

    [Fact]
    public void ChooseLevel_Falls_Back_To_4()
    {
        var b = new LiteBuild(Drawing(4, Robot()));
        Assert.Equal(4, b.ChooseLevel(1));
        Assert.Equal(5, b.LevelSizes.Count);
    }

    // --- precision

    [Theory]
    [InlineData(4, 2)] // mm
    [InlineData(6, 5)] // m
    [InlineData(1, 3)] // inch
    [InlineData(5, 3)] // cm
    [InlineData(0, 2)] // unitless is mm
    public void Precision_Is_About_A_Hundredth_Of_A_Mm(int insUnits, int decimals) => Assert.Equal(decimals, LiteDxfWriter.Precision(insUnits));

    [Theory]
    [InlineData(4, 1000.123456, 2)]
    [InlineData(6, 1.123456789, 5)]
    [InlineData(1, 10.123456, 3)]
    public void Lengths_Are_Written_At_That_Precision_And_Angles_Stay_Fine(int insUnits, double v, int decimals)
    {
        var d = Drawing(insUnits, new Geo3[]
        {
            L3(v, v, v * 3, v * 2),
            new Circle3("A", new Vec3(v, v, 0), Up, v / 3),
            new Arc3("A", new Vec3(v, v, 0), Up, v / 7, Math.PI / 7, 3),
        });
        var b = new LiteBuild(d);
        var (dxf, _) = Written(b, LiteLevels.Get(0, insUnits));

        Assert.All(Values(dxf, 10, 20, 11, 21, 40), s => Assert.True(Decimals(s) <= decimals, s));
        Assert.Contains(Values(dxf, 10, 20, 11, 21, 40), s => Decimals(s) == decimals);
        Assert.Contains("25.7143", Values(dxf, 50));
        Assert.DoesNotMatch(@"\r\n-?\d+\.\d{" + (decimals + 1) + @",}\r\n", dxf.Substring(0, dxf.IndexOf("ENTITIES", StringComparison.Ordinal))); // the header too
    }

    // --- bytes

    [Fact]
    public void Bytes_Is_What_Entity_Writes_At_That_Precision()
    {
        var ents = new LiteEntity[]
        {
            new LiteLine("A", 1.123456, 2, 3, 4),
            new LiteCircle("A", 1, 2, 3.123456),
            new LiteArc("A", 1, 2, 3.123456, 10.123456, 20),
            new LitePolyline("A", new[] { new LiteVertex(0.001, 0.123456, 0.5), new LiteVertex(1, 1) }, true),
        };
        foreach (var e in ents)
            foreach (int decimals in new[] { 2, 3, 5 })
            {
                var sw = new StringWriter();
                LiteDxfWriter.Entity(sw, e, "NAME", 5, 7, decimals);
                Assert.Equal(sw.ToString().Length, LiteDxfWriter.Bytes(e, "NAME", 5, 7, decimals));
            }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(1)]
    public void Total_Is_The_File_Byte_For_Byte(int insUnits)
    {
        var d = Drawing(insUnits, new Geo3[] { L3(1.5, 2.5, 7.25, 9.125, "한글 레이어"), new Circle3("B", new Vec3(3, 4, 0), Up, 2.5), new Arc3("C", new Vec3(9, 4, 0), Up, 1.5, 0.5, 2) });
        for (int i = 0; i < 40; i++) d.Layers["L" + i] = new LiteLayerState(7, false, false); // the table holds only used layers
        var b = new LiteBuild(d);
        var t = LiteLevels.Get(0, insUnits);
        var (dxf, r) = Written(b, t);
        Assert.Equal(0, r.Deduped);
        Assert.Equal(dxf.Length, b.Total(t));
        Assert.All(dxf, c => Assert.True(c < 128));
    }

    [Fact]
    public void An_Empty_Drawing_Measures_The_Same_Too()
    {
        var b = new LiteBuild(Drawing(4, Array.Empty<Geo3>()));
        var t = LiteLevels.Get(0, 4);
        var (dxf, r) = Written(b, t);
        Assert.Equal(dxf.Length, b.Total(t));
        Assert.Equal(0, r.Entities);
        Assert.True(r.Extents.IsEmpty);
    }

    [Fact]
    public void Per_Layer_Estimate_Counts_Placed_Entities_And_Their_Bytes()
    {
        var d = Drawing(4, Array.Empty<Geo3>(), new Geo3[] { L3(0, 0, 1000, 0, "0"), L3(0, 500, 1000, 500, "B") });
        Use(d, 0, 1, Move(0), "A");
        Use(d, 0, 1, Move(5000), "A");
        var b = new LiteBuild(d);
        var t = LiteLevels.Get(0, 4);
        var est = b.Estimate(t);
        Assert.Equal(new[] { "A", "B" }, est.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(2, est["A"].Count);
        var (dxf, _) = Written(b, t);
        long entities = est.Values.Sum(v => v.Bytes);
        Assert.Equal(Entities(dxf).Length - Entities(dxf).Substring(Entities(dxf).LastIndexOf("  0\r\nENDSEC", StringComparison.Ordinal)).Length - "ENTITIES\r\n".Length, entities);
        Assert.Equal(b.Biggest(t).Max(x => x.Bytes), b.Biggest(t, 1)[0].Bytes);
    }

    // --- global dedup

    [Fact]
    public void The_Same_Definition_Twice_On_The_Same_Spot_Is_Written_Once()
    {
        var d = Drawing(4, Array.Empty<Geo3>(), new Geo3[] { L3(0, 0, 1000, 0), new Circle3("A", new Vec3(0, 0, 0), Up, 100) });
        Use(d, 0, 1, Move(5000));
        Use(d, 0, 1, Move(5000));
        Use(d, 0, 1, Move(9000));
        var b = new LiteBuild(d);
        var t = LiteLevels.Get(0, 4);
        var (dxf, r) = Written(b, t);

        Assert.Equal(4, r.Entities);
        Assert.Equal(2, r.Deduped);
        Assert.Equal(2, r.Written["LINE"]);
        Assert.Equal(b.Total(t), dxf.Length + r.DedupedBytes);
        Assert.True(r.DedupedBytes > 0);
    }

    [Fact]
    public void Dedup_Compares_Placed_Coordinates_Whatever_The_Definition_Or_Layer()
    {
        // two definitions, one line each, meeting in the drawing: reversed, on two layers, 0.03 mm apart
        var d = Drawing(4, new Geo3[] { L3(0, 0, 2000, 0, "Z") }, new Geo3[] { L3(100, 0.03, -1900, 0.03, "B") });
        Use(d, 0, 1, Move(1900));
        var (dxf, r) = Written(new LiteBuild(d), LiteLevels.Get(0, 4));
        Assert.Equal((1, 1), (r.Entities, r.Deduped));
        Assert.Equal("B", new[] { "B", "Z" }.Single(l => dxf.Contains("\r\n  8\r\n" + l + "\r\n"))); // layer order decides who stays
    }

    [Fact]
    public void Different_Instances_Stay()
    {
        var d = Drawing(4, Array.Empty<Geo3>(), new Geo3[] { L3(0, 0, 1000, 0) });
        Use(d, 0, 1, Move(0));
        Use(d, 0, 1, Move(0, 1));
        Assert.Equal(0, Written(new LiteBuild(d), LiteLevels.Get(0, 4)).R.Deduped);
    }

    [Fact]
    public void Same_Input_Same_Bytes()
    {
        LiteBlocks D()
        {
            var d = Drawing(4, Array.Empty<Geo3>(), new Geo3[] { L3(0, 0, 1000, 0, "B"), L3(0, 300, 1000, 300, "A") });
            for (int i = 0; i < 30; i++) Use(d, 0, 1, Move(i % 7 * 3000, i % 3));
            return d;
        }
        var t = LiteLevels.Get(0, 4);
        Assert.Equal(Written(new LiteBuild(D()), t).Dxf, Written(new LiteBuild(D()), t).Dxf);
    }

    [Fact]
    public void A_Duplicate_Across_A_Shift_Step_Cannot_Move_The_Shift()
    {
        // the second line (layer B) goes as a duplicate of the first, and only it reaches below the 1000 step: both the measure and the file keep the shift of everything placed
        var d = Drawing(4, new Geo3[] { L3(1000.02, 0, 3000, 0, "A") }, new Geo3[] { L3(999.98, 0, 3000, 0, "B") });
        Use(d, 0, 1, Affine3.Identity);
        var b = new LiteBuild(d);
        var t = LiteLevels.Get(0, 4);
        var (dxf, r) = Written(b, t);
        Assert.Equal(1, r.Deduped);
        Assert.Equal(0, r.ShiftX);
        Assert.Equal(b.Total(t), dxf.Length + r.DedupedBytes);
    }

    [Fact]
    public void The_Shift_Comes_From_What_Is_Written_Not_From_Hidden_Geometry()
    {
        var d = Drawing(4, new Geo3[] { L3(5000, 5000, 7000, 5000) }, new Geo3[] { L3(-900_000, -900_000, -800_000, -900_000, "0") });
        d.Layers["OFF"] = new LiteLayerState(7, true, false);
        Use(d, 0, 1, Move(0), "OFF");
        var (_, r) = Written(new LiteBuild(d), LiteLevels.Get(0, 4));
        Assert.Equal((5000, 5000), (r.ShiftX, r.ShiftY));
        Assert.Equal(2000, r.Extents.MaxX, 6);
    }

    [Fact]
    public void Extents_Are_Tight_Under_A_Rotated_Instance()
    {
        // the corners of a rotated child's box reach out to 1414 where the line itself reaches 1000
        double c = Math.Sqrt(0.5);
        var d = Drawing(4, Array.Empty<Geo3>(), new Geo3[] { L3(0, 0, 1000, 1000) });
        Use(d, 0, 1, Xf(c, -c, c, c, 3000, 3000));
        var (_, r) = Written(new LiteBuild(d), LiteLevels.Get(0, 4));
        Assert.Equal(1414.2136, r.Extents.MaxY - r.Extents.MinY, 3);
        Assert.Equal(0, r.Extents.MaxX - r.Extents.MinX, 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Estimate_Is_An_Upper_Bound_Over_50_Random_Placements(int level)
    {
        var def = new List<Geo3>
        {
            L3(0, 0, 1500, 0),
            L3(100, 400, 900, 1200, "B"),
            new Circle3("A", new Vec3(700, 100, 0), Up, 321.5),
            new Circle3("0", new Vec3(-500, 800, 0), Up, 77),
            new Arc3("B", new Vec3(0, 900, 0), Up, 450.25, 0.3, 2.9),
            new Arc3("A", new Vec3(1200, 900, 0), Up, 2000, 5.5, 0.4),
            new Poly3("A", new[] { new LiteVertex(0, 0, 0.5), new LiteVertex(800, 0, -0.3), new LiteVertex(800, 800, 1), new LiteVertex(0, 800) }, true, Up, 0),
            new Poly3("B", new[] { new LiteVertex(-300, -300, 0.2), new LiteVertex(-300, 500, 0.9), new LiteVertex(400, 500) }, false, Up, 0),
            new Poly3("B", new[] { new LiteVertex(2000, 0, 1), new LiteVertex(2600, 0, 1) }, true, Up, 0),
        };
        for (int seed = 0; seed < 50; seed++)
        {
            var rnd = new Random(seed * 31 + level);
            var d = Drawing(4, Array.Empty<Geo3>(), def, Array.Empty<Geo3>());
            d.Defs[2].Uses.Add(new BlockUse(1, Xf(0.5, 0, 0, 0.5, 300, 300), "0"));
            var at = new List<Affine3>();
            for (int i = rnd.Next(2, 9); i > 0; i--)
            {
                double a = rnd.NextDouble() * 2 * Math.PI, s = 0.2 + rnd.NextDouble() * 3, c = Math.Cos(a) * s, n = Math.Sin(a) * s;
                double e = (rnd.NextDouble() - 0.5) * 2e6, f = (rnd.NextDouble() - 0.5) * 2e6;
                at.Add(rnd.Next(5) switch
                {
                    0 => Xf(c, -n, n, c, e, f),         // rotated, scaled
                    1 => Xf(c, n, n, -c, e, f),         // mirrored
                    2 => Xf(c * 2.5, -n * 0.4, n * 2.5, c * 0.4, e, f), // uneven
                    3 => Xf(-c * 0.3, -n, -n * 0.3, c, e, f),           // uneven and mirrored
                    _ => Xf(1, 0, 0, 1, Math.Round(e), Math.Round(f)),
                });
                if (rnd.Next(3) == 0) at.Add(at[at.Count - 1]); // a true duplicate
            }
            foreach (var xf in at) Use(d, 0, rnd.Next(3) == 0 ? 2 : 1, xf, "L" + rnd.Next(3));

            var b = new LiteBuild(d);
            var t = LiteLevels.Get(level, 4);
            long estimate = b.Total(t);
            var (dxf, r) = Written(b, t);
            Assert.True(estimate >= dxf.Length, $"seed {seed}: {estimate} < {dxf.Length}");
            Assert.Equal(estimate, dxf.Length + r.DedupedBytes);
            Assert.NotEmpty(DxfLite.Read(new StringReader(dxf)).Segs);
        }
    }

    [Theory]
    [InlineData(1, 0, 0, 1)]   // as it is
    [InlineData(0, -2, 2, 0)]  // rotated and scaled
    [InlineData(-1, 0, 0, 1)]  // mirrored
    [InlineData(2, 0, 0, 0.5)] // uneven: chords
    public void A_Closed_Two_Point_Polyline_With_Bulges_Survives_The_Writer_And_DxfLite(double a, double b, double c, double d)
    {
        var ring = new Geo3[] { new Poly3("A", new[] { new LiteVertex(0, 0, 1), new LiteVertex(1000, 0, 1) }, true, Up, 0) };
        var dr = Drawing(4, Array.Empty<Geo3>(), ring);
        Use(dr, 0, 1, Xf(a, b, c, d, 50_000, 50_000));
        var (dxf, r) = Written(new LiteBuild(dr), LiteLevels.Get(0, 4));

        Assert.Equal(1, r.Written["POLYLINE"]);
        var segs = DxfLite.Read(new StringReader(dxf)).Segs;
        Assert.True(segs.Count >= 16, segs.Count.ToString());
        Assert.Equal((segs[0].X1, segs[0].Y1), (segs[segs.Count - 1].X2, segs[segs.Count - 1].Y2)); // closes
        // the image of the unit circle: both axes reached
        double w = segs.Max(s => s.X1) - segs.Min(s => s.X1), h = segs.Max(s => s.Y1) - segs.Min(s => s.Y1);
        Assert.Equal(1000 * Math.Sqrt(a * a + b * b), w, 0.08 * w);
        Assert.Equal(1000 * Math.Sqrt(c * c + d * d), h, 0.08 * h);
    }

    // --- settings

    static string Temp() => Path.Combine(Path.GetTempPath(), "lite-settings-" + Guid.NewGuid().ToString("N") + ".tsv");

    [Fact]
    public void Settings_Round_Trip_With_A_Bom()
    {
        var path = Temp();
        try
        {
            new LiteSettings { Level = 3, ExcludeLayers = { "벽 레이어", "A|B" }, NoBlobs = true }.Save(path);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
            var s = LiteSettings.Load(path);
            Assert.Equal(3, s.Level);
            Assert.Equal(new[] { "벽 레이어", "A|B" }, s.ExcludeLayers);
            Assert.True(s.NoBlobs);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_Missing_File_Is_Auto()
    {
        var s = LiteSettings.Load(Temp());
        Assert.Null(s.Level);
        Assert.Empty(s.ExcludeLayers);
        Assert.False(s.NoBlobs);
    }

    [Theory]
    [InlineData("Level\t7")]
    [InlineData("Level\t-1")]
    [InlineData("Level\tabc")]
    [InlineData("Level\t")]
    [InlineData("Level\tauto")]
    [InlineData("Level")]
    [InlineData("")]
    public void Bad_Values_Are_Auto(string line) => Assert.Null(LiteSettings.Parse(new[] { line, "NoBlobs\tmaybe" }).Level);

    [Fact]
    public void Bad_NoBlobs_Is_Off() => Assert.False(LiteSettings.Parse(new[] { "NoBlobs\tmaybe" }).NoBlobs);

    [Fact]
    public void Unknown_Keys_Survive_A_Rewrite()
    {
        var path = Temp();
        try
        {
            File.WriteAllLines(path, new[] { "Future\tvalue\twith\ttabs", "Level\t2", "; a note" }, new UTF8Encoding(true));
            var s = LiteSettings.Load(path);
            Assert.Equal(2, s.Level);
            s.Level = null;
            s.Save(path);
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            Assert.Contains("Future\tvalue\twith\ttabs", lines);
            Assert.Contains("; a note", lines);
            Assert.Null(LiteSettings.Load(path).Level);
            Assert.Single(lines, l => l.StartsWith("Level\t", StringComparison.Ordinal));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_Level_Used_Is_Recorded_Once_And_Is_Not_A_Choice()
    {
        var s = new LiteSettings();
        s.Record(2, LiteLevels.Get(2, 4));
        s.Record(3, LiteLevels.Get(3, 4));
        var lines = s.Lines();
        Assert.Contains("UsedLevel\t3", lines);
        Assert.Contains("UsedToleranceMm\t20", lines);
        Assert.Contains("UsedMinSizeMm\t100", lines);
        Assert.Contains("UsedDenseMmPerM2\t10000", lines);
        Assert.Contains("UsedMaxBlobMm\t30000", lines);
        Assert.Single(lines, l => l.StartsWith("UsedLevel", StringComparison.Ordinal));
        Assert.Null(LiteSettings.Parse(lines).Level);
    }

    [Fact]
    public void Excluded_Layers_From_The_Settings_Go_Before_Cleaning()
    {
        // a hidden duplicate must not take the visible line with it (R8)
        var d = Drawing(4, new Geo3[] { L3(0, 0, 2000, 0, "HIDE"), L3(0, 0, 2000, 0, "SHOW") });
        var (dxf, r) = Written(new LiteBuild(d, new LiteSettings { ExcludeLayers = { "HIDE" } }), LiteLevels.Get(0, 4));
        Assert.Equal(1, r.Entities);
        Assert.Contains("\r\nSHOW\r\n", Entities(dxf));
        Assert.DoesNotContain("HIDE", dxf);
    }

    // --- density before cleaning

    [Fact]
    public void Histogram_Measures_The_Drawing_Before_Cleaning_On_The_1_M_Grid()
    {
        var d = Drawing(4, new Geo3[] { L3(0, 100, 1000, 100), L3(0, 100, 1000, 100), L3(0, 100, 1000, 100, "HIDE") }, new Geo3[] { L3(0, 0, 500, 0) });
        Use(d, 0, 1, Xf(2, 0, 0, 2, 5000, 5000));
        var h = new LiteBuild(d, new[] { "HIDE" }).Histogram();
        Assert.Equal(2000 + 1000, h.TotalMm, 6); // the duplicate counts, the hidden line does not, the instance is placed at scale 2
        Assert.Equal(2, h.Cells);
        Assert.False(h.Cut);
    }

    [Fact]
    public void Histogram_Is_In_Mm_Whatever_The_Unit_And_Follows_Inherited_Layers()
    {
        var d = Drawing(6, new Geo3[] { L3(0, 0, 2, 0) }, new Geo3[] { L3(0, 0, 1, 0, "0") });
        d.Layers["OFF"] = new LiteLayerState(7, true, false);
        Use(d, 0, 1, Move(0, 3), "OFF");
        Assert.Equal(2000, new LiteBuild(d).Histogram().TotalMm, 6);
    }

    [Fact]
    public void Histogram_Stops_At_The_Budget_And_Says_So()
    {
        var d = Drawing(4, new Geo3[] { L3(0, 0, 1000, 0), L3(0, 300, 1000, 300), L3(0, 600, 1000, 600) });
        var h = new LiteBuild(d) { Budget = 2 }.Histogram(); // the visit and one line
        Assert.True(h.Cut);
        Assert.Equal(1000, h.TotalMm, 6);
    }

    [Fact]
    public void Histogram_Budget_Catches_A_Fan_Out_Of_Empty_Blocks()
    {
        var d = Drawing(4, Array.Empty<Geo3>(), Array.Empty<Geo3>(), Array.Empty<Geo3>(), Array.Empty<Geo3>());
        for (int level = 0; level < 3; level++)
            for (int i = 0; i < 100; i++) Use(d, level, level + 1, Move(i));
        Assert.True(new LiteBuild(d) { Budget = 1000 }.Histogram().Cut);
    }

    [Fact]
    public void Histogram_Survives_A_Cycle()
    {
        var d = Drawing(4, new Geo3[] { L3(0, 0, 1000, 0) }, new Geo3[] { L3(0, 300, 1000, 300) });
        Use(d, 0, 1, Move(0));
        Use(d, 1, 0, Move(5000));
        Assert.Equal(2000, new LiteBuild(d).Histogram().TotalMm, 6);
    }

    // --- blobs in drawing coordinates

    [Fact]
    public void Blob_Rows_Use_The_First_Placement_And_Count_The_Instances()
    {
        var d = Drawing(4, Array.Empty<Geo3>(), Robot());
        Use(d, 0, 1, Xf(0, -1, 1, 0, 50_000, 20_000)); // 90 degrees: (1000, 1000) goes to (49000, 21000)
        Use(d, 0, 1, Move(90_000));
        var row = Assert.Single(new LiteBuild(d).TopBlobs(BlobsTests.L2));
        Assert.Equal(2, row.Instances);
        Assert.Equal(49_000, row.X, 0.2 * 1000);
        Assert.Equal(21_000, row.Y, 0.2 * 1000);
        Assert.InRange(row.DiagMm, 2800, 3200);
        Assert.True(row.Lines > 100);
        Assert.True(row.Density > 20_000);
    }

    [Fact]
    public void A_Blob_Scales_To_Drawing_Mm()
    {
        var d = Drawing(6, Array.Empty<Geo3>(), Robot()); // a metre drawing, the block drawn in mm and placed at 0.001
        Use(d, 0, 1, Xf(0.001, 0, 0, 0.001, 7, 8));
        var row = Assert.Single(new LiteBuild(d).TopBlobs(LiteLevels.Get(2, 6)));
        Assert.InRange(row.DiagMm, 2800, 3200);
        Assert.Equal(8, row.X, 0.2);
    }

    [Fact]
    public void A_Blob_Whose_Node_Is_No_Longer_Placed_Has_No_Position()
    {
        // the child's mesh is its own blob, then the parent's mesh swallows the instance
        var d = Drawing(4, Robot(), Robot(n: 30));
        Use(d, 0, 1, Move(600, 600));
        var b = new LiteBuild(d);
        var rows = b.TopBlobs(BlobsTests.L2);
        Assert.Equal(1, b.Run(BlobsTests.L2).Stats.DroppedInstances);
        var gone = Assert.Single(rows, r => r.Instances == 0);
        Assert.True(double.IsNaN(gone.X));
        Assert.Single(rows, r => r.Instances == 1);
    }

    // --- report

    static string Report(LiteBuild b, int level, long? liteBytes = null)
    {
        var sw = new StringWriter();
        var r = b.Write(sw, level);
        var w = new StringWriter();
        LiteReport.Write(w, b, r, b.Histogram(), @"D:\p\a.dwg", 123456789, @"D:\p\lite\a_lite.dxf", liteBytes ?? sw.ToString().Length, new DateTime(2026, 11, 6, 9, 0, 0), TimeSpan.FromSeconds(83.5));
        return w.ToString();
    }

    static LiteBlocks Plant()
    {
        var d = Drawing(4, new Geo3[] { L3(123_456, 45_678, 200_000, 45_678, "WALL"), L3(0, 0, 1, 1, "OTHER") }.Concat(Robot(130_000, 50_000)), new Geo3[] { L3(0, 0, 1000, 0, "BLK") });
        Use(d, 0, 1, Move(140_000, 50_000));
        Use(d, 0, 1, Move(140_000, 50_000));
        d.Layers["OFF"] = new LiteLayerState(7, true, false);
        d.Layers["WALL"] = new LiteLayerState(7, false, false);
        return d;
    }

    [Fact]
    public void Report_Has_The_Level_And_The_Sizes_Per_Level()
    {
        var b = new LiteBuild(Plant());
        int level = b.ChooseLevel(b.Total(2));
        var t = Report(b, level);
        Assert.Equal(2, level);
        Assert.Contains("[단계]", t);
        Assert.Contains("쓴 단계\t2", t);
        Assert.Contains("선택\t자동", t);
        Assert.Contains("목표\t5.00 MB", t);
        Assert.Contains("단계\t곡선 허용 오차 mm\t최소 크기 mm\t밀도 기준 mm/m²\t덩어리 최대 mm\t예상 크기", t);
        Assert.Contains("0\t5\t0\t끔\t-\t", t);
        Assert.Contains("2\t10\t50\t20000\t6000\t", t);
        Assert.Contains("4\t30\t200\t5000\t100000\t-", t); // not measured: the choice stopped at 2
    }

    [Fact]
    public void Report_Marks_A_Fixed_Level()
    {
        var b = new LiteBuild(Plant(), new LiteSettings { Level = 1, ExcludeLayers = { "OFF2" }, NoBlobs = true });
        var t = Report(b, 1);
        Assert.Contains("쓴 단계\t1", t);
        Assert.Contains("선택\t고정", t);
        Assert.Contains("[설정]", t);
        Assert.Contains("단계 고정\t1", t);
        Assert.Contains("제외 레이어\tOFF2", t);
        Assert.Contains("외곽선 끄기\t예", t);
    }

    [Fact]
    public void Report_Lists_The_Cleaning_Steps()
    {
        var t = Report(new LiteBuild(Plant()), 2);
        Assert.Contains("[정리 단계]", t);
        Assert.Contains("단계\t제거 개수\t줄인 바이트", t);
        foreach (var name in new[] { "퇴화", "중복", "겹침", "작은 요소", "덩어리 외곽선" }) Assert.Matches("(?m)^" + name + "\t\\d+\t\\d+", t);
        Assert.Matches("(?m)^인스턴스 사이 중복\t1\t[1-9]", t);
    }

    [Fact]
    public void Report_Lists_Blobs_In_Original_Coordinates()
    {
        var t = Report(new LiteBuild(Plant()), 2);
        Assert.Contains("[덩어리 상위 30]", t);
        Assert.Contains("중심 X (원본)\t중심 Y (원본)\t대각선 mm\t선 밀도 mm/m²\t지운 선 수\t인스턴스 수", t);
        Assert.Matches("(?m)^131\\d{3}(\\.\\d+)?\t51\\d{3}(\\.\\d+)?\t", t); // the mesh at (130000, 50000), 2 m wide
    }

    [Fact]
    public void Report_Shows_No_Position_For_A_Blob_No_Longer_Placed()
    {
        var d = Drawing(4, Robot(), Robot(n: 30));
        Use(d, 0, 1, Move(600, 600));
        Assert.Matches("(?m)^-\t-\t-\t\\d", Report(new LiteBuild(d), 2));
    }

    [Fact]
    public void Report_Keeps_The_Origin_Section_Word_For_Word()
    {
        var b = new LiteBuild(Drawing(4, new Geo3[] { L3(123_456, 45_678, 200_000, 45_678) }));
        var t = Report(b, 0);
        Assert.Contains("[원점 이동]", t);
        Assert.Contains("이동량 X\t123000", t);
        Assert.Contains("이동량 Y\t45000", t);
        Assert.Contains("OriginX\t-123000", t);
        Assert.Contains("OriginY\t-45000", t);
        Assert.Contains("경량 좌표 = 원본 좌표 - 이동량. 경량 도면의 범위: (456, 678) ~ (77000, 678)", t);
        Assert.Contains("ash.settings.tsv에 넣을 값\t경량 도면에 그린 경로를 원본 도면 좌표 기준 모델로 만들 때(원본에서 Origin 0이었다면):", t);
        Assert.Contains("참고\t모델 좌표 = (도면 좌표 - Origin) x Scale. 원본에서 Origin (Ox, Oy)를 쓰던 프로젝트는 OriginX = Ox - 이동량 X, OriginY = Oy - 이동량 Y", t);
    }

    [Fact]
    public void Report_Has_The_Histogram_Layers_Entities_And_Time()
    {
        var t = Report(new LiteBuild(Plant()), 2);
        Assert.Contains("[밀도 히스토그램]", t);
        Assert.Contains("점유 셀\t", t);
        Assert.Contains("소요 시간\t83.5 s", t);
        Assert.Contains("[레이어]", t);
        Assert.Contains("레이어\t제외\t엔티티\t예상 크기 (KB)", t);
        Assert.Matches("(?m)^WALL\t아니오\t1\t", t);
        Assert.Matches("(?m)^OFF\t예\t0\t", t);
        Assert.Contains("[경량 도면 엔티티]", t);
        Assert.Contains("INSUNITS 4 = mm", t);
        Assert.Contains("판정\t목표 충족", t);
    }

    [Fact]
    public void Report_Names_The_Budget_Overrun_By_Nodes()
    {
        var d = Drawing(4, Array.Empty<Geo3>(), new Geo3[] { L3(0, 0, 1000, 0), L3(0, 300, 1000, 300), L3(0, 600, 1000, 600), L3(0, 900, 1000, 900) });
        Use(d, 0, 1, Move(0));
        var b = new LiteBuild(d) { Budget = 2 };
        var t = Report(b, 2);
        Assert.Equal(1, b.Run(b.Thresholds(2)).Truncated);
        Assert.Contains("예산 초과\t1개 노드", t);
        Assert.Contains("밀도 예산 초과\t예", t);
        Assert.DoesNotContain("예산 초과", Report(new LiteBuild(Plant()), 2));
    }

    [Fact]
    public void Over_10_Mb_Report_States_The_Blobs_And_Layers_Left()
    {
        var b = new LiteBuild(Plant());
        var over = Report(b, 4, 11L * 1048576);
        Assert.Contains("판정\t10 MB 초과", over);
        Assert.Contains("[덩어리 상위 30]", over);
        Assert.Matches("(?m)^큰 덩어리 1\t", over);
        Assert.Matches("(?m)^큰 레이어 1\t", over);
        var under = Report(b, 4, 6L * 1048576);
        Assert.Contains("판정\t5~10 MB", under);
        Assert.DoesNotContain("큰 덩어리", under);
    }

    [Fact]
    public void Level_Writes_Are_Stamped()
    {
        var b = new LiteBuild(Drawing(4, new Geo3[] { L3(0, 0, 1000, 0) }));
        Assert.Equal(1, b.Write(new StringWriter(), 1).Level);
        Assert.Equal(-1, b.Write(new StringWriter(), b.Thresholds(1)).Level);
    }

    [Fact]
    public void Measure_Gives_Every_Level_Its_Size_And_Null_When_Stopped()
    {
        var d = Drawing(4, new Geo3[] { L3(0, 0, 1000, 0), L3(0, 500, 1000, 500) });
        var heard = new List<int>();
        var all = LiteJob.Measure(new LiteBuild(d), default, heard.Add)!;
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, heard);
        Assert.Equal(5, all.Count);
        var b = new LiteBuild(d);
        Assert.Equal(b.Total(1), all[1].Bytes);
        Assert.All(all, e => Assert.Equal(0, e.Blobs));
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        Assert.Null(LiteJob.Measure(new LiteBuild(d), stop.Token, _ => { }));
    }

    [Fact]
    public void Describe_Names_Only_What_A_Person_Fixed()
    {
        Assert.Null(new LiteSettings().Describe());
        var s = new LiteSettings { Level = 3, ExcludeLayers = { "A", "B" }, NoBlobs = true };
        Assert.Equal("저장된 조정값: 단계 3 고정, 레이어 2개 제외, 외곽선 끔 · AGVLITEX에서 자동으로", s.Describe());
        Assert.Equal("저장된 조정값: 단계 0 고정 · AGVLITEX에서 자동으로", new LiteSettings { Level = 0 }.Describe());
    }
}
