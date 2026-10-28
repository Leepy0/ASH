using System.Text.RegularExpressions;
using ASH.Server;

namespace ASH.Server.Tests;

public class StampedWriterTests
{
    [Fact]
    public void Every_Line_Starts_With_A_Millisecond_Stamp()
    {
        var sw = new StringWriter();
        var w = new StampedWriter(sw);
        w.WriteLine("시작");
        w.Write("run 1 "); w.Write("ok\nrun 2 ok\n");
        w.WriteLine();
        var lines = sw.ToString().Split('\n');
        Assert.Equal(5, lines.Length); // 3 lines, the empty one, the end
        Assert.All(lines.Take(3), l => Assert.Matches(new Regex(@"^\[\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3}\] "), l));
        Assert.EndsWith("] run 1 ok", lines[1]);
        Assert.Equal("", lines[3]); // an empty line gets no stamp
    }
}
