using System.Collections.Generic;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using ASH.Core.Traffic;
using ASH.Plugin.Cad;
using ASH.Plugin.Ui;
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.GraphicsInterface;
using ZwSoft.ZwCAD.Runtime;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;
using CoreLayout = ASH.Core.Layout.Layout;

[assembly: CommandClass(typeof(ASH.Plugin.Commands.CpEditCommands))]

namespace ASH.Plugin.Commands;

/// <summary>Changes to CPs that are already placed: AGVCPEDIT (group / CAP / type of many at once), AGVCPRENUM (names in travel order), AGVCPMOVE (slide along the path).</summary>
public sealed class CpEditCommands
{
    [CommandMethod("AGVCPEDIT")]
    public void AgvCpEdit()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;
        PathCommands.Run(doc, "AGVCPEDIT", (db, tr) =>
        {
            var ids = SelectCps(ed, tr, "\n편집할 CP 선택");
            if (ids.Count == 0) { ed.WriteMessage("\nCP를 선택하지 않았습니다."); return false; }
            var cps = ids.Select(id => ReadCp(tr, id)).ToList();
            int? sharedCap = cps.Select(c => c.Cap).Distinct().Count() == 1 ? cps[0].Cap : (int?)null;
            string? sharedGroup = cps.Select(c => c.Group).Distinct().Count() == 1 ? cps[0].Group : null;
            string? sharedType = cps.Select(c => c.Type).Distinct().Count() == 1 ? cps[0].Type : null;
            using var dlg = new CpEditDialog(cps.Count, sharedGroup, sharedCap, sharedType);
            if (Application.ShowModalDialog(dlg) != System.Windows.Forms.DialogResult.OK) { PathCommands.FinalStatus = "AGVCPEDIT: 취소"; return false; }

            var (group, cap, type) = (dlg.Group, dlg.Cap, dlg.Type);
            var renames = new List<CpRename>();
            if (dlg.Rename && type != null)
            {
                // numbered in travel order after the highest number the unselected CPs already use
                var layout = DrawingReader.Read(db, tr);
                var mine = new HashSet<string>(cps.Select(c => c.Id));
                var naming = new CpNaming(layout.Cps.Where(c => !mine.Contains(c.Id)).Select(c => c.Name));
                var prefix = AutoNamer.PrefixFor(type);
                foreach (var cp in CpRenumber.TravelOrder(layout, cps)) renames.Add(new CpRename(cp, cp.Name, naming.Next(prefix)));
            }
            var newName = renames.ToDictionary(r => r.Cp.Id, r => r.NewName);
            int missing = 0;
            foreach (var (id, cp) in ids.Zip(cps, (i, c) => (i, c)))
            {
                if (group != null) missing += DrawingWriter.SetAttribute(tr, id, "GROUP", group) ? 0 : 1;
                if (cap != null) missing += DrawingWriter.SetAttribute(tr, id, "CAP", CpTypes.FormatCap(cap.Value)) ? 0 : 1;
                if (newName.TryGetValue(cp.Id, out var n)) missing += DrawingWriter.SetAttribute(tr, id, "NAME", n) ? 0 : 1;
                if (type != null)
                {
                    missing += DrawingWriter.SetAttribute(tr, id, "TYPE", type) ? 0 : 1;
                    DrawingWriter.SetLayer(tr, id, Conventions.LayerCpFor(type, cap ?? cp.Cap)); // the type's own layer: colour and on/off follow the type
                }
                else if (cap != null) DrawingWriter.SetLayer(tr, id, Conventions.LayerCpFor(cp.Type, cap.Value)); // traffic CAP 1 ↔ other CAP changes layer
            }
            ed.WriteMessage($"\nCP {cps.Count}개 수정.");
            if (missing > 0) ed.WriteMessage($"\n어트리뷰트가 없는 항목 {missing}개는 건너뜀 (AGV_CP 블록 정의 확인).");
            Report(ed, renames);
            PathCommands.FinalStatus = $"AGVCPEDIT: CP {cps.Count}개 수정" + (renames.Count > 0 ? " · 이름 변경 (Excel 수정 필요)" : "");
            return true;
        }, refreshArrows: false);
    }

    [CommandMethod("AGVCPRENUM")]
    public void AgvCpRenum()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;
        PathCommands.Run(doc, "AGVCPRENUM", (db, tr) =>
        {
            var layout = DrawingReader.Read(db, tr);
            if (layout.Cps.Count == 0) { ed.WriteMessage("\nCP가 없습니다."); return false; }
            if (layout.Paths.Count == 0) { ed.WriteMessage("\n경로가 없습니다."); return false; }
            var present = CpTypes.All.Concat(layout.Cps.Select(c => c.Type).Where(t => System.Array.IndexOf(CpTypes.All, t) < 0)).Distinct().Where(t => layout.Cps.Any(c => c.Type == t)).ToList();
            var keys = new[] { "All" }.Concat(present).ToArray();
            var type = PathCommands.Keyword(ed, $"\n번호를 다시 매길 종류 [{string.Join("/", keys)}] <All>", "All", keys);
            if (type == null) return false;
            var pr = ed.GetPoint(new PromptPointOptions("\n시작 위치 (Enter = 자동, 흐름이 시작되는 경로부터)") { AllowNone = true });
            if (pr.Status != PromptStatus.OK && pr.Status != PromptStatus.None) return false;
            Point2? start = pr.Status == PromptStatus.OK ? new Point2(pr.Value.X, pr.Value.Y) : (Point2?)null;

            var plan = CpRenumber.Plan(layout, type == "All" ? null : new[] { type }, start);
            var changed = plan.Where(r => r.Changed).ToList();
            if (changed.Count == 0) { ed.WriteMessage($"\nCP {plan.Count}개 모두 이미 순서대로입니다."); PathCommands.FinalStatus = "AGVCPRENUM: 바뀐 이름 없음"; return false; }
            foreach (var r in changed) DrawingWriter.SetAttribute(tr, DrawingWriter.FromHandle(db, r.Cp.Id), "NAME", r.NewName);
            ed.WriteMessage($"\nCP {plan.Count}개 중 {changed.Count}개 이름 변경.");
            Report(ed, changed);
            PathCommands.FinalStatus = $"AGVCPRENUM: {changed.Count}개 이름 변경 (Excel 수정 필요)";
            return true;
        }, refreshArrows: false);
    }

    [CommandMethod("AGVCPMOVE")]
    public void AgvCpMove()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;
        PathCommands.Run(doc, "AGVCPMOVE", (db, tr) =>
        {
            var layout = DrawingReader.Read(db, tr);
            if (layout.Paths.Count == 0) { ed.WriteMessage("\n경로가 없습니다."); return false; }
            int moved = 0;
            bool first = true;
            while (true)
            {
                var id = PickCp(ed, tr, "\n이동할 CP 선택 (Enter 종료)", first);
                first = false;
                if (id.IsNull) break;
                var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                var from = br.Position;
                var hit = LayoutChecker.Project(layout, new Point2(from.X, from.Y));
                if (hit == null) continue;
                // only the paths joined to the CP's own: the CP slides along one network and across its joints
                var sub = new CoreLayout { Paths = CpRenumber.ConnectedPaths(layout, hit.Value.Path), Tolerance = layout.Tolerance };
                var jig = new SlideJig(sub, from, 0.3 * br.ScaleFactors.X);
                if (ed.Drag(jig).Status != PromptStatus.OK) continue; // Esc: this CP stays, pick the next one
                br.UpgradeOpen();
                br.TransformBy(Matrix3d.Displacement(jig.Position - from)); // the NAME text etc. move with the block
                tr.TransactionManager.QueueForGraphicsFlush();
                doc.TransactionManager.FlushGraphics();
                ed.WriteMessage($"\nCP {ReadCp(tr, id).Name} → 경로 {jig.PathName}.");
                moved++;
            }
            PathCommands.FinalStatus = moved > 0 ? $"AGVCPMOVE: CP {moved}개 이동" : null;
            return moved > 0;
        }, refreshArrows: false);
    }

    /// <summary>Follows the cursor onto the nearest of the given paths: a circle the size of the CP symbol shows where a click puts it.</summary>
    private sealed class SlideJig : DrawJig
    {
        private readonly CoreLayout _paths;
        private readonly double _radius;

        public SlideJig(CoreLayout paths, Point3d start, double radius) { _paths = paths; Position = start; _radius = radius; }

        public Point3d Position { get; private set; }
        public string PathName { get; private set; } = "";

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var r = prompts.AcquirePoint(new JigPromptPointOptions("\n새 위치 (경로를 따라 이동, 클릭으로 확정)"));
            if (r.Status != PromptStatus.OK) return SamplerStatus.Cancel;
            var hit = LayoutChecker.Project(_paths, new Point2(r.Value.X, r.Value.Y));
            if (hit == null) return SamplerStatus.NoChange;
            var p = hit.Value.Path.PointAt(hit.Value.Dist);
            var np = new Point3d(p.X, p.Y, Position.Z);
            PathName = hit.Value.Path.Name;
            if (np.DistanceTo(Position) < 1e-9) return SamplerStatus.NoChange;
            Position = np;
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            draw.SubEntityTraits.Color = 3;
            draw.Geometry.Circle(Position, _radius, Vector3d.ZAxis);
            return true;
        }
    }

    private static bool IsCp(Transaction tr, ObjectId id) =>
        tr.GetObject(id, OpenMode.ForRead) is BlockReference br && !br.IsErased && Conventions.IsCpLayer(br.Layer)
        && DrawingReader.Eq(DrawingReader.BlockName(br, tr), Conventions.CpBlockName);

    private static LayoutCp ReadCp(Transaction tr, ObjectId id)
    {
        var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
        var a = DrawingReader.Attributes(br, tr);
        string Get(string t) => a.TryGetValue(t, out var v) ? v.Trim() : "";
        return new LayoutCp
        {
            Name = Get("NAME"), Type = Get("TYPE").ToLowerInvariant(), Group = Get("GROUP"),
            Cap = CpTypes.TryParseCap(Get("CAP"), out var c) ? c : 1,
            Position = DrawingReader.Xy(br.Position), Id = br.Handle.ToString(),
        };
    }

    /// <summary>The CPs already selected (PICKFIRST, cleared afterwards) or, when there are none, a prompt.</summary>
    private static List<ObjectId> SelectCps(Editor ed, Transaction tr, string msg)
    {
        var imp = ed.SelectImplied();
        if (imp.Status == PromptStatus.OK && imp.Value.Count > 0)
        {
            var pre = imp.Value.GetObjectIds().Where(i => IsCp(tr, i)).ToList();
            ed.SetImpliedSelection(new ObjectId[0]);
            if (pre.Count > 0) return pre;
        }
        var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "INSERT"), new TypedValue((int)DxfCode.LayerName, Conventions.LayerCp + "*") });
        var r = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = msg }, filter);
        return r.Status == PromptStatus.OK ? r.Value.GetObjectIds().Where(i => IsCp(tr, i)).ToList() : new List<ObjectId>();
    }

    /// <summary>One CP (a selected one when asked, else a pick; a click on its text counts); null id on Enter or Esc.</summary>
    private static ObjectId PickCp(Editor ed, Transaction tr, string msg, bool pickFirst)
    {
        if (pickFirst)
        {
            var imp = ed.SelectImplied();
            var pre = imp.Status == PromptStatus.OK ? imp.Value.GetObjectIds().Where(i => IsCp(tr, i)).ToList() : new List<ObjectId>();
            if (pre.Count > 0) { ed.SetImpliedSelection(new ObjectId[0]); return pre[0]; }
        }
        while (true)
        {
            var r = ed.GetEntity(new PromptEntityOptions(msg) { AllowNone = true });
            if (r.Status != PromptStatus.OK) return ObjectId.Null;
            var id = r.ObjectId;
            if (tr.GetObject(id, OpenMode.ForRead) is AttributeReference ar) id = ar.OwnerId;
            if (IsCp(tr, id)) return id;
            ed.WriteMessage("\nCP가 아닙니다.");
        }
    }

    /// <summary>Old → new table on the command line (first 60) and, whole, on the clipboard as TSV. The Excel rows still hold the old names, so say so.</summary>
    private static void Report(Editor ed, IReadOnlyList<CpRename> renames)
    {
        var changed = renames.Where(r => r.Changed).ToList();
        if (changed.Count == 0) return;
        ed.WriteMessage("\n이전 → 새 이름:");
        foreach (var r in changed.Take(60)) ed.WriteMessage($"\n  {r.OldName} → {r.NewName}");
        if (changed.Count > 60) ed.WriteMessage($"\n  … 외 {changed.Count - 60}개");
        try
        {
            System.Windows.Forms.Clipboard.SetText(CpRenumber.Tsv(changed));
            ed.WriteMessage("\n표 전체를 클립보드에 복사했습니다 (이전 이름 · 새 이름 · 종류 · 그룹).");
        }
        catch (System.Exception ex) { ed.WriteMessage("\n클립보드 복사 실패: " + ex.Message); }
        ed.WriteMessage("\n주의: Excel 행은 이전 이름을 가리킵니다. 위 표로 직접 고치세요.");
    }
}
