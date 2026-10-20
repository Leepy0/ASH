using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ASH.Core.AutoMod
{
    public sealed record AmBuildResult(bool Success, string MakeErrText, string Am2ErrText, IReadOnlyList<string> ClosedDialogTitles);

    public static class AmBuilder
    {
        public static AmBuildResult Build(string amHome, string modelDir, string modelName, TimeSpan timeout)
        {
            var closed = new List<string>();
            var psi = new ProcessStartInfo(Path.Combine(amHome, "bin", "am264.exe"), "-bq " + modelName)
            { WorkingDirectory = modelDir, UseShellExecute = false };
            bool ok;
            using (var proc = Process.Start(psi))
            {
                using (DialogCloser.Start(proc.Id, new[] { "Entity Limit" }, t => { lock (closed) closed.Add(t); }))
                {
                    if (!proc.WaitForExit((int)timeout.TotalMilliseconds))
                    {
                        try { proc.Kill(); } catch (InvalidOperationException) { }
                        proc.WaitForExit();
                        ok = false;
                    }
                    else ok = proc.ExitCode == 0;
                }
            }
            ok = ok && File.Exists(Path.Combine(modelDir, modelName + ".dir", modelName + "64.dll"));
            lock (closed)
                return new AmBuildResult(ok,
                    Read(Path.Combine(modelDir, modelName + ".dir", "make.err")),
                    Read(Path.Combine(modelDir, "am2err.dat")),
                    closed.ToArray());
        }

        static string Read(string p) => File.Exists(p) ? File.ReadAllText(p) : "";
    }
}
