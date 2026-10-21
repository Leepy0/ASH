using System;
using ASH.Core.Layout;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Traffic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ASH.Plugin.Ui;

/// <summary>Side palette: header, four tabs of command buttons (CP tab holds the CP placement settings), status line.
/// Every control is created once here; switching tabs only changes visibility. No ZWCAD type is touched until a click.</summary>
public sealed class AgvPaletteControl : UserControl
{
    public AgvPaletteControl()
    {
        DoubleBuffered = true;
        BackColor = Theme.Bg;
        Font = Theme.Base;
        ForeColor = Theme.Text;
        MinimumSize = new Size(260, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed, Appearance = TabAppearance.Normal, Padding = new Point(2, 6), Font = Theme.Base, Multiline = true };
        tabs.DrawItem += DrawTab;

        tabs.TabPages.Add(Page("경로", Theme.Section("경로 그리기·편집"),
            Theme.Make("경로 그리기", GlyphKind.Path, "AGVPATH"), Theme.Make("방향 반전", GlyphKind.Reverse, "AGVREVERSE"), Theme.Make("경로 종류 변경", GlyphKind.PathType, "AGVPTYPE"),
            Theme.Make("평행 복제", GlyphKind.Offset, "AGVOFFSET"), Theme.Make("배열 복제", GlyphKind.Array, "AGVARRAY"),
            Theme.Section("연결"),
            Theme.Make("교차 분할", GlyphKind.Split, "AGVSPLIT"), Theme.Make("경로 잇기", GlyphKind.Join, "AGVJOIN"), Theme.Make("필렛", GlyphKind.Fillet, "AGVFILLET"),
            Theme.Section("방향표"), Theme.Make("방향표 크기·갱신", GlyphKind.ArrowSize, "AGVARROW")));

        var s = CpSettings.Current;
        var detailed = new CheckBox { Text = "상세 모드", Checked = s.DetailedMode, AutoSize = true, Margin = new Padding(2, 4, 0, 4) };
        detailed.CheckedChanged += (o, e) => s.DetailedMode = detailed.Checked;
        var type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 22 };
        type.Items.AddRange(CpTypes.All);
        type.SelectedItem = s.Type;
        type.DrawItem += DrawCpType;
        var prefix = Box(s.Prefix);
        var hint = new Label { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, Margin = new Padding(2, 2, 0, 0) };
        var group = Field(s.Group, v => s.Group = v);
        var cap = Field(CpTypes.FormatCap(s.Cap), v => { if (CpTypes.TryParseCap(v, out var c)) s.Cap = c; });
        void ShowType()
        {
            hint.Text = $"이름 {AutoNamer.PatternFor(s.Type)} · CAP {CpTypes.FormatCap(s.Cap)} · 레이어 {Conventions.LayerCpFor(s.Type)}";
            hint.ForeColor = Theme.CpColor(s.Type);
        }
        // SelectedIndexChanged, not TextChanged: Text can still hold the previous item when TextChanged fires on a drop-down list
        type.SelectedIndexChanged += (o, e) =>
        {
            if (!(type.SelectedItem is string t)) return;
            s.Type = t; // also resets Prefix and Cap to the type's defaults
            prefix.Text = s.Prefix;
            cap.Text = CpTypes.FormatCap(s.Cap);
            ShowType();
        };
        prefix.TextChanged += (o, e) => { s.Prefix = prefix.Text.Trim(); ShowType(); };
        cap.Leave += (o, e) => { cap.Text = CpTypes.FormatCap(s.Cap); ShowType(); }; // show the value AGVCP will actually use
        ShowType();
        tabs.TabPages.Add(Page("CP", Theme.Section("CP 속성"), detailed,
            Theme.Caption("TYPE  (색 · 이름 규칙)"), type, hint,
            Theme.Caption("GROUP"), group,
            Theme.Caption("접두어"), prefix, Theme.Caption("CAP  (숫자 또는 INF = 무제한)"), cap,
            Theme.Section("배치"), Theme.Make("CP 배치", GlyphKind.Cp, "AGVCP"),
            Theme.Caption("traffic CP는 교통제어 탭에서 block과 함께 자동 생성")));

        tabs.TabPages.Add(Page("교통제어", Theme.Section("자동 생성"),
            Theme.Make("block 자동 생성", GlyphKind.Block, "AGVBLOCKAUTO"), Theme.Make("교통제어 자동 생성", GlyphKind.Traffic, "AGVTRAFFIC")));

        var settings = Theme.Make("Settings 열기", GlyphKind.Settings, "");
        settings.Click += (o, e) => Commands.ExportCommands.OpenSettings();

        var checkResult = Theme.Caption("마지막 검사 결과: -");
        var problems = Grid(("문제", 120), ("대상", 64), ("내용", 160));
        problems.Height = 190;
        problems.DoubleClick += (o, e) =>
        {
            if (problems.SelectedItems.Count > 0 && problems.SelectedItems[0].Tag is FindingRow r && r.HasLocation) Navigator.Go(r.X, r.Y, null);
        };
        PaletteStatus.Findings = rows =>
        {
            problems.BeginUpdate();
            problems.Items.Clear();
            foreach (var r in rows)
                problems.Items.Add(new ListViewItem(new[] { r.Kind, r.Target, r.Message }) { Tag = r, ForeColor = Theme.CpColor("traffic"), ToolTipText = r.Message });
            problems.EndUpdate();
        };
        tabs.TabPages.Add(Page("검증·내보내기", Theme.Section("검증"), Theme.Make("검증", GlyphKind.Check, "AGVCHECK"), checkResult,
            Theme.Caption("문제 목록 — 더블클릭하면 그 위치로 이동"), problems,
            Theme.Section("내보내기"), Theme.Make("내보내기", GlyphKind.Export, "AGVEXPORT"),
            Theme.Section("모델"), Theme.Make("Excel 양식", GlyphKind.Excel, "AGVEXCEL"), Theme.Make("모델 생성·빌드", GlyphKind.Model, "AGVMODEL"), Theme.Make("서버로 보내기", GlyphKind.Send, "AGVSEND"), Theme.Make("결과 올리기", GlyphKind.Send, "AGVRESULTUP"),
            Theme.Section("설정"), settings));

        var finder = FinderPage(tabs);
        tabs.TabPages.Add(finder);

        var header = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Theme.Panel, Padding = new Padding(10, 6, 6, 6) };
        var init = Theme.Make("양식 초기화", GlyphKind.Init, "AGVINIT");
        init.Dock = DockStyle.Right;
        init.AutoSize = true;
        var title = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = Padding.Empty };
        title.Controls.Add(new Label { Text = "ASH", Font = new Font("Segoe UI", 11f, FontStyle.Bold), ForeColor = Theme.Text, AutoSize = true, Margin = Padding.Empty });
        title.Controls.Add(new Label { Text = "AGV Simul Helper", Font = new Font("Segoe UI", 8.25f), ForeColor = Theme.Muted, AutoSize = true, Margin = Padding.Empty });
        header.Controls.Add(title); // Fill first, then the right-docked button
        header.Controls.Add(init);
        // icon-only so the header still fits the 260 px minimum width
        var update = Theme.Make("업데이트", GlyphKind.Update, "AGVUPDATE");
        update.Text = "";
        update.TextAlign = ContentAlignment.MiddleCenter;
        update.ImageAlign = ContentAlignment.MiddleCenter;
        update.Padding = Padding.Empty;
        update.Dock = DockStyle.Right;
        update.Width = 36;
        new ToolTip().SetToolTip(update, "업데이트 확인 (AGVUPDATE)");
        header.Controls.Add(update);

        var status = new Label { Dock = DockStyle.Bottom, Height = 24, ForeColor = Theme.Muted, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 4, 0), Text = "준비" };
        PaletteStatus.Line = t => status.Text = t;
        PaletteStatus.Check = n => checkResult.Text = n == 0 ? "마지막 검사 결과: 문제 없음" : $"마지막 검사 결과: {n}건";

        Controls.Add(tabs); // Fill first: later Top/Bottom controls dock before it
        Controls.Add(header);
        Controls.Add(status);
    }

    // A table with fixed columns; the last one takes the rest of the width.
    private static ListView Grid(params (string Title, int Width)[] columns)
    {
        var lv = new ListView
        {
            View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BorderStyle = BorderStyle.FixedSingle, Font = Theme.Base, BackColor = Color.White, Margin = new Padding(0, 2, 0, 4), ShowItemToolTips = true,
        };
        foreach (var (t, w) in columns) lv.Columns.Add(t, w);
        lv.Resize += (o, e) =>
        {
            int others = 0;
            for (int i = 0; i + 1 < lv.Columns.Count; i++) others += lv.Columns[i].Width;
            lv.Columns[lv.Columns.Count - 1].Width = Math.Max(60, lv.ClientSize.Width - others);
        };
        return lv;
    }

    // Finder: pick a category (path type, CP type, block ...), narrow with text, double-click a row to centre the drawing on it (zoom unchanged).
    private static TabPage FinderPage(TabControl tabs)
    {
        var all = new List<FoundObject>();
        var loaded = false;
        var category = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
        category.Items.AddRange(ObjectIndex.Categories);
        category.SelectedIndex = 0;
        var text = Box("");
        var count = new Label { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, Margin = new Padding(2, 2, 0, 4), Text = "목록 새로고침을 누르세요" };
        var list = Grid(("종류", 54), ("이름", 84), ("정보", 140));
        list.Dock = DockStyle.Fill;

        void Apply()
        {
            list.BeginUpdate();
            list.Items.Clear();
            var cat = category.SelectedItem as string ?? ObjectIndex.All;
            foreach (var o in all.Where(o => ObjectIndex.Matches(o, cat, text.Text)))
                list.Items.Add(new ListViewItem(new[] { o.Kind, o.Name, o.Info }) { Tag = o, ToolTipText = o.Name + "  " + o.Info });
            list.EndUpdate();
            count.Text = loaded ? $"{list.Items.Count}개 표시 / 전체 {all.Count}개 — 더블클릭하면 그 위치로 이동" : "목록 새로고침을 누르세요";
        }
        category.SelectedIndexChanged += (o, e) => Apply();
        text.TextChanged += (o, e) => Apply();
        list.DoubleClick += (o, e) =>
        {
            if (list.SelectedItems.Count > 0 && list.SelectedItems[0].Tag is FoundObject f) Navigator.Go(f.At.X, f.At.Y, f.Handle);
        };
        PaletteStatus.Objects = items => { all = items.ToList(); loaded = true; Apply(); };

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Panel, Padding = Padding.Empty };
        top.Controls.AddRange(new Control[] { Theme.Caption("범주"), category, Theme.Caption("이름·정보로 거르기"), text, Theme.Make("목록 새로고침", GlyphKind.Search, "AGVFIND"), count });
        top.Layout += (o, e) =>
        {
            int w = top.ClientSize.Width;
            foreach (Control c in top.Controls) if (!(c is Label)) c.Width = Math.Max(120, w - c.Margin.Horizontal);
        };
        var page = new TabPage("찾기") { BackColor = Theme.Panel, Padding = new Padding(8, 4, 8, 8) };
        page.Controls.Add(list); // Fill first: the Top panel then docks before it
        page.Controls.Add(top);
        // the first visit fills the list by itself
        tabs.SelectedIndexChanged += (o, e) => { if (tabs.SelectedTab == page && !loaded) AgvPalette.Send("AGVFIND"); };
        return page;
    }

    // a colour swatch, the type, and its name pattern on the right
    private static void DrawCpType(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var t = (string)((ComboBox)sender).Items[e.Index];
        e.DrawBackground();
        using (var b = new SolidBrush(Theme.CpColor(t))) e.Graphics.FillRectangle(b, e.Bounds.Left + 5, e.Bounds.Top + 5, 12, 12);
        TextRenderer.DrawText(e.Graphics, t, Theme.Base, new Point(e.Bounds.Left + 24, e.Bounds.Top + 2), e.ForeColor);
        var pattern = AutoNamer.PatternFor(t);
        var size = TextRenderer.MeasureText(pattern, Theme.Small);
        TextRenderer.DrawText(e.Graphics, pattern, Theme.Small, new Point(e.Bounds.Right - size.Width - 8, e.Bounds.Top + 4), Theme.Muted);
        e.DrawFocusRectangle();
    }

    private static void DrawTab(object sender, DrawItemEventArgs e)
    {
        var tabs = (TabControl)sender;
        var r = tabs.GetTabRect(e.Index);
        bool sel = e.Index == tabs.SelectedIndex;
        using (var bg = new SolidBrush(sel ? Theme.Panel : Theme.Bg)) e.Graphics.FillRectangle(bg, Rectangle.Inflate(r, 2, 2));
        TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, Theme.Base, r, sel ? Theme.Accent : Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        if (sel) using (var a = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(a, r.Left, r.Bottom - 2, r.Width, 3);
    }

    private static TabPage Page(string title, params Control[] items)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8, 4, 8, 8), BackColor = Theme.Panel };
        flow.Controls.AddRange(items);
        // stretch buttons and inputs to the panel width (a TopDown flow does not)
        flow.Layout += (o, e) =>
        {
            int w = flow.ClientSize.Width - flow.Padding.Horizontal;
            foreach (Control c in flow.Controls) if (!(c is Label) && !(c is CheckBox)) c.Width = Math.Max(120, w - c.Margin.Horizontal);
        };
        var page = new TabPage(title) { BackColor = Theme.Panel, Padding = Padding.Empty };
        page.Controls.Add(flow);
        return page;
    }

    private static TextBox Box(string value) => new TextBox { Text = value, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 0, 2) };

    private static TextBox Field(string value, Action<string> set)
    {
        var t = Box(value);
        t.TextChanged += (o, e) => set(t.Text.Trim());
        return t;
    }
}
