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
using ASH.Core.Update;

namespace ASH.Installer;

// ASH.Installer.exe [/silent] [/uninstall] [/dir:<ZWCAD folder>]... [/server:<http://host:port>] [/noupdatecheck]
//   (hidden: /capture:<png> renders the form without detection and exits). The window checks github.com for a newer Setup unless /noupdatecheck; /silent never does.
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
        var bad = dirs.Where(d => Manual(d) == null).Select(d => "/dir: ZWCAD.exe(MFG는 ZWCADM.exe) 없음, 무시: " + d).ToList();
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
        var form = new MainForm(Detect().Concat(manual).Distinct().ToList(), uninstall, server ?? ExistingServerUrl(), args);
        Application.Run(form);
        return form.ExitCode;
    }

    /// <summary>ZWCAD year the embedded plugin targets (AshZwcadVersion in Directory.Build.props).</summary>
    public static readonly string SupportedVersion = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .First(a => a.Key == "ZwcadVersion").Value!;

    /// <summary>Version of this Setup and the plugin it embeds (Version in Directory.Build.props, possibly +build).</summary>
    public static readonly string Version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    /// <summary>The embedded update-key.pub (same as the plugin's), if the build had one; a key that cannot be read is an error, never unsigned mode.</summary>
    public static (ManifestSignature? Key, string? Error) LoadKey()
    {
        using var s = typeof(Program).Assembly.GetManifestResourceStream("update-key.pub");
        return UpdateFlow.LoadKey(s);
    }

    public static List<ZwcadInstall> Detect() => ZwcadLocator.Find(new Win32Registry(), new RealFileSystem(),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

    // best effort; reads the same install-target user's file that SaveServerUrl writes
    static string ExistingServerUrl()
    {
        try { using var r = new StepRunner(_ => { }); return r.ExistingServerUrl(); }
        catch (Exception) { return ""; }
    }

    /// <summary>A folder holding ZWCAD.exe; version from the folder name ("ZWCAD 2026" → 2026), "" when absent.</summary>
    /// <summary>
    /// Starts the first of the installed ZWCADs (ZWCAD.exe, or ZWCADM.exe for MFG). Done through explorer.exe so it runs with the user's normal rights:
    /// started from this installer (an administrator process) ZWCAD itself would run elevated. False when nothing could be started.
    /// </summary>
    public static bool LaunchZwcad(IEnumerable<ZwcadInstall> targets)
    {
        foreach (var t in targets)
        {
            var exe = new[] { "ZWCAD.exe", "ZWCADM.exe" }.Select(n => Path.Combine(t.InstallDir, n)).FirstOrDefault(File.Exists);
            if (exe == null) continue;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + exe + "\"") { UseShellExecute = false });
                return true;
            }
            catch (Exception) { /* try the next one */ }
        }
        return false;
    }

    public static ZwcadInstall? Manual(string dir)
    {
        dir = dir.Trim().Trim('"').TrimEnd('\\');
        var zw = File.Exists(Path.Combine(dir, "ZWCAD.exe"));
        if (!zw && !File.Exists(Path.Combine(dir, "ZWCADM.exe"))) return null;
        var m = Regex.Match(Path.GetFileName(dir), @"(?<!\d)(20\d{2})(?!\d)");
        var ver = m.Success ? m.Groups[1].Value : "";
        var root = ZwcadLocator.ProfileRoot(ZwcadLocator.LooksMfg(Path.GetFileName(dir)) || !zw);
        return new ZwcadInstall(ver, "", dir, ver.Length > 0 ? root + "\\" + ver : root);
    }

    // no detection: the capture path must not read or write the registry
    static int Capture(string png)
    {
        var f = new MainForm(new List<ZwcadInstall>(), false, "", new[] { SetupUpdate.SkipCheckArg }) { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, 0), ShowInTaskbar = false };
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
