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
                File.AppendAllText(Path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + text + Environment.NewLine);
            }
        }
        catch (Exception) { /* logging must never break the plugin */ }
    }

    public static void Write(string what, Exception ex) => Write(what + ": " + ex);
}
