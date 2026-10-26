namespace ASH.Plugin.Ui;

/// <summary>One row of the check table in the palette: only problems are listed.</summary>
public sealed class FindingRow
{
    public FindingRow(string kind, string target, string message, double x, double y, bool hasLocation, bool warning = false)
    {
        Kind = kind; Target = target; Message = message; X = x; Y = y; HasLocation = hasLocation; Warning = warning;
    }

    public string Kind { get; }
    public string Target { get; }
    public string Message { get; }
    public double X { get; }
    public double Y { get; }
    public bool HasLocation { get; }
    /// <summary>A finding of the cause analysis (shown as a warning, not an error).</summary>
    public bool Warning { get; }
}

/// <summary>Where the palette's tables send the drawing: a point to pan to and, when there is one, the object to select.</summary>
internal static class Navigator
{
    public sealed class Target
    {
        public Target(double x, double y, string? handle) { X = x; Y = y; Handle = handle; }
        public double X { get; }
        public double Y { get; }
        public string? Handle { get; }
    }

    /// <summary>Read by AGVGOTO. The palette cannot touch the drawing itself (only a command may), so it leaves the target here and runs the command.</summary>
    internal static Target? Pending;

    public static void Go(double x, double y, string? handle)
    {
        Pending = new Target(x, y, handle);
        AgvPalette.Send("AGVGOTO");
    }
}
