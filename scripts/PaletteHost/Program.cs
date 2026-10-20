using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

// Usage: PaletteHost.exe <out.png> [tabIndex]. Constructing the palette touches no ZWCAD type, so no ZWCAD dlls are needed.
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        var f = new Form { ClientSize = new Size(280, 900), FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Location = new Point(-2000, 0), ShowInTaskbar = false };
        var c = new ASH.Plugin.Ui.AgvPaletteControl { Dock = DockStyle.Fill };
        f.Controls.Add(c);
        int tab = args.Length > 1 ? int.Parse(args[1]) : 0;
        f.Shown += (s, e) =>
        {
            foreach (Control x in c.Controls) if (x is TabControl t) t.SelectedIndex = tab;
            Application.DoEvents();
            using var bmp = new Bitmap(c.Width, c.Height);
            c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
            bmp.Save(args[0], ImageFormat.Png);
            f.Close();
        };
        Application.Run(f);
        return 0;
    }
}
