using System;
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

        var tabs = new TabControl { Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed, Appearance = TabAppearance.Normal, Padding = new Point(2, 6), Font = Theme.Base };
        tabs.DrawItem += DrawTab;

        tabs.TabPages.Add(Page("경로", Theme.Section("경로 그리기·편집"),
            Theme.Make("경로 그리기", GlyphKind.Path, "AGVPATH"), Theme.Make("방향 반전", GlyphKind.Reverse, "AGVREVERSE"), Theme.Make("경로 종류 변경", GlyphKind.PathType, "AGVPTYPE"),
            Theme.Make("평행 복제", GlyphKind.Offset, "AGVOFFSET"), Theme.Make("배열 복제", GlyphKind.Array, "AGVARRAY"),
            Theme.Section("연결"),
            Theme.Make("교차 분할", GlyphKind.Split, "AGVSPLIT"), Theme.Make("경로 잇기", GlyphKind.Join, "AGVJOIN"), Theme.Make("필렛", GlyphKind.Fillet, "AGVFILLET")));

        var s = CpSettings.Current;
        var detailed = new CheckBox { Text = "상세 모드", Checked = s.DetailedMode, AutoSize = true, Margin = new Padding(2, 4, 0, 4) };
        detailed.CheckedChanged += (o, e) => s.DetailedMode = detailed.Checked;
        var type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
        type.Items.AddRange(CpSettings.CpTypes);
        type.SelectedItem = s.Type;
        var prefix = Box(s.Prefix);
        type.TextChanged += (o, e) => { s.Type = type.Text.Trim(); prefix.Text = s.Prefix; };
        prefix.TextChanged += (o, e) => s.Prefix = prefix.Text.Trim();
        var group = Field(s.Group, v => s.Group = v);
        var station = Field(s.Station, v => s.Station = v);
        var cap = Field(s.Cap.ToString(CultureInfo.InvariantCulture),
            v => s.Cap = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) && c > 0 ? c : 1);
        cap.Leave += (o, e) => cap.Text = s.Cap.ToString(CultureInfo.InvariantCulture); // show the value AGVCP will actually use
        tabs.TabPages.Add(Page("CP", Theme.Section("CP 속성"), detailed,
            Theme.Caption("TYPE"), type, Theme.Caption("GROUP"), group, Theme.Caption("STATION"), station,
            Theme.Caption("접두어"), prefix, Theme.Caption("CAP"), cap,
            Theme.Section("배치"), Theme.Make("CP 배치", GlyphKind.Cp, "AGVCP"), Theme.Make("traffic CP 자동 생성", GlyphKind.Cp, "AGVCPAUTO")));

        var settings = Theme.Make("Settings 열기", GlyphKind.Settings, "");
        settings.Click += (o, e) => Commands.ExportCommands.OpenSettings();
        tabs.TabPages.Add(Page("교통제어", Theme.Section("자동 생성"),
            Theme.Make("block 자동 생성", GlyphKind.Block, "AGVBLOCKAUTO"), Theme.Make("교통제어 자동 생성", GlyphKind.Traffic, "AGVTRAFFIC"),
            Theme.Section("설정"), settings));

        var checkResult = Theme.Caption("마지막 검사 결과: -");
        tabs.TabPages.Add(Page("검증·내보내기", Theme.Section("검증"), Theme.Make("검증", GlyphKind.Check, "AGVCHECK"), checkResult,
            Theme.Section("내보내기"), Theme.Make("내보내기", GlyphKind.Export, "AGVEXPORT"),
            Theme.Section("모델"), Theme.Make("Excel 양식", GlyphKind.Excel, "AGVEXCEL"), Theme.Make("모델 생성·빌드", GlyphKind.Model, "AGVMODEL")));

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
        PaletteStatus.Check = n => checkResult.Text = $"마지막 검사 결과 {n}건";

        Controls.Add(tabs); // Fill first: later Top/Bottom controls dock before it
        Controls.Add(header);
        Controls.Add(status);
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
