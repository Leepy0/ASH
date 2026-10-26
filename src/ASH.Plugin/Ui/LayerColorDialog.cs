using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ASH.Core.Layout;

namespace ASH.Plugin.Ui;

/// <summary>Form for the ASH layer colours: one row per layer (name, purpose, swatch, AutoCAD colour number 1-255, "기본값"), "모두 기본값", OK/Cancel.
/// Starts from this PC's current changes; <see cref="Overrides"/> holds only the colours that differ from the default. The caller saves and applies them
/// (AGVLAYERCOLOR): the palette never touches the drawing. Touches no ZWCAD type.</summary>
public sealed class LayerColorDialog : Form
{
    private readonly ToolTip _tip = new ToolTip();
    private readonly List<(LayerColor Layer, NumericUpDown Box, Panel Swatch, Button Reset)> _rows = new List<(LayerColor, NumericUpDown, Panel, Button)>();

    /// <summary>Layer → colour for every layer whose number differs from its default (what LayerColors.Format writes).</summary>
    public IReadOnlyDictionary<string, short> Overrides => _rows
        .Where(r => (short)r.Box.Value != r.Layer.Default)
        .ToDictionary(r => r.Layer.Layer, r => (short)r.Box.Value);

    public LayerColorDialog(IReadOnlyDictionary<string, short> current)
    {
        Text = "ASH 레이어 색상";
        Font = Theme.Base;
        ForeColor = Theme.Text;
        BackColor = Theme.Panel;
        Theme.ScaleToDpi(this);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(640, 700);

        // right padding leaves room for the vertical scroll bar (17 px at 96 dpi, scaled with the form), so no horizontal one appears
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 5, BackColor = Theme.Panel, Padding = new Padding(16, 4, 16 + 17, 12) };
        // text columns size to their text (a fixed width would cut it at 125% / 150% Windows scaling)
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        int row = 0;
        var intro = new Label { Text = "AutoCAD 색 번호(1–255)로 정합니다. 바꾼 색은 이 PC에 저장되어 현재 도면과 이후 모든 ASH 명령에 쓰입니다.", ForeColor = Theme.Muted, AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(2, 8, 0, 8) };
        table.Controls.Add(intro, 0, row++);
        table.SetColumnSpan(intro, 5);
        foreach (var layer in LayerColors.Table)
        {
            short start = current.TryGetValue(layer.Layer, out var v) && LayerColors.IsAci(v) ? v : layer.Default;
            var box = new NumericUpDown { Minimum = 1, Maximum = 255, Value = start, Width = 64, Anchor = AnchorStyles.Left, Margin = new Padding(0, 2, 8, 2), TextAlign = HorizontalAlignment.Right, AccessibleName = layer.Layer };
            var swatch = new Panel { Size = new Size(24, 20), Anchor = AnchorStyles.Left, Margin = new Padding(0, 2, 8, 2), BorderStyle = BorderStyle.FixedSingle };
            var reset = new Button { Text = "기본값", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(68, 26), Anchor = AnchorStyles.Left, Margin = new Padding(0, 2, 0, 2), FlatStyle = FlatStyle.Flat, BackColor = Theme.Panel, ForeColor = Theme.Text, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
            reset.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
            var r = (layer, box, swatch, reset);
            _rows.Add(r);
            box.ValueChanged += (o, e) => Sync(r);
            reset.Click += (o, e) => box.Value = layer.Default;
            table.Controls.Add(new Label { Text = layer.Layer, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(2, 0, 16, 0) }, 0, row);
            table.Controls.Add(new Label { Text = layer.Purpose, AutoSize = true, ForeColor = Theme.Muted, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) }, 1, row);
            table.Controls.Add(swatch, 2, row);
            table.Controls.Add(box, 3, row);
            table.Controls.Add(reset, 4, row++);
            Sync(r);
        }

        var ok = Flat("확인", Theme.Accent, Color.White);
        var cancel = Flat("취소", Theme.Panel, Theme.Text);
        cancel.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
        cancel.FlatAppearance.BorderSize = 1;
        cancel.DialogResult = DialogResult.Cancel;
        ok.Click += (o, e) => DialogResult = DialogResult.OK;
        var all = new LinkLabel { Text = "모두 기본값", AutoSize = true, LinkColor = Theme.Accent };
        all.LinkClicked += (o, e) => { foreach (var x in _rows) x.Box.Value = x.Layer.Default; };
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Theme.Bg };
        bar.Controls.AddRange(new Control[] { all, ok, cancel });
        bar.Layout += (o, e) =>
        {
            cancel.Location = new Point(bar.ClientSize.Width - 16 - cancel.Width, (bar.ClientSize.Height - cancel.Height) / 2);
            ok.Location = new Point(cancel.Left - 8 - ok.Width, cancel.Top);
            all.Location = new Point(16, (bar.ClientSize.Height - all.Height) / 2);
        };
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(table); // Fill first, then the bottom bar docks before it
        Controls.Add(bar);
        Load += (o, e) => Theme.FitHeight(this, table); // after the windows exist: see FitHeight
    }

    // swatch = the colour AutoCAD draws for the number; "기본값" only where it would change something
    private void Sync((LayerColor Layer, NumericUpDown Box, Panel Swatch, Button Reset) r)
    {
        var (cr, cg, cb) = AciColors.Rgb((int)r.Box.Value);
        r.Swatch.BackColor = Color.FromArgb(cr, cg, cb);
        r.Reset.Enabled = (short)r.Box.Value != r.Layer.Default;
        _tip.SetToolTip(r.Swatch, "AutoCAD 색 번호 " + (int)r.Box.Value + (r.Box.Value == 7 ? " (배경에 따라 흰색·검정)" : ""));
    }

    private static Button Flat(string text, Color back, Color fore)
    {
        var b = new Button { Text = text, Size = new Size(88, 32), FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = fore, Font = Theme.Base, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }
}
