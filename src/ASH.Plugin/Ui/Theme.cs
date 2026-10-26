using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ASH.Core.Layout;
using ASH.Core.Settings;
using ASH.Core.Traffic;

namespace ASH.Plugin.Ui;

/// <summary>Flat palette look: colours, fonts and control factories. Touches no ZWCAD type until a button is clicked.</summary>
public static class Theme
{
    public static readonly Color Accent = ColorTranslator.FromHtml("#2F6FED");
    public static readonly Color Bg = ColorTranslator.FromHtml("#F7F8FA");
    public static readonly Color Panel = Color.White;
    public static readonly Color Text = ColorTranslator.FromHtml("#1F2328");
    public static readonly Color Muted = ColorTranslator.FromHtml("#6B7280");
    public static readonly Color Hover = ColorTranslator.FromHtml("#E8EEFB");
    public static readonly Font Base = new Font("Segoe UI", 9.5f);
    public static readonly Font Small = new Font("Segoe UI", 8.25f, FontStyle.Bold);

    /// <summary>36 px flat button, 20 px glyph left of the text. Empty cmd = caller wires Click itself.</summary>
    public static Button Make(string text, GlyphKind glyph, string cmd)
    {
        var b = new Button
        {
            Text = "  " + text, Height = 36, // leading spaces = gap after the glyph (ImageBeforeText has none)
            Font = Base, ForeColor = Text, BackColor = Panel, Cursor = Cursors.Hand,
            FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, ImageAlign = ContentAlignment.MiddleLeft,
            TextImageRelation = TextImageRelation.ImageBeforeText, Image = Glyphs.Draw(glyph, 20, Accent),
            Padding = new Padding(8, 0, 0, 0), Margin = new Padding(0, 0, 0, 2), UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Hover;
        b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(Hover, 0.05f);
        if (cmd.Length > 0) b.Click += (o, e) => AgvPalette.Send(cmd);
        return b;
    }

    /// <summary>Error text (problem list).</summary>
    public static readonly Color Error = Color.FromArgb(220, 38, 38);

    /// <summary>Warning text (cause findings in the problem list).</summary>
    public static readonly Color Warning = Color.FromArgb(180, 83, 9);

    // this PC's layer colour changes (ash.user.tsv), read once; ReloadLayerColors re-reads them after the colour dialog
    private static IReadOnlyDictionary<string, short> _layerColors = LoadLayerColors();

    /// <summary>Raised after <see cref="ReloadLayerColors"/>: whatever shows a CP colour repaints it.</summary>
    public static event Action? LayerColorsChanged;

    private static IReadOnlyDictionary<string, short> LoadLayerColors()
    {
        try { return LayerColors.Parse(UserSettings.Load(UserSettings.DefaultPath).LayerColors); }
        catch (Exception) { return new Dictionary<string, short>(); } // unreadable file: the defaults
    }

    public static void ReloadLayerColors()
    {
        _layerColors = LoadLayerColors();
        LayerColorsChanged?.Invoke();
    }

    /// <summary>Screen colour of a CP type: the colour of its layer (default or this PC's choice) as AutoCAD would draw it on white. Muted for an unknown type.</summary>
    public static Color CpColor(string type)
    {
        var t = (type ?? "").ToLowerInvariant();
        if (Array.IndexOf(CpTypes.All, t) < 0) return Muted;
        var (r, g, b) = AciColors.OnWhite(LayerColors.Effective(_layerColors, Conventions.LayerCpFor(t)));
        return Color.FromArgb(r, g, b);
    }

    /// <summary>Dialogs: pixel sizes (ClientSize, buttons, columns, margins) follow Windows scaling the way the fonts do. Without it a DPI-aware host (ZWCAD)
    /// draws larger text into the same pixel boxes and clips it. Set the sizes at 96 dpi, call this before them, and <see cref="FitHeight"/> last.</summary>
    public static void ScaleToDpi(Form f)
    {
        f.AutoScaleDimensions = new SizeF(96f, 96f);
        f.AutoScaleMode = AutoScaleMode.Dpi;
    }

    /// <summary>Scales now (so what is measured is in final pixels), then sets the client height to what <paramref name="content"/> needs plus the docked bars, at most
    /// the work area (content scrolls beyond that), and centres the form again. Null content = keep the height, only cap it.
    /// For a measuring dialog call it from the Load event, not the constructor, so the controls are measured with their windows in place.</summary>
    public static void FitHeight(Form f, Control? content)
    {
        f.PerformAutoScale();
        f.PerformLayout();
        int h = f.ClientSize.Height;
        if (content != null)
        {
            int bars = f.Controls.Cast<Control>().Where(c => c != content && (c.Dock == DockStyle.Top || c.Dock == DockStyle.Bottom)).Sum(c => c.Height);
            // the rows as laid out at the real width (the preferred size of a table counts a wrapping label as one line)
            int need = content is TableLayoutPanel t ? t.GetRowHeights().Sum() + t.Padding.Vertical : content.GetPreferredSize(new Size(f.ClientSize.Width, 0)).Height;
            h = need + bars;
        }
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        f.ClientSize = new Size(f.ClientSize.Width, Math.Min(h, wa.Height - (f.Height - f.ClientSize.Height) - 8));
        if (f.StartPosition == FormStartPosition.CenterScreen) f.Location = new Point(wa.Left + (wa.Width - f.Width) / 2, wa.Top + (wa.Height - f.Height) / 2);
    }

    public static Label Section(string text) =>
        new Label { Text = text, Font = Small, ForeColor = Muted, AutoSize = true, Margin = new Padding(2, 12, 0, 4) };

    public static Label Caption(string text) =>
        new Label { Text = text, Font = Base, ForeColor = Muted, AutoSize = true, Margin = new Padding(2, 6, 0, 2) };
}
