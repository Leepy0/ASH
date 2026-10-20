namespace ASH.Core.Install;

/// <summary>Language is "" when found only by folder/uninstall entry.</summary>
public sealed record ZwcadInstall(string Version, string Language, string InstallDir, string ProfileKeyPath);
