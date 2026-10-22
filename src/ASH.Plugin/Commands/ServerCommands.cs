using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ASH.Core;
using ASH.Core.Runner;
using ASH.Core.Settings;
using ASH.Plugin.Ui;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Runtime;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ASH.Plugin.Commands.ServerCommands))]

namespace ASH.Plugin.Commands;

public sealed class ServerCommands
{
    private static readonly TimeSpan QueueTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan UploadTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan HardCapGrace = TimeSpan.FromSeconds(2);

    [CommandMethod("AGVSEND")]
    public void AgvSend() => Command("AGVSEND", results: false);

    /// <summary>Uploads what ash-run computed on this PC (runs\summary.tsv, search.tsv, the runs' result.tsv) with the model, for the same result pages.</summary>
    [CommandMethod("AGVRESULTUP")]
    public void AgvResultUp() => Command("AGVRESULTUP", results: true);

    internal static void Command(string name, bool results)
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor;
        var zip = Path.Combine(Path.GetTempPath(), "ASH-send-" + Guid.NewGuid().ToString("N") + ".zip");
        try { Send(ed, zip, results); }
        catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
        {
            Say(ed, name + " 오류: " + ex.Message);
            PluginLog.Write(name, ex);
        }
        finally { try { File.Delete(zip); } catch (System.Exception) { /* temp file; the OS cleans it up eventually */ } }
    }

    private static void Send(Editor ed, string zip, bool results)
    {
        if (!ExportCommands.TryLoadSettings(ed, out var s, out var dir)) return;
        var missing = JobPackage.Missing(dir, s.ModelName);
        if (missing.Count > 0)
        {
            ed.WriteMessage("\n보낼 파일이 없습니다: " + string.Join(", ", missing));
            Say(ed, "먼저 모델 생성(AGVMODEL)을 실행하세요.");
            return;
        }
        if (results && JobPackage.MissingResults(dir) is { Count: > 0 } noRuns)
        {
            ed.WriteMessage("\n올릴 결과가 없습니다: " + string.Join(", ", noRuns));
            Say(ed, "먼저 이 PC에서 ash-run으로 대수 탐색을 실행하세요(ash-run 0.12.0 이상이 runs\\search.tsv를 남깁니다).");
            return;
        }

        if (!UserSettings.TryNormalizeUrl(UserSettings.Load().ServerUrl, out var url))
        {
            Say(ed, $"서버 주소가 설정되지 않았습니다. AGVSERVER로 설정하세요(파일 {UserSettings.DefaultPath}).");
            return;
        }

        QueueInfo q;
        try
        {
            using var qc = new JobServerClient(new Uri(url));
            q = Run(qc.GetQueueAsync, QueueTimeout);
        }
        catch (System.Exception e) when (IsNetwork(e)) { Fail(ed, "서버에 연결하지 못했습니다", e); return; }
        if (!q.Enabled && !results) { Say(ed, "이 서버는 작업을 실행하지 않습니다(서버 설정에서 꺼져 있음)."); return; }
        if (results)
        {
            // nothing is queued for an uploaded result: only the question
            if (PathCommands.Keyword(ed, "\n이 PC의 ash-run 결과를 서버에 올릴까요? [Y/N] <Y>", "Y", "Y", "N") != "Y") { PaletteStatus.Set("AGVRESULTUP: 취소"); return; }
        }
        else
        {
            ed.WriteMessage($"\n서버 대기 {q.Ahead}건");
            if (!string.IsNullOrEmpty(q.EstimatedStart)) ed.WriteMessage($" · 예상 시작 {SeoulTime.FromIso(q.EstimatedStart)}");
            if (q.TypicalMinutes is double m) ed.WriteMessage($" · 보통 {m:0.#}분 걸림");
            if (PathCommands.Keyword(ed, "\n서버로 보낼까요? [Y/N] <Y>", "Y", "Y", "N") != "Y") { PaletteStatus.Set("AGVSEND: 취소"); return; }
        }

        Say(ed, "패키지 만드는 중…");
        var files = JobPackage.Create(dir, s.ModelName, zip, includeResults: results);
        Say(ed, $"서버로 보내는 중… (파일 {files.Count}개, {new FileInfo(zip).Length / 1048576.0:0.#} MB, 최대 {UploadTimeout.TotalMinutes:0}분)");
        var job = new JobSubmission(Path.GetFileName(dir.TrimEnd('\\', '/')), Path.GetFileNameWithoutExtension((string)Application.GetSystemVariable("DWGNAME")),
            s.ModelName, Environment.UserName);
        try
        {
            using var client = new JobServerClient(new Uri(url));
            var receipt = Run(ct => client.SubmitAsync(zip, job, ct), UploadTimeout);
            Say(ed, results ? $"결과 올림: {receipt.Id}" : $"전송 완료: 작업 {receipt.Id} (앞에 {receipt.Ahead}건)");
            var page = client.PageUri(receipt);
            if (page == null) { ed.WriteMessage("\n서버가 알려 준 주소가 올바르지 않아 브라우저를 열지 않습니다: " + receipt.Url); return; }
            ed.WriteMessage("\n" + page);
            try { Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception ex) { ed.WriteMessage($"\n브라우저를 열지 못했습니다: {ex.Message}"); }
        }
        catch (JobServerException e)
        {
            Say(ed, $"서버가 받지 않았습니다(HTTP {e.Status}): {e.Body}");
            PluginLog.Write("AGVSEND rejected", e);
        }
        catch (System.Exception e) when (IsNetwork(e)) { Fail(ed, "전송하지 못했습니다", e); }
    }

    // Task.Run so ZWCAD's UI sync context cannot deadlock the wait; the hard cap is for net48 DNS/WPAD stalls that ignore the token
    private static T Run<T>(Func<CancellationToken, Task<T>> f, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var task = Task.Run(() => f(cts.Token));
        var first = Task.WhenAny(task, Task.Delay(timeout + HardCapGrace)).GetAwaiter().GetResult();
        if (first != task) throw new TimeoutException();
        return task.GetAwaiter().GetResult();
    }

    private static bool IsNetwork(System.Exception e)
        => e is HttpRequestException || e is OperationCanceledException || e is TimeoutException || e is IOException || e is JobServerException;

    private static void Fail(Editor ed, string what, System.Exception e)
    {
        var why = e is JobServerException j ? $"서버 응답 {j.Status}: {j.Body}"
            : e is OperationCanceledException || e is TimeoutException ? "시간 초과"
            : e is InvalidDataException ? e.Message
            : (e.InnerException ?? e).Message;
        Say(ed, $"{what}: {why}");
        PluginLog.Write("AGVSEND", e);
    }

    private static void Say(Editor ed, string text)
    {
        ed.WriteMessage("\n" + text);
        PaletteStatus.Set(text);
    }
}
