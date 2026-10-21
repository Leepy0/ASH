using System;
using System.IO;
using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;
using ClosedXML.Graphics;

namespace ASH.Core.Input;

/// <summary>
/// ClosedXML measures text with a font engine (SixLabors.Fonts) that it creates as soon as a workbook is created and that parses the fonts installed on the PC.
/// Inside ZWCAD's process that threw a TypeInitializationException (SixLabors.Fonts.Tables.TableLoader) the moment AGVEXCEL made a workbook.
/// ASH never needs exact text sizes (it writes plain tables and reads cell values), so this engine answers with plain estimates and touches no font.
/// </summary>
internal sealed class FontFreeGraphicEngine : IXLGraphicEngine
{
    private static bool _installed;
    private static readonly object Gate = new object();

    /// <summary>Makes every workbook created from now on use this engine. Call before the first <c>new XLWorkbook</c>; harmless to call again.</summary>
    public static void Install()
    {
        lock (Gate)
        {
            if (_installed) return;
            LoadOptions.DefaultGraphicEngine = new FontFreeGraphicEngine();
            _installed = true;
        }
    }

    private static double PxPerPt(double dpi) => dpi / 72.0;

    // average advance of a Latin letter or digit is about half an em; Korean and other wide characters take a whole em
    private static double Advance(char c, double emPx) => (c >= 0x1100 ? 1.0 : 0.5) * emPx;

    public double GetTextWidth(string text, IXLFontBase font, double dpi)
    {
        double em = font.FontSize * PxPerPt(dpi), w = 0;
        foreach (var c in text ?? "") w += Advance(c, em);
        return w;
    }

    public double GetTextHeight(IXLFontBase font, double dpi) => font.FontSize * PxPerPt(dpi) * 1.22;

    public double GetMaxDigitWidth(IXLFontBase font, double dpi) => Math.Max(1, Math.Round(font.FontSize * PxPerPt(dpi) * 0.48));

    public double GetDescent(IXLFontBase font, double dpi) => font.FontSize * PxPerPt(dpi) * 0.2;

    public GlyphBox GetGlyphBox(ReadOnlySpan<int> graphemeCluster, IXLFontBase font, Dpi dpi)
    {
        float em = (float)(font.FontSize * PxPerPt(dpi.X));
        bool wide = graphemeCluster.Length > 0 && graphemeCluster[0] >= 0x1100;
        return new GlyphBox(em * (wide ? 1f : 0.5f), em, em * 0.2f);
    }

    public XLPictureInfo GetPictureInfo(Stream imageStream, XLPictureFormat expectedFormat)
        => throw new NotSupportedException("ASH는 Excel에 그림을 넣지 않습니다.");
}
