using ZwSoft.ZwCAD.Runtime;

[assembly: ExtensionApplication(typeof(ASH.Plugin.PluginEntry))]

namespace ASH.Plugin;

public sealed class PluginEntry : IExtensionApplication
{
    public void Initialize() { }
    public void Terminate() { }
}
