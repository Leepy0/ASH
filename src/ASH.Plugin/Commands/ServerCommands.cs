using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ASH.Core;
using ASH.Core.Analysis;
using ASH.Core.Dxf;
using ASH.Core.Layout;
using ASH.Core.Model;
using ASH.Core.Runner;
using ASH.Core.Settings;
using ASH.Plugin.Cad;
using ASH.Plugin.Ui;
using ZwSoft.ZwCAD.DatabaseServices;
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
    public void AgvSend()
    {
        // not in Command: AGVMODEL ends in Command too and has warned already
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc != null)
            try { Cad.CpViewer.WarnIfHidden(doc); }
            catch (System.Exception ex) { PluginLog.Write("AGVSEND hidden CP check", ex); } // only a courtesy line
        Command("AGVSEND", results: false);
    }

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
        // a normal send checks for the .arc only after the queue answer: a new server (generates) makes the model from the source itself
        if (results && !ModelReady(ed, dir, s.ModelName)) return;
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
        // new server: send the source (drawing + Excel, checked here before anything leaves the PC); an old one or a result upload still gets the model
        bool source = !results && q.Generates;
        if (!results && !source && !ModelReady(ed, dir, s.ModelName)) return;
        if (source && !PrepareSource(ed, s, dir)) return;
        // a 0.18 server has no /api/analyses: send unclassified and say so in the dialog
        IReadOnlyList<AnalysisSummary>? analyses = null;
        try
        {
            using var ac = new JobServerClient(new Uri(url));
            analyses = Run(ac.GetAnalysesAsync, QueueTimeout);
        }
        catch (System.Exception e) when (IsNetwork(e) || e is InvalidDataException) { PluginLog.Write("AGVSEND analyses", e); }

        var folder = Path.GetFileName(dir.TrimEnd('\\', '/'));
        var drawing = Path.GetFileNameWithoutExtension((string)Application.GetSystemVariable("DWGNAME"));
        var info = results ? "이 PC의 ash-run 결과를 모델과 함께 서버에 올립니다." : $"서버 대기 {q.Ahead}건";
        if (!results)
        {
            if (!string.IsNullOrEmpty(q.Busy)) info += $" ({q.Busy})";
            if (!string.IsNullOrEmpty(q.EstimatedStart)) info += $" · 예상 시작 {SeoulTime.FromIso(q.EstimatedStart)}";
            if (q.TypicalMinutes is double m) info += $" · 보통 {m:0.#}분 걸림";
        }
        var initial = analyses == null ? new SendChoice() : SendChoice.Default(analyses, folder, drawing, SendChoice.Load(dir));
        using var dlg = new SendDialog(results ? "ASH 결과 올리기" : "ASH 서버로 보내기", info, results ? "올리기" : "보내기", analyses, initial);
        if (Application.ShowModalDialog(dlg) != DialogResult.OK) { PaletteStatus.Set((results ? "AGVRESULTUP" : "AGVSEND") + ": 취소"); return; }
        var c = dlg.Choice;
        if (analyses != null)
        {
            try { c.Save(dir); }
            catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { PluginLog.Write("ash.send.tsv", ex); }
        }

        var (abort, dwg) = results ? (false, null) : DwgToSend(ed); // ask before any package work; a result upload never carries the drawing
        if (abort) { PaletteStatus.Set("AGVSEND: 취소"); return; }
        Say(ed, source ? "원본 패키지 만드는 중…" : "패키지 만드는 중…");
        // the factory drawing and its coordinate frame go along for the server's cut editor and videos; a DXF failure is only a warning
        new ModelFrame(s.Units, s.Origin.X, s.Origin.Y, s.Scale).Write(Path.Combine(dir, ModelFrame.FileName));
        SaveDxf(ed, Path.Combine(dir, JobPackage.LocalDxf)); // own name: a drawing.dxf of the user's in this folder is left alone
        IReadOnlyList<PackageFile> files;
        IReadOnlyList<PackageFile> Pack(string? dwgPath) => source ? JobPackage.CreateSource(dir, zip, dwgPath) : JobPackage.Create(dir, s.ModelName, zip, includeResults: results, dwgPath: dwgPath);
        try { files = Pack(dwg); }
        catch (IOException) when (dwg != null) // ZWCAD may hold the DWG open exclusively
        {
            ed.WriteMessage("\nDWG를 열 수 없어 DWG 없이 보냅니다.");
            files = Pack(null);
        }
        Say(ed, $"서버로 보내는 중… (파일 {files.Count}개, {new FileInfo(zip).Length / 1048576.0:0.#} MB, 최대 {UploadTimeout.TotalMinutes:0}분)");
        var job = new JobSubmission(folder, drawing, s.ModelName, Environment.UserName,
            AnalysisId: c.AnalysisId, PlanId: c.PlanId, AnalysisName: c.AnalysisName, PlanName: c.PlanName, VersionNote: dlg.Note,
            Corp: dlg.Corp, ProductLine: dlg.ProductLine, Line: dlg.Line);
        try
        {
            using var client = new JobServerClient(new Uri(url));
            var receipt = Run(ct => client.SubmitAsync(zip, job, ct), UploadTimeout);
            try { SendChoice.SaveJob(dir, receipt.Id); }
            catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { PluginLog.Write("ash.job.txt", ex); } // only AGVCAUSE's default
            Say(ed, results ? $"결과 올림: {receipt.Id}" : $"전송 완료: 작업 {receipt.Id} (앞에 {receipt.Ahead}건)" + (receipt.Version > 0 ? $" · v{receipt.Version}" : ""));
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

    /// <summary>False (after saying so) when the model package for the old path is not there.</summary>
    private static bool ModelReady(Editor ed, string dir, string model)
    {
        var missing = JobPackage.Missing(dir, model);
        if (missing.Count == 0) return true;
        ed.WriteMessage("\n보낼 파일이 없습니다: " + string.Join(", ", missing));
        Say(ed, "먼저 모델 생성(AGVMODEL)을 실행하세요.");
        return false;
    }

    /// <summary>Source send: reads the Excel input and the drawing, runs the model build's input checks (<see cref="ModelBuilder.Validate"/>), marks the Excel cells and writes
    /// layout.tsv beside the snapshot. False (nothing to send, said why) on an input error, a declined drawing error or a missing/unreadable Excel.</summary>
    private static bool PrepareSource(Editor ed, ProjectSettings s, string dir)
    {
        var xlsx = Path.Combine(dir, ModelCommands.ExcelFileName);
        if (!File.Exists(xlsx)) { Say(ed, $"{ModelCommands.ExcelFileName}이 없습니다. 먼저 AGVEXCEL로 양식을 만들어 채우세요."); return false; }
        Say(ed, "검증 중…");
        bool ok = false;
        using var excel = ExcelCom.Session(); // read and marks share one hidden Excel when the file is encrypted
        PathCommands.Run(Application.DocumentManager.MdiActiveDocument, "AGVSEND", (db, tr) =>
        {
            PathCommands.FinalStatus = "AGVSEND: 보내지 않음";
            var input = ModelCommands.LoadInput(ed, xlsx, dir);
            if (input == null) return false;
            var layout = DrawingReader.Read(db, tr);
            int errors = CheckCommands.Report(ed, layout).Count(f => ModelCommands.Blocking.Contains(f.Code));
            if (errors > 0 && PathCommands.Keyword(ed, $"\n오류 {errors}건이 있습니다. 계속할까요? [Y/N] <N>", "N", "Y", "N") != "Y") return false;
            var findings = ModelBuilder.Validate(input, layout, SourceBuild.Options(input, s).Scale);
            foreach (var f in findings.Where(f => f.Code != FindingCode.InputWarning)) ed.WriteMessage($"\n[{f.Code}] {f.Message}");
            ModelCommands.MarkExcel(ed, xlsx, findings);
            if (findings.Any(f => f.Code == FindingCode.InputError)) { ed.WriteMessage("\n입력 오류가 있어 보내지 않습니다."); return false; }
            LayoutSnapshot.Write(layout, Path.Combine(dir, LayoutSnapshot.FileName));
            PathCommands.FinalStatus = "AGVSEND: 검증 통과";
            ok = true;
            return false; // nothing in the drawing changed
        }, refreshArrows: false, unitsPerMeter: UnitScale.FromSettings(s));
        return ok;
    }

    /// <summary>The open drawing's on-disk path (null = send without DWG: untitled, refused, failed, over MaxDwgBytes); Abort = user cancelled. DBMOD != 0 asks to save first.</summary>
    private static (bool Abort, string? Path) DwgToSend(Editor ed)
    {
        try
        {
            if (Convert.ToInt32(Application.GetSystemVariable("DWGTITLED")) == 0) { ed.WriteMessage("\n저장한 적 없는 도면이라 DWG 없이 보냅니다."); return (false, null); }
            var name = (string)Application.GetSystemVariable("DWGNAME");
            if (!name.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)) { ed.WriteMessage("\nDXF 도면은 DWG로 동봉하지 않습니다. DWG 없이 보냅니다."); return (false, null); }
            var path = Path.Combine((string)Application.GetSystemVariable("DWGPREFIX"), name);
            var modified = Convert.ToInt32(Application.GetSystemVariable("DBMOD")) != 0;
            if (modified)
            {
                var ans = MessageBox.Show("도면에 저장하지 않은 변경이 있습니다. 저장하고 DWG를 함께 보낼까요?"+Environment.NewLine+"예: 저장 후 보내기 · 아니요: DWG 없이 보내기 · 취소: 보내지 않음", "ASH", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (ans == DialogResult.Cancel) return (true, null);
                if (ans != DialogResult.Yes) return (false, null);
                { var db = Application.DocumentManager.MdiActiveDocument.Database; db.SaveAs(path, db.OriginalFileVersion); }
            }
            if (new FileInfo(path).Length > JobPackage.MaxDwgBytes) { ed.WriteMessage("\n도면(DWG)이 " + JobPackage.MaxDwgBytes / (1024 * 1024) + " MB보다 커서 보내지 않습니다. 모델만 보냅니다."); return (false, null); }
            return (false, path);
        }
        catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ZwSoft.ZwCAD.Runtime.Exception)
        {
            ed.WriteMessage("\n도면(DWG)을 저장하지 못했습니다. DWG 없이 보냅니다: " + ex.Message);
            PluginLog.Write("AGVSEND dwg", ex);
            return (false, null);
        }
    }

    // the command runs in the document context, so the document is already locked
    private static void SaveDxf(Editor ed, string path)
    {
        try
        {
            File.Delete(path); // never send a stale drawing from an earlier send
            Application.DocumentManager.MdiActiveDocument.Database.DxfOut(path, 16, DwgVersion.Current);
            if (new FileInfo(path).Length > JobPackage.MaxDxfBytes)
            {
                File.Delete(path);
                ed.WriteMessage("\n도면(DXF)이 100 MB보다 커서 보내지 않습니다. 모델만 보냅니다.");
            }
        }
        catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ZwSoft.ZwCAD.Runtime.Exception)
        {
            ed.WriteMessage("\n도면(DXF)을 저장하지 못했습니다. 모델만 보냅니다: " + ex.Message);
            PluginLog.Write("AGVSEND dxf", ex);
            try { File.Delete(path); } catch (System.Exception) { /* a partial file must not be sent */ }
        }
    }

    // Task.Run so ZWCAD's UI sync context cannot deadlock the wait; the hard cap is for net48 DNS/WPAD stalls that ignore the token
    internal static T Run<T>(Func<CancellationToken, Task<T>> f, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var task = Task.Run(() => f(cts.Token));
        var first = Task.WhenAny(task, Task.Delay(timeout + HardCapGrace)).GetAwaiter().GetResult();
        if (first != task) throw new TimeoutException();
        return task.GetAwaiter().GetResult();
    }

    internal static bool IsNetwork(System.Exception e)
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
