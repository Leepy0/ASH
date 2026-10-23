using ASH.Core.Cel;
using ASH.Server.Catalog;
using Microsoft.Extensions.Logging.Abstractions;

namespace ASH.Server.Tests;

public class AssetStoreTests : IDisposable
{
    const string Stl = "solid body\nfacet normal 0 0 1\n outer loop\n  vertex 0 0 0\n  vertex 1000 0 0\n  vertex 0 1000 500\n endloop\nendfacet\nendsolid body\n";
    readonly string dir = Path.Combine(Path.GetTempPath(), "ash-asset-test-" + Guid.NewGuid().ToString("N"));
    readonly string stlPath;
    readonly AssetStore st;

    public AssetStoreTests()
    {
        Directory.CreateDirectory(dir);
        stlPath = Path.Combine(dir, "upload.tmp"); // the upload path has no extension; the original name decides
        File.WriteAllText(stlPath, Stl);
        st = new AssetStore(dir, NullLogger<AssetStore>.Instance);
    }
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

    AssetInfo Make(string type = "T_AGV", string name = "agv_a") => st.Create(type, name, "", 0, stlPath, "agv.stl", new SimplifyOptions(), "admin");

    [Fact] public void Create_Writes_Four_Files_And_Reloads()
    {
        var a = Make();
        foreach (var f in new[] { "source.stl", "asset.cel", "preview.svg", "asset.json" }) Assert.True(File.Exists(Path.Combine(st.Dir, a.Id, f)), f);
        var st2 = new AssetStore(dir, NullLogger<AssetStore>.Instance);
        Assert.Equal("agv_a", st2.Find(a.Id)!.Name);
        Assert.Single(st2.ForType("T_AGV"));
    }

    [Fact] public void Duplicate_Name_In_Type_Rejected()
    {
        Make();
        Assert.Throws<ArgumentException>(() => Make());
        Make("T_AMR");
    }

    [Fact] public void Bad_Name_Rejected() => Assert.Throws<ArgumentException>(() => Make(name: "agv a"));

    [Fact] public void Reconvert_Keeps_Bak()
    {
        var a = Make();
        st.Reconvert(a.Id, "agv_b", "", 90, new SimplifyOptions(Origin: OriginAt.Front), "admin");
        Assert.True(File.Exists(Path.Combine(st.Dir, a.Id, "asset.cel.bak")));
        Assert.True(File.Exists(Path.Combine(st.Dir, a.Id, "asset.json.bak")));
        Assert.Equal("agv_b", st.Find(a.Id)!.Name);
        var re = new AssetStore(dir, NullLogger<AssetStore>.Instance).Find(a.Id)!;
        Assert.Equal(90, re.ConveyorDir);
        Assert.Equal(OriginAt.Front, re.Options.Origin);
    }

    [Fact] public void Failed_Convert_Leaves_No_Folder()
    {
        var bad = Path.Combine(dir, "bad.tmp");
        File.WriteAllText(bad, "v 1 2\nf 1 2 3\n");
        Assert.ThrowsAny<Exception>(() => st.Create("T_AGV", "x", "", 0, bad, "x.obj", new SimplifyOptions(), "a"));
        Assert.Empty(Directory.GetDirectories(st.Dir));
    }

    [Fact] public void Delete_Removes_Folder()
    {
        var a = Make();
        Assert.True(st.Delete(a.Id));
        Assert.Empty(Directory.GetDirectories(st.Dir));
        Assert.False(st.Delete(a.Id));
    }

    [Fact] public void Failed_Reconvert_Leaves_Asset_Intact()
    {
        var a = Make();
        var d = Path.Combine(st.Dir, a.Id);
        var cel = File.ReadAllBytes(Path.Combine(d, "asset.cel")); var js = File.ReadAllBytes(Path.Combine(d, "asset.json"));
        File.WriteAllText(Path.Combine(d, "source.stl"), "solid x\nfacet normal 0 0 1\n outer loop\n vertex a b c\n");
        Assert.ThrowsAny<Exception>(() => st.Reconvert(a.Id, "agv_b", "", 90, new SimplifyOptions(), "admin"));
        Assert.Equal(cel, File.ReadAllBytes(Path.Combine(d, "asset.cel")));
        Assert.Equal(js, File.ReadAllBytes(Path.Combine(d, "asset.json")));
        Assert.False(File.Exists(Path.Combine(d, "asset.cel.bak")));
        Assert.Empty(Directory.GetDirectories(st.Dir, ".tmp-*"));
        Assert.Equal("agv_a", st.Find(a.Id)!.Name);
    }

    [Fact] public void Unsupported_Extension_Rejected() =>
        Assert.Throws<NotSupportedException>(() => st.Create("T_AGV", "x", "", 0, stlPath, "x.dxf", new SimplifyOptions(), "a"));

    [Fact] public void Bad_Direction_And_Empty_Type_Rejected()
    {
        Assert.Throws<ArgumentException>(() => st.Create("T_AGV", "x", "", 45, stlPath, "x.stl", new SimplifyOptions(), "a"));
        Assert.Throws<ArgumentException>(() => st.Create("", "x", "", 0, stlPath, "x.stl", new SimplifyOptions(), "a"));
    }

    [Fact] public void Corrupt_Asset_Json_Is_Skipped()
    {
        var a = Make();
        File.WriteAllText(Path.Combine(st.Dir, a.Id, "asset.json"), "{ not json");
        Assert.Empty(new AssetStore(dir, NullLogger<AssetStore>.Instance).All());
    }

    [Fact] public void Path_Rejects_Unknown_Id_Or_File()
    {
        var a = Make();
        Assert.EndsWith("asset.cel", st.Path(a.Id, "asset.cel"));
        Assert.Throws<ArgumentException>(() => st.Path("s-nope", "asset.cel"));
        Assert.Throws<ArgumentException>(() => st.Path(a.Id, "../x"));
    }
}
