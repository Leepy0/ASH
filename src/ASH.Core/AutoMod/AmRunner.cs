using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ASH.Core.AutoMod
{
    /// <summary>Paths are where the files would be; check File.Exists. ExitCode is null on timeout.</summary>
    public sealed record AmRunResult(int? ExitCode, bool TimedOut, TimeSpan Duration, string ResultTsvPath, string ReportPath,
        string MessagePath, IReadOnlyList<string> ClickedDialogTitles);

    // shortcut: temporary single-run helper; Task 8 promotes it into ModelRunner (params swap, run folders).
    public static class AmRunner
    {
        public static AmRunResult RunHeadless(string exePath, string workDir, TimeSpan timeout, string? dialogButton)
        {
            var model = Path.GetFileNameWithoutExtension(exePath);
            var clicked = new List<string>();
            var resultTsv = Path.Combine(workDir, "result.tsv");
            if (File.Exists(resultTsv)) File.Delete(resultTsv); // a stale result must not pass for this run's
            var psi = new ProcessStartInfo(exePath, "-w") { WorkingDirectory = workDir, UseShellExecute = false };
            var sw = Stopwatch.StartNew();
            int? exit = null;
            bool timedOut = false;
            using (var proc = Process.Start(psi))
            {
                IDisposable? closer = dialogButton == null ? null
                    : DialogCloser.Start(proc.Id, new[] { "Authorization" }, t => { lock (clicked) clicked.Add(t); }, dialogButton);
                try
                {
                    if (proc.WaitForExit((int)timeout.TotalMilliseconds)) exit = proc.ExitCode;
                    else
                    {
                        timedOut = true;
                        try { proc.Kill(); }
                        catch (InvalidOperationException) { } // already exited
                        catch (System.ComponentModel.Win32Exception) { } // exiting or access denied
                        proc.WaitForExit(5000);
                    }
                }
                finally { closer?.Dispose(); }
            }
            sw.Stop();
            lock (clicked)
                return new AmRunResult(exit, timedOut, sw.Elapsed, resultTsv,
                    Path.Combine(workDir, model + ".report"), Path.Combine(workDir, model + ".message"), clicked.ToArray());
        }
    }
}
