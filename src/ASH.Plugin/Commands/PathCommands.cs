using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using ASH.Core.Settings;
using ASH.Core.Traffic;
using ASH.Plugin.Cad;
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Runtime;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;
using CoreLayout = ASH.Core.Layout.Layout;

[assembly: CommandClass(typeof(ASH.Plugin.Commands.PathCommands))]

namespace ASH.Plugin.Commands;

public sealed class PathCommands
{
    // ---------- AGVPATH: PLINE inside the command, then tag the new polyline(s) ----------

    [CommandMethod("AGVPATH")]
    public void AgvPath()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;
        var key = Keyword(ed, KindPrompt, "O", "O", "T", "S", "C");
        if (key == null) return;
        var kind = KindOf(key);
        var db = doc.Database;
        ObjectId ms;
        using (var t = db.TransactionManager.StartTransaction()) ms = ModelSpaceId(db, t);
        // a layout sheet or the block editor would hand over sheet or block-local coordinates, which land as paths around 0,0 in model space
        if (db.CurrentSpaceId != ms) { ed.WriteMessage("\n경로는 모델 공간에서만 그립니다. 모델 탭(또는 레이아웃의 뷰포트 안)에서 다시 실행하세요."); return; }
        var added = new List<ObjectId>();
        // only model space: other commands (INSERT, paste, ASH's own block definitions) append polylines to block definitions in local coordinates
        ObjectEventHandler onAppend = (s, e) => { if (e.DBObject is Polyline pl && pl.OwnerId == ms) added.Add(pl.ObjectId); };
        db.ObjectAppended += onAppend;
        var inline = true;
        try
        {
            // PLINE runs INSIDE this command, so the command that Enter repeats is AGVPATH (with its type question), not a bare PLINE
            ed.Command("_.PLINE");
        }
        catch (ZwSoft.ZwCAD.Runtime.Exception ex) when (ex.ErrorStatus == ErrorStatus.UserBreak) { } // Esc during PLINE: keep what was drawn
        catch (System.Exception ex) when (added.Count == 0)
        {
            PluginLog.Write("AGVPATH: Editor.Command(PLINE) failed, starting PLINE after this command instead", ex);
            inline = false;
        }
        // Command returned but PLINE is still running: this ZWCAD does not wait for the user, so tag when PLINE ends instead
        var stillDrawing = inline && added.Count == 0 && (Application.GetSystemVariable("CMDNAMES") as string ?? "").IndexOf("PLINE", StringComparison.OrdinalIgnoreCase) >= 0;
        if (stillDrawing) PluginLog.Write("AGVPATH: Editor.Command(PLINE) returned while PLINE is running, tagging when it ends");
        if (inline && !stillDrawing) db.ObjectAppended -= onAppend;
        if (inline && !stillDrawing) TagNewPaths(doc, added, kind);
        else AgvPathDeferred(doc, added, onAppend, kind, startPline: !stillDrawing && !inline);
    }

    // Fallback when Editor.Command is not available: PLINE is queued, and the paths are tagged when it ends (Enter then repeats a bare PLINE).
    private static void AgvPathDeferred(Document doc, List<ObjectId> added, ObjectEventHandler onAppend, (bool Spur, bool Two, bool Crab) kind, bool startPline)
    {
        var db = doc.Database;
        CommandEventHandler? onEnd = null, onStart = null;
        var done = false;
        void Finish()
        {
            if (done) return;
            done = true;
            db.ObjectAppended -= onAppend;
            doc.CommandEnded -= onEnd;
            doc.CommandCancelled -= onEnd;
            doc.CommandFailed -= onEnd;
            doc.CommandWillStart -= onStart;
            TagNewPaths(doc, added, kind);
        }
        static bool IsPline(CommandEventArgs e) => e.GlobalCommandName.IndexOf("PLINE", StringComparison.OrdinalIgnoreCase) >= 0;
        onEnd = (s, e) => { if (IsPline(e)) Finish(); };
        // the PLINE end was missed (a nested PLINE may not report it): the next other command ends the watch, so it never collects that command's polylines
        onStart = (s, e) => { if (!IsPline(e)) Finish(); };
        doc.CommandEnded += onEnd;
        doc.CommandCancelled += onEnd;
        doc.CommandFailed += onEnd;
        doc.CommandWillStart += onStart;
        if (startPline) doc.SendStringToExecute("_.PLINE ", true, false, false);
    }

    private static void TagNewPaths(Document doc, List<ObjectId> added, (bool Spur, bool Two, bool Crab) kind)
    {
        var ed = doc.Editor;
        var label = PathKinds.Label(kind.Spur, kind.Two, kind.Crab);
        Run(doc, "AGVPATH", (d, tr) =>
        {
            var names = DrawingReader.Read(d, tr).Paths.Select(p => p.Name).ToList();
            var ms = ModelSpaceId(d, tr);
            int made = 0;
            foreach (var id in added.Where(i => !i.IsErased).ToList())
            {
                var pl = (Polyline)tr.GetObject(id, OpenMode.ForWrite);
                if (pl.OwnerId != ms) continue;
                var drawn = DrawingReader.ReadPath(pl, kind); // its straight lines and arcs, in the order drawn
                if (drawn == null) continue;
                // PLINE joins consecutive segments into one polyline; each line (or arc) becomes a path of its own
                var pieces = PathConvert.Pieces(drawn.Segments, kind, names);
                DrawingWriter.ReplacePaths(d, tr, new[] { pl.Handle.ToString() }, pieces);
                made += pieces.Count;
                ed.WriteMessage(pieces.Count == 1
                    ? $"\n경로 {pieces[0].Name} 추가 ({label})."
                    : $"\n경로 {pieces[0].Name} ~ {pieces[pieces.Count - 1].Name} {pieces.Count}개 추가 ({label}).");
            }
            return made > 0;
        });
    }

    // ---------- AGVCONVERT: lines/polylines drawn with ordinary tools -> one-way paths, then split at every crossing ----------

    [CommandMethod("AGVCONVERT", CommandFlags.UsePickSet)]
    public void AgvConvert() => Run(Doc, "AGVCONVERT", (db, tr) =>
    {
        var ids = ConvertTargets(Ed, db, tr);
        if (ids == null) return false;
        var names = DrawingReader.Read(db, tr).Paths.Select(p => p.Name).ToList();
        var skipped = new Dictionary<string, int>();
        var sources = new List<string>();
        var made = new List<LayoutPath>();
        var ms = ModelSpaceId(db, tr);
        foreach (var id in ids)
        {
            if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e) || e.IsErased) continue;
            var locked = ((LayerTableRecord)tr.GetObject(e.LayerId, OpenMode.ForRead)).IsLocked;
            // a layout's or a block's objects are in sheet or block-local coordinates: as paths they would land around 0,0
            var why = e.OwnerId != ms ? "모델 공간 밖(레이아웃·블록 편집기)" : PathConvert.SkipReason(ConvertType(e), e.Layer, locked, Conventions.PathLayers);
            // drawn order, XData ignored: every converted path is one-way from the start point to the end point
            var segs = why != null ? null : e is Polyline2d p2 ? DrawingReader.Segments(p2, tr) : DrawingReader.Segments(e);
            if (why == null && (segs == null || segs.Count == 0)) why = "길이 0";
            if (why != null) { skipped[why] = (skipped.TryGetValue(why, out var n) ? n : 0) + 1; continue; }
            made.AddRange(PathConvert.Pieces(segs!, (false, false, false), names));
            sources.Add(e.Handle.ToString());
        }
        Ed.WriteMessage("\n" + PathConvert.Summary(sources.Count, made.Count, skipped));
        if (made.Count == 0) { FinalStatus = "AGVCONVERT: 변환할 선·폴리선 없음"; return false; }
        DrawingWriter.ReplacePaths(db, tr, sources, made);
        var layout = DrawingReader.Read(db, tr); // every path, new and old, as AGVSPLIT with Enter
        SplitPaths(db, tr, Ed, layout.Paths, layout.Tolerance);
        Ed.WriteMessage("\n" + PathConvert.Guidance);
        return true;
    });

    internal static ObjectId ModelSpaceId(Database db, Transaction tr) => ((BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))[BlockTableRecord.ModelSpace];

    // Already selected, a selection, or everything on the layer of one picked object. Never the whole drawing: walls are lines too.
    private static ObjectId[]? ConvertTargets(Editor ed, Database db, Transaction tr)
    {
        var imp = ed.SelectImplied();
        if (imp.Status == PromptStatus.OK && imp.Value.Count > 0)
        {
            ed.SetImpliedSelection(new ObjectId[0]);
            return imp.Value.GetObjectIds();
        }
        var mode = Keyword(ed, "\n변환 대상 [선택(S)/레이어(L)] <선택>", "S", "S", "L");
        if (mode == null) return null;
        if (mode == "S")
        {
            // no type filter: splines and other objects are selected too, so the summary can say they were skipped
            var sel = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\n경로로 바꿀 선·폴리선 선택" });
            return sel.Status == PromptStatus.OK ? sel.Value.GetObjectIds() : null;
        }
        var pick = ed.GetEntity("\n변환할 레이어의 객체 하나 선택");
        if (pick.Status != PromptStatus.OK) return null;
        var layer = ((Entity)tr.GetObject(pick.ObjectId, OpenMode.ForRead)).Layer;
        var all = DrawingReader.ModelSpace(db, tr, OpenMode.ForRead)
            .Where(i => tr.GetObject(i, OpenMode.ForRead) is Entity e && DrawingReader.Eq(e.Layer, layer)).ToArray();
        ed.WriteMessage($"\n레이어 {layer}: 객체 {all.Length}개");
        return all;
    }

    private static string ConvertType(Entity e) => e switch
    {
        Line _ => "LINE",
        Arc _ => "ARC",
        Polyline _ => "LWPOLYLINE",
        Polyline2d p => p.PolyType == Poly2dType.SimplePoly ? "POLYLINE2D" : PathConvert.FittedPolyline2d,
        Polyline3d _ => "3D POLYLINE",
        _ => e.GetRXClass().DxfName,
    };

    // ---------- path type ----------

    private const string KindPrompt = "\n경로 종류 [단방향(O)/양방향(T)/지선(S)/크랩(C)] <단방향>";

    /// <summary>O one-way, T two-way, S spur, C crab (the four AutoMod path types).</summary>
    private static (bool Spur, bool Two, bool Crab) KindOf(string key) => key switch
    {
        "T" => (false, true, false),
        "S" => (true, false, false),
        "C" => (false, false, true),
        _ => (false, false, false),
    };

    [CommandMethod("AGVPTYPE", CommandFlags.UsePickSet)]
    public void AgvPType() => Run(Doc, "AGVPTYPE", (db, tr) =>
    {
        var ids = SelectPaths(Ed, tr, "\n종류를 바꿀 경로 선택");
        if (ids.Count == 0) return false;
        var key = Keyword(Ed, KindPrompt, "O", "O", "T", "S", "C");
        if (key == null) return false;
        var kind = KindOf(key);
        foreach (var id in ids)
        {
            var e = (Entity)tr.GetObject(id, OpenMode.ForWrite);
            var x = XData.ReadPath(e);
            e.Layer = Conventions.LayerFor(kind.Spur, kind.Two, kind.Crab);
            XData.WritePath(e, x?.Name ?? "?" + e.Handle, x?.Reversed ?? false, kind, tr);
        }
        Ed.WriteMessage($"\n경로 {ids.Count}개를 {PathKinds.Label(kind.Spur, kind.Two, kind.Crab)}(으)로 변경.");
        return true;
    });

    // ---------- edits ----------

    [CommandMethod("AGVREVERSE", CommandFlags.UsePickSet)]
    public void AgvReverse() => Run(Doc, "AGVREVERSE", (db, tr) =>
    {
        var ids = SelectPaths(Ed, tr, "\n방향을 뒤집을 경로 선택");
        foreach (var id in ids)
        {
            var e = (Entity)tr.GetObject(id, OpenMode.ForWrite);
            var x = XData.ReadPath(e);
            var kind = x is { } t ? (t.IsSpur, t.IsTwoWay, t.IsCrab) : Conventions.KindOfLayer(e.Layer);
            XData.WritePath(e, x?.Name ?? "?" + e.Handle, !(x?.Reversed ?? false), kind, tr);
        }
        Ed.WriteMessage($"\n경로 {ids.Count}개 방향 반전.");
        return ids.Count > 0;
    });

    // Three questions: base point, displacement (pick a point or type a value), reverse the new path's direction.
    // The side is the side of the picked point (typed value: the side of the base point) relative to the path's direction of travel.
    [CommandMethod("AGVOFFSET", CommandFlags.UsePickSet)]
    public void AgvOffset() => Run(Doc, "AGVOFFSET", (db, tr) =>
    {
        var layout = DrawingReader.Read(db, tr);
        var picked = Preselected(Ed, tr).Select(i => i.Handle.ToString()).ToHashSet();
        var paths = layout.Paths.Where(q => picked.Contains(q.Id)).ToList();
        if (paths.Count == 0)
        {
            var one = PickPath(Ed, layout, "\n복제할 경로 선택");
            if (one == null) return false;
            paths.Add(one);
        }
        var bp = Ed.GetPoint("\n기준점");
        if (bp.Status != PromptStatus.OK) return false;
        var dr = Ed.GetPoint(new PromptPointOptions("\n변위 (점을 찍거나 값을 입력)") { UseBasePoint = true, BasePoint = bp.Value, AllowArbitraryInput = true, AllowNone = false });
        double dist;
        var sidePoint = new Point2(bp.Value.X, bp.Value.Y);
        if (dr.Status == PromptStatus.OK)
        {
            dist = bp.Value.DistanceTo(dr.Value);
            sidePoint = new Point2(dr.Value.X, dr.Value.Y);
        }
        else if (dr.Status == PromptStatus.Keyword || dr.Status == PromptStatus.Other)
        {
            if (!double.TryParse(dr.StringResult, NumberStyles.Float, CultureInfo.InvariantCulture, out dist)) { Ed.WriteMessage("\n변위는 점을 찍거나 숫자를 입력하세요."); return false; }
        }
        else return false;
        if (!(dist > 0)) { Ed.WriteMessage("\n변위는 0보다 커야 합니다."); return false; }
        var rev = Keyword(Ed, "\n새 경로의 진행 방향을 뒤집을까요? [예(Y)/아니오(N)] <예>", "Y", "Y", "N");
        if (rev == null) return false;

        var taken = layout.Paths.Select(q => q.Name).ToList();
        var made = new List<LayoutPath>();
        foreach (var src in paths)
        {
            int side = PathOps.SideOf(src, sidePoint, layout.Tolerance);
            if (side == 0) side = 1; // base point on the path and a typed value: left of travel
            LayoutPath o;
            try { o = PathOps.Offset(src, side * dist, rev == "Y"); }
            catch (ArgumentException ex) { Ed.WriteMessage($"\n{src.Name}: {ex.Message}"); continue; }
            o.Name = AutoNamer.Next("P", taken);
            taken.Add(o.Name);
            made.Add(o);
            Ed.WriteMessage($"\n경로 {o.Name} 추가 ({src.Name}의 {(side > 0 ? "왼쪽" : "오른쪽")} {dist:0.###}).");
        }
        if (made.Count == 0) return false;
        DrawingWriter.ReplacePaths(db, tr, new string[0], made);
        return true;
    });

    [CommandMethod("AGVARRAY", CommandFlags.UsePickSet)]
    public void AgvArray() => Run(Doc, "AGVARRAY", (db, tr) =>
    {
        ObjectId[] chosen;
        var imp = Ed.SelectImplied(); // already selected: use it right away
        if (imp.Status == PromptStatus.OK && imp.Value.Count > 0)
        {
            chosen = imp.Value.GetObjectIds();
            Ed.SetImpliedSelection(new ObjectId[0]);
        }
        else
        {
            var sel = Ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\n배열 복제할 경로·CP·block 선택" });
            if (sel.Status != PromptStatus.OK) return false;
            chosen = sel.Value.GetObjectIds();
        }
        var picked = new HashSet<string>(chosen.Select(i => i.Handle.ToString()));
        var b = Ed.GetPoint("\n간격 기준점");
        if (b.Status != PromptStatus.OK) return false;
        var t = Ed.GetPoint(new PromptPointOptions("\n간격 끝점") { UseBasePoint = true, BasePoint = b.Value });
        if (t.Status != PromptStatus.OK) return false;
        var n = Ed.GetInteger(new PromptIntegerOptions("\n복제 개수") { LowerLimit = 1, DefaultValue = 1, UseDefaultValue = true });
        if (n.Status != PromptStatus.OK) return false;
        double dx = t.Value.X - b.Value.X, dy = t.Value.Y - b.Value.Y;

        var layout = DrawingReader.Read(db, tr);
        var pathNames = layout.Paths.Select(p => p.Name).ToList();
        var newPaths = new List<LayoutPath>();
        foreach (var p in layout.Paths.Where(p => picked.Contains(p.Id)))
            foreach (var c in PathOps.Array(p, dx, dy, n.Value))
            {
                if (pathNames.Contains(c.Name)) c.Name = AutoNamer.Next("P", pathNames);
                pathNames.Add(c.Name);
                newPaths.Add(c);
            }
        DrawingWriter.ReplacePaths(db, tr, new string[0], newPaths);

        var cpNames = layout.Cps.Select(c => c.Name).ToList();
        int cps = 0;
        foreach (var cp in layout.Cps.Where(c => picked.Contains(c.Id)))
            for (int k = 1; k <= n.Value; k++)
            {
                var name = AutoNamer.Next(Prefix(cp.Name, AutoNamer.PrefixFor(cp.Type)), cpNames);
                cpNames.Add(name);
                DrawingWriter.AddCp(db, tr, new LayoutCp
                {
                    Name = name, Type = cp.Type, Group = cp.Group, Cap = cp.Cap,
                    Position = new Point2(cp.Position.X + dx * k, cp.Position.Y + dy * k),
                });
                cps++;
            }

        var blockNames = layout.Blocks.Select(x => x.Name).ToList();
        int blocks = 0;
        foreach (var bl in layout.Blocks.Where(x => picked.Contains(x.Id)))
            for (int k = 1; k <= n.Value; k++)
            {
                var name = AutoNamer.Next(Prefix(bl.Name, "B"), blockNames);
                blockNames.Add(name);
                double vx = dx * k, vy = dy * k;
                DrawingWriter.AddBlock(db, tr, new LayoutBlock
                {
                    Name = name, Cap = bl.Cap,
                    Rect = new Rect2(new Point2(bl.Rect.Min.X + vx, bl.Rect.Min.Y + vy), new Point2(bl.Rect.Max.X + vx, bl.Rect.Max.Y + vy)),
                });
                blocks++;
            }
        Ed.WriteMessage($"\n배열 복제: 경로 {newPaths.Count}개, CP {cps}개, block {blocks}개.");
        return newPaths.Count + cps + blocks > 0;
    });

    [CommandMethod("AGVSPLIT", CommandFlags.UsePickSet)]
    public void AgvSplit() => Run(Doc, "AGVSPLIT", (db, tr) =>
    {
        var layout = DrawingReader.Read(db, tr);
        var pre = Preselected(Ed, tr).Select(i => i.Handle.ToString()).ToHashSet();
        if (pre.Count > 0) // selected paths: split them at every crossing right away
            return SplitPaths(db, tr, Ed, layout.Paths.Where(q => pre.Contains(q.Id)).ToList(), layout.Tolerance);
        var mode = Keyword(Ed, "\n분할 대상 [선택(S)/점(P)] <전체>", "", "S", "P");
        if (mode == null) return false;
        if (mode == "P")
        {
            var pr = Ed.GetPoint("\n분할할 점");
            if (pr.Status != PromptStatus.OK) return false;
            var hit = LayoutChecker.Project(layout, new Point2(pr.Value.X, pr.Value.Y));
            if (hit == null) { Ed.WriteMessage("\n경로가 없습니다."); return false; }
            if (hit.Value.Offset > 10 * layout.Tolerance) { Ed.WriteMessage($"\n찍은 점이 가장 가까운 경로 {hit.Value.Path.Name}에서 {hit.Value.Offset:0.###} 떨어져 있습니다. 경로 위의 점을 찍으세요."); return false; }
            var pieces = PathSplitter.SplitAt(hit.Value.Path, new[] { hit.Value.Dist }, layout.Tolerance);
            if (pieces.Count < 2) { Ed.WriteMessage("\n경로 끝점이라 분할하지 않았습니다."); return false; }
            var taken = new HashSet<string>(layout.Paths.Select(q => q.Name));
            int k = 1;
            foreach (var piece in pieces)
            {
                while (!taken.Add(hit.Value.Path.Name + "_" + k)) k++; // same suffix skipping as PathSplitter.SplitAll
                piece.Name = hit.Value.Path.Name + "_" + k++;
            }
            DrawingWriter.ReplacePaths(db, tr, new[] { hit.Value.Path.Id }, pieces);
            Ed.WriteMessage($"\n경로 {hit.Value.Path.Name} → {string.Join(", ", pieces.Select(q => q.Name))}");
            return true;
        }
        var paths = layout.Paths;
        if (mode == "S")
        {
            var ids = new HashSet<string>(SelectPaths(Ed, tr, "\n분할할 경로 선택").Select(i => i.Handle.ToString()));
            paths = paths.Where(q => ids.Contains(q.Id)).ToList();
        }
        return SplitPaths(db, tr, Ed, paths, layout.Tolerance);
    });

    /// <summary>Splits the given paths at every intersection and rewrites the drawing. True when anything changed.</summary>
    internal static bool SplitPaths(Database db, Transaction tr, Editor ed, IReadOnlyList<LayoutPath> paths, double tol)
    {
        var r = PathSplitter.SplitAll(paths, tol);
        var split = r.Paths.Where(p => p.Id.Contains("#")).ToList(); // pieces carry "<original id>#k"
        var removed = split.Select(p => p.Id.Substring(0, p.Id.IndexOf('#'))).Distinct().ToList();
        DrawingWriter.ReplacePaths(db, tr, removed, split);
        ed.WriteMessage($"\n교차 {r.IntersectionCount}개, 경로 {paths.Count}개 → {r.Paths.Count}개");
        return removed.Count > 0;
    }

    [CommandMethod("AGVJOIN", CommandFlags.UsePickSet)]
    public void AgvJoin() => Run(Doc, "AGVJOIN", (db, tr) =>
    {
        var layout = DrawingReader.Read(db, tr);
        var pre = Preselected(Ed, tr).Select(i => i.Handle.ToString()).ToHashSet();
        var sel = layout.Paths.Where(q => pre.Contains(q.Id)).ToList();
        LayoutPath? a, b;
        if (sel.Count == 2) (a, b) = InOrder(sel[0], sel[1], layout.Tolerance); // two selected: the order is found from where they touch
        else
        {
            a = PickPath(Ed, layout, "\n앞 경로 선택");
            b = a == null ? null : PickPath(Ed, layout, "\n뒤 경로 선택");
        }
        if (a == null || b == null || a == b) return false;
        var j = PathOps.Join(a, b, layout.Tolerance);
        if (j == null) { Ed.WriteMessage("\n앞 경로의 끝점과 뒤 경로의 시작점이 맞닿지 않습니다."); return false; }
        DrawingWriter.ReplacePaths(db, tr, new[] { a.Id, b.Id }, new[] { j });
        Ed.WriteMessage($"\n경로 {a.Name} + {b.Name} → {j.Name}");
        return true;
    });

    /// <summary>x then y when x ends where y starts, y then x when the other way round, else as given.</summary>
    private static (LayoutPath, LayoutPath) InOrder(LayoutPath x, LayoutPath y, double tol) =>
        x.End.DistanceTo(y.Start) <= tol ? (x, y) : y.End.DistanceTo(x.Start) <= tol ? (y, x) : (x, y);

    private const double DefaultFilletM = 1.2; // offered when no radius was ever entered: 1200 in a millimetre drawing

    // The last radius is kept per PC in ash.user.tsv, in meters so it means the same in drawings with other units.
    private static double SavedFillet()
    {
        try { var m = UserSettings.Load(UserSettings.DefaultPath).FilletRadiusM; return (m > 0 ? m : DefaultFilletM) * DrawingUnits.PerMeter; }
        catch (IOException) { return DefaultFilletM * DrawingUnits.PerMeter; }
        catch (UnauthorizedAccessException) { return DefaultFilletM * DrawingUnits.PerMeter; }
    }

    private static void SaveFillet(double radius)
    {
        try { var path = UserSettings.DefaultPath; (UserSettings.Load(path) with { FilletRadiusM = radius / DrawingUnits.PerMeter }).Save(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // What is rounded depends on what is selected: one path = every corner inside it, two paths = the corner where they meet.
    // Nothing selected: click a path near the corner (a one-segment path then asks for the path that continues it).
    [CommandMethod("AGVFILLET", CommandFlags.UsePickSet)]
    public void AgvFillet() => Run(Doc, "AGVFILLET", (db, tr) =>
    {
        var layout = DrawingReader.Read(db, tr);
        var pre = Preselected(Ed, tr).Select(i => i.Handle.ToString()).ToHashSet();
        var sel = layout.Paths.Where(q => pre.Contains(q.Id)).ToList();
        LayoutPath? a = null, b = null;
        Point2? near = null;
        if (sel.Count == 1) a = sel[0];
        else if (sel.Count == 2) (a, b) = InOrder(sel[0], sel[1], layout.Tolerance);
        else if (sel.Count == 0)
        {
            var pr = Ed.GetEntity("\n필렛할 경로 선택 (모서리 가까이를 클릭)");
            if (pr.Status != PromptStatus.OK) return false;
            a = layout.Paths.FirstOrDefault(q => q.Id == pr.ObjectId.Handle.ToString());
            if (a == null) { Ed.WriteMessage("\nAGV_PATH 계열 레이어의 경로가 아닙니다."); return false; }
            near = new Point2(pr.PickedPoint.X, pr.PickedPoint.Y);
            if (a.Segments.Count < 2)
            {
                b = PickPath(Ed, layout, "\n이어지는 경로 선택");
                if (b == null) return false;
                (a, b) = InOrder(a, b, layout.Tolerance);
                near = null;
            }
        }
        else { Ed.WriteMessage("\n경로를 1개(안의 모든 모서리) 또는 2개(맞닿은 두 경로)만 선택하세요."); return false; }

        double def = SavedFillet();
        var rr = Ed.GetDistance(new PromptDistanceOptions("\n반경") { AllowNegative = false, AllowZero = false, DefaultValue = def, UseDefaultValue = true });
        if (rr.Status != PromptStatus.OK) return false;
        SaveFillet(rr.Value);

        if (b == null) // corners inside one path (a polyline drawn with AGVPATH)
        {
            var r = PathOps.FilletCorners(a!, rr.Value, layout.Tolerance, near);
            if (r == null) { Ed.WriteMessage("\n둥글릴 모서리가 없습니다 (직선 두 개가 꺾여 만나는 곳이어야 합니다)."); return false; }
            var (path, rounded, skipped) = r.Value;
            if (rounded == 0) { Ed.WriteMessage($"\n반경 {rr.Value:0.###}에 비해 모서리 앞뒤 직선이 짧아 필렛할 수 없습니다: {string.Join(", ", skipped)}"); return false; }
            DrawingWriter.ReplacePaths(db, tr, new[] { a!.Id }, new[] { path });
            Ed.WriteMessage($"\n경로 {a.Name}: 작업 완료 (모서리 {rounded}개)" + (skipped.Count > 0 ? $", 반경에 비해 짧아 건너뜀 {skipped.Count}개: {string.Join(", ", skipped)}." : "."));
            return true;
        }

        var f = PathOps.Fillet(a!, b, rr.Value, layout.Tolerance);
        if (f == null) { Ed.WriteMessage("\n필렛 불가: 두 경로 모두 직선 하나짜리, 앞 끝점 = 뒤 시작점, 평행 아님, 반경에 비해 충분한 길이여야 합니다."); return false; }
        var names = new HashSet<string>(layout.Paths.Select(q => q.Name));
        string baseName = f.Value.Fillet.Name;
        for (int k = 1; names.Contains(f.Value.Fillet.Name); k++) f.Value.Fillet.Name = baseName + "_" + k;
        DrawingWriter.ReplacePaths(db, tr, new[] { a!.Id, b.Id }, new[] { f.Value.A, f.Value.Fillet, f.Value.B });
        Ed.WriteMessage($"\n작업 완료 (원호 경로 {f.Value.Fillet.Name}).");
        return true;
    });

    // ---------- arrows ----------

    // Saves the arrow length (meters) in ash.settings.tsv and redraws every arrow now. Enter keeps the length and just redraws (after editing the file by hand).
    [CommandMethod("AGVARROW")]
    public void AgvArrow()
    {
        var doc = Doc;
        if (!ExportCommands.TryLoadSettings(doc.Editor, out var s, out var dir)) return;
        var r = doc.Editor.GetDouble(new PromptDoubleOptions("\n방향표 길이 (m)") { DefaultValue = s.ArrowSize, UseDefaultValue = true, AllowNegative = false, AllowZero = false });
        if (r.Status != PromptStatus.OK) return;
        if (Math.Abs(r.Value - s.ArrowSize) > 1e-12) SettingsFile.Save(s with { ArrowSize = r.Value }, Path.Combine(dir, ExportCommands.SettingsFileName));
        // Run reads the length back from the file, so the redraw below uses the new value
        Run(doc, "AGVARROW", (db, tr) => true, unitsPerMeter: UnitScale.FromSettings(s));
    }

    // ---------- helpers ----------

    private static Document Doc => Application.DocumentManager.MdiActiveDocument;
    private static Editor Ed => Doc.Editor;

    /// <summary>Runs body in one locked transaction; when it returns true, refreshes arrows (unless refreshArrows is false) and commits. Errors go to the command line.
    /// unitsPerMeter comes from settings the caller already loaded; null loads (or creates) ash.settings.tsv via ExportCommands.UnitsPerMeter.</summary>
    /// <summary>A body may set this for its final status line instead of "변경 없음" (returns false) or "완료" (returns true).</summary>
    internal static string? FinalStatus;

    internal static void Run(Document doc, string cmd, Func<Database, Transaction, bool> body, bool refreshArrows = true, double? unitsPerMeter = null)
    {
        DrawingUnits.PerMeter = unitsPerMeter ?? ExportCommands.UnitsPerMeter(doc.Editor);
        DrawingUnits.ArrowMeters = ExportCommands.ArrowMeters();
        try
        {
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                InitCommands.Init(doc.Database, tr); // layers/blocks must exist before any Layer set or block insert
                FinalStatus = null;
                var changed = body(doc.Database, tr);
                RefreshCpGroups(doc.Database, tr);
                if (!changed) { Ui.PaletteStatus.Set(FinalStatus ?? $"{cmd}: 변경 없음"); FinalStatus = null; return; }
                if (refreshArrows) DrawingWriter.RefreshDirectionArrows(doc.Database, tr);
                tr.Commit();
                Ui.PaletteStatus.Set(FinalStatus ?? $"{cmd} 완료");
                FinalStatus = null;
            }
        }
        catch (OperationCanceledException)
        {
            // Esc in a nested prompt: abort without committing
            Ui.PaletteStatus.Set($"{cmd} 취소");
        }
        catch (System.Exception ex)
        {
            doc.Editor.WriteMessage($"\n{cmd} 오류: {ex.GetType().Name}: {ex.Message}");
            PluginLog.Write(cmd + " failed", ex);
            Ui.PaletteStatus.Set($"{cmd} 오류: {ex.Message}");
        }
        finally
        {
            DrawingUnits.PerMeter = 1.0;
        }
    }

    /// <summary>Hands the palette's "그룹 보기" list the CP groups as the command left them (also feeds the status line's hidden-group count). Never fails the command.</summary>
    private static void RefreshCpGroups(Database db, Transaction tr)
    {
        if (!Ui.PaletteStatus.WantsGroups) return;
        try { Ui.PaletteStatus.SetGroups(CpViewer.Scan(db, tr)); }
        catch (System.Exception ex) { PluginLog.Write("CP group list", ex); }
    }

    /// <summary>Paths that were already selected when the command started (PICKFIRST). The selection is cleared so it does not linger.</summary>
    private static List<ObjectId> Preselected(Editor ed, Transaction tr)
    {
        var res = new List<ObjectId>();
        var imp = ed.SelectImplied();
        if (imp.Status != PromptStatus.OK || imp.Value.Count == 0) return res;
        foreach (var id in imp.Value.GetObjectIds())
            if (tr.GetObject(id, OpenMode.ForRead) is Entity e && Conventions.PathLayers.Any(l => DrawingReader.Eq(e.Layer, l))) res.Add(id);
        if (res.Count > 0) ed.SetImpliedSelection(new ObjectId[0]);
        return res;
    }

    /// <summary>The selected paths if there are any, otherwise a prompt.</summary>
    private static List<ObjectId> SelectPaths(Editor ed, Transaction tr, string msg)
    {
        var pre = Preselected(ed, tr);
        if (pre.Count > 0) return pre;
        var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.LayerName, string.Join(",", Conventions.PathLayers)) });
        var r = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = msg }, filter);
        return r.Status == PromptStatus.OK ? r.Value.GetObjectIds().ToList() : new List<ObjectId>();
    }

    private static LayoutPath? PickPath(Editor ed, CoreLayout layout, string msg)
    {
        var r = ed.GetEntity(new PromptEntityOptions(msg));
        if (r.Status != PromptStatus.OK) return null;
        var p = layout.Paths.FirstOrDefault(q => q.Id == r.ObjectId.Handle.ToString());
        if (p == null) ed.WriteMessage("\nAGV_PATH 계열 레이어(AGV_PATH, _TWO, _SPUR, _CRAB)의 경로가 아닙니다.");
        return p;
    }

    /// <summary>Keyword prompt; Enter returns def. Null on cancel.</summary>
    internal static string? Keyword(Editor ed, string msg, string def, params string[] keys)
    {
        var o = new PromptKeywordOptions(msg) { AllowNone = true, AppendKeywordsToMessage = false };
        foreach (var k in keys) o.Keywords.Add(k);
        var r = ed.GetKeywords(o);
        if (r.Status == PromptStatus.None) return def;
        return r.Status == PromptStatus.OK ? (string.IsNullOrEmpty(r.StringResult) ? def : r.StringResult) : null;
    }

    // "T012" -> "T"; empty name -> fallback.
    private static string Prefix(string name, string fallback)
    {
        var p = name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        return p.Length > 0 ? p : fallback;
    }
}
