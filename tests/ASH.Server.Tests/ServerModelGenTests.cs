using System.Net;
using System.Text.Json;
using ASH.Core.Cel;
using ASH.Core.Geometry;
using ASH.Core.Input;
using ASH.Core.Layout;
using ASH.Core.Runner;
using ASH.Core.Settings;
using ASH.Server;
using ASH.Server.Catalog;
using Microsoft.Extensions.DependencyInjection;

namespace ASH.Server.Tests;

/// <summary>A source package (layout.tsv + cells + settings, no .arc) is made into a model by the server at upload and at rerun.</summary>
public class ServerModelGenTests
{
    const string Stl = "solid body\nfacet normal 0 0 1\n outer loop\n  vertex 0 0 0\n  vertex 1000 0 0\n  vertex 0 1000 500\n endloop\nendfacet\nendsolid body\n";

    /// <summary>JobEnv.MakePackage's layout, but packed as the new plugin packs it. <paramref name="asset"/> goes in the first AGV_Types row's Asset cell.</summary>
    static string MakeSource(string root, bool dropLoad = false, string? asset = null)
    {
        var proj = Path.Combine(root, "src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(proj);
        var l = new ASH.Core.Layout.Layout();
        void Line(string n, double x0, double y0, double x1, double y1) { var p = new LayoutPath(n); p.Segments.Add(new LineSeg(new Point2(x0, y0), new Point2(x1, y1))); l.Paths.Add(p); }
        Line("p1", 0, 0, 10, 0); Line("p2", 10, 0, 10, 10); Line("p3", 10, 10, 0, 10); Line("p4", 0, 10, 0, 0);
        l.Cps.Add(new LayoutCp { Name = "S001", Type = "standby", Group = "G1", Position = new Point2(5, 0), Cap = ASH.Core.Traffic.CpTypes.DefaultCap("standby") });
        l.Cps.Add(new LayoutCp { Name = "L001", Type = "load", Group = "G1", Position = new Point2(10, 5), Cap = ASH.Core.Traffic.CpTypes.DefaultCap("load") });
        l.Cps.Add(new LayoutCp { Name = "U001", Type = "unload", Group = "G1", Position = new Point2(2, 10), Cap = ASH.Core.Traffic.CpTypes.DefaultCap("unload") });
        l.Blocks.Add(new LayoutBlock { Name = "B_x1", Cap = 1, Rect = new Rect2(new Point2(8, -1), new Point2(11, 2)) });
        var xlsx = Path.Combine(proj, "input.xlsx");
        ExcelTemplate.Write(xlsx, l);
        using (var wb = ExcelReader.Open(xlsx))
        {
            if (asset != null)
            {
                var ws = wb.Worksheet("AGV_Types");
                ws.Cell(3, ws.Row(1).CellsUsed().First(c => c.GetString() == "Asset").Address.ColumnNumber).Value = asset;
            }
            CellDump.Write(wb, Path.Combine(proj, CellDump.FileName));
        }
        File.Delete(xlsx);
        if (dropLoad) l.Cps.RemoveAll(c => c.Name == "L001"); // the workbook still names it
        SettingsFile.Save(new ProjectSettings(Scale: 1), Path.Combine(proj, JobPackage.SettingsInPackage));
        LayoutSnapshot.Write(l, Path.Combine(proj, LayoutSnapshot.FileName));
        var zip = Path.Combine(root, "pkg-" + Guid.NewGuid().ToString("N") + ".zip");
        JobPackage.CreateSource(proj, zip);
        return zip;
    }

    static string[] JobFolders(JobEnv env) => Directory.GetDirectories(env.Store.Dir).Where(d => JobStore.IsValidId(Path.GetFileName(d))).ToArray();

    static string Id(string json) => JsonDocument.Parse(json).RootElement.GetProperty("id").GetString()!;

    [Fact] public async Task A_Source_Package_Is_Accepted_And_The_Server_Makes_The_Model()
    {
        using var env = new JobEnv(worker: false);
        var res = await env.Send(MakeSource(env.Dir));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var id = Id(await res.Content.ReadAsStringAsync());
        var j = env.Store.Get(id)!;
        Assert.True(j.ServerGenerated);
        Assert.True(File.Exists(Path.Combine(env.Store.ProjectDir(id), "m.arc", "agv.asy")));
        var log = File.ReadAllText(env.Store.LogPath(id));
        Assert.Matches(@"\] 모델 생성: 서버 v\S+", log);
        Assert.Contains("서버에서 생성", await env.Http.GetStringAsync("/jobs/" + id + "?tab=input"));
        // job.json keeps the flag; an old job.json without it reads as false
        Assert.False(JsonSerializer.Deserialize<JobInfo>("{\"Id\":\"x\"}")!.ServerGenerated);
        Assert.True(JsonSerializer.Deserialize<JobInfo>(JsonSerializer.Serialize(j))!.ServerGenerated);
    }

    [Fact] public async Task A_Model_Package_Is_Not_Marked_Server_Generated()
    {
        using var env = new JobEnv(worker: false);
        var id = Id(await (await env.Send(JobEnv.MakePackage(env.Dir))).Content.ReadAsStringAsync());
        Assert.False(env.Store.Get(id)!.ServerGenerated);
        Assert.DoesNotContain("모델 생성", File.Exists(env.Store.LogPath(id)) ? File.ReadAllText(env.Store.LogPath(id)) : "");
    }

    [Fact] public async Task Input_Errors_Are_A_400_With_The_Errors_And_No_Job_Folder()
    {
        using var env = new JobEnv(worker: false);
        var res = await env.Send(MakeSource(env.Dir, dropLoad: true));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var text = await res.Content.ReadAsStringAsync();
        Assert.StartsWith("입력 오류 ", text);
        Assert.Contains("L001", text);
        Assert.Empty(env.Store.All());
        Assert.Empty(JobFolders(env));
    }

    [Fact] public async Task A_Broken_Layout_File_Is_A_400_Too()
    {
        using var env = new JobEnv(worker: false);
        var dir = Path.Combine(env.Dir, "broken"); Directory.CreateDirectory(dir);
        System.IO.Compression.ZipFile.ExtractToDirectory(MakeSource(env.Dir), dir);
        File.WriteAllText(Path.Combine(dir, LayoutSnapshot.FileName), "not a layout\n");
        var zip = Path.Combine(env.Dir, "broken.zip");
        JobPackage.CreateSource(dir, zip);
        var res = await env.Send(zip);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.StartsWith("모델을 만들지 못했습니다: ", await res.Content.ReadAsStringAsync());
        Assert.Empty(JobFolders(env));
    }

    [Fact] public void Error_Text_Lists_Twenty_Lines_And_Counts_The_Rest()
    {
        var t = ServerModelGen.ErrorText(Enumerable.Range(1, 23).Select(i => "e" + i).ToList());
        var lines = t.Split('\n');
        Assert.Equal("입력 오류 23건", lines[0]);
        Assert.Equal(22, lines.Length); // header + 20 + the rest
        Assert.Equal("외 3건", lines[^1]);
    }

    [Fact] public async Task Rerun_Of_A_Source_Job_Makes_The_Model_Again()
    {
        using var env = new JobEnv(worker: false);
        var id = Id(await (await env.Send(MakeSource(env.Dir))).Content.ReadAsStringAsync());
        env.Store.Update(id, j => { j.State = JobState.Done; j.Decided = new() { ["G1"] = 2 }; });
        var g = JobView.For(env.Store, env.Store.Get(id)!).Input!.Groups.Single();
        var n3 = Math.Min(g.MaxCount, 3).ToString();
        Assert.True((await env.Http.PostAsync($"/jobs/{id}/rerun", new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("n.G1", n3) }))).IsSuccessStatusCode);
        var n = env.Store.All().Single(j => j.Id != id);
        Assert.True(n.ServerGenerated);
        Assert.True(File.Exists(Path.Combine(env.Store.ProjectDir(n.Id), "m.arc", "agv.asy")));
        Assert.Contains("모델 생성: 서버 v", File.ReadAllText(env.Store.LogPath(n.Id)));
    }

    [Fact] public async Task The_Vehicle_Asset_Comes_From_The_Asset_Store()
    {
        using var env = new JobEnv(worker: false);
        var stl = Path.Combine(env.Dir, "agv.stl"); File.WriteAllText(stl, Stl);
        var xlsx = Path.Combine(env.Dir, "x.xlsx"); ExcelTemplate.Write(xlsx);
        var type = ExcelReader.Read(xlsx).AgvTypes[0].Type;
        env.Factory.Services.GetRequiredService<AssetStore>().Create(type, "agv_a", "", 0, stl, "agv.stl", new SimplifyOptions(), "t");
        var id = Id(await (await env.Send(MakeSource(env.Dir, asset: "agv_a"))).Content.ReadAsStringAsync());
        Assert.True(File.Exists(Path.Combine(env.Store.ProjectDir(id), "m.arc", "assets", "agv_a.cel")));
        var lg = File.ReadAllText(env.Store.LogPath(id)); Assert.Contains("경고: 타입 T_AGV: 에셋 'agv_a' 크기", lg); Assert.DoesNotContain("카탈로그에 없", lg); // found; only the size differs from Excel
    }

    [Fact] public async Task A_Missing_Asset_Is_A_Warning_In_The_Log_Not_An_Error()
    {
        using var env = new JobEnv(worker: false);
        var id = Id(await (await env.Send(MakeSource(env.Dir, asset: "nope"))).Content.ReadAsStringAsync());
        Assert.Contains("경고: ", File.ReadAllText(env.Store.LogPath(id)));
        Assert.False(Directory.Exists(Path.Combine(env.Store.ProjectDir(id), "m.arc", "assets")));
    }

    [Fact] public async Task Queue_Says_The_Server_Generates()
    {
        using var env = new JobEnv(worker: false);
        Assert.True(JsonDocument.Parse(await env.Http.GetStringAsync("/api/jobs/queue")).RootElement.GetProperty("generates").GetBoolean());
    }
}
