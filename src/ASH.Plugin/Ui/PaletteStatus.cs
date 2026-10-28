using System;
using System.Collections.Generic;
using ASH.Core.Layout;
using ASH.Core.Traffic;

namespace ASH.Plugin.Ui;

/// <summary>Command results for the palette status line. No-op until a palette exists.</summary>
public static class PaletteStatus
{
    internal static Action<string>? Line;
    internal static Action<int>? Check;

    internal static Action<IReadOnlyList<FindingRow>>? Findings;
    internal static Action<IReadOnlyList<FoundObject>>? Objects;
    internal static Action<IReadOnlyList<CpGroupInfo>>? Groups;

    // "숨긴 그룹 N개" stays in front of every status text while the drawing has hidden CPs (set from the group list after each ASH command)
    private static string _hidden = "";

    // dot on the palette's notice button while unseen notices exist (set by the startup check / AGVNOTICE; a palette built later reads it)
    internal static Action<bool>? NoticeMark;
    internal static bool NoticeUnseen { get; private set; }
    public static void SetNoticeUnseen(bool on) { NoticeUnseen = on; NoticeMark?.Invoke(on); }

    public static void SetFindings(IReadOnlyList<FindingRow> rows) => Findings?.Invoke(rows);
    public static void SetObjects(IReadOnlyList<FoundObject> items) => Objects?.Invoke(items);

    /// <summary>True once a palette listens, so the commands skip the scan of the drawing when nobody shows it.</summary>
    internal static bool WantsGroups => Groups != null;

    public static void SetGroups(IReadOnlyList<CpGroupInfo> groups)
    {
        int n = CpGroupView.HiddenGroups(groups);
        _hidden = n > 0 ? $"숨긴 그룹 {n}개 · " : "";
        Groups?.Invoke(groups);
    }

    public static void Set(string text) => Line?.Invoke(_hidden + text);
    public static void SetCheck(int findings) => Check?.Invoke(findings);
}
