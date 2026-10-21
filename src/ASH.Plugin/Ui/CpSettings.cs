using ASH.Core.Traffic;

namespace ASH.Plugin.Ui;

/// <summary>CP placement state shared by the palette and AGVCP.</summary>
public sealed class CpSettings
{
    public static CpSettings Current { get; } = new CpSettings();

    private string _type = "traffic";

    /// <summary>Setting Type also resets Prefix and Cap to the type's defaults (traffic: 1, every other type: unlimited).</summary>
    public string Type
    {
        get => _type;
        set { _type = value ?? ""; Prefix = AutoNamer.PrefixFor(_type); Cap = CpTypes.DefaultCap(_type); }
    }

    public string Group { get; set; } = "G1"; // matches the default ProjectSettings.Groups
    public string Station { get; set; } = "";
    public string Prefix { get; set; } = "T";
    public int Cap { get; set; } = CpTypes.DefaultCap("traffic");
    public bool DetailedMode { get; set; }
}
