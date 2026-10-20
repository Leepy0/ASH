using System.Globalization;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using ASH.Core.Traffic;
using ASH.Plugin.Cad;
using ASH.Plugin.Ui;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Runtime;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;

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
            double maxOffsetM = new CheckOptions().CpMaxOffset, maxOffset = maxOffsetM * DrawingUnits.PerMeter;
            var names = layout.Cps.Select(c => c.Name).ToList();
            var naming = new CpNaming(names);
            int added = 0;
            while (true)
            {
                var s = CpSettings.Current;
                var pr = ed.GetPoint(new PromptPointOptions($"\nCP 위치 ({(s.DetailedMode ? "상세" : "연속")} 모드, Enter 종료)") { AllowNone = true });
                if (pr.Status != PromptStatus.OK) break;
                var hit = LayoutChecker.Project(layout, new Point2(pr.Value.X, pr.Value.Y));
                if (hit == null || hit.Value.Offset > maxOffset)
                {
                    ed.WriteMessage($"\n경로에서 너무 멉니다 (허용 {maxOffsetM} m).");
                    continue;
                }
                var prefix = string.IsNullOrEmpty(s.Prefix) ? AutoNamer.PrefixFor(s.Type) : s.Prefix;
                var cp = new LayoutCp
                {
                    Type = s.Type, Group = s.Group, Station = s.Station, Cap = s.Cap,
                    Position = hit.Value.Path.PointAt(hit.Value.Dist),
                };
                if (s.DetailedMode)
                {
                    var name = Ask(ed, "NAME", AutoNamer.Next(prefix, names));
                    while (name != null && names.Contains(name))
                    {
                        ed.WriteMessage("\n이미 있는 이름입니다.");
                        name = Ask(ed, "NAME", AutoNamer.Next(prefix, names));
                    }
                    var type = name == null ? null : Ask(ed, "TYPE", s.Type)?.ToLowerInvariant();
                    while (type != null && !CpSettings.CpTypes.Contains(type))
                    {
                        ed.WriteMessage($"\nTYPE은 {string.Join(", ", CpSettings.CpTypes)} 중 하나입니다.");
                        type = Ask(ed, "TYPE", s.Type)?.ToLowerInvariant();
                    }
                    var group = type == null ? null : Ask(ed, "GROUP", s.Group);
                    while (group != null && !LayoutChecker.IsValidName(group))
                    {
                        ed.WriteMessage("\nGROUP 이름에 공백·예약어·'?'로 시작하는 이름은 쓸 수 없습니다.");
                        group = Ask(ed, "GROUP", s.Group);
                    }
                    var station = group == null ? null : Ask(ed, "STATION", s.Station);
                    var cap = station == null ? null : Ask(ed, "CAP", s.Cap.ToString(CultureInfo.InvariantCulture));
                    if (cap == null) break;
                    cp.Name = name!; cp.Type = type!; cp.Group = group!; cp.Station = station!;
                    cp.Cap = int.TryParse(cap, NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) && c > 0 ? c : 1;
                    names.Add(cp.Name);
                    naming = new CpNaming(names); // typed names must not be handed out again
                }
                else { cp.Name = naming.Next(prefix); names.Add(cp.Name); }
                DrawingWriter.AddCp(db, tr, cp);
                tr.TransactionManager.QueueForGraphicsFlush();
                doc.TransactionManager.FlushGraphics();
                ed.WriteMessage($"\nCP {cp.Name} ({cp.Type}) 삽입, 경로 {hit.Value.Path.Name}.");
                added++;
            }
            return added > 0;
        }, refreshArrows: false);
    }

    // Enter keeps the default; null on cancel.
    private static string? Ask(Editor ed, string tag, string def)
    {
        var r = ed.GetString(new PromptStringOptions($"\n{tag}") { DefaultValue = def, UseDefaultValue = true, AllowSpaces = false });
        if (r.Status != PromptStatus.OK) return null;
        return string.IsNullOrEmpty(r.StringResult) ? def : r.StringResult;
    }
}
