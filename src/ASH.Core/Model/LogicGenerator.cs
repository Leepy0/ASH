using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ASH.Core.Asy;
using ASH.Core.Input;

namespace ASH.Core.Model;

/// <summary>StandbyCpByGroup is not referenced by the logic (vehicles start on the AGVS namelist); kept for the builder's checks.</summary>
public sealed record LayoutInfo(IReadOnlyList<string> CpNames, IReadOnlyList<string> BlockNames,
    IReadOnlyDictionary<string, string> StandbyCpByGroup, string AgvSystemName = "agv");

public static class LogicGenerator
{
    static string Res(string name)
    {
        using var s = typeof(LogicGenerator).Assembly.GetManifestResourceStream("ASH.Core.Model.Templates." + name)
            ?? throw new InvalidOperationException("missing template " + name);
        using var r = new StreamReader(s);
        return r.ReadToEnd().Replace("\r\n", "\n");
    }

    /// <summary>Hook bodies for a fresh user_ext.m (all return true).</summary>
    public static string DefaultUserExt => Res("user_ext.m.txt");

    static string N(double d) => AsyWriter.Num(d);

    /// <summary>Text placed inside an AM string literal or used as an entity name: entity-name rules plus no quote/backslash.</summary>
    static string Lit(string s)
    {
        if (!Layout.LayoutChecker.IsValidName(s) || s.IndexOf('"') >= 0 || s.IndexOf('\\') >= 0) throw new ArgumentException("invalid name for logic: " + s);
        return s;
    }

    static string Cp(LayoutInfo l, string cp)
    {
        if (!l.CpNames.Contains(cp)) throw new ArgumentException("route CP not in layout: " + cp);
        return cp;
    }

    static string Fill(string t, params (string Key, string Value)[] kv)
    {
        foreach (var (k, v) in kv) t = t.Replace("{{" + k + "}}", v);
        return t;
    }

    public static string Render(ProjectInput input, LayoutInfo layout)
    {
        if (input.Groups.Count == 0 || input.Materials.Count == 0) throw new ArgumentException("logic needs at least one group and one material");
        var sys = Lit(layout.AgvSystemName);
        var types = new HashSet<string>();
        var groupLogic = new StringBuilder();
        var snap = new StringBuilder();
        for (int i = 0; i < input.Groups.Count; i++)
        {
            var g = input.Groups[i];
            // one AGVS vehicle type per group: vehicle procedures are defined per type
            if (!types.Add(g.Type)) throw new ArgumentException("vehicle type used by two groups: " + g.Type);
            int gi = i + 1;
            groupLogic.Append(Fill(Res("code_group.m.txt"), ("G", gi.ToString()), ("TYPE", Lit(g.Type)), ("SYS", sys)));
            snap.Append($"\tset V_tmp to {g.MaxCount} - V_parked({gi})\n");
            snap.Append($"\tprint \"group\\t{Lit(g.Group)}\\t{g.Group}\\t\" V_tmp \"\\t0\\t0\\t0\\t0\\t0\\t0\\t0\" to V_out\n");
        }

        var matLogic = new StringBuilder();
        var create = new StringBuilder();
        for (int i = 0; i < input.Materials.Count; i++)
        {
            var m = input.Materials[i];
            int mi = i + 1;
            var r = input.Routes.FirstOrDefault(x => x.Material == m.Material) ?? throw new ArgumentException("no route for material " + m.Material);
            var g = input.Groups.FirstOrDefault(x => x.Group == m.Group) ?? throw new ArgumentException("unknown group " + m.Group);
            var t = input.AgvTypes.FirstOrDefault(x => x.Type == g.Type) ?? throw new ArgumentException("unknown AGV type " + g.Type);
            string Leg(string to) => Fill(Res("code_leg.m.txt"), ("M", mi.ToString()), ("SYS", sys), ("TO", Cp(layout, to)));
            var legs = new StringBuilder();
            if (!string.IsNullOrEmpty(r.BufferCP))
            {
                legs.Append(Leg(r.BufferCP!));
                legs.Append($"\twhile V_stock({mi}) > V_unload({mi}) do begin\n\t\twait for 5 sec\n\tend\n");
            }
            legs.Append(Leg(r.SupplyToCP));
            var ret = new StringBuilder();
            if (m.EmptyReturn)
                foreach (var cp in new[] { r.ReturnFromCP, r.ReturnToCP })
                    if (!string.IsNullOrEmpty(cp)) ret.Append(Leg(cp!));
            matLogic.Append(Fill(Res("code_material.m.txt"), ("M", mi.ToString()), ("NAME", Lit(m.Material)), ("SYS", sys),
                ("FROM", Cp(layout, r.SupplyFromCP)), ("LOAD_TIME", N(t.LoadTime)), ("UNLOAD_TIME", N(t.UnloadTime)),
                ("SUPPLY_LEGS", legs.ToString().TrimEnd('\n')), ("RETURN_LEGS", ret.ToString().TrimEnd('\n'))));
            create.Append($"\tcreate 1 load of type L_ctl to P_consume_{mi}\n");

            snap.Append($"\tif V_samples({mi}) > 0 then set V_x to V_stockSum({mi}) / V_samples({mi})\n\telse set V_x to V_stock({mi})\n");
            snap.Append($"\tif V_samples({mi}) = 0 then set V_minStock({mi}) to V_stock({mi})\n");
            snap.Append($"\tset V_y to (V_simLen - V_warmup) / V_takt({mi}) * V_need({mi})\n");
            snap.Append($"\tprint \"material\\t{m.Material}\\t{Lit(m.Group)}\\t\" V_minStock({mi}) \"\\t\" V_x \"\\t\" V_supplies({mi}) \"\\t\" V_y \"\\t\" V_stop({mi}) \"\\t0\\t0\\t0\" to V_out\n");
            // a supply leg still running at the end counts as stuck once it is over the limit
            snap.Append($"\tif V_inLeg({mi}) = 1 and ac - V_legStart({mi}) > V_deadlockSec then inc V_deadlock by 1\n");
        }
        foreach (var b in layout.BlockNames)
            snap.Append($"\tprint \"block\\t{Lit(b)}\\t\\t\" {b} total \"\\t\" {b} average waiting time \"\\t0\\t\" {b} utilization \"\\t0\\t0\\t0\\t0\" to V_out\n");

        return Fill(Res("code.m.txt"), ("GROUP_COUNT", input.Groups.Count.ToString()), ("MATERIAL_COUNT", input.Materials.Count.ToString()),
            ("CREATE_CONSUMERS", create.ToString().TrimEnd('\n')), ("GROUP_LOGIC", groupLogic.ToString()), ("MATERIAL_LOGIC", matLogic.ToString()),
            ("SNAP_ROWS", snap.ToString().TrimEnd('\n')));
    }

    /// <summary>The load-centric logic needs AM's own dispatch: every CP gets a work list searching all CPs where a load waits for
    /// pickup (namelist nl_pickup), every non-standby CP a park list to the standby namelist, and every pickup CP a load search list.</summary>
    // shortcut: park lists go to the first group's standby namelist and any vehicle type serves any material; per-group dispatch needs per-type lists (multi-group projects).
    public static void AddDispatchLists(AgvsSystem s, ProjectInput input, LayoutInfo layout)
    {
        var pickup = new List<string>();
        void Add(string? cp) { if (!string.IsNullOrEmpty(cp) && !pickup.Contains(cp!)) pickup.Add(Cp(layout, cp!)); }
        foreach (var m in input.Materials)
        {
            var r = input.Routes.FirstOrDefault(x => x.Material == m.Material) ?? throw new ArgumentException("no route for material " + m.Material);
            Add(r.SupplyFromCP); Add(r.BufferCP);
            if (m.EmptyReturn && !string.IsNullOrEmpty(r.ReturnFromCP)) { Add(r.SupplyToCP); Add(r.ReturnFromCP); }
        }
        s.NameLists["nl_pickup"] = pickup;
        var park = "nl_" + input.Groups[0].Group;
        var standby = new HashSet<string>(s.NameLists.Where(kv => kv.Key.StartsWith("nl_") && kv.Key != "nl_pickup").SelectMany(kv => kv.Value));
        foreach (var c in s.ControlPoints)
        {
            s.WorkListLines.Add($"WORKLST name {c.Name} Oldest item nl_pickup ");
            if (!standby.Contains(c.Name)) s.ParkListLines.Add($"PARKLST name {c.Name} Sequential item {park} ");
        }
        // without a search list a waiting load never wakes an idle (parked) vehicle; it waits until one happens to finish a job
        foreach (var cp in pickup) s.SearchListLines.Add($"SRCHLST name {cp} Closest item All ");
    }

    /// <summary>Entities the logic references. Blocks are not added (LayoutInfo has names only): the caller adds a BlockEntity per layout.BlockNames.
    /// UserExtSource holds the default hooks; the builder replaces it with the project's user_ext.m.</summary>
    public static ProcessModel BuildProcessModel(ProjectInput input, LayoutInfo layout, string units)
    {
        var m = new ProcessModel { Units = units, LogicSource = Render(input, layout), UserExtSource = DefaultUserExt };
        int nm = input.Materials.Count;
        // arrays get at least 2 elements: AM mishandles 1-element arrays (seen with BLOCK n 1)
        int gs = Math.Max(2, input.Groups.Count), ms = Math.Max(2, nm);
        m.LoadTypes.Add("L_ctl");
        for (int i = 1; i <= nm; i++) { m.Procs.Add(new($"P_consume_{i}")); m.Procs.Add(new($"P_supply_{i}")); }
        m.Queues.Add(new("Q_park"));
        m.Orders.Add("OL_forever");
        for (int i = 1; i <= nm; i++) m.Orders.Add($"OL_refill_{i}");
        m.Atts.Add(new("A_index", "Integer")); m.Atts.Add(new("A_t0", "Real"));
        m.Vars.Add(new("V_fp", "FilePtr")); m.Vars.Add(new("V_out", "FilePtr")); m.Vars.Add(new("V_tok", "String"));
        foreach (var v in new[] { "V_seed", "V_n", "V_i", "V_tmp", "V_deadlock", "V_extMat" }) m.Vars.Add(new(v, "Integer"));
        foreach (var v in new[] { "V_simLen", "V_warmup", "V_x", "V_y" }) m.Vars.Add(new(v, "Real"));
        m.Vars.Add(new("V_deadlockSec", "Real", 0, "600"));
        foreach (var v in new[] { "V_active", "V_vehCount", "V_parked" }) m.Vars.Add(new(v, "Integer", gs));
        foreach (var v in new[] { "V_pending", "V_samples", "V_supplies", "V_inLeg" }) m.Vars.Add(new(v, "Integer", ms));
        foreach (var v in new[] { "V_call", "V_unload", "V_stock", "V_safety", "V_takt", "V_perCycle", "V_perContainer", "V_need",
                     "V_minStock", "V_stockSum", "V_stop", "V_legStart" })
            m.Vars.Add(new(v, "Real", ms));
        m.Funcs.AddRange(new[] { "F_ext_on_call", "F_ext_select_vehicle", "F_ext_before_unload", "F_ext_on_snap" });
        return m;
    }
}
