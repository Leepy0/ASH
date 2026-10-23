using System;
using System.Globalization;
using System.Text.RegularExpressions;
using ASH.Core.Cel;
using Xunit;

namespace ASH.Tests.Cel;

public class SvgPreviewTests
{
    static Box[] Two() => new[] { new Box("Body", 0, 0, 0, 0.6, 1.2, 0.4), new Box("Lamp", 0, -0.5, 0.4, 0.1, 0.1, 0.1, 1) };

    [Fact] public void Three_Panels_And_All_Boxes()
    {
        var s = SvgPreview.Render(Two());
        Assert.StartsWith("<svg", s);
        Assert.Contains("viewBox=", s);
        Assert.Equal(3, Regex.Matches(s, "<g class=\"panel\"").Count);
        Assert.Equal(3 * 2, Regex.Matches(s, "<rect class=\"box\"").Count + Regex.Matches(s, "<path class=\"box\"").Count);
        Assert.DoesNotContain("http://", s.Replace("http://www.w3.org/2000/svg", ""));
    }

    [Fact] public void Front_Is_Up_In_Top_View()
    {
        var s = SvgPreview.Render(Two());
        var panel = s.Substring(s.IndexOf("<g class=\"panel\"", StringComparison.Ordinal));
        panel = panel.Substring(0, panel.IndexOf("</g>", StringComparison.Ordinal));
        var ys = Regex.Matches(panel, "<rect class=\"box\" x=\"[^\"]+\" y=\"([^\"]+)\" width=\"[^\"]+\" height=\"([^\"]+)\"");
        Assert.Equal(2, ys.Count);
        double Y(int i) => double.Parse(ys[i].Groups[1].Value, CultureInfo.InvariantCulture);
        // draw order is far-to-near by height: Body first, Lamp second; the lamp (front, -y) sits above the body's centre
        double bodyCentre = Y(0) + double.Parse(ys[0].Groups[2].Value, CultureInfo.InvariantCulture) / 2;
        Assert.True(Y(1) < bodyCentre);
    }

    [Fact] public void Empty_List_Still_Renders() => Assert.Contains("상자 없음", SvgPreview.Render(Array.Empty<Box>()));
}
