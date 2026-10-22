using ASH.Core.Dxf;

namespace ASH.Tests.Dxf;

public class ModelFrameTests
{
    [Fact]
    public void Frame_Round_Trip()
    {
        var p = Path.Combine(Path.GetTempPath(), "ash_frame_" + Guid.NewGuid().ToString("N") + ".tsv");
        try
        {
            var f = new ModelFrame("Millimeters", 1234.5, -0.25, 0.001);
            f.Write(p);
            Assert.Equal(f, ModelFrame.Read(p));
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void Frame_File_Has_No_AmHome()
    {
        var p = Path.Combine(Path.GetTempPath(), "ash_frame_" + Guid.NewGuid().ToString("N") + ".tsv");
        try
        {
            new ModelFrame("Meters", 0, 0, 1).Write(p);
            var text = File.ReadAllText(p);
            Assert.DoesNotContain("AmHome", text);
            Assert.DoesNotContain("AutoMod", text);
            Assert.DoesNotContain(":\\", text);
        }
        finally { File.Delete(p); }
    }
}
