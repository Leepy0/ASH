using ASH.Server;

namespace ASH.Server.Tests;

public class JobProgressTests
{
    [Fact] public void Progress_Never_Exceeds_One_And_Is_Full_Only_When_Done()
    {
        var j = new JobInfo { State = JobState.Running, ExpectedRuns = 10, RunsDone = 10 };
        Assert.Equal(0.99, JobProgress.Ratio(j)!.Value, 3);
        j.RunsDone = 25; Assert.Equal(0.99, JobProgress.Ratio(j)!.Value, 3);
        j.State = JobState.Failed; Assert.Equal(0.99, JobProgress.Ratio(j)!.Value, 3);
        j.State = JobState.Done; Assert.Equal(1.0, JobProgress.Ratio(j));
        Assert.Null(JobProgress.Ratio(new JobInfo { State = JobState.Running }));
        Assert.Equal(0.0, JobProgress.Ratio(new JobInfo { State = JobState.Queued, ExpectedRuns = 5 }));
    }
    [Fact] public void Eta_Uses_Average_Run_Over_Parallel()
    {
        var now = new DateTime(2026, 10, 23, 1, 0, 0, DateTimeKind.Utc);
        var j = new JobInfo { State = JobState.Running, ExpectedRuns = 12, RunsDone = 4, RunSeconds = 480, StartedUtc = now.AddMinutes(-10) };
        Assert.Equal(now.AddMinutes(8), JobProgress.Eta(j, null, 2, now));
        Assert.Equal(TimeSpan.FromMinutes(2), JobProgress.AvgRun(j));
        var fresh = new JobInfo { State = JobState.Running, StartedUtc = now.AddMinutes(-1), ExpectedRuns = 12 };
        Assert.Equal(now.AddMinutes(29), JobProgress.Eta(fresh, TimeSpan.FromMinutes(30), 2, now));
        Assert.Null(JobProgress.Eta(fresh, null, 2, now));
    }
}
