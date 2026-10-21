namespace ASH.Core.Install;

/// <summary>Language is "" when found only by folder/uninstall entry.</summary>
public sealed record ZwcadInstall(string Version, string Language, string InstallDir, string ProfileKeyPath)
{
    /// <summary>ZWCAD MFG keeps its profile under Software\ZWSoft\ZWCADM (ZWCAD itself: ...\ZWCAD).</summary>
    public bool IsMfg => ProfileKeyPath.StartsWith(ZwcadLocator.MfgRoot, System.StringComparison.OrdinalIgnoreCase);

    public string Product => IsMfg ? "ZWCAD MFG" : "ZWCAD";
}
