using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using ASH.Core.Lite;

namespace ASH.Plugin.Ui;

/// <summary>A layer as AGVLITEX lists it. State is empty for a layer a person may choose, else why it is always out ("꺼짐", "동결"): such a row is grey and cannot be ticked.</summary>
public sealed class LiteLayerRow
{
    public string Name { get; }
    public string State { get; }
    public bool Included { get; }
    public LiteLayerRow(string name, string state, bool included) { Name = name; State = state; Included = included; }
}

/// <summary>AGVLITEX's choices: the level (자동 or 0..4, each with its estimated size and outlined blobs), whether blob outlines are off, and which layers stay in.
/// The estimates are those of the exclusions the drawing was measured with; they do not follow the ticks. Touches no ZWCAD type.</summary>
public sealed class LiteDialog : Form
{
    private static readonly Color Amber = Color.FromArgb(180, 83, 9), Red = Color.FromArgb(220, 38, 38), Green = Color.FromArgb(21, 128, 61);

    private readonly RadioButton[] _radio; // 0 = 자동, then one per level
    private readonly CheckBox _noBlobs;
    private readonly ListView _list;

    /// <summary>Null = 자동, else the level.</summary>
    public int? Level => _radio[0].Checked ? null : Array.FindIndex(_radio, r => r.Checked) - 1;
    public bool NoBlobs => _noBlobs.Checked;
    /// <summary>The layers a person may choose and left unticked.</summary>
    public List<string> ExcludedLayers => _list.Items.Cast<ListViewItem>().Where(i => !i.Checked && ((LiteLayerRow)i.Tag).State.Length == 0).Select(i => ((LiteLayerRow)i.Tag).Name).ToList();

    public LiteDialog(string drawing, string output, IReadOnlyList<LiteLevelEstimate> levels, IReadOnlyList<LiteLayerRow> layers, int? level, bool noBlobs)
    {
        Text = "ASH 경량화 조정";
        Font = Theme.Base;
        ForeColor = Theme.Text;
        BackColor = Theme.Panel;
        Theme.ScaleToDpi(this);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(497, 610); // 96 dpi; 17 px of it are for the scroll bar. FitHeight below keeps it on the screen

        var info = new Label { AutoSize = true, MaximumSize = new Size(448, 0), ForeColor = Theme.Muted, Margin = new Padding(2, 8, 0, 0), Text = $"{drawing}  →  {output}" };

        int auto = AutoLevel(levels);
        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 2, 0, 4) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        table.Controls.Add(new Label { Text = "", AutoSize = true }, 0, 0);
        table.Controls.Add(Cell("예상 크기", Theme.Muted, true), 1, 0);
        table.Controls.Add(Cell("외곽선 덩어리", Theme.Muted, true), 2, 0);
        _radio = new RadioButton[levels.Count + 1];
        for (int i = 0; i < _radio.Length; i++)
        {
            var r = _radio[i] = new RadioButton { Text = i == 0 ? $"자동 ({auto}단계)" : $"{i - 1}단계", AutoSize = true, Margin = new Padding(2, 4, 0, 4) };
            table.Controls.Add(r, 0, i + 1);
            var e = levels[i == 0 ? auto : i - 1];
            table.Controls.Add(Cell($"{Mb(e.Bytes)} MB", e.Bytes > LiteLevels.WarnBytes ? Red : e.Bytes > LiteLevels.TargetBytes ? Amber : Green, false), 1, i + 1);
            table.Controls.Add(Cell($"{e.Blobs.ToString("N0", CultureInfo.InvariantCulture)}개", Theme.Text, false), 2, i + 1);
        }
        _radio[Math.Min(_radio.Length - 1, (level ?? -1) + 1)].Checked = true;

        _noBlobs = new CheckBox { Text = "덩어리 외곽선 끄기", Checked = noBlobs, AutoSize = true, Margin = new Padding(2, 6, 0, 0) };

        _list = new ListView
        {
            View = View.Details, CheckBoxes = true, FullRowSelect = true, MultiSelect = false, HideSelection = true, HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White, Font = Theme.Base, Width = 448, Height = 200, Margin = new Padding(0, 2, 0, 2),
        };
        _list.Columns.Add("레이어", 340);
        _list.Columns.Add("상태", 80);
        foreach (var l in layers)
            _list.Items.Add(new ListViewItem(new[] { l.Name, l.State }) { Tag = l, Checked = l.State.Length == 0 && l.Included, ForeColor = l.State.Length == 0 ? Theme.Text : Theme.Muted });
        _list.ItemCheck += (s, e) => { if (((LiteLayerRow)_list.Items[e.Index].Tag).State.Length > 0) e.NewValue = e.CurrentValue; }; // off and frozen are always out
        _list.Resize += (s, e) => _list.Columns[0].Width = Math.Max(120, _list.ClientSize.Width - 80);

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16, 4, 16, 8), BackColor = Theme.Panel };
        flow.Controls.AddRange(new Control[] { info, Theme.Section("단계"), table, _noBlobs, Theme.Section("레이어"), _list });

        var ok = Flat("만들기", Theme.Accent, Color.White);
        var cancel = Flat("취소", Theme.Panel, Theme.Text);
        cancel.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
        cancel.FlatAppearance.BorderSize = 1;
        cancel.DialogResult = DialogResult.Cancel;
        ok.Click += (s, e) =>
        {
            var chosen = levels[Level ?? auto];
            if (chosen.Bytes > LiteLevels.WarnBytes
                && MessageBox.Show(this, $"10 MB 초과 ({Mb(chosen.Bytes)} MB)\nAutoMod 느려질 수 있음\n그대로 만들까요?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            DialogResult = DialogResult.OK;
        };
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Theme.Bg };
        bar.Controls.AddRange(new Control[] { ok, cancel });
        bar.Layout += (s, e) =>
        {
            cancel.Location = new Point(bar.ClientSize.Width - 16 - cancel.Width, (bar.ClientSize.Height - cancel.Height) / 2);
            ok.Location = new Point(cancel.Left - 8 - ok.Width, cancel.Top);
        };
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(flow); // Fill first, then the bottom bar docks before it
        Controls.Add(bar);
        Theme.FitHeight(this, null);
    }

    /// <summary>The level automatic stops at: the first that fits the target size, else the last.</summary>
    private static int AutoLevel(IReadOnlyList<LiteLevelEstimate> levels)
    {
        for (int i = 0; i < levels.Count; i++)
            if (levels[i].Bytes <= LiteLevels.TargetBytes) return i;
        return levels.Count - 1;
    }

    private static Label Cell(string text, Color color, bool header) =>
        new Label { Text = text, ForeColor = color, Font = header ? Theme.Small : Theme.Base, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(2, 4, 0, 4) };

    private static string Mb(long bytes) => (bytes / 1048576.0).ToString(bytes < 10 * 1048576 ? "0.00" : "0.0", CultureInfo.InvariantCulture);

    private static Button Flat(string text, Color back, Color fore)
    {
        var b = new Button { Text = text, Size = new Size(88, 32), FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = fore, Font = Theme.Base, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }
}
