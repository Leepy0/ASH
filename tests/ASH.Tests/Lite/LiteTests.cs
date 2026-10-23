using System.Diagnostics;
using System.Text.RegularExpressions;
using ASH.Core.Dxf;
using ASH.Core.Lite;

namespace ASH.Tests.Lite;

public class LiteLayerTests
{
    [Theory]
    [InlineData("A-WALL", true)]
    [InlineData("COLUMN", true)]
    [InlineData("건물", true)]
    [InlineData("설비_라인", true)]
    [InlineData("0", true)]
    [InlineData("GRID", true)]
    [InlineData("DIM", false)]
    [InlineData("A-ANNO-NOTE", false)]
    [InlineData("mtext", false)]
    [InlineData("Defpoints", false)]
    [InlineData("치수", false)]
    [InlineData("문자_1", false)]
    [InlineData("해치", false)]
    [InlineData("TITLE_BLOCK", false)]
    [InlineData("표제란", false)]
    [InlineData("AGV_PATH", false)]
    [InlineData("agv_cp_load", false)]
    public void KeepByDefault_Follows_Keywords(string layer, bool keep) => Assert.Equal(keep, LiteLayers.KeepByDefault(layer));

    [Fact]
    public void R12Names_Are_Legal_Unique_And_Stable()
    {
        var src = new[] { "0", "a-wall", "X|WALL", "건물", "벽체", "A WALL", "A_WALL", new string('L', 40), new string('L', 40) + "2", "$dollar", "Defpoints" };
        var map = LiteLayers.R12Names(src);
        Assert.Equal("0", map["0"]);
        Assert.Equal("A-WALL", map["a-wall"]);
        Assert.Equal("X_WALL", map["X|WALL"]);
        Assert.StartsWith("LAYER_", map["건물"]);
        Assert.NotEqual(map["건물"], map["벽체"]);
        Assert.NotEqual(map["A WALL"], map["A_WALL"]);
        Assert.Equal("$DOLLAR", map["$dollar"]);
        Assert.All(map.Values, v => Assert.Matches(new Regex("^[A-Z0-9$_-]{1,31}$"), v));
        Assert.Equal(map.Count, map.Values.Distinct().Count());
        Assert.Equal(map, LiteLayers.R12Names(Enumerable.Reverse(src)));
    }
}

public class CurveApproxTests
{
    // largest distance from a densely sampled true curve to the chord polyline
    static double MaxDev(Func<double, (double X, double Y)> f, double t0, double t1, List<(double X, double Y)> pts)
    {
        double worst = 0;
        for (int i = 0; i <= 4000; i++)
        {
            var p = f(t0 + (t1 - t0) * i / 4000);
            double best = double.MaxValue;
            for (int j = 1; j < pts.Count; j++) best = Math.Min(best, Seg(p, pts[j - 1], pts[j]));
            worst = Math.Max(worst, best);
        }
        return worst;
    }

    static double Seg((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, l = dx * dx + dy * dy, t = l > 0 ? Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l)) : 0;
        return Math.Sqrt(Math.Pow(a.X + t * dx - p.X, 2) + Math.Pow(a.Y + t * dy - p.Y, 2));
    }

    [Fact]
    public void Arc_Chords_Stay_Within_Tolerance()
    {
        var pts = CurveApprox.Arc(10, 20, 1000, 0.3, 2.5, 0.5);
        Assert.True(MaxDev(t => (10 + 1000 * Math.Cos(t), 20 + 1000 * Math.Sin(t)), 0.3, 2.8, pts) <= 0.5 + 1e-9);
        Assert.InRange(pts.Count, 20, 200); // not wildly over-sampled either
    }

    [Fact]
    public void Ellipse_Chords_Stay_Within_Tolerance()
    {
        // major (3000, 1000), minor perpendicular at ratio 0.3, half the ellipse
        double mx = 3000, my = 1000, nx = -300, ny = 900;
        var pts = CurveApprox.Ellipse(5, 5, mx, my, nx, ny, 0, Math.PI, 2);
        Assert.True(MaxDev(t => (5 + mx * Math.Cos(t) + nx * Math.Sin(t), 5 + my * Math.Cos(t) + ny * Math.Sin(t)), 0, Math.PI, pts) <= 2 + 1e-9);
        Assert.Equal((5 + mx, 5 + my), pts[0]);
    }

    [Fact]
    public void Adaptive_Sampling_Meets_Tolerance_And_Caps_Points()
    {
        (double, double) Wave(double t) => (t, 50 * Math.Sin(t / 30));
        var pts = CurveApprox.Adaptive(Wave, 0, 600, 0.2, 8);
        Assert.True(MaxDev(Wave, 0, 600, pts) <= 0.2 + 1e-6);
        Assert.Equal(Wave(600), pts[pts.Count - 1]);
        var capped = CurveApprox.Adaptive(Wave, 0, 600, 1e-12, 8);
        Assert.True(capped.Count <= CurveApprox.MaxPoints);
    }

    [Fact]
    public void Similarity_Keeps_Arcs_And_Mirror_Flips_Direction()
    {
        // rotate 90 degrees, scale 2, move (100, 0)
        var rot = new Affine2(0, -2, 2, 0, 100, 0);
        var a = (LiteArc)CurveApprox.Transform(new LiteArc("A", 10, 0, 5, 0, 90), rot, 0.1, "A");
        Assert.Equal(100, a.Cx, 9); Assert.Equal(20, a.Cy, 9); Assert.Equal(10, a.R, 9);
        Assert.Equal(90, a.StartDeg, 9); Assert.Equal(180, a.EndDeg, 9);
        // mirror about the y axis: the quarter 0..90 lands on 90..180, still counter-clockwise
        var m = (LiteArc)CurveApprox.Transform(new LiteArc("A", 0, 0, 5, 0, 90), new Affine2(-1, 0, 0, 1, 0, 0), 0.1, "B");
        Assert.Equal(90, m.StartDeg, 9); Assert.Equal(180, m.EndDeg, 9); Assert.Equal("B", m.Layer);
        var p = (LitePolyline)CurveApprox.Transform(new LitePolyline("A", new[] { new LiteVertex(0, 0, 1), new LiteVertex(10, 0) }, false), new Affine2(-1, 0, 0, 1, 0, 0), 0.1, "A");
        Assert.Equal(-1, p.Vertices[0].Bulge);
    }

    [Fact]
    public void Stretch_Turns_Circle_Into_Chords_On_The_Ellipse()
    {
        var xf = new Affine2(3, 0, 0, 1, 0, 0); // x stretched 3x
        var p = Assert.IsType<LitePolyline>(CurveApprox.Transform(new LiteCircle("A", 0, 0, 100), xf, 0.5, "A"));
        Assert.True(p.Closed);
        Assert.All(p.Vertices, v => Assert.Equal(1, v.X * v.X / 90000 + v.Y * v.Y / 10000, 6));
        var pts = p.Vertices.Select(v => (v.X, v.Y)).Append((p.Vertices[0].X, p.Vertices[0].Y)).ToList();
        Assert.True(MaxDev(t => (300 * Math.Cos(t), 100 * Math.Sin(t)), 0, 2 * Math.PI, pts) <= 0.5 + 1e-9);
    }
}

public class LiteDxfWriterTests
{
    static readonly Dictionary<string, short> NoColors = new();

    static string Write(IReadOnlyList<LiteEntity> ents, double dx = 0, double dy = 0)
    {
        var names = LiteLayers.R12Names(ents.Select(e => e.Layer));
        var sw = new StringWriter();
        LiteDxfWriter.Write(sw, ents, names, NoColors, dx, dy);
        return sw.ToString();
    }

    // segments DxfLite makes from each entity (its own curve rules)
    static int Segs(LiteEntity e) => e switch
    {
        LiteLine => 1,
        LiteCircle => 36,
        LiteArc a => Math.Max(1, (int)Math.Ceiling(36 * ((a.EndDeg - a.StartDeg + 360) % 360 is var s && s == 0 ? 360 : s) / 360)),
        LitePolyline p => p.Closed && p.Vertices.Count > 2 ? p.Vertices.Count : p.Vertices.Count - 1,
        _ => throw new ArgumentException(),
    };

    [Fact]
    public void Output_Is_R12_And_Reads_Back_With_DxfLite()
    {
        var ents = new LiteEntity[]
        {
            new LiteLine("wall", 1000, 2000, 5000, 2000),
            new LitePolyline("설비", new[] { new LiteVertex(1000, 1000), new LiteVertex(2000, 1000), new LiteVertex(2000, 1500) }, true),
            new LitePolyline("wall", new[] { new LiteVertex(0, 0), new LiteVertex(10, 0), new LiteVertex(10, 10), new LiteVertex(0, 10) }, false),
            new LiteCircle("COL", 3000, 3000, 250),
            new LiteArc("COL", 4000, 4000, 100, 30, 120),
        };
        var text = Write(ents, 1000, 0);
        Assert.Contains("AC1009", text);
        Assert.DoesNotContain("LWPOLYLINE", text);
        var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
        for (int i = 0; i + 1 < lines.Length; i += 2) Assert.True(int.TryParse(lines[i], out _), $"line {i}: '{lines[i]}' is not a group code");
        Assert.All(text, ch => Assert.True(ch < 128, "ASCII only"));

        var d = DxfLite.Read(new StringReader(text));
        Assert.Equal(ents.Sum(Segs), d.Segs.Count);
        Assert.Equal(3, d.Layers.Count);
        Assert.Contains("WALL", d.Layers); Assert.Contains("COL", d.Layers); Assert.Contains(d.Layers, l => l.StartsWith("LAYER_"));
        Assert.Contains(new DxfSeg(0, 2000, 4000, 2000, "WALL"), d.Segs); // moved by -1000 in x
        // extents agree with the true geometry within the reader's arc chord error
        var box = LiteBox.Of(ents);
        Assert.Equal(box.MinX - 1000, d.Segs.Min(s => Math.Min(s.X1, s.X2)), 6);
        Assert.Equal(box.MaxY, d.Segs.Max(s => Math.Max(s.Y1, s.Y2)), 0);
        Assert.InRange(d.Segs.Max(s => Math.Max(s.X1, s.X2)), box.MaxX - 1000 - 2, box.MaxX - 1000);
        Assert.Contains("$EXTMIN\r\n 10\r\n-1000.0\r\n 20\r\n0.0", text);
    }

    [Fact]
    public void Bulged_Polyline_Round_Trips_As_Arc()
    {
        // semicircle 0,0 -> 10,0 (bulge 1): DxfLite cuts it into 12 chords on the circle centre (5,0) r 5
        var text = Write(new[] { new LitePolyline("A", new[] { new LiteVertex(0, 0, 1), new LiteVertex(10, 0) }, false) });
        Assert.Contains(" 42\r\n1.0\r\n", text);
        var d = DxfLite.Read(new StringReader(text));
        Assert.Equal(12, d.Segs.Count);
        Assert.All(d.Segs, s => Assert.Equal(5, Math.Sqrt(Math.Pow(s.X1 - 5, 2) + s.Y1 * s.Y1), 9));
    }

    [Fact]
    public void Bytes_Match_What_Write_Emits()
    {
        var ents = new LiteEntity[] { new LiteLine("A", 1.5, 2, 3, 4), new LiteCircle("A", 0, 0, 1), new LitePolyline("A", new[] { new LiteVertex(0, 0, 0.5), new LiteVertex(1, 1) }, true) };
        long sum = ents.Sum(e => LiteDxfWriter.Bytes(e, "A", 0, 0));
        var empty = Write(Array.Empty<LiteEntity>()).Length;
        Assert.InRange(Write(ents).Length - empty - sum, 0, 120); // only the one extra layer row and the extents' digits differ
        Assert.InRange(empty, LiteDxfWriter.OverheadBytes(0) * 0.8, LiteDxfWriter.OverheadBytes(0) * 1.2);
    }
}

public class LitePlanTests
{
    static LiteSource Sample()
    {
        var s = new LiteSource { InsUnits = 4, Tolerance = 10 };
        s.Layers["WALL"] = new LiteLayerState(1, false, false);
        s.Layers["DIM"] = new LiteLayerState(2, false, false);
        s.Layers["OLD"] = new LiteLayerState(3, true, false);
        s.Items.Add(new LiteItem(new LiteLine("WALL", 123456, 45678, 130000, 45678), LiteFlags.None));
        s.Items.Add(new LiteItem(new LiteLine("DIM", 123456, 45678, 124000, 46000), LiteFlags.None));
        s.Items.Add(new LiteItem(new LiteLine("OLD", 0, 0, 1, 1), LiteFlags.Hidden));
        s.Items.Add(new LiteItem(new LiteLine("WALL", 200000, 50000, 201000, 50000), LiteFlags.Xref));
        s.Items.Add(new LiteItem(new LitePolyline("WALL", new[] { new LiteVertex(125000, 46000), new LiteVertex(126000, 46000), new LiteVertex(126000, 47000), new LiteVertex(125000, 47000) }, true), LiteFlags.Footprint));
        s.Solids3d = 1; s.XrefRefs = 1;
        LiteSource.Count(s.Removed, "해치", 7);
        LiteSource.Count(s.Before, "LINE", 3);
        return s;
    }

    [Theory]
    [InlineData(123456.7, 4, 123000)]
    [InlineData(5, 4, 0)]
    [InlineData(-0.5, 6, -1)]
    [InlineData(12.3, 1, 10)]
    [InlineData(-12345.6, 0, -13000)]
    public void Shift_Rounds_Down_To_A_Clean_Step(double min, int insUnits, double shift) => Assert.Equal(shift, LitePlan.Shift(min, insUnits), 9);

    [Fact]
    public void Options_And_Layers_Select_Items_And_Shift_Follows_What_Is_Written()
    {
        var plan = new LitePlan(Sample());
        Assert.Equal(new[] { "DIM", "OLD", "WALL" }, plan.Layers);
        Assert.False(plan.DefaultOn("DIM"));
        var o = new LiteOptions();
        Assert.Equal(1, plan.Layer("WALL", o).Count);
        Assert.Equal(0, plan.Layer("OLD", o).Count);
        Assert.Equal(3, plan.Layer("WALL", o with { IncludeXref = true, Footprints = true }).Count);
        Assert.Equal(1, plan.Layer("OLD", o with { SkipHidden = false }).Count);

        var on = new[] { "WALL" };
        var sw = new StringWriter();
        var r = plan.Write(sw, o with { Footprints = true }, on);
        Assert.Equal(2, r.Entities);
        Assert.Equal(123000, r.ShiftX); Assert.Equal(45000, r.ShiftY);
        var d = DxfLite.Read(new StringReader(sw.ToString()));
        Assert.Equal(1 + 4, d.Segs.Count);
        Assert.Contains(new DxfSeg(456, 678, 7000, 678, "WALL"), d.Segs);
        // the estimate uses the provisional (all-geometry) shift, here 0, so its coordinates carry 3 more digits: a little high, never far off
        Assert.InRange((double)sw.ToString().Length / plan.Total(o with { Footprints = true }, on), 0.8, 1.05);
    }

    [Fact]
    public void Report_Has_Origin_Removed_Counts_And_Layer_Table()
    {
        var plan = new LitePlan(Sample());
        var o = new LiteOptions(); var on = new[] { "WALL", "OLD" };
        var r = plan.Write(new StringWriter(), o, on);
        var w = new StringWriter();
        LiteReport.Write(w, plan, o, on, r, @"D:\p\a.dwg", 123456789, @"D:\p\lite\a_lite.dxf", 2000, new DateTime(2026, 10, 23, 9, 0, 0));
        var t = w.ToString();
        Assert.Contains("OriginX\t-123000", t);
        Assert.Contains("OriginY\t-45000", t);
        Assert.Contains("해치\t7", t);
        Assert.Contains("3D 솔리드·메시·서피스\t1 (제거)", t);
        Assert.Contains("꺼진·동결 레이어의 선·곡선\t1", t);
        Assert.Contains("선택 해제한 레이어의 선·곡선\t1", t); // DIM
        Assert.Contains("DIM\tDIM\t켜짐\t아니오\t1\t", t);
        Assert.Contains("INSUNITS 4 = mm", t);
        Assert.Contains("목표(5 MB 이하) 충족", t);
    }

    [Fact]
    public void Thousands_Of_Random_Entities_Estimate_Well_And_Write_Fast()
    {
        var rnd = new Random(7);
        var s = new LiteSource { InsUnits = 4 };
        double R() => 500000 + rnd.NextDouble() * 200000;
        for (int i = 0; i < 6000; i++)
        {
            var layer = "L" + rnd.Next(20);
            LiteEntity e = (i % 4) switch
            {
                0 => new LiteLine(layer, R(), R(), R(), R()),
                1 => new LiteCircle(layer, R(), R(), rnd.NextDouble() * 1000),
                2 => new LiteArc(layer, R(), R(), rnd.NextDouble() * 1000, rnd.NextDouble() * 360, rnd.NextDouble() * 360),
                _ => new LitePolyline(layer, Enumerable.Range(0, 2 + rnd.Next(20)).Select(_ => new LiteVertex(R(), R(), rnd.Next(4) == 0 ? rnd.NextDouble() - 0.5 : 0)).ToArray(), rnd.Next(2) == 0),
            };
            s.Items.Add(new LiteItem(e, LiteFlags.None));
            s.Layers[layer] = new LiteLayerState(7, false, false);
        }
        var watch = Stopwatch.StartNew();
        var plan = new LitePlan(s);
        var sw = new StringWriter();
        var r = plan.Write(sw, new LiteOptions(), plan.Layers.ToList());
        watch.Stop();
        Assert.True(watch.Elapsed.TotalSeconds < 10, $"took {watch.Elapsed.TotalSeconds:0.0} s");
        Assert.Equal(6000, r.Entities);
        long est = plan.Total(new LiteOptions(), plan.Layers.ToList());
        Assert.InRange((double)sw.ToString().Length / est, 0.98, 1.02);
        var d = DxfLite.Read(new StringReader(sw.ToString()));
        Assert.False(d.Truncated);
        Assert.Equal(20, d.Layers.Count);
        Assert.InRange(d.Segs.Min(x => Math.Min(x.X1, x.X2)), -1, 1000); // shifted near the origin (polyline bulges may reach a little below the vertex box)
    }
}
