using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ASH.Core.AutoMod
{
    /// <summary>Why a build failed when AutoMod's own error files stay empty: Facts = exit code or time-out, whether the dll was made, AutoMod's console output (last part).</summary>
    public sealed record AmBuildResult(bool Success, string MakeErrText, string Am2ErrText, IReadOnlyList<string> ClosedDialogTitles, string Facts = "");

    public static class AmBuilder
    {
        // 버전별 빌드 실행 파일: 12.6 이상은 am264.exe, 12.4 이하는 am2.exe
        static readonly string[] BuildExeNames = { "am264.exe", "am2.exe" };

        /// <summary>bin 폴더에서 빌드 exe를 찾는다. 없으면 null.</summary>
        public static string? FindBuildExe(string amHome)
        {
            foreach (var n in BuildExeNames)
            {
                var p = Path.Combine(amHome, "bin", n);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        /// <summary>오류 메시지용: 찾아본 경로 목록</summary>
        public static string BuildExeCandidates(string amHome) =>
            string.Join(" 또는 ", Array.ConvertAll(BuildExeNames, n => Path.Combine(amHome, "bin", n)));

        public static AmBuildResult Build(string amHome, string modelDir, string modelName, TimeSpan timeout)
        {
            var closed = new List<string>();
            var exe = FindBuildExe(amHome) ?? throw new FileNotFoundException("AutoMod 빌드 실행 파일이 없습니다: " + BuildExeCandidates(amHome));
            var psi = new ProcessStartInfo(exe, "-bq " + modelName)
            { WorkingDirectory = modelDir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            // AM's link.exe must win over any other link.exe (e.g. Git's usr\bin) on PATH.
            psi.EnvironmentVariables["PATH"] = Path.Combine(amHome, "bin") + ";" + Environment.GetEnvironmentVariable("PATH");
            bool ok, timedOut = false;
            int? exit = null;
            var said = new System.Text.StringBuilder(); // the build's console output: often the only reason when make.err/am2err.dat stay empty
            var started = DateTime.UtcNow;
            using (var proc = Process.Start(psi))
            {
                DataReceivedEventHandler keep = (_, e) => { if (e.Data == null) return; lock (said) { said.AppendLine(e.Data); if (said.Length > 8000) said.Remove(0, said.Length - 4000); } };
                proc.OutputDataReceived += keep; proc.ErrorDataReceived += keep;
                proc.BeginOutputReadLine(); proc.BeginErrorReadLine();
                using (DialogCloser.Start(proc.Id, new[] { "Entity Limit" }, t => { lock (closed) closed.Add(t); }))
                {
                    if (!proc.WaitForExit((int)timeout.TotalMilliseconds))
                    {
                        try { proc.Kill(); } catch (InvalidOperationException) { }
                        proc.WaitForExit();
                        ok = false; timedOut = true;
                    }
                    else { proc.WaitForExit(); exit = proc.ExitCode; ok = exit == 0; }
                }
            }
            // 64비트 빌드는 <모델>64.dll, 구버전은 <모델>.dll — 둘 중 이번 빌드에서 만든 것이 있으면 성공
            var dirPath = Path.Combine(modelDir, modelName + ".dir");
            var made = Array.Exists(new[] { modelName + "64.dll", modelName + ".dll" }, n =>
            {
                var dll = Path.Combine(dirPath, n);
                return File.Exists(dll) && File.GetLastWriteTimeUtc(dll) >= started;
            }); // a stale dll from an earlier build is not success
            ok = ok && made;
            string output; lock (said) output = said.ToString().Trim();
            var facts = (timedOut ? $"시간 초과({timeout.TotalMinutes:0}분)" : $"종료 코드 {exit}") + (made ? " · dll 있음" : " · dll 없음") +
                        (output.Length > 0 ? " · 출력: " + (output.Length > 600 ? "…" + output.Substring(output.Length - 600) : output).Replace("\r", "").Replace("\n", " | ") : " · 출력 없음");
            lock (closed)
                return new AmBuildResult(ok,
                    Read(Path.Combine(modelDir, modelName + ".dir", "make.err")),
                    Read(Path.Combine(modelDir, "am2err.dat")),
                    closed.ToArray(), facts);
        }

        static string Read(string p) => File.Exists(p) ? File.ReadAllText(p) : "";

        /// <summary>True when &lt;Model&gt;.exe exists and the built dll in &lt;Model&gt;.dir is not older than any file in &lt;Model&gt;.arc.
        /// params.txt is left out: it is read at run time, and ash-run rewrites it before every run.</summary>
        public static bool IsBuilt(string modelDir, string modelName)
        {
            var arc = Path.Combine(modelDir, modelName + ".arc");
            if (!File.Exists(Path.Combine(modelDir, modelName + ".exe")) || !Directory.Exists(arc)) return false;
            var built = DateTime.MinValue; // the exe is a copy of runModel64.exe with the install's old date, so the dll tells when the build ran
            foreach (var n in new[] { modelName + "64.dll", modelName + ".dll" })
            {
                var dll = Path.Combine(modelDir, modelName + ".dir", n);
                if (File.Exists(dll) && File.GetLastWriteTimeUtc(dll) > built) built = File.GetLastWriteTimeUtc(dll);
            }
            if (built == DateTime.MinValue) return false;
            foreach (var f in Directory.GetFiles(arc, "*", SearchOption.AllDirectories))
                if (!string.Equals(Path.GetFileName(f), "params.txt", StringComparison.OrdinalIgnoreCase) && File.GetLastWriteTimeUtc(f) > built) return false;
            return true;
        }

        /// <summary>Opens the built model in AutoMod's simulation window (graphics on, unlike ModelRunner's "-w" batch run) and returns without waiting.</summary>
        public static Process Launch(string amHome, string modelDir, string modelName)
        {
            var psi = new ProcessStartInfo(Path.Combine(modelDir, modelName + ".exe")) { WorkingDirectory = modelDir, UseShellExecute = false };
            psi.EnvironmentVariables["PATH"] = Path.Combine(amHome, "bin") + ";" + Environment.GetEnvironmentVariable("PATH"); // as Build: AM's dlls first
            return Process.Start(psi);
        }
    }
}
