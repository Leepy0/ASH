using ASH.Core.Input;

namespace ASH.Tests.Input;

/// <summary>The grid's clipboard text (copy/paste of AGVINPUT): Excel's tab-separated form and where a pasted block lands.</summary>
public class GridTextTests
{
    [Fact]
    public void Format_Is_Tab_And_CrLf_Separated_And_Quotes_What_Needs_It()
    {
        var text = GridText.Format(new[] { new[] { "a", "b" }, new[] { "x\ty", "say \"hi\"", "l1\nl2" } });
        Assert.Equal("a\tb\r\n\"x\ty\"\t\"say \"\"hi\"\"\"\t\"l1\nl2\"\r\n", text);
    }

    [Theory]
    [InlineData("a\tb\r\nc\td\r\n")]
    [InlineData("a\tb\nc\td")]
    [InlineData("a\tb\rc\td\r")]
    public void Parse_Takes_Any_Line_Break_And_One_Final_Break_Adds_No_Row(string text)
    {
        var rows = GridText.Parse(text);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "a", "b" }, rows[0]);
        Assert.Equal(new[] { "c", "d" }, rows[1]);
    }

    [Fact]
    public void Parse_Keeps_Empty_Cells_Quoted_Cells_And_Gives_Nothing_For_Empty_Text()
    {
        Assert.Empty(GridText.Parse(""));
        var rows = GridText.Parse("\t\"x\ty\"\t\"say \"\"hi\"\"\"\t\r\n\r\nlast");
        Assert.Equal(new[] { "", "x\ty", "say \"hi\"", "" }, rows[0]);
        Assert.Equal(new[] { "" }, rows[1]);                 // an empty line is an empty row
        Assert.Equal(new[] { "last" }, rows[2]);
        Assert.Single(GridText.Parse("one"));
    }

    [Fact]
    public void Format_Then_Parse_Gives_The_Cells_Back()
    {
        var cells = new[] { new[] { "", "a\tb", "q\"" }, new[] { "1.5", "multi\r\nline", "" } };
        Assert.Equal(cells, GridText.Parse(GridText.Format(cells)));
    }

    [Fact]
    public void Plan_Starts_At_The_Cell_Skips_Read_Only_Columns_Drops_The_Overflow_And_Says_How_Many_Rows_Are_Needed()
    {
        // visible columns 0,2,3,5 (1 and 4 hidden); 2 is read-only (Auto)
        var cols = new[] { 0, 2, 3, 5 };
        var data = new[] { new[] { "a", "b", "c" }, new[] { "d", "e", "f" } };
        var (cells, rows, skipped) = GridText.Plan(data, startRow: 4, startPos: 1, cols, c => c != 2);

        Assert.Equal(new[] { new GridText.PasteCell(4, 3, "b"), new GridText.PasteCell(4, 5, "c"), new GridText.PasteCell(5, 3, "e"), new GridText.PasteCell(5, 5, "f") }, cells);
        Assert.Equal(6, rows);                                // rows 4 and 5 -> the grid needs 6
        Assert.Equal(2, skipped);                             // the values for column 2

        var (over, _, dropped) = GridText.Plan(new[] { new[] { "x", "y", "z" } }, 0, 3, cols, _ => true);
        Assert.Equal(new[] { new GridText.PasteCell(0, 5, "x") }, over);
        Assert.Equal(2, dropped);
    }
}
