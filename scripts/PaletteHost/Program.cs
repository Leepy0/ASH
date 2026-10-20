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
}
