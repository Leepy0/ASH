using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ASH.Core.AutoMod
{
    public sealed record AmBuildResult(bool Success, string MakeErrText, string Am2ErrText, IReadOnlyList<string> ClosedDialogTitles);

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
            { WorkingDirectory = modelDir, UseShellExecute = false };
            // AM's link.exe must win over any other link.exe (e.g. Git's usr\bin) on PATH.
            psi.EnvironmentVariables["PATH"] = Path.Combine(amHome, "bin") + ";" + Environment.GetEnvironmentVariable("PATH");
            bool ok;
            var started = DateTime.UtcNow;
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
            // 64비트 빌드는 <모델>64.dll, 구버전은 <모델>.dll — 둘 중 이번 빌드에서 만든 것이 있으면 성공
            var dirPath = Path.Combine(modelDir, modelName + ".dir");
            ok = ok && Array.Exists(new[] { modelName + "64.dll", modelName + ".dll" }, n =>
            {
                var dll = Path.Combine(dirPath, n);
                return File.Exists(dll) && File.GetLastWriteTimeUtc(dll) >= started;
            }); // a stale dll from an earlier build is not success
            lock (closed)
                return new AmBuildResult(ok,
                    Read(Path.Combine(modelDir, modelName + ".dir", "make.err")),
                    Read(Path.Combine(modelDir, "am2err.dat")),
                    closed.ToArray());
        }

        static string Read(string p) => File.Exists(p) ? File.ReadAllText(p) : "";
    }
}
