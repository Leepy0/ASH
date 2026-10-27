namespace ASH.Plugin.Cad;

/// <summary>Drawing units per meter for the running command (UnitScale.FromSettings). Set by PathCommands.Run, read by DrawingReader/DrawingWriter and the commands.</summary>
// shortcut: ambient per-command state, safe because ZWCAD runs commands one at a time on the UI thread; pass it explicitly if commands ever run concurrently.
public static class DrawingUnits
{
    public static double PerMeter { get; internal set; } = 1.0;
    /// <summary>Direction arrow length in meters (ash.settings.tsv ArrowSize), set with PerMeter.</summary>
    public static double ArrowMeters { get; internal set; } = 2.0;
    /// <summary>Path auto-connect distance in meters (ash.settings.tsv JoinGap, 0 = off), set with PerMeter.</summary>
    public static double JoinGapMeters { get; internal set; } = 0.3;
}
