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
    public const string LayerCpPrefix = "AGV_CP_";
    public const string LayerBlock = "AGV_BLOCK";
    public const string LayerStation = "AGV_STATION";
    public const string LayerResultPrefix = "AGV_RESULT_";
    /// <summary>Conveyors: AGV_CONV polylines are real conveyor sections (named by AGVCONV, may carry products); AGV_3D_CONV ones are picture only.</summary>
    public const string LayerConv = "AGV_CONV", Layer3dConv = "AGV_3D_CONV";
    /// <summary>3D picture: ASH_3D_* block inserts live on AGV_3D; walls and fences are lines or polylines on their own layer.</summary>
    public const string Layer3d = "AGV_3D", Layer3dWall = "AGV_3D_WALL", Layer3dFence = "AGV_3D_FENCE";
    /// <summary>Conveyor use-point markers (AGVCONVUSE), cause markers from the server (AGVCAUSE), route highlight (AGVROUTE; temporary, cleared by the command).</summary>
    public const string LayerConvUse = "AGV_CONV_USE", LayerCause = "AGV_CAUSE", LayerRoute = "AGV_ROUTE";
    /// <summary>Dangling path ends and one-way dead ends, redrawn by every path command (temporary).</summary>
    public const string LayerPathGap = "AGV_PATH_GAP";
    /// <summary>3D asset block name for a kind (ASH.Core.Layout.AssetKinds): ASH_3D_ROBOT_L ...</summary>
    public const string Asset3dBlockPrefix = "ASH_3D_";
    public const string CpBlockName = "AGV_CP";
    public const string StationBlockName = "AGV_STATION";
    public const string BlockBlockName = "AGV_BLOCK";
    public const string DirBlockName = "AGV_DIR";
    public const string DirTwoBlockName = "AGV_DIR2";
    /// <summary>Spur: arrow with a stop bar at its head (a dead end the AGV backs out of). Crab: arrow with two cross ticks on its shaft (sideways travel).</summary>
    public const string DirSpurBlockName = "AGV_DIR_SPUR", DirCrabBlockName = "AGV_DIR_CRAB";
    public static readonly string[] DirBlockNames = { DirBlockName, DirTwoBlockName, DirSpurBlockName, DirCrabBlockName };

    /// <summary>The arrow block for a path type.</summary>
    public static string DirBlockFor(bool spur, bool twoWay, bool crab) => twoWay ? DirTwoBlockName : spur ? DirSpurBlockName : crab ? DirCrabBlockName : DirBlockName;
    /// <summary>Length of the arrow block definitions (meters); the inserted size is ArrowSize / this.</summary>
    public const double ArrowDefMeters = 0.6;
    /// <summary>One layer per CP type (AGV_CP_LOAD, ...) so each type has its own colour and can be switched off; unknown types and old drawings use AGV_CP.
    /// A traffic CP at CAP 1 has its own layer (AGV_CP_TRAFFIC_CAP1).</summary>
    public static string LayerCpFor(string? type, int cap = ASH.Core.Traffic.CpTypes.Infinity)
    {
        if (ASH.Core.Traffic.CpTypes.IsSingleTraffic(type, cap)) return ASH.Core.Traffic.CpTypes.SingleTrafficLayer;
        var t = (type ?? "").Trim().ToLowerInvariant();
        return System.Array.IndexOf(ASH.Core.Traffic.CpTypes.All, t) >= 0 ? LayerCpPrefix + t.ToUpperInvariant() : LayerCp;
    }

    public static bool IsCpLayer(string layer) =>
        string.Equals(layer, LayerCp, System.StringComparison.OrdinalIgnoreCase) || layer.StartsWith(LayerCpPrefix, System.StringComparison.OrdinalIgnoreCase);

    public static readonly string[] PathLayers = { LayerPath, LayerPathTwo, LayerPathSpur, LayerPathCrab };

    /// <summary>Layer (= colour) of a path type; the XData holds the exact flags. Spur wins over two-way, which wins over crab.</summary>
    public static string LayerFor(bool spur, bool two, bool crab) => spur ? LayerPathSpur : two ? LayerPathTwo : crab ? LayerPathCrab : LayerPath;

    /// <summary>Path type implied by a layer, for entities without XData.</summary>
    public static (bool Spur, bool Two, bool Crab) KindOfLayer(string layer) =>
        (string.Equals(layer, LayerPathSpur, System.StringComparison.OrdinalIgnoreCase),
         string.Equals(layer, LayerPathTwo, System.StringComparison.OrdinalIgnoreCase),
         string.Equals(layer, LayerPathCrab, System.StringComparison.OrdinalIgnoreCase));
    public static readonly string[] CpAttributes = { "NAME", "TYPE", "GROUP", "CAP" };
    public static readonly string[] BlockAttributes = { "NAME", "CAP" };
}
