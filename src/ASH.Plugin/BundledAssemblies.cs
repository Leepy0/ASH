using System;
using System.IO;
using System.Reflection;

namespace ASH.Plugin;

/// <summary>
/// ZWCAD loads the plugin into its own process, which has no binding redirects for the plugin's private dependencies. ClosedXML, SixLabors.Fonts and the System.*
/// packages ask for exact versions (System.Buffers 4.0.2.0, System.Numerics.Vectors 4.1.3.0 ...) while the folder holds the newer ones that NuGet picked, so the
/// first use threw FileNotFoundException, e.g. as a TypeInitializationException in SixLabors.Fonts.Tables.TableLoader when AGVEXCEL made a workbook.
/// Test hosts add the redirects themselves, which is why this never showed in tests. Here every dll that sits next to ASH.Plugin.dll is answered with that file,
/// whatever version was asked for.
/// </summary>
internal static class BundledAssemblies
{
    private static bool _hooked;

    public static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        var dir = Path.GetDirectoryName(typeof(BundledAssemblies).Assembly.Location);
        if (string.IsNullOrEmpty(dir)) return;
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            try
            {
                var name = new AssemblyName(args.Name).Name;
                if (string.IsNullOrEmpty(name)) return null;
                var path = Path.Combine(dir, name + ".dll");
                if (!File.Exists(path)) return null;
                PluginLog.Write("assembly " + args.Name + " -> " + path);
                return Assembly.LoadFrom(path);
            }
            catch (Exception ex)
            {
                PluginLog.Write("assembly resolve failed for " + args.Name, ex);
                return null;
            }
        };
    }
}
