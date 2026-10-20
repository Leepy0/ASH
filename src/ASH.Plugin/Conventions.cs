namespace ASH.Plugin;

/// <summary>Layer, block and attribute names shared by every command (spec §6.1).</summary>
public static class Conventions
{
    public const string AppName = "ASH";
    public const string LayerPath = "AGV_PATH";
    public const string LayerPathSpur = "AGV_PATH_SPUR";
    public const string LayerDir = "AGV_DIR";
    public const string LayerCp = "AGV_CP";
    public const string LayerBlock = "AGV_BLOCK";
    public const string LayerStation = "AGV_STATION";
    public const string LayerResultPrefix = "AGV_RESULT_";
    public const string CpBlockName = "AGV_CP";
    public const string StationBlockName = "AGV_STATION";
    public const string BlockBlockName = "AGV_BLOCK";
    public const string DirBlockName = "AGV_DIR";
    public static readonly string[] CpAttributes = { "NAME", "TYPE", "GROUP", "STATION", "CAP" };
    public static readonly string[] BlockAttributes = { "NAME", "CAP" };
}
