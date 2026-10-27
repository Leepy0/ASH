using System.IO;
using System.Linq;
using ASH.Core.Analysis;
using ASH.Core.Cause;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using ASH.Core.Runner;
using Xunit;

namespace ASH.Tests.Cause;

public class CauseMarksTests
{
    static CauseInfo Item(string title, double? x = null, double? y = null, int sev = 1, params string[] refs) =>
        new() { Kind = CauseKinds.BlockHold, Severity = sev, Title = title, X = x, Y = y, Refs = refs.ToList() };

    [Fact] public void Plan_Numbers_Only_Items_With_A_Place_And_Converts_Metres_Back_To_Drawing_Units()
    {
        // mm drawing, Origin (1000, 2000): the model metre (4, 0) is the drawing point (5000, 2000) (CauseFrameTests: CP1 at 5000,2000 -> 4, 0)
        var items = new[] { Item("a", 4, 0), Item("no place"), Item("nan", double.NaN, 1), Item("b", 10, 5) };
        var marks = CauseMarks.Plan(items, new Point2(1000, 2000), 0.001);
        Assert.Equal(new[] { 1, 2 }, marks.Select(m => m.Number));
        Assert.Equal(5000, marks[0].At.X, 6); Assert.Equal(2000, marks[0].At.Y, 6);
        Assert.Equal(11000, marks[1].At.X, 6); Assert.Equal(7000, marks[1].At.Y, 6);
        Assert.Same(items[3], marks[1].Item);
    }

    [Fact] public void Plan_Is_The_Inverse_Of_The_Exporters_Frame()
    {
        var drawing = new ASH.Core.Layout.Layout();
        var o = new Point2(-250, 120); const double k = 0.02;
        drawing.Cps.Add(new LayoutCp { Name = "CP1", Position = new Point2(333.5, -77.25) });
        var m = CauseFrame.ToMetres(drawing, o, k).Cps.Single().Position;
        var mark = CauseMarks.Plan(new[] { Item("x", m.X, m.Y) }, o, k).Single();
        Assert.Equal(333.5, mark.At.X, 9); Assert.Equal(-77.25, mark.At.Y, 9);
    }

    [Fact] public void Plan_Rejects_A_Scale_That_Cannot_Be_Inverted() =>
        Assert.Throws<System.ArgumentOutOfRangeException>(() => CauseMarks.Plan(new[] { Item("a", 1, 1) }, new Point2(0, 0), 0));

    [Fact] public void ShortTitle_And_SeverityName()
    {
        Assert.Equal("교착 B005·B007", CauseMarks.ShortTitle("  교착 B005·B007 "));
        var t = CauseMarks.ShortTitle(new string('가', 30), 20);
        Assert.Equal(20, t.Length); Assert.EndsWith("…", t);
        Assert.Equal(new[] { "참고", "주의", "심각", "참고", "심각" }, new[] { 0, 1, 2, -3, 9 }.Select(CauseMarks.SeverityName));
    }

    [Fact] public void RefHandles_Match_Paths_Cps_And_Blocks_By_Name_Ignoring_Case_And_Missing_Names()
    {
        var l = new ASH.Core.Layout.Layout();
        var p = new LayoutPath("P1") { Id = "1A" }; p.Segments.Add(new LineSeg(new Point2(0, 0), new Point2(5, 0))); l.Paths.Add(p);
        l.Cps.Add(new LayoutCp { Name = "U003", Type = "unload", Position = new Point2(1, 0), Id = "2B" });
        l.Cps.Add(new LayoutCp { Name = "CP9", Position = new Point2(2, 0) }); // no handle
        l.Blocks.Add(new LayoutBlock { Name = "B005", Cap = 1, Rect = new Rect2(new Point2(0, 0), new Point2(1, 1)), Id = "3C" });
        l.Stations.Add(new LayoutStation { Name = "S1", Position = new Point2(0, 0) });
        var objs = ObjectIndex.Build(l, 1);
        Assert.Equal(new[] { "1A", "2B", "3C" }, CauseMarks.RefHandles(objs, new[] { "p1", "U003", "B005", "nope", "CP9", "S1", "P1" }));
        Assert.Empty(CauseMarks.RefHandles(objs, null));
        Assert.Empty(CauseMarks.RefHandles(objs, new string[0]));
    }

    [Fact] public void ParseCauses_Reads_The_Servers_Json_And_Fills_Absent_Keys()
    {
        var c = JobServerClient.ParseCauses(@"{""job"":""j1"",""run"":""r2"",""runs"":[{""id"":""r1"",""label"":""실행 1""},{""id"":""r2"",""label"":""실행 2 · 교착 1회""}],""note"":"""",
            ""items"":[{""kind"":""deadlock"",""severity"":2,""title"":""교착 B005·B007"",""where"":""B005"",""detail"":""d"",""fix"":""f"",""atSec"":3725.5,""x"":12.5,""y"":-3,""refs"":[""B005"",""P1""]},
                       {""kind"":""time-loss"",""severity"":0,""title"":""대기 많음""}]}");
        Assert.Equal("j1", c.Job); Assert.Equal("r2", c.Run); Assert.Equal(2, c.Runs.Count); Assert.Equal("실행 2 · 교착 1회", c.Runs[1].Label);
        var a = c.Items[0];
        Assert.Equal((CauseKinds.Deadlock, 2, 3725.5, 12.5, -3.0), (a.Kind, a.Severity, a.AtSec, a.X, a.Y)); Assert.Equal(new[] { "B005", "P1" }, a.Refs);
        var b = c.Items[1];
        Assert.Null(b.X); Assert.Null(b.Y); Assert.Null(b.AtSec); Assert.Equal("", b.Fix); Assert.Equal("", b.Where); Assert.Empty(b.Refs);
        Assert.Throws<InvalidDataException>(() => JobServerClient.ParseCauses("not json"));
    }

    [Fact] public void ParseAnalyses_Keeps_The_Plans_Finished_Jobs_And_Old_Servers_Have_None()
    {
        var l = JobServerClient.ParseAnalyses(@"[{""id"":""a1"",""name"":""A"",""plans"":[{""id"":""p1"",""name"":""안"",""nextVersion"":3,""jobs"":[{""id"":""j2"",""version"":2,""selected"":true},{""id"":""j1"",""version"":1,""selected"":false}]},{""id"":""p2"",""name"":""B"",""nextVersion"":1}]}]");
        Assert.Equal(new[] { "j2", "j1" }, l[0].Plans[0].Jobs.Select(j => j.Id)); Assert.True(l[0].Plans[0].Jobs[0].Selected);
        Assert.Empty(l[0].Plans[1].Jobs);
    }

    [Fact] public void Last_Job_Is_Remembered_Beside_The_Drawing_And_Not_In_The_Send_Choice()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash-job-" + System.Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(SendChoice.LoadJob(dir));
            SendChoice.SaveJob(dir, " 20260101-000000-abcd\n");
            new SendChoice { AnalysisName = "a", PlanName = "p" }.Save(dir); // saving the choice must not lose the job
            Assert.Equal("20260101-000000-abcd", SendChoice.LoadJob(dir));
            File.WriteAllText(Path.Combine(dir, SendChoice.JobFileName), "  \n");
            Assert.Null(SendChoice.LoadJob(dir));
        }
        finally { Directory.Delete(dir, true); }
    }
}
