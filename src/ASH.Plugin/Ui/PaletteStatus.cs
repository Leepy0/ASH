using System;

namespace ASH.Plugin.Ui;

/// <summary>Command results for the palette status line. No-op until a palette exists.</summary>
public static class PaletteStatus
{
    internal static Action<string>? Line;
    internal static Action<int>? Check;

    public static void Set(string text) => Line?.Invoke(text);
    public static void SetCheck(int findings) => Check?.Invoke(findings);
}
