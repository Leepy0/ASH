using System.Globalization;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using ASH.Core.Traffic;
using ASH.Plugin.Cad;
using ASH.Plugin.Ui;
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Runtime;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;
using CoreLayout = ASH.Core.Layout.Layout;

[assembly: CommandClass(typeof(ASH.Plugin.Commands.CpCommands))]

namespace ASH.Plugin.Commands;

public sealed class CpCommands
{
    [CommandMethod("AGVCP")]
    public void AgvCp()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;
        PathCommands.Run(doc, "AGVCP", (db, tr) =>
        {
            var layout = DrawingReader.Read(db, tr);
            if (layout.Paths.Count == 0) { ed.WriteMessage("\n경로가 없습니다."); return false; }
            double maxOffsetM = new CheckOptions().CpMaxOffset;
            var names = layout.Cps.Select(c => c.Name).ToList();
            var naming = new CpNaming(names);
            int added = 0;
            while (true)
            {
                var s = CpSettings.Current;
                var pr = ed.GetPoint(new PromptPointOptions($"\nCP 위치 [{s.Type} · {AutoNamer.PatternFor(s.Type)}] ({(s.DetailedMode ? "상세" : "연속")} 모드, Enter 종료)") { AllowNone = true });
                if (pr.Status != PromptStatus.OK) break;
                var hit = Snap(ed, layout, pr.Value.X, pr.Value.Y, maxOffsetM);
                if (hit == null) continue;
                var prefix = string.IsNullOrEmpty(s.Prefix) ? AutoNamer.PrefixFor(s.Type) : s.Prefix;
                var cp = new LayoutCp { Type = s.Type, Group = s.Group, Cap = s.Cap };
                if (s.DetailedMode)
                {
                    var name = Ask(ed, "NAME", AutoNamer.Next(prefix, names));
                    while (name != null && names.Contains(name))
                    {
                        ed.WriteMessage("\n이미 있는 이름입니다.");
                        name = Ask(ed, "NAME", AutoNamer.Next(prefix, names));
                    }
                    var type = name == null ? null : Ask(ed, "TYPE", s.Type)?.ToLowerInvariant();
                    while (type != null && !CpTypes.All.Contains(type))
                    {
                        ed.WriteMessage($"\nTYPE은 {string.Join(", ", CpTypes.All)} 중 하나입니다.");
                        type = Ask(ed, "TYPE", s.Type)?.ToLowerInvariant();
                    }
                    var group = type == null ? null : Ask(ed, "GROUP", s.Group);
                    while (group != null && !LayoutChecker.IsValidName(group))
                    {
                        ed.WriteMessage("\nGROUP 이름에 공백·예약어·'?'로 시작하는 이름은 쓸 수 없습니다.");
                        group = Ask(ed, "GROUP", s.Group);
                    }
                    var cap = group == null ? null : Ask(ed, "CAP (숫자 또는 INF=무제한)", CpTypes.FormatCap(type == s.Type ? s.Cap : CpTypes.DefaultCap(type)));
                    if (cap == null) break;
                    cp.Name = name!; cp.Type = type!; cp.Group = group!;
                    cp.Cap = CpTypes.TryParseCap(cap, out var c) ? c : CpTypes.DefaultCap(type);
                    names.Add(cp.Name);
                    naming = new CpNaming(names); // typed names must not be handed out again
                }
                else { cp.Name = naming.Next(prefix); names.Add(cp.Name); }
                Place(doc, db, tr, cp, hit.Value);
                added++;
            }
            return added > 0;
        }, refreshArrows: false);
    }

    /// <summary>AGVCPROUTE: one route's CPs in order (standby, load, unload, rtnload, rtnunload, finish), the type following the step instead of CpSettings.Current.Type.</summary>
    [CommandMethod("AGVCPROUTE")]
    public void AgvCpRoute()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;
        PathCommands.Run(doc, "AGVCPROUTE", (db, tr) =>
        {
            var layout = DrawingReader.Read(db, tr);
            if (layout.Paths.Count == 0) { ed.WriteMessage("\n경로가 없습니다."); return false; }
            var group = Ask(ed, "그룹", CpSettings.Current.Group);
            while (group != null && !LayoutChecker.IsValidName(group))
            {
                ed.WriteMessage("\nGROUP 이름에 공백·예약어·'?'로 시작하는 이름은 쓸 수 없습니다.");
                group = Ask(ed, "그룹", CpSettings.Current.Group);
            }
            if (group == null) return false;
            double maxOffsetM = new CheckOptions().CpMaxOffset;
            var naming = new CpNaming(layout.Cps.Select(c => c.Name));
            var seq = new CpRouteSequence();
            var routeId = ASH.Core.SeoulTime.Now.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture); // ties the placed CPs together for AGVEXCEL
            int added = 0;
            while (!seq.Done)
            {
                PaletteStatus.Set(seq.Prompt());
                // Enter and Space both come back as None; S is a keyword so it is not mistaken for a point
                var po = new PromptPointOptions("\n" + seq.Prompt()) { AllowNone = true, AppendKeywordsToMessage = false };
                po.Keywords.Add("S", "S", "S");
                var pr = ed.GetPoint(po);
                if (pr.Status == PromptStatus.None) { seq.Next(); continue; }
                if (pr.Status == PromptStatus.Keyword) { seq.Skip(); continue; }
                if (pr.Status != PromptStatus.OK) break; // Esc: stop here, what is placed stays
                var hit = Snap(ed, layout, pr.Value.X, pr.Value.Y, maxOffsetM);
                if (hit == null) continue;
                var type = seq.Type;
                Place(doc, db, tr, new LayoutCp { Type = type, Group = group, Cap = CpTypes.DefaultCap(type), Name = naming.Next(AutoNamer.PrefixFor(type)), RouteId = routeId }, hit.Value);
                seq.Placed();
                added++;
            }
            ed.WriteMessage("\n" + seq.Summary(group));
            PathCommands.FinalStatus = "AGVCPROUTE: " + seq.Summary(group);
            return added > 0;
        }, refreshArrows: false);
    }

    /// <summary>AGVCPVIEW: applies the palette's group view (PaletteStatus tells the palette the new state when the command ends).</summary>
    [CommandMethod("AGVCPVIEW")]
    public void AgvCpView()
    {
        var req = CpViewRequest.Pending;
        CpViewRequest.Pending = null;
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (req == null) { doc.Editor.WriteMessage("\n루트 탭의 '그룹 보기'에서 실행합니다."); return; }
        PathCommands.Run(doc, "AGVCPVIEW", (db, tr) =>
        {
            CpViewer.Apply(db, tr, req.Mode, req.Selected);
            PathCommands.FinalStatus = req.Selected == null ? "AGVCPVIEW: 모두 표시" : $"AGVCPVIEW: 그룹 {req.Selected.Count}개 보통, 나머지 {(req.Mode == CpViewMode.Hide ? "숨김" : "회색")}";
            return true;
        }, refreshArrows: false);
    }

    // A clicked point on the nearest path, or null (with a message) when it is farther than the allowed offset.
    private static (LayoutPath Path, double Dist, double Offset)? Snap(Editor ed, CoreLayout layout, double x, double y, double maxOffsetM)
    {
        var hit = LayoutChecker.Project(layout, new Point2(x, y));
        if (hit != null && hit.Value.Offset <= maxOffsetM * DrawingUnits.PerMeter) return hit;
        ed.WriteMessage($"\n경로에서 너무 멉니다 (허용 {maxOffsetM} m).");
        return null;
    }

    // Puts the CP on the snapped spot and shows it at once (AGVCP and AGVCPROUTE).
    private static void Place(Document doc, Database db, Transaction tr, LayoutCp cp, (LayoutPath Path, double Dist, double Offset) hit)
    {
        cp.Position = hit.Path.PointAt(hit.Dist);
        DrawingWriter.AddCp(db, tr, cp);
        tr.TransactionManager.QueueForGraphicsFlush();
        doc.TransactionManager.FlushGraphics();
        doc.Editor.WriteMessage($"\nCP {cp.Name} ({cp.Type}, CAP {CpTypes.FormatCap(cp.Cap)}) 삽입, 경로 {hit.Path.Name}.");
    }

    // Enter keeps the default; null on cancel.
    private static string? Ask(Editor ed, string tag, string def)
    {
        var r = ed.GetString(new PromptStringOptions($"\n{tag}") { DefaultValue = def, UseDefaultValue = true, AllowSpaces = false });
        if (r.Status != PromptStatus.OK) return null;
        return string.IsNullOrEmpty(r.StringResult) ? def : r.StringResult;
    }
}
