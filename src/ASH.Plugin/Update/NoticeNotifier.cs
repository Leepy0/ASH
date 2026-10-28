using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using ASH.Core.Update;
using ASH.Plugin.Ui;
using ZwSoft.ZwCAD.ApplicationServices;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;

namespace ASH.Plugin.Update;

/// <summary>Startup notices, driven by <see cref="UpdateNotifier"/>: the worker thread fetches and works out what is unseen (<see cref="FetchAsync"/>,
/// never throws), the UI thread shows it (<see cref="Present"/>). Also the window shared with AGVNOTICE.</summary>
internal static class NoticeNotifier
{
    internal sealed class Startup
    {
        public Startup(string latest, List<Notice> unseen) { Latest = latest; Unseen = unseen; }
        public string Latest { get; }
        public List<Notice> Unseen { get; }
    }

    private static readonly TimeSpan HardCapGrace = TimeSpan.FromSeconds(2);

    /// <summary>Worker thread. Notices newer than this plugin that are not seen yet; null on any failure or when there are none (silent).</summary>
    internal static async Task<Startup?> FetchAsync(Uri server, TimeSpan timeout)
    {
        try
        {
            var fetch = NoticeFeed.FetchAsync(server, UpdateNotifier.CurrentVersion, timeout);
            // net48 DNS/WPAD can ignore the cancellation token
            if (await Task.WhenAny(fetch, Task.Delay(timeout + HardCapGrace)).ConfigureAwait(false) != fetch) return null;
            var list = await fetch.ConfigureAwait(false);
            var unseen = NoticeFeed.Unseen(list.Notices, NoticeFeed.LoadSeen(NoticeFeed.SeenPath));
            return unseen.Count == 0 ? null : new Startup(list.Latest, unseen);
        }
        catch (Exception e) { Debug.WriteLine("ASH notices: " + e.Message); return null; }
    }

    /// <summary>UI thread, idle, a drawing is open. Important unseen: the window (nothing is marked unless the user answers 확인 or 지금 업데이트).
    /// Otherwise one line on the command line and the palette status; nothing is marked.</summary>
    internal static void Present(Document doc, Startup s, string? updateLine)
    {
        PaletteStatus.SetNoticeUnseen(true);
        if (NoticeFeed.ShouldPopup(s.Unseen))
        {
            if (ShowWindow(s.Unseen, s.Latest, viewAll: false)) AgvPalette.Send("AGVUPDATE");
            return;
        }
        var line = NoticeFeed.Summary(s.Unseen.Count);
        doc.Editor.WriteMessage("\n" + line);
        PaletteStatus.Set(updateLine == null ? line : updateLine + " · " + line);
    }

    /// <summary>Shows the window; 확인 and 지금 업데이트 mark what was shown as seen, 나중에/닫기 marks nothing. True when the user wants the update.</summary>
    internal static bool ShowWindow(IReadOnlyList<Notice> notices, string latest, bool viewAll)
    {
        using var dlg = new NoticeDialog(notices, NoticeFeed.UpdateAvailable(latest, UpdateNotifier.CurrentVersion), viewAll);
        Application.ShowModalDialog(dlg);
        if (dlg.Choice == NoticeChoice.Later) return false;
        try { NoticeFeed.MarkSeen(NoticeFeed.SeenPath, notices); }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Debug.WriteLine("ASH notices seen: " + e.Message); }
        PaletteStatus.SetNoticeUnseen(false);
        return dlg.Choice == NoticeChoice.Update;
    }
}
