using System.Linq;
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

    private static void RunWithSettings(string cmd, System.Func<Database, Transaction, Editor, TrafficParams, bool> body)
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (!ExportCommands.TryLoadSettings(doc.Editor, out var s, out _)) return;
        var p = new TrafficParams(s.VehicleLength, s.VehicleWidth, s.SafetyGap, s.BlockReach, s.ClaimDist, s.CpSpacing);
        PathCommands.Run(doc, cmd, (db, tr) => body(db, tr, doc.Editor, p));
    }

    private static bool Blocks(Database db, Transaction tr, Editor ed, TrafficParams p)
    {
        var prop = BlockGenerator.Generate(DrawingReader.Read(db, tr), p);
        var blocks = prop.Blocks;
        foreach (var g in prop.OverlapGroups)
        {
            var members = blocks.Where(b => g.Contains(b.Name)).ToList();
            if (members.Count < 2) continue; // an earlier merge already took them
            var ans = PathCommands.Keyword(ed, $"\n{string.Join(", ", g)} 합칠까요? [Y/N] <Y>", "Y", "Y", "N");
            if (ans == null) throw new System.OperationCanceledException(); // Run aborts the whole command, AGVTRAFFIC included
            if (ans != "Y") continue;
            var merged = BlockGenerator.Merge(members, members[0].Name);
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
