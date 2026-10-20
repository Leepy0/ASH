using System;
using System.IO;
using ASH.Core.Asy;

namespace ASH.Core.Model;

public static class ProcessAsyWriter
{
    static string Res(string name)
    {
        using var s = typeof(ProcessAsyWriter).Assembly.GetManifestResourceStream("ASH.Core.Model.Templates." + name)
            ?? throw new InvalidOperationException("missing template " + name);
        using var r = new StreamReader(s);
        return r.ReadToEnd().Replace("\r\n", "\n");
    }

    public static void WriteFile(ProcessModel m, string path)
    {
        using var w = new StreamWriter(path, false, new System.Text.UTF8Encoding(false)) { NewLine = "\n" };
        Write(m, w);
    }

    // shortcut: header is the demo's (Feet Seconds); no RSRC/COUNT/CONTAINER/RCYCLE entities. Add when a model needs them.
    public static void Write(ProcessModel m, TextWriter w)
    {
        w.NewLine = "\n";
        string N(double d) => AsyWriter.Num(d);
        w.Write(Res("process_header.txt"));
        w.WriteLine("PROCDEF");
        foreach (var p in m.Procs) w.WriteLine($"PROC name {p.Name} 0 traf Infinite nextproc die");
        foreach (var l in m.LoadTypes) { w.WriteLine($"LDTYPE name {l} 0"); w.Write(Res("ldtype_template.txt")); }
        foreach (var q in m.Queues) { w.WriteLine($"QUEUE name {q.Name} {q.Count} cap {q.Cap}"); w.WriteLine(); w.WriteLine("\tUserDef"); w.WriteLine(); }
        int li = 0;
        foreach (var l in m.Labels)
        {
            w.WriteLine($"LABEL name {l} 0");
            w.WriteLine($" dis 0 picpos begx 30 begy {N(50 - 5 * li++)} endx 31 endy {N(50 - 5 * (li - 1))} scx 3 scy 3 scz 3");
            w.WriteLine();
            w.Write(Res("label_template.txt").Replace("{TEXT}", l));
        }
        foreach (var b in m.Blocks)
        {
            // AM segfaults on `BLOCK n 1` (1-element array); a single block is `0` + `dis 0`.
            // AM segfaults on `BLOCK n 1` (1-element array); a single block is `0` + `dis 0`.
            // shortcut: block template assumed to be a 1x1 box centred on picpos beg; scx/scy stretch it over the rect. Verify visually.
            double cx = (b.Rect.Min.X + b.Rect.Max.X) / 2, cy = (b.Rect.Min.Y + b.Rect.Max.Y) / 2;
            w.WriteLine($"BLOCK name {b.Name} 0 cap {b.Cap}");
            w.WriteLine($" dis 0 picpos begx {N(cx)} begy {N(cy)} endx {N(cx + 1)} endy {N(cy)} scx {N(b.Rect.Max.X - b.Rect.Min.X)} scy {N(b.Rect.Max.Y - b.Rect.Min.Y)} scz 1");
            w.WriteLine();
            w.Write(Res("block_template.txt"));
        }
        foreach (var o in m.Orders) w.WriteLine($"ORDER name {o} 0");
        foreach (var t in m.Tables) w.WriteLine($"TABLE name {t} 0 bins 1 0 1");
        foreach (var a in m.Atts) w.WriteLine($"ATT name {a.Name} 0 type {a.Type}");
        foreach (var v in m.Vars)
            w.WriteLine(v.ArraySize > 0 ? $"VAR name {v.Name} 1 {v.ArraySize} type {v.Type}"
                                        : $"VAR name {v.Name} 0 type {v.Type}" + (v.Init is null ? "" : " " + v.Init));
        foreach (var r in m.RnStreams)
        {
            w.WriteLine($"RNSTREAM {r.Name} 0 type CMRG flags 1");
            w.WriteLine("\tcmrgseed 1 " + string.Join(" ", r.Seed, r.Seed, r.Seed, r.Seed, r.Seed, r.Seed));
        }
        foreach (var f in m.Funcs) w.WriteLine($"FUNC name {f} type Integer");
        w.WriteLine("SFileBegin\tname code.m");
        w.Write(m.LogicSource.TrimEnd('\r', '\n').Replace("\r\n", "\n"));
        w.WriteLine();
        w.WriteLine();
        w.WriteLine("#@!");
    }
}
