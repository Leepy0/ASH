using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ASH.Core.Cause;
using ASH.Core.Runner;

namespace ASH.Plugin.Ui;

/// <summary>One finding of the server's cause analysis as the palette lists it. X, Y, Width are drawing units (the place to zoom to; Width = the view width).</summary>
public sealed class CauseRow
{
    public CauseRow(CauseInfo item, int number, double x, double y, double width)
    {
        Item = item; Number = number; X = x; Y = y; Width = width;
    }
    public CauseInfo Item { get; }
    /// <summary>The marker number in the drawing; 0 = the finding has no place.</summary>
    public int Number { get; }
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public bool HasLocation => Number > 0;
}

/// <summary>The "원인" tab: AGVCAUSE fills the list from the server, a double-click zooms to the marker and selects the finding's CPs, paths and blocks.</summary>
internal static class CausePanel
{
    /// <summary>Fills the palette list (no-op until a palette exists). The text is the line above it: job and run.</summary>
    internal static Action<IReadOnlyList<CauseRow>, string>? Show;

    /// <summary>Read by AGVCAUSEGO; the palette cannot touch the drawing itself, so it leaves the row here and runs the command.</summary>
    internal static CauseRow? Pending;

    public static void Set(IReadOnlyList<CauseRow> rows, string caption) => Show?.Invoke(rows, caption);

    internal static TabPage Page()
    {
        var caption = Theme.Caption("원인 없음");
        var list = new ListView
        {
            View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, HeaderStyle = ColumnHeaderStyle.Nonclickable, Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle, Font = Theme.Base, BackColor = Color.White, ShowItemToolTips = true,
        };
        list.Columns.Add("#", 28); list.Columns.Add("정도", 44); list.Columns.Add("제목", 120); list.Columns.Add("조치", 140);
        list.Resize += (o, e) => list.Columns[3].Width = Math.Max(60, list.ClientSize.Width - list.Columns[0].Width - list.Columns[1].Width - list.Columns[2].Width);
        list.DoubleClick += (o, e) =>
        {
            if (list.SelectedItems.Count == 0 || !(list.SelectedItems[0].Tag is CauseRow r)) return;
            Pending = r;
            AgvPalette.Send("AGVCAUSEGO");
        };
        Show = (rows, text) =>
        {
            caption.Text = rows.Count == 0 ? "원인 없음" : text;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var r in rows)
            {
                var i = r.Item;
                var tip = string.Join(" · ", new[] { i.Where, i.Detail, i.Fix.Length > 0 ? "조치 " + i.Fix : "" }.Where(t => t.Length > 0));
                list.Items.Add(new ListViewItem(new[] { r.Number > 0 ? r.Number.ToString() : "", CauseMarks.SeverityName(i.Severity), i.Title, i.Fix })
                {
                    Tag = r, ToolTipText = tip, ForeColor = i.Severity >= 2 ? Theme.Error : i.Severity == 1 ? Theme.Warning : Theme.Muted,
                });
            }
            list.EndUpdate();
        };

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Panel, Padding = Padding.Empty };
        top.Controls.AddRange(new Control[]
        {
            Theme.Section("서버 분석"), Theme.Make("원인 보기", GlyphKind.Search, "AGVCAUSE"), Theme.Make("작업 고르기", GlyphKind.Send, "AGVCAUSEJOB"),
            Theme.Make("지우기", GlyphKind.Init, "AGVCAUSECLEAR"), caption, Theme.Caption("더블클릭하면 그 위치로 이동"),
        });
        top.Layout += (o, e) =>
        {
            int w = top.ClientSize.Width;
            foreach (Control c in top.Controls) if (!(c is Label)) c.Width = Math.Max(120, w - c.Margin.Horizontal);
        };
        var page = new TabPage("원인") { BackColor = Theme.Panel, Padding = new Padding(8, 4, 8, 8) };
        page.Controls.Add(list); // Fill first: the Top panel then docks before it
        page.Controls.Add(top);
        return page;
    }
}

/// <summary>Picks one finished job from the server's analyses (AGVCAUSE without a remembered job, or "작업 고르기").</summary>
internal sealed class JobPickDialog : Form
{
    private readonly ListBox _list = new ListBox { Dock = DockStyle.Fill, Font = Theme.Base, IntegralHeight = false };
    public string JobId { get; private set; } = "";

    private sealed class Item
    {
        public string Id = "", Text = "";
        public override string ToString() => Text;
    }

    public JobPickDialog(IReadOnlyList<AnalysisSummary> analyses)
    {
        Text = "ASH 원인 — 작업 고르기";
        Font = Theme.Base;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 340);
        MinimizeBox = MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        foreach (var a in analyses)
            foreach (var p in a.Plans)
                foreach (var j in p.Jobs)
                    _list.Items.Add(new Item { Id = j.Id, Text = $"{a.Name} · {p.Name} v{j.Version}{(j.Selected ? " · 선정" : "")}  ({j.Id})" });
        var ok = new Button { Text = "보기", DialogResult = DialogResult.OK, Dock = DockStyle.Right, Width = 80 };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Dock = DockStyle.Right, Width = 80 };
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(0, 4, 0, 0) };
        bar.Controls.Add(cancel); bar.Controls.Add(ok);
        Controls.Add(_list); Controls.Add(bar);
        AcceptButton = ok; CancelButton = cancel;
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        _list.DoubleClick += (o, e) => { if (_list.SelectedItem != null) DialogResult = DialogResult.OK; };
        Theme.ScaleToDpi(this);
    }

    public bool HasJobs => _list.Items.Count > 0;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (DialogResult == DialogResult.OK) JobId = (_list.SelectedItem as Item)?.Id ?? "";
        base.OnClosing(e);
    }
}
