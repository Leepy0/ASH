using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using ASH.Core.AutoMod;

namespace ASH.Core.Film;

/// <summary>Mp4 = the movie AutoView made: the file the camera script names (filename + ".mp4"), or filename + ".avi" from an AutoView that writes AVI
/// (12.4; the ffmpeg steps take either as input); Dialogs = AutoView windows that were clicked away;
/// TimedOut = the run was killed at its time limit (AutoView hung: a dialog nobody clicked, or no desktop to render on).</summary>
public sealed record RenderResult(bool Ok, string? Mp4, string? Error, IReadOnlyList<string> Dialogs, bool TimedOut = false);

/// <summary>AutoView and ffmpeg steps of filming; the server's film tests plug in a fake.</summary>
public interface IFilmRenderer
{
    /// <summary>One AutoView run (one cut = one run = one MP4, guide 6.4) on &lt;workDir&gt;\&lt;modelName&gt;.avw with this camera.des.
    /// A run past <paramref name="timeout"/> (or cancelled) is killed and fails alone. Never throws for a failed run.</summary>
    RenderResult Render(string workDir, string modelName, string cameraScript, TimeSpan timeout, CancellationToken ct);

    /// <summary>join = false: mp4s[0] re-encoded for phones and PowerPoint (H.264, yuv420p, faststart) with captions[0] drawn at the bottom ("" = none).
    /// join = true: mp4s (same size, fps and codec) put one after another without re-encoding; captions are ignored.
    /// Returns <paramref name="outPath"/>; InvalidOperationException with ffmpeg's message on failure.</summary>
    string Finish(IReadOnlyList<string> mp4s, IReadOnlyList<string> captions, string outPath, bool join, CancellationToken ct = default);

    /// <summary>Pixel width and height of what differs between the first frames of two renders of the same view (the F0 calibration mark, guide 10.4); null when nothing differs.</summary>
    (double W, double H)? MarkSize(string mp4With, string mp4Without, int width, int height, CancellationToken ct = default);
}

/// <summary>aview64 -n &lt;model&gt; in the folder holding the .avw (guide 6.2/6.3): the "Attention!" windows (grid warnings, "a movie will be generated") and the AVI codec dialog are clicked OK,
/// NoDefaultCurrentDirectoryInExePath is removed from the child environment (else AutoView's convert.bat silently fails), aviewerr.dat must stay empty.
/// ffmpeg must be on PATH for AutoView itself; a full <paramref name="ffmpeg"/> path is also put on the child PATH.
/// <paramref name="dialogTitles"/>: windows whose title contains one of these are clicked OK (Film:DialogTitles; null or empty = <see cref="DefaultDialogTitles"/>).
/// A run killed at its limit or ending without an MP4 says what AutoView left behind (<see cref="AutoViewDiagnostics"/>).</summary>
public sealed class AutoViewRenderer(string aviewExe, string? amHome, string ffmpeg, IReadOnlyList<string>? dialogTitles = null) : IFilmRenderer
{
    /// <summary>"Attention!" (grid warnings, "a movie will be generated"), then 12.4's AVI codec dialog: "비디오 압축" on Korean Windows, "Video Compression" on English.</summary>
    public static readonly string[] DefaultDialogTitles = { "Attention", "비디오 압축", "Video Compression" };

    public RenderResult Render(string workDir, string modelName, string cameraScript, TimeSpan timeout, CancellationToken ct)
    {
        var dialogs = new List<string>();
        RenderResult Fail(string e, bool timedOut = false) { lock (dialogs) return new RenderResult(false, null, e, dialogs.ToArray(), timedOut); }
        var avw = Path.Combine(workDir, modelName + ".avw");
        if (!Directory.Exists(avw)) return Fail("AutoView 기록(.avw)이 없습니다: " + avw);
        if (MovieFile(cameraScript) is not { } movie) return Fail("카메라 스크립트에 filename이 없습니다.");
        var mp4 = movie + ".mp4";
        try
        {
            File.WriteAllText(Path.Combine(avw, "camera.des"), cameraScript, new UTF8Encoding(false)); // the script name is fixed (guide 6.2)
            foreach (var n in new[] { "aviewerr.dat", "aviewout.dat" }) File.Delete(Path.Combine(workDir, n));
            File.Delete(mp4); File.Delete(movie + ".avi");
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return Fail("작업 폴더에 쓸 수 없습니다: " + e.Message); }

        var bin = string.Join(";", new[] { amHome == null ? null : Path.Combine(amHome, "bin"), Path.IsPathRooted(ffmpeg) ? Path.GetDirectoryName(ffmpeg) : null }.Where(x => x != null));
        var said = new StringBuilder(); // AutoView's own stdout/stderr (a usage line if it does not take -n), last part only
        string Diag(int? pid)
        {
            string output, clicked;
            lock (said) output = said.ToString();
            lock (dialogs) clicked = string.Join(", ", dialogs);
            return AutoViewDiagnostics.Text(aviewExe, pid, workDir, avw, movie, bin) + (clicked.Length > 0 ? " · 누른 창 " + clicked : " · 누른 창 없음") +
                (output.Trim().Length > 0 ? $" · 출력 \"{Tail(output, 300).Replace("\r", "").Replace("\n", " | ")}\"" : "");
        }
        var started = DateTime.UtcNow;
        Process p;
        try
        {
            var psi = Psi(aviewExe, new[] { "-n", modelName }, workDir, bin);
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            p = Process.Start(psi);
            DataReceivedEventHandler keep = (_, e) => { if (e.Data == null) return; lock (said) { said.AppendLine(e.Data); if (said.Length > 4000) said.Remove(0, said.Length - 2000); } };
            p.OutputDataReceived += keep; p.ErrorDataReceived += keep;
            p.BeginOutputReadLine(); p.BeginErrorReadLine();
        }
        catch (System.ComponentModel.Win32Exception e) { return Fail($"AutoView({aviewExe})를 실행하지 못했습니다: {e.Message}"); }
        var titles = dialogTitles is { Count: > 0 } ? dialogTitles.ToArray() : DefaultDialogTitles;
        using (p)
        using (DialogCloser.Start(p.Id, titles, t => { lock (dialogs) dialogs.Add(t); }))
        {
            var sw = Stopwatch.StartNew();
            while (!p.WaitForExit(500))
                if (sw.Elapsed > timeout || ct.IsCancellationRequested)
                {
                    if (ct.IsCancellationRequested) { Kill(p); return Fail("취소되었습니다."); }
                    var diag = Diag(p.Id); // before the kill: the windows it is stuck on
                    Kill(p);
                    return Fail($"시간 제한({timeout.TotalMinutes:0.#}분)을 넘어 AutoView를 끊었습니다(대화상자에 걸렸거나 너무 긴 컷). " +
                        $"창 제목이 보이면 설정 Film:DialogTitles에 더합니다. {diag}", true);
                }
        }
        var err = Read(Path.Combine(workDir, "aviewerr.dat"));
        if (err.Trim().Length > 0) return Fail("카메라 스크립트 오류(aviewerr.dat): " + Tail(err, 400));
        if (Movie(mp4, movie + ".avi", workDir, avw, started) is not { } made)
            // only 14.5 was tried (guide 5.2/6.3): whether older AutoView makes an MP4 this way is not known
            return Fail("AutoView가 동영상(MP4/AVI)을 만들지 않았습니다. MP4 출력은 AutoView 14.x에서 확인한 방식(프레임 JPG → convert.bat → ffmpeg/ezavi 변환)이며 " +
                "12.4 같은 이전 AutoView에서는 되지 않을 수 있습니다(확인되지 않음). AutoView 버전, 서버 PATH의 ffmpeg, 서비스 세션 여부를 확인합니다. " + Diag(null));
        lock (dialogs) return new RenderResult(true, made, null, dialogs.ToArray());
    }

    public string Finish(IReadOnlyList<string> mp4s, IReadOnlyList<string> captions, string outPath, bool join, CancellationToken ct = default)
    {
        if (mp4s.Count == 0 || (!join && mp4s.Count != 1)) throw new ArgumentException("join needs one or more files, a single finish exactly one", nameof(mp4s));
        var tmp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outPath))!, ".ff-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(tmp);
        try
        {
            var part = Path.Combine(tmp, "out.mp4");
            var args = new List<string> { "-v", "error", "-y" };
            if (join)
            {
                // concat demuxer list: forward slashes, a quote inside a name closes, escapes and reopens the quoted string
                File.WriteAllLines(Path.Combine(tmp, "list.txt"), mp4s.Select(m => "file '" + Path.GetFullPath(m).Replace('\\', '/').Replace("'", "'\\''") + "'"));
                args.AddRange(new[] { "-f", "concat", "-safe", "0", "-i", "list.txt", "-c", "copy" });
            }
            else
            {
                args.AddRange(new[] { "-i", Path.GetFullPath(mp4s[0]) });
                var cap = captions.Count > 0 ? captions[0] : "";
                var vf = "";
                if (cap.Trim().Length > 0 && Font() is { } font)
                {
                    // the text goes through a file in the cwd (no escaping of the text itself); expansion=none keeps '%' literal
                    File.WriteAllText(Path.Combine(tmp, "cap.txt"), cap.Replace("\r", ""), new UTF8Encoding(false));
                    vf = "drawtext=fontfile='" + font.Replace('\\', '/').Replace(":", "\\:") + "':textfile=cap.txt:expansion=none:fontsize=h/26:fontcolor=white" +
                        ":box=1:boxcolor=black@0.55:boxborderw=10:line_spacing=8:x=24:y=h-th-28,";
                }
                // AutoView's MP4 is full-range yuvj420p: -pix_fmt alone keeps the full range (ffprobe still says yuvj420p), so convert the range too
                args.AddRange(new[] { "-vf", vf + "scale=out_range=tv,format=yuv420p", "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p", "-an" });
            }
            args.AddRange(new[] { "-movflags", "+faststart", part });
            var r = Ffmpeg(args, tmp, TimeSpan.FromMinutes(30), ct);
            if (r.Exit != 0 || !File.Exists(part) || new FileInfo(part).Length == 0) throw new InvalidOperationException("ffmpeg 실패: " + Tail(r.Err, 600));
            if (File.Exists(outPath)) File.Delete(outPath);
            File.Move(part, outPath);
            return outPath;
        }
        finally { try { Directory.Delete(tmp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    public (double W, double H)? MarkSize(string mp4With, string mp4Without, int width, int height, CancellationToken ct = default)
    {
        byte[]? Frame(string f)
        {
            var r = Ffmpeg(new[] { "-v", "error", "-i", Path.GetFullPath(f), "-frames:v", "1", "-s", $"{width}x{height}", "-f", "rawvideo", "-pix_fmt", "rgb24", "-" },
                Path.GetDirectoryName(Path.GetFullPath(f))!, TimeSpan.FromMinutes(2), ct);
            return r.Exit == 0 && r.Out.Length == width * height * 3 ? r.Out : null;
        }
        if (Frame(mp4With) is not { } a || Frame(mp4Without) is not { } b) return null;
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 3;
                if (Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]) <= 90) continue; // guide 10.4: RGB difference sum > 90
                x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
            }
        return x1 < 0 ? null : (x1 - x0 + 1, y1 - y0 + 1);
    }

    /// <summary>What the run made: &lt;movie&gt;.mp4 (14.x), else &lt;movie&gt;.avi, else the newest AVI written since <paramref name="since"/> in the work or .avw folder
    /// (12.4 asked for "model.avi" in its movie dialog, so it may not use our filename), moved to &lt;movie&gt;.avi so the next run cannot overwrite it; null when none.
    /// shortcut: the AVI route is unverified (no 12.4 here; 14.5's AVI was a blank picture); confirm with one film on a 12.4 server.</summary>
    internal static string? Movie(string mp4, string avi, string workDir, string avw, DateTime since)
    {
        static bool Has(string f) => File.Exists(f) && new FileInfo(f).Length > 0;
        if (Has(mp4)) return mp4;
        if (Has(avi)) return avi;
        var other = new[] { workDir, avw }.Where(Directory.Exists).SelectMany(d => Directory.GetFiles(d, "*.avi"))
            .Select(f => new FileInfo(f)).Where(f => f.Length > 0 && f.LastWriteTimeUtc >= since.AddSeconds(-2)).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
        if (other == null) return null;
        try { File.Move(other.FullName, avi); return avi; }
        catch (IOException) { return other.FullName; }
    }

    /// <summary>The movie file of a camera script (its "filename" line, no extension); null when there is none.</summary>
    public static string? MovieFile(string cameraScript) =>
        Regex.Match(cameraScript, "^filename \"([^\"]+)\"\\s*$", RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>A font with Hangul glyphs (Malgun Gothic on Windows); null = no caption.</summary>
    static string? Font()
    {
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (dir.Length == 0) dir = @"C:\Windows\Fonts";
        return new[] { "malgunbd.ttf", "malgun.ttf", "gulim.ttc", "arial.ttf" }.Select(n => Path.Combine(dir, n)).FirstOrDefault(File.Exists);
    }

    /// <summary>OperationCanceledException (ffmpeg killed) when <paramref name="ct"/> fires.</summary>
    (int Exit, string Err, byte[] Out) Ffmpeg(IEnumerable<string> args, string cwd, TimeSpan timeout, CancellationToken ct)
    {
        var psi = Psi(ffmpeg, args, cwd, "");
        psi.RedirectStandardError = true; psi.RedirectStandardOutput = true; psi.CreateNoWindow = true;
        Process p;
        try { p = Process.Start(psi); }
        catch (System.ComponentModel.Win32Exception e) { throw new InvalidOperationException($"ffmpeg({ffmpeg})를 실행하지 못했습니다(Film:FfmpegPath 또는 PATH): {e.Message}", e); }
        using (p)
        {
            var err = p.StandardError.ReadToEndAsync();
            var ms = new MemoryStream();
            var output = p.StandardOutput.BaseStream.CopyToAsync(ms);
            var sw = Stopwatch.StartNew();
            while (!p.WaitForExit(250))
            {
                if (ct.IsCancellationRequested) { Kill(p); ct.ThrowIfCancellationRequested(); }
                if (sw.Elapsed > timeout) { Kill(p); throw new InvalidOperationException("ffmpeg가 시간 제한을 넘었습니다."); }
            }
            output.Wait(); p.WaitForExit();
            return (p.ExitCode, err.Result, ms.ToArray());
        }
    }

    static ProcessStartInfo Psi(string exe, IEnumerable<string> args, string cwd, string pathPrefix)
    {
        var psi = new ProcessStartInfo(exe, string.Join(" ", args.Select(Quote))) { WorkingDirectory = cwd, UseShellExecute = false };
        psi.EnvironmentVariables.Remove("NoDefaultCurrentDirectoryInExePath"); // guide 6.3: with it set, AutoView's convert.bat is not found and no MP4 is made
        if (pathPrefix.Length > 0) psi.EnvironmentVariables["PATH"] = pathPrefix + ";" + Environment.GetEnvironmentVariable("PATH");
        return psi;
    }

    /// <summary>One argument for CommandLineToArgvW (netstandard2.0 has no ArgumentList).</summary>
    internal static string Quote(string a)
    {
        if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a;
        var sb = new StringBuilder("\"");
        int bs = 0;
        foreach (var c in a)
        {
            if (c == '\\') { bs++; continue; }
            sb.Append('\\', c == '"' ? bs * 2 + 1 : bs).Append(c);
            bs = 0;
        }
        return sb.Append('\\', bs * 2).Append('"').ToString();
    }

    static void Kill(Process p)
    {
        try { Process.Start(new ProcessStartInfo("taskkill", "/PID " + p.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true })?.WaitForExit(5000); }
        catch (System.ComponentModel.Win32Exception) { }
        try { p.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
        p.WaitForExit(5000);
    }

    static string Read(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : ""; }
        catch (IOException) { return ""; }
    }

    static string Tail(string s, int max) { s = s.Trim(); return s.Length <= max ? s : "…" + s.Substring(s.Length - max); }
}
