namespace ASH.Plugin.Ui;

/// <summary>Palette 3D tab state for AGV3DLINE: widths in metres (kept per session) and the kind a button hands to the command.</summary>
public static class Line3dSettings
{
    public const string Wall = "wall", Fence = "fence", Conv = "conv", ConvPicture = "convpic";
    public static double WallM = 0.2, ConvM = 0.6;
    /// <summary>Set by a palette button right before it sends AGV3DLINE; the command takes it and clears it.</summary>
    public static string? Pending;

    public static bool TryParseM(string s, out double m) =>
        (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out m) ||
         double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out m)) && m > 0 && m < 100;
}
