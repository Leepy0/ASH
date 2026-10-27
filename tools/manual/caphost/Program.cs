using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using ASH.Core.Runner;
using ASH.Core.Traffic;
using ASH.Plugin.Ui;

// caphost <outDir> <analyses.json> <causes.json> : palette tabs filled with the follow-along example's data + the dialogs, as PNG
static class Program
{
    static string Out = "";

    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Out = args[0];
        Directory.CreateDirectory(Out);
        var analyses = JobServerClient.ParseAnalyses(File.ReadAllText(args[1]));
        var causes = JobServerClient.ParseCauses(File.ReadAllText(args[2]));
        try
        {
            var heights = new[] { 830, 1500, 330, 820, 960, 620, 520 };
            var names = new[] { "palette-path", "palette-cp", "palette-traffic", "palette-3d", "palette-check", "palette-find", "palette-cause" };
            for (int t = 0; t < names.Length; t++) Palette(names[t], t, heights[t], analyses, causes);
            Dialog("dlg-settings", new SettingsDialog(@"D:\ASH예제\ash.settings.tsv", new ASH.Core.Settings.ProjectSettings(), "http://ash-server:8082"));
            var pick = ASH.Core.Analysis.SendChoice.Default(analyses, "ASH예제", "example", null);
            Dialog("dlg-send", new SendDialog("ASH 서버로 보내기", "서버 대기 0건", "보내기", analyses, pick));
            Dialog("dlg-cpedit", new CpEditDialog(2, "G1", null, "load"));
            Dialog("dlg-layercolor", new LayerColorDialog(new Dictionary<string, short>()));
            var jp = typeof(SendDialog).Assembly.GetType("ASH.Plugin.Ui.JobPickDialog")!;
            Dialog("dlg-jobpick", (Form)Activator.CreateInstance(jp, new object[] { analyses }));
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(Out, "caphost-error.txt"), ex.ToString()); return 1; }
        return 0;
    }

    static void Dialog(string name, Form d)
    {
        d.StartPosition = FormStartPosition.Manual; d.Location = new Point(-3000, 0);
        d.Shown += (s, e) =>
        {
            Application.DoEvents();
            using var bmp = new Bitmap(d.Width, d.Height);
            d.DrawToBitmap(bmp, new Rectangle(0, 0, d.Width, d.Height));
            bmp.Save(Path.Combine(Out, name + ".png"), ImageFormat.Png);
            d.Close();
        };
        d.ShowDialog();
        d.Dispose();
    }

    static void Palette(string name, int tab, int height, IReadOnlyList<AnalysisSummary> analyses, CauseReply causes)
    {
        var f = new Form { ClientSize = new Size(300, height), FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Location = new Point(-3000, 0), ShowInTaskbar = false };
        var c = new AgvPaletteControl { Dock = DockStyle.Fill };
        f.Controls.Add(c);
        Fill(causes);
        if (tab != 6) PaletteStatus.Set("준비");
        f.Shown += (s, e) =>
        {
            foreach (Control x in c.Controls) if (x is TabControl t) t.SelectedIndex = tab;
            Application.DoEvents();
            using var bmp = new Bitmap(c.Width, c.Height);
            c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
            bmp.Save(Path.Combine(Out, name + ".png"), ImageFormat.Png);
            f.Close();
        };
        Application.Run(f);
    }

    static void Invoke(Type t, string field, object arg)
    {
        var d = (Delegate?)t.GetField(field, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(null);
        d?.DynamicInvoke(arg);
    }

    // the example's drawing as the palette would list it after AGVTRAFFIC (names, kinds, group); positions are not shown in the lists
    static void Fill(CauseReply causes)
    {
        var at = new Point2(0, 0);
        var objs = new List<FoundObject>();
        foreach (var (n, len) in new[] { ("P001", 20), ("P002", 20), ("P003", 16), ("P004", 20), ("P005", 20), ("P006", 16), ("P007", 16) })
            objs.Add(new FoundObject("경로", n, $"단방향 · {len} m", at, n, new[] { "경로 전체", "경로 · 단방향" }));
        foreach (var (n, t, g, cap) in new[] { ("S001", "standby", "G1", "INF"), ("F001", "finish", "G1", "INF"), ("L001", "load", "G1", "INF"), ("L002", "load", "G1", "INF"), ("U001", "unload", "G1", "INF"), ("U002", "unload", "G1", "INF"), ("T001", "traffic", "-", "1"), ("T002", "traffic", "-", "1"), ("T003", "traffic", "-", "1") })
            objs.Add(new FoundObject("CP", n, $"{t} · {g} · CAP {cap}", at, n, new[] { "CP 전체", "CP · " + t }));
        foreach (var n in new[] { "B001", "B002" }) objs.Add(new FoundObject("block", n, "CAP 1", at, n, new[] { "block" }));
        PaletteStatus.SetObjects(objs);
        PaletteStatus.SetGroups(new[] { new CpGroupInfo("G1", 6, 0, 0), new CpGroupInfo("", 3, 0, 0) });
        PaletteStatus.SetCheck(0);
        PaletteStatus.SetFindings(new FindingRow[0]);
        PaletteStatus.Set("AGVTRAFFIC 완료");
        var asm = typeof(PaletteStatus).Assembly;
        Invoke(asm.GetType("ASH.Plugin.Ui.RouteRequest")!, "Materials", new List<(string, string)> { ("M001", "G1"), ("M002", "G1") });
        // the server's cause list for the C plan (problem example): number = marker in the drawing
        var rows = causes.Items.Select((i, k) => new CauseRow(i, i.X != null ? k + 1 : 0, (i.X ?? 0) * 1000, (i.Y ?? 0) * 1000, 10000)).ToList();
        var cp = asm.GetType("ASH.Plugin.Ui.CausePanel")!;
        var run = causes.Runs.FirstOrDefault(r => r.Id == causes.Run)?.Label ?? "";
        cp.GetMethod("Set", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, new object[] { rows, string.Join(" · ", new[] { causes.Job, run, causes.Note }.Where(t => !string.IsNullOrEmpty(t))) });
        PaletteStatus.Set($"AGVCAUSE: 원인 {rows.Count}건, 표시 {rows.Count(r => r.Number > 0)}개");
    }
}
