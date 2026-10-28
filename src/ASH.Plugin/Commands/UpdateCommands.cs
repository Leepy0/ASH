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
    private static readonly TimeSpan GitHubTimeout = TimeSpan.FromSeconds(20); // 프록시·WPAD 탐색 포함

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
        ed.WriteMessage("\n" + UpdateMessages.Current(UpdateNotifier.CurrentVersion));
        var (key, keyErr) = UpdateNotifier.LoadKey();
        if (keyErr != null) { Say(ed, keyErr + " — 업데이트를 중단했습니다."); return; } // never fall back to unsigned
        // 서명 키가 없는 빌드(기본)는 github.com 릴리스와 대조
        var github = key == null ? new GitHubManifestCheck(GitHubTimeout) : null;

        // the LAN server first (fast, works without internet); unset or unreachable: github.com itself, as the setup does (only for unsigned builds,
        // whose files are checked against github.com anyway). A signed build has no signature on github.com, so it stays with the server.
        UpdateResult? r = null;
        Func<TimeSpan, UpdateClient>? source = null;
        if (UserSettings.TryNormalizeUrl(UserSettings.Load().ServerUrl, out var url))
        {
            Say(ed, $"{url} 에서 새 버전을 확인하는 중…");
            r = Check(ed, t => new UpdateClient(new Uri(url), t), key, CheckTimeout);
            if (r != null) source = t => new UpdateClient(new Uri(url), t);
        }
        else ed.WriteMessage("\n서버 주소가 없습니다(AGVSERVER로 설정).");
        if (r == null)
        {
            if (github == null) { PaletteStatus.Set("AGVUPDATE: 확인 실패"); return; }
            Say(ed, "github.com 에서 새 버전을 확인하는 중…");
            r = Check(ed, t => UpdateClient.GitHub(t), key, GitHubTimeout);
            if (r == null) return;
            source = t => UpdateClient.GitHub(t);
        }
        else if (github != null && r.Status.State != UpdateState.Available && r.Status.State != UpdateState.BelowMinimum)
        {
            // the server says "latest": its GitHub pull may be stuck (proxy, DNS), so github.com gets the last word; a failure there keeps the server's answer
            ed.WriteMessage("\n" + r.Status.Message + " · github.com도 확인하는 중…");
            var g = Check(ed, t => UpdateClient.GitHub(t), key, GitHubTimeout);
            if (g != null && (g.Status.State == UpdateState.Available || g.Status.State == UpdateState.BelowMinimum)) { r = g; source = t => UpdateClient.GitHub(t); }
        }

        if (r.Status.State != UpdateState.Available && r.Status.State != UpdateState.BelowMinimum) { Say(ed, r.Status.Message); return; }

        Say(ed, $"ASH {r.Fetched.Manifest.Version} 다운로드 중… (최대 {DownloadTotal.TotalMinutes:0}분)");
        var dir = Path.Combine(Path.GetTempPath(), "ASH-update");
        string path;
        using var cts = new CancellationTokenSource(DownloadTotal);
        try
        {
            var download = Task.Run(async () =>
            {
                using var c = source!(DownloadIdleTimeout);
                return await c.DownloadVerifiedAsync(r.Fetched, key, dir, cts.Token, github).ConfigureAwait(false);
            });
            if (!download.Wait(DownloadTotal + HardCapGrace)) { Say(ed, UpdateMessages.DownloadFailed(new TimeoutException(), totalTimeout: true)); return; }
            path = download.GetAwaiter().GetResult();
        }
        catch (System.Exception e) { Say(ed, UpdateMessages.DownloadFailed(e, totalTimeout: cts.IsCancellationRequested)); return; }

        Say(ed, $"다운로드 완료: {path} — ZWCAD를 완전히 종료한 뒤 이 파일을 실행하세요");
        try { Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"/select,\"{path}\""); }
        catch (System.Exception e) { ed.WriteMessage("\n폴더를 열지 못했습니다: " + e.Message); }
    }

    /// <summary>The check against one source; null (said why) when it cannot be reached or answers badly.</summary>
    private static UpdateResult? Check(Editor ed, Func<TimeSpan, UpdateClient> make, ManifestSignature? key, TimeSpan timeout)
    {
        try
        {
            var check = Task.Run(async () =>
            {
                using var c = make(timeout);
                return await UpdateFlow.CheckAsync(c, key, UpdateNotifier.CurrentVersion).ConfigureAwait(false);
            });
            // hard cap: net48 DNS/WPAD can ignore the cancellation token
            if (check.Wait(timeout + HardCapGrace)) return check.GetAwaiter().GetResult();
            Say(ed, UpdateMessages.CheckFailed(new TimeoutException()));
        }
        catch (System.Exception e) { Say(ed, UpdateMessages.CheckFailed(e)); }
        return null;
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
        var msg = "\n서버 주소(예: http://server:8082)" + (current.Length > 0 ? $" <{current}, Enter=유지>" : "");
        var r = ed.GetString(new PromptStringOptions(msg) { AllowSpaces = false });
        if (r.Status != PromptStatus.OK || r.StringResult.Trim().Length == 0) return null;
        if (UserSettings.TryNormalizeUrl(r.StringResult, out var u)) return u;
        ed.WriteMessage("\nhttp:// 또는 https://로 시작하는 주소를 입력하세요.");
        return null;
    }
}
