using System;
using System.IO;
using ASH.Core.Cel;
using ASH.Tests.Fixtures;
using Xunit;

namespace ASH.Tests.Cel;

public class AssetConverterTests
{
    [Fact] public void Stl_To_Cel_And_Svg()
    {
        var p = Path.Combine(Path.GetTempPath(), "ash-asset-" + Guid.NewGuid().ToString("N") + ".stl");
        File.WriteAllText(p, MeshFixtures.StlAscii2Solids);
        try
        {
            var r = AssetConverter.Convert(p, "agv_a", new SimplifyOptions());
            Assert.Equal(2, r.Boxes.Count);
            Assert.Equal("10000 21", r.Cel.Split('\n')[0].TrimEnd('\r'));
            Assert.StartsWith("<svg", r.Svg);
        }
        finally { File.Delete(p); }
    }
}
