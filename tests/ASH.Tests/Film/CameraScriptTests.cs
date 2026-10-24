using System.Globalization;
using ASH.Core.Film;

namespace ASH.Tests.Film;

public class CameraScriptTests
{
    static CutSpec Hold(bool persp = false, Rect? a = null) =>
        new("c1", "cut", CutKind.Hold, 400, 20, a ?? new Rect(0, 0, 55, 40), null, 0, 0, 0, 10, 0, 0, persp, false);
    static CutSpec Move() =>
        new("c2", "move", CutKind.Move, 400, 0, new Rect(0, 0, 55, 40), new Rect(100, 50, 20, 20), 3, 4, 5, 10, 45, 0, false, false);
    static string Build(CutSpec c, int fps = 15) => CameraScript.Build(c, 81, 1280, 720, fps, 80, @"C:\job\cut01");
    static string[] Lines(string s) => s.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
    static int Idx(string[] l, string startsWith) => Array.FindIndex(l, x => x.StartsWith(startsWith));

    [Fact]
    public void Script_Hold_Has_Jump_Record_And_Generate()
    {
        var l = Lines(Build(Hold()));
        Assert.Equal("camera description", l[0]);                                   // guide 3.1
        Assert.Contains("time scale 1.3333", l);                                    // guide 5.1: 10x @ 15 fps
        Assert.Contains("define movie spec", l); Assert.Contains("end movie spec", l);
        Assert.Contains("format mp4", l); Assert.Contains("filename \"C:\\job\\cut01\"", l);   // guide 3.6
        Assert.Contains("width 1280", l); Assert.Contains("height 720", l); Assert.Contains("quality 80", l); Assert.Contains("framespersec 15", l);
        int jump = Idx(l, "time 400"), view = Idx(l, "view focus relative to world"), rec = Idx(l, "record movie");
        Assert.True(0 < jump && jump < view && view < rec);
        Assert.True(Idx(l, "end movie spec") < jump);                               // movie spec before the first time statement
        Assert.Equal("time 420", l[rec + 1]);                                       // guide 9 (1): t1 = t0 + HoldSec (simulation seconds)
        Assert.Equal("generate movie", l[rec + 2]);
        Assert.Equal(l.Length - 1, rec + 2);
    }

    [Fact]
    public void Script_Move_Has_Pan_With_MoveSec()
    {
        // guide 9 (2): N=3, M=4, L=5 simulation seconds -> t0=400, 403, 407, 412
        var l = Lines(Build(Move()));
        int rec = Idx(l, "record movie"), t1 = Idx(l, "time 403");
        Assert.Equal(1, l.Count(x => x == "record movie"));
        Assert.True(rec > 0 && rec < t1);
        Assert.Equal("pan to next view", l[t1 + 1]);
        Assert.Equal("time 407", l[t1 + 2]);
        Assert.StartsWith("view focus relative to world", l[t1 + 3]);
        Assert.Contains("upvector 0, 0, 1 scale ", l[t1 + 3]);                      // tilted: up = +Z
        Assert.Equal("time 412", l[t1 + 4]);
        Assert.Equal("generate movie", l[t1 + 5]);
        Assert.Equal(2, l.Count(x => x.StartsWith("view focus")));
        Assert.Equal(12.0, Move().SimSeconds);
        Assert.Equal(1.2, Move().VideoSeconds, 9);
    }

    [Fact]
    public void Script_Orthogonal_When_Perspective_Off()
    {
        Assert.Contains("orthogonal", Lines(Build(Hold(false)))); Assert.DoesNotContain("perspective", Lines(Build(Hold(false))));
        Assert.Contains("perspective", Lines(Build(Hold(true)))); Assert.DoesNotContain("orthogonal", Lines(Build(Hold(true))));
    }

    [Fact]
    public void Coordinates_Are_Micrometres()
    {
        // 2x2 m rect at (0,0) -> centre (1 m, 1 m) -> 1,000,000 um; top view camera 100 ft = 30,480,000 um
        var s = Build(Hold(a: new Rect(0, 0, 2, 2)));
        Assert.Contains("view focus relative to world pos 1000000, 1000000, 0 camera relative to world pos 1000000, 1000000, 30480000 upvector 0, 1, 0 scale ", s);
    }

    [Fact]
    public void Roll_Changes_Only_The_Upvector_And_Zero_Keeps_The_Old_Script()
    {
        Assert.Equal(Build(Hold()), Build(Hold() with { RollDeg = 0 }));
        var s = Build(Hold(a: new Rect(0, 0, 2, 2)) with { RollDeg = 90 });
        Assert.Contains("view focus relative to world pos 1000000, 1000000, 0 camera relative to world pos 1000000, 1000000, 30480000 upvector 1, 0, 0 scale ", s);
        var m = Lines(Build(Move() with { RollDeg = -30 })).Where(x => x.StartsWith("view focus")).ToArray();
        Assert.Equal(2, m.Length);
        Assert.All(m, x => Assert.DoesNotContain("upvector 0, 0, 1 ", x)); // tilted + rolled: the rolled screen-up, not +Z
    }

    [Fact]
    public void Script_Is_Culture_Invariant_And_Rejects_Bad_Input()
    {
        var old = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = new CultureInfo("de-DE"); Assert.Contains("time scale 1.3333", Build(Hold())); }
        finally { CultureInfo.CurrentCulture = old; }
        Assert.Throws<ArgumentException>(() => Build(Move() with { B = null }));
    }
}
