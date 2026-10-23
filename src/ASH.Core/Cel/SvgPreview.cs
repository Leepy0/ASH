using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ASH.Core.Cel;

public static class SvgPreview
{
    // shortcut: VehicleGeometry names only 0,1,2,3,4,8,9 (colorwin.h); 5,6,7,10..15 are guessed VGA-style, check against AutoMod.
    static readonly string[] Palette =
    {
        "#000000", "#e03030", "#30b030", "#e8d020", "#3050e0", "#c030c0", "#20c0c0", "#f0f0f0",
        "#b0b0b0", "#707070", "#901818", "#187818", "#182890", "#781878", "#187878", "#505050",
    };

    const double Margin = 14, LabelH = 22, Depth = 0.5, Cos30 = 0.8660254037844386, Sin30 = 0.5;

    static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>One SVG (width x height px) with three panels: top (x→right, front(-y)→up), side (y→right, z→up), isometric (cabinet projection 30°);
    /// boxes drawn far-to-near, fill from the AutoMod colour index, 1 px outline; a 1 m scale bar and an arrow marking the front. No external resources.</summary>
    public static string Render(IReadOnlyList<Box> boxes, int width = 900, int height = 300)
    {
        if (boxes == null) throw new ArgumentNullException(nameof(boxes));
        var sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(width).Append("\" height=\"").Append(height)
          .Append("\" viewBox=\"0 0 ").Append(width).Append(' ').Append(height).Append("\">");
        sb.Append("<rect x=\"0\" y=\"0\" width=\"").Append(width).Append("\" height=\"").Append(height).Append("\" fill=\"#ffffff\"/>");
        double pw = width / 3.0;

        // Per box: polygons in metres with svg-style y (down); Order ascending = far first.
        var panels = new (string Label, bool Rects, Func<Box, double> Order, Func<Box, double[][]> Poly)[]
        {
            ("위", true, b => b.Z0 + b.H, b => Rect(b.X - b.Dx / 2, b.Y - b.Dy / 2, b.X + b.Dx / 2, b.Y + b.Dy / 2)),
            ("옆", true, b => b.X, b => Rect(b.Y - b.Dy / 2, -(b.Z0 + b.H), b.Y + b.Dy / 2, -b.Z0)),
            ("사선", false, b => -b.Y, Iso),
        };
        for (int i = 0; i < 3; i++)
        {
            var p = panels[i];
            sb.Append("<g class=\"panel\" transform=\"translate(").Append(N(i * pw)).Append(",0)\">");
            sb.Append("<text x=\"").Append(N(Margin)).Append("\" y=\"16\" font-size=\"14\" font-family=\"sans-serif\" fill=\"#333333\">").Append(p.Label).Append("</text>");
            var items = boxes.OrderBy(p.Order).Select(b => (b, polys: p.Poly(b))).ToList();
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (var (_, polys) in items)
                foreach (var poly in polys)
                    for (int k = 0; k < poly.Length; k += 2)
                    {
                        x0 = Math.Min(x0, poly[k]); x1 = Math.Max(x1, poly[k]); y0 = Math.Min(y0, poly[k + 1]); y1 = Math.Max(y1, poly[k + 1]);
                    }
            if (items.Count == 0) { x0 = y0 = 0; x1 = y1 = 1; }
            double w = Math.Max(x1 - x0, 1e-9), h = Math.Max(y1 - y0, 1e-9);
            double availW = pw - 2 * Margin, availH = height - LabelH - 2 * Margin - 10;
            double s = Math.Min(availW / w, availH / h);
            double offX = Margin + (availW - w * s) / 2 - x0 * s, offY = LabelH + Margin + (availH - h * s) / 2 - y0 * s;
            string X(double v) => N(v * s + offX);
            string Y(double v) => N(v * s + offY);
            foreach (var (b, polys) in items)
            {
                string fill = Palette[((b.Color % 16) + 16) % 16];
                if (p.Rects)
                {
                    var r = polys[0];
                    sb.Append("<rect class=\"box\" x=\"").Append(X(r[0])).Append("\" y=\"").Append(Y(r[1])).Append("\" width=\"").Append(N((r[2] - r[0]) * s))
                      .Append("\" height=\"").Append(N((r[5] - r[1]) * s)).Append("\" fill=\"").Append(fill).Append("\" stroke=\"#000000\" stroke-width=\"1\"/>");
                }
                else
                {
                    sb.Append("<path class=\"box\" d=\"");
                    foreach (var poly in polys)
                    {
                        for (int k = 0; k < poly.Length; k += 2) sb.Append(k == 0 ? "M" : "L").Append(X(poly[k])).Append(' ').Append(Y(poly[k + 1]));
                        sb.Append('Z');
                    }
                    sb.Append("\" fill=\"").Append(fill).Append("\" stroke=\"#000000\" stroke-width=\"1\" stroke-linejoin=\"round\"/>");
                }
            }
            if (items.Count == 0)
                sb.Append("<text x=\"").Append(N(pw / 2)).Append("\" y=\"").Append(N(height / 2.0)).Append("\" font-size=\"13\" text-anchor=\"middle\" font-family=\"sans-serif\" fill=\"#888888\">상자 없음</text>");
            double by = height - 10;   // 1 m scale bar, bottom-left
            sb.Append("<path d=\"M").Append(N(Margin)).Append(' ').Append(N(by)).Append('h').Append(N(s)).Append("\" fill=\"none\" stroke=\"#000000\" stroke-width=\"2\"/>")
              .Append("<text x=\"").Append(N(Margin + s + 4)).Append("\" y=\"").Append(N(by + 4)).Append("\" font-size=\"10\" font-family=\"sans-serif\" fill=\"#333333\">1 m</text>");
            if (i == 0)   // front is up in the top view
                sb.Append("<path d=\"M").Append(N(pw - 24)).Append(" 26V10M").Append(N(pw - 24)).Append(" 6l-4 7h8z\" fill=\"#000000\" stroke=\"#000000\" stroke-width=\"1\"/>")
                  .Append("<text x=\"").Append(N(pw - 42)).Append("\" y=\"20\" font-size=\"11\" font-family=\"sans-serif\" fill=\"#333333\">앞</text>");
            sb.Append("</g>");
        }
        return sb.Append("</svg>").ToString();
    }

    static double[][] Rect(double x0, double y0, double x1, double y1) => new[] { new[] { x0, y0, x1, y0, x1, y1, x0, y1 } };

    // Cabinet: x right, z up, depth y recedes up-right (rear = +y is farther); the near face is the front (-y).
    static double[][] Iso(Box b)
    {
        double x0 = b.X - b.Dx / 2, x1 = b.X + b.Dx / 2, yn = b.Y - b.Dy / 2, yf = b.Y + b.Dy / 2, z0 = b.Z0, z1 = b.Z0 + b.H;
        double[] P(double x, double y, double z) => new[] { x + Depth * y * Cos30, -(z + Depth * y * Sin30) };
        double[] Poly(params double[][] pts) => pts.SelectMany(q => q).ToArray();
        return new[]
        {
            Poly(P(x0, yn, z0), P(x1, yn, z0), P(x1, yn, z1), P(x0, yn, z1)),
            Poly(P(x0, yn, z1), P(x1, yn, z1), P(x1, yf, z1), P(x0, yf, z1)),
            Poly(P(x1, yn, z0), P(x1, yf, z0), P(x1, yf, z1), P(x1, yn, z1)),
        };
    }
}
