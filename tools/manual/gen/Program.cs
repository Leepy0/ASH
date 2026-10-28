// Follow-along example for the ASH manual: the layout a user draws in ZWCAD (mm drawing, Scale 0.001), built in code.
// gen <dir> [B]            : input.xlsx, model.arc, input.cells.tsv, DXFs, ash.frame.tsv, ash.settings.tsv, layout.json (for the illustrations)
// build <dir> <amHome>     : AutoMod build of <dir>\model.arc (trial entity check) + one run with 2 vehicles
// submit <dir> <url> <analysis> <plan> <note>
using System.Globalization;
using System.Text;
using System.Text.Json;
using ASH.Core.Dxf;
using ASH.Core.Export;
using ASH.Core.Geometry;
using ASH.Core.Input;
using ASH.Core.Layout;
using ASH.Core.Lite;
using ASH.Core.Model;
using ASH.Core.Runner;
using ASH.Core.Settings;
using ASH.Core.Traffic;
using ClosedXML.Excel;

const double K = 1000; // mm per m
var C = CultureInfo.InvariantCulture;

static LayoutPath Line(string n, double x1, double y1, double x2, double y2) =>
    new(n) { Segments = { new LineSeg(new Point2(x1 * 1000, y1 * 1000), new Point2(x2 * 1000, y2 * 1000)) } };
static LayoutCp Cp(string n, double x, double y, string type, string group = "G1") =>
    new() { Name = n, Type = type, Group = group, Cap = CpTypes.DefaultCap(type), Position = new Point2(x * 1000, y * 1000) };

Layout Build(bool planB, bool planC = false)
{
    var l = new Layout { Tolerance = 1 };
    l.Paths.AddRange(new[]
    {
        Line("P001", 0, 0, 20, 0), Line("P002", 20, 0, 40, 0), Line("P003", 40, 0, 40, 16), Line("P004", 40, 16, 20, 16),
        Line("P005", 20, 16, 0, 16), Line("P006", 0, 16, 0, 0), Line("P007", 20, 16, 20, 0),
    });
    l.Cps.AddRange(new[]
    {
        Cp("S001", 20, 5, "standby"), Cp("F001", 20, 13, "finish"),
        Cp("L001", planC ? 21.8 : 26, 0, "load"), Cp("L002", 34, 0, "load"),
        Cp("U001", 30, 16, "unload"), planB ? Cp("U002", 26, 16, "unload") : Cp("U002", 10, 16, "unload"),
    });
    var p = new TrafficParams(2.0 * K, 1.0 * K, 0.5 * K); // ash.settings.tsv defaults (VehicleLength 2, VehicleWidth 1, SafetyGap 0.5) in mm
    var bp = BlockGenerator.Generate(l, p);
    foreach (var w in bp.Warnings) Console.WriteLine("block warning: " + w);
    foreach (var g in bp.OverlapGroups) Console.WriteLine("overlap group: " + string.Join(",", g));
    l.Blocks.AddRange(bp.Blocks);
    var cp = TrafficCpGenerator.Generate(l, p);
    foreach (var w in cp.Warnings) Console.WriteLine("traffic cp warning: " + w);
    l.Cps.AddRange(cp.Cps);
    // 3D picture (no effect on the simulation): walls, columns, racks by the loads, machines by the unloads
    l.Assets.Add(new LayoutAsset { Kind = AssetKinds.Wall, Segments = Rect(-6, -8, 46, 22) });
    foreach (var (x, y) in planC ? new[] { (10.0, 8.0), (30.0, 8.0), (10.0, -6.0), (30.0, -6.0), (37.0, 0.9) } : new[] { (10.0, 8.0), (30.0, 8.0), (10.0, -6.0), (30.0, -6.0) }) l.Assets.Add(new LayoutAsset { Kind = AssetKinds.Column, X = x * K, Y = y * K });
    foreach (var x in new[] { 26.0, 34.0 }) l.Assets.Add(new LayoutAsset { Kind = AssetKinds.Rack, X = x * K, Y = -3 * K });
    foreach (var x in new[] { 30.0, planB ? 26.0 : 10.0 }) l.Assets.Add(new LayoutAsset { Kind = AssetKinds.Machine, X = x * K, Y = 19.5 * K });
    return l;
}

static List<Segment> Rect(double x1, double y1, double x2, double y2)
{
    var pts = new[] { (x1, y1), (x2, y1), (x2, y2), (x1, y2), (x1, y1) };
    return Enumerable.Range(0, 4).Select(i => (Segment)new LineSeg(new Point2(pts[i].Item1 * 1000, pts[i].Item2 * 1000), new Point2(pts[i + 1].Item1 * 1000, pts[i + 1].Item2 * 1000))).ToList();
}

void Report(Layout l)
{
    foreach (var b in l.Blocks) Console.WriteLine($"block {b.Name} cap {b.Cap} ({b.Rect.Min.X / K:0.###},{b.Rect.Min.Y / K:0.###})-({b.Rect.Max.X / K:0.###},{b.Rect.Max.Y / K:0.###})");
    foreach (var c in l.Cps.Where(c => c.Type == "traffic")) Console.WriteLine($"traffic {c.Name} ({c.Position.X / K:0.###},{c.Position.Y / K:0.###})");
    foreach (var f in LayoutChecker.Check(l, new CheckOptions(CpMaxOffset: 0.05 * K))) Console.WriteLine($"check [{f.Code}] {f.Message}");
}

void Set(IXLWorksheet ws, int row, string col, object v)
{
    var c = ws.Row(1).CellsUsed().First(x => x.GetString() == col).Address.ColumnNumber;
    ws.Cell(row, c).Value = v switch { double d => d, int i => i, _ => v.ToString() };
}

void FillExcel(string xlsx)
{
    using var wb = new XLWorkbook(xlsx);
    var st = wb.Worksheet("Settings");
    foreach (var row in st.RowsUsed().Skip(2))
    {
        var key = row.Cell(1).GetString();
        var v = key switch { "SimLength" => "4", "Warmup" => "0.5", "Replications" => "2", _ => null };
        if (v != null) row.Cell(2).Value = double.Parse(v, C);
    }
    var t = wb.Worksheet("AGV_Types");
    foreach (var (col, v) in new (string, object)[] { ("Category", "예제"), ("SpeedLoaded", 1.0), ("SpeedEmpty", 1.2), ("Accel", 0.3), ("Decel", 0.3), ("Capacity", 1), ("LoadTime", 15), ("UnloadTime", 15),
        ("MTBF", 0), ("MTTR", 0), ("Length", 1.5), ("Width", 1.0), ("Color", "Blue"), ("Kind", "Standard") }) Set(t, 3, col, v);
    var g = wb.Worksheet("AGV_Groups");
    Set(g, 3, "MinCount", 1); Set(g, 3, "MaxCount", 4); Set(g, 3, "Dispatch", "Nearest"); Set(g, 3, "FinishCP", "F001");
    var m = wb.Worksheet("Materials");
    foreach (var (row, station, takt) in new[] { (3, "조립1", 40), (4, "조립2", 60) })
    {
        Set(m, row, "Station", station); Set(m, row, "TaktTime", takt); Set(m, row, "UnitsPerCycle", 1); Set(m, row, "UnitsPerContainer", 10);
        Set(m, row, "CallThreshold", 3); Set(m, row, "UnloadThreshold", 1); Set(m, row, "InitStock", 5); Set(m, row, "SafetyStock", 1); Set(m, row, "Form", "Box");
        Set(m, row, "ExtraTime", 0); Set(m, row, "BufferWaitSec", 0); // the route is on the material's row
    }
    // the template's grey italic example look must go once real values are in
    foreach (var ws in new[] { t, g, m }) foreach (var row in new[] { 3, 4 }) ws.Row(row).Style.Font.Italic = false;
    foreach (var ws in new[] { t, g, m }) foreach (var row in new[] { 3, 4 }) ws.Row(row).Style.Font.FontColor = XLColor.Black;
    wb.Save();
}

// DXF: walls / columns / racks / machines as plain lines (background), optionally the ASH layers (paths, CP circles, block rectangles) + labels
void WriteDxf(string path, Layout l, bool ash)
{
    var ents = new List<LiteEntity>();
    void Box(string layer, double cx, double cy, double w, double h) => ents.Add(new LitePolyline(layer, new[] { new LiteVertex(cx - w / 2, cy - h / 2), new LiteVertex(cx + w / 2, cy - h / 2), new LiteVertex(cx + w / 2, cy + h / 2), new LiteVertex(cx - w / 2, cy + h / 2) }, true));
    foreach (var a in l.Assets)
    {
        if (a.Kind == AssetKinds.Wall) foreach (var s in a.Segments) ents.Add(new LiteLine("A-WALL", s.Start.X, s.Start.Y, s.End.X, s.End.Y));
        else if (ASH.Core.Cel.AssetShapes.Footprint(a.Kind) is { } fp)
            Box(a.Kind == AssetKinds.Column ? "A-COLUMN" : "EQUIP", a.X + (fp.MinX + fp.MaxX) / 2 * K, a.Y + (fp.MinY + fp.MaxY) / 2 * K, (fp.MaxX - fp.MinX) * K, (fp.MaxY - fp.MinY) * K);
    }
    var texts = new List<(double X, double Y, double H, string T, string Layer)>
    {
        (24 * K, -5.2 * K, 500, "RACK", "EQUIP"), (2 * K, 20.6 * K, 500, "LINE", "EQUIP"),
    };
    if (ash)
    {
        foreach (var p in l.Paths) foreach (var s in p.Segments) ents.Add(new LiteLine(p.IsTwoWay ? "AGV_PATH_TWO" : "AGV_PATH", s.Start.X, s.Start.Y, s.End.X, s.End.Y));
        foreach (var c in l.Cps)
        {
            var layer = "AGV_CP_" + c.Type.ToUpperInvariant();
            ents.Add(new LiteCircle(layer, c.Position.X, c.Position.Y, 300));
            texts.Add((c.Position.X + 400, c.Position.Y + 400, 400, c.Name, layer));
        }
        foreach (var b in l.Blocks)
        {
            ents.Add(new LitePolyline("AGV_BLOCK", new[] { new LiteVertex(b.Rect.Min.X, b.Rect.Min.Y), new LiteVertex(b.Rect.Max.X, b.Rect.Min.Y), new LiteVertex(b.Rect.Max.X, b.Rect.Max.Y), new LiteVertex(b.Rect.Min.X, b.Rect.Max.Y) }, true));
            texts.Add((b.Rect.Max.X + 200, b.Rect.Max.Y + 200, 400, b.Name, "AGV_BLOCK"));
        }
        foreach (var p in l.Paths) { var mid = p.PointAt(p.Length / 2); texts.Add((mid.X + 300, mid.Y - 900, 400, p.Name, "AGV_PATH")); }
    }
    var names = ents.Select(e => e.Layer).Concat(texts.Select(t => t.Layer)).Distinct().ToDictionary(x => x, x => x);
    var colors = new Dictionary<string, short> { ["A-WALL"] = 8, ["A-COLUMN"] = 8, ["EQUIP"] = 9, ["TEXT"] = 7 };
    foreach (var lc in LayerColors.Table) colors[lc.Layer] = lc.Default;
    var sw = new StringWriter();
    LiteDxfWriter.Write(sw, ents, names, colors, 0, 0);
    var dxf = sw.ToString();
    // LiteDxfWriter has no TEXT: put the labels in just before the ENTITIES section ends (the last ENDSEC)
    var sb = new StringBuilder();
    foreach (var t in texts)
        sb.Append($"  0\r\nTEXT\r\n  8\r\n{t.Layer}\r\n 10\r\n{t.X.ToString("0.0", C)}\r\n 20\r\n{t.Y.ToString("0.0", C)}\r\n 30\r\n0.0\r\n 40\r\n{t.H.ToString("0.0", C)}\r\n  1\r\n{t.T}\r\n");
    int at = dxf.LastIndexOf("  0\r\nENDSEC", StringComparison.Ordinal);
    // the layer table only lists entity layers: labels sit on layers that also carry geometry, except TEXT / AGV_DIR, which is fine for R12 readers
    File.WriteAllText(path, dxf.Insert(at, sb.ToString()), Encoding.ASCII);
}

if (args.Length == 0) { Console.WriteLine("gen|build|submit|plan"); return 1; }
switch (args[0])
{
    case "plan":
        Report(Build(args.Length > 1 && args[1] == "B"));
        return 0;
    case "gen":
    {
        var dir = Path.GetFullPath(args[1]); bool b = args.Length > 2 && args[2] == "B", cc = args.Length > 2 && args[2] == "C";
        Directory.CreateDirectory(dir);
        var l = Build(b, cc);
        Report(l);
        var xlsx = Path.Combine(dir, "input.xlsx");
        File.Delete(xlsx);
        foreach (var line in ExcelTemplate.Write(xlsx, l, null, 0.001)) Console.WriteLine("AGVEXCEL: " + line); // Scale 0.001 as the plugin passes it (mm drawing)
        FillExcel(xlsx);
        var input = ExcelReader.Read(xlsx, out var xw);
        foreach (var w in xw) Console.WriteLine("excel warning: " + w.Message);
        var findings = InputValidator.Validate(input, l.Cps.Select(c => c.Name).ToList());
        foreach (var f in findings) Console.WriteLine($"input [{f.Code}] {f.EntityId} {f.Message}");
        var s = new ProjectSettings(Scale: 0.001);
        SettingsFile.Save(s, Path.Combine(dir, "ash.settings.tsv"));
        var r = ModelBuilder.Build(new ModelBuildRequest(dir, "model", input, l, new ExportOptions(Units: s.Units, Origin: s.Origin, Scale: s.Scale, Groups: ModelBuilder.VehicleGroups(input))), null, TimeSpan.FromMinutes(5));
        foreach (var f in r.Findings) Console.WriteLine($"build finding [{f.Code}] {f.Message}");
        foreach (var w in r.Warnings) Console.WriteLine("build warning: " + w);
        using (var wb = ExcelReader.Open(xlsx)) CellDump.Write(wb, Path.Combine(dir, CellDump.FileName));
        new ModelFrame(s.Units, 0, 0, s.Scale).Write(Path.Combine(dir, ModelFrame.FileName));
        WriteDxf(Path.Combine(dir, "background.dxf"), l, false);
        WriteDxf(Path.Combine(dir, JobPackage.LocalDxf), l, true);
        // layout in metres for the Pillow illustrations
        object P(Point2 q) => new[] { q.X / K, q.Y / K };
        File.WriteAllText(Path.Combine(dir, "layout.json"), JsonSerializer.Serialize(new
        {
            paths = l.Paths.Select(p => new { name = p.Name, start = P(p.Start), end = P(p.End), two = p.IsTwoWay }),
            cps = l.Cps.Select(c => new { name = c.Name, type = c.Type, group = c.Group, at = P(c.Position) }),
            blocks = l.Blocks.Select(x => new { name = x.Name, cap = x.Cap, min = P(x.Rect.Min), max = P(x.Rect.Max) }),
            assets = l.Assets.Select(a => new { kind = a.Kind, at = new[] { a.X / K, a.Y / K }, fp = ASH.Core.Cel.AssetShapes.Footprint(a.Kind) is { } f ? new[] { f.MinX, f.MinY, f.MaxX, f.MaxY } : null, segs = a.Segments.Select(sg => new[] { sg.Start.X / K, sg.Start.Y / K, sg.End.X / K, sg.End.Y / K }) }),
            layers = LayerColors.Table.Select(x => new { layer = x.Layer, aci = x.Default, rgb = new[] { AciColors.OnWhite(x.Default).R, AciColors.OnWhite(x.Default).G, AciColors.OnWhite(x.Default).B } }),
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("arc: " + r.ArcDir);
        return findings.Any(f => f.Code == FindingCode.InputError) ? 2 : 0;
    }
    case "build":
    {
        var dir = Path.GetFullPath(args[1]);
        ModelBuilder.PrepareBuildDir(dir, "model");
        var bres = ASH.Core.AutoMod.AmBuilder.Build(args[2], dir, "model", TimeSpan.FromMinutes(10));
        Console.WriteLine($"build success {bres.Success} dialogs [{string.Join("|", bres.ClosedDialogTitles)}]\n{bres.MakeErrText}{bres.Am2ErrText}");
        if (!bres.Success) return 3;
        var input = ExcelReader.Read(Path.Combine(dir, "input.xlsx"));
        var o = ModelRunner.Run(new RunRequest(dir, "model", input, new RunScenario(1, new Dictionary<string, int> { ["G1"] = int.Parse(args.Length > 3 ? args[3] : "2") }), TimeSpan.FromMinutes(10), "Student Version"));
        Console.WriteLine($"run success {o.Success} {o.Error} {o.Duration.TotalSeconds:0} s {o.RunDir}");
        if (o.Success) Console.WriteLine(File.ReadAllText(Path.Combine(o.RunDir, "result.tsv")));
        return o.Success ? 0 : 4;
    }
    case "submit":
    {
        var dir = Path.GetFullPath(args[1]);
        var zip = Path.Combine(Path.GetTempPath(), "ash-manual-" + Guid.NewGuid().ToString("N") + ".zip");
        var files = JobPackage.Create(dir, "model", zip);
        Console.WriteLine($"package {files.Count} files: " + string.Join(", ", files.Select(f => f.Path)));
        using var client = new JobServerClient(new Uri(args[2]));
        var rc = await client.SubmitAsync(zip, new JobSubmission(Path.GetFileName(dir), "example", "model", "manual", AnalysisName: args[3], PlanName: args[4], VersionNote: args[5]));
        File.Delete(zip);
        Console.WriteLine($"job {rc.Id} ahead {rc.Ahead} v{rc.Version} {rc.Url}");
        return 0;
    }
}
return 1;
