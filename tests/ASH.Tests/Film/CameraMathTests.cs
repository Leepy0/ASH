using ASH.Core.Film;

namespace ASH.Tests.Film;

public class CameraMathTests
{
    const double Ft = 0.3048;
    // guide 10.1: rectangle A = x -10..45, y -35..5 ft (55x40 ft), 1280x720
    static readonly Rect A = new(-10 * Ft, -35 * Ft, 55 * Ft, 40 * Ft);
    const double Um = 1_000_000;

    static void Near(double expected, double actual, double relTol = 0.01) =>
        Assert.True(Math.Abs(actual - expected) <= Math.Abs(expected) * relTol, $"expected {expected} +-{relTol:P1}, got {actual}");

    [Fact]
    public void Frame_TopView_Matches_Guide_Formula()
    {
        // guide 4 (m1 experiment): L = max(40, 55*720/1280=30.9)*1.1 = 44 ft, F0 266 ft -> S ~ 6.05, centre (17.5, -15) ft = (5334000, -4572000) um
        var v = CameraMath.Frame(A, Um, 266 * Ft, 1280, 720, 0, 0, 0.1);
        Near(6.05, v.Scale);
        Assert.Equal(5334000, v.FocusX, 0); Assert.Equal(-4572000, v.FocusY, 0); Assert.Equal(0, v.FocusZ, 0);
        Assert.Equal(5334000, v.CamX, 0); Assert.Equal(-4572000, v.CamY, 0);
        Assert.Equal(30480000, v.CamZ, 0); // guide 4/9: 100 ft above
    }

    [Fact]
    public void Frame_Fits_Wider_Rect_By_Width()
    {
        // 100 x 10 m: width limits (100*720/1280 = 56.25 > 10). L = 56.25 * 1.08
        var v = CameraMath.Frame(new Rect(0, 0, 100, 10), Um, 80, 1280, 720, 0, 0);
        Assert.Equal(80 / (56.25 * 1.08), v.Scale, 6);
        // taller rect fits by height: 10 x 40 -> L = 40 * 1.08
        Assert.Equal(80 / (40 * 1.08), CameraMath.Frame(new Rect(0, 0, 10, 40), Um, 80, 1280, 720, 0, 0).Scale, 6);
    }

    [Fact]
    public void Frame_TopView_Up_Follows_Dir()
    {
        var v0 = CameraMath.Frame(A, Um, 81, 1280, 720, 0, 0);   // guide 3.5 / 10.1: phi 0 -> (0,1,0)
        Assert.Equal((0.0, 1.0, 0.0), (v0.UpX, v0.UpY, v0.UpZ));
        var v90 = CameraMath.Frame(A, Um, 81, 1280, 720, 0, 90); // guide 10.1: theta 0 -> (sin phi, cos phi, 0)
        Assert.Equal(1.0, v90.UpX, 9); Assert.Equal(0.0, v90.UpY, 9); Assert.Equal(0.0, v90.UpZ, 9);
    }

    [Fact]
    public void Frame_Tilted_Puts_Camera_South_And_Up()
    {
        var v = CameraMath.Frame(A, Um, 81, 1280, 720, 45, 0);
        Assert.True(v.CamY < v.FocusY); Assert.True(v.CamZ > 0); Assert.Equal(v.CamX, v.FocusX, 3);
        Assert.Equal((0.0, 0.0, 1.0), (v.UpX, v.UpY, v.UpZ));          // guide 10.1: theta > 0 -> (0,0,1)
        Assert.Equal(30480000 * Math.Sin(Math.PI / 4), v.FocusY - v.CamY, 0);
        Assert.Equal(30480000 * Math.Cos(Math.PI / 4), v.CamZ, 0);
        // k(theta) = 1.00 (guide 10.3): 45 deg magnifies vs the plan view by the guide's S(45)/S(0) ~ 1.131 (Hz = 10 ft, m = 0.1, F0 267 ft)
        double s0 = CameraMath.Frame(A, Um, 267 * Ft, 1280, 720, 0, 0, 0.1, 10 * Ft).Scale;
        double s45 = CameraMath.Frame(A, Um, 267 * Ft, 1280, 720, 45, 0, 0.1, 10 * Ft).Scale;
        Near(1.131, s45 / s0, 0.005);
    }

    [Fact]
    public void Frame_Tilted_Matches_Guide_Worked_Examples()
    {
        // guide 10.2: orthogonal, Hz = 10 ft, theta 45, phi 0, D = 100 ft, m = 0.1, F0 = 267 ft -> S = 6.8654, focus z 1524000
        var v = CameraMath.Frame(A, Um, 267 * Ft, 1280, 720, 45, 0, 0.1, 10 * Ft);
        Near(6.8654, v.Scale, 0.001);
        Assert.Equal(1524000, v.FocusZ, 0);
        Assert.Equal(-26124615, v.CamY, 0); Assert.Equal(23076615, v.CamZ, 0); Assert.Equal(5334000, v.CamX, 0);
        // guide 10.2: perspective same view -> S = 6.1372
        Near(6.1372, CameraMath.Frame(A, Um, 267 * Ft, 1280, 720, 45, 0, 0.1, 10 * Ft, perspective: true).Scale, 0.001);
        // guide 10.2: phi 90, theta 60 -> S = 6.7125, camera (-21062454, -4572000, 16764000)
        var p = CameraMath.Frame(A, Um, 267 * Ft, 1280, 720, 60, 90, 0.1, 10 * Ft);
        Near(6.7125, p.Scale, 0.001);
        Assert.Equal(-21062454, p.CamX, 0); Assert.Equal(-4572000, p.CamY, 0); Assert.Equal(16764000, p.CamZ, 0);
    }

    [Fact]
    public void Frame_Small_Tilt_Snaps_To_Top_View_And_Rejects_Bad_Input()
    {
        var v = CameraMath.Frame(A, Um, 81, 1280, 720, 3, 0);
        Assert.Equal((0.0, 1.0, 0.0), (v.UpX, v.UpY, v.UpZ)); Assert.Equal(v.FocusX, v.CamX, 6); Assert.Equal(v.FocusY, v.CamY, 6);
        Assert.Throws<ArgumentOutOfRangeException>(() => CameraMath.Frame(A, Um, 81, 1280, 720, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CameraMath.Frame(A, Um, 81, 1280, 720, 71, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CameraMath.Frame(A, Um, 81, 720, 1280, 0, 0));
        CameraMath.Frame(A, Um, 81, 1280, 720, 70, 0); // boundary allowed
    }

    [Fact]
    public void Frame_Roll_Turns_Up_About_The_Sight_Line()
    {
        // plan view, roll 90: screen-up leans to the old screen-right (east), so a 100 x 10 m rect now fits by its 100 m side vertically
        var v = CameraMath.Frame(new Rect(0, 0, 100, 10), Um, 80, 1280, 720, 0, 0, rollDeg: 90);
        Assert.Equal(1.0, v.UpX, 9); Assert.Equal(0.0, v.UpY, 9); Assert.Equal(0.0, v.UpZ, 9);
        Assert.Equal(80 / (100 * 1.08), v.Scale, 6);
        // tilted and rolled: the upvector stays a unit vector off the sight line, camera position does not move
        var t = CameraMath.Frame(A, Um, 81, 1280, 720, 45, 30, rollDeg: 20);
        var t0 = CameraMath.Frame(A, Um, 81, 1280, 720, 45, 30);
        Assert.Equal((t0.CamX, t0.CamY, t0.CamZ), (t.CamX, t.CamY, t.CamZ));
        double dx = t.FocusX - t.CamX, dy = t.FocusY - t.CamY, dz = t.FocusZ - t.CamZ;
        Assert.Equal(0, (t.UpX * dx + t.UpY * dy + t.UpZ * dz) / Math.Sqrt(dx * dx + dy * dy + dz * dz), 9);
        Assert.Equal(1, Math.Sqrt(t.UpX * t.UpX + t.UpY * t.UpY + t.UpZ * t.UpZ), 9);
        Assert.Equal(Math.Sin(45 * Math.PI / 180) * Math.Cos(20 * Math.PI / 180), t.UpZ, 9); // screen-up's vertical part shrinks by cos(roll)
    }

    [Fact]
    public void TimeScale_10x_15fps()
    {
        Assert.Equal(1.3333, CameraMath.TimeScale(10, 15), 4);   // guide 5.1 (m1: 1.3333 @ 15 fps = 9.97x)
        Assert.Equal(0.6667, CameraMath.TimeScale(10, 30), 4);   // guide 5.1: 10x at 30 fps -> 0.6667
    }
}
