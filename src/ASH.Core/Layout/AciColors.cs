namespace ASH.Core.Layout;

/// <summary>AutoCAD colour index (ACI, 1-255) to RGB, so the palette can show a layer's colour without a ZWCAD type.</summary>
public static class AciColors
{
    static readonly int[] Fixed =
    {
        0x000000, 0xFF0000, 0xFFFF00, 0x00FF00, 0x00FFFF, 0x0000FF, 0xFF00FF, 0xFFFFFF, 0x808080, 0xC0C0C0, // 0-9 (0 is ByBlock: black here)
    };
    static readonly int[] Grays = { 0x333333, 0x505050, 0x696969, 0x828282, 0xBEBEBE, 0xFFFFFF }; // 250-255
    static readonly int[] Bright = { 255, 204, 153, 127, 76 }; // value of the five shades of a hue

    /// <summary>The colour AutoCAD draws for an ACI on a dark background. 10-249: 24 hues in steps of 15 degrees, five shades each, odd entries half as saturated; 256 (ByLayer) and 0 are not colours.</summary>
    public static (int R, int G, int B) Rgb(int aci)
    {
        if (aci < 1 || aci > 255) return (0, 0, 0);
        if (aci < 10) return Split(Fixed[aci]);
        if (aci >= 250) return Split(Grays[aci - 250]);
        int h = (aci - 10) / 10, n = (aci - 10) % 10;
        int v = Bright[n / 2];
        double sat = n % 2 == 0 ? 1.0 : 0.5;
        double hue = h * 15.0 / 60.0; // 0..6
        int sector = (int)hue;
        double f = hue - sector;
        double p = v * (1 - sat), q = v * (1 - sat * f), t = v * (1 - sat * (1 - f));
        double r, g, b;
        switch (sector)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
        return ((int)r, (int)g, (int)b);
    }

    /// <summary>The same colour for text and icons on a white panel: white (7, 255) turns black as it does on AutoCAD's white background, other bright colours (yellow, cyan) are darkened to stay readable.</summary>
    public static (int R, int G, int B) OnWhite(int aci)
    {
        if (aci == 7) return (0, 0, 0);
        var (r, g, b) = Rgb(aci);
        double lum = 0.299 * r + 0.587 * g + 0.114 * b, cap = 0.55 * 255;
        if (lum <= cap) return (r, g, b);
        double k = cap / lum;
        return ((int)(r * k), (int)(g * k), (int)(b * k));
    }

    static (int, int, int) Split(int rgb) => ((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
}
