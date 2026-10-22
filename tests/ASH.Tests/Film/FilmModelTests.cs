using ASH.Core.Film;
using ASH.Core.Geometry;
using ASH.Core.Model;

namespace ASH.Tests.Film;

public class FilmModelTests
{
    static string Amo() => ModelAmoWriter.Render("m", "Meters", 2, new[] { "agv" }, new Rect2(new Point2(0, 0), new Point2(10, 10)), autoView: true);

    [Fact]
    public void Recording_Window_Splits_The_Control_Line()
    {
        var amo = ModelAmoWriter.WithRecording(Amo(), 7200, 1800, 3600);
        Assert.Contains("CONTROL snaplen 0.5 Hours counts 1\nCONTROL snaplen 0.5 Hours counts 1 autoview\nCONTROL snaplen 1 Hours counts 1 autorep\n", amo);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(amo, "autoview"));
        Assert.StartsWith("VERSION ", amo);
        // a window at the start or the end leaves no empty snap, and the report stays on the last one
        Assert.Contains("CONTROL snaplen 1 Hours counts 1 autoview\nCONTROL snaplen 1 Hours counts 1 autorep\n", ModelAmoWriter.WithRecording(Amo(), 7200, 0, 3600));
        Assert.Contains("CONTROL snaplen 1 Hours counts 1\nCONTROL snaplen 1 Hours counts 1 autorep autoview\n", ModelAmoWriter.WithRecording(Amo(), 7200, 3600, 9999));
        // applied twice (a rerun on the same copy): still one set of CONTROL lines
        Assert.Equal(amo, ModelAmoWriter.WithRecording(amo, 7200, 1800, 3600));
    }

    [Fact]
    public void Display_Lines_Go_Before_The_First_Time()
    {
        var cut = new CutSpec("c", "n", CutKind.Hold, 100, 60, new Rect(0, 0, 10, 10), null, 0, 0, 0, 10, 0, 0, false, true);
        var s = CameraScript.Build(cut, 80, 1280, 720, 15, 80, @"C:\f\cut01", new[] { "turn system ash_cal picture System off" });
        Assert.True(s.IndexOf("turn system ash_cal picture System off", StringComparison.Ordinal) < s.IndexOf("define movie spec", StringComparison.Ordinal));
        Assert.Equal(@"C:\f\cut01", AutoViewRenderer.MovieFile(s));
    }

    [Theory]
    [InlineData("a", "a")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("", "\"\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"C:\dir with space\", "\"C:\\dir with space\\\\\"")]
    public void Arguments_Are_Quoted_For_The_Command_Line(string arg, string quoted) => Assert.Equal(quoted, AutoViewRenderer.Quote(arg));
}
