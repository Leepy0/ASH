using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

// Usage: PaletteHost.exe <out.png> [tabIndex] [width]. Constructing the palette touches no ZWCAD type, so no ZWCAD dlls are needed.
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        int tab = args.Length > 1 ? int.Parse(args[1]) : 0;
        int width = args.Length > 2 ? int.Parse(args[2]) : 280;
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
