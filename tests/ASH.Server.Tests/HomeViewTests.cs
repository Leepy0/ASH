using ASH.Core;
using ASH.Core.Analysis;
using ASH.Server;

namespace ASH.Server.Tests;

public class HomeViewTests
{
    static JobInfo J(string id, string ip, string who, string state = "done", string? analysis = null, string? plan = null, int version = 1, bool selected = false, int? expected = null, int runs = 0) =>
        new() { Id = id, Ip = ip, Submitter = who, State = state, AnalysisId = analysis, PlanId = plan, Version = version, Selected = selected, ExpectedRuns = expected, RunsDone = runs,
                CreatedUtc = new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc), Decided = state == "done" ? new() { ["G1"] = 2 } : null };
    static readonly DateTime now = new(2026, 10, 23, 1, 0, 0, DateTimeKind.Utc);
    static AnalysisInfo A(string id, string name, string planId) => new() { Id = id, Name = name, Plans = { new PlanInfo { Id = planId, Name = "A안" } } };
    // 분석 A(a1/p1): v1 done 선정, v2 running(10회 중 3회), 보낸 IP 10.0.0.5 — 분석 B(a2/p2): v1 done, 보낸 IP 10.0.0.9
    static readonly AnalysisInfo[] analyses = { A("a1", "A", "p1"), A("a2", "B", "p2") };
    static readonly JobInfo[] jobs = { J("j1", "10.0.0.5", "kim", analysis: "a1", plan: "p1", selected: true), J("j2", "10.0.0.5", "kim", "running", "a1", "p1", 2, expected: 10, runs: 3), J("j3", "10.0.0.9", "park", analysis: "a2", plan: "p2") };

    [Fact] public void Mine_Collects_Every_Submitter_Of_The_Ip()
    {
        var jobs = new[] { J("a", ip: "10.0.0.5", who: "kim"), J("b", ip: "10.0.0.5", who: "lee"), J("c", ip: "10.0.0.9", who: "lee"), J("d", ip: "10.0.0.9", who: "park") };
        var names = Mine.Names(jobs, "10.0.0.5");
        Assert.Equal(new[] { "kim", "lee" }, names.OrderBy(x => x));
        Assert.True(Mine.Is(jobs[2], "10.0.0.5", names)); Assert.False(Mine.Is(jobs[3], "10.0.0.5", names));
    }

    [Fact] public void Queued_Start_Is_Now_Plus_Remaining_Then_One_Typical_Per_Job_Ahead()
    {
        var typ = TimeSpan.FromMinutes(5);
        var js = new[] { J("q1", "10.0.0.5", "kim", "queued"), J("q2", "10.0.0.5", "kim", "queued") };
        // QueueInfo's Start is for a NEW job: now + 10 min left + typical x 2 queued
        var v = HomeView.Build([], js, [], null, (2, now.AddMinutes(10) + typ * 2, typ), true, 1, "10.0.0.5", null, now);
        string Hm(DateTime d) => SeoulTime.FromUtc(d).ToString("HH:mm");
        Assert.Equal(Hm(now.AddMinutes(10)), v.Queued[0].Start); Assert.Equal(Hm(now.AddMinutes(15)), v.Queued[1].Start);
    }

    [Fact] public void Running_Film_Without_Job_Becomes_The_Running_Card()
    {
        var req = new FilmRequest("f1", "a1", ["c1"], ["j1"], 640, 480, 10, "joined", false, null, 0, null);
        var film = new FilmInfo { Request = req, AnalysisName = "A", State = "running", StartedUtc = now.AddMinutes(-10), Done = 1, Items = { new FilmItem(), new FilmItem(), new FilmItem(), new FilmItem() } };
        var typ = TimeSpan.FromMinutes(5);
        var q = new[] { J("q1", "10.0.0.5", "kim", "queued") };
        var v = HomeView.Build(analyses, q, [], film, (1, null, typ), true, 1, "10.0.0.5", null, now);
        Assert.Equal("A › 영상", v.Running!.Title); Assert.Equal(0.25, v.Running.Progress); Assert.Equal("/films/f1", v.Running.Href);
        Assert.Equal(SeoulTime.FromUtc(now.AddMinutes(30)).ToString("HH:mm"), v.Running.Eta); // 10 min per cut, 3 left
        Assert.Equal(SeoulTime.FromUtc(now.AddMinutes(30)).ToString("HH:mm"), v.Queued[0].Start);
        var l = LiveFields.OfHome(v);
        Assert.Equal("running", l.Watch["f1"]); Assert.Equal("25%", l.Fields["running.progress"]); Assert.Equal("컷 1 / 4", l.Fields["running.film"]);
    }

    [Fact] public void Version_Note_Shows_On_The_Running_Card()
    {
        var j = J("r1", "10.0.0.5", "kim", "running", "a1", "p1", 2, expected: 10, runs: 3); j.VersionNote = "게이트 변경"; j.LastLine = "run 3";
        var v = HomeView.Build(analyses, [j], [], null, (1, null, null), true, 1, "10.0.0.5", null, now);
        Assert.Equal("게이트 변경", v.Running!.Note); Assert.Contains("게이트 변경", HomePages.Home(v, "", "", null));
        j.VersionNote = ""; Assert.Equal("run 3", HomeView.Build(analyses, [j], [], null, (1, null, null), true, 1, "10.0.0.5", null, now).Running!.Note);
    }

    [Fact] public void Chip_Text_Marks_Failed_And_Cancelled()
    {
        PlanChip C(string s) => new("p", "A안", "j", 2, s, null, null, false);
        Assert.Equal("A안 v2 실패", LiveFields.ChipText(C("failed"))); Assert.Equal("A안 v2 취소", LiveFields.ChipText(C("cancelled")));
    }

    [Fact] public void Build_Splits_In_Progress_And_Finished_And_Falls_Back_To_All()
    {
        var v = HomeView.Build(analyses, jobs, [], null, (1, null, null), true, 2, "10.0.0.5", me: null, now);
        Assert.True(v.Me); Assert.Single(v.InProgress); Assert.Equal("A", v.InProgress[0].Name); Assert.Empty(v.Finished); // B는 내 것 아님
        Assert.Equal(0.3, v.Running!.Progress!.Value, 3); Assert.Equal("A › A안 v2", v.Running.Title);
        Assert.Equal(new[] { "kim" }, v.InProgress[0].Submitters);
        var all = HomeView.Build(analyses, jobs, [], null, (1, null, null), true, 2, "10.0.0.5", me: false, now);
        Assert.Single(all.Finished); Assert.Equal("B", all.Finished[0].Name);
        var nobody = HomeView.Build(analyses, jobs, [], null, (1, null, null), true, 2, "10.9.9.9", me: null, now);
        Assert.False(nobody.Me); Assert.Equal(2, nobody.InProgress.Count + nobody.Finished.Count);
        var forced = HomeView.Build(analyses, jobs, [], null, (1, null, null), true, 2, "10.9.9.9", me: true, now);
        Assert.False(forced.Me); Assert.Equal(2, forced.InProgress.Count + forced.Finished.Count);
    }
}
