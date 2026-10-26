using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ASH.Core.Input;

namespace ASH.Core.Model;

/// <summary>Fingerprint of the inputs baked into a built model (logic, vehicles, model.amo). Run-time values that go through params.txt
/// (takt, thresholds, stocks, warmup, replications...) are left out, so changing them needs no rebuild.</summary>
public static class InputSignature
{
    public const string FileName = "input.sig";

    public static string Compute(ProjectInput p)
    {
        var sb = new StringBuilder();
        // G15, not R: net48 (plugin builds) and net10 (ash-run compares) can round-trip doubles to different digits
        string N(double d) => d.ToString("G15", CultureInfo.InvariantCulture);
        void L(params object?[] xs) => sb.Append(string.Join("\t", xs.Select(x => x is double d ? N(d) : x?.ToString() ?? ""))).Append('\n');
        L("settings", p.Settings.Units, p.Settings.SimLengthSec);
        // curve speeds only when set (> 0; null and 0 mean "same as straight"): models made before them keep their signature
        foreach (var t in p.AgvTypes)
            if (t.SpeedLoadedCurve > 0 || t.SpeedEmptyCurve > 0) L("type", t.Type, t.SpeedLoaded, t.LoadTime, t.UnloadTime, t.Length, t.Width, t.SpeedLoadedCurve ?? 0, t.SpeedEmptyCurve ?? 0);
            else L("type", t.Type, t.SpeedLoaded, t.LoadTime, t.UnloadTime, t.Length, t.Width);
        // FinishCP only when set: models made before it existed keep their signature
        foreach (var g in p.Groups) { if (string.IsNullOrEmpty(g.FinishCP)) L("group", g.Group, g.Type, g.MaxCount, string.Join(",", g.StandbyCPs)); else L("group", g.Group, g.Type, g.MaxCount, string.Join(",", g.StandbyCPs), g.FinishCP); }
        // Output swaps the container pictures in the logic; written only then, so models built before Flow keep their signature
        foreach (var m in p.Materials) { if (m.IsOutput) L("material", m.Material, m.Group, m.EmptyReturn, "Output"); else L("material", m.Material, m.Group, m.EmptyReturn); }
        // BufferWaitSec only when set: models made before it existed keep their signature
        foreach (var r in p.Routes)
            if (r.BufferWaitSec > 0) L("route", r.Material, r.SupplyFromCP, r.SupplyToCP, r.BufferCP, r.ReturnFromCP, r.ReturnToCP, r.ExtraTime, r.BufferWaitSec);
            else L("route", r.Material, r.SupplyFromCP, r.SupplyToCP, r.BufferCP, r.ReturnFromCP, r.ReturnToCP, r.ExtraTime);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").ToLowerInvariant();
    }

    public static void Write(string arcDir, ProjectInput p) => File.WriteAllText(Path.Combine(arcDir, FileName), Compute(p) + "\n");

    /// <summary>Null when the model has no input.sig (built before signatures existed).</summary>
    public static string? Read(string arcDir)
    {
        var f = Path.Combine(arcDir, FileName);
        return File.Exists(f) ? File.ReadAllText(f).Trim() : null;
    }
}
