using System;
using System.Threading.Tasks;
using ASH.Core.Settings;
using ASH.Core.Update;
using ASH.Plugin.Ui;
using ASH.Plugin.Update;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Runtime;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ASH.Plugin.Commands.NoticeCommands))]

namespace ASH.Plugin.Commands;

public sealed class NoticeCommands
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HardCapGrace = TimeSpan.FromSeconds(2);

    /// <summary>Every approved notice the server has, seen or not, in the notice window.</summary>
    [CommandMethod("AGVNOTICE")]
    public void AgvNotice()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor;
        try { Run(ed); }
        catch (System.Exception ex) { Say(ed, "AGVNOTICE 오류: " + ex.Message); }
    }

    private static void Say(Editor ed, string text)
    {
        ed.WriteMessage("\n" + text);
        PaletteStatus.Set(text);
    }

    private static void Run(Editor ed)
    {
        if (!UserSettings.TryNormalizeUrl(UserSettings.Load().ServerUrl, out var url)) { Say(ed, "서버 주소가 없습니다(AGVSERVER로 설정)"); return; }
        NoticeList list;
        try
        {
            // "0.0.0": everything the server has approved, not only what is newer than this PC
            var fetch = Task.Run(() => NoticeFeed.FetchAsync(new Uri(url), "0.0.0", Timeout));
            if (!fetch.Wait(Timeout + HardCapGrace)) throw new TimeoutException(); // net48 DNS/WPAD can ignore the cancellation token
            list = fetch.GetAwaiter().GetResult();
        }
        catch (System.Exception e) { Say(ed, $"공지 확인 실패({UpdateMessages.Reason(e)})"); return; }
        if (list.Notices.Count == 0) { Say(ed, "공지 없음"); return; }
        // the update runs here, not through AgvPalette.Send: this command is still running, and Send refuses while one is
        if (NoticeNotifier.ShowWindow(list.Notices, list.Latest, viewAll: true)) UpdateCommands.RunUpdate(ed);
    }
}
