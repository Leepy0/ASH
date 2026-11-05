using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace ASH.Core.Lite;

/// <summary>Where one AGVLITE run reads from and writes to. SourceBytes = 0: unknown.</summary>
public sealed record LiteFiles(string Source, long SourceBytes, string Dxf, string Report, string Settings);

/// <summary>Cancelled: nothing was written. Failed: the file that could not be written, Why: the system's words. Otherwise Result is the DXF written, Bytes its size.</summary>
public sealed record LiteOutcome(bool Cancelled = false, string? Failed = null, string? Why = null, LiteResult? Result = null, long Bytes = 0, LiteStats? Stats = null);

/// <summary>One level as AGVLITEX shows it: the estimated file size and the blobs replaced by an outline.</summary>
public sealed record LiteLevelEstimate(long Bytes, int Blobs);

/// <summary>Everything of AGVLITE after the drawing is read. It runs off ZWCAD's thread and holds no ZWCAD object.</summary>
public static class LiteJob
{
    const string Tmp = ".tmp";

    /// <summary>Picks the level (R21: a saved one is used as it is, auto starts at 0), then writes the DXF, the report and the settings. ct stops it up to the first file, never after:
    /// a cancelled run leaves the folder as it was. The DXF and the report go to .tmp files and replace the old pair only once both are whole and neither old file is held open, so a failed
    /// run leaves the previous result as it was. saveSettings = false keeps a settings file that could not be read from being replaced. measuring hears each level before its cleaning.</summary>
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

        string at = f.Dxf, dxfTmp = f.Dxf + Tmp, reportTmp = f.Report + Tmp;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(f.Dxf)!);
            LiteResult r;
            using (var w = new StreamWriter(dxfTmp, false, Encoding.ASCII)) r = b.Write(w, level);
            long bytes = new FileInfo(dxfTmp).Length;
            at = f.Report;
            using (var w = new StreamWriter(reportTmp, false, new UTF8Encoding(true))) // BOM: Excel opens the Korean text right
                LiteReport.Write(w, b, r, density, f.Source, f.SourceBytes, f.Dxf, bytes, SeoulTime.Now, clock.Elapsed, log);

            // both or neither: a new DXF beside an old report would describe the wrong file
            at = f.Dxf;
            NotHeld(f.Dxf);
            at = f.Report;
            NotHeld(f.Report);
            at = f.Dxf;
            Put(dxfTmp, f.Dxf);
            at = f.Report;
            Put(reportTmp, f.Report);

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
        finally
        {
            Drop(dxfTmp);
            Drop(reportTmp);
        }
    }

    /// <summary>The size of the file and the blobs outlined at every level, lowest first, for the current exclusions (AGVLITEX shows them). Null when ct stopped it.</summary>
    public static List<LiteLevelEstimate>? Measure(LiteBuild b, CancellationToken ct, Action<int> measuring)
    {
        b.Cancel = ct;
        try
        {
            var all = new List<LiteLevelEstimate>();
            for (int level = 0; level <= LiteLevels.Max; level++)
            {
                measuring(level);
                long bytes = b.Total(level);
                all.Add(new LiteLevelEstimate(bytes, b.Run(b.Thresholds(level)).Stats.Blobs)); // Total left that run cached
            }
            return all;
        }
        catch (OperationCanceledException) { return null; }
        finally { b.Cancel = default; }
    }

    /// <summary>Throws what replacing the file would throw, before anything has moved.</summary>
    static void NotHeld(string path)
    {
        if (File.Exists(path)) using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }

    static void Put(string tmp, string path)
    {
        if (File.Exists(path)) File.Replace(tmp, path, null, true);
        else File.Move(tmp, path);
    }

    static void Drop(string tmp)
    {
        try { if (File.Exists(tmp)) File.Delete(tmp); }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { } // a stray .tmp must not hide the run's result; the next run overwrites it
    }
}
