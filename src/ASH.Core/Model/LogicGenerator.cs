using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ASH.Core.Asy;
using ASH.Core.Input;

namespace ASH.Core.Model;

/// <summary>StandbyCpByGroup is the fallback standby CP for a group whose AgvGroup.StandbyCPs is empty.
/// EntryBlockByCp maps a traffic-entry CP (traffic.tsv entry_cp) to its block, for the blockgroup wait rows.</summary>
public sealed record LayoutInfo(IReadOnlyList<string> CpNames, IReadOnlyList<string> BlockNames,
    IReadOnlyDictionary<string, string> StandbyCpByGroup, string AgvSystemName = "agv",
    IReadOnlyDictionary<string, string>? EntryBlockByCp = null);

/// <summary>Seeding: the logic draws no random numbers, so a run is fully determined by params.txt (seeded init stocks).
/// AM-side RNSTREAM seeding is not needed until randomness (MTBF/MTTR, phase 3) exists.</summary>
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

    /// <summary>Text placed inside an AM string literal or used as an entity name. Last guard only: InputValidator reports bad names
    /// with their Excel row first.</summary>
    static string Lit(string s)
    {
        if (!InputValidator.IsAmText(s)) throw new ArgumentException($"로직에 쓸 수 없는 이름입니다: '{s}'");
        return s;
    }

    static string Cp(LayoutInfo l, string cp)
    {
        if (!l.CpNames.Contains(cp)) throw new ArgumentException($"CP '{cp}'이(가) 레이아웃에 없습니다.");
        return cp;
    }

    static string Fill(string t, params (string Key, string Value)[] kv)
    {
        foreach (var (k, v) in kv) t = t.Replace("{{" + k + "}}", v);
        return t;
    }

    static void Check(ProjectInput input)
    {
        if (input.Groups.Count == 0 || input.Materials.Count == 0) throw new ArgumentException("그룹과 자재가 1개 이상 있어야 합니다.");
        var types = new Dictionary<string, string>();
        foreach (var g in input.Groups)
        {
            // vehicle procedures and dispatch filters are defined per AGVS vehicle type
            if (types.TryGetValue(g.Type, out var other)) throw new ArgumentException($"그룹 '{g.Group}'과(와) '{other}'이(가) 같은 Type '{g.Type}'을(를) 씁니다.");
            types[g.Type] = g.Group;
        }
        foreach (var m in input.Materials)
            if (!input.Groups.Any(g => g.Group == m.Group)) throw new ArgumentException($"자재 '{m.Material}'의 Group '{m.Group}'이(가) 없습니다.");
    }

    static int GroupIndex(ProjectInput input, string group) => input.Groups.FindIndex(g => g.Group == group) + 1;

    static Route RouteOf(ProjectInput input, MaterialInfo m) =>
        input.Routes.FirstOrDefault(x => x.Material == m.Material) ?? throw new ArgumentException($"자재 '{m.Material}'의 Routes 행이 없습니다.");

    static List<string> StandbyOf(AgvGroup g, LayoutInfo l)
    {
        var s = g.StandbyCPs.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Cp(l, x)).ToList();
        if (s.Count == 0 && l.StandbyCpByGroup.TryGetValue(g.Group, out var cp)) s.Add(Cp(l, cp));
        if (s.Count == 0) throw new ArgumentException($"그룹 '{g.Group}'에 대기(standby) CP가 없습니다.");
        return s;
    }

    public static string Render(ProjectInput input, LayoutInfo layout)
    {
        Check(input);
        var sys = Lit(layout.AgvSystemName);
        var groupLogic = new StringBuilder();
        var snap = new StringBuilder();
        for (int i = 0; i < input.Groups.Count; i++)
        {
            var g = input.Groups[i];
            int gi = i + 1;
            var parkOk = string.Join("\n", StandbyOf(g, layout).Select(cp => $"\tif parkLoc = {sys}:{cp} then return true"));
            groupLogic.Append(Fill(Res("code_group.m.txt"), ("G", gi.ToString()), ("TYPE", Lit(g.Type)), ("SYS", sys), ("PARK_OK", parkOk)));
            // value1 = the requested active count; Max - parked would count never-woken vehicles as active
            snap.Append($"\tprint \"group\\t{Lit(g.Group)}\\t{g.Group}\\t\" V_active({gi}) \"\\t0\\t0\\t0\\t0\\t0\\t0\\t0\" to V_out\n");
        }

        // blockgroup wait keys (block, group) -> 1-based slot; a leg to a traffic-entry CP counts for that CP's block, any other leg for "-".
        // shortcut: v1 approximation, the blocks a leg merely passes through are not attributed; upgrade with per-leg route -> blocks.
        var bgKeys = new List<(string Block, string Group)>();
        int Bg(string cp, string group)
        {
            var block = layout.EntryBlockByCp != null && layout.EntryBlockByCp.TryGetValue(cp, out var b) ? b : "-";
            var key = (block, group);
            var ix = bgKeys.IndexOf(key);
            if (ix < 0) { bgKeys.Add(key); ix = bgKeys.Count - 1; }
            return ix + 1;
        }
        int nm = input.Materials.Count;
        var matLogic = new StringBuilder();
        var create = new StringBuilder();
        for (int i = 0; i < input.Materials.Count; i++)
        {
            var m = input.Materials[i];
            int mi = i + 1;
            var r = RouteOf(input, m);
            var g = input.Groups.First(x => x.Group == m.Group);
            var t = input.AgvTypes.FirstOrDefault(x => x.Type == g.Type) ?? throw new ArgumentException($"그룹 '{g.Group}'의 Type '{g.Type}'이(가) AGV_Types에 없습니다.");
            // leg slot = mi for supply legs, mi + nm for return legs (a return can overlap the next supply of the same material)
            string Leg(string to, int slot) => Fill(Res("code_leg.m.txt"), ("M", mi.ToString()), ("SLOT", slot.ToString()), ("SYS", sys),
                ("TO", Cp(layout, to)), ("BG", Bg(to, m.Group).ToString()));
            var legs = new StringBuilder();
            if (!string.IsNullOrEmpty(r.BufferCP))
            {
                legs.Append(Leg(r.BufferCP!, mi));
                legs.Append($"\twhile V_stock({mi}) > V_unload({mi}) do begin\n\t\twait for 5 sec\n\tend\n");
            }
            legs.Append(Leg(r.SupplyToCP, mi));
            var ret = new StringBuilder();
            if (m.EmptyReturn)
            {
                // empty container: picked up at ReturnFrom (LoadTime), set down at ReturnTo (UnloadTime)
                if (!string.IsNullOrEmpty(r.ReturnFromCP)) ret.Append(Leg(r.ReturnFromCP!, mi + nm)).Append($"\twait for {N(t.LoadTime)} sec\n");
                if (!string.IsNullOrEmpty(r.ReturnToCP)) ret.Append(Leg(r.ReturnToCP!, mi + nm)).Append($"\twait for {N(t.UnloadTime)} sec\n");
            }
            // Route.ExtraTime: once per supply cycle, after the unload, with the vehicle still held
            var extra = r.ExtraTime > 0 ? $"\twait for {N(r.ExtraTime)} sec" : "";
            matLogic.Append(Fill(Res("code_material.m.txt"), ("M", mi.ToString()), ("NAME", Lit(m.Material)), ("SYS", sys),
                ("G", GroupIndex(input, m.Group).ToString()),
                ("FROM", Cp(layout, r.SupplyFromCP)), ("LOAD_TIME", N(t.LoadTime)), ("UNLOAD_TIME", N(t.UnloadTime)),
                ("SUPPLY_LEGS", legs.ToString().TrimEnd('\n')), ("EXTRA_TIME", extra), ("RETURN_LEGS", ret.ToString().TrimEnd('\n'))));
            create.Append($"\tcreate 1 load of type L_ctl to P_consume_{mi}\n");

            // a stop still running at the end of the run counts up to the snap
            snap.Append($"\tif V_stopped({mi}) = 1 then begin\n\t\tif V_stopSince({mi}) < V_warmup then set V_stopSince({mi}) to V_warmup\n" +
                        $"\t\tif ac > V_stopSince({mi}) then inc V_stop({mi}) by ac - V_stopSince({mi})\n\t\tset V_stopSince({mi}) to ac\n\tend\n");
            snap.Append($"\tif V_samples({mi}) > 0 then set V_x to V_stockSum({mi}) / V_samples({mi})\n\telse set V_x to V_stock({mi})\n");
            snap.Append($"\tif V_samples({mi}) = 0 then set V_minStock({mi}) to V_stock({mi})\n");
            snap.Append($"\tset V_y to (V_simLen - V_warmup) / V_takt({mi}) * V_need({mi})\n");
            snap.Append($"\tprint \"material\\t{m.Material}\\t{Lit(m.Group)}\\t\" V_minStock({mi}) \"\\t\" V_x \"\\t\" V_supplies({mi}) \"\\t\" V_y \"\\t\" V_stop({mi}) \"\\t\" V_consumed({mi}) \"\\t0\\t0\" to V_out\n");
            // a leg still running at the end counts as stuck once it is over the limit
            foreach (var slot in new[] { mi, mi + nm })
                snap.Append($"\tif V_inLeg({slot}) = 1 and ac - V_legStart({slot}) > V_deadlockSec then inc V_deadlock by 1\n");
        }
        for (int k = 0; k < bgKeys.Count; k++)
            snap.Append($"\tprint \"blockgroup\\t{Lit(bgKeys[k].Block)}\\t{Lit(bgKeys[k].Group)}\\t\" V_bgWait({k + 1}) \"\\t\" V_bgCnt({k + 1}) \"\\t0\\t0\\t0\\t0\\t0\\t0\" to V_out\n");
        foreach (var b in layout.BlockNames)
            snap.Append($"\tprint \"block\\t{Lit(b)}\\t\\t\" {b} total \"\\t\" {b} average waiting time \"\\t0\\t\" {b} utilization \"\\t0\\t0\\t0\\t0\" to V_out\n");

        return Fill(Res("code.m.txt"), ("GROUP_COUNT", input.Groups.Count.ToString()), ("MATERIAL_COUNT", input.Materials.Count.ToString()),
            ("CREATE_CONSUMERS", create.ToString().TrimEnd('\n')), ("GROUP_LOGIC", groupLogic.ToString()), ("MATERIAL_LOGIC", matLogic.ToString()),
            ("SNAP_ROWS", snap.ToString().TrimEnd('\n')));
    }

    /// <summary>AM dispatch for the load-centric logic. Scheduling lists are per location, not per vehicle type, so the lists hold every
    /// group and the per-type work/load search/park ok functions in code.m filter by group: nl_pick_&lt;G&gt; = the group's pickup CPs
    /// (SupplyFromCP, ReturnFromCP when EmptyReturn); nl_&lt;G&gt; = the group's standby CPs (vehicle start); every CP gets a WORKLST over
    /// all nl_pick lists, every non-standby CP a PARKLST to each group's first standby CP, every pickup CP a SRCHLST.</summary>
    public static void AddDispatchLists(AgvsSystem s, ProjectInput input, LayoutInfo layout)
    {
        Check(input);
        var picks = new List<string>();
        var parks = new List<string>();
        var standbyAll = new HashSet<string>();
        var pickupAll = new List<string>();
        foreach (var g in input.Groups)
        {
            var standby = StandbyOf(g, layout);
            s.NameLists["nl_" + g.Group] = standby;
            standbyAll.UnionWith(standby);
            parks.Add(standby[0]);
            var pick = new List<string>();
            void Add(string? cp) { if (!string.IsNullOrEmpty(cp) && !pick.Contains(cp!)) pick.Add(Cp(layout, cp!)); }
            foreach (var m in input.Materials.Where(x => x.Group == g.Group))
            {
                var r = RouteOf(input, m);
                Add(r.SupplyFromCP);
                if (m.EmptyReturn) Add(r.ReturnFromCP);
            }
            if (pick.Count == 0) continue;
            s.NameLists["nl_pick_" + g.Group] = pick;
            picks.Add("nl_pick_" + g.Group);
            foreach (var cp in pick) if (!pickupAll.Contains(cp)) pickupAll.Add(cp);
        }
        string Items(IEnumerable<string> xs) => string.Concat(xs.Select(x => "item " + x + " "));
        foreach (var c in s.ControlPoints)
        {
            if (picks.Count > 0) s.WorkListLines.Add($"WORKLST name {c.Name} Oldest {Items(picks)}");
            if (!standbyAll.Contains(c.Name)) s.ParkListLines.Add($"PARKLST name {c.Name} Sequential {Items(parks)}");
        }
        // without a search list a waiting load never wakes an idle (parked) vehicle; it waits until one happens to finish a job
        foreach (var cp in pickupAll) s.SearchListLines.Add($"SRCHLST name {cp} Closest item All ");
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
        m.Atts.Add(new("A_index", "Integer")); m.Atts.Add(new("A_grp", "Integer"));
        m.Atts.Add(new("A_veh", "VehiclePtr")); m.Atts.Add(new("A_mv0", "Real")); m.Atts.Add(new("A_w", "Real")); m.Atts.Add(new("A_t0", "Real"));
        m.Vars.Add(new("V_fp", "FilePtr")); m.Vars.Add(new("V_out", "FilePtr")); m.Vars.Add(new("V_tok", "String"));
        foreach (var v in new[] { "V_seed", "V_n", "V_i", "V_tmp", "V_deadlock", "V_extMat", "V_debug" }) m.Vars.Add(new(v, "Integer"));
        foreach (var v in new[] { "V_simLen", "V_warmup", "V_x", "V_y" }) m.Vars.Add(new(v, "Real"));
        m.Vars.Add(new("V_deadlockSec", "Real", 0, "600"));
        foreach (var v in new[] { "V_active", "V_vehCount", "V_parked" }) m.Vars.Add(new(v, "Integer", gs));
        foreach (var v in new[] { "V_pending", "V_samples", "V_supplies", "V_stopped" }) m.Vars.Add(new(v, "Integer", ms));
        foreach (var v in new[] { "V_call", "V_unload", "V_stock", "V_safety", "V_takt", "V_perCycle", "V_perContainer", "V_need",
                     "V_minStock", "V_stockSum", "V_stop", "V_stopSince", "V_consumed" })
            m.Vars.Add(new(v, "Real", ms));
        // leg slots: 1..nm supply legs, nm+1..2nm return legs
        m.Vars.Add(new("V_inLeg", "Integer", 2 * nm)); m.Vars.Add(new("V_legStart", "Real", 2 * nm));
        int bgs = Math.Max(2, nm * 4); // at most 4 legs per material (buffer, supply, 2 return); slots are keyed by (block, group) in Render
        m.Vars.Add(new("V_bgWait", "Real", bgs)); m.Vars.Add(new("V_bgCnt", "Integer", bgs));
        m.Funcs.AddRange(new[] { "F_ext_on_call", "F_ext_select_vehicle", "F_ext_before_unload", "F_ext_on_snap" });
        return m;
    }
}
