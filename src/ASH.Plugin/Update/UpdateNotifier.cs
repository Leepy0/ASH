using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ASH.Core.Settings;
using ASH.Core.Update;
using ASH.Plugin.Ui;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;

namespace ASH.Plugin.Update;

/// <summary>
/// Startup update check. The worker thread only fetches and decides; the line is printed from one Application.Idle handler on the UI thread.
/// Nothing here may throw into ZWCAD or wait on the network on the UI thread.
/// </summary>
internal static class UpdateNotifier
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private static string? _message;   // written before _done, read after it
    private static int _done;
    private static EventHandler? _idle;

    /// <summary>Plugin version (AssemblyInformationalVersion = &lt;Version&gt;, possibly with +build, which SemVersion ignores).</summary>
    internal static string CurrentVersion =>
        typeof(UpdateNotifier).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? throw new InvalidOperationException("플러그인 버전 정보 없음");

    /// <summary>The embedded update-key.pub, if the build had one. A key that cannot be read is an error, never unsigned mode.</summary>
    internal static (ManifestSignature? Key, string? Error) LoadKey()
    {
        using var s = typeof(UpdateNotifier).Assembly.GetManifestResourceStream("update-key.pub");
        return UpdateFlow.LoadKey(s);
    }

    /// <summary>UI thread, from Initialize. Does nothing without a configured server.</summary>
    internal static void Start()
    {
        var url = UserSettings.Load().ServerUrl;
        if (url.Length == 0) return;
        _idle = OnIdle;
        Application.Idle += _idle;
        Task.Run(() => Work(url));
    }

    internal static void Stop()
    {
        if (_idle == null) return;
        Application.Idle -= _idle;
        _idle = null;
    }

    private static async Task Work(string url)
    {
        string? m;
        try
        {
            var (key, err) = LoadKey();
            if (err != null) m = UpdateMessages.CheckFailed(new InvalidDataException(err));
            else if (!UserSettings.TryNormalizeUrl(url, out var u)) m = UpdateMessages.CheckFailed(new InvalidDataException("서버 주소 형식 오류"));
            else m = await UpdateFlow.StartupMessageAsync(new Uri(u), Timeout, key, CurrentVersion).ConfigureAwait(false);
        }
        catch (Exception e) { m = UpdateMessages.CheckFailed(e); }
        _message = m;
        Volatile.Write(ref _done, 1);
    }

    private static void OnIdle(object? sender, EventArgs e)
    {
        try
        {
            if (Volatile.Read(ref _done) == 0) return;
            var m = _message;
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (m != null && doc == null) return; // nowhere to print yet, try again at the next idle
            Stop();
            if (m == null) return;
            doc!.Editor.WriteMessage("\n" + m);
            PaletteStatus.Set(m);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("ASH update: " + ex);
            try { Stop(); } catch (Exception) { }
        }
    }
}
