using System;
using System.Drawing.Imaging;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZwSoft.Windows;

namespace ASH.Plugin.Ui;

/// <summary>"ASH" ribbon tab with the palette's commands. Idempotent: safe to call on every ComponentManager.ItemInitialized.</summary>
public static class AgvRibbon
{
    public const string TabId = "ASH_TAB";

    /// <summary>Adds the tab unless the ribbon is missing (not created yet) or already has it.</summary>
    public static void EnsureBuilt()
    {
        var ribbon = ComponentManager.Ribbon;
        if (ribbon == null || Find(ribbon) != null) return;
        var tab = new RibbonTab { Title = "ASH", Id = TabId };
        tab.Panels.Add(Panel("레이아웃", ("양식 초기화", GlyphKind.Init, "AGVINIT"), ("팔레트", GlyphKind.Block, "AGVPALETTE"),
            ("업데이트", GlyphKind.Update, "AGVUPDATE"), ("서버 설정", GlyphKind.Settings, "AGVSERVER")));
        tab.Panels.Add(Panel("경로", ("경로 그리기", GlyphKind.Path, "AGVPATH"),
            ("방향 반전", GlyphKind.Reverse, "AGVREVERSE"), ("경로 종류", GlyphKind.PathType, "AGVPTYPE"), ("평행 복제", GlyphKind.Offset, "AGVOFFSET"), ("배열 복제", GlyphKind.Array, "AGVARRAY"),
            ("교차 분할", GlyphKind.Split, "AGVSPLIT"), ("경로 잇기", GlyphKind.Join, "AGVJOIN"), ("필렛", GlyphKind.Fillet, "AGVFILLET"),
            ("방향표 크기", GlyphKind.ArrowSize, "AGVARROW")));
        tab.Panels.Add(Panel("CP", ("CP 배치", GlyphKind.Cp, "AGVCP")));
        tab.Panels.Add(Panel("교통제어", ("교통제어 자동", GlyphKind.Traffic, "AGVTRAFFIC"), ("block 자동", GlyphKind.Block, "AGVBLOCKAUTO")));
        tab.Panels.Add(Panel("검증·내보내기", ("검증", GlyphKind.Check, "AGVCHECK"), ("내보내기", GlyphKind.Export, "AGVEXPORT"), ("Excel 양식", GlyphKind.Excel, "AGVEXCEL"), ("모델 생성·빌드", GlyphKind.Model, "AGVMODEL"), ("서버로 보내기", GlyphKind.Send, "AGVSEND"), ("결과 올리기", GlyphKind.Send, "AGVRESULTUP")));
        ribbon.Tabs.Add(tab);
    }

    public static void Remove()
    {
        var ribbon = ComponentManager.Ribbon;
        var tab = ribbon == null ? null : Find(ribbon);
        if (tab != null) ribbon!.Tabs.Remove(tab);
    }

    /// <summary>Builds if needed and brings the tab to front. False when there is no ribbon yet.</summary>
    public static bool Activate()
    {
        EnsureBuilt();
        var ribbon = ComponentManager.Ribbon;
        var tab = ribbon == null ? null : Find(ribbon);
        if (tab == null) return false;
        tab.IsActive = true;
        return true;
    }

    private static RibbonTab? Find(RibbonControl ribbon)
    {
        foreach (var t in ribbon.Tabs) if (t.Id == TabId) return t;
        return null;
    }

    // first item = large vertical button, the rest = standard buttons in rows of 3
    private static RibbonPanel Panel(string title, params (string Text, GlyphKind Glyph, string Cmd)[] items)
    {
        var src = new RibbonPanelSource { Title = title };
        src.Items.Add(Button(items[0], true));
        if (items.Length > 1)
        {
            var row = new RibbonRowPanel();
            for (int i = 1; i < items.Length; i++)
            {
                if (i > 1 && (i - 1) % 3 == 0) row.Items.Add(new RibbonRowBreak());
                row.Items.Add(Button(items[i], false));
            }
            src.Items.Add(row);
        }
        return new RibbonPanel { Source = src };
    }

    private static RibbonButton Button((string Text, GlyphKind Glyph, string Cmd) it, bool large) => new RibbonButton
    {
        Text = it.Text, ShowText = true, ShowImage = true, ToolTip = $"{it.Text} ({it.Cmd})",
        Size = large ? RibbonItemSize.Large : RibbonItemSize.Standard,
        Orientation = large ? Orientation.Vertical : Orientation.Horizontal,
        Image = Icon(it.Glyph, 16), LargeImage = Icon(it.Glyph, 32),
        CommandParameter = it.Cmd, CommandHandler = RibbonCmd.Instance,
    };

    // Copies the ARGB pixels directly: GetHbitmap + CreateBitmapSourceFromHBitmap drops the alpha channel (black icon background).
    private static ImageSource Icon(GlyphKind kind, int px)
    {
        using var bmp = Glyphs.Draw(kind, px, Theme.Accent);
        var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, px, px), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var src = BitmapSource.Create(px, px, 96, 96, PixelFormats.Bgra32, null, data.Scan0, data.Stride * px, data.Stride);
            src.Freeze();
            return src;
        }
        finally { bmp.UnlockBits(data); }
    }

    private sealed class RibbonCmd : ICommand
    {
        public static readonly RibbonCmd Instance = new RibbonCmd();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        // The ribbon passes the clicked button (RibbonCommandItem) to Execute, not its CommandParameter; a plain string is accepted too.
        public void Execute(object? parameter)
        {
            var cmd = parameter as string ?? (parameter as RibbonCommandItem)?.CommandParameter as string;
            PluginLog.Write("ribbon click: " + (cmd ?? "(no command, parameter " + (parameter?.GetType().FullName ?? "null") + ")"));
            if (!string.IsNullOrEmpty(cmd)) AgvPalette.Send(cmd!); // CMDNAMES guard lives in Send
        }
    }
}
