using System;
using System.Collections.Generic;
using ASH.Core.Layout;

namespace ASH.Plugin.Ui;

/// <summary>Command results for the palette status line. No-op until a palette exists.</summary>
public static class PaletteStatus
{
    internal static Action<string>? Line;
    internal static Action<int>? Check;

    internal static Action<IReadOnlyList<FindingRow>>? Findings;
    internal static Action<IReadOnlyList<FoundObject>>? Objects;

    public static void SetFindings(IReadOnlyList<FindingRow> rows) => Findings?.Invoke(rows);
    public static void SetObjects(IReadOnlyList<FoundObject> items) => Objects?.Invoke(items);

    public static void Set(string text) => Line?.Invoke(text);
    public static void SetCheck(int findings) => Check?.Invoke(findings);
}
