using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ASH.Core.Film;

/// <summary>
/// What a hung or empty AutoView run leaves behind, as one Korean line for the film page and film.log: the program and its version, the visible
/// windows of the AutoView process tree (a dialog nobody clicked shows here by its title: add it to Film:DialogTitles), the server's session,
/// the files AutoView writes (guide 6.2/6.3, 5.2: camera.des, aviewerr/aviewout.dat, convert.bat, the frame JPGs) and whether ffmpeg is on AutoView's PATH.
/// </summary>
public static class AutoViewDiagnostics
{
    /// <summary>"path (버전 x)" from the file's version resource.</summary>
    public static string Program(string exe)
    {
        string? v = null;
        try { var i = FileVersionInfo.GetVersionInfo(exe); v = i.FileVersion ?? i.ProductVersion; }
        catch (Exception e) when (e is IOException || e is ArgumentException) { return exe + " (파일 없음)"; }
        return $"{exe} (버전 {(string.IsNullOrWhiteSpace(v) ? "정보 없음" : v!.Trim())})";
    }

    /// <param name="pid">the running AutoView (its windows are listed); null once it has ended</param>
    /// <param name="avw">the model's .avw folder (camera.des is written there)</param>
    /// <param name="movie">the camera script's filename (no extension): frames are in movie_frames\</param>
    /// <param name="pathPrefix">what was put before PATH for AutoView (its convert.bat calls plain "ffmpeg")</param>
    public static string Text(string exe, int? pid, string workDir, string avw, string movie, string pathPrefix)
    {
        var parts = new List<string> { "진단: AutoView " + Program(exe) };
        if (pid is { } id)
        {
            List<string> w;
            try { w = Windows(id); } catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException) { w = new List<string>(); }
            parts.Add(w.Count == 0 ? "보이는 창 없음" : "창 " + string.Join(" / ", w.Take(8)));
        }
        int session;
        try { session = Process.GetCurrentProcess().SessionId; } catch (InvalidOperationException) { session = -1; }
        parts.Add($"서버 세션 {session}" + (session == 0 ? "(서비스: 화면 없음)" : ""));
        parts.Add(File.Exists(Path.Combine(avw, "camera.des")) ? "camera.des 있음" : "camera.des 없음");
        foreach (var n in new[] { "aviewerr.dat", "aviewout.dat" })
        {
            var f = Path.Combine(workDir, n);
            string? text = null;
            try { if (File.Exists(f)) text = File.ReadAllText(f); } catch (IOException) { text = "(읽지 못함)"; }
            parts.Add(text == null ? n + " 없음" : text.Trim().Length == 0 ? n + " 비어 있음" : $"{n} \"{OneLine(Tail(text, 400))}\"");
        }
        parts.Add(File.Exists(movie + ".mp4") ? $"{Path.GetFileName(movie)}.mp4 있음" : $"{Path.GetFileName(movie)}.mp4 없음");
        var avis = new[] { workDir, avw }.Where(Directory.Exists).SelectMany(d => Directory.GetFiles(d, "*.avi")).Select(f => $"{Path.GetFileName(f)}({new FileInfo(f).Length}바이트)").ToList();
        parts.Add(avis.Count == 0 ? "avi 없음" : "avi " + string.Join(", ", avis.Take(5)));
        parts.Add(File.Exists(Path.Combine(workDir, "convert.bat")) ? "convert.bat 있음" : "convert.bat 없음");
        var frames = movie + "_frames";
        int jpgs = 0;
        try { if (Directory.Exists(frames)) jpgs = Directory.GetFiles(frames, "*.jpg").Length; } catch (IOException) { }
        parts.Add($"프레임 JPG {jpgs}개");
        parts.Add(Which("ffmpeg", pathPrefix) is { } ff ? "ffmpeg " + ff : "ffmpeg 없음(AutoView의 PATH에서 못 찾음)");
        return string.Join(" · ", parts);
    }

    /// <summary>The first "name(.exe|.cmd|.bat)" in pathPrefix;PATH, as a child started with that PATH would find it; null when none.</summary>
    internal static string? Which(string name, string pathPrefix)
    {
        var dirs = (pathPrefix + ";" + Environment.GetEnvironmentVariable("PATH")).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var d in dirs)
            foreach (var ext in new[] { ".exe", ".cmd", ".bat" })
            {
                try { var p = Path.Combine(d.Trim().Trim('"'), name + ext); if (File.Exists(p)) return p; }
                catch (ArgumentException) { } // a PATH entry with bad characters
            }
        return null;
    }

    static string Tail(string s, int max) { s = s.Trim(); return s.Length <= max ? s : "…" + s.Substring(s.Length - max); }
    static string OneLine(string s) => Regex.Replace(s, @"\s*\r?\n\s*", " | ");

    // ---- Win32: windows of a process tree ----
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern int GetWindowTextLengthW(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snap, ref ProcessEntry e);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snap, ref ProcessEntry e);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcessEntry
    {
        public uint Size, Usage, Pid;
        public IntPtr HeapId;
        public uint ModuleId, Threads, ParentPid;
        public int PriClassBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }

    static string Text(IntPtr h)
    {
        var sb = new StringBuilder(GetWindowTextLengthW(h) + 1);
        GetWindowTextW(h, sb, sb.Capacity);
        return sb.ToString();
    }

    static string Class(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetClassNameW(h, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>The process and its descendants, pid -> exe name (one process snapshot; a reused parent pid may add a stranger, fine for a report).</summary>
    static Dictionary<uint, string> Tree(int root)
    {
        var all = new List<(uint Pid, uint Parent, string Exe)>();
        var snap = CreateToolhelp32Snapshot(2 /* TH32CS_SNAPPROCESS */, 0);
        if (snap != new IntPtr(-1))
            try
            {
                var e = new ProcessEntry { Size = (uint)Marshal.SizeOf(typeof(ProcessEntry)) };
                for (var ok = Process32FirstW(snap, ref e); ok; ok = Process32NextW(snap, ref e)) all.Add((e.Pid, e.ParentPid, e.ExeFile));
            }
            finally { CloseHandle(snap); }
        var tree = new Dictionary<uint, string> { [(uint)root] = all.FirstOrDefault(x => x.Pid == (uint)root).Exe ?? "?" };
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var x in all)
                if (x.Pid != x.Parent && tree.ContainsKey(x.Parent) && !tree.ContainsKey(x.Pid)) { tree[x.Pid] = x.Exe; grew = true; }
        }
        return tree;
    }

    /// <summary>Every visible top-level window of the process tree: "title" [class] 버튼 a/b 내용 "static text" (exe).</summary>
    internal static List<string> Windows(int rootPid)
    {
        var tree = Tree(rootPid);
        var list = new List<string>();
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var pid);
            if (!tree.TryGetValue(pid, out var exe) || !IsWindowVisible(h)) return true;
            var buttons = new List<string>();
            var statics = new List<string>();
            EnumChildWindows(h, (c, __) =>
            {
                var t = Text(c).Replace("&", "").Trim();
                if (t.Length == 0) return true;
                var cls = Class(c);
                if (cls == "Button") buttons.Add(t);
                else if (cls == "Static") statics.Add(t);
                return true;
            }, IntPtr.Zero);
            var s = $"\"{Text(h)}\" [{Class(h)}]";
            if (buttons.Count > 0) s += " 버튼 " + string.Join("/", buttons.Take(6));
            var said = OneLine(string.Join(" ", statics));
            if (said.Length > 160) said = said.Substring(0, 160) + "…";
            if (said.Length > 0) s += $" 내용 \"{said}\"";
            list.Add(s + $" ({exe})");
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
