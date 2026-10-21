using System.Linq;
using ASH.Core.Input;
using ClosedXML.Excel;
using ClosedXML.Graphics;

namespace ASH.Tests.Input;

/// <summary>
/// AGVEXCEL threw a TypeInitializationException (SixLabors.Fonts TableLoader) inside ZWCAD: ClosedXML creates its font-parsing text engine
/// the moment a workbook is created. ASH installs an engine that never touches a font.
/// </summary>
public class ExcelTemplateFontTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "ash-tpl-" + Guid.NewGuid().ToString("N") + ".xlsx");

    [Fact]
    public void The_default_font_engine_is_never_created_for_the_template_or_for_reading_it()
    {
        var path = Tmp();
        try
        {
            // another test in this process may have made a plain workbook before ASH's code ran; then nothing can be said about ours
            var alreadyThere = DefaultGraphicEngine.Instance.IsValueCreated;
            ExcelTemplate.Write(path);
            ExcelReader.Read(path, out _);
            if (!alreadyThere) Assert.False(DefaultGraphicEngine.Instance.IsValueCreated, "ClosedXML created its font engine (it parses the PC's fonts)");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_installed_engine_answers_without_fonts()
    {
        FontFreeGraphicEngine.Install();
        FontFreeGraphicEngine.Install(); // twice is harmless
        using var wb = new XLWorkbook();
        var font = wb.AddWorksheet("S").Cell(1, 1).Style.Font;
        var e = new FontFreeGraphicEngine();
        Assert.True(e.GetMaxDigitWidth(font, 96) >= 5);
        Assert.True(e.GetTextWidth("가나다", font, 96) > e.GetTextWidth("abc", font, 96));   // wide characters are wider
        Assert.True(e.GetTextHeight(font, 96) > 0);
        Assert.True(e.GetDescent(font, 96) > 0);
    }

    [Fact]
    public void Columns_are_wide_enough_for_their_text_but_not_absurd()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path);
            using var wb = new XLWorkbook(path);
            foreach (var ws in wb.Worksheets)
                foreach (var c in ws.Row(1).CellsUsed())
                {
                    var w = ws.Column(c.Address.ColumnNumber).Width;
                    Assert.InRange(w, 8, 40);
                    Assert.True(w >= Math.Min(40, c.GetString().Length), $"{ws.Name}!{c.Address}: {w} for '{c.GetString()}'");
                }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(new string[0], 8)]                 // never narrower than 8
    [InlineData(new[] { "abc" }, 8)]
    [InlineData(new[] { "0123456789" }, 13)]       // 10 * 1.1 + 2
    [InlineData(new[] { "가나다라마바사아자차" }, 24)] // Korean counts double: 20 * 1.1 + 2
    [InlineData(new[] { "x", "0123456789012345678901234567890123456789" }, 40)]   // capped
    public void Width_comes_from_the_widest_text(string[] texts, double width) => Assert.Equal(width, ExcelTemplate.ColumnWidth(texts), 6);
}
