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
            Theme.Make("경로 그리기", GlyphKind.Path, "AGVPATH"), Theme.Make("선·폴리선 → 경로 변환", GlyphKind.Convert, "AGVCONVERT"), Theme.Make("방향 반전", GlyphKind.Reverse, "AGVREVERSE"), Theme.Make("경로 종류 변경", GlyphKind.PathType, "AGVPTYPE"),
            Theme.Make("평행 복제", GlyphKind.Offset, "AGVOFFSET"), Theme.Make("배열 복제", GlyphKind.Array, "AGVARRAY"),
            Theme.Section("연결"),
            Theme.Make("교차 분할", GlyphKind.Split, "AGVSPLIT"), Theme.Make("경로 잇기", GlyphKind.Join, "AGVJOIN"), Theme.Make("필렛", GlyphKind.Fillet, "AGVFILLET"),
            Theme.Section("방향표"), Theme.Make("방향표 크기·갱신", GlyphKind.ArrowSize, "AGVARROW")));

        var s = CpSettings.Current;
        var detailed = new CheckBox { Text = "상세 모드", Checked = s.DetailedMode, AutoSize = true, Margin = new Padding(2, 4, 0, 4) };
        detailed.CheckedChanged += (o, e) => s.DetailedMode = detailed.Checked;
        var prefix = Box(s.Prefix);
        var hint = new Label { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, Margin = new Padding(2, 2, 0, 0) };
        var group = Field(s.Group, v => s.Group = v);
        var cap = Field(CpTypes.FormatCap(s.Cap), v => { if (CpTypes.TryParseCap(v, out var c)) s.Cap = c; });
        void ShowType()
        {
            hint.Text = $"{s.Type} · 이름 {AutoNamer.PatternFor(s.Type)} · CAP {CpTypes.FormatCap(s.Cap)} · 레이어 {Conventions.LayerCpFor(s.Type)}";
            hint.ForeColor = Theme.CpColor(s.Type);
        }
        prefix.TextChanged += (o, e) => { s.Prefix = prefix.Text.Trim(); ShowType(); };
        cap.Leave += (o, e) => { cap.Text = CpTypes.FormatCap(s.Cap); ShowType(); }; // show the value AGVCP will actually use
        ShowType();
        // one button per TYPE in CpTypes.PaletteOrder (a work pair per row), glyph in the type's layer colour; the type is set only once AGVCP was really sent (not while a command blocks it)
        // fixed height, width from Page's stretch (AutoSize would shrink the columns to the text)
        // two-line labels (type over Korean name): the row height follows the font, so Windows scaling cannot clip the text
        int rowH = 2 * TextRenderer.MeasureText("가", Theme.Base).Height + 8;
        var place = new TableLayoutPanel { ColumnCount = 2, RowCount = (CpTypes.PaletteOrder.Length + 1) / 2, Height = (CpTypes.PaletteOrder.Length + 1) / 2 * rowH, Margin = Padding.Empty };
        place.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        place.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < place.RowCount; i++) place.RowStyles.Add(new RowStyle(SizeType.Absolute, rowH));
        var cpButtons = new Dictionary<string, Button>();
        foreach (var t in CpTypes.PaletteOrder)
        {
            var b = Theme.Make(t, GlyphKind.Cp, "");
            b.Text = " " + t + "\n " + CpTypes.Korean(t);
            b.Padding = new Padding(4, 0, 0, 0); // a two-line label in half a palette: every pixel counts
            b.Image = Glyphs.Draw(GlyphKind.Cp, 20, Theme.CpColor(t));
            cpButtons[t] = b;
            b.Dock = DockStyle.Fill;
            b.Margin = new Padding(0, 0, 2, 2);
            new ToolTip().SetToolTip(b, $"{t} CP 배치 · 이름 {AutoNamer.PatternFor(t)} (AGVCP)");
            b.Click += (o, e) =>
            {
                if (s.Type == t) { AgvPalette.Send("AGVCP"); return; } // same type again keeps an edited prefix/CAP
                // set before sending (AGVCP must see it whether ZWCAD queues or runs the command at once), put back when the send is refused
                var (oldType, oldPrefix, oldCap) = (s.Type, s.Prefix, s.Cap);
                s.Type = t; // also resets Prefix and Cap to the type's defaults
                if (!AgvPalette.Send("AGVCP")) { s.Type = oldType; s.Prefix = oldPrefix; s.Cap = oldCap; return; }
                prefix.Text = s.Prefix;
                cap.Text = CpTypes.FormatCap(s.Cap);
                ShowType();
            };
            place.Controls.Add(b);
        }
        // standby → load → unload → rtnload → rtnunload → finish in one go; takes the GROUP below, leaves the TYPE buttons' state alone
        var route = Theme.Make("루트 순서 배치", GlyphKind.Path, "AGVCPROUTE");
        new ToolTip().SetToolTip(route, "standby → load → unload → rtnload → rtnunload → finish 순서로 CP 배치 (AGVCPROUTE)");
        // the colour dialog (AGVLAYERCOLOR) changed a layer colour: repaint the glyphs and the hint
        Action recolor = () =>
        {
            foreach (var kv in cpButtons) kv.Value.Image = Glyphs.Draw(GlyphKind.Cp, 20, Theme.CpColor(kv.Key));
            ShowType();
        };
        Theme.LayerColorsChanged += recolor;
        Disposed += (o, e) => Theme.LayerColorsChanged -= recolor;
        tabs.TabPages.Add(Page("CP", Theme.Section("CP 배치  (TYPE별 버튼)"), place, route, hint,
            Theme.Caption("traffic CP는 교통제어 탭에서 block과 함께 자동 생성"),
            Theme.Section("CP 속성"), detailed,
            Theme.Caption("GROUP"), group,
            Theme.Caption("접두어  (마지막 TYPE에 적용)"), prefix, Theme.Caption("CAP  (숫자 또는 INF = 무제한)"), cap,
            Theme.Section("그룹 보기"), GroupView(tabs),
            Theme.Section("루트 보기"), RouteViewPanel()));

        tabs.TabPages.Add(Page("교통제어", Theme.Section("자동 생성"),
            Theme.Make("block 자동 생성", GlyphKind.Block, "AGVBLOCKAUTO"), Theme.Make("블록 채번", GlyphKind.BlockNum, "AGVBLOCKNUM"), Theme.Make("교통제어 자동 생성", GlyphKind.Traffic, "AGVTRAFFIC")));

        // 3D picture assets (design 2026-10-26 §8): the kind goes to AGV3D as its keyword answer
        var kind3d = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.System };
        kind3d.Items.AddRange(AssetKinds.Blocks.Select(k => (object)ASH.Core.Cel.AssetShapes.Label(k)).ToArray());
        kind3d.SelectedIndex = 0;
        var place3d = Theme.Make("배치", GlyphKind.Block, "");
        place3d.Click += (o, e) => AgvPalette.Send("AGV3D " + AssetKinds.Blocks[Math.Max(0, kind3d.SelectedIndex)].Replace("_", "")); // AGV3D keyword: kind without '_'
        var wallW = Box(Line3dSettings.WallM.ToString(System.Globalization.CultureInfo.CurrentCulture));
        var convW = Box(Line3dSettings.ConvM.ToString(System.Globalization.CultureInfo.CurrentCulture));
        wallW.TextChanged += (o, e) => { if (Line3dSettings.TryParseM(wallW.Text, out var m)) Line3dSettings.WallM = m; };
        convW.TextChanged += (o, e) => { if (Line3dSettings.TryParseM(convW.Text, out var m)) Line3dSettings.ConvM = m; };
        wallW.Leave += (o, e) => wallW.Text = Line3dSettings.WallM.ToString(System.Globalization.CultureInfo.CurrentCulture); // show the value AGV3DLINE will use
        convW.Leave += (o, e) => convW.Text = Line3dSettings.ConvM.ToString(System.Globalization.CultureInfo.CurrentCulture);
        // the kind is handed over through Pending (the command runs after this handler returns); a blocked send must not leave it behind
        Button line3d(string text, string kind)
        {
            var b = Theme.Make(text, GlyphKind.Path, "");
            b.Click += (o, e) => { Line3dSettings.Pending = kind; if (!AgvPalette.Send("AGV3DLINE")) Line3dSettings.Pending = null; };
            return b;
        }
        tabs.TabPages.Add(Page("3D", Theme.Section("3D 배치"), Theme.Caption("종류"), kind3d, place3d,
            Theme.Caption("크기: 블록 X/Y 배율 (1 = 기본 크기)"),
            Theme.Section("선으로 그리기"),
            Theme.Caption("벽 두께 (m)"), wallW, line3d("벽", Line3dSettings.Wall),
            line3d("펜스", Line3dSettings.Fence),
            Theme.Caption("컨베이어 폭 (m)"), convW, line3d("컨베이어", Line3dSettings.Conv), line3d("컨베이어 (그림만)", Line3dSettings.ConvPicture),
            Theme.Caption("점 클릭 · Enter 끝 · C 닫기 · U 되돌리기"),
            Theme.Section("컨베이어"),Theme.Make("컨베이어 이름 붙이기", GlyphKind.Path, "AGVCONV")));

        var settings = Theme.Make("설정 열기", GlyphKind.Settings, "");
        settings.Click += (o, e) => Commands.ExportCommands.OpenSettings();
        var layerColors = Theme.Make("레이어 색상", GlyphKind.Settings, "AGVLAYERCOLOR"); // the command shows the dialog and recolours the drawing

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
                problems.Items.Add(new ListViewItem(new[] { r.Kind, r.Target, r.Message }) { Tag = r, ForeColor = r.Warning ? Theme.Warning : Theme.Error, ToolTipText = r.Message });
            problems.EndUpdate();
        };
        tabs.TabPages.Add(Page("검증·내보내기", Theme.Section("도면 준비"), Theme.Make("도면 경량화", GlyphKind.Lite, "AGVLITE"),
            Theme.Section("검증"),Theme.Make("검증", GlyphKind.Check, "AGVCHECK"), checkResult,
            Theme.Caption("문제 목록 — 더블클릭하면 그 위치로 이동"), problems,
            // "모델 생성" also does what "내보내기" did (AGVEXPORT is still a command): <Model>.arc, and <Model>.dir when this PC has AutoMod
            Theme.Section("모델"), Theme.Make("Excel 양식", GlyphKind.Excel, "AGVEXCEL"), Theme.Make("모델 생성", GlyphKind.Model, "AGVMODEL"), Theme.Make("빌드·실행", GlyphKind.Run, "AGVRUN"),
            Theme.Make("서버로 보내기", GlyphKind.Send, "AGVSEND"), Theme.Make("결과 올리기", GlyphKind.Send, "AGVRESULTUP"),
            Theme.Section("설정"), settings, layerColors));

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

    private sealed class GroupItem
    {
        public GroupItem(CpGroupInfo info) { Info = info; }
        public CpGroupInfo Info { get; }
        public override string ToString() => $"{(Info.Group.Length > 0 ? Info.Group : "(그룹 없음)")}   CP {Info.Count}개";
    }

    // F1 group view: the ticked groups stay as they are, the others turn grey (강조) or disappear (숨김). AGVCPVIEW does it in the drawing.
    // The list is filled after every ASH command (PaletteStatus.SetGroups); the first visit of the CP tab asks for one.
    private static Control GroupView(TabControl tabs)
    {
        var mode = CpViewMode.Highlight;
        var busy = false; // a refill of the list or a take-back of a refused click must not send again
        var highlight = new RadioButton { Text = "강조", Checked = true, AutoSize = true };
        var hide = new RadioButton { Text = "숨김", AutoSize = true };
        var modes = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 26, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        modes.Controls.Add(highlight);
        modes.Controls.Add(hide);
        var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Base };
        var info = new Label { Dock = DockStyle.Bottom, Height = 18, Font = Theme.Small, ForeColor = Theme.Muted, Text = "CP가 있는 그룹이 없습니다" };
        new ToolTip().SetToolTip(list, "체크한 그룹은 그대로 두고, 나머지는 강조(회색) 또는 숨김 (AGVCPVIEW)");
        var showAll = Theme.Make("모두 표시", GlyphKind.Check, "");
        showAll.Dock = DockStyle.Bottom;

        HashSet<string> Ticked(int index, CheckState state)
        {
            var set = new HashSet<string>();
            for (int i = 0; i < list.Items.Count; i++)
                if (i == index ? state == CheckState.Checked : list.GetItemChecked(i)) set.Add(((GroupItem)list.Items[i]).Info.Group);
            return set;
        }
        list.ItemCheck += (o, e) =>
        {
            if (busy) return;
            if (!CpViewRequest.Go(mode, Ticked(e.Index, e.NewValue))) e.NewValue = e.CurrentValue; // refused: the box stays as the drawing is
        };
        EventHandler setMode = (o, e) =>
        {
            var m = hide.Checked ? CpViewMode.Hide : CpViewMode.Highlight;
            if (busy || m == mode) return;
            var ticked = Ticked(-1, CheckState.Unchecked);
            // nothing is grey or hidden while every group is ticked, so only the mode changes then
            if (ticked.Count < list.Items.Count && !CpViewRequest.Go(m, ticked))
            {
                busy = true;
                (mode == CpViewMode.Hide ? hide : highlight).Checked = true;
                busy = false;
                return;
            }
            mode = m;
        };
        highlight.CheckedChanged += setMode;
        hide.CheckedChanged += setMode;
        showAll.Click += (o, e) => CpViewRequest.Go(mode, null);

        var loaded = false;
        PaletteStatus.Groups = groups =>
        {
            loaded = true;
            busy = true;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var g in groups) list.Items.Add(new GroupItem(g), g.Shown);
            list.EndUpdate();
            busy = false;
            info.Text = groups.Count == 0 ? "CP가 있는 그룹이 없습니다" : $"그룹 {groups.Count}개 · CP {groups.Sum(g => g.Count)}개";
        };
        tabs.SelectedIndexChanged += (o, e) => { if (!loaded && tabs.SelectedTab?.Text == "CP") AgvPalette.Send("AGVFIND"); }; // AGVFIND ends in Run, which fills the list

        var panel = new Panel { Height = 220, Margin = new Padding(0, 0, 0, 4) };
        panel.Controls.Add(list); // Fill first: the docked edges then take their space before it
        panel.Controls.Add(info);
        panel.Controls.Add(showAll);
        panel.Controls.Add(modes);
        return panel;
    }

    // Route view: the materials of the drawing's Excel input; a double-click draws that material's route on AGV_ROUTE (AGVROUTE), 지우기 clears the layer.
    private static Control RouteViewPanel()
    {
        var list = Grid(("자재", 110), ("그룹", 90));
        list.Dock = DockStyle.Fill;
        var info = new Label { Dock = DockStyle.Bottom, Height = 18, Font = Theme.Small, ForeColor = Theme.Muted, Text = "목록 새로고침을 누르세요" };
        var refresh = Theme.Make("목록 새로고침", GlyphKind.Search, "");
        var clear = Theme.Make("지우기", GlyphKind.Check, "");
        refresh.Dock = DockStyle.Top;
        clear.Dock = DockStyle.Bottom;
        new ToolTip().SetToolTip(list, "더블클릭하면 그 자재의 CP와 구간별 최단 경로를 AGV_ROUTE 레이어에 그림 (AGVROUTE)");
        refresh.Click += (o, e) => RouteRequest.Go(RouteRequest.Kind.List);
        clear.Click += (o, e) => RouteRequest.Go(RouteRequest.Kind.Clear);
        list.DoubleClick += (o, e) =>
        {
            if (list.SelectedItems.Count > 0) RouteRequest.Go(RouteRequest.Kind.Show, list.SelectedItems[0].Text);
        };
        RouteRequest.Materials = items =>
        {
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var (material, group) in items) list.Items.Add(new ListViewItem(new[] { material, group }));
            list.EndUpdate();
            info.Text = items.Count == 0 ? "자재 없음" : $"자재 {items.Count}개 — 더블클릭하면 루트 표시";
        };
        var panel = new Panel { Height = 230, Margin = new Padding(0, 0, 0, 4) };
        panel.Controls.Add(list); // Fill first: the docked edges then take their space before it
        panel.Controls.Add(info);
        panel.Controls.Add(clear);
        panel.Controls.Add(refresh);
        return panel;
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
        var category = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.System };
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
        top.Controls.AddRange(new Control[] { Theme.Caption("범주"), category, Theme.Caption("이름·정보 검색"), text, Theme.Make("목록 새로고침", GlyphKind.Search, "AGVFIND"), count });
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
