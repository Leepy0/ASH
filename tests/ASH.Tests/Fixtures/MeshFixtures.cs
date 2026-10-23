namespace ASH.Tests.Fixtures;

internal static class MeshFixtures
{
    public const string StlAscii2Solids =
@"solid body
facet normal 0 0 1
 outer loop
  vertex 0 0 0
  vertex 1 0 0
  vertex 0 1 0
 endloop
endfacet
facet normal 0 0 1
 outer loop
  vertex 1 0 0
  vertex 1 1 0
  vertex 0 1 0
 endloop
endfacet
endsolid body
solid wheel
facet normal 0 0 1
 outer loop
  vertex 0 0 1
  vertex 2.5e0 0 1
  vertex 0 2 1
 endloop
endfacet
endsolid wheel
";

    public const string ObjQuad =
@"o box
v 0 0 0
v 1 0 0
v 1 1 0
v 0 1 0
f 1 2 3 4
";
}
