using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using ASH.Core.Lite;
using ASH.Plugin.Ui;
using ZwSoft.ZwCAD.ApplicationServices;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;

[assembly: ZwSoft.ZwCAD.Runtime.CommandClass(typeof(ASH.Plugin.Commands.LiteStatCommand))]

namespace ASH.Plugin.Commands;

/// <summary>AGVLITESTAT: temporary measuring command (removed once the AGVLITE density thresholds are set from real drawings; see docs/lite-baseline.md).
/// Reads like AGVLITE (read-only, no dialog, nothing written to the drawing) and writes lite\&lt;name&gt;_lite.stat.tsv. Not on the ribbon or palette.</summary>
public sealed class LiteStatCommand
{
    private const double CellMm = 1000;

    [ZwSoft.ZwCAD.Runtime.CommandMethod("AGVLITESTAT")]
    public void AgvLiteStat()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        try { Run(doc); }
        catch (System.Exception ex)
        {
            doc.Editor.WriteMessage($"\nAGVLITESTAT 오류: {ex.GetType().Name}: {ex.Message}");
            PluginLog.Write("AGVLITESTAT failed", ex);
        }
    }

    private static void Run(Document doc)
    {
        var ed = doc.Editor;
        if (Convert.ToInt32(Application.GetSystemVariable("DWGTITLED")) == 0) { ed.WriteMessage("\nAGVLITESTAT: 도면을 먼저 저장하세요."); return; }
        var dir = (string)Application.GetSystemVariable("DWGPREFIX");
        var dwg = Path.Combine(dir, (string)Application.GetSystemVariable("DWGNAME"));
        var outPath = Path.Combine(dir, "lite", Path.GetFileNameWithoutExtension(dwg) + "_lite.stat.tsv");
        int insUnits = Convert.ToInt32(Application.GetSystemVariable("INSUNITS"));

        var sw = Stopwatch.StartNew();
        var log = new LiteReadLog();
        var blocks = LiteCommands.Read(doc, insUnits, log, t => ed.WriteMessage("\n" + t));
        if (blocks == null) { ed.WriteMessage("\nAGVLITESTAT 취소"); return; }
        double readSec = sw.Elapsed.TotalSeconds;

        sw.Restart();
        var build = new LiteBuild(blocks);
        var h = build.Histogram();
        double histSec = sw.Elapsed.TotalSeconds;

        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
        using (var w = new StreamWriter(outPath, false, new UTF8Encoding(true))) // BOM: Excel opens the Korean text right
            LiteStat.Write(w, Path.GetFileName(dwg), readSec, histSec, h.Lines, h.Cut, CellMm, h, build.BlockRows(log.Names, log.Solids), log.Before);

        ed.WriteMessage($"\nAGVLITESTAT: 읽기 {readSec:0.0} 초, 히스토그램 {histSec:0.0} 초" + (h.Cut ? " (작업 한도로 일부만 읽음)" : ""));
        ed.WriteMessage($"\n선분 {h.Lines:N0}개, 점유 셀 {h.Cells:N0}개");
        ed.WriteMessage($"\n밀도 mm/m² 50% {h.P50:N0}, 90% {h.P90:N0}, 99% {h.P99:N0}, 최대 {h.Max:N0}");
        ed.WriteMessage("\n파일: " + outPath);
    }
}
