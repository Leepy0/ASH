using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ASH.Core.Update;

/// <summary>
/// Texts and arguments for the Setup exe checking github.com itself (no ZWCAD, no ASH.Server).
/// The fetch and the verified download are <see cref="UpdateClient.GitHub"/> + <see cref="UpdateFlow.CheckAsync"/> / <see cref="UpdateClient.DownloadVerifiedAsync"/>.
/// </summary>
public static class SetupUpdate
{
    /// <summary>Given to the downloaded Setup so it does not check again (no download loop).</summary>
    public const string SkipCheckArg = "/noupdatecheck";

    public static bool IsNewer(UpdateStatus s) => s.State == UpdateState.Available || s.State == UpdateState.BelowMinimum;

    public static string Checking(string current) => $"설치 파일 버전 {Show(current)} / GitHub 최신 확인 중…";

    public static string Result(string current, UpdateResult r)
    {
        var head = $"설치 파일 버전 {Show(current)} / GitHub 최신 {Show(r.Fetched.Manifest.Version)}";
        switch (r.Status.State)
        {
            case UpdateState.Available:
            case UpdateState.BelowMinimum: return head + " — 새 버전이 있습니다";
            case UpdateState.UpToDate: return head + " — 최신입니다";
            case UpdateState.ServerOlder: return head + " — 이 설치 파일이 더 새 버전입니다";
            default: return head + " — " + r.Status.Message;
        }
    }

    /// <summary>Check failed: the embedded version still installs, so the note says so.</summary>
    public static string Failed(string current, Exception e) =>
        $"설치 파일 버전 {Show(current)} / GitHub 최신 확인 실패({UpdateMessages.Reason(e)}) — 이 버전으로 설치할 수 있습니다";

    /// <summary>This run's arguments for the downloaded Setup, plus <see cref="SkipCheckArg"/>; hidden /capture is dropped.</summary>
    public static string RelaunchArguments(IEnumerable<string> args) =>
        string.Join(" ", args.Where(a => !a.Equals(SkipCheckArg, StringComparison.OrdinalIgnoreCase) && !a.StartsWith("/capture:", StringComparison.OrdinalIgnoreCase))
            .Concat(new[] { SkipCheckArg }).Select(Quote));

    // CommandLineToArgvW rules: backslashes are literal unless they precede a quote
    internal static string Quote(string a)
    {
        if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a;
        var sb = new StringBuilder("\"");
        int bs = 0;
        foreach (var ch in a)
        {
            if (ch == '\\') { bs++; continue; }
            sb.Append('\\', ch == '"' ? bs * 2 + 1 : bs).Append(ch);
            bs = 0;
        }
        return sb.Append('\\', bs * 2).Append('"').ToString();
    }

    private static string Show(string v) => SemVersion.TryParse(v, out var s) ? s!.ToString() : v;
}
