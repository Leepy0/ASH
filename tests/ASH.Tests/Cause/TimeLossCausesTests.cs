using ASH.Core.Cause;
using ASH.Core.Input;
using ASH.Core.Runner;

namespace ASH.Tests.Cause;

public class TimeLossCausesTests
{
    static string W(string body) { var p = Path.Combine(Path.GetTempPath(), "ash_tl_" + Guid.NewGuid().ToString("N") + ".tsv"); File.WriteAllText(p, body); return p; }
    const string H = "kind\tname\tgroup\tvalue1\tvalue2\tvalue3\tvalue4\tvalue5\tvalue6\tvalue7\tvalue8\tvalue9\tvalue10\tvalue11\n";

    static CauseContext Ctx(IReadOnlyList<BlockResult> blocks, params GroupTime[] times) =>
        new(new ASH.Core.Layout.Layout(), new ProjectInput(), new ResultSet(new List<MaterialResult>(), new List<GroupResult>(), blocks, 0, 3600,
            new List<ResultRow>(), GroupTimes: times));

    static GroupTime T(string g, int jobs, double job, double free, double stop, double hand, double turn, Dictionary<string, int>? claims = null) =>
        new(g, jobs, job, free, stop, hand, turn, claims ?? new Dictionary<string, int>());

    [Fact]
    public void Parse_Reads_Tsplit_And_Its_Block_Claims()
    {
        var r = ResultParser.Parse(W(H + "tsplit\tG1\tG1\t40\t7269.6\t1600\t2705\t800\t1120\ntblock\tB_x1\tG1\t43\ntblock\tB_top\tG1\t43\n" +
                                     "tsplit\tG2\tG2\t0\t0\t0\t0\t0\t0\nblock\tB_top\t\t45\t26.66\t0\t0.95\nclock\tclock\t\t0\t3600\n"));
        Assert.Equal(new[] { "G1", "G2" }, r.GroupTimes!.Select(x => x.Group));
        var g1 = r.GroupTimes![0];
        Assert.Equal((40, 7269.6, 1600.0, 2705.0, 800.0, 1120.0), (g1.Jobs, g1.JobSec, g1.FreeSec, g1.StopSec, g1.HandlingSec, g1.TurnSec));
        Assert.Equal(new Dictionary<string, int> { ["B_x1"] = 43, ["B_top"] = 43 }, g1.BlockClaims);
        Assert.Empty(r.GroupTimes![1].BlockClaims);
        // G2 had no job after warmup: no row
        var b = Assert.Single(TimeLossCauses.Breakdown(new CauseContext(new ASH.Core.Layout.Layout(), new ProjectInput(), r)));
        Assert.Equal(43 * 26.66 / 40, b.BlockWaitSec, 9);
        Assert.Equal(2705.0 / 40 - b.BlockWaitSec, b.QueueSec, 9);
    }

    [Fact]
    public void Old_Results_Without_Time_Rows_Give_Nothing()
    {
        var r = ResultParser.Parse(Path.Combine(AppContext.BaseDirectory, "Fixtures", "runner", "result.tsv"));
        Assert.Null(r.GroupTimes);
        var c = new CauseContext(new ASH.Core.Layout.Layout(), new ProjectInput(), r);
        Assert.Empty(TimeLossCauses.Breakdown(c));
        Assert.Empty(TimeLossCauses.Find(c));
        Assert.Empty(TimeLossCauses.Breakdown(c with { Result = null }));
        Assert.Empty(TimeLossCauses.Find(c with { Result = null }));
    }

    [Fact]
    public void Breakdown_Splits_A_Job_And_Find_Names_The_Worst_Block()
    {
        // 10 jobs of 690 s: driving 300 s at free speed + 30 s turning, standing 310 s of which 50 claims x 50 s at B5 (= 250 s per job)
        var blocks = new List<BlockResult> { new("B5", 60, 50, 90, 0.5), new("B7", 30, 4, 10, 0.1), new("B9", 10, 80, 90, 0.1) };
        var c = Ctx(blocks, T("G1", 10, 6900, 3000, 3100, 0, 300, new Dictionary<string, int> { ["B5"] = 50, ["B7"] = 25 }));
        var b = Assert.Single(TimeLossCauses.Breakdown(c));
        Assert.Equal(("G1", 10, 690.0, 330.0), (b.Group, b.Jobs, b.JobSec, b.PureSec));
        Assert.Equal(260, b.BlockWaitSec, 9); // B5 50 x 50 + B7 25 x 4 over 10 jobs
        Assert.Equal(50, b.QueueSec, 9);
        Assert.Equal(50, b.OtherSec, 9);
        Assert.Equal(b.JobSec, b.PureSec + b.BlockWaitSec + b.QueueSec + b.HandlingSec + b.OtherSec, 9);
        var f = Assert.Single(TimeLossCauses.Find(c));
        Assert.Equal((CauseKinds.TimeLoss, 2, "G1 정체 52%", "G1"), (f.Kind, f.Severity, f.Title, f.Where));
        Assert.Equal("작업 1회 690초 · 주행 330초 · block 대기 260초 · CP 대기 50초", f.Detail);
        Assert.Equal("B5 block 점검 · 1회 대기 50초", f.Fix); // B9 waits longer but this group never claims it
    }

    [Fact]
    public void Find_Leaves_Out_Handling_And_Grades_By_The_Ratio()
    {
        var none = new List<BlockResult>();
        // 100 s driving + 100 s handling + 20 s queue: the travel is 1.2x the driving, nothing to report
        Assert.Empty(TimeLossCauses.Find(Ctx(none, T("G1", 1, 220, 100, 20, 100, 0))));
        // 1.6x: 주의; queues outweigh block waits, so no block to name
        var f = Assert.Single(TimeLossCauses.Find(Ctx(none, T("G1", 2, 2 * 210, 2 * 100, 2 * 60, 2 * 50, 0))));
        Assert.Equal((1, "G1 정체 29%", ""), (f.Severity, f.Title, f.Fix));
        Assert.EndsWith("CP 대기 60초 · 적재·하역 50초", f.Detail);
        // the block wait never exceeds the measured standing
        var b = Assert.Single(TimeLossCauses.Breakdown(Ctx(new List<BlockResult> { new("B1", 5, 100, 100, 1) }, T("G1", 1, 200, 100, 30, 0, 0, new Dictionary<string, int> { ["B1"] = 1 }))));
        Assert.Equal((30.0, 0.0), (b.BlockWaitSec, b.QueueSec));
    }
}
