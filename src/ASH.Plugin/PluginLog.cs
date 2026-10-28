using System;
using System.IO;

namespace ASH.Plugin;

/// <summary>Field diagnostics: %APPDATA%\ASH\plugin.log (kept under 256 KB). Never throws, so it is safe in every UI handler.</summary>
internal static class PluginLog
{
    private const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new object();

    public static string Path { get; } = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ASH", "plugin.log");

    public static void Write(string text)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                var fi = new FileInfo(Path);
                if (fi.Exists && fi.Length > MaxBytes) File.Delete(Path);
                File.AppendAllText(Path, ASH.Core.SeoulTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + text + Environment.NewLine);
            }
        }
        catch (Exception) { /* logging must never break the plugin */ }
    }

    public static void Write(string what, Exception ex) => Write(what + ": " + ex);

    /// <summary>`using (PluginLog.Time("…"))`: "시간 … 123 ms" when disposed, to find where a slow command spends its time.</summary>
    public static IDisposable Time(string label) => new Timing(label);

    private sealed class Timing : IDisposable
    {
        private readonly string _label;
        private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
        private bool _done;
        public Timing(string label) { _label = label; }
        public void Dispose() { if (_done) return; _done = true; Write($"시간 {_label}: {_sw.ElapsedMilliseconds} ms"); }
    }
}
