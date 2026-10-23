using System.IO.Compression;
using System.Net;
using System.Text.Json;
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
}
