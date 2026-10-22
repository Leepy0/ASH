using System.Net;
using System.Text.Json;
using ASH.Core.Dxf;

namespace ASH.Server.Tests;

public class DrawingJsonTests
{
    static async Task<string> Upload(JobEnv env) =>
        JsonDocument.Parse(await (await env.Send(JobEnv.MakePackage(env.Dir))).Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

    [Fact]
    public async Task Drawing_Json_Without_Dxf_Has_Paths_Only()
    {
        using var env = new JobEnv(worker: false);
        var id = await Upload(env);
        var res = await env.Http.GetAsync($"/jobs/{id}/drawing.json");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var root = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0, root.GetProperty("segs").GetArrayLength());
        Assert.Equal(0, root.GetProperty("texts").GetArrayLength());
        Assert.Equal(4, root.GetProperty("paths").GetArrayLength());
        Assert.Equal(3, root.GetProperty("cps").GetArrayLength());
        Assert.Equal(1, root.GetProperty("blocks").GetArrayLength());
        Assert.False(root.GetProperty("truncated").GetBoolean());
        var b = root.GetProperty("bounds").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        Assert.Equal(new[] { 0.0, -1.0, 11.0, 10.0 }, b);
        Assert.True(File.Exists(Path.Combine(env.Store.JobDir(id), "drawing.json")));
        Assert.Equal(HttpStatusCode.NotFound, (await env.Http.GetAsync("/jobs/nope/drawing.json")).StatusCode);
    }

    [Fact]
    public async Task Drawing_Json_Moves_The_Dxf_Into_Model_Metres()
    {
        using var env = new JobEnv(worker: false);
        var id = await Upload(env);
        var project = env.Store.ProjectDir(id);
        File.WriteAllText(Path.Combine(project, "drawing.dxf"), "0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nWALL\n10\n1000\n20\n2000\n11\n3000\n21\n2000\n0\nTEXT\n8\nNAME\n10\n1500\n20\n2500\n40\n250\n1\nA동\n0\nENDSEC\n0\nEOF\n");
        new ModelFrame("Millimeters", 1000, 2000, 0.001).Write(Path.Combine(project, ModelFrame.FileName));
        var root = JsonDocument.Parse(await env.Http.GetStringAsync($"/jobs/{id}/drawing.json")).RootElement;
        Assert.Equal(new[] { 0.0, 0.0, 2.0, 0.0, 0.0 }, root.GetProperty("segs")[0].EnumerateArray().Select(x => x.GetDouble()).ToArray());
        Assert.Equal("A동", root.GetProperty("texts")[0][3].GetString());
        Assert.Equal(new[] { "WALL", "NAME" }, root.GetProperty("layers").EnumerateArray().Select(x => x.GetString()).ToArray());
    }

    [Fact]
    public async Task Binary_Dxf_Falls_Back_To_Paths_With_A_Message()
    {
        using var env = new JobEnv(worker: false);
        var id = await Upload(env);
        var project = env.Store.ProjectDir(id);
        File.WriteAllText(Path.Combine(project, "drawing.dxf"), "AutoCAD Binary DXF\r\n\u001a\0");
        new ModelFrame("Meters", 0, 0, 1).Write(Path.Combine(project, ModelFrame.FileName));
        var root = JsonDocument.Parse(await env.Http.GetStringAsync($"/jobs/{id}/drawing.json")).RootElement;
        Assert.Equal(0, root.GetProperty("segs").GetArrayLength());
        Assert.Contains("바이너리", root.GetProperty("drawingError").GetString());
        Assert.Equal(4, root.GetProperty("paths").GetArrayLength());
    }
}
