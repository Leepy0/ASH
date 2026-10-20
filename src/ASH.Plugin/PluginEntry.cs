using System.Diagnostics;
using System.Runtime.CompilerServices;
using ASH.Plugin.Ui;
using ZwSoft.Windows;
using ZwSoft.ZwCAD.Runtime;

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
        if (ComponentManager.Ribbon != null) AgvRibbon.EnsureBuilt();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void UnhookRibbon()
    {
        ComponentManager.ItemInitialized -= OnItemInitialized;
        AgvRibbon.Remove();
    }

    // also fires when a workspace switch recreates the ribbon; EnsureBuilt is idempotent
    private static void OnItemInitialized(object sender, RibbonItemEventArgs e)
    {
        try { AgvRibbon.EnsureBuilt(); }
        catch (System.Exception ex) { Debug.WriteLine("ASH ribbon: " + ex); }
    }
}
