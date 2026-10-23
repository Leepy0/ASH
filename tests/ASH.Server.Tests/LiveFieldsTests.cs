using ASH.Server;
using Xunit;

namespace ASH.Server.Tests;

public class LiveFieldsTests
{
    [Fact] public void Fields_Are_Plain_Text()
    {
        var j = new JobInfo { Id = "20260101-000000-aaaa", State = JobState.Running, Phase = "<b>탐색</b>", RunsDone = 3, ExpectedRuns = 10, LastLine = "a&b" };
        var l = LiveFields.OfJob(j, null, 2);
        Assert.True(l.Active); Assert.Equal("<b>탐색</b>", l.Fields["job.phase"]); Assert.Equal("30", l.Fields["job.bar"]); Assert.Equal("running", l.Fields["job.state"]);
        Assert.Equal("running", l.Watch["20260101-000000-aaaa"]);
        Assert.False(LiveFields.OfJob(new JobInfo { Id = "20260101-000000-bbbb", State = JobState.Done }, null, 2).Active);
    }

    [Fact] public void Poll_Script_Uses_TextContent_Only()
    {
        var s = JobPages.PollScript("/api/home?me=1");
        Assert.Contains("textContent", s); Assert.DoesNotContain("innerHTML", s); Assert.Contains("10000", s); Assert.Contains("60000", s); Assert.Contains("연결 끊김 · 다시 시도 중", s);
        Assert.Contains("prev[id]==='queued'&&w[id]==='running'", s); Assert.Contains("location.reload()", s);
        Assert.Contains("!(id in w)&&(prev[id]==='queued'||prev[id]==='running')) ended=true", s); // a running/queued key that vanished counts as ended
    }
}
