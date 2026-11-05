using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace ASH.Core.Lite;

/// <summary>Where one AGVLITE run reads from and writes to. SourceBytes = 0: unknown.</summary>
public sealed record LiteFiles(string Source, long SourceBytes, string Dxf, string Report, string Settings);

/// <summary>Cancelled: nothing was written. Failed: the file that could not be written, Why: the system's words. Otherwise Result is the DXF written, Bytes its size.</summary>
public sealed record LiteOutcome(bool Cancelled = false, string? Failed = null, string? Why = null, LiteResult? Result = null, long Bytes = 0, LiteStats? Stats = null);

/// <summary>Everything of AGVLITE after the drawing is read. It runs off ZWCAD's thread and holds no ZWCAD object.</summary>
public static class LiteJob
{
    /// <summary>Picks the level (R21: a saved one is used as it is, auto starts at 0), then writes the DXF, the report and the settings. ct stops it up to the first file, never after:
    /// a cancelled run leaves the folder as it was. saveSettings = false keeps a settings file that could not be read from being replaced. measuring hears each level before its cleaning.</summary>
    public static LiteOutcome Run(LiteBuild b, LiteReadLog log, LiteFiles f, bool saveSettings, Stopwatch clock, CancellationToken ct, Action<int> measuring)
    {
        int level;
        LiteStat.Hist density;
        b.Cancel = ct;
        try
        {
            if (b.Settings.Level is int saved)
            {
                level = saved;
                measuring(level);
                b.Total(level);
            }
            else level = b.ChooseLevel(LiteLevels.TargetBytes, measuring);
            density = b.Histogram();
            ct.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) { return new LiteOutcome(Cancelled: true); }
        b.Cancel = default; // a half-written DXF is worse than a late Esc

        string at = f.Dxf;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(f.Dxf)!);
            LiteResult r;
            using (var w = new StreamWriter(f.Dxf, false, Encoding.ASCII)) r = b.Write(w, level);
            long bytes = new FileInfo(f.Dxf).Length;
            at = f.Report;
            using (var w = new StreamWriter(f.Report, false, new UTF8Encoding(true))) // BOM: Excel opens the Korean text right
                LiteReport.Write(w, b, r, density, f.Source, f.SourceBytes, f.Dxf, bytes, SeoulTime.Now, clock.Elapsed, log);
            if (saveSettings)
            {
                at = f.Settings;
                b.Settings.Record(level, b.Thresholds(level));
                b.Settings.Save(f.Settings);
            }
            return new LiteOutcome(Result: r, Bytes: bytes, Stats: b.Run(b.Thresholds(level)).Stats);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return new LiteOutcome(Failed: at, Why: ex.Message);
        }
    }
}
