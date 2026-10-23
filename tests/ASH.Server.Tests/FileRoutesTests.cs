using System.IO.Compression;
using System.Net;
using System.Text.Json;
using ASH.Core.Analysis;
using ASH.Core.Input;
using ASH.Core.Runner;
using ASH.Server;
using Microsoft.Extensions.DependencyInjection;

namespace ASH.Server.Tests;

public class FileRoutesTests
{
    static async Task<(JobEnv Env, string Id)> Upload(bool dwg)
    {
        var env = new JobEnv();
        var zip = JobEnv.MakePackage(env.Dir, addFiles: p =>
        {
            File.WriteAllText(Path.Combine(p, "ash.drawing.dxf"), "0\nEOF\n");
            File.WriteAllText(Path.Combine(p, "ash.settings.tsv"), "Scale\t1\n");
            if (dwg) File.WriteAllBytes(Path.Combine(p, "x.dwg"), new byte[] { 1, 2, 3 });
        });
        if (dwg)
        {   // MakePackage cannot pass dwgPath: repack with it
            var proj = Directory.GetDirectories(env.Dir, "proj-*").Last();
            var dwgPath = Path.Combine(proj, "x.dwg");
            File.Delete(zip);
            JobPackage.Create(proj, "m", zip, dwgPath: dwgPath);
        }
        var res = await env.Send(zip);
        var id = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
        await env.WaitFinished(id);
        return (env, id);
    }

    [Fact]
    public async Task Downloads_Each_File_And_Keeps_The_Old_Names()
    {
        var (env, id) = await Upload(true);
        using var env0 = env;
        Assert.Equal(HttpStatusCode.OK, (await env.Http.GetAsync($"/jobs/{id}/files/drawing.dwg")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await env.Http.GetAsync($"/jobs/{id}/files/drawing.dxf")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await env.Http.GetAsync($"/jobs/{id}/files/input.xlsx")).StatusCode);
        Assert.Equal("application/zip", (await env.Http.GetAsync($"/jobs/{id}/files/continue.zip")).Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.OK, (await env.Http.GetAsync($"/jobs/{id}/files/summary.tsv")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await env.Http.GetAsync($"/jobs/{id}/files/x.dwg")).StatusCode);
    }

    [Fact]
    public async Task ContinueZip_Has_The_Bundle_And_Korean_Readme()
    {
        var (env, id) = await Upload(true);
        using var env0 = env;
        var an = env.Factory.Services.GetRequiredService<AnalysisStore>();
        var a = an.CreateAnalysis("라인A", ""); var p = an.CreatePlan(a.Id, "안1", "");
        env.Store.Update(id, j => { j.AnalysisId = a.Id; j.PlanId = p.Id; j.Version = 3; });
        using var za = new ZipArchive(await env.Http.GetStreamAsync($"/jobs/{id}/files/continue.zip"));
        Assert.Equal(new[] { "README.txt", "ash.settings.tsv", "input.xlsx", "layout.dwg" }, za.Entries.Select(e => e.FullName).OrderBy(x => x, StringComparer.Ordinal));
        string readme;
        using (var r = new StreamReader(za.GetEntry("README.txt")!.Open())) readme = r.ReadToEnd();
        Assert.StartsWith("이 자료는 분석 라인A › 안 안1 v3.", readme.TrimStart('﻿'));
        Assert.Contains("같은 분석을 고르면 다음 버전, 새 안을 고르면 새 안", readme);
        var tmp = Path.Combine(env.Dir, "back.xlsx");
        using (var e = za.GetEntry("input.xlsx")!.Open()) using (var f = File.Create(tmp)) e.CopyTo(f);
        using var wb = CellDump.Load(Path.Combine(env.Store.ProjectDir(id), CellDump.FileName));
        Assert.Equal(ExcelReader.Read(wb, out _).Materials.Count, ExcelReader.Read(tmp).Materials.Count);
    }

    [Fact]
    public async Task Without_Dwg_The_Zip_Carries_The_Dxf_And_Dwg_Is_404()
    {
        var (env, id) = await Upload(false);
        using var env0 = env;
        Assert.Equal(HttpStatusCode.NotFound, (await env.Http.GetAsync($"/jobs/{id}/files/drawing.dwg")).StatusCode);
        using var za = new ZipArchive(await env.Http.GetStreamAsync($"/jobs/{id}/files/continue.zip"));
        Assert.NotNull(za.GetEntry("drawing.dxf"));
        Assert.Null(za.GetEntry("layout.dwg"));
        using var r = new StreamReader(za.GetEntry("README.txt")!.Open());
        Assert.Contains("DWG 없음: DXF를 넣었습니다", r.ReadToEnd());
    }

    [Fact] public void ContinueLink_Points_At_The_Zip() => Assert.Contains("/jobs/j1/files/continue.zip", FileRoutes.ContinueLink(new JobInfo { Id = "j1", Files = { new("input.cells.tsv", 1, "") } }));
    [Fact] public void ContinueLink_Is_Empty_Without_Snapshot() => Assert.Equal("", FileRoutes.ContinueLink(new JobInfo { Id = "j1" }));

    [Fact]
    public void Search_Matches_Name_Material_Date_And_Tags()
    {
        var a = new AnalysisInfo { Id = "a1", Name = "A", Corp = "P", Plans = { new PlanInfo { Id = "p1", Name = "X" } } };
        var b = new AnalysisInfo { Id = "a2", Name = "B", Corp = "Q", Plans = { new PlanInfo { Id = "p2", Name = "Y" } } };
        var jobs = new[]
        {
            new JobInfo { Id = "j1", AnalysisId = "a1", PlanId = "p1", Version = 1, CreatedUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) },
            new JobInfo { Id = "j2", AnalysisId = "a2", PlanId = "p2", Version = 1, CreatedUtc = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc) },
            new JobInfo { Id = "j3", Project = "프로젝트", Plan = "Z", CreatedUtc = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc) },
        };
        IReadOnlyList<string> Mats(string id) => id == "j1" ? new[] { "M-100" } : id == "j2" ? new[] { "M-200" } : Array.Empty<string>();
        string[] Ids(FileRoutes.FileQuery f) => FileRoutes.Search(f, jobs, new[] { a, b }, Mats).Select(j => j.Id).ToArray();
        Assert.Equal(new[] { "j1" }, Ids(new(Q: "X")));
        Assert.Equal(new[] { "j3" }, Ids(new(Q: "프로젝트")));
        Assert.Equal(new[] { "j1" }, Ids(new(Material: "100")));
        Assert.Equal(new[] { "j3", "j2" }, Ids(new(FromUtc: new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc))));
        Assert.Equal(new[] { "j2" }, Ids(new(Corp: "q")));
        Assert.Equal(3, Ids(new()).Length);
        var qs = FileRoutes.FileQuery.From(new Microsoft.AspNetCore.Http.QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["from"] = "2026-10-02", ["to"] = "2026-10-20" }));
        Assert.Equal(new[] { "j2" }, Ids(qs)); // 10-20 Seoul day ends 10-20 15:00Z; j2 is 10-20 00:00Z, j1 is before
    }

    [Fact]
    public async Task Files_Page_Lists_Jobs_With_Download_Links()
    {
        var (env, id) = await Upload(true);
        using var env0 = env;
        var res = await env.Http.GetAsync("/files");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains($"/jobs/{id}/files/continue.zip", html);
        Assert.Contains("drawing.dwg", html);
    }

    [Fact]
    public void Readme_Says_No_Drawing_When_Neither_Exists() =>
        Assert.Contains("도면 파일이 없습니다", FileRoutes.ReadmeText(null, null, new JobInfo { Id = "j1" }, false, true, hasDxf: false));
    [Fact]
    public async Task Tag_Filter_Applies_On_Home_And_Analyses_And_Status_Tab_Has_The_Continue_Link()
    {
        var (env, id) = await Upload(false);
        using var env0 = env;
        var an = env.Factory.Services.GetRequiredService<AnalysisStore>();
        var a = an.CreateAnalysis("라인A", "", corp: "P"); var p = an.CreatePlan(a.Id, "안1", "");
        var b = an.CreateAnalysis("라인B", "", corp: "Q");
        env.Store.Update(id, j => { j.AnalysisId = a.Id; j.PlanId = p.Id; j.Version = 1; });
        foreach (var path in new[] { "/?me=0", "/analyses" })
        {
            var all = await env.Http.GetStringAsync(path);
            Assert.Contains("name=\"corp\"", all); Assert.Contains("라인A", all); Assert.Contains("라인B", all);
            var onlyP = await env.Http.GetStringAsync(path + (path.Contains('?') ? "&" : "?") + "corp=P");
            Assert.Contains("라인A", onlyP); Assert.DoesNotContain("라인B", onlyP);
            var onlyQ = await env.Http.GetStringAsync(path + (path.Contains('?') ? "&" : "?") + "corp=q");
            Assert.Contains("라인B", onlyQ); Assert.DoesNotContain("라인A", onlyQ);
        }
        Assert.Contains($"href=\"/jobs/{id}/files/continue.zip\"", await env.Http.GetStringAsync("/analyses/" + a.Id));
    }
}
