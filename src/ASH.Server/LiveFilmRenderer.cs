using ASH.Core.Film;
using Microsoft.Extensions.Configuration;

namespace ASH.Server;

/// <summary>
/// The film renderer with the AutoMod folder, the AutoView program and ffmpeg read from the settings at each call. The settings page says
/// Jobs:AmHome, Film:AviewExe and Film:FfmpegPath take effect at once, so the renderer must not keep the values it saw at startup (it used
/// to, and then AutoView ran from an old AutoMod folder while the build used the new one).
/// </summary>
/// AutoView runs go to the logged-on session's helper when this process is the service in session 0 (<see cref="FilmHelper"/>).
public sealed class LiveFilmRenderer(IConfiguration cfg, FilmHelper.Client? helper = null) : IFilmRenderer
{
    /// <summary>Film:AviewExe when set; otherwise the AutoView program under Jobs:AmHome (aview64.exe, or aview.exe for 12.4); null when neither exists.</summary>
    internal static string? ResolveAviewExe(IConfiguration cfg)
    {
        if (cfg["Film:AviewExe"] is { Length: > 0 } given) return given;
        return AutoViewExe.Find(AmHome(cfg));
    }

    internal static string AmHome(IConfiguration cfg) => new AutoModJobRunner(cfg).AmHome;

    static string Ffmpeg(IConfiguration cfg) => cfg["Film:FfmpegPath"] is { Length: > 0 } ff ? ff : "ffmpeg";

    /// <summary>Film:DialogTitles as a list (comma separated, blanks dropped); the default "Attention" when nothing is left.</summary>
    internal static string[] DialogTitles(IConfiguration cfg) =>
        (cfg["Film:DialogTitles"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is { Length: > 0 } t ? t : AutoViewRenderer.DefaultDialogTitles;

    public RenderResult Render(string workDir, string modelName, string cameraScript, TimeSpan timeout, CancellationToken ct) =>
        helper != null && InSessionZero() ? helper.Render(workDir, modelName, cameraScript, timeout, ct) : RenderHere(workDir, modelName, cameraScript, timeout, ct);

    static bool InSessionZero() { try { return System.Diagnostics.Process.GetCurrentProcess().SessionId == 0; } catch (InvalidOperationException) { return false; } }

    /// <summary>AutoView in this process's own session (the helper, or a server run from a logged-on console).</summary>
    public RenderResult RenderHere(string workDir, string modelName, string cameraScript, TimeSpan timeout, CancellationToken ct)
    {
        var exe = ResolveAviewExe(cfg);
        if (exe == null)
            return new RenderResult(false, null, "AutoView 실행 파일을 찾지 못했습니다: " + AutoViewExe.Candidates(AmHome(cfg)) + " (또는 설정의 Film:AviewExe)", new string[0]);
        return new AutoViewRenderer(exe, AmHome(cfg), Ffmpeg(cfg), DialogTitles(cfg)).Render(workDir, modelName, cameraScript, timeout, ct);
    }

    // ffmpeg only: the AutoView program is not needed here
    public string Finish(IReadOnlyList<string> mp4s, IReadOnlyList<string> captions, string outPath, bool join, CancellationToken ct = default, double seconds = 0, int fps = 0) =>
        new AutoViewRenderer(ResolveAviewExe(cfg) ?? "", AmHome(cfg), Ffmpeg(cfg)).Finish(mp4s, captions, outPath, join, ct, seconds, fps);

    public int Frames(string video, CancellationToken ct = default) => new AutoViewRenderer(ResolveAviewExe(cfg) ?? "", AmHome(cfg), Ffmpeg(cfg)).Frames(video, ct);

    public void Thumbnail(string mp4, string outJpg, double atSec, CancellationToken ct = default, int width = 320) =>
        new AutoViewRenderer(ResolveAviewExe(cfg) ?? "", AmHome(cfg), Ffmpeg(cfg)).Thumbnail(mp4, outJpg, atSec, ct, width);

    public (double W, double H)? MarkSize(string mp4With, string mp4Without, int width, int height, CancellationToken ct = default) =>
        new AutoViewRenderer(ResolveAviewExe(cfg) ?? "", AmHome(cfg), Ffmpeg(cfg)).MarkSize(mp4With, mp4Without, width, height, ct);
}
