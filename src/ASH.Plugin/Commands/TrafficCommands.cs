using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Settings;
using ASH.Core.Traffic;
using ASH.Plugin.Cad;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Runtime;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ASH.Plugin.Commands.TrafficCommands))]

namespace ASH.Plugin.Commands;

public sealed class TrafficCommands
{
    [CommandMethod("AGVBLOCKAUTO")]
    public void AgvBlockAuto() => RunWithSettings("AGVBLOCKAUTO", (db, tr, ed, p) => Blocks(db, tr, ed, p));

    [CommandMethod("AGVCPAUTO")]
    public void AgvCpAuto() => RunWithSettings("AGVCPAUTO", (db, tr, ed, p) => Cps(db, tr, ed, p));

    [CommandMethod("AGVTRAFFIC")]
    public void AgvTraffic() => RunWithSettings("AGVTRAFFIC", (db, tr, ed, p) => Blocks(db, tr, ed, p) | Cps(db, tr, ed, p)); // | : run both

    /// <summary>Renames every block on AGV_BLOCK B001.. from the drawing origin (BlockGenerator.Renumber), hand-drawn rects included: one without a
    /// tag gets one at its centre. Nothing else in the drawing stores block names (traffic.tsv is rebuilt from geometry on export).</summary>
    [CommandMethod("AGVBLOCKNUM")]
    public void AgvBlockNum()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;
        PathCommands.Run(doc, "AGVBLOCKNUM", (db, tr) =>
        {
            var blocks = DrawingReader.Read(db, tr).Blocks;
            if (blocks.Count == 0) { ed.WriteMessage($"\n{Conventions.LayerBlock} 레이어에 닫힌 사각형(block)이 없습니다."); return false; }
            var tagged = new HashSet<string>();
            int added = 0;
            foreach (var (b, name) in BlockGenerator.Renumber(blocks))
            {
                // a tag can sit inside two nested rects; only the first keeps it, the other gets its own
                var id = b.TagId.Length > 0 && tagged.Add(b.TagId) ? DrawingWriter.FromHandle(db, b.TagId) : ObjectId.Null;
                if (!id.IsNull && DrawingWriter.SetAttribute(tr, id, "NAME", name)) continue;
                var c = new Point2((b.Rect.Min.X + b.Rect.Max.X) / 2, (b.Rect.Min.Y + b.Rect.Max.Y) / 2);
                DrawingWriter.InsertBlock(db, tr, Conventions.BlockBlockName, Conventions.LayerBlock, c, 0, new Dictionary<string, string>
                {
                    ["NAME"] = name, ["CAP"] = (b.Cap > 0 ? b.Cap : 1).ToString(CultureInfo.InvariantCulture),
                });
                added++;
            }
            ed.WriteMessage($"\nblock {blocks.Count}개 채번: B001~B{blocks.Count:D3}, 원점(0,0)에서 가까운 순" +
                $"{(added > 0 ? $" (태그 없던 {added}개에 태그 추가)" : "")}. 이전 실행 결과·보고서의 block 이름은 바뀌지 않습니다.");
            return true;
        }, refreshArrows: false);
    }

    private static void RunWithSettings(string cmd, System.Func<Database, Transaction, Editor, TrafficParams, bool> body)
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (!ExportCommands.TryLoadSettings(doc.Editor, out var s, out _)) { Ui.PaletteStatus.Set($"{cmd} 중단: 설정 파일 확인 (명령줄 참고)"); return; }
        double u = UnitScale.FromSettings(s); // settings are meters, the drawing is in drawing units
        var p = new TrafficParams(s.VehicleLength * u, s.VehicleWidth * u, s.SafetyGap * u, s.BlockReach * u, s.ClaimDist * u, s.CpSpacing * u);
        PathCommands.Run(doc, cmd, (db, tr) => body(db, tr, doc.Editor, p), refreshArrows: false, unitsPerMeter: u);
    }

    private static bool Blocks(Database db, Transaction tr, Editor ed, TrafficParams p)
    {
        var layout = DrawingReader.Read(db, tr);
        var prop = BlockGenerator.Generate(layout, p);
        var blocks = prop.Blocks;
        foreach (var g in prop.OverlapGroups)
        {
            var members = blocks.Where(b => g.Contains(b.Name)).ToList();
            if (members.Count < 2) continue; // an earlier merge already took them
            var merged = BlockGenerator.Merge(members, members[0].Name);
            // the union box can swallow a load/standby CP between the junctions: a vehicle stopping there would hold the merged block forever
            var stops = BlockGenerator.StopCpsIn(layout, merged.Rect);
            var warn = stops.Count > 0 ? $" (정차 CP {string.Join(", ", stops)}가 안에 들어가 교착 위험)" : "";
            var ans = PathCommands.Keyword(ed, $"\n{string.Join(", ", g)} 가까워 합칠까요?{warn} [Y/N] <{(stops.Count > 0 ? "N" : "Y")}>", stops.Count > 0 ? "N" : "Y", "Y", "N");
            if (ans == null) throw new System.OperationCanceledException(); // Run aborts the whole command, AGVTRAFFIC included
            if (ans != "Y") continue;
            blocks.Insert(blocks.IndexOf(members[0]), merged);
            blocks.RemoveAll(members.Contains);
        }
        foreach (var b in blocks) DrawingWriter.AddBlock(db, tr, b);
        foreach (var w in prop.Warnings) ed.WriteMessage($"\n경고: {w}");
        ed.WriteMessage($"\nblock {blocks.Count}개 생성{(blocks.Count > 0 ? ": " + string.Join(", ", blocks.Select(b => b.Name)) : "")}.");
        return blocks.Count > 0;
    }

    // Re-reads the drawing so blocks made earlier in the same transaction are seen.
    private static bool Cps(Database db, Transaction tr, Editor ed, TrafficParams p)
    {
        var prop = TrafficCpGenerator.Generate(DrawingReader.Read(db, tr), p);
        foreach (var cp in prop.Cps) DrawingWriter.AddCp(db, tr, cp);
        foreach (var w in prop.Warnings) ed.WriteMessage($"\n경고: {w}");
        ed.WriteMessage($"\ntraffic CP {prop.Cps.Count}개 생성{(prop.Cps.Count > 0 ? ": " + string.Join(", ", prop.Cps.Select(c => c.Name)) : "")}.");
        return prop.Cps.Count > 0;
    }
}
