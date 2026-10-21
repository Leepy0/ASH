using System.Diagnostics;

namespace ASH.Server;

/// <summary>--install / --uninstall via sc.exe (needs an elevated prompt).</summary>
internal static class ServiceCommand
{
    public const string ServiceName = "ASH.Server";

    /// <summary>
    /// sc.exe arguments for create. binPath carries literal quotes so a path with spaces cannot be hijacked
    /// (unquoted service path, CWE-428); ArgumentList makes the framework escape them for the child's command line.
    /// </summary>
    public static IReadOnlyList<string> BuildCreateArgs(string exe) =>
        new[] { "create", ServiceName, "binPath=", $"\"{exe}\"", "start=", "auto", "DisplayName=", "ASH Server" };

    public static int Run(bool install)
    {
        var exe = Environment.ProcessPath ?? "";
        if (install && !exe.EndsWith("ASH.Server.exe", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("ASH.Server.exe 로 실행했을 때만 서비스로 등록할 수 있습니다(배포 zip의 exe).");
            return 1;
        }
        var code = Sc(install ? BuildCreateArgs(exe) : new[] { "delete", ServiceName });
        if (code == 5) Console.Error.WriteLine("접근이 거부되었습니다. 관리자 권한 명령 프롬프트에서 다시 실행하세요.");
        else if (code == 0 && install)
        {
            Console.WriteLine("서비스 등록 완료. 아래 BINARY_PATH_NAME 이 따옴표로 감싸져 있는지 확인하세요.");
            Sc(new[] { "qc", ServiceName });
            Console.WriteLine($"시작: sc start {ServiceName}  (방화벽 TCP 8082 허용, appsettings.json의 Admin:Token 16자 이상 설정 필요)");
        }
        else if (code == 0) Console.WriteLine("서비스를 제거했습니다.");
        return code;
    }

    // sc.exe prints its own messages (inherited console)
    private static int Sc(IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo("sc.exe") { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode;
    }
}
