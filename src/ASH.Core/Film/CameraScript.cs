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
    /// <param name="legacy">AutoView before 14 (<see cref="AutoViewExe.IsLegacy"/>): AVI and no upvector, see <see cref="MovieSpec"/> and <see cref="View"/>.</param>
    public static string Build(CutSpec cut, double f0Metres, int width, int height, int fps, int quality, string movieFile, IEnumerable<string>? display = null, bool legacy = false)
    {
        bool move = cut.Kind == CutKind.Move;
        if (move && cut.B == null) throw new ArgumentException("Move cut needs B", nameof(cut));
        if (move ? cut.HoldASec <= 0 || cut.MoveSec <= 0 || cut.HoldBSec <= 0 : cut.HoldSec <= 0) throw new ArgumentException("cut durations must be positive", nameof(cut));
        if (cut.Speed <= 0) throw new ArgumentException("speed must be positive", nameof(cut));

        string V(Rect r)
        {
            var v = CameraMath.Frame(r, 1_000_000, f0Metres, width, height, cut.TiltDeg, cut.DirDeg, perspective: cut.Perspective, rollDeg: cut.RollDeg);
            return View(v.FocusX, v.FocusY, v.FocusZ, v.CamX, v.CamY, v.CamZ, v.UpX, v.UpY, v.UpZ, v.Scale, legacy);
        }

        var sb = new StringBuilder();
        void L(string s) => sb.Append(s).Append('\n');
        L("camera description");
        L("distance feet"); L("time seconds"); L("angle degree");
        L("time scale " + N(CameraMath.TimeScale(cut.Speed, fps)));
        L(cut.Perspective ? "perspective" : "orthogonal");
        L("solid"); L("grid off");
        foreach (var d in display ?? Array.Empty<string>()) L(d);
        sb.Append(MovieSpec(movieFile, width, height, quality, fps, legacy));

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

    /// <summary>"define movie spec … end movie spec" lines. 14.x: MP4 (AutoView adds .mp4; frames through saveframedir, guide 5.2).
    /// Before 14: AVI, the only video format its grammar has besides MPEG/custom; the file name carries .avi as AutoView's own template writes it.</summary>
    public static string MovieSpec(string movieFile, int width, int height, int quality, int fps, bool legacy)
    {
        var sb = new StringBuilder();
        void L(string s) => sb.Append(s).Append('\n');
        L("define movie spec");
        if (legacy) { L("format avi"); L($"filename \"{movieFile}.avi\""); }
        else { L("format mp4"); L($"filename \"{movieFile}\""); }
        L($"width {width}"); L($"height {height}"); L($"quality {quality}"); L($"framespersec {fps}"); L("keyframefreq 30");
        if (!legacy) { L($"saveframedir \"{movieFile}_frames\\\""); L("saveframes false"); }
        L("end movie spec");
        return sb.ToString();
    }

    /// <summary>One "view focus … camera …" line from micrometres (written as such for 14.x). Before 14 there is no upvector: a camera straight above the focus (up vector in the plan)
    /// is moved off the sight line by 1/1000 of its height against the wanted screen-up direction, so the plan view keeps its heading (0.06° of tilt);
    /// a tilted view already says it by where the camera stands. Roll cannot be given there.
    /// Before 14 the positions are also read in the script's "distance" unit (feet: its own saved views write "distance unit …" before them), where 14.x
    /// took the micrometres as they are; written in micrometres the 12.6 camera stood about 30 million ft away and every frame was white.</summary>
    public static string View(double fx, double fy, double fz, double cx, double cy, double cz, double ux, double uy, double uz, double scale, bool legacy)
    {
        if (!legacy)
            return $"view focus relative to world pos {N(fx, "0")}, {N(fy, "0")}, {N(fz, "0")} " +
                   $"camera relative to world pos {N(cx, "0")}, {N(cy, "0")}, {N(cz, "0")} upvector {N(ux, "0.######")}, {N(uy, "0.######")}, {N(uz, "0.######")} scale {N(scale)}";
        if (Math.Abs(uz) < 0.5) { var h = (cz - fz) * 1e-3; cx -= ux * h; cy -= uy * h; }
        const double UmPerFoot = 304_800;
        string Ft(double um) => N(um / UmPerFoot, "0.#####");
        return $"view focus relative to world pos {Ft(fx)}, {Ft(fy)}, {Ft(fz)} camera relative to world pos {Ft(cx)}, {Ft(cy)}, {Ft(cz)} scale {N(scale)}";
    }
}
