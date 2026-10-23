using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ASH.Plugin.Ui;

public enum GlyphKind { Init, Path, Reverse, Offset, Array, Split, Join, Fillet, Cp, Block, Traffic, Check, Export, Settings, Excel, Model, Update, PathType, ArrowSize, Search, Send, BlockNum, Run, Convert }

/// <summary>Single-colour line icons drawn in code (no image files). Coordinates are in a unit square scaled to px.</summary>
public static class Glyphs
{
    public static Bitmap Draw(GlyphKind kind, int px, Color color)
    {
        var bmp = new Bitmap(px, px, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(px, px);
        using var pen = new Pen(color, Math.Max(1.5f / px, 0.085f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var fill = new SolidBrush(color);
        switch (kind)
        {
            case GlyphKind.Init:
                g.DrawRectangle(pen, 0.15f, 0.15f, 0.7f, 0.7f);
                for (int i = 1; i < 3; i++)
                {
                    float v = 0.15f + 0.7f * i / 3;
                    g.DrawLine(pen, v, 0.15f, v, 0.85f);
                    g.DrawLine(pen, 0.15f, v, 0.85f, v);
                }
                break;
            case GlyphKind.Path:
                g.DrawLines(pen, new[] { P(0.12f, 0.82f), P(0.38f, 0.38f), P(0.6f, 0.62f), P(0.86f, 0.18f) });
                Head(g, pen, P(0.6f, 0.62f), P(0.86f, 0.18f));
                break;
            case GlyphKind.Convert: // a drawn line (grip squares at its ends) that gains a direction head in the middle
                g.DrawLine(pen, 0.2f, 0.8f, 0.8f, 0.2f);
                foreach (var q in new[] { P(0.2f, 0.8f), P(0.8f, 0.2f) }) g.FillRectangle(fill, q.X - 0.1f, q.Y - 0.1f, 0.2f, 0.2f);
                Head(g, pen, P(0.3f, 0.7f), P(0.56f, 0.44f));
                break;
            case GlyphKind.Reverse:
                Arrow(g, pen, P(0.15f, 0.33f), P(0.85f, 0.33f));
                Arrow(g, pen, P(0.85f, 0.67f), P(0.15f, 0.67f));
                break;
            case GlyphKind.PathType:
                g.DrawLine(pen, 0.12f, 0.5f, 0.88f, 0.5f);
                Head(g, pen, P(0.35f, 0.5f), P(0.12f, 0.5f));
                Head(g, pen, P(0.65f, 0.5f), P(0.88f, 0.5f));
                break;
            case GlyphKind.Send:
                Arrow(g, pen, P(0.5f, 0.68f), P(0.5f, 0.12f));
                g.DrawLines(pen, new[] { P(0.15f, 0.55f), P(0.15f, 0.88f), P(0.85f, 0.88f), P(0.85f, 0.55f) });
                break;
            case GlyphKind.Search:
                g.DrawEllipse(pen, 0.14f, 0.14f, 0.5f, 0.5f);
                g.DrawLine(pen, 0.56f, 0.56f, 0.86f, 0.86f);
                break;
            case GlyphKind.ArrowSize:
                g.DrawLine(pen, 0.12f, 0.62f, 0.88f, 0.62f);
                Head(g, pen, P(0.6f, 0.62f), P(0.88f, 0.62f));
                g.DrawLine(pen, 0.12f, 0.26f, 0.12f, 0.46f);   // a length mark above the arrow
                g.DrawLine(pen, 0.88f, 0.26f, 0.88f, 0.46f);
                g.DrawLine(pen, 0.12f, 0.36f, 0.88f, 0.36f);
                break;
            case GlyphKind.Offset:
                g.DrawLine(pen, 0.15f, 0.62f, 0.62f, 0.15f);
                g.DrawLine(pen, 0.38f, 0.85f, 0.85f, 0.38f);
                break;
            case GlyphKind.Array:
                foreach (var x in new[] { 0.2f, 0.5f, 0.8f }) g.FillEllipse(fill, x - 0.1f, 0.4f, 0.2f, 0.2f);
                break;
            case GlyphKind.Split:
                g.DrawLine(pen, 0.15f, 0.15f, 0.85f, 0.85f);
                g.DrawLine(pen, 0.85f, 0.15f, 0.6f, 0.4f); // gap at the crossing = the cut
                g.DrawLine(pen, 0.4f, 0.6f, 0.15f, 0.85f);
                break;
            case GlyphKind.Join:
                g.DrawLines(pen, new[] { P(0.12f, 0.7f), P(0.48f, 0.7f), P(0.86f, 0.25f) });
                g.FillEllipse(fill, 0.39f, 0.61f, 0.18f, 0.18f);
                break;
            case GlyphKind.Fillet:
                using (var gp = new GraphicsPath())
                {
                    gp.AddLine(0.2f, 0.86f, 0.2f, 0.55f);
                    gp.AddArc(0.2f, 0.2f, 0.7f, 0.7f, 180, 90);
                    gp.AddLine(0.55f, 0.2f, 0.86f, 0.2f);
                    g.DrawPath(pen, gp);
                }
                break;
            case GlyphKind.Cp:
                g.DrawEllipse(pen, 0.18f, 0.18f, 0.64f, 0.64f);
                g.FillEllipse(fill, 0.41f, 0.41f, 0.18f, 0.18f);
                break;
            case GlyphKind.Block:
                g.DrawRectangle(pen, 0.2f, 0.2f, 0.6f, 0.6f);
                break;
            case GlyphKind.BlockNum:
                g.DrawRectangle(pen, 0.15f, 0.15f, 0.7f, 0.7f);
                foreach (var v in new[] { 0.42f, 0.58f }) // a '#' inside the block
                {
                    g.DrawLine(pen, v, 0.3f, v, 0.7f);
                    g.DrawLine(pen, 0.3f, v, 0.7f, v);
                }
                break;
            case GlyphKind.Traffic:
                g.DrawRectangle(pen, 0.15f, 0.15f, 0.7f, 0.7f);
                Arrow(g, pen, P(0.3f, 0.5f), P(0.7f, 0.5f));
                break;
            case GlyphKind.Check:
                g.DrawLines(pen, new[] { P(0.18f, 0.52f), P(0.42f, 0.76f), P(0.84f, 0.26f) });
                break;
            case GlyphKind.Export:
                g.DrawLines(pen, new[] { P(0.32f, 0.45f), P(0.15f, 0.45f), P(0.15f, 0.85f), P(0.85f, 0.85f), P(0.85f, 0.45f), P(0.68f, 0.45f) });
                Arrow(g, pen, P(0.5f, 0.68f), P(0.5f, 0.12f));
                break;
            case GlyphKind.Excel: // grid with an X
                g.DrawRectangle(pen, 0.15f, 0.15f, 0.7f, 0.7f);
                g.DrawLine(pen, 0.15f, 0.38f, 0.85f, 0.38f);
                g.DrawLine(pen, 0.38f, 0.38f, 0.38f, 0.85f);
                g.DrawLine(pen, 0.5f, 0.5f, 0.76f, 0.76f);
                g.DrawLine(pen, 0.76f, 0.5f, 0.5f, 0.76f);
                break;
            case GlyphKind.Model: // box with a gear dot
                g.DrawRectangle(pen, 0.15f, 0.15f, 0.7f, 0.7f);
                g.DrawEllipse(pen, 0.33f, 0.33f, 0.34f, 0.34f);
                g.FillEllipse(fill, 0.45f, 0.45f, 0.1f, 0.1f);
                break;
            case GlyphKind.Run: // play triangle
                g.DrawPolygon(pen, new[] { P(0.28f, 0.18f), P(0.82f, 0.5f), P(0.28f, 0.82f) });
                break;
            case GlyphKind.Update: // circular arrow: ~290 degree arc with a head at its end
                g.DrawArc(pen, 0.2f, 0.2f, 0.6f, 0.6f, -60, 290);
                Head(g, pen, OnCircle(200), OnCircle(230));
                break;
            case GlyphKind.Settings: // gear: ring + 4 ticks + hub
                g.DrawEllipse(pen, 0.27f, 0.27f, 0.46f, 0.46f);
                for (int i = 0; i < 4; i++)
                {
                    double a = i * Math.PI / 2 + Math.PI / 4;
                    g.DrawLine(pen, P(0.5f + (float)(0.23 * Math.Cos(a)), 0.5f + (float)(0.23 * Math.Sin(a))), P(0.5f + (float)(0.37 * Math.Cos(a)), 0.5f + (float)(0.37 * Math.Sin(a))));
                }
                g.FillEllipse(fill, 0.43f, 0.43f, 0.14f, 0.14f);
                break;
        }
        return bmp;
    }

    private static PointF P(float x, float y) => new PointF(x, y);

    // point on the Update glyph's circle (centre 0.5, radius 0.3) at a GDI+ angle (degrees, clockwise from +x)
    private static PointF OnCircle(double deg) => P(0.5f + (float)(0.3 * Math.Cos(deg * Math.PI / 180)), 0.5f + (float)(0.3 * Math.Sin(deg * Math.PI / 180)));

    private static void Arrow(Graphics g, Pen pen, PointF a, PointF b)
    {
        g.DrawLine(pen, a, b);
        Head(g, pen, a, b);
    }

    // two 0.18-long strokes at +-30 degrees back from the tip b
    private static void Head(Graphics g, Pen pen, PointF a, PointF b)
    {
        double ang = Math.Atan2(a.Y - b.Y, a.X - b.X);
        foreach (var d in new[] { -0.52, 0.52 })
            g.DrawLine(pen, b, P(b.X + (float)(0.18 * Math.Cos(ang + d)), b.Y + (float)(0.18 * Math.Sin(ang + d))));
    }
}
