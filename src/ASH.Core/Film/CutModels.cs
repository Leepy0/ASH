namespace ASH.Core.Film;

public enum CutKind { Hold, Move, Whole }

/// <summary>Axis-aligned rectangle in model metres; (X, Y) is the minimum corner.</summary>
public sealed record Rect(double X, double Y, double W, double H);

/// <summary>One film cut. Hold/Whole use <see cref="HoldSec"/> (simulation seconds) and A only.
/// Move uses A, B and HoldASec/MoveSec/HoldBSec, which are also SIMULATION seconds (guide 9 (2): N, M, L become t0+N, t0+N+M, t0+N+M+L).
/// TiltDeg: 0 = straight down (plan view), up to 70. DirDeg: compass direction shown screen-up (0 = looking north, +Y up; 90 = +X up).
/// RollDeg: camera turned about its sight line, -180..180 (0 = none; cuts saved before it existed load as 0).</summary>
public sealed record CutSpec(string Id, string Name, CutKind Kind, double StartSec, double HoldSec, Rect A, Rect? B, double HoldASec, double MoveSec, double HoldBSec,
    double Speed, double TiltDeg, double DirDeg, bool Perspective, bool Caption, int VehicleDelta = 0, double RollDeg = 0)
{
    public double SimSeconds => Kind == CutKind.Move ? HoldASec + MoveSec + HoldBSec : HoldSec;
    public double VideoSeconds => SimSeconds / Speed;
}

/// <summary>AutoView view, all positions in micrometres (guide 3.2/4).</summary>
public sealed record View(double FocusX, double FocusY, double FocusZ, double CamX, double CamY, double CamZ, double UpX, double UpY, double UpZ, double Scale);
