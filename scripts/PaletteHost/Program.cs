using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

// Usage: PaletteHost.exe <out.png> [tabIndex] [width] [--sample] [--settings] [--send [--old]].Constructing the palette touches no ZWCAD type, so no ZWCAD dlls are needed.
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        int tab = args.Length > 1 ? int.Parse(args[1]) : 0;
        int width = args.Length > 2 ? int.Parse(args[2]) : 280;
        if (Array.IndexOf(args, "--settings") >= 0) // the settings dialog instead of the palette (default values, sample server address)
        {
            using var d = new ASH.Plugin.Ui.SettingsDialog(@"D:\project\ash.settings.tsv", new ASH.Core.Settings.ProjectSettings(), "http://server:8082") { StartPosition = FormStartPosition.Manual, Location = new Point(-2000, 0) };
            d.Shown += (s, e) =>
            {
                Application.DoEvents();
                using var bmp = new Bitmap(d.Width, d.Height);
                d.DrawToBitmap(bmp, new Rectangle(0, 0, d.Width, d.Height));
                bmp.Save(args[0], ImageFormat.Png);
                d.Close();
            };
            d.ShowDialog();
            return 0;
        }
        if (Array.IndexOf(args, "--send") >= 0) // the send dialog with a sample server list (--old = a server without /api/analyses)
        {
            static ASH.Core.Runner.AnalysisSummary A(string id, string name, params ASH.Core.Runner.PlanSummary[] plans) => new ASH.Core.Runner.AnalysisSummary { Id = id, Name = name, Plans = new System.Collections.Generic.List<ASH.Core.Runner.PlanSummary>(plans) };
            static ASH.Core.Runner.PlanSummary P(string id, string name, int next) => new ASH.Core.Runner.PlanSummary { Id = id, Name = name, NextVersion = next };
            var list = new[] { A("a1", "공장A 증설", P("p1", "기본안", 1), P("p2", "B안", 3)), A("a2", "공장B", P("p3", "기본안", 2)) };
            var pick = ASH.Core.Analysis.SendChoice.Default(list, "공장A 증설", "B안", null);
            using var d = new ASH.Plugin.Ui.SendDialog("ASH 서버로 보내기", "서버 대기 2건 · 예상 시작 14:20 · 보통 12.5분 걸림", "보내기", Array.IndexOf(args, "--old") >= 0 ? null : list, pick) { StartPosition = FormStartPosition.Manual, Location = new Point(-2000, 0) };
            d.Shown += (s, e) =>
            {
                Application.DoEvents();
                using var bmp = new Bitmap(d.Width, d.Height);
                d.DrawToBitmap(bmp, new Rectangle(0, 0, d.Width, d.Height));
                bmp.Save(args[0], ImageFormat.Png);
                d.Close();
            };
            d.ShowDialog();
            return 0;
        }
        if (Array.IndexOf(args, "--lite") >= 0) // the AGVLITE dialog with made-up layers (about 12 MB checked, so the over-10 MB warning shows)
        {
            using var d = new ASH.Plugin.Ui.LiteDialog(new ASH.Core.Lite.LitePlan(LiteSample()), "공장A_레이아웃.dwg", @"lite\공장A_레이아웃_lite.dxf", 10) { StartPosition = FormStartPosition.Manual, Location = new Point(-2000, 0) };
            d.Shown += (s, e) =>
            {
                Application.DoEvents();
                using var bmp = new Bitmap(d.Width, d.Height);
                d.DrawToBitmap(bmp, new Rectangle(0, 0, d.Width, d.Height));
                bmp.Save(args[0], ImageFormat.Png);
                d.Close();
            };
            d.ShowDialog();
            return 0;
        }
        var f = new Form { ClientSize = new Size(width, 900), FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Location = new Point(-2000, 0), ShowInTaskbar = false };
        var c = new ASH.Plugin.Ui.AgvPaletteControl { Dock = DockStyle.Fill };
        f.Controls.Add(c);
        if (Array.IndexOf(args, "--sample") >= 0) Sample();
        f.Shown += (s, e) =>
        {
            try
            {
                foreach (Control x in c.Controls) if (x is TabControl t) t.SelectedIndex = tab;
                Application.DoEvents();
                using var bmp = new Bitmap(c.Width, c.Height);
                c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
                bmp.Save(args[0], ImageFormat.Png);
                f.Close();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                Environment.Exit(1); // never leave the capture script waiting on an error dialog
            }
        };
        Application.Run(f);
        return 0;
    }

    static ASH.Core.Lite.LiteSource LiteSample()
    {
        var s = new ASH.Core.Lite.LiteSource { InsUnits = 4, Tolerance = 10, XrefRefs = 2, Solids3d = 340 };
        var rnd = new Random(1);
        void Add(string layer, int lines, ASH.Core.Lite.LiteFlags flags = ASH.Core.Lite.LiteFlags.None, bool off = false, bool frozen = false)
        {
            s.Layers[layer] = new ASH.Core.Lite.LiteLayerState(7, off, frozen);
            for (int i = 0; i < lines; i++)
                s.Items.Add(new ASH.Core.Lite.LiteItem(new ASH.Core.Lite.LiteLine(layer, 1200000 + rnd.NextDouble() * 250000, 400000 + rnd.NextDouble() * 120000, 1200000 + rnd.NextDouble() * 250000, 400000 + rnd.NextDouble() * 120000), flags));
        }
        Add("A-WALL", 52000); Add("A-COLUMN", 9000); Add("설비_라인", 38000); Add("통로", 4000); Add("A-GRID", 1500);
        Add("A-DIM", 21000); Add("문자", 6000); Add("HATCH-FLOOR", 30000); Add("표제란", 2500); Add("AGV_PATH", 800);
        Add("3D-EQUIP", 340, ASH.Core.Lite.LiteFlags.Footprint);
        Add("XREF-BASE|A-WALL", 15000, ASH.Core.Lite.LiteFlags.Xref);
        Add("OLD_LAYOUT", 7000, ASH.Core.Lite.LiteFlags.Hidden, frozen: true);
        Add("0", 300);
        ASH.Core.Lite.LiteSource.Count(s.Removed, "해치", 1800);
        ASH.Core.Lite.LiteSource.Count(s.Removed, "문자", 9200);
        return s;
    }

    // sample rows for the check table and the finder, so the tabs can be shown without a drawing
    static void Sample()
    {
        ASH.Plugin.Ui.PaletteStatus.SetCheck(3);
        ASH.Plugin.Ui.PaletteStatus.SetFindings(new[]
        {
            new ASH.Plugin.Ui.FindingRow("끝점이 이어지지 않음", "P003", "Path P003 has a free end", 1200, 800, true),
            new ASH.Plugin.Ui.FindingRow("CP가 경로에서 벗어남", "L002", "CP L002 is off the path network", 5400, 300, true),
            new ASH.Plugin.Ui.FindingRow("CP 이름 중복", "T001", "Duplicate CP name T001", 2000, 2000, true),
        });
        var at = new ASH.Core.Geometry.Point2(0, 0);
        ASH.Plugin.Ui.PaletteStatus.SetObjects(new[]
        {
            new ASH.Core.Layout.FoundObject("경로", "P001", "단방향 · 12.4 m", at, "1A", new[] { "경로 전체", "경로 · 단방향" }),
            new ASH.Core.Layout.FoundObject("경로", "P002", "양방향 · 8 m", at, "1B", new[] { "경로 전체", "경로 · 양방향" }),
            new ASH.Core.Layout.FoundObject("경로", "P003", "지선 · 3.2 m", at, "1C", new[] { "경로 전체", "경로 · 지선" }),
            new ASH.Core.Layout.FoundObject("CP", "T001", "traffic · - · CAP 1", at, "2A", new[] { "CP 전체", "CP · traffic" }),
            new ASH.Core.Layout.FoundObject("CP", "L001", "load · G1 · CAP INF", at, "2B", new[] { "CP 전체", "CP · load" }),
            new ASH.Core.Layout.FoundObject("block", "B001", "CAP 1", at, "3A", new[] { "block" }),
        });
    }
}
