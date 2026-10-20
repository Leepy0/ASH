using ASH.Core.Traffic;

namespace ASH.Plugin.Ui;

/// <summary>CP placement state shared by the palette and AGVCP.</summary>
public sealed class CpSettings
{
    public static CpSettings Current { get; } = new CpSettings();

    private string _type = "traffic";

    /// <summary>Setting Type also resets Prefix to the type's default.</summary>
    public string Type
    {
        get => _type;
        set { _type = value ?? ""; Prefix = AutoNamer.PrefixFor(_type); }
    }

    public string Group { get; set; } = "";
    public string Station { get; set; } = "";
    public string Prefix { get; set; } = "T";
    public int Cap { get; set; } = 1;
    public bool DetailedMode { get; set; }
}
