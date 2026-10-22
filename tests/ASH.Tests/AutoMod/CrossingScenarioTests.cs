using System.Globalization;
using System.Text;
using ASH.Core.Export;
using ASH.Core.Geometry;
using ASH.Core.Input;
using ASH.Core.Layout;
using ASH.Core.Model;
using ASH.Core.Runner;
using ASH.Core.Traffic;

namespace ASH.Tests.AutoMod;

/// <summary>
/// The whole search on a real AutoMod model, without windows (model runs are `-w`): groups A and B share one one-way loop, group C runs its own loop that
/// crosses the shared loop at right angles four times, with node blocks and entry traffic CPs made the way AGVTRAFFIC makes them. The demand is set so the
/// counts can be worked out by hand (see Expected); the test checks the search lands there and that C is held at the crossing blocks.
/// Set ASH_AM_HOME (e.g. C:\AutoMod\12_6). ASH_SCENARIO_OUT=dir keeps the project and writes report.txt there.
/// </summary>
public class CrossingScenarioTests
{
    static string? AmHome => Environment.GetEnvironmentVariable("ASH_AM_HOME");
    const string Model = "cross";

    /// <summary>ASH_SCENARIO_VARIANT contains "mergedab": A and B become one group AB on one standby CP on the bottom line (same demand).</summary>
    static bool Merged => (Environment.GetEnvironmentVariable("ASH_SCENARIO_VARIANT") ?? "").Contains("mergedab");

    static string Variant => Environment.GetEnvironmentVariable("ASH_SCENARIO_VARIANT") ?? "";
    static bool Real => Variant.Contains("real");

    // Dictionary.GetValueOrDefault is not on net48
    static TV Get<TK, TV>(Dictionary<TK, TV> d, TK k) where TK : notnull => d.TryGetValue(k, out var v) ? v : default!;

    static LayoutPath Line(string n, double x0, double y0, double x1, double y1, bool spur = false)
    {
        var p = new LayoutPath(n) { IsSpur = spur };
        p.Segments.Add(new LineSeg(new Point2(x0, y0), new Point2(x1, y1)));
        return p;
    }

    /// <summary>
    /// Shared loop (A, B), one-way counter-clockwise in metres: bottom y=0 x 0→80, right x=80 y 0→20, top y=20 x 80→0, left x=0 y 20→0.
    /// C loop: down x=40 y 35→-15, east y=-15 x 40→46, up x=46 y -15→35, west y=35 x 46→40; it crosses the shared loop at (40,0), (46,0), (40,20), (46,20).
    /// A and B unload on the top between x=46 and 80 and load on the bottom left, so a C path is never a shortcut for them (both are one-way the wrong way).
    /// Standby CPs sit on small one-way parking loops beside the through paths (a dead-end spur cannot be driven out of: no route back).
    /// </summary>
    public static ASH.Core.Layout.Layout Layout()
    {
        var raw = new List<LayoutPath>
        {
            Line("bot", 0, 0, 80, 0), Line("right", 80, 0, 80, 20), Line("top", 80, 20, 0, 20), Line("left", 0, 20, 0, 0),
            Line("cdown", 40, 35, 40, -15), Line("ceast", 40, -15, 46, -15), Line("cup", 46, -15, 46, 35), Line("cwest", 46, 35, 40, 35),
            // parking loops (one-way side loops): a parked vehicle never stands on a through path and can always drive out
            Line("pc1", 45, 35, 45, 55), Line("pc2", 45, 55, 41, 55), Line("pc3", 41, 55, 41, 35),
        };
        if (!Merged)
            // 20 m lanes: every vehicle of the group queues for its standby CP without its tail reaching back into the junction block
            raw.AddRange(new[] { Line("pa1", 2, 0, 2, -20), Line("pa2", 2, -20, 6, -20), Line("pa3", 6, -20, 6, 0),
                Line("pb1", 14, 0, 14, -20), Line("pb2", 14, -20, 18, -20), Line("pb3", 18, -20, 18, 0) });
        var l = new ASH.Core.Layout.Layout();
        l.Paths.AddRange(PathSplitter.SplitAll(raw, l.Tolerance).Paths);
        void Cp(string n, double x, double y, string type, string group) => l.Cps.Add(new LayoutCp { Name = n, Type = type, Group = group, Position = new Point2(x, y), Cap = CpTypes.DefaultCap(type) });
        // merged: A and B are one group whose standby SA sits on the bottom line itself, SA -> LA -> LB in a straight line, no turn into a lane
        if (Merged) Cp("SA", 5, 0, "standby", "AB");
        else { Cp("SA", 4, -20, "standby", "A"); Cp("SB", 16, -20, "standby", "B"); }
        Cp("SC", 43, 55, "standby", "C");
        Cp("LA", 10, 0, "load", Merged ? "AB" : "A"); Cp("UA", 60, 20, "unload", Merged ? "AB" : "A");
        Cp("LB", 24, 0, "load", Merged ? "AB" : "B"); Cp("UB", 70, 20, "unload", Merged ? "AB" : "B");
        Cp("LC", 40, 30, "load", "C"); Cp("UC", 46, -10, "unload", "C");
        var tp = new TrafficParams(1.2, 0.8);
        // as AGVTRAFFIC with "Y" to every merge question
        var prop = BlockGenerator.Generate(l, tp);
        var blocks = prop.Blocks;
        foreach (var g in prop.OverlapGroups)
        {
            var members = blocks.Where(b => g.Contains(b.Name)).ToList();
            if (members.Count < 2) continue;
            var merged = BlockGenerator.Merge(members, members[0].Name);
            blocks.Insert(blocks.IndexOf(members[0]), merged);
            blocks.RemoveAll(members.Contains);
        }
        l.Blocks.AddRange(blocks);
        var variant = Environment.GetEnvironmentVariable("ASH_SCENARIO_VARIANT") ?? "";
        if (variant.Contains("noblocks")) { l.Blocks.Clear(); l.Cps.RemoveAll(c => c.Type == "traffic"); }
        if (variant.Contains("noc")) { }
        if (variant.Contains("square"))
        {
            // one block over the whole crossing square
            var sq = l.Blocks.Where(x => x.Rect.Contains(new Point2(40, 0)) || x.Rect.Contains(new Point2(40, 20))).ToList();
            var m = BlockGenerator.Merge(sq, sq[0].Name);
            l.Blocks.RemoveAll(sq.Contains); l.Blocks.Add(m);
        }
        if (variant.Contains("exit"))
        {
            // a traffic CP just after every block exit, so a vehicle reaches a CP outside the block before the next block's entry CP
            int k = 1;
            foreach (var blk in l.Blocks.ToList())
                foreach (var path in l.Paths)
                {
                    bool prev = blk.Rect.Contains(path.PointAt(0));
                    for (double d = 0.05; d <= path.Length; d += 0.05)
                    {
                        bool now = blk.Rect.Contains(path.PointAt(d));
                        if (prev && !now && d + 0.6 < path.Length)
                            l.Cps.Add(new LayoutCp { Name = "X" + (k++).ToString("000"), Type = "traffic", Group = "", Position = path.PointAt(d + 0.6), Cap = 1 });
                        prev = now;
                    }
                }
        }
        foreach (var c in TrafficCpGenerator.Generate(l, tp).Cps) l.Cps.Add(c);
        return l;
    }

    /// <summary>
    /// One container per material per trip; each material has at most one call in flight, so a group needs several materials to keep several vehicles busy.
    /// Speed 1 m/s, load + unload 40 s. Round trips: A 200 m (240 s), B 200 m (240 s), C 112 m (152 s).
    /// A: 5 materials, one container every 600 s each → 1/120 s → 2.0 vehicles busy → 3 at UtilMax 0.75 (2 would be ~100 %).
    /// B: 2 materials, every 360 s each → 1/180 s → 1.33 busy → 2 (67 %).
    /// C: 2 materials, every 240 s each → 1/120 s → 1.27 busy → 2 (63 %), plus whatever the crossings cost.
    /// </summary>
    public static ProjectInput Input()
    {
        var p = new ProjectInput { Settings = new ASH.Core.Input.Settings { Units = "Meters", SimLengthSec = 6 * 3600, WarmupSec = 3600, Replications = 3, InitStockMode = "Uniform", UtilMax = 0.75 } };
        var types = Merged ? new[] { ("TAB", "AB"), ("TC", "C") } : new[] { ("TA", "A"), ("TB", "B"), ("TC", "C") };
        foreach (var (t, g) in types)
            // "real": breakdowns (MTBF 2 h, MTTR 5 min) and a faster empty run; "cap2": two containers per trip
            p.AgvTypes.Add(new AgvType(t, 1.0, Real ? 1.3 : 1.0, 0.3, 0.3, Variant.Contains("cap2") ? 2 : 1, 20, 20, Real ? 7200 : 0, Real ? 300 : 0, 1.2, 0.8, "Blue", Kind: "Standard"));
        if (Merged) p.Groups.Add(new AgvGroup("AB", "TAB", 1, 9, new List<string> { "SA" }, "Nearest"));
        else
        {
            p.Groups.Add(new AgvGroup("A", "TA", 1, 5, new List<string> { "SA" }, "Nearest"));
            p.Groups.Add(new AgvGroup("B", "TB", 1, 5, new List<string> { "SB" }, "Nearest"));
        }
        p.Groups.Add(new AgvGroup("C", "TC", 1, 4, new List<string> { "SC" }, "Nearest"));
        void Mat(string m, string g, double takt, string from, string to)
        {
            p.Materials.Add(new MaterialInfo(m, "", g, takt, 1, 1, 4, 1, null, null, false));
            p.Routes.Add(new Route(m, from, to, null, null, null, 0));
        }
        for (int i = 1; i <= 5; i++) Mat("MA" + i, Merged ? "AB" : "A", 600, "LA", "UA"); // five materials in one group, same total demand as before (1 / 120 s)
        for (int i = 1; i <= 2; i++) Mat("MB" + i, Merged ? "AB" : "B", 360, "LB", "UB");
        for (int i = 1; i <= 2; i++) Mat("MC" + i, "C", 240, "LC", "UC");
        return p;
    }

    public static readonly IReadOnlyDictionary<string, int> Expected = new Dictionary<string, int> { ["A"] = 3, ["B"] = 2, ["C"] = 2 };

    [Fact]
    public void The_Layout_Is_Clean_And_Every_Crossing_Has_A_Block_With_Entry_Cps()
    {
        var l = Layout();
        var errors = LayoutChecker.Check(l, new CheckOptions()).Where(f => f.Code is FindingCode.DanglingEnd or FindingCode.UnsplitIntersection or FindingCode.CpOffPath or FindingCode.StopCpInBlock).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Code + ": " + e.Message)));
        foreach (var x in new[] { new Point2(40, 0), new Point2(46, 0), new Point2(40, 20), new Point2(46, 20) })
            Assert.Contains(l.Blocks, b => b.Rect.Contains(x));
        var table = TrafficTable.Build(l);
        Assert.All(table.Entries.Where(e => l.Blocks.Any(b => b.Name == e.BlockName && b.Rect.Contains(new Point2(40, 0)))), e => Assert.NotNull(e.CpName));
        Assert.DoesNotContain(InputValidator.Validate(Input(), l.Cps.Select(c => c.Name).ToList()), f => f.Code == FindingCode.InputError);
    }

    [SkippableFact]
    public void Search_Finds_The_Hand_Worked_Counts_With_C_Held_At_The_Crossings()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var keep = Environment.GetEnvironmentVariable("ASH_SCENARIO_OUT");
        var work = keep ?? Path.Combine(Path.GetTempPath(), "ash_cross_" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);
        var l = Layout(); var input = Input();
        if ((Environment.GetEnvironmentVariable("ASH_SCENARIO_VARIANT") ?? "").Contains("fastload"))
            for (int i = 0; i < input.AgvTypes.Count; i++) input.AgvTypes[i] = input.AgvTypes[i] with { LoadTime = 2, UnloadTime = 2 };
        if ((Environment.GetEnvironmentVariable("ASH_SCENARIO_VARIANT") ?? "").Contains("amax8"))
            input.Groups[0] = input.Groups[0] with { MaxCount = 8 };
        var specs = ModelBuilder.VehicleGroups(input);
        var build = ModelBuilder.Build(new ModelBuildRequest(work, Model, input, l, new ExportOptions(Groups: specs)), AmHome!, TimeSpan.FromMinutes(10));
        Assert.True(build.Build?.Success == true, "build failed:\n" + build.Build?.MakeErrText + build.Build?.Am2ErrText + string.Join("\n", build.Findings.Select(f => f.Message)));

        var log = new StringWriter();
        var started = DateTime.UtcNow;
        var r = SearchJob.Run(new SearchJobOptions(work, Model, AmHome, Parallel: 2, DialogButton: "Student Version", RunTimeout: TimeSpan.FromMinutes(10), Impact: true), input, log);
        var elapsed = DateTime.UtcNow - started;

        var rows = SummaryTsv.ReadAll(Path.Combine(work, "runs", "summary.tsv"));
        var sum = SearchSummaryFile.Read(Path.Combine(work, "runs", SearchSummaryFile.FileName));
        // bottlenecks by zone: every block's wait and occupancy over the confirm runs (no attribution to groups)
        var crossing = l.Blocks.Where(b => new[] { new Point2(40, 0), new Point2(46, 0), new Point2(40, 20), new Point2(46, 20) }.Any(b.Rect.Contains)).Select(b => b.Name).ToHashSet();
        var blocks = new Dictionary<string, (double Wait, double Passes, double Util, int N)>();
        foreach (var id in sum.ConfirmRunIds)
        {
            var tsv = Path.Combine(work, "runs", id, "result.tsv");
            if (!File.Exists(tsv)) continue;
            var rs = ResultParser.Parse(tsv);
            foreach (var b in rs.Blocks)
            {
                var (w, p, u, n) = Get(blocks, b.Name);
                blocks[b.Name] = (w + b.AvgWaitSec, p + b.Passes, u + b.Utilization, n + 1);
            }
        }

        var inv = CultureInfo.InvariantCulture;
        var rep = new StringBuilder();
        rep.AppendLine($"exit {r.ExitCode} {r.Error}");
        rep.AppendLine($"runs {r.Runs} (failed {r.Failures}), elapsed {elapsed:hh\\:mm\\:ss}");
        rep.AppendLine("clusters: " + string.Join(" | ", sum.Clusters.Select(c => string.Join(",", c))));
        foreach (var g in input.Groups)
        {
            var at = rows.Where(x => x.Group == g.Group && sum.ConfirmRunIds.Contains(x.RunId)).ToList();
            rep.AppendLine($"group {g.Group}: decided {(r.Search?.Decided.TryGetValue(g.Group, out var d) == true ? d : -1)} expected {(Expected.TryGetValue(g.Group, out var ex) ? ex : -1)} range {g.MinCount}-{g.MaxCount}" +
                $" util {(at.Count == 0 ? double.NaN : at.Average(x => x.Utilization)).ToString("0.000", inv)} minStock {(at.Count == 0 ? double.NaN : at.Min(x => x.MinStockRatio)).ToString("0.00", inv)}" +
                $" ok {at.Count > 0 && at.All(x => x.Ok)}");
            foreach (var c in rows.Where(x => x.Group == g.Group).GroupBy(x => x.Active).OrderBy(x => x.Key))
                rep.AppendLine($"   {g.Group}={c.Key}: runs {c.Count()} util {c.Average(x => x.Utilization).ToString("0.000", inv)} fails {c.Count(x => !x.Ok)} {string.Join("; ", c.Where(x => !x.Ok).Select(x => x.Reason).Distinct().Take(2))}");
        }
        // per vehicle and per material numbers of the first confirm run (the logic's own tables)
        var first = sum.ConfirmRunIds.Select(id => Path.Combine(work, "runs", id, "result.tsv")).FirstOrDefault(File.Exists);
        if (first != null)
        {
            var rs0 = ResultParser.Parse(first);
            rep.AppendLine("confirm run " + Path.GetFileName(Path.GetDirectoryName(first)) + ":");
            foreach (var v in rs0.Vehicles ?? new List<VehicleResult>())
                if (v.Jobs > 0) rep.AppendLine($"   vehicle {v.Name}: util {(v.BusySec / v.WindowSec).ToString("0.000", inv)} jobs {v.Jobs} distance {v.Distance.ToString("0", inv)} m ({(v.Distance / v.Jobs).ToString("0", inv)} m/job, {(v.BusySec / v.Jobs).ToString("0", inv)} s/job, repair {v.DownSec.ToString("0", inv)} s)");
            foreach (var m in rs0.MaterialStats ?? new List<MaterialStat>())
            {
                var mr = rs0.Materials.First(x => x.Name == m.Name);
                rep.AppendLine($"   material {m.Name}: supplies {mr.Supplies}/{mr.Expected.ToString("0", inv)} stop {mr.StopTimeSec.ToString("0", inv)} s, lead call->pickup avg {m.PickAvg.ToString("0", inv)} max {m.PickMax.ToString("0", inv)} s, call->delivered avg {m.DelivAvg.ToString("0", inv)} max {m.DelivMax.ToString("0", inv)} s, stock avg {m.StockAvg.ToString("0.00", inv)} sd {m.StockStd.ToString("0.00", inv)} min {m.StockMin.ToString("0.00", inv)} max {m.StockMax.ToString("0.00", inv)}");
            }
        }
        foreach (var kv in blocks.Where(k => k.Value.Passes > 0).OrderByDescending(k => k.Value.Wait / k.Value.N * k.Value.Passes / k.Value.N))
            rep.AppendLine($"block {kv.Key}{(crossing.Contains(kv.Key) ? " (crossing)" : "")}: avg wait {(kv.Value.Wait / kv.Value.N).ToString("0.0", inv)} s, passes/run {(kv.Value.Passes / kv.Value.N).ToString("0", inv)}, " +
                $"wait/run {(kv.Value.Wait / kv.Value.N * kv.Value.Passes / kv.Value.N).ToString("0", inv)} s, occupied {(kv.Value.Util / kv.Value.N * 100).ToString("0", inv)} %");
        foreach (var w in sum.Warnings) rep.AppendLine("warning: " + w);
        foreach (var im in r.Search?.Impact ?? new List<ImpactRow>()) rep.AppendLine($"impact {im.Group}+1 ({im.PlusOne}) -> {im.OtherGroup}: util {(im.UtilDelta * 100).ToString("+0.0;-0.0;0", inv)} %p");
        rep.AppendLine("---- log ----").Append(log);
        var text = rep.ToString();
        File.WriteAllText(Path.Combine(work, "report.txt"), text);
        Console.WriteLine(text);

        Assert.True(r.ExitCode != 1, text);
        if (Merged) { if (keep == null) Directory.Delete(work, true); return; } // a what-if: read report.txt
        var noBlocks = (Environment.GetEnvironmentVariable("ASH_SCENARIO_VARIANT") ?? "").Contains("noblocks");
        // without traffic blocks the search lands on the hand count for every group (verified 2026-10-21: A 3, B 2, C 2 in 29 runs).
        // With the blocks this layout saturates: A and B share one loop with nine vehicles in a row, a job takes ~690 s for 240 m (330 s free run),
        // A fails even at its maximum and more vehicles only slow B and C down (2026-10-22). No vehicle count fixes a path problem; the merged
        // layout (ASH_SCENARIO_VARIANT=mergedab: one group, standby on the line) needs AB 7 and has every material fully supplied.
        if (noBlocks)
            foreach (var g in Expected.Keys)
                Assert.InRange(r.Search!.Decided[g], Expected[g], Expected[g] + 1);
        if (noBlocks) { if (keep == null) Directory.Delete(work, true); return; }
        var cross = blocks.Where(k => crossing.Contains(k.Key)).Select(k => k.Value).ToList();
        Assert.True(cross.Sum(b => b.Passes) > 0, "nobody passed the crossing blocks:\n" + text);
        Assert.True(cross.Sum(b => b.Wait) > 0, "no one waited at a crossing:\n" + text);
        if (keep == null) Directory.Delete(work, true);
    }
}
