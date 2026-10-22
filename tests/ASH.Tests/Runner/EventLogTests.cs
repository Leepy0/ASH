using ASH.Core.Runner;

namespace ASH.Tests.Runner;

public class EventLogTests
{
    [Fact]
    public void EventLog_Read_Parses_And_Skips_Partial_Line()
    {
        var f = Path.GetTempFileName();
        try
        {
            File.WriteAllText(f, "t\tkind\tmat\tcp\tvalue\n3700.5\tcall\tM1\t\t2\n3800\tdeliver\tM1\tU001\t123.25\n3900\twait\tM1\tB0");
            var ev = EventLog.Read(f);
            Assert.Equal(2, ev.Count);
            Assert.Equal(new SimEvent(3700.5, "call", "M1", "", 2), ev[0]);
            Assert.Equal(new SimEvent(3800, "deliver", "M1", "U001", 123.25), ev[1]);
            File.WriteAllText(f, "t\tkind\tmat\tcp\tvalue\n1\tcall\tM1\t\t2\n2\tpick\tM1\tL001\t5\n");
            Assert.Equal(2, EventLog.Read(f).Count);
            Assert.Empty(EventLog.Read(f + ".none"));
        }
        finally { File.Delete(f); }
    }
}
