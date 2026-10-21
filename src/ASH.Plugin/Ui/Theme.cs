using System.Drawing;
using System.Windows.Forms;

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

    /// <summary>Screen colour of a CP type, matching the AutoCAD colour of its layer: red traffic, green load, blue unload, orange rtnload, magenta rtnunload, gold standby, teal buffer.</summary>
    public static Color CpColor(string type)
    {
        switch ((type ?? "").ToLowerInvariant())
        {
            case "traffic": return Color.FromArgb(220, 38, 38);
            case "load": return Color.FromArgb(22, 163, 74);
            case "unload": return Color.FromArgb(37, 99, 235);
            case "rtnload": return Color.FromArgb(234, 88, 12);
            case "rtnunload": return Color.FromArgb(192, 38, 211);
            case "standby": return Color.FromArgb(202, 138, 4);
            case "buffer": return Color.FromArgb(8, 145, 178);
            default: return Muted;
        }
    }

    public static Label Section(string text) =>
        new Label { Text = text, Font = Small, ForeColor = Muted, AutoSize = true, Margin = new Padding(2, 12, 0, 4) };

    public static Label Caption(string text) =>
        new Label { Text = text, Font = Base, ForeColor = Muted, AutoSize = true, Margin = new Padding(2, 6, 0, 2) };
}
