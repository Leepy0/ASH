using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ASH.Core.Install;
using ASH.Core.Settings;

namespace ASH.Installer;

// ASH.Installer.exe [/silent] [/uninstall] [/dir:<ZWCAD folder>]... [/server:<http://host:port>]   (hidden: /capture:<png> renders the form without detection and exits)
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // ASH.Core is embedded, not shipped next to the exe; hook before any Core type is touched
        Assembly? core = null; // cached: a second Assembly.Load(byte[]) would give a distinct, type-incompatible copy
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            if (new AssemblyName(e.Name).Name != "ASH.Core") return null;
            if (core != null) return core;
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("ASH.Core.dll");
            if (s == null) return null;
            using var m = new MemoryStream();
            s.CopyTo(m);
            return core = Assembly.Load(m.ToArray());
        };
        return Run(args);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run(string[] args)
    {
        bool silent = Has(args, "/silent"), uninstall = Has(args, "/uninstall");
        var capture = Arg(args, "/capture:").FirstOrDefault();
        var dirs = Arg(args, "/dir:").ToList();
        var manual = dirs.Select(Manual).Where(m => m != null).Select(m => m!).ToList();
        var bad = dirs.Where(d => Manual(d) == null).Select(d => "/dir: ZWCAD.exe 없음, 무시: " + d).ToList();
        string? server = null, badServer = null;
        var serverArg = Arg(args, "/server:").FirstOrDefault();
        if (serverArg != null)
        {
            if (UserSettings.TryNormalizeUrl(serverArg.Trim('"'), out var s)) server = s;
            else badServer = "/server: http:// 또는 https:// 주소가 아님, 무시: " + serverArg;
        }

        if (silent)
        {
            using var r = new StepRunner(_ => { }, silent: true);
            if (StepRunner.ZwcadRunning()) { r.Log(StepRunner.CloseZwcad); return 1; }
            bad.ForEach(r.Log);
            var targets = Detect().Concat(manual).Distinct().ToList();
            if (targets.Count == 0) { r.Log("ZWCAD 설치를 찾지 못했습니다."); return 1; }
            if (badServer != null) r.Warn(badServer);
            try
            {
                if (uninstall) r.Uninstall(targets);
                else { r.Install(targets); if (server != null && r.ExitCode != 1) r.SaveServerUrl(server); }
            }
            catch (Exception ex) { r.Log("오류: " + ex.Message); return 1; }
            return r.ExitCode; // 0 ok, 1 failure, 2 warnings (autoload not registered)
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (capture != null) return Capture(capture);
        if (badServer != null) bad.Add(badServer);
        if (bad.Count > 0) MessageBox.Show(string.Join(Environment.NewLine, bad), "ASH 설치");
        var form = new MainForm(Detect().Concat(manual).Distinct().ToList(), uninstall, server ?? ExistingServerUrl());
        Application.Run(form);
        return form.ExitCode;
    }

    /// <summary>ZWCAD year the embedded plugin targets (AshZwcadVersion in Directory.Build.props).</summary>
    public static readonly string SupportedVersion = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .First(a => a.Key == "ZwcadVersion").Value!;

    public static List<ZwcadInstall> Detect() => ZwcadLocator.Find(new Win32Registry(), new RealFileSystem(),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

    // best effort; reads the same install-target user's file that SaveServerUrl writes
    static string ExistingServerUrl()
    {
        try { using var r = new StepRunner(_ => { }); return r.ExistingServerUrl(); }
        catch (Exception) { return ""; }
    }

    /// <summary>A folder holding ZWCAD.exe; version from the folder name ("ZWCAD 2026" → 2026), "" when absent.</summary>
    public static ZwcadInstall? Manual(string dir)
    {
        dir = dir.Trim().Trim('"').TrimEnd('\\');
        if (!File.Exists(Path.Combine(dir, "ZWCAD.exe"))) return null;
        var m = Regex.Match(Path.GetFileName(dir), @"(?<!\d)(20\d{2})(?!\d)");
        var ver = m.Success ? m.Groups[1].Value : "";
        return new ZwcadInstall(ver, "", dir, ver.Length > 0 ? @"Software\ZWSoft\ZWCAD\" + ver : @"Software\ZWSoft\ZWCAD");
    }

    // no detection: the capture path must not read or write the registry
    static int Capture(string png)
    {
        var f = new MainForm(new List<ZwcadInstall>(), false, "") { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, 0), ShowInTaskbar = false };
        int code = 0;
        f.Shown += (_, _) =>
        {
            try
            {
                Application.DoEvents();
                using var bmp = new Bitmap(f.Width, f.Height);
                f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(png, ImageFormat.Png);
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); code = 1; }
            f.Close();
        };
        Application.Run(f);
        return code;
    }

    static bool Has(string[] args, string flag) => args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
    static IEnumerable<string> Arg(string[] args, string prefix) =>
        args.Where(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(a => a.Substring(prefix.Length));
}
