using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ASH.Core.Asy;
using ASH.Core.Input;

namespace ASH.Core.Model;

/// <summary>StandbyCpByGroup is the fallback standby CP for a group whose AgvGroup.StandbyCPs is empty.</summary>
public sealed record LayoutInfo(IReadOnlyList<string> CpNames, IReadOnlyList<string> BlockNames,
    IReadOnlyDictionary<string, string> StandbyCpByGroup, string AgvSystemName = "agv");

/// <summary>Seeding: params.txt seeds the init stocks; breakdowns (MTBF/MTTR) draw from RS_fail, whose seed is fixed at build time, so the
/// logic skips (seed % 100) x 5000 samples at start and each replication gets its own stretch of the stream.</summary>
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

    // a load shows only between a pickup and its set-down (code_material shrinks it at the start and shows it at the supply pickup)
    const string Show = "\tscale to x 1, y 1, z 1\n", Hide = "\tscale to x 0.001, y 0.001, z 0.001\n";

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

    /// <summary>unitsPerMeter: model units in one meter (the speeds in the input are m/s).</summary>
    public static string Render(ProjectInput input, LayoutInfo layout, double unitsPerMeter = 1)
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
            groupLogic.Append(Fill(Res("code_group.m.txt"), ("G", gi.ToString()), ("TYPE", Lit(g.Group)), ("SYS", sys), ("PARK_OK", parkOk),
                ("VID_OFFSET", input.Groups.Take(i).Sum(x => x.MaxCount).ToString()),
                ("STOP_M", ASH.Core.Export.LayoutExporter.StopGapM.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)),
                ("STOP_FT", (ASH.Core.Export.LayoutExporter.StopGapM / 0.3048).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture))));
            // value1 = the requested active count; Max - parked would count never-woken vehicles as active
            snap.Append($"\tprint \"group\\t{Lit(g.Group)}\\t{g.Group}\\t\" V_active({gi}) \"\\t0\\t0\\t0\\t0\\t0\\t0\\t0\" to V_out\n");
        }

        // time split arrays (code_tsplit.m): V_vT = run flag | standing s | turning s per vehicle (nv each); V_gT(k * ng + g) = group sums
        // k = 0 jobs, 1 job s, 2 free-speed s, 3 standing s, 4 load/unload/extra s, 5 turning s, then 6 + block index = block claims
        int nv = input.Groups.Sum(x => x.MaxCount), ng = input.Groups.Count;
        int GT(int k, int g) => k * ng + g;
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
            // breakdowns of this group's vehicles: checked at the end of every leg, and at boarding for the time the vehicle stood idle
            bool fails = t.MTBF > 0 && t.MTTR > 0;
            // held: the repair holds a job inside its busy time (A_dn); after the Finish CP it does not
            string Fail(string start, bool held = true) => fails ? Fill(Res("code_fail.m.txt"), ("START", start), ("MTBF", N(t.MTBF)), ("MTTR", N(t.MTTR)),
                ("DOWN", held ? "\t\t\tinc A_dn by A_rep - ac\n" : "")).TrimEnd('\n') : "";
            // empty runs (to the pickup, after the unload, home) at SpeedEmpty; "specification" is the loaded line of the AGVS vehicle
            bool speeds = t.SpeedEmpty > 0 && t.SpeedEmpty != t.SpeedLoaded;
            string emptySpeed = speeds ? $"\tset A_veh defined velocity to {N(t.SpeedEmpty * unitsPerMeter)}" : "";
            string loadedSpeed = speeds ? "\tset A_veh defined velocity to specification" : "";
            // free speed of a run for the time split, model units/s: loaded legs at the loaded speed, empty ones at SpeedEmpty (when set)
            string Vel(bool loaded) => N((loaded || t.SpeedEmpty <= 0 ? t.SpeedLoaded : t.SpeedEmpty) * unitsPerMeter);
            string Travel(string to) => Fill(Res("code_travel.m.txt"), ("SYS", sys), ("TO", Cp(layout, to)), ("VEL", Vel(false))).TrimEnd('\n');
            // leg slot = mi for supply legs, mi + nm for return legs (a return can overlap the next supply of the same material)
            // loaded: the leg carries the supply or the empty container (A_ld = the job's loaded driving time)
            string Leg(string to, int slot, bool loaded = false) => Fill(Res("code_leg.m.txt"), ("M", mi.ToString()), ("SLOT", slot.ToString()), ("SYS", sys),
                ("TO", Cp(layout, to)), ("NAME", Lit(m.Material)), ("FAIL", Fail("ac")), ("LOADED", loaded ? "\tinc A_ld by ac - A_t0\n" : ""), ("VEL", Vel(loaded)));
            var legs = new StringBuilder();
            if (!string.IsNullOrEmpty(r.BufferCP))
            {
                legs.Append(Leg(r.BufferCP!, mi, true));
                // waiting at the buffer for the stock to drop to the unload threshold: up to Route.BufferWaitSec of it is busy, only the excess is left out
                var hold = r.BufferWaitSec > 0 ? $"\tset A_hold to ac - A_hold - {N(r.BufferWaitSec)}\n\tif A_hold < 0 then set A_hold to 0\n" : "\tset A_hold to ac - A_hold\n";
                // A_bw = the whole wait, A_hold = the part left out of busy time
                legs.Append($"\tset A_bw to ac\n\tset A_hold to ac\n\twhile V_stock({mi}) > V_unload({mi}) do begin\n\t\twait for 5 sec\n\tend\n{hold}\tset A_bw to ac - A_bw\n");
            }
            legs.Append(Leg(r.SupplyToCP, mi, true));
            var ret = new StringBuilder();
            // Route.ExtraTime: once per supply cycle with the vehicle held; at the return pickup when there is one (before its load time), else after the unload
            var extra = r.ExtraTime > 0 ? $"\twait for {N(r.ExtraTime)} sec\n" : "";
            bool returnPick = m.EmptyReturn && !string.IsNullOrEmpty(r.ReturnFromCP);
            if (m.EmptyReturn)
            {
                // empty container: picked up at ReturnFrom (LoadTime), set down at ReturnTo (UnloadTime)
                if (returnPick) ret.Append(Leg(r.ReturnFromCP!, mi + nm)).Append(extra).Append(Show).Append($"\twait for {N(t.LoadTime)} sec\n").Append(loadedSpeed + "\n");
                if (!string.IsNullOrEmpty(r.ReturnToCP)) ret.Append(Leg(r.ReturnToCP!, mi + nm, true)).Append($"\twait for {N(t.UnloadTime)} sec\n").Append(Hide).Append(emptySpeed + "\n");
            }
            if (!returnPick) ret.Insert(0, extra);
            // without a return pickup the empty container rides on from the unload CP to ReturnTo: still loaded until there
            bool carriesOn = m.EmptyReturn && !returnPick && !string.IsNullOrEmpty(r.ReturnToCP);
            // busy ends at the Finish CP when the group has one (put it at the entry of the standby lane: the queue behind a waiting vehicle is idle
            // time), else on arrival at the standby CP. A free-run return time was tried and under-counts badly when the way home is congested.
            var home = StandbyOf(g, layout)[0];
            string finish, finishAtHome = "";
            if (!string.IsNullOrEmpty(g.FinishCP)) finish = Leg(g.FinishCP!, mi + nm) + "\tset A_fin to ac";
            else { finish = ""; finishAtHome = "\tset A_fin to ac"; }
            int vidOffset = input.Groups.TakeWhile(x => x.Group != g.Group).Sum(x => x.MaxCount);
            // the supply load shows as the material's own picture, the container goes back as its empty picture
            var emptyType = m.EmptyReturn && ret.Length > 0 ? $"\tset load type to L_m{mi}e" : "";
            // the fixed waits of one job: supply load + unload, the extra time, the empty container's load + unload
            double handle = t.LoadTime + t.UnloadTime + Math.Max(0, r.ExtraTime) + (returnPick ? t.LoadTime : 0)
                + (m.EmptyReturn && !string.IsNullOrEmpty(r.ReturnToCP) ? t.UnloadTime : 0);
            matLogic.Append(Fill(Res("code_material.m.txt"), ("M", mi.ToString()), ("NAME", Lit(m.Material)), ("SYS", sys),
                ("SET_TYPE", $"\tset load type to L_m{mi}"), ("EMPTY_TYPE", emptyType),
                ("G", GroupIndex(input, m.Group).ToString()), ("NV", nv.ToString()), ("NV2", (2 * nv).ToString()),
                ("GT_JOBS", GT(0, GroupIndex(input, m.Group)).ToString()), ("GT_JOB", GT(1, GroupIndex(input, m.Group)).ToString()),
                ("GT_FREE", GT(2, GroupIndex(input, m.Group)).ToString()), ("GT_STOP", GT(3, GroupIndex(input, m.Group)).ToString()),
                ("GT_HAND", GT(4, GroupIndex(input, m.Group)).ToString()), ("GT_TURN", GT(5, GroupIndex(input, m.Group)).ToString()),
                ("FROM", Cp(layout, r.SupplyFromCP)), ("TO_CP", Cp(layout, r.SupplyToCP)), ("LOAD_TIME", N(t.LoadTime)), ("UNLOAD_TIME", N(t.UnloadTime)),
                ("SUPPLY_LEGS", legs.ToString().TrimEnd('\n')), ("RETURN_LEGS", ret.ToString().TrimEnd('\n')),
                ("FINISH", finish.TrimEnd('\n')), ("FINISH_AT_HOME", finishAtHome), ("HOME", home), ("VID_OFFSET", vidOffset.ToString()),
                ("CAPACITY", Math.Max(1, t.Capacity).ToString()), ("FAIL_IDLE", Fail("V_vNextFail(A_vid)")), ("FAIL_LEG", Fail("ac")),
                ("FAIL_HOME", Fail("ac", string.IsNullOrEmpty(g.FinishCP))), ("HANDLE", N(handle)),
                // the run home is part of the job only without a Finish CP (busy ends there)
                ("PICK_TRAVEL", Travel(r.SupplyFromCP)), ("HOME_TRAVEL", finishAtHome.Length > 0 ? Travel(home) : $"\ttravel to {sys}:{home}"),
                ("EMPTY_SPEED", emptySpeed), ("LOADED_SPEED", loadedSpeed), ("AFTER_UNLOAD_SPEED", carriesOn ? "" : emptySpeed),
                ("AFTER_UNLOAD_HIDE", carriesOn ? "" : Hide.TrimEnd('\n')), ("SPEC_SPEED", loadedSpeed),
                // a conveyor-fed material is consumed by the products passing its use point (code_conv.m), not by this clock
                ("CONSUME", m.ConveyorFed ? "\t\t\t/* consumed by the conveyor products (code_conv.m) */"
                    : $"\t\t\tdec V_stock({mi}) by V_need({mi})\n\t\t\tif ac >= V_warmup then inc V_consumed({mi}) by V_need({mi})")));
            create.Append($"\tcreate 1 load of type L_ctl to P_consume_{mi}\n");

            // a stop still running at the end of the run counts up to the snap
            snap.Append($"\tif V_stopped({mi}) = 1 then begin\n\t\tif V_stopSince({mi}) < V_warmup then set V_stopSince({mi}) to V_warmup\n" +
                        $"\t\tif ac > V_stopSince({mi}) then inc V_stop({mi}) by ac - V_stopSince({mi})\n\t\tset V_stopSince({mi}) to ac\n\tend\n");
            snap.Append($"\tif V_samples({mi}) > 0 then set V_x to V_stockSum({mi}) / V_samples({mi})\n\telse set V_x to V_stock({mi})\n");
            snap.Append($"\tif V_samples({mi}) = 0 then set V_minStock({mi}) to V_stock({mi})\n");
            snap.Append($"\tset V_y to (V_simLen - V_warmup) / V_takt({mi}) * V_need({mi})\n");
            snap.Append($"\tprint \"material\\t{m.Material}\\t{Lit(m.Group)}\\t\" V_minStock({mi}) \"\\t\" V_x \"\\t\" V_supplies({mi}) \"\\t\" V_y \"\\t\" V_stop({mi}) \"\\t\" V_consumed({mi}) \"\\t\" V_mDist({mi}) \"\\t\" V_mJobs({mi}) to V_out\n");
            // a leg still running at the end counts as stuck once it is over the limit
            foreach (var slot in new[] { mi, mi + nm })
                snap.Append($"\tif V_inLeg({slot}) = 1 and ac - V_legStart({slot}) > V_deadlockSec then inc V_deadlock by 1\n");
        }
        // per vehicle (every one up to MaxCount; the judge counts the active ones): busy s, distance, jobs, window s, index in group, repair s,
        // then where the time went (value7..11): loaded driving, load/unload/extra waits, buffer hold busy / not busy, repairs that held a job
        int vid = 0;
        var failInit = new StringBuilder();
        foreach (var g in input.Groups)
        {
            var gt = input.AgvTypes.FirstOrDefault(x => x.Type == g.Type);
            for (int k = 1; k <= g.MaxCount; k++)
            {
                vid++;
                snap.Append($"\tset V_x to V_simLen - V_warmup\n");
                snap.Append($"\tprint \"vehicle\\t{Lit(g.Type)}{k}\\t{Lit(g.Group)}\\t\" V_vBusy({vid}) \"\\t\" V_vDist({vid}) \"\\t\" V_vJobs({vid}) \"\\t\" V_x \"\\t{k}\\t\" V_vDown({vid}) " +
                            $"\"\\t\" V_vLoad({vid}) \"\\t\" V_vHand({vid}) \"\\t\" V_vBufB({vid}) \"\\t\" V_vBufX({vid}) \"\\t\" V_vRep({vid}) to V_out\n");
                if (gt != null && gt.MTBF > 0 && gt.MTTR > 0) failInit.Append($"\tset V_vNextFail({vid}) to rn RS_fail e {N(gt.MTBF)}\n");
            }
        }
        // time split per group (code_material, code_leg, code_tsplit), sums over the jobs after warmup: jobs, job s, free-speed s, standing s,
        // load/unload/extra s, turning on the spot s; then a tblock row per block and group it claimed in measured runs (value1 = claims)
        // (V_tmp = the claiming vehicle's group)
        var claims = string.Join("\n", layout.BlockNames.Select((b, k) => $"\t\t\tif theBlock = {b} then inc V_gT(V_tmp + {GT(6 + k, 0)}) by 1"));
        matLogic.Append(Fill(Res("code_tsplit.m.txt"), ("NV", nv.ToString()), ("NV2", (2 * nv).ToString()), ("SYS", sys), ("BLOCK_CLAIMS", claims)));
        create.Append("\tcreate 1 load of type L_ctl to P_tsplit\n");
        for (int i = 1; i <= ng; i++)
        {
            var g = Lit(input.Groups[i - 1].Group);
            snap.Append($"\tprint \"tsplit\\t{g}\\t{g}\\t\" V_gT({GT(0, i)}) \"\\t\" V_gT({GT(1, i)}) \"\\t\" V_gT({GT(2, i)}) \"\\t\" V_gT({GT(3, i)}) \"\\t\" V_gT({GT(4, i)}) \"\\t\" V_gT({GT(5, i)}) to V_out\n");
            for (int k = 0; k < layout.BlockNames.Count; k++)
                snap.Append($"\tif V_gT({GT(6 + k, i)}) > 0 then print \"tblock\\t{Lit(layout.BlockNames[k])}\\t{g}\\t\" V_gT({GT(6 + k, i)}) to V_out\n");
        }
        if (failInit.Length > 0)
            failInit.Insert(0, "\t/* RS_fail is seeded at build time: skipping a seed-dependent stretch of it gives each replication its own breakdowns */\n" +
                               "\tset V_i to (V_seed % 100) * 5000\n\twhile V_i > 0 do begin\n\t\tset V_x to RS_fail sample real\n\t\tdec V_i by 1\n\tend\n");
        // per material: lead times (call -> pickup, call -> delivered) and stock samples (one per takt), from AutoMod tables
        for (int i = 0; i < input.Materials.Count; i++)
        {
            var m = input.Materials[i]; int mi = i + 1;
            snap.Append($"\tprint \"matstat\\t{m.Material}\\t{Lit(m.Group)}\\t\" T_pick_{mi} average \"\\t\" T_pick_{mi} maximum \"\\t\" T_deliv_{mi} average \"\\t\" T_deliv_{mi} maximum " +
                        $"\"\\t\" T_stock_{mi} average \"\\t\" T_stock_{mi} stddev \"\\t\" T_stock_{mi} minimum \"\\t\" T_stock_{mi} maximum to V_out\n");
        }
        foreach (var b in layout.BlockNames)
            snap.Append($"\tprint \"block\\t{Lit(b)}\\t\\t\" {b} total \"\\t\" {b} average waiting time \"\\t0\\t\" {b} utilization \"\\t0\\t0\\t0\\t0\" to V_out\n");
        // conveyors (code_conv.m): each Conveyors row runs its own product line on the conveyor system, the first product at once;
        // row: products passed the end, seconds stopped at the use point for want of stock, seconds a product waited at the full entry (all after warmup)
        for (int k = 1; k <= input.Conveyors.Count; k++)
        {
            var c = input.Conveyors[k - 1];
            int mi = input.Materials.FindIndex(x => x.Material == c.Material) + 1;
            if (mi == 0) throw new ArgumentException($"컨베이어 '{c.Conveyor}'의 자재 '{c.Material}'이(가) Materials에 없습니다.");
            matLogic.Append(Fill(Res("code_conv.m.txt"), ("K", k.ToString()), ("CV", Lit(c.Conveyor)), ("M", mi.ToString()), ("NAME", Lit(c.Material)),
                ("SYS", ConveyorAsyWriter.SystemName), ("TAKT", N(c.ProductTakt))));
            create.Append($"\tcreate 1 load of type L_prod to P_cv_{k}\n");
            foreach (var (on, since, sum) in new[] { ("V_cvWait", "V_cvSince", "V_cvStop"), ("V_cvBlk", "V_cvT0", "V_cvLate") })
                snap.Append($"\tif {on}({k}) = 1 then begin\n\t\tif {since}({k}) < V_warmup then set {since}({k}) to V_warmup\n" +
                            $"\t\tif ac > {since}({k}) then inc {sum}({k}) by ac - {since}({k})\n\t\tset {since}({k}) to ac\n\tend\n");
            snap.Append($"\tprint \"conveyor\\t{Lit(c.Conveyor)}\\t{Lit(c.Material)}\\t\" V_cvPass({k}) \"\\t\" V_cvStop({k}) \"\\t\" V_cvLate({k}) to V_out\n");
        }

        return Fill(Res("code.m.txt"), ("GROUP_COUNT", input.Groups.Count.ToString()), ("MATERIAL_COUNT", input.Materials.Count.ToString()),
            ("CREATE_CONSUMERS", create.ToString().TrimEnd('\n')), ("GROUP_LOGIC", groupLogic.ToString()), ("MATERIAL_LOGIC", matLogic.ToString()),
            ("SNAP_ROWS", snap.ToString().TrimEnd('\n')), ("FAIL_INIT", failInit.ToString().TrimEnd('\n')));
    }

    /// <summary>AM dispatch for the load-centric logic. Scheduling lists are per location, not per vehicle type, so the lists hold every
    /// group and the per-type work/load search/park ok functions in code.m filter by group: a call enters at the group's standby CP and waits
    /// there, so nl_pick_&lt;G&gt; = nl_&lt;G&gt; = the group's standby CPs; every CP gets a WORKLST over all nl_pick lists (Oldest: first come
    /// first served), every non-standby CP a PARKLST to each group's first standby CP, every standby CP a SRCHLST.</summary>
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
            // calls wait at the group's standby CPs (the load boards there and rides along), so that is where a free vehicle looks for work
            var pick = new List<string>(standby);
            if (!input.Materials.Any(x => x.Group == g.Group)) continue;
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
        var m = new ProcessModel { Units = units, LogicSource = Render(input, layout, ASH.Core.Settings.UnitScale.ModelUnitsPerMeter(units) ?? 1), UserExtSource = DefaultUserExt };
        int nm = input.Materials.Count;
        // arrays get at least 2 elements: AM mishandles 1-element arrays (seen with BLOCK n 1)
        int gs = Math.Max(2, input.Groups.Count), ms = Math.Max(2, nm);
        m.LoadTypes.Add("L_ctl");
        for (int i = 1; i <= nm; i++)
        {
            var mat = input.Materials[i - 1];
            var form = MaterialForms.Normalize(mat.Form) ?? MaterialForms.Box;
            var d = LoadGeometry.DefaultSize(form);
            double len = mat.LoadLength ?? d.L, wid = mat.LoadWidth ?? d.W, hei = mat.LoadHeight ?? d.H;
            m.LoadTypes.Add($"L_m{i}");
            // L_m<i> rides the supply leg, L_m<i>e the return leg: full then empty, or for an Output flow an empty container out and the full one back
            var out_ = mat.IsOutput;
            var full = LoadGeometry.Build(form, out_, len, wid, hei, units);
            if (full != null) m.LoadGraphics[$"L_m{i}"] = full;
            if (mat.EmptyReturn)
            {
                m.LoadTypes.Add($"L_m{i}e");
                var empty = LoadGeometry.Build(form, !out_, len, wid, hei, units);
                if (empty != null) m.LoadGraphics[$"L_m{i}e"] = empty;
            }
        }
        for (int i = 1; i <= nm; i++) { m.Procs.Add(new($"P_consume_{i}")); m.Procs.Add(new($"P_supply_{i}")); }
        m.Queues.Add(new("Q_park"));
        m.Orders.Add("OL_forever");
        m.Atts.Add(new("A_index", "Integer")); m.Atts.Add(new("A_grp", "Integer"));
        m.Atts.Add(new("A_veh", "VehiclePtr")); m.Atts.Add(new("A_mv0", "Real")); m.Atts.Add(new("A_w", "Real")); m.Atts.Add(new("A_t0", "Real"));
        m.Vars.Add(new("V_fp", "FilePtr")); m.Vars.Add(new("V_out", "FilePtr")); m.Vars.Add(new("V_ev", "FilePtr")); m.Vars.Add(new("V_tok", "String"));
        foreach (var v in new[] { "V_seed", "V_n", "V_i", "V_tmp", "V_deadlock", "V_extMat", "V_debug", "V_events" }) m.Vars.Add(new(v, "Integer"));
        foreach (var v in new[] { "V_simLen", "V_warmup", "V_x", "V_y" }) m.Vars.Add(new(v, "Real"));
        m.Vars.Add(new("V_deadlockSec", "Real", 0, "600"));
        foreach (var v in new[] { "V_active", "V_vehCount", "V_parked" }) m.Vars.Add(new(v, "Integer", gs));
        foreach (var v in new[] { "V_pending", "V_samples", "V_supplies", "V_stopped", "V_mJobs" }) m.Vars.Add(new(v, "Integer", ms));
        foreach (var v in new[] { "V_call", "V_unload", "V_stock", "V_safety", "V_takt", "V_perCycle", "V_perContainer", "V_need",
                     "V_minStock", "V_stockSum", "V_stop", "V_stopSince", "V_consumed", "V_mDist" })
            m.Vars.Add(new(v, "Real", ms));
        // leg slots: 1..nm supply legs, nm+1..2nm return legs
        m.Vars.Add(new("V_inLeg", "Integer", 2 * nm)); m.Vars.Add(new("V_legStart", "Real", 2 * nm));
        foreach (var a in new[] { "A_tCall", "A_tPick", "A_ret", "A_fin", "A_rLast", "A_dLast", "A_rep", "A_hold", "A_bw", "A_ld", "A_dn" }) m.Atts.Add(new(a, "Real"));
        foreach (var a in new[] { "A_vid", "A_qty" }) m.Atts.Add(new(a, "Integer"));
        int vs = Math.Max(2, input.Groups.Sum(g => g.MaxCount));
        m.Vars.Add(new("V_vBusy", "Real", vs)); m.Vars.Add(new("V_vDist", "Real", vs)); m.Vars.Add(new("V_vJobs", "Integer", vs));
        m.Vars.Add(new("V_vNextFail", "Real", vs)); m.Vars.Add(new("V_vDown", "Real", vs));
        foreach (var v in new[] { "V_vLoad", "V_vHand", "V_vBufB", "V_vBufX", "V_vRep" }) m.Vars.Add(new(v, "Real", vs));
        // time split (code_tsplit.m, layout of the arrays in Render): few entities, the trial licence counts them
        m.Procs.Add(new("P_tsplit"));
        m.Vars.Add(new("V_vPtr", "VehiclePtr", vs)); m.Vars.Add(new("V_vT", "Real", 3 * vs));
        m.Vars.Add(new("V_gT", "Real", Math.Max(2, (6 + layout.BlockNames.Count) * input.Groups.Count)));
        foreach (var a in new[] { "A_d0", "A_jf", "A_js0", "A_jt0" }) m.Atts.Add(new(a, "Real"));
        // the seed is arbitrary: replications differ by the samples the logic skips at start (see the class summary)
        if (input.AgvTypes.Any(t => t.MTBF > 0 && t.MTTR > 0)) m.RnStreams.Add(new("RS_fail", 12345));
        for (int i = 1; i <= nm; i++) m.Tables.AddRange(new[] { $"T_pick_{i}", $"T_deliv_{i}", $"T_stock_{i}" });
        m.Funcs.AddRange(new[] { "F_ext_on_call", "F_ext_select_vehicle", "F_ext_before_unload", "F_ext_on_snap" });
        int nc = input.Conveyors.Count;
        if (nc > 0)
        {
            // the product: an orange box, its length along the conveyor (AutoMod spaces accumulated loads by their size)
            m.LoadTypes.Add("L_prod");
            var u = ASH.Core.Settings.UnitScale.ModelUnitsPerMeter(units);
            if (u != null)
                m.LoadGraphics["L_prod"] = new[] { "700 17", "2 2 0 1 1 none", "1" }
                    .Concat(LoadGeometry.Box("Product", 13, 0, 0, 0, ConveyorAsyWriter.ProductLengthM * u.Value, 0.4 * u.Value, 0.25 * u.Value)).ToList();
            for (int k = 1; k <= nc; k++) { m.Procs.Add(new($"P_cvsrc_{k}")); m.Procs.Add(new($"P_cv_{k}")); }
            int cs = Math.Max(2, nc);
            foreach (var v in new[] { "V_cvPass", "V_cvWait", "V_cvBlk" }) m.Vars.Add(new(v, "Integer", cs));
            foreach (var v in new[] { "V_cvStop", "V_cvLate", "V_cvSince", "V_cvT0" }) m.Vars.Add(new(v, "Real", cs));
        }
        return m;
    }
}
