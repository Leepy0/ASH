using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ASH.Core.Layout;
using ASH.Core.Traffic;

namespace ASH.Plugin.Ui;

/// <summary>AGVCPEDIT's form: group, CAP and type for all selected CPs at once. Only the ticked rows are applied (a row ticks itself when its value is edited);
/// "이름 다시 매기기" gives the CPs new names with the new type's prefix. The fields start with the value the selection shares. Touches no ZWCAD type.</summary>
public sealed class CpEditDialog : Form
{
    private static readonly Font HintFont = new Font("Segoe UI", 8.25f);

    private readonly CheckBox _useGroup, _useCap, _useType, _rename;
    private readonly TextBox _group, _cap;
    private readonly ComboBox _type;

    /// <summary>Null = leave that property alone.</summary>
    public string? Group => _useGroup.Checked ? _group.Text.Trim() : null;
    public int? Cap => _useCap.Checked && CpTypes.TryParseCap(_cap.Text, out var c) ? c : (int?)null;
    public string? Type => _useType.Checked ? CpTypes.All[_type.SelectedIndex] : null;
    public bool Rename => _useType.Checked && _rename.Checked;

    public CpEditDialog(int count, string? sharedGroup, int? sharedCap, string? sharedType)
    {
        Text = "ASH CP 편집";
        Font = Theme.Base;
        ForeColor = Theme.Text;
        BackColor = Theme.Panel;
        Theme.ScaleToDpi(this);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(380, 300); // 96 dpi; FitHeight below keeps it on the screen

        _group = new TextBox { BorderStyle = BorderStyle.FixedSingle, Text = sharedGroup ?? "", Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };
        _cap = new TextBox { BorderStyle = BorderStyle.FixedSingle, Text = sharedCap == null ? "" : CpTypes.FormatCap(sharedCap.Value), Width = 80, Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 0, 0) };
        _type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.System, Width = 160, Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 0, 0) };
        _type.Items.AddRange(CpTypes.All.Select(t => (object)(t + "  " + CpTypes.Korean(t))).ToArray());
        _type.SelectedIndex = Math.Max(0, Array.IndexOf(CpTypes.All, sharedType ?? ""));
        _useGroup = Tick("그룹");
        _useCap = Tick("CAP");
        _useType = Tick("종류");
        _rename = new CheckBox { Text = "이름 다시 매기기 (새 접두어)", AutoSize = true, Enabled = false, Margin = new Padding(2, 2, 0, 0) };
        _useType.CheckedChanged += (o, e) => _rename.Enabled = _useType.Checked;
        _group.TextChanged += (o, e) => _useGroup.Checked = true;
        _cap.TextChanged += (o, e) => _useCap.Checked = true;
        _type.SelectedIndexChanged += (o, e) => _useType.Checked = true;

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, BackColor = Theme.Panel, Padding = new Padding(16, 4, 16, 8) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int row = 0;
        void Span(Control c) { table.Controls.Add(c, 0, row++); table.SetColumnSpan(c, 2); }
        void Row(CheckBox tick, Control box) { table.Controls.Add(tick, 0, row); table.Controls.Add(box, 1, row++); }
        Span(new Label { Text = $"선택한 CP {count}개", AutoSize = true, Margin = new Padding(2, 8, 0, 4) });
        Span(new Label { Text = "체크한 항목만 적용", Font = HintFont, ForeColor = Theme.Muted, AutoSize = true, Margin = new Padding(2, 0, 0, 6) });
        Row(_useGroup, _group);
        Row(_useCap, _cap);
        table.Controls.Add(new Label { Text = "숫자 또는 INF = 무제한", Font = HintFont, ForeColor = Theme.Muted, AutoSize = true, Margin = new Padding(0, 1, 0, 2) }, 1, row++);
        Row(_useType, _type);
        table.Controls.Add(_rename, 1, row++);
        Span(new Label { Text = "이름 변경 시: 이전→새 이름 표를 명령줄·클립보드로 (Excel은 직접 수정)", Font = HintFont, ForeColor = Theme.Muted, AutoSize = true, MaximumSize = new Size(340, 0), Margin = new Padding(2, 4, 0, 2) });

        var ok = Flat("확인", Theme.Accent, Color.White);
        var cancel = Flat("취소", Theme.Panel, Theme.Text);
        cancel.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
        cancel.FlatAppearance.BorderSize = 1;
        cancel.DialogResult = DialogResult.Cancel;
        ok.Click += (o, e) => { if (Valid()) DialogResult = DialogResult.OK; };
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Theme.Bg };
        bar.Controls.AddRange(new Control[] { ok, cancel });
        bar.Layout += (o, e) =>
        {
            cancel.Location = new Point(bar.ClientSize.Width - 16 - cancel.Width, (bar.ClientSize.Height - cancel.Height) / 2);
            ok.Location = new Point(cancel.Left - 8 - ok.Width, cancel.Top);
        };
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(table); // Fill first, then the bottom bar docks before it
        Controls.Add(bar);
        Theme.FitHeight(this, null);
    }

    private bool Valid()
    {
        string? err = null;
        if (!_useGroup.Checked && !_useCap.Checked && !_useType.Checked) err = "바꿀 항목을 체크하세요.";
        else if (_useGroup.Checked && _group.Text.Trim().Length > 0 && !LayoutChecker.IsValidName(_group.Text.Trim())) err = "그룹 이름에 공백·예약어·'?'로 시작하는 이름은 쓸 수 없습니다.";
        else if (_useCap.Checked && !CpTypes.TryParseCap(_cap.Text, out _)) err = "CAP은 숫자 또는 INF입니다.";
        if (err == null) return true;
        MessageBox.Show(this, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private static CheckBox Tick(string text) => new CheckBox { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(2, 6, 12, 0) };

    private static Button Flat(string text, Color back, Color fore)
    {
        var b = new Button { Text = text, Size = new Size(88, 32), FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = fore, Font = Theme.Base, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }
}
