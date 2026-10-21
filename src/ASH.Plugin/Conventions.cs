namespace ASH.Plugin;

/// <summary>Layer, block and attribute names shared by every command (spec §6.1).</summary>
public static class Conventions
{
    public const string AppName = "ASH";
    public const string LayerPath = "AGV_PATH";
    public const string LayerPathSpur = "AGV_PATH_SPUR";
    public const string LayerPathTwo = "AGV_PATH_TWO";
    public const string LayerPathCrab = "AGV_PATH_CRAB";
    public const string LayerDir = "AGV_DIR";
    public const string LayerCp = "AGV_CP";
    public const string LayerBlock = "AGV_BLOCK";
    public const string LayerStation = "AGV_STATION";
    public const string LayerResultPrefix = "AGV_RESULT_";
    public const string CpBlockName = "AGV_CP";
    public const string StationBlockName = "AGV_STATION";
    public const string BlockBlockName = "AGV_BLOCK";
    public const string DirBlockName = "AGV_DIR";
    public const string DirTwoBlockName = "AGV_DIR2";
    /// <summary>Length of the arrow block definitions (meters); the inserted size is ArrowSize / this.</summary>
    public const double ArrowDefMeters = 0.6;
    public static readonly string[] PathLayers = { LayerPath, LayerPathTwo, LayerPathSpur, LayerPathCrab };

    /// <summary>Layer (= colour) of a path type; the XData holds the exact flags. Spur wins over two-way, which wins over crab.</summary>
    public static string LayerFor(bool spur, bool two, bool crab) => spur ? LayerPathSpur : two ? LayerPathTwo : crab ? LayerPathCrab : LayerPath;

    /// <summary>Path type implied by a layer, for entities without XData.</summary>
    public static (bool Spur, bool Two, bool Crab) KindOfLayer(string layer) =>
        (string.Equals(layer, LayerPathSpur, System.StringComparison.OrdinalIgnoreCase),
         string.Equals(layer, LayerPathTwo, System.StringComparison.OrdinalIgnoreCase),
         string.Equals(layer, LayerPathCrab, System.StringComparison.OrdinalIgnoreCase));
    public static readonly string[] CpAttributes = { "NAME", "TYPE", "GROUP", "STATION", "CAP" };
    public static readonly string[] BlockAttributes = { "NAME", "CAP" };
}
