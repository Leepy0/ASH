using ASH.Core.Traffic;
using Xunit;

namespace ASH.Tests.Traffic;

public class CpRouteSequenceTests
{
    [Fact] public void Standby_Advances_By_Itself_After_One()
    {
        var q = new CpRouteSequence();
        Assert.Equal("standby", q.Type);
        Assert.True(q.Placed());
        Assert.Equal("load", q.Type);
    }

    [Fact] public void Middle_Steps_Take_Many_And_Wait_For_Next()
    {
        var q = new CpRouteSequence();
        q.Placed();
        Assert.False(q.Placed()); Assert.False(q.Placed());
        Assert.Equal("load", q.Type);
        q.Next();
        Assert.Equal("unload", q.Type);
        Assert.Equal(2, q.Count("load"));
    }

    [Fact] public void Skip_And_Empty_Next_Leave_The_Step_At_Zero()
    {
        var q = new CpRouteSequence();
        q.Skip();                 // standby left empty
        q.Next(); q.Next();       // load, unload empty
        q.Skip(); q.Skip();       // rtnload, rtnunload
        Assert.Equal("finish", q.Type);
        Assert.False(q.Done);
        Assert.Equal(0, q.Count("rtnload"));
    }

    [Fact] public void Finish_Ends_After_One_Click_And_Ignores_Further_Input()
    {
        var q = new CpRouteSequence();
        for (int i = 0; i < 5; i++) q.Next();
        Assert.True(q.Placed());
        Assert.True(q.Done);
        Assert.False(q.Placed());
        q.Next();
        Assert.Equal(1, q.Count("finish"));
    }

    [Fact] public void Next_On_Finish_Ends()
    {
        var q = new CpRouteSequence();
        for (int i = 0; i < 6; i++) q.Next();
        Assert.True(q.Done);
    }

    [Fact] public void Prompt_And_Summary_Text()
    {
        var q = new CpRouteSequence();
        q.Placed(); q.Placed(); q.Placed();
        Assert.Equal("[2/6] load 로딩 — 클릭: 배치, Enter/Space: 다음, S: 건너뛰기, Esc: 끝", q.Prompt());
        q.Next(); q.Placed(); q.Placed(); q.Next();
        q.Next(); q.Next();
        q.Placed();
        Assert.Equal("G1: standby 1, load 2, unload 2, rtnload 0, rtnunload 0, finish 1", q.Summary("G1"));
    }
}
