using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ASH.Core.Settings;
using ASH.Core.Update;
using ASH.Plugin.Ui;
using ASH.Plugin.Update;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Runtime;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ASH.Plugin.Commands.UpdateCommands))]

namespace ASH.Plugin.Commands;

public sealed class UpdateCommands
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DownloadIdleTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DownloadTotal = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HardCapGrace = TimeSpan.FromSeconds(2);

    [CommandMethod("AGVUPDATE")]
    public void AgvUpdate() => Guarded("AGVUPDATE", RunUpdate);

    [CommandMethod("AGVSERVER")]
    public void AgvServer() => Guarded("AGVSERVER", RunServer);

    // No transaction or document lock: these touch no drawing. Errors go to the command line, never out of the command.
    private static void Guarded(string cmd, Action<Editor> body)
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        try { body(doc.Editor); }
        catch (System.Exception ex) { Say(doc.Editor, $"{cmd} 오류: {ex.Message}"); }
    }

    private static void Say(Editor ed, string text)
    {
        ed.WriteMessage("\n" + text);
        PaletteStatus.Set(text);
    }

    private static void RunUpdate(Editor ed)
    {
        string url;
        if (!UserSettings.TryNormalizeUrl(UserSettings.Load().ServerUrl, out url))
        {
            var entered = PromptUrl(ed, "");
            if (entered == null) { PaletteStatus.Set("AGVUPDATE 취소"); return; }
            url = entered;
            (UserSettings.Load() with { ServerUrl = url }).Save();
        }

        var (key, keyErr) = UpdateNotifier.LoadKey();
        if (keyErr != null) { Say(ed, keyErr + " — 업데이트를 중단했습니다."); return; } // never fall back to unsigned
        if (key == null) ed.WriteMessage("\n경고: 서명 키 미설정 — SHA-256만 확인");

        Say(ed, $"{url} 에서 새 버전을 확인하는 중…");
        UpdateResult r;
        try
        {
            var check = Task.Run(async () =>
            {
                using var c = new UpdateClient(new Uri(url), CheckTimeout);
                return await UpdateFlow.CheckAsync(c, key, UpdateNotifier.CurrentVersion).ConfigureAwait(false);
            });
            // hard cap: net48 DNS/WPAD can ignore the cancellation token
            if (!check.Wait(CheckTimeout + HardCapGrace)) { Say(ed, UpdateMessages.CheckFailed(new TimeoutException())); return; }
            r = check.GetAwaiter().GetResult();
        }
        catch (System.Exception e) { Say(ed, UpdateMessages.CheckFailed(e)); return; }

        if (r.Status.State != UpdateState.Available && r.Status.State != UpdateState.BelowMinimum) { Say(ed, r.Status.Message); return; }

        Say(ed, $"ASH {r.Fetched.Manifest.Version} 다운로드 중… (최대 {DownloadTotal.TotalMinutes:0}분)");
        var dir = Path.Combine(Path.GetTempPath(), "ASH-update");
        string path;
        using var cts = new CancellationTokenSource(DownloadTotal);
        try
        {
            var download = Task.Run(async () =>
            {
                using var c = new UpdateClient(new Uri(url), DownloadIdleTimeout);
                return await c.DownloadVerifiedAsync(r.Fetched, key, dir, cts.Token).ConfigureAwait(false);
            });
            if (!download.Wait(DownloadTotal + HardCapGrace)) { Say(ed, UpdateMessages.DownloadFailed(new TimeoutException(), totalTimeout: true)); return; }
            path = download.GetAwaiter().GetResult();
        }
        catch (System.Exception e) { Say(ed, UpdateMessages.DownloadFailed(e, totalTimeout: cts.IsCancellationRequested)); return; }

        Say(ed, $"다운로드 완료: {path} — ZWCAD를 완전히 종료한 뒤 이 파일을 실행하세요");
        try { Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"/select,\"{path}\""); }
        catch (System.Exception e) { ed.WriteMessage("\n폴더를 열지 못했습니다: " + e.Message); }
    }

    private static void RunServer(Editor ed)
    {
        var cur = UserSettings.Load();
        ed.WriteMessage($"\n서버 주소: {(cur.ServerUrl.Length == 0 ? "(설정 안 됨)" : cur.ServerUrl)}  (파일 {UserSettings.DefaultPath})");
        var u = PromptUrl(ed, cur.ServerUrl);
        if (u == null) { PaletteStatus.Set("AGVSERVER: 변경 없음"); return; }
        (cur with { ServerUrl = u }).Save();
        Say(ed, "서버 주소를 저장했습니다: " + u);
    }

    /// <summary>Null on cancel, empty input (keep) or an invalid address.</summary>
    private static string? PromptUrl(Editor ed, string current)
    {
        var msg = "\n서버 주소(예: http://server:5080)" + (current.Length > 0 ? $" <{current}, Enter=유지>" : "");
        var r = ed.GetString(new PromptStringOptions(msg) { AllowSpaces = false });
        if (r.Status != PromptStatus.OK || r.StringResult.Trim().Length == 0) return null;
        if (UserSettings.TryNormalizeUrl(r.StringResult, out var u)) return u;
        ed.WriteMessage("\nhttp:// 또는 https://로 시작하는 주소를 입력하세요.");
        return null;
    }
}
