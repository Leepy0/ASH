using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using ASH.Core.Update;

namespace ASH.Plugin.Ui;

/// <summary>What the user chose in <see cref="NoticeDialog"/>. Closing the window with the X counts as Later.</summary>
public enum NoticeChoice { Later, Ok, Update }

/// <summary>"ASH 공지": the notices in one scrollable read-only list, a "v0.31.0 · 제목" header and "•" lines per notice (NoticeFeed.Sections).
/// Touches no ZWCAD type; the caller marks things seen and starts the update.</summary>
public sealed class NoticeDialog : Form
{
    public NoticeChoice Choice { get; private set; } = NoticeChoice.Later;

    /// <param name="canUpdate">The server has a newer version than this PC: shows "지금 업데이트".</param>
    /// <param name="viewAll">AGVNOTICE (everything, seen or not): the close button reads "닫기" instead of "나중에".</param>
    public NoticeDialog(IEnumerable<Notice> notices, bool canUpdate, bool viewAll)
    {
        Text = "ASH 공지";
        Font = Theme.Base;
        ForeColor = Theme.Text;
        BackColor = Theme.Panel;
        Theme.ScaleToDpi(this);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 460); // 96 dpi; the list scrolls, FitHeight below only keeps it on the screen

        var list = new RichTextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Panel, Font = Theme.Base,
            ScrollBars = RichTextBoxScrollBars.Vertical, DetectUrls = false, TabStop = false,
        };
        var holder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 8, 8), BackColor = Theme.Panel };
        holder.Controls.Add(list);
        bool first = true;
        foreach (var (header, bullets) in NoticeFeed.Sections(notices))
        {
            if (!first) list.AppendText("\r\n");
            first = false;
            list.SelectionFont = new Font(Theme.Base, FontStyle.Bold);
            list.AppendText(header + "\r\n");
            list.SelectionFont = Theme.Base;
            foreach (var b in bullets) list.AppendText(b + "\r\n");
        }
        list.Select(0, 0);

        var ok = Flat("확인", 88, Theme.Accent, Color.White);
        ok.Click += (o, e) => { Choice = NoticeChoice.Ok; DialogResult = DialogResult.OK; };
        var update = Flat("지금 업데이트", 120, Theme.Panel, Theme.Text);
        update.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
        update.FlatAppearance.BorderSize = 1;
        update.Visible = canUpdate;
        update.Click += (o, e) => { Choice = NoticeChoice.Update; DialogResult = DialogResult.Yes; };
        var later = Flat(viewAll ? "닫기" : "나중에", 88, Theme.Panel, Theme.Text);
        later.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
        later.FlatAppearance.BorderSize = 1;
        later.DialogResult = DialogResult.Cancel;

        var bar = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Theme.Bg };
        bar.Controls.AddRange(new Control[] { ok, update, later });
        bar.Layout += (o, e) =>
        {
            int y = (bar.ClientSize.Height - ok.Height) / 2;
            later.Location = new Point(bar.ClientSize.Width - 16 - later.Width, y);
            int x = later.Left;
            if (update.Visible) { update.Location = new Point(x - 8 - update.Width, y); x = update.Left; }
            ok.Location = new Point(x - 8 - ok.Width, y);
        };
        AcceptButton = ok;
        CancelButton = later;
        Controls.Add(holder); // Fill first, then the bottom bar docks before it
        Controls.Add(bar);
        Theme.FitHeight(this, null);
    }

    private static Button Flat(string text, int width, Color back, Color fore)
    {
        var b = new Button { Text = text, Size = new Size(width, 32), FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = fore, Font = Theme.Base, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }
}
