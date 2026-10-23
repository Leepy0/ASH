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

    static Box[] Five() => new[]
    {
        new Box("Body", 0, 0, 0, 0.6, 1.2, 0.4),
        new Box("Hook", 0, -1.2, 0, 0.1, 0.1, 0.1),      // in front of the body
        new Box("Right", 0.9, 0, 0, 0.1, 0.1, 0.1),      // to the right
        new Box("Roof", 0, 0, 0.4, 0.3, 0.3, 0.3),       // above the body
        new Box("Rear", 0, 1.2, 0, 0.1, 0.1, 0.1),       // behind the body
    };

    static string Panel(string svg, int n)
    {
        var parts = svg.Split(new[] { "<g class=\"panel\"" }, StringSplitOptions.None);
        return parts[n + 1];
    }

    // name-independent: boxes carry no id, so find by draw order via the tuple of attributes
    static double[] Rects(string panel, int col)
    {
        var ms = Regex.Matches(panel, "<rect class=\"box\" x=\"([^\"]+)\" y=\"([^\"]+)\" width=\"([^\"]+)\" height=\"([^\"]+)\"");
        var r = new double[ms.Count];
        for (int i = 0; i < r.Length; i++) r[i] = double.Parse(ms[i].Groups[col].Value, CultureInfo.InvariantCulture);
        return r;
    }

    [Fact] public void Top_View_Front_Up_Right_Right()
    {
        var p = Panel(SvgPreview.Render(Five()), 0);
        // top-view draw order (height asc): Hook/Right/Rear (0.1), Body (0.4), Roof (0.7) -> stable by input: Hook, Right, Rear, Body, Roof
        var x = Rects(p, 1); var y = Rects(p, 2);
        Assert.True(y[0] < y[3]);   // hook above body top
        Assert.True(y[4] > y[0] && y[0] < y[2]);   // hook above rear
        Assert.True(x[1] > x[3]);   // right box right of body
    }

    [Fact] public void Side_View_Z_Up_Front_Left()
    {
        var p = Panel(SvgPreview.Render(Five()), 1);
        // side order (x asc, stable): Body, Hook, Roof, Rear (x=0), Right (0.9)
        var x = Rects(p, 1); var y = Rects(p, 2);
        Assert.True(y[2] < y[0]);   // roof above body
        Assert.True(x[1] < x[0]);   // hook (front) left of body
        Assert.True(x[3] > x[0]);   // rear right of body
        Assert.Contains("앞", p);
    }

    [Fact] public void Iso_Far_Box_Drawn_First()
    {
        var p = Panel(SvgPreview.Render(new[] { new Box("Near", 0, -1, 0, 0.5, 0.5, 0.5, 1), new Box("Far", 0, 1, 0, 0.5, 0.5, 0.5, 2) }), 2);
        // fill colours 2 (green) = far, 1 (red) = near
        Assert.True(p.IndexOf("#30b030", StringComparison.Ordinal) < p.IndexOf("#e03030", StringComparison.Ordinal));
    }

    [Fact] public void Tiny_Model_Has_No_NaN_And_Scale_Bar_Fits()
    {
        var s = SvgPreview.Render(new[] { new Box("A", 0, 0, 0, 0.001, 0.001, 0.001), new Box("B", 0.001, 0, 0, 0.001, 0.001, 0.001) });
        Assert.DoesNotContain("NaN", s);
        Assert.DoesNotContain("Infinity", s);
        Assert.DoesNotContain("1 m", s);   // 1 m would overflow the panel: bar is hidden
    }

    [Fact] public void Empty_List_Still_Renders() => Assert.Contains("상자 없음", SvgPreview.Render(Array.Empty<Box>()));
}
