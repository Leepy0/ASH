using System.Diagnostics;
using System.Runtime.CompilerServices;
using ASH.Plugin.Ui;
using ZwSoft.Windows;
using ZwSoft.ZwCAD.Runtime;
using ZwApp = ZwSoft.ZwCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(ASH.Plugin.PluginEntry))]

namespace ASH.Plugin;

public sealed class PluginEntry : IExtensionApplication
{
    // Initialize only hooks events: on demand-load the ribbon may not exist yet, ItemInitialized retries later.
    public void Initialize()
    {
        try { HookRibbon(); }
        catch (System.Exception ex) { Debug.WriteLine("ASH ribbon: " + ex); } // never throw from Initialize
    }

    public void Terminate()
    {
        try { UnhookRibbon(); }
        catch (System.Exception ex) { Debug.WriteLine("ASH ribbon: " + ex); }
    }

    // NoInlining: a missing ZdWindows.dll then fails inside the try above instead of when Initialize is JIT-compiled
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HookRibbon()
    {
        ComponentManager.ItemInitialized += OnItemInitialized;
        ZwApp.SystemVariableChanged += OnSysVarChanged;
        if (ComponentManager.Ribbon != null) AgvRibbon.EnsureBuilt();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void UnhookRibbon()
    {
        ComponentManager.ItemInitialized -= OnItemInitialized;
        ZwApp.SystemVariableChanged -= OnSysVarChanged;
        AgvRibbon.Remove();
    }

    // fires as ribbon items are created (incl. a recreated ribbon); EnsureBuilt is idempotent
    private static void OnItemInitialized(object sender, RibbonItemEventArgs e)
    {
        try { AgvRibbon.EnsureBuilt(); }
        catch (System.Exception ex) { Debug.WriteLine("ASH ribbon: " + ex); }
    }

    // workspace switch may rebuild the ribbon without an ItemInitialized we can rely on
    private static void OnSysVarChanged(object sender, ZwSoft.ZwCAD.ApplicationServices.SystemVariableChangedEventArgs e)
    {
        if (!string.Equals(e.Name, "WSCURRENT", System.StringComparison.OrdinalIgnoreCase)) return;
        try { AgvRibbon.EnsureBuilt(); }
        catch (System.Exception ex) { Debug.WriteLine("ASH ribbon: " + ex); }
    }
}
