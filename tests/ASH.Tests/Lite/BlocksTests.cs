using System.Text.RegularExpressions;
using ASH.Core.Cel;
using ASH.Core.Dxf;
using ASH.Core.Lite;

namespace ASH.Tests.Lite;

/// <summary>LiteBuild over block definitions: leaves first, variants per orientation (R7), instances judged per placement (R2a), layers before cleaning (R8), cycles cut (R9).</summary>
public class BlocksTests
{
    static readonly LiteThresholds L2 = BlobsTests.L2;
    static readonly Vec3 Up = new(0, 0, 1);
    static readonly Affine3 RotX90 = new(1, 0, 0, 0, 0, 0, -1, 0, 0, 1, 0, 0), RotXm90 = new(1, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, 0);

    static Affine3 Move(double x, double y = 0, double scale = 1) => new(scale, 0, 0, x, 0, scale, 0, y, 0, 0, scale, 0);
    static Line3 L3(double x1, double y1, double x2, double y2, string layer = "A") => new(layer, new Vec3(x1, y1, 0), new Vec3(x2, y2, 0));
    static IEnumerable<Geo3> Square(double x, double y, double s, string layer) => new[] { L3(x, y, x + s, y, layer), L3(x + s, y, x + s, y + s, layer), L3(x + s, y + s, x, y + s, layer), L3(x, y + s, x, y, layer) };
    static IEnumerable<Geo3> Geo(IEnumerable<LiteEntity> flat) => flat.Select(e => e switch
    {
        LiteLine l => (Geo3)new Line3(l.Layer, new Vec3(l.X1, l.Y1, 0), new Vec3(l.X2, l.Y2, 0)),
        LitePolyline p => new Poly3(p.Layer, p.Vertices, p.Closed, Up, 0),
        _ => throw new InvalidOperationException(),
    });

    static LiteBlocks Drawing(params IEnumerable<Geo3>[] defs)
    {
        var d = new LiteBlocks { InsUnits = 4 };
        foreach (var g in defs) d.Defs.Add(new BlockDef { Geo = g.ToList() });
        return d;
    }

    static void Use(LiteBlocks d, int parent, int child, Affine3 xf, string layer = "0") => d.Defs[parent].Uses.Add(new BlockUse(child, xf, layer));

    static string Dxf(LiteBuild b, LiteThresholds t)
    {
        var sw = new StringWriter();
        b.Write(sw, t);
        return sw.ToString();
    }

    /// <summary>(kind, layer) of every entity in the ENTITIES section, in file order.</summary>
    static List<(string Kind, string Layer)> Entities(string dxf) =>
        Regex.Matches(dxf.Substring(dxf.IndexOf("ENTITIES", StringComparison.Ordinal)), "  0\r\n(LINE|POLYLINE|CIRCLE|ARC)\r\n  8\r\n([^\r]*)\r\n").Cast<Match>().Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToList();

    [Fact]
    public void Leaves_First_Robot_Is_An_Outline_Fence_And_Line_Stay_Lines()
    {
        // root ⊃ line ⊃ 3 cells (10 m fence) ⊃ robot (2 m mesh)
        var d = Drawing(Array.Empty<Geo3>(), Array.Empty<Geo3>(), Square(-4000, -4000, 10_000, "FENCE"), Geo(BlobsTests.Robot(0, 0)));
        Use(d, 0, 1, Affine3.Identity);
        for (int i = 0; i < 3; i++) Use(d, 1, 2, Move(i * 12_000));
        Use(d, 2, 3, Affine3.Identity);
        var b = new LiteBuild(d);
        foreach (var t in new[] { L2, BlobsTests.L3, BlobsTests.L4 })
        {
            var run = b.Run(t);
            var cell = Assert.Single(Assert.Single(run.Root.Uses).Child.Uses.Select(u => u.Child).Distinct());
            var fence = Assert.IsType<LitePolyline>(Assert.Single(cell.Own));
            Assert.True(fence.Closed);
            Assert.InRange(LiteBox.Of(new[] { fence }).Diagonal, 14_000, 14_200);
            var robot = Assert.Single(cell.Uses).Child;
            var outline = Assert.IsType<LitePolyline>(Assert.Single(robot.Own));
            Assert.InRange(LiteBox.Of(new[] { outline }).Diagonal, 2800, 3200);
            Assert.Equal(1, run.Stats.Blobs);
        }

        var est = b.Estimate(L2);
        Assert.Equal((3, 3), (est["FENCE"].Count, est["ROBOT"].Count));
        var ents = Entities(Dxf(b, L2));
        Assert.Equal(6, ents.Count);
        Assert.All(ents, e => Assert.Equal("POLYLINE", e.Kind));
        Assert.Equal(3, ents.Count(e => e.Layer == "ROBOT"));
        Assert.True(b.Total(L2) > 0);
    }

    [Fact]
    public void One_Block_At_Identity_Writes_The_Same_Bytes_As_No_Block()
    {
        var scene = BlobsTests.Robot(3000, 3000).Concat(BlobsTests.Walls()).Concat(BlobsTests.Columns()).ToList();
        var flat = Drawing(Geo(scene));
        var blocked = Drawing(Array.Empty<Geo3>(), Geo(scene));
        Use(blocked, 0, 1, Affine3.Identity);
        foreach (var t in new[] { L2, BlobsTests.L3, BlobsTests.L4 })
            Assert.Equal(Dxf(new LiteBuild(flat), t), Dxf(new LiteBuild(blocked), t));
        var ents = Entities(Dxf(new LiteBuild(blocked), L2));
        Assert.Equal(2, ents.Count(e => e.Kind == "LINE" && e.Layer == "WALL"));
        Assert.Equal(4, ents.Count(e => e.Layer == "COL"));
        Assert.Single(ents.Where(e => e.Layer == "ROBOT"));
    }

    [Fact]
    public void A_Definition_Placed_Flat_And_Tilted_Gives_Two_Plans()
    {
        // Sq: 1 m square in XY. P: its own square plus Sq tilted back (-90) so that under P at +90 it lies flat again.
        var d = Drawing(Array.Empty<Geo3>(), Square(0, 0, 1000, "SQ"), Square(0, 0, 1000, "P"));
        Use(d, 0, 1, Affine3.Identity);
        Use(d, 0, 1, RotX90.Then(Move(3000)));
        Use(d, 2, 1, RotXm90.Then(Move(0, 2000)));
        Use(d, 0, 2, RotX90.Then(Move(5000)));
        var b = new LiteBuild(d);
        var run = b.Run(L2);

        Assert.Equal(2, run.Nodes.Count(n => n.Def == 1)); // plan and tilted
        var ents = Entities(Dxf(b, L2));
        Assert.Equal(2, ents.Count(e => e.Kind == "POLYLINE" && e.Layer == "SQ"));
        Assert.Equal(1, ents.Count(e => e.Kind == "LINE" && e.Layer == "SQ"));
        Assert.Equal(1, ents.Count(e => e.Kind == "LINE" && e.Layer == "P"));
        var dxf = DxfLite.Read(new StringReader(Dxf(b, L2)));
        var lines = dxf.Segs.Where(s => s.Layer == "P").ToList();
        Assert.Single(lines);
        Assert.Equal((5000, 6000), (Math.Min(lines[0].X1, lines[0].X2), Math.Max(lines[0].X1, lines[0].X2)));
    }

    [Fact]
    public void PlanHasZ_Is_Relative_To_The_Plan_Scale()
    {
        Assert.True(new Affine3(1e-10, 0, 0, 0, 0, 0, -1e-10, 0, 0, 1e-10, 0, 0).PlanHasZ); // RotX90 at scale 1e-10
        Assert.False(new Affine3(1, 0, 1e-12, 0, 0, 1, 0, 0, 0, 0, 1, 0).PlanHasZ);          // rounding noise at scale 1
    }

    [Fact]
    public void An_Instance_Smaller_Than_MinSize_Is_Not_Placed_And_A_Zero_Scale_One_Is_Skipped()
    {
        var d = Drawing(Array.Empty<Geo3>(), Square(0, 0, 100, "S"));
        Use(d, 0, 1, Move(0));
        Use(d, 0, 1, Move(1000, 0, 0.2)); // 28 mm diagonal < 50
        Use(d, 0, 1, Move(2000, 0, 0));
        var b = new LiteBuild(d);
        var run = b.Run(L2);
        Assert.Single(run.Root.Uses);
        Assert.Equal(1, run.Stats.SmallInstances);
        Assert.Single(Entities(Dxf(b, L2)));
        Assert.Equal(1, b.Estimate(L2)["S"].Count);
    }

    [Fact]
    public void A_Hidden_Duplicate_Does_Not_Delete_The_Visible_Line()
    {
        var d = Drawing(new[] { L3(0, 0, 1000, 0, "F"), L3(0, 0, 1000, 0, "V"), L3(0, 500, 1000, 500, "0") });
        d.Layers["F"] = new LiteLayerState(7, false, true);
        d.Layers["V"] = new LiteLayerState(7, false, false);
        var ents = Entities(Dxf(new LiteBuild(d), L2));
        Assert.Equal(("LINE", "0"), ents[0]);
        Assert.Equal(("LINE", "V"), ents[1]);
        Assert.Equal(2, ents.Count);
    }

    [Fact]
    public void Layer_Zero_Inherits_At_Placement_And_A_Frozen_Parent_Hides_Everything()
    {
        var d = Drawing(Array.Empty<Geo3>(), new[] { L3(0, 0, 1000, 0, "0"), L3(0, 100, 1000, 100, "V") });
        d.Layers["FZ"] = new LiteLayerState(7, false, true);
        d.Layers["OFF"] = new LiteLayerState(7, true, false);
        Use(d, 0, 1, Move(0), "ON");
        Use(d, 0, 1, Move(2000), "FZ");
        Use(d, 0, 1, Move(4000), "OFF");
        var ents = Entities(Dxf(new LiteBuild(d, new[] { "V" }), L2)); // V also excluded by the user
        Assert.Equal(new[] { ("LINE", "ON") }, ents);
    }

    [Fact]
    public void A_Cycle_Is_Cut_And_Everything_Is_Written_Once()
    {
        var d = Drawing(Array.Empty<Geo3>(), new[] { L3(0, 0, 1000, 0, "A") }, new[] { L3(0, 100, 1000, 100, "B") });
        Use(d, 0, 1, Affine3.Identity);
        Use(d, 1, 2, Affine3.Identity);
        Use(d, 2, 1, Affine3.Identity);
        var ents = Entities(Dxf(new LiteBuild(d), L2));
        Assert.Equal(new[] { ("LINE", "A"), ("LINE", "B") }, ents);
    }

    [Fact]
    public void Instances_Inside_A_Parent_Blob_Are_Dropped_As_Instances()
    {
        // P: a 2 m mesh with 1000 small crosses placed inside it; the crosses go with the blob, the result holds no expanded entities
        var d = Drawing(Array.Empty<Geo3>(), Geo(BlobsTests.Robot(0, 0)), new[] { L3(-50, 0, 50, 0, "X"), L3(0, -50, 0, 50, "X") });
        Use(d, 0, 1, Affine3.Identity);
        for (int i = 0; i < 1000; i++) Use(d, 1, 2, Move(100 + i % 30 * 60, 100 + i / 30 * 60));
        var run = new LiteBuild(d).Run(L2);
        var p = Assert.Single(run.Root.Uses).Child;
        Assert.Empty(p.Uses);
        Assert.Single(p.Own);
        Assert.Equal(1000, run.Stats.DroppedInstances);
        Assert.Equal(199 + 2000, run.Stats.Blob.Count); // the mesh pieces merged into 200 lines in D, two of them joined at the corner in E
        Assert.Equal(2, run.Nodes.Single(n => n.Def == 2).Own.Count);
    }

    [Fact]
    public void A_Child_Straddling_A_Parent_Blob_Stays_While_One_Inside_Goes()
    {
        // X: one cross; S: a cross plus another 3 m away. Both placed at (1000, 1000) in the 2 m mesh: S's second cross lands outside, in a sparse group of its own.
        var d = Drawing(Array.Empty<Geo3>(), Geo(BlobsTests.Robot(0, 0)), new[] { L3(-50, 0, 50, 0, "X"), L3(0, -50, 0, 50, "X") },
            new[] { L3(-50, 0, 50, 0, "S"), L3(0, -50, 0, 50, "S"), L3(2950, 0, 3050, 0, "S"), L3(3000, -50, 3000, 50, "S") });
        Use(d, 0, 1, Affine3.Identity);
        Use(d, 1, 2, Move(1000, 1000));
        Use(d, 1, 3, Move(1000, 1000));
        var run = new LiteBuild(d).Run(L2);
        var p = Assert.Single(run.Root.Uses).Child;
        Assert.Equal(3, Assert.Single(p.Uses).Child.Def);
        Assert.Equal(1, run.Stats.DroppedInstances);
    }

    [Fact]
    public void A_Block_Smaller_Than_The_Minimum_Goes_Whole_And_A_Big_One_Is_Judged_Inside()
    {
        var d = Drawing(new[] { L3(0, 0, 5000, 0) },                                                    // 0 model space: a wall the tiny block touches
            new[] { L3(10000, 0, 13000, 0), L3(12000, 1000, 12030, 1000) },                              // 1 big block: 3000 frame + a lone 30 detail
            new[] { L3(10000, 500, 10300, 500), L3(10300, 500, 10300, 800) },                            // 2 nested 300 x 300 block
            new[] { L3(0, 0, 40, 0) });                                                                   // 3 tiny block (40 mm)
        Use(d, 0, 1, Affine3.Identity);
        Use(d, 0, 3, Move(5000));
        Use(d, 1, 2, Affine3.Identity);
        var b = new LiteBuild(d);
        var run = b.Run(L2);

        Assert.Empty(run.Nodes.Single(n => n.Def == 3).Own);    // the tiny block goes whole, though its line touches the wall (no chaining across definitions)
        Assert.Single(run.Root.Uses);
        var big = run.Nodes.Single(n => n.Def == 1);
        Assert.Equal(3000, LiteBox.Size(Assert.Single(big.Own)), 6);  // the frame stays, the lone 30 detail inside the big block is judged by itself and goes
        var nested = Assert.Single(big.Uses).Child;
        Assert.Equal(Math.Sqrt(300 * 300 + 300 * 300), LiteBox.Of(nested.Own).Diagonal, 6); // a nested block above the minimum keeps its pieces
        Assert.Equal(2, run.Stats.Small.Count);               // the lone detail and the tiny block's line
        Assert.Equal(3, Entities(Dxf(b, L2)).Count);             // wall, frame, nested outline
    }

    [Fact]
    public void Thresholds_Follow_The_Largest_Placement_Scale()
    {
        // a 20 mm line: under MinSize 50 at scale 1, 60 mm at scale 3. The definition is cleaned at scale 3, so the line survives; the scale 1 instance is then not placed (R2a).
        var d = Drawing(Array.Empty<Geo3>(), new[] { L3(0, 0, 20, 0, "S") });
        Use(d, 0, 1, Move(0));
        Use(d, 0, 1, Move(1000, 0, 3));
        var b = new LiteBuild(d);
        var run = b.Run(L2);
        var node = Assert.Single(run.Nodes.Where(n => n.Def == 1));
        Assert.Equal(3, node.ScaleMax);
        Assert.Single(node.Own);
        Assert.Single(run.Root.Uses);
        Assert.Equal(1, run.Stats.SmallInstances);
        var seg = Assert.Single(DxfLite.Read(new StringReader(Dxf(b, L2))).Segs);
        Assert.Equal(60, Math.Abs(seg.X2 - seg.X1), 6);

        var alone = Drawing(Array.Empty<Geo3>(), new[] { L3(0, 0, 20, 0, "S") });
        Use(alone, 0, 1, Move(0));
        Assert.Empty(new LiteBuild(alone).Run(L2).Nodes.Single(n => n.Def == 1).Own);
    }

    [Fact]
    public void Over_The_Budget_The_Own_Geometry_Is_Still_Judged_And_The_Output_Stays_Complete()
    {
        var zigzag = new Points3("X", Enumerable.Range(0, 40).Select(i => new Vec3(i * 2.5, i % 2 * 100, 0)).ToList(), false);
        var d = Drawing(Array.Empty<Geo3>(), Geo(BlobsTests.Robot(0, 0)), new Geo3[] { zigzag });
        Use(d, 0, 1, Affine3.Identity);
        for (int i = 0; i < 5; i++) Use(d, 1, 2, Move(500 + i * 200, 1000));
        var b = new LiteBuild(d) { Budget = 20 }; // 39 chords in each child, but 13 instances and entities: the sheet is not cut
        var run = b.Run(L2);
        var p = Assert.Single(run.Root.Uses).Child;
        Assert.Single(p.Own);                 // the mesh became its outline
        Assert.Equal(5, p.Uses.Count);        // the children were given up, not dropped
        Assert.Equal(2, run.Truncated);       // P and the root both stream more than 20 chords from their children
        Assert.Equal(0, run.Stats.DroppedInstances);
        var ents = Entities(Dxf(b, L2));
        Assert.False(run.Stats.Partial);
        Assert.Equal(5, ents.Count(e => e.Kind == "POLYLINE" && e.Layer == "X"));
        Assert.Equal(1, ents.Count(e => e.Kind == "POLYLINE" && e.Layer == "ROBOT"));
    }

    /// <summary>Ten levels of ten inserts over one line: 10^10 lines if they were all placed.</summary>
    static LiteBlocks Runaway()
    {
        var d = Drawing(Enumerable.Range(0, 11).Select(i => (IEnumerable<Geo3>)(i == 10 ? new[] { L3(0, 0, 1000, 0, "X") } : Array.Empty<Geo3>())).ToArray());
        for (int level = 0; level < 10; level++)
            for (int i = 0; i < 10; i++) Use(d, level, level + 1, Move(i * 2000, level * 100));
        return d;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_Runaway_Drawing_Stops_At_The_Budget_And_Says_The_Output_Is_Partial(int level)
    {
        var b = new LiteBuild(Runaway()) { Budget = 1000 };
        var sw = new StringWriter();
        var r = b.Write(sw, level);
        var run = b.Run(b.Thresholds(level));
        Assert.True(run.Stats.Partial);
        Assert.Equal(1, run.Truncated);
        Assert.InRange(r.Entities, 1, 1000);
        Assert.Equal(sw.ToString().Length, b.Total(level) - r.DedupedBytes);
    }

    [Fact]
    public void The_Budget_Counts_Entities_Too()
    {
        var d = Drawing(Array.Empty<Geo3>(), Enumerable.Range(0, 100).Select(i => (Geo3)L3(0, i * 300, 5000, i * 300, "X")));
        for (int i = 0; i < 10; i++) Use(d, 0, 1, Move(i * 10000));
        var b = new LiteBuild(d) { Budget = 500 };
        var r = b.Write(new StringWriter(), LiteLevels.Max);
        Assert.True(b.Run(b.Thresholds(LiteLevels.Max)).Stats.Partial);
        Assert.Equal(400, r.Entities); // one root visit plus 101 per instance: four fit
    }

    [Fact]
    public void Within_The_Budget_Nothing_Is_Partial()
    {
        var d = Drawing(Array.Empty<Geo3>(), new[] { L3(0, 0, 5000, 0, "X") });
        for (int i = 0; i < 10; i++) Use(d, 0, 1, Move(i * 10000));
        var b = new LiteBuild(d) { Budget = 21 };
        Assert.Equal(10, b.Write(new StringWriter(), 0).Entities);
        Assert.False(b.Run(b.Thresholds(0)).Stats.Partial);
    }

    [Fact]
    public void Writing_Order_Is_Layer_Then_Definition_Then_Original()
    {
        var d = Drawing(new[] { L3(0, 0, 1000, 0, "B"), L3(0, 1, 1000, 1, "A") }, new[] { L3(0, 2, 1000, 2, "A") });
        Use(d, 0, 1, Affine3.Identity);
        var dxf = Dxf(new LiteBuild(d), L2);
        Assert.Equal(new[] { ("LINE", "A"), ("LINE", "A"), ("LINE", "B") }, Entities(dxf));
        var segs = DxfLite.Read(new StringReader(dxf)).Segs;
        Assert.Equal(new[] { 1.0, 2.0, 0.0 }, segs.Select(s => s.Y1).ToArray());
    }
}
