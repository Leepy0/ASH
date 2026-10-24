using System.IO.Compression;
using System.Net;
using System.Text.Json;
using ASH.Core.Runner;
using ASH.Server;
using Microsoft.Extensions.DependencyInjection;

namespace ASH.Server.Tests;

public class JobLocalAndRetentionTests
{
    static string Id(string json) => JsonDocument.Parse(json).RootElement.GetProperty("id").GetString()!;

    /// <summary>A project folder unpacked from the plugin package, plus what ash-run leaves in runs\.</summary>
    internal static string LocalResultPackage(string root)
    {
        var proj = Path.Combine(root, "local-" + Guid.NewGuid().ToString("N"));
        ZipFile.ExtractToDirectory(JobEnv.MakePackage(root), proj);
        var runs = Path.Combine(proj, "runs");
        foreach (var (n, util) in new[] { (3, 0.4), (2, 0.6) })
            SummaryTsv.Append(Path.Combine(runs, "summary.tsv"), new SummaryRow($"n{n}_s1", "G1", n, 1, true, "", util, 2, 10, 10, 0, 0, 5, "G1=" + n));
        Directory.CreateDirectory(Path.Combine(runs, "n2_s1"));
        File.WriteAllText(Path.Combine(runs, "n2_s1", "result.tsv"), "kind\tname\tgroup\tv1\tv2\tv3\tv4\nblock\tB_x1\t-\t12\t3.5\t9\t0.4\nclock\t-\t-\t0\t28800\n"
            + FakeResult.Tsv(new[] { "M_loc" }).Split('\n', 2)[1]);
        SearchSummaryFile.Write(Path.Combine(runs, SearchSummaryFile.FileName), new SearchSummary(0, null, new Dictionary<string, int> { ["G1"] = 2 },
            new[] { new[] { "G1" } }, new[] { "n2_s1" }, new[] { "로컬 경고" }, Array.Empty<string>(), 2, 0, 120));
        var zip = Path.Combine(root, "localpkg-" + Guid.NewGuid().ToString("N") + ".zip");
        var files = JobPackage.Create(proj, "m", zip, includeResults: true);
        Assert.Contains(files, f => f.Path == "runs/n2_s1/result.tsv");
        return zip;
    }

    [Fact]
    public void Search_Summary_Round_Trips()
    {
        var p = Path.Combine(Path.GetTempPath(), "search-" + Guid.NewGuid().ToString("N") + ".tsv");
        try
        {
            SearchSummaryFile.Write(p, new SearchSummary(2, "x\ty", new Dictionary<string, int> { ["A"] = 3, ["B"] = 1 }, new[] { new[] { "A", "B" } },
                new[] { "n3-1_s1", "n3-1_s2" }, new[] { "w1" }, new[] { "f1" }, 12, 1, 65.5));
            var s = SearchSummaryFile.Read(p);
            Assert.Equal(2, s.ExitCode); Assert.Equal("x y", s.Error);
            Assert.Equal(3, s.Decided["A"]); Assert.Equal(new[] { "A", "B" }, s.Clusters.Single());
            Assert.Equal(2, s.ConfirmRunIds.Count); Assert.Equal(12, s.Runs); Assert.Equal(1, s.Failures); Assert.Equal(65.5, s.ElapsedSec, 1);
            File.WriteAllText(p, "hello");
            Assert.Throws<InvalidDataException>(() => SearchSummaryFile.Read(p));
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public async Task A_Local_Result_Is_Shown_Without_Running_On_The_Server()
    {
        using var env = new JobEnv(worker: false);
        var res = await env.Send(LocalResultPackage(env.Dir));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var id = Id(await res.Content.ReadAsStringAsync());
        var j = env.Store.Get(id)!;
        Assert.Equal(JobSource.Local, j.Source);
        Assert.Equal(JobState.Done, j.State);
        Assert.Equal(2, j.Decided!["G1"]);
        var page = await env.Http.GetStringAsync("/jobs/" + id);
        Assert.Contains("내 PC에서 실행한 결과", page);
        Assert.Contains("로컬 경고", page);
        Assert.Contains("3.5초", page);                    // block wait from the uploaded result.tsv
        var xlsx = await env.Http.GetAsync($"/jobs/{id}/files/report.xlsx");
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        Assert.Equal(0, env.Store.QueueInfo().Ahead);       // nothing queued
    }

    [Fact]
    public async Task Old_Run_Folders_Are_Zipped_And_The_Pages_Keep_Their_Numbers()
    {
        using var env = new JobEnv(worker: false);
        var id = Id(await (await env.Send(LocalResultPackage(env.Dir))).Content.ReadAsStringAsync());
        var project = env.Store.ProjectDir(id);
        Directory.CreateDirectory(Path.Combine(project, "m.dir"));
        File.WriteAllText(Path.Combine(project, "m.dir", "big.dll"), new string('x', 1000));
        var zipPath = Path.Combine(env.Store.JobDir(id), "runs.zip");
        var original = File.ReadAllText(Path.Combine(project, "runs", "n2_s1", "result.tsv"));
        var retention = env.Factory.Services.GetRequiredService<JobRetention>();

        Assert.Equal(0, retention.Sweep(DateTime.UtcNow));               // too new
        Assert.False(File.Exists(zipPath));
        Assert.Equal(1, retention.Sweep(DateTime.UtcNow.AddDays(31)));  // default 30 days
        Assert.False(Directory.Exists(Path.Combine(project, "runs", "n2_s1")));   // original folder gone ...
        Assert.False(Directory.Exists(Path.Combine(project, "m.dir")));           // ... build output too (rebuilt on demand)
        Assert.True(File.Exists(Path.Combine(project, "runs", "summary.tsv")));
        Assert.True(File.Exists(env.Store.UploadPath(id)));
        Assert.NotNull(env.Store.Get(id)!.RunsPurgedUtc);
        Assert.False(File.Exists(zipPath + ".tmp"));
        Assert.Equal(0, retention.Sweep(DateTime.UtcNow.AddDays(60)));  // once

        // ... but the run is still in runs.zip, readable, with the same bytes
        using (var z = ZipFile.OpenRead(zipPath))
        {
            Assert.Contains(z.Entries, e => e.FullName == "summary.tsv");
            using var r = new StreamReader(z.GetEntry("n2_s1/result.tsv")!.Open());
            Assert.Equal(original, r.ReadToEnd());
        }
        var dl = await env.Http.GetAsync($"/jobs/{id}/files/runs.zip");   // the download serves the archive
        Assert.Equal(HttpStatusCode.OK, dl.StatusCode);
        Assert.Equal(new FileInfo(zipPath).Length, (await dl.Content.ReadAsByteArrayAsync()).Length);
        var files = await env.Http.GetStringAsync($"/jobs/{id}?tab=files");
        Assert.Contains("압축 보관", files);
        Assert.True(File.Exists(Path.Combine(env.Store.JobDir(id), JobRetention.BlocksFile)));
        Assert.True(File.Exists(Path.Combine(env.Store.JobDir(id), JobRetention.StatsFile)));

        // a new FinishedUtc drops the cached view, so the next load has no run folders and reads stats.tsv
        env.Store.Update(id, x => x.FinishedUtc = x.FinishedUtc!.Value.AddSeconds(1));
        var view = JobView.For(env.Store, env.Store.Get(id)!);
        Assert.Equal(2, view.Vehicles.Count);
        Assert.Equal(0.6, view.Vehicles[0].Util, 6);
        Assert.Equal(30, view.Vehicles[0].Down);
        Assert.Equal(0, view.Vehicles[1].Down);
        var m = Assert.Single(view.Materials);
        Assert.Equal("M_loc", m.Name);
        Assert.Equal(100.5, m.DelivAvg, 6);
        Assert.Equal(50.0, view.VehicleGroups.Single().Util * 100, 6);
        var page = await env.Http.GetStringAsync("/jobs/" + id);
        Assert.Contains("M_loc", page);
        Assert.Contains("<td>G1</td><td class=\"num\">2</td><td class=\"num\">50.0</td>", page);
    }

    [Fact]
    public async Task A_Stale_Temp_Zip_From_A_Crash_Is_Replaced_And_A_Failed_Zip_Keeps_The_Originals()
    {
        using var env = new JobEnv(worker: false);
        var id = Id(await (await env.Send(LocalResultPackage(env.Dir))).Content.ReadAsStringAsync());
        var project = env.Store.ProjectDir(id);
        var tmp = Path.Combine(env.Store.JobDir(id), "runs.zip.tmp");
        File.WriteAllText(tmp, "half written by a crashed sweep");
        var retention = env.Factory.Services.GetRequiredService<JobRetention>();

        Assert.Equal(1, retention.Sweep(DateTime.UtcNow.AddDays(31)));
        Assert.False(File.Exists(tmp));                                            // moved over runs.zip, nothing left behind
        using (var z = ZipFile.OpenRead(Path.Combine(env.Store.JobDir(id), "runs.zip"))) Assert.NotNull(z.GetEntry("n2_s1/result.tsv"));

        // a zip that cannot be written (the temp name is a folder): the sweep gives up on that job and the run folders stay
        var id2 = Id(await (await env.Send(LocalResultPackage(env.Dir))).Content.ReadAsStringAsync());
        Directory.CreateDirectory(Path.Combine(env.Store.JobDir(id2), "runs.zip.tmp"));
        Assert.Equal(0, retention.Sweep(DateTime.UtcNow.AddDays(31)));
        Assert.True(File.Exists(Path.Combine(env.Store.ProjectDir(id2), "runs", "n2_s1", "result.tsv")));
        Assert.Null(env.Store.Get(id2)!.RunsPurgedUtc);
    }
}
