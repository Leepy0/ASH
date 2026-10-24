using System.Text;
using ASH.Core.Cel;
using ASH.Tests.Fixtures;

namespace ASH.Tests.Cel;

public class MeshReaderTests
{
    static byte[] BinaryStl(int n)
    {
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.ASCII, true))
        {
            var head = new byte[80];
            Encoding.ASCII.GetBytes("solid fake").CopyTo(head, 0);
            w.Write(head);
            w.Write((uint)n);
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < 3; k++) w.Write(0f);          // normal
                w.Write((float)i); w.Write(0f); w.Write(0f);       // A
                w.Write((float)i + 1); w.Write(0f); w.Write(0f);   // B
                w.Write((float)i); w.Write(1f); w.Write(0f);       // C
                w.Write((ushort)0);
            }
        }
        return ms.ToArray();
    }

    [Fact] public void Stl_Ascii_Splits_Solids() { var p = MeshReader.ReadStl(Encoding.ASCII.GetBytes(MeshFixtures.StlAscii2Solids)); Assert.Equal(new[]{"body","wheel"}, p.Select(x=>x.Name)); Assert.Equal(2, p[0].Triangles.Count); Assert.Equal(2.5, p[1].Triangles[0].B.X); }
    [Fact] public void Stl_Binary_With_Solid_Header_Is_Binary() { var p = MeshReader.ReadStl(BinaryStl(2)); Assert.Single(p); Assert.Equal(2, p[0].Triangles.Count); Assert.Equal(2.0, p[0].Triangles[1].B.X); }
    [Fact] public void Obj_Quad_Fans_To_Two_Triangles() { var p = MeshReader.ReadObj(new StringReader(MeshFixtures.ObjQuad)); Assert.Equal("box", p[0].Name); Assert.Equal(2, p[0].Triangles.Count); }
    [Fact] public void Obj_Bad_Face_Index_Reports_Line() { var e = Assert.Throws<FormatException>(() => MeshReader.ReadObj(new StringReader("v 0 0 0\nf 1 2 3\n"))); Assert.Contains("2", e.Message); }
    [Fact] public void Obj_Negative_Index_And_Slash_Refs() { var p = MeshReader.ReadObj(new StringReader("v 0 0 0\nv 1 0 0\nv 0 1 0\nf -3/1/1 -2//1 -1\n")); Assert.Single(p); Assert.Equal("part1", p[0].Name); Assert.Equal(1.0, p[0].Triangles[0].B.X); }
    [Fact] public void Read_Unknown_Extension_Throws() { Assert.Throws<NotSupportedException>(() => MeshReader.Read("a.step")); }
}
