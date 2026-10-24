namespace ASH.Server.Tests;

public class JobPagesAssetTests
{
    static async Task<string> InputTab(JobEnv env, Action<string>? addFiles)
    {
        var res = await env.Send(JobEnv.MakePackage(env.Dir, addFiles: addFiles));
        var id = System.Text.Json.JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
        await env.WaitFinished(id);
        return await env.Http.GetStringAsync($"/jobs/{id}?tab=input");
    }

    [Fact]
    public async Task Input_Tab_Shows_The_Asset_Name_Of_Each_Type()
    {
        using var env = new JobEnv();
        // the cell snapshot is plain text: sheet, row, column (Asset = 18th AGV_Types column, first data row = 3 after the description row), kind, value
        var html = await InputTab(env, proj => File.AppendAllText(Path.Combine(proj, ASH.Core.Input.CellDump.FileName), "AGV_Types\t3\t18\tS\tagv_a\n"));
        Assert.Contains("<th>에셋</th>", html);
        Assert.Contains("<td>agv_a</td>", html);
        Assert.DoesNotContain("기본 그림", html);
    }

    [Fact]
    public async Task Input_Tab_Says_Default_Picture_When_No_Asset()
    {
        using var env = new JobEnv();
        var html = await InputTab(env, null);
        Assert.Contains("<th>에셋</th>", html);
        Assert.Contains("<td>기본 그림</td>", html);
    }
}
