using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ASH.Core.Analysis;
using ASH.Core.Cause;
using ASH.Core.Layout;
using ASH.Core.Runner;
using ASH.Core.Settings;
using ASH.Plugin.Cad;
using ASH.Plugin.Ui;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Runtime;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ASH.Plugin.Commands.CauseCommands))]

namespace ASH.Plugin.Commands;

/// <summary>The server's cause analysis (원인 tab) in the drawing: markers on AGV_CAUSE and a list in the palette.</summary>
public sealed class CauseCommands
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60); // the analysers run on the request
    private const double MarkerM = 0.8, TextM = 0.6, ZoomWidthM = 30;

    /// <summary>The drawing's last sent job (AGVSEND / AGVRESULTUP), else a list from the server. Replaces the earlier markers.</summary>
    [CommandMethod("AGVCAUSE")]
    public void AgvCause() => Show("AGVCAUSE", pick: false);

    /// <summary>Always the list.</summary>
    [CommandMethod("AGVCAUSEJOB")]
    public void AgvCauseJob() => Show("AGVCAUSEJOB", pick: true);

    [CommandMethod("AGVCAUSECLEAR")]
    public void AgvCauseClear()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        PathCommands.Run(doc, "AGVCAUSECLEAR", (db, tr) =>
        {
            int n = EraseMarks(db, tr);
            PathCommands.FinalStatus = n > 0 ? $"AGVCAUSECLEAR: 지움 {n}개" : null;
            return n > 0;
        }, refreshArrows: false);
        CausePanel.Set(new List<CauseRow>(), "");
    }

    /// <summary>Palette double-click: zoom to the marker and select the CPs, paths and blocks the finding names.</summary>
    [CommandMethod("AGVCAUSEGO")]
    public void AgvCauseGo()
    {
        var r = CausePanel.Pending;
        CausePanel.Pending = null;
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (r == null || !r.HasLocation || doc == null) return;
        var ed = doc.Editor;
        PathCommands.Run(doc, "AGVCAUSEGO", (db, tr) =>
        {
            FindCommands.PanTo(ed, r.X, r.Y);
            using (var v = ed.GetCurrentView())
            {
                double aspect = v.Width > 0 ? v.Height / v.Width : 1;
                v.Width = r.Width; v.Height = r.Width * aspect;
                ed.SetCurrentView(v);
            }
            var handles = CauseMarks.RefHandles(ObjectIndex.Build(DrawingReader.Read(db, tr), DrawingUnits.PerMeter), r.Item.Refs);
            var ids = handles.Select(h => DrawingWriter.FromHandle(db, h)).Where(i => !i.IsNull && !i.IsErased).ToArray();
            if (ids.Length > 0) ed.SetImpliedSelection(ids);
            PathCommands.FinalStatus = $"원인 {r.Number}: {r.Item.Title}" + (ids.Length > 0 ? $" · 선택 {ids.Length}개" : "");
            return false;
        }, refreshArrows: false);
    }

    private static void Show(string cmd, bool pick)
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor;
        try
        {
            if (!ExportCommands.TryLoadSettings(ed, out var s, out var dir)) return;
            if (!UserSettings.TryNormalizeUrl(UserSettings.Load().ServerUrl, out var url))
            {
                Say(ed, $"서버 주소가 설정되지 않았습니다. AGVSERVER로 설정하세요(파일 {UserSettings.DefaultPath}).");
                return;
            }
            using var client = new JobServerClient(new Uri(url));
            string? id = pick ? null : SendChoice.LoadJob(dir);
            CauseReply? reply = null;
            if (id != null)
            {
                // a remembered job the server no longer has (or has not finished) falls back to the list
                reply = Fetch(ed, client, id, out var gone, retryable: true);
                if (reply == null && !gone) return;
            }
            if (reply == null)
            {
                IReadOnlyList<AnalysisSummary> analyses;
                try { analyses = ServerCommands.Run(client.GetAnalysesAsync, TimeSpan.FromSeconds(10)); }
                catch (System.Exception e) when (ServerCommands.IsNetwork(e) || e is InvalidDataException) { Fail(ed, "작업 목록을 받지 못했습니다", e); return; }
                using var dlg = new JobPickDialog(analyses);
                if (!dlg.HasJobs) { Say(ed, "끝난 작업이 없습니다."); return; }
                if (Application.ShowModalDialog(dlg) != DialogResult.OK || dlg.JobId.Length == 0) { PaletteStatus.Set(cmd + ": 취소"); return; }
                id = dlg.JobId;
                reply = Fetch(ed, client, id, out _, retryable: false);
                if (reply == null) return;
                try { SendChoice.SaveJob(dir, id); }
                catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { PluginLog.Write("ash.job.txt", ex); }
            }

            var marks = CauseMarks.Plan(reply.Items, s.Origin, s.Scale);
            double perM = UnitScale.FromSettings(s);
            var rows = new List<CauseRow>();
            foreach (var i in reply.Items)
            {
                var m = marks.FirstOrDefault(k => ReferenceEquals(k.Item, i));
                rows.Add(m == null ? new CauseRow(i, 0, 0, 0, 0) : new CauseRow(i, m.Number, m.At.X, m.At.Y, ZoomWidthM * perM));
            }
            PathCommands.Run(doc, cmd, (db, tr) =>
            {
                EraseMarks(db, tr);
                var ms = (BlockTableRecord)tr.GetObject(PathCommands.ModelSpaceId(db, tr), OpenMode.ForWrite);
                foreach (var m in marks) Draw(db, tr, ms, m, perM);
                PathCommands.FinalStatus = $"{cmd}: 원인 {reply.Items.Count}건, 표시 {marks.Count}개";
                return true;
            }, refreshArrows: false, unitsPerMeter: perM);

            var run = reply.Runs.FirstOrDefault(r => r.Id == reply.Run)?.Label ?? "";
            CausePanel.Set(rows, string.Join(" · ", new[] { reply.Job, run, reply.Note }.Where(t => !string.IsNullOrEmpty(t))));
            foreach (var r in rows)
            {
                var i = r.Item;
                var text = string.Join(" · ", new[] { i.Title, i.Detail, i.Fix.Length > 0 ? "조치 " + i.Fix : "" }.Where(t => t.Length > 0));
                ed.WriteMessage($"\n[{CauseMarks.SeverityName(i.Severity)}] {(r.Number > 0 ? "#" + r.Number + " " : "")}{text}");
            }
            if (reply.Items.Count == 0) Say(ed, "원인 없음" + (reply.Note.Length > 0 ? " · " + reply.Note : ""));
        }
        catch (System.Exception ex) when (!(ex is OperationCanceledException))
        {
            Say(ed, cmd + " 오류: " + ex.Message);
            PluginLog.Write(cmd, ex);
        }
    }

    /// <summary>The findings of a job, or null after saying why. <paramref name="gone"/>: the job is missing or unfinished (only when <paramref name="retryable"/>), so the caller shows the list.</summary>
    private static CauseReply? Fetch(Editor ed, JobServerClient client, string id, out bool gone, bool retryable)
    {
        gone = false;
        try
        {
            Say(ed, "원인 받는 중…");
            return ServerCommands.Run(ct => client.GetCausesAsync(id, null, ct), Timeout);
        }
        catch (JobServerException e) when (retryable && (e.Status == 404 || e.Status == 409))
        {
            ed.WriteMessage($"\n기억한 작업({id})을 쓸 수 없습니다: {e.Body}");
            gone = true;
            return null;
        }
        catch (System.Exception e) when (ServerCommands.IsNetwork(e) || e is InvalidDataException) { Fail(ed, "원인을 받지 못했습니다", e); return null; }
    }

    private static int EraseMarks(Database db, Transaction tr)
    {
        int n = 0;
        foreach (var id in DrawingReader.ModelSpace(db, tr, OpenMode.ForRead))
            if (tr.GetObject(id, OpenMode.ForRead) is Entity e && DrawingReader.Eq(e.Layer, Conventions.LayerCause)) { e.UpgradeOpen(); e.Erase(); n++; }
        return n;
    }

    /// <summary>A circle, its number in the middle and the short title beside it; colour by severity (AGVINIT puts it back to the layer's).</summary>
    private static void Draw(Database db, Transaction tr, BlockTableRecord ms, CauseMark m, double perM)
    {
        short color = m.Item.Severity >= 2 ? (short)1 : m.Item.Severity == 1 ? (short)30 : (short)8;
        double r = MarkerM * perM, h = TextM * perM;
        void Add(Entity e) { e.Layer = Conventions.LayerCause; e.ColorIndex = color; ms.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); }
        Add(new Circle(new Point3d(m.At.X, m.At.Y, 0), Vector3d.ZAxis, r));
        var n = new DBText { TextString = m.Number.ToString(), Height = h, HorizontalMode = TextHorizontalMode.TextCenter, VerticalMode = TextVerticalMode.TextVerticalMid, AlignmentPoint = new Point3d(m.At.X, m.At.Y, 0) };
        Add(n);
        n.AdjustAlignment(db);
        Add(new DBText { TextString = CauseMarks.ShortTitle(m.Item.Title), Height = h, Position = new Point3d(m.At.X + r * 1.3, m.At.Y - h / 2, 0) });
    }

    private static void Fail(Editor ed, string what, System.Exception e)
    {
        var why = e is JobServerException j ? $"서버 응답 {j.Status}: {j.Body}"
            : e is OperationCanceledException || e is TimeoutException ? "시간 초과"
            : e is InvalidDataException ? e.Message
            : (e.InnerException ?? e).Message;
        Say(ed, $"{what}: {why}");
        PluginLog.Write("AGVCAUSE", e);
    }

    private static void Say(Editor ed, string text)
    {
        ed.WriteMessage("\n" + text);
        PaletteStatus.Set(text);
    }
}
