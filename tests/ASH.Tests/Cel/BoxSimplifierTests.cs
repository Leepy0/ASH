using ASH.Core.Cel;

namespace ASH.Tests.Cel;

public class BoxSimplifierTests
{
    // Closed cuboid (12 triangles), mm.
    static MeshPart Cuboid(string name, double x0, double x1, double y0, double y1, double z0, double z1)
    {
        var v = new Vec3[8];
        for (int i = 0; i < 8; i++)
            v[i] = new Vec3((i & 1) == 0 ? x0 : x1, (i & 2) == 0 ? y0 : y1, (i & 4) == 0 ? z0 : z1);
        int[][] q = { new[] { 0, 1, 3, 2 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 }, new[] { 0, 2, 6, 4 }, new[] { 1, 3, 7, 5 } };
        var t = new List<Triangle>();
        foreach (var f in q) { t.Add(new Triangle(v[f[0]], v[f[1]], v[f[2]])); t.Add(new Triangle(v[f[0]], v[f[2]], v[f[3]])); }
        return new MeshPart(name, t);
    }

    static MeshPart[] Parts() => new[]
    {
        Cuboid("body", 0, 2000, -500, 500, 0, 600),
        Cuboid("lamp", 1950, 2000, -50, 50, 300, 340),
        Cuboid("dust", 1000, 1010, 0, 10, 0, 10),
    };

    [Fact] public void PlusX_Heading_Puts_Front_At_Negative_Y() { var r = BoxSimplifier.Simplify(Parts(), new SimplifyOptions(Heading: Heading.PlusX, Origin: OriginAt.Center, MinPartPct: 1)); var lamp = r.Boxes.Single(b => b.Name == "lamp"); Assert.True(lamp.Y < -0.9, lamp.Y.ToString()); Assert.Equal(2.0, r.LengthM, 6); Assert.Equal(1.0, r.WidthM, 6); }
    [Fact] public void Front_Origin_Starts_At_Zero() { var r = BoxSimplifier.Simplify(Parts(), new SimplifyOptions(Origin: OriginAt.Front)); Assert.Equal(0, r.Boxes.Min(b => b.Y - b.Dy / 2), 6); }
    [Fact] public void Floor_Is_Zero_And_Small_Parts_Dropped() { var r = BoxSimplifier.Simplify(Parts(), new SimplifyOptions()); Assert.Equal(0, r.Boxes.Min(b => b.Z0), 6); Assert.DoesNotContain(r.Boxes, b => b.Name == "dust"); Assert.Equal(1, r.DroppedSmall); }
    [Fact] public void MaxBoxes_Keeps_Largest() { var r = BoxSimplifier.Simplify(Parts(), new SimplifyOptions(MaxBoxes: 1, MinPartPct: 0)); Assert.Equal("body", r.Boxes.Single().Name); Assert.Equal(2, r.DroppedOverMax); }
    [Fact] public void Grid_Splits_Single_Part() { var r = BoxSimplifier.Simplify(new[] { Parts()[0] }, new SimplifyOptions(Grid: 2, MinPartPct: 0)); Assert.Equal(4, r.Boxes.Count); }

    [Fact] public void NaN_Or_Infinite_Coordinate_Is_Rejected()
    {
        foreach (var bad in new[] { double.NaN, double.PositiveInfinity })
        {
            var p = new[] { Cuboid("body", 0, 2000, -500, 500, 0, 600), new MeshPart("bad", new[] { new Triangle(new Vec3(0, 0, 0), new Vec3(bad, 0, 0), new Vec3(0, 1, 0)) }) };
            Assert.Contains("숫자가 아닌 좌표", Assert.Throws<ArgumentException>(() => BoxSimplifier.Simplify(p, new SimplifyOptions())).Message);
        }
    }

    // Extra: heading/rotation conventions and naming.
    [Fact] public void MinusY_Heading_Is_Identity_And_Rotate90_Turns_Ccw()
    {
        var p = new[] { Cuboid("a", 0, 1000, 0, 3000, 0, 500) };   // 1 m across x, 3 m along y
        var id = BoxSimplifier.Simplify(p, new SimplifyOptions(Heading: Heading.MinusY));
        Assert.Equal(3.0, id.LengthM, 6); Assert.Equal(1.0, id.WidthM, 6); Assert.Equal(0.5, id.HeightM, 6);
        var rot = BoxSimplifier.Simplify(p, new SimplifyOptions(Heading: Heading.MinusY, RotateDeg: 90));
        Assert.Equal(1.0, rot.LengthM, 6); Assert.Equal(3.0, rot.WidthM, 6);
        Assert.Throws<ArgumentException>(() => BoxSimplifier.Simplify(p, new SimplifyOptions(RotateDeg: 45)));
    }

    [Fact] public void Back_Origin_Ends_At_Zero() { var r = BoxSimplifier.Simplify(Parts(), new SimplifyOptions(Origin: OriginAt.Back)); Assert.Equal(0, r.Boxes.Max(b => b.Y + b.Dy / 2), 6); }

    [Fact] public void Names_Are_Sanitized_And_Deduplicated()
    {
        var p = new[] { Cuboid("Body Shell-1", 0, 2000, 0, 1000, 0, 500), Cuboid("Body Shell-1", 0, 1000, 0, 1000, 0, 500) };
        var r = BoxSimplifier.Simplify(p, new SimplifyOptions());
        Assert.Equal(new[] { "Body_Shell_1", "Body_Shell_1_2" }, r.Boxes.Select(b => b.Name).ToArray());
    }

    [Fact] public void No_Triangles_Throws() => Assert.Throws<ArgumentException>(() => BoxSimplifier.Simplify(new[] { new MeshPart("x", new Triangle[0]) }, new SimplifyOptions()));
}
