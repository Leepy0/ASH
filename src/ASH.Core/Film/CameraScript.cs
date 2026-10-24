using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ASH.Core.Film;

/// <summary>Writes an AutoView camera.des for one cut (one cut = one aview run = one MP4, guide 6.4 method A).</summary>
public static class CameraScript
{
    // -0.0 + 0.0 = +0.0: no "-0" in the output
    static string N(double v, string fmt = "0.####") => (v + 0.0).ToString(fmt, CultureInfo.InvariantCulture);

    /// <summary>Camera script for <paramref name="cut"/> (syntax: guide 3, recipes: guide 9). <paramref name="movieFile"/> has no extension (AutoView adds .mp4).
    /// Cut times are simulation seconds from <see cref="CutSpec.StartSec"/>; Hold records HoldSec, Move holds A for HoldASec, pans to B over MoveSec, holds B for HoldBSec (all simulation seconds).
    /// The scene height is taken as Hz = 0, so tall objects (vehicles, racks) may be clipped at high tilt.</summary>
    /// <param name="display">Extra display statements before the first time statement, e.g. "turn system ash_cal picture System off".</param>
    public static string Build(CutSpec cut, double f0Metres, int width, int height, int fps, int quality, string movieFile, IEnumerable<string>? display = null)
    {
        bool move = cut.Kind == CutKind.Move;
        if (move && cut.B == null) throw new ArgumentException("Move cut needs B", nameof(cut));
        if (move ? cut.HoldASec <= 0 || cut.MoveSec <= 0 || cut.HoldBSec <= 0 : cut.HoldSec <= 0) throw new ArgumentException("cut durations must be positive", nameof(cut));
        if (cut.Speed <= 0) throw new ArgumentException("speed must be positive", nameof(cut));

        string V(Rect r)
        {
            var v = CameraMath.Frame(r, 1_000_000, f0Metres, width, height, cut.TiltDeg, cut.DirDeg, perspective: cut.Perspective, rollDeg: cut.RollDeg);
            return $"view focus relative to world pos {N(v.FocusX, "0")}, {N(v.FocusY, "0")}, {N(v.FocusZ, "0")} " +
                   $"camera relative to world pos {N(v.CamX, "0")}, {N(v.CamY, "0")}, {N(v.CamZ, "0")} " +
                   $"upvector {N(v.UpX, "0.######")}, {N(v.UpY, "0.######")}, {N(v.UpZ, "0.######")} scale {N(v.Scale)}";
        }

        var sb = new StringBuilder();
        void L(string s) => sb.Append(s).Append('\n');
        L("camera description");
        L("distance feet"); L("time seconds"); L("angle degree");
        L("time scale " + N(CameraMath.TimeScale(cut.Speed, fps)));
        L(cut.Perspective ? "perspective" : "orthogonal");
        L("solid"); L("grid off");
        foreach (var d in display ?? Array.Empty<string>()) L(d);
        L("define movie spec");
        L("format mp4");
        L($"filename \"{movieFile}\"");
        L($"width {width}"); L($"height {height}"); L($"quality {quality}"); L($"framespersec {fps}"); L("keyframefreq 30");
        L($"saveframedir \"{movieFile}_frames\\\""); L("saveframes false");
        L("end movie spec");

        double t0 = cut.StartSec;
        L("time " + N(t0));
        L(V(cut.A));
        L("record movie");
        if (move)
        {
            L("time " + N(t0 + cut.HoldASec));
            L("pan to next view");
            L("time " + N(t0 + cut.HoldASec + cut.MoveSec));
            L(V(cut.B!));
            L("time " + N(t0 + cut.SimSeconds));
        }
        else L("time " + N(t0 + cut.HoldSec));
        L("generate movie");
        return sb.ToString();
    }
}
