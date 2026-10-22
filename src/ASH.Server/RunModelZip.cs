using System.IO.Compression;
using System.Text;
using ASH.Core.Input;
using ASH.Core.Model;

namespace ASH.Server;

/// <summary>
/// One run to watch on a PC with AutoMod: the model as the server built it (&lt;Model&gt;.exe and &lt;Model&gt;.dir, when still there) and &lt;Model&gt;.arc with
/// that run's params.txt, plus run.bat and README.txt. The .dir is small (C sources, objs, the model dll and its .sys/.mod data, about 1 MB for the demo), and
/// with it the PC does not need to build. After JobRetention purged the build output, or for a job run on a PC (no build uploaded), run.bat builds from the .arc first.
/// </summary>
public static class RunModelZip
{
    public static bool IsValidRunId(string run) => run.Length is > 0 and <= 200 && run.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static async Task WriteAsync(Stream output, string projectDir, string model, ProjectInput input, RunScenario s, string jobId, string runId, string amHome)
    {
        await using var zip = await ZipArchive.CreateAsync(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null);
        var exe = Path.Combine(projectDir, model + ".exe");
        bool built = File.Exists(exe) && Directory.Exists(Path.Combine(projectDir, model + ".dir"));
        if (built) await zip.CreateEntryFromFileAsync(exe, model + ".exe", CompressionLevel.Fastest);
        foreach (var sub in built ? new[] { model + ".arc", model + ".dir" } : new[] { model + ".arc" })
        {
            var dir = Path.Combine(projectDir, sub);
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(dir, f).Replace('\\', '/');
                if (sub.EndsWith(".arc") && rel.Equals("params.txt", StringComparison.OrdinalIgnoreCase)) continue; // replaced by this run's below
                await zip.CreateEntryFromFileAsync(f, sub + "/" + rel, CompressionLevel.Fastest);
            }
        }
        await Text(zip, model + ".arc/params.txt", ParamsFile.Render(input, s), bom: false); // the model reads it as ModelRunner writes it
        await Text(zip, "run.bat", RunBat(model, amHome), bom: false);
        await Text(zip, "README.txt", Readme(model, input, s, jobId, runId, built, amHome), bom: true);
    }

    static async Task Text(ZipArchive zip, string name, string text, bool bom)
    {
        await using var st = await zip.CreateEntry(name, CompressionLevel.Fastest).OpenAsync();
        var bytes = (bom ? new UTF8Encoding(true).GetPreamble() : Array.Empty<byte>()).Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        await st.WriteAsync(bytes);
    }

    /// <summary>ASCII only: cmd reads a .bat in the OEM code page. No -w, so AutoMod opens its simulation window (as AmBuilder.Launch).</summary>
    internal static string RunBat(string model, string amHome) => string.Join("\r\n",
        "@echo off",
        "setlocal",
        "cd /d \"%~dp0\"",
        "rem AutoMod folder: set AMHOME before running, or edit this line",
        $"if \"%AMHOME%\"==\"\" set \"AMHOME={amHome}\"",
        "if not exist \"%AMHOME%\\bin\" (echo AutoMod not found: %AMHOME%  - set AMHOME to the AutoMod folder & pause & exit /b 1)",
        "set \"PATH=%AMHOME%\\bin;%PATH%\"",
        $"if exist \"{model}.exe\" goto run",
        "echo Building the model with AutoMod...",
        $"if exist \"{model}.dir\" rmdir /s /q \"{model}.dir\"", // as ModelBuilder.PrepareBuildDir: a build next to an old .dir does not relink
        "set \"AMB=%AMHOME%\\bin\\am264.exe\"",
        "if not exist \"%AMB%\" set \"AMB=%AMHOME%\\bin\\am2.exe\"",
        $"\"%AMB%\" -bq {model}",
        $"if not exist \"{model}.exe\" (echo Build failed: see {model}.dir\\make.err & pause & exit /b 1)",
        ":run",
        $"start \"\" \"{model}.exe\"",
        "");

    static string Readme(string model, ProjectInput input, RunScenario s, string jobId, string runId, bool built, string amHome) => string.Join("\r\n", new[]
    {
        $"ASH 작업 {jobId} · 실행 {runId}",
        "",
        "이 실행을 AutoMod 창에서 그대로 보려면:",
        "1. 이 zip을 빈 폴더에 모두 풉니다.",
        "2. run.bat을 두 번 누릅니다. AutoMod 시뮬레이션 창이 뜹니다(서버처럼 -w로 숨겨 돌리지 않습니다).",
        $"   AutoMod가 {amHome}에 없으면 run.bat의 AMHOME 줄을 고치거나 환경 변수 AMHOME을 설정하세요.",
        "",
        built
            ? $"서버에서 빌드한 {model}.exe와 {model}.dir을 그대로 넣었습니다. PC의 AutoMod 버전이 서버와 달라 창이 뜨지 않으면 {model}.exe를 지우고 run.bat을 다시 실행하세요(이 PC에서 다시 빌드합니다)."
            : $"빌드 결과({model}.exe, {model}.dir)는 보관 기간이 지나 정리되었거나 PC에서 돌린 결과라 없습니다. run.bat이 처음에 {model}.arc로 빌드합니다.",
        "",
        "이 실행의 조건 (" + model + ".arc\\params.txt에 들어 있습니다):",
        $"  시드 {s.Seed}" + (s.Debug ? " (디버그 실행)" : ""),
    }.Concat(input.Groups.Select(g => $"  {g.Group}: {(s.ActiveByGroup.TryGetValue(g.Group, out var n) ? n : g.MinCount)}대"))
     .Concat(new[] { "", "같은 시드와 대수로 돌므로 서버의 그 실행과 같은 흐름을 봅니다. 결과(result.tsv, " + model + ".report)는 이 폴더에 생깁니다.", "" }));
}
