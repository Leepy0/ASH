using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using ASH.Core.Lite;

namespace ASH.Plugin.Ui;

/// <summary>AGVLITE's choices: which entity types go into the lite DXF (each with its entity count and estimated size), the minimum size, the options, and the curve tolerance.
/// Counts and sizes follow the options live; the total turns amber over 5 MB and red over 10 MB with the heaviest checked types named. Touches no ZWCAD type.</summary>
public sealed class LiteDialog : Form
{
    private const long Target = 5L * 1048576, Limit = 10L * 1048576;
    private static readonly Font HintFont = new Font("Segoe UI", 8.25f);
    private static readonly Color Amber = Color.FromArgb(180, 83, 9), Red = Color.FromArgb(220, 38, 38), Green = Color.FromArgb(21, 128, 61);

    private readonly LitePlan _plan;
    private readonly ListView _list;
    private readonly CheckBox _xref, _hidden;
    private readonly NumericUpDown _tol, _min;
    private readonly Label _total, _warn;
    private bool _loading = true;

    public LiteOptions Options => new LiteOptions(_xref.Checked, _hidden.Checked, _plan.FromMm((double)_min.Value));
    public List<string> Types => _list.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (string)i.Tag).ToList();
    public double ToleranceMm => (double)_tol.Value;

    public LiteDialog(LitePlan plan, string drawing, string output, double toleranceMm, double minSizeMm, LiteOptions? initial = null, ICollection<string>? checkedTypes = null)
    {
        _plan = plan;
        var o = initial ?? new LiteOptions();
        var src = plan.Source;
        Text = "ASH 도면 경량화";
        Font = Theme.Base;
        ForeColor = Theme.Text;
        BackColor = Theme.Panel;
        Theme.ScaleToDpi(this);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(617, 740); // 96 dpi; 17 px of it are for the scroll bar. FitHeight below keeps it on the screen

        var info = new Label { AutoSize = true, MaximumSize = new Size(568, 0), Font = HintFont, ForeColor = Theme.Muted, Margin = new Padding(2, 8, 0, 0),
            Text = $"{drawing}  →  {output}\n원본 도면은 바꾸지 않습니다. 블록 안까지 풀어 읽은 선·곡선 {src.Items.Count:N0}개 · 모든 좌표 Z = 0\n" +
                // what went where, biggest first: a block that seems to vanish shows here (its splines, 3D objects, text ...)
                $"항상 제외 {src.Removed.Values.Sum():N0}개: " + string.Join(" · ", src.Removed.OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Key} {kv.Value:N0}")) +
                (src.Removed.Count > 6 ? " 외" : "") };
        var problems = new List<string>();
        if (src.Truncated) problems.Add("작업 한도에 도달해 도면 일부만 읽었습니다(블록 중첩·반복이 너무 많음). 결과가 빠질 수 있습니다.");
        if (src.Failures > 0) problems.Add($"읽지 못한 엔티티 {src.Failures:N0}개는 건너뜁니다.");
        var problem = new Label { AutoSize = true, MaximumSize = new Size(568, 0), ForeColor = Red, Margin = new Padding(2, 4, 0, 0), Text = string.Join("\n", problems), Visible = problems.Count > 0 };

        _list = new ListView
        {
            View = View.Details, CheckBoxes = true, FullRowSelect = true, MultiSelect = false, HideSelection = true, HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White, Font = Theme.Base, Height = 140, Margin = new Padding(0, 2, 0, 2), ShowItemToolTips = true,
        };
        _list.Columns.Add("종류", 344);
        _list.Columns.Add("엔티티", 90, HorizontalAlignment.Right);
        _list.Columns.Add("예상 MB", 90, HorizontalAlignment.Right);
        foreach (var t in plan.Types)
            _list.Items.Add(new ListViewItem(new[] { t, "", "" })
            {
                Tag = t, Checked = checkedTypes != null ? checkedTypes.Contains(t) : plan.DefaultOn(t),
                ToolTipText = t == LiteTypes.Footprint ? "3D 솔리드·메시·서피스의 범위 상자(GeometricExtents). 실제 평면 투영이 아닙니다."
                    : t == LiteTypes.Spline ? "기본은 지웁니다. 다른 CAD에서 가져온 장비 블록의 외곽이 스플라인이면 켜세요(곡선 허용 오차로 나눈 선으로 남김)."
                    : "최소 크기 미만은 빠집니다. 블록은 외곽 상자가 작으면 통째로, 크면 안의 것을 같은 기준으로(이어진 선은 묶음 전체 크기로) 판단.",
            });
        _list.ItemChecked += (s, e) => Recount(false);

        LinkLabel Link(string text, Action act)
        {
            var k = new LinkLabel { Text = text, AutoSize = true, LinkColor = Theme.Accent, Margin = new Padding(2, 2, 12, 0) };
            k.LinkClicked += (s, e) => { _loading = true; act(); _loading = false; Recount(false); };
            return k;
        }
        var links = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
        links.Controls.Add(Link("모두 선택", () => { foreach (ListViewItem i in _list.Items) i.Checked = true; }));
        links.Controls.Add(Link("모두 해제", () => { foreach (ListViewItem i in _list.Items) i.Checked = false; }));
        links.Controls.Add(Link("기본값", () => { foreach (ListViewItem i in _list.Items) i.Checked = plan.DefaultOn((string)i.Tag); }));

        CheckBox Opt(string text, bool value)
        {
            var c = new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new Padding(2, 4, 0, 0) };
            c.CheckedChanged += (s, e) => Recount(true);
            return c;
        }
        _xref = Opt($"외부참조 내용 포함  (외부참조 {src.XrefRefs}개 삽입)", o.IncludeXref);
        _min = new NumericUpDown { Minimum = 0m, Maximum = 100000m, DecimalPlaces = 0, Increment = 10m, Width = 80, Value = (decimal)Math.Max(0, Math.Min(100000, minSizeMm)), Margin = new Padding(0, 2, 6, 0) };
        _min.ValueChanged += (s, e) => Recount(true);
        var minRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 2, 0, 0) };
        minRow.Controls.Add(new Label { Text = "최소 크기 (mm)", AutoSize = true, Margin = new Padding(2, 5, 8, 0) });
        minRow.Controls.Add(_min);
        var minHint = Hint("외곽 상자 대각선이 이보다 작으면 지웁니다. 끝점이 이어진 선·호·열린 폴리선은 묶음 전체 크기로 판단해, 짧은 조각으로 그린 큰 외곽은 남깁니다. 블록은 외곽 상자가 이보다 작으면 통째로 지우고, 크면 안의 것을 같은 기준으로 판단합니다(중첩 블록도 단계마다). 0 = 지우지 않음.");
        _hidden = Opt("꺼진·동결 레이어 제외", o.SkipHidden);
        _tol = new NumericUpDown { Minimum = 0.1m, Maximum = 1000m, DecimalPlaces = 1, Increment = 1m, Width = 80, Value = (decimal)Math.Max(0.1, Math.Min(1000, toleranceMm)), Margin = new Padding(0, 2, 6, 0) };
        var tolRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        tolRow.Controls.Add(new Label { Text = "곡선 허용 오차 (mm)", AutoSize = true, Margin = new Padding(2, 5, 8, 0) });
        tolRow.Controls.Add(_tol);
        var tolHint = Hint("타원과 늘어난 블록의 원호를 선분으로 바꿀 때의 최대 오차. 바꾸면 '만들기' 뒤에 도면을 다시 읽습니다(예상 크기는 지금 값 기준).");

        _total = new Label { AutoSize = true, Font = new Font("Segoe UI", 11f, FontStyle.Bold), Margin = new Padding(2, 10, 0, 0) };
        _warn = new Label { AutoSize = true, MaximumSize = new Size(568, 0), Margin = new Padding(2, 2, 0, 0) };

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16, 4, 16, 8), BackColor = Theme.Panel };
        flow.Controls.AddRange(new Control[] { info, problem, Theme.Section("종류  (체크한 종류만 경량 도면에 들어갑니다)"), links, _list, minRow, minHint,
            Theme.Section("옵션"), _xref, _hidden, tolRow, tolHint, _total, _warn });
        _list.Width = 568;
        _list.Resize += (s, e) => _list.Columns[0].Width = Math.Max(120, _list.ClientSize.Width - 90 - 90);

        var ok = Flat("만들기", Theme.Accent, Color.White);
        var cancel = Flat("취소", Theme.Panel, Theme.Text);
        cancel.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
        cancel.FlatAppearance.BorderSize = 1;
        cancel.DialogResult = DialogResult.Cancel;
        ok.Click += (s, e) =>
        {
            if (Types.Count == 0) { MessageBox.Show(this, "종류를 하나 이상 체크하세요.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
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
        _loading = false;
        Recount(true);
    }

    // rows (when options changed) and the total; ListView fires ItemChecked for every row while loading, hence the guard
    private void Recount(bool rows)
    {
        if (_loading) return;
        var o = Options;
        _list.BeginUpdate();
        var by = rows ? _plan.ByType(o) : null;
        if (by != null)
            foreach (ListViewItem i in _list.Items)
            {
                var (n, bytes) = by[(string)i.Tag];
                i.SubItems[1].Text = n.ToString("N0", CultureInfo.InvariantCulture);
                i.SubItems[2].Text = Mb(bytes);
                i.ForeColor = n == 0 ? Theme.Muted : Theme.Text;
            }
        _list.EndUpdate();
        var on = Types;
        long total = _plan.Total(o, on);
        _total.Text = $"예상 크기: {Mb(total)} MB  ·  종류 {on.Count}/{_list.Items.Count}개  ·  작아서 지움 {_plan.Small(o):N0}개";
        _total.ForeColor = total > Limit ? Red : total > Target ? Amber : Green;
        if (total > Limit)
        {
            var big = _plan.Biggest(o, on).Select(b => $"{b.Type} ({Mb(b.Bytes)} MB)");
            _warn.Text = "10 MB를 넘습니다. AutoMod가 느려지거나 멈출 수 있습니다(목표 5 MB 이하). 최소 크기를 키우거나 끄기 후보: " + string.Join(", ", big);
            _warn.ForeColor = Red;
        }
        else
        {
            _warn.Text = total > Target ? "목표(5 MB 이하)보다 큽니다. 열 수는 있지만 최소 크기를 키워 보세요." : "목표(5 MB 이하) 안입니다.";
            _warn.ForeColor = total > Target ? Amber : Theme.Muted;
        }
    }

    private static string Mb(long bytes) => (bytes / 1048576.0).ToString(bytes < 10 * 1048576 ? "0.00" : "0.0", CultureInfo.InvariantCulture);

    private static Label Hint(string text) =>
        new Label { Text = text, Font = HintFont, ForeColor = Theme.Muted, AutoSize = true, MaximumSize = new Size(530, 0), Margin = new Padding(20, 0, 0, 2) };

    private static Button Flat(string text, Color back, Color fore)
    {
        var b = new Button { Text = text, Size = new Size(88, 32), FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = fore, Font = Theme.Base, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }
}
