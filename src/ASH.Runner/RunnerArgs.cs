using System.Globalization;

namespace ASH.Runner;

/// <param name="DialogButton">Null (default) never clicks AutoMod dialogs; a trial install needs "Student Version".</param>
/// <param name="Append">Keep an existing runs\ folder instead of rotating it to runs.&lt;yyyyMMdd_HHmmss&gt;\.</param>
public sealed record RunnerArgs(string ProjectDir, string Model, string? AmHome = null, int Parallel = 2, string? DialogButton = null,
    int TimeoutMin = 30, int InitialReps = 1, bool Impact = false, bool Debug = false, bool Append = false, string? Server = null, bool NoUpdate = false)
{
    public const string Usage = "usage: ash-run <ProjectDir> --model <name> [--am-home <AutoMod dir>] [--parallel 2] [--dialog-button \"Student Version\"] [--timeout-min 30] [--initial-reps 1] [--impact] [--append] [--debug] [--server <http://host:port>] [--no-update]\n       ash-run --update [--server <http://host:port>]   (check the server and update ash-run now)\n       ash-run --version";

    /// <summary>Null result = error (message in <paramref name="error"/>).</summary>
    public static RunnerArgs? Parse(string[] args, out string? error)
    {
        error = null;
        string? dir = null, model = null, am = null, btn = null, server = null;
        int parallel = 2, timeout = 30, reps = 1;
        bool impact = false, debug = false, append = false, noUpdate = false;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            string? err = null;
            string? Val()
            {
                if (i + 1 >= args.Length) { err = a + " needs a value"; return null; }
                return args[++i];
            }
            int? Int(int min)
            {
                var v = Val();
                if (v == null) return null;
                if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < min) { err = $"{a}: expected an integer >= {min}, got '{v}'"; return null; }
                return n;
            }
            switch (a)
            {
                case "--model": model = Val(); break;
                case "--am-home": am = Val(); break;
                case "--dialog-button": btn = Val(); if (btn == "") btn = null; break;
                case "--parallel": parallel = Int(1) ?? parallel; break;
                case "--timeout-min": timeout = Int(1) ?? timeout; break;
                case "--initial-reps": reps = Int(1) ?? reps; break;
                case "--impact": impact = true; break;
                case "--no-impact": impact = false; break;
                case "--append": append = true; break;
                case "--debug": debug = true; break;
                case "--server": server = Val(); break;
                case "--no-update": noUpdate = true; break;
                default:
                    if (a.StartsWith("--")) err = "unknown option " + a;
                    else if (dir == null) dir = a;
                    else err = "unexpected argument " + a;
                    break;
            }
            if (err != null) { error = err; return null; }
        }
        if (dir == null) { error = "ProjectDir is required"; return null; }
        if (string.IsNullOrEmpty(model)) { error = "--model is required"; return null; }
        return new RunnerArgs(dir, model, am, parallel, btn, timeout, reps, impact, debug, append, server, noUpdate);
    }
}
