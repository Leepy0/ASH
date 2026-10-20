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
        string N(double d) => d.ToString("R", CultureInfo.InvariantCulture);
        void L(params object?[] xs) => sb.Append(string.Join("\t", xs.Select(x => x is double d ? N(d) : x?.ToString() ?? ""))).Append('\n');
        L("settings", p.Settings.Units, p.Settings.SimLengthSec);
        foreach (var t in p.AgvTypes) L("type", t.Type, t.SpeedLoaded, t.LoadTime, t.UnloadTime, t.Length, t.Width);
        foreach (var g in p.Groups) L("group", g.Group, g.Type, g.MaxCount, string.Join(",", g.StandbyCPs));
        foreach (var m in p.Materials) L("material", m.Material, m.Group, m.EmptyReturn);
        foreach (var r in p.Routes) L("route", r.Material, r.SupplyFromCP, r.SupplyToCP, r.BufferCP, r.ReturnFromCP, r.ReturnToCP, r.ExtraTime);
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
