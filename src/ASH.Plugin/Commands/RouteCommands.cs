using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ASH.Core.Cause;
using ASH.Core.Geometry;
using ASH.Core.Input;
using ASH.Plugin.Cad;
using ASH.Plugin.Ui;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Runtime;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ASH.Plugin.Commands.RouteCommands))]

namespace ASH.Plugin.Commands;

/// <summary>Route view (AGVROUTE): one material's CPs and the directed shortest path of each leg, drawn on AGV_ROUTE. Re-running replaces it; "지우기" clears the layer.</summary>
public sealed class RouteCommands
{
    const string ClearKey = "지우기", ClearKeyAscii = "CLEAR";
    const short SupplyColor = InitCommands.ByLayer, ReturnColor = 30, StandbyColor = 8, ProblemColor = 1;
    const double LineWidthM = 0.4, CircleRadiusM = 1.0, NumberHeightM = 0.9, NameHeightM = 0.6, DistHeightM = 0.7;

    // the Excel input read last, so a second material from the palette does not wait for Excel again; reread when the file changed or "목록 새로고침"
    static (string Path, DateTime Stamp, ProjectInput Input)? _cache;

    [CommandMethod("AGVROUTE")]
    public void AgvRoute()
    {
        var req = RouteRequest.Pending;
        RouteRequest.Pending = null;
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;
        var kind = req?.Do ?? RouteRequest.Kind.Show;
        string material = req?.Material ?? "";
        ProjectInput? input = null;
        if (kind != RouteRequest.Kind.Clear)
        {
            input = Load(ed, force: kind == RouteRequest.Kind.List);
            if (input == null) { PaletteStatus.Set("AGVROUTE: Excel 입력 없음"); return; }
            RouteRequest.Materials?.Invoke(input.Materials.Select(m => (m.Material, m.Group)).ToList());
            if (kind == RouteRequest.Kind.List) { PaletteStatus.Set($"AGVROUTE: 자재 {input.Materials.Count}개"); return; }
            if (req == null)
            {
                var names = input.Materials.Select(m => m.Material).ToList();
                if (names.Count == 0) { ed.WriteMessage("\nExcel 입력에 자재가 없습니다."); return; }
                ed.WriteMessage($"\n자재 {names.Count}개: {string.Join(", ", names)}");
                // bare keywords between the slashes (ZWCAD offers each as a choice); a name with a space cannot be a keyword, pick it in the palette
                var keys = names.Where(n => n.Length > 0 && !n.Any(char.IsWhiteSpace)).Concat(new[] { ClearKey, ClearKeyAscii }).Distinct().ToArray();
                var pick = PathCommands.Keyword(ed, $"\n루트 볼 자재 [{string.Join("/", keys)}]", "", keys);
                if (string.IsNullOrEmpty(pick)) return;
                if (pick == ClearKey || pick == ClearKeyAscii) kind = RouteRequest.Kind.Clear; else material = pick!;
            }
        }
        PathCommands.Run(doc, "AGVROUTE", (db, tr) =>
        {
            int erased = Erase(db, tr);
            if (kind == RouteRequest.Kind.Clear) { PathCommands.FinalStatus = erased > 0 ? $"AGVROUTE: 지움 {erased}개" : "AGVROUTE: 지울 것 없음"; return erased > 0; }
            var route = RouteView.Build(DrawingReader.Read(db, tr), input!, material);
            if (route == null)
            {
                ed.WriteMessage($"\n{material}: Excel {input!.RouteSheet}에 경로 행이 없습니다.");
                PathCommands.FinalStatus = $"AGVROUTE: {material} 루트 행 없음";
                return erased > 0;
            }
            Draw(db, tr, route);
            Report(ed, route);
            int bad = route.Legs.Count(l => l.Problem != null);
            PathCommands.FinalStatus = $"AGVROUTE: {material} 구간 {route.Legs.Count}개" + (bad > 0 ? $" · 문제 {bad}개" : "");
            return true;
        }, refreshArrows: false);
    }

    static ProjectInput? Load(Editor ed, bool force)
    {
        if (!ExportCommands.TryLoadSettings(ed, out _, out var dir)) return null;
        var xlsx = Path.Combine(dir, ModelCommands.ExcelFileName);
        if (!File.Exists(xlsx)) { ed.WriteMessage($"\n{ModelCommands.ExcelFileName}이 없습니다. 먼저 AGVEXCEL로 양식을 만들어 채우세요."); return null; }
        var stamp = File.GetLastWriteTimeUtc(xlsx);
        if (!force && _cache is { } c && c.Path == xlsx && c.Stamp == stamp) return c.Input;
        var input = ModelCommands.LoadInput(ed, xlsx, dir);
        if (input != null) _cache = (xlsx, stamp, input);
        return input;
    }

    static int Erase(Database db, Transaction tr)
    {
        int n = 0;
        foreach (var id in DrawingReader.ModelSpace(db, tr, OpenMode.ForRead))
            if (tr.GetObject(id, OpenMode.ForRead) is Entity e && DrawingReader.Eq(e.Layer, Conventions.LayerRoute)) { e.UpgradeOpen(); e.Erase(); n++; }
        return n;
    }

    static void Report(Editor ed, MaterialRoute r)
    {
        double perM = DrawingUnits.PerMeter;
        ed.WriteMessage($"\n루트 {r.Material} (그룹 {r.Group}): " + string.Join(" → ", r.Stops.Where(s => s.Label.Length > 0).GroupBy(s => s.Label).Select(g => string.Join(",", g.Select(s => s.Name))))); // load CPs that take turns share label 1
        foreach (var l in r.Legs)
            ed.WriteMessage(l.Problem != null ? $"\n  {l.Problem}" : $"\n  {l.From}→{l.To}  {l.Distance!.Value / perM:0.#} m");
        double sum = r.Legs.Where(l => l.Kind != RouteLegKind.Standby && l.Distance != null).Sum(l => l.Distance!.Value) / perM;
        ed.WriteMessage($"\n  공급·회송 합계 {sum:0.#} m  (공급 레이어색 · 회송 주황 · 대기 회색 · 문제 빨강)");
    }

    static void Draw(Database db, Transaction tr, MaterialRoute r)
    {
        double u = DrawingUnits.PerMeter;
        var pos = r.Stops.Where(s => s.Position != null).ToDictionary(s => s.Name, s => s.Position!.Value);
        // standby legs first, so the supply and return legs lie on top where they share a path
        foreach (var leg in r.Legs.OrderBy(l => l.Kind == RouteLegKind.Standby ? 0 : l.Kind == RouteLegKind.Return ? 1 : 2))
        {
            if (leg.Problem != null)
            {
                if (pos.TryGetValue(leg.From, out var a) && pos.TryGetValue(leg.To, out var b))
                {
                    Add(db, tr, Line(new[] { a, b }, 0.15 * u, ProblemColor));
                    Add(db, tr, Text(db, tr, leg.Problem, new Point2((a.X + b.X) / 2, (a.Y + b.Y) / 2), DistHeightM * u, ProblemColor));
                }
                else if (pos.TryGetValue(leg.From, out a) || pos.TryGetValue(leg.To, out a))
                    Add(db, tr, Text(db, tr, leg.Problem, new Point2(a.X, a.Y - 2 * CircleRadiusM * u), DistHeightM * u, ProblemColor));
                continue;
            }
            short color = leg.Kind == RouteLegKind.Supply ? SupplyColor : leg.Kind == RouteLegKind.Return ? ReturnColor : StandbyColor;
            Add(db, tr, Line(leg.Points, LineWidthM * u, color));
            var mid = Middle(leg.Points);
            Add(db, tr, Text(db, tr, $"{leg.Distance!.Value / u:0.#} m", new Point2(mid.X, mid.Y + 0.5 * u), DistHeightM * u, color));
        }
        foreach (var s in r.Stops.Where(s => s.Position != null))
        {
            var p = s.Position!.Value;
            Add(db, tr, new Circle(new Point3d(p.X, p.Y, 0), Vector3d.ZAxis, CircleRadiusM * u) { Layer = Conventions.LayerRoute, ColorIndex = Commands.InitCommands.ByLayer });
            if (s.Label.Length > 0) Add(db, tr, Text(db, tr, s.Label, p, NumberHeightM * u, Commands.InitCommands.ByLayer, center: true));
            Add(db, tr, Text(db, tr, $"{s.Name} {s.Role}", new Point2(p.X + 1.4 * CircleRadiusM * u, p.Y - 0.5 * NameHeightM * u), NameHeightM * u, Commands.InitCommands.ByLayer));
        }
    }

    static Polyline Line(IReadOnlyList<Point2> pts, double width, short color)
    {
        var pl = new Polyline { Layer = Conventions.LayerRoute, ColorIndex = color, ConstantWidth = width };
        for (int i = 0; i < pts.Count; i++) pl.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
        return pl;
    }

    // the point half way along the polyline
    static Point2 Middle(IReadOnlyList<Point2> pts)
    {
        double total = 0;
        for (int i = 1; i < pts.Count; i++) total += pts[i - 1].DistanceTo(pts[i]);
        double d = total / 2;
        for (int i = 1; i < pts.Count; i++)
        {
            double len = pts[i - 1].DistanceTo(pts[i]);
            if (d <= len && len > 0) return new Point2(pts[i - 1].X + (pts[i].X - pts[i - 1].X) * d / len, pts[i - 1].Y + (pts[i].Y - pts[i - 1].Y) * d / len);
            d -= len;
        }
        return pts[pts.Count - 1];
    }

    static DBText Text(Database db, Transaction tr, string text, Point2 at, double height, short color, bool center = false)
    {
        var t = new DBText { Layer = Conventions.LayerRoute, ColorIndex = color, TextString = text, Height = height, TextStyleId = Asset3dCommands.KoreanTextStyle(db, tr) };
        if (center)
        {
            t.HorizontalMode = TextHorizontalMode.TextCenter;
            t.VerticalMode = TextVerticalMode.TextVerticalMid;
            t.AlignmentPoint = new Point3d(at.X, at.Y, 0);
            t.AdjustAlignment(db);
        }
        else t.Position = new Point3d(at.X, at.Y, 0);
        return t;
    }

    static void Add(Database db, Transaction tr, Entity e)
    {
        var ms = (BlockTableRecord)tr.GetObject(PathCommands.ModelSpaceId(db, tr), OpenMode.ForWrite);
        try { ms.AppendEntity(e); } catch { e.Dispose(); throw; }
        tr.AddNewlyCreatedDBObject(e, true);
    }
}
