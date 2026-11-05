using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ASH.Core.Cel;
using ASH.Core.Lite;
using ASH.Plugin.Ui;
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Runtime;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ASH.Plugin.Commands.LiteCommands))]

namespace ASH.Plugin.Commands;

/// <summary>AGVLITE (spec §5): a light copy of the open factory drawing to draw AGV paths on and to use as AutoMod's background. No dialog: it reads every block definition once
/// (read-only: the open drawing is never changed), Core picks the cleanup level off ZWCAD's thread and writes lite\&lt;name&gt;_lite.dxf (R12, ASH's own writer) with its report and settings.</summary>
public sealed class LiteCommands
{
    [CommandMethod("AGVLITE")]
    public void AgvLite()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        try { Run(doc); }
        catch (System.Exception ex)
        {
            doc.Editor.WriteMessage($"\nAGVLITE 오류: {ex.GetType().Name}: {ex.Message}");
            PluginLog.Write("AGVLITE failed", ex);
            PaletteStatus.Set("AGVLITE 오류: " + ex.Message);
        }
    }

    private static void Run(Document doc)
    {
        var clock = Stopwatch.StartNew();
        var ed = doc.Editor;
        void Say(string t) { ed.WriteMessage("\n" + t); PaletteStatus.Set(t); }
        if (Convert.ToInt32(Application.GetSystemVariable("DWGTITLED")) == 0) { Say("AGVLITE: 도면을 먼저 저장하세요. 경량 도면은 도면 폴더의 lite\\ 에 만듭니다."); return; }
        var dir = (string)Application.GetSystemVariable("DWGPREFIX");
        var dwg = Path.Combine(dir, (string)Application.GetSystemVariable("DWGNAME"));
        var name = Path.GetFileNameWithoutExtension(dwg);
        var outDir = Path.Combine(dir, "lite");
        var dxfPath = Path.Combine(outDir, name + "_lite.dxf");
        var reportPath = Path.Combine(outDir, name + "_lite.report.tsv");
        var settingsPath = Path.Combine(outDir, name + "_lite.settings.tsv");
        int insUnits = Convert.ToInt32(Application.GetSystemVariable("INSUNITS"));

        LiteSettings settings;
        bool saveSettings = true;
        try { settings = LiteSettings.Load(settingsPath); }
        catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            settings = new LiteSettings();
            saveSettings = false; // what AGVLITEX fixed in that file must outlive this run
            Say($"AGVLITE: 설정 파일 읽기 실패, 자동 단계 사용: {ex.Message}");
        }

        Say("AGVLITE: 도면 읽는 중… (원본은 바꾸지 않습니다)");
        var log = new LiteReadLog();
        var blocks = Read(doc, insUnits, log, Say);
        if (blocks == null) { Say("AGVLITE 취소"); return; }
        if (blocks.Defs.All(d => d.Geo.Count == 0)) { Say("AGVLITE: 모델 공간에 남길 선·곡선이 없습니다."); return; }

        var files = new LiteFiles(dwg, File.Exists(dwg) ? new FileInfo(dwg).Length : 0, dxfPath, reportPath, settingsPath);
        var build = new LiteBuild(blocks, settings);
        LiteOutcome o;
        using (var esc = new CancellationTokenSource())
        {
            int level = -1, shown = -1;
            var work = Task.Run(() => LiteJob.Run(build, log, files, saveSettings, clock, esc.Token, k => Volatile.Write(ref level, k)));
            while (!((IAsyncResult)work).AsyncWaitHandle.WaitOne(100))
            {
                int k = Volatile.Read(ref level);
                if (k != shown) Say($"AGVLITE: 정리 중 {shown = k}단계…");
                if (HostApplicationServices.Current.UserBreak()) esc.Cancel();
                // a minutes-long wait: without a pump the window goes "not responding" and these lines never paint. After UserBreak, so the pump cannot eat the Esc.
                System.Windows.Forms.Application.DoEvents();
            }
            o = work.GetAwaiter().GetResult();
        }
        if (o.Cancelled) { Say("AGVLITE 취소"); return; }
        if (o.Failed != null) { Say($"AGVLITE: {o.Failed}에 쓰지 못했습니다(다른 프로그램이 열고 있나요?): {o.Why}"); return; }

        var r = o.Result!;
        var s = o.Stats!;
        double mb = o.Bytes / 1048576.0;
        ed.WriteMessage($"\nAGVLITE: {r.Level}단계, 엔티티 {r.Entities:N0}개, {mb:0.00} MB, 덩어리 {s.Blobs:N0}개 외곽선, 중복 {s.Duplicate.Count + r.Deduped:N0}개·작은 요소 {s.Small.Count + s.SmallInstances:N0}개 제거 (원본 DWG {files.SourceBytes / 1048576.0:0.00} MB)");
        ed.WriteMessage($"\n원점 이동 ({r.ShiftX:0.###}, {r.ShiftY:0.###}): 원본 좌표로 되돌리려면 ash.settings.tsv에 OriginX {-r.ShiftX:0.###}, OriginY {-r.ShiftY:0.###} (보고서 참고)");
        ed.WriteMessage($"\nDXF: {dxfPath}\n보고서: {reportPath}");
        if (o.Bytes > LiteLevels.WarnBytes) ed.WriteMessage("\n경고: 10 MB를 넘습니다. AutoMod가 느려지거나 멈출 수 있습니다(보고서의 큰 덩어리·큰 레이어 참고).");
        if (log.ExplodeFailed > 0) ed.WriteMessage($"\n참고: 폭발 실패 {log.ExplodeFailed:N0}개, 범위 상자로 대체.");
        if (log.Failures > 0) ed.WriteMessage($"\n참고: 읽지 못한 엔티티 {log.Failures:N0}개를 건너뛰었습니다.");
        PaletteStatus.Set($"AGVLITE 완료: {r.Level}단계, {mb:0.00} MB" + (o.Bytes > LiteLevels.WarnBytes ? " (10 MB 초과)" : ""));

        if (PathCommands.Keyword(ed, "\n경량 도면을 ZWCAD에서 열까요? [Y/N] <Y>", "Y", "Y", "N") != "Y") return;
        try
        {
            // opening from a document command is not allowed: run it once the command has returned to the application
            Application.DocumentManager.ExecuteInApplicationContext(_ =>
            {
                try { Application.DocumentManager.Open(dxfPath, false); }
                catch (System.Exception ex) { PluginLog.Write("AGVLITE open", ex); }
            }, null);
        }
        catch (System.Exception ex)
        {
            PluginLog.Write("AGVLITE open", ex);
            ed.WriteMessage("\n자동으로 열지 못했습니다. 직접 여세요: " + dxfPath);
        }
    }

    /// <summary>Every block definition model space reaches, in block-local 3D; null when Esc stopped it. The document lock ends here: Core never needs the drawing again.</summary>
    internal static LiteBlocks? Read(Document doc, int insUnits, LiteReadLog log, Action<string> say)
    {
        var blocks = new LiteBlocks { InsUnits = insUnits };
        using (doc.LockDocument())
        using (var tr = doc.Database.TransactionManager.StartTransaction())
        {
            bool done = new Reader(tr, blocks, log, LiteRead.Tolerance(insUnits), say).Run(doc.Database);
            tr.Commit(); // read-only; commit is just cheaper than abort
            return done ? blocks : null;
        }
    }

    /// <summary>Reads each definition once, in the order inserts name them: a definition only lists its inserts, so reading never recurses and a block that contains itself is left to Core (R9).</summary>
    private sealed class Reader
    {
        /// <summary>Progress and Esc per this many definitions (plan Step 4)...</summary>
        private const int DefsPerReport = 100;
        /// <summary>...and Esc per this many entities: model space alone can hold millions.</summary>
        private const int EntitiesPerBreak = 4096;
        /// <summary>Solid to Region or Surface to curves is three: more means an object that explodes into itself.</summary>
        private const int MaxExplode = 8;
        private static readonly Vec3 Up = new Vec3(0, 0, 1);

        private readonly Transaction _tr;
        private readonly LiteBlocks _blocks;
        private readonly LiteReadLog _log;
        private readonly double _tol;
        private readonly Action<string> _say;
        private readonly Dictionary<ObjectId, int> _defs = new Dictionary<ObjectId, int>();
        private readonly Queue<ObjectId> _todo = new Queue<ObjectId>();
        private int _at, _seen, _depth; // definition being read, entities looked at, explosions under way

        public Reader(Transaction tr, LiteBlocks blocks, LiteReadLog log, double tol, Action<string> say) { _tr = tr; _blocks = blocks; _log = log; _tol = tol; _say = say; }

        private static bool Esc() => HostApplicationServices.Current.UserBreak();

        public bool Run(Database db)
        {
            var lt = (LayerTable)_tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId id in lt)
                if (_tr.GetObject(id, OpenMode.ForRead) is LayerTableRecord l && !l.IsErased)
                    _blocks.Layers[l.Name] = new LiteLayerState(l.Color.IsByAci ? l.Color.ColorIndex : (short)7, l.IsOff, l.IsFrozen);
            var ms = ((BlockTable)_tr.GetObject(db.BlockTableId, OpenMode.ForRead))[BlockTableRecord.ModelSpace];
            _blocks.Root = Def((BlockTableRecord)_tr.GetObject(ms, OpenMode.ForRead));

            long lines = 0;
            for (_at = 0; _todo.Count > 0; _at++) // the queue holds definitions in Defs order
            {
                var g = _blocks.Defs[_at];
                if (!Read((BlockTableRecord)_tr.GetObject(_todo.Dequeue(), OpenMode.ForRead), g)) return false;
                lines += g.Geo.Count;
                if ((_at + 1) % DefsPerReport != 0) continue;
                _say($"AGVLITE: 읽는 중 {_at + 1:N0}/{_blocks.Defs.Count:N0} 정의, 선 {lines:N0}개");
                if (Esc()) return false;
            }
            return true;
        }

        private int Def(BlockTableRecord b)
        {
            if (_defs.TryGetValue(b.ObjectId, out int d)) return d;
            _defs[b.ObjectId] = d = _blocks.Defs.Count;
            _blocks.Defs.Add(new BlockDef());
            _log.Names.Add(b.Name);
            _log.Solids.Add(0);
            _todo.Enqueue(b.ObjectId);
            return d;
        }

        private bool Read(BlockTableRecord btr, BlockDef g)
        {
            foreach (ObjectId id in btr)
            {
                if (++_seen % EntitiesPerBreak == 0 && Esc()) return false;
                try
                {
                    if (!(_tr.GetObject(id, OpenMode.ForRead, false) is Entity e) || e.IsErased) continue;
                    if (_at == _blocks.Root) LiteReadLog.Count(_log.Before, e.GetType().Name);
                    if (!e.Visible) { LiteReadLog.Count(_log.Removed, "숨긴 엔티티"); continue; }
                    Add(g, e);
                }
                catch (System.Exception) { _log.Failures++; } // an odd entity must never stop the command
            }
            return true;
        }

        private void Add(BlockDef g, Entity e)
        {
            string layer = e.Layer;
            void Drop(string kind) => LiteReadLog.Count(_log.Removed, kind);
            void Sample(Curve c, int initial)
            {
                var pts = CurveApprox.Adaptive(t => V(c.GetPointAtParameter(t)), c.StartParam, c.EndParam, _tol, initial);
                if (c.Closed && pts.Count > 2) pts.RemoveAt(pts.Count - 1); // the end parameter is the start point again
                if (pts.Count >= 2) g.Geo.Add(new Points3(layer, pts, c.Closed));
            }

            switch (e)
            {
                case Table _: Drop("표"); break;
                case MInsertBlock mi:
                {
                    if (Target(mi) is not int def) break;
                    var xf = Affine3.From(mi.BlockTransform.ToArray());
                    for (int r = 0; r < mi.Rows; r++)
                        for (int c = 0; c < mi.Columns; c++)
                            g.Uses.Add(new BlockUse(def, LiteRead.Minsert(xf, V(mi.Normal), mi.Rotation, c * mi.ColumnSpacing, r * mi.RowSpacing), layer));
                    break;
                }
                case BlockReference br:
                    if (Target(br) is int d) g.Uses.Add(new BlockUse(d, Affine3.From(br.BlockTransform.ToArray()), layer));
                    break;
                case Line l: g.Geo.Add(new Line3(layer, V(l.StartPoint), V(l.EndPoint))); break;
                case Polyline pl:
                {
                    if (IsRevCloud(pl)) { Drop("구름형 리비전"); break; }
                    var v = new LiteVertex[pl.NumberOfVertices];
                    for (int i = 0; i < v.Length; i++) { var p = pl.GetPoint2dAt(i); v[i] = new LiteVertex(p.X, p.Y, pl.GetBulgeAt(i)); } // OCS, as Poly3 wants
                    if (v.Length >= 2) g.Geo.Add(new Poly3(layer, v, pl.Closed, V(pl.Normal), pl.Elevation));
                    break;
                }
                case Polyline2d p2:
                {
                    if (p2.PolyType != Poly2dType.SimplePoly) { Sample(p2, 64); break; } // fit / spline-fit curves: sample the curve ZWCAD shows
                    var v = new List<LiteVertex>();
                    foreach (ObjectId vid in p2)
                        if (_tr.GetObject(vid, OpenMode.ForRead) is Vertex2d vx) v.Add(new LiteVertex(vx.Position.X, vx.Position.Y, vx.Bulge)); // OCS: not p2.VertexPosition, that is WCS
                    if (v.Count >= 2) g.Geo.Add(new Poly3(layer, v, p2.Closed, V(p2.Normal), p2.Elevation));
                    break;
                }
                case Polyline3d p3:
                {
                    if (p3.PolyType != Poly3dType.SimplePoly) { Sample(p3, 64); break; }
                    var pts = new List<Vec3>();
                    foreach (ObjectId vid in p3)
                        if (_tr.GetObject(vid, OpenMode.ForRead) is PolylineVertex3d vx) pts.Add(V(vx.Position));
                    if (pts.Count >= 2) g.Geo.Add(new Points3(layer, pts, p3.Closed));
                    break;
                }
                case Circle c: g.Geo.Add(new Circle3(layer, V(c.Center), V(c.Normal), c.Radius)); break;
                case Arc a: g.Geo.Add(new Arc3(layer, V(a.Center), V(a.Normal), a.Radius, a.StartAngle, a.EndAngle)); break;
                case Ellipse el: Sample(el, 16); break;
                case Spline sp: Sample(sp, Math.Max(8, sp.NumControlPoints * 4)); break;
                case Leader _: case MLeader _: Drop("지시선"); break;
                case Ray _: case Xline _: Drop("구성선 (XLINE/RAY)"); break;
                case Hatch _: Drop("해치"); break;
                case Dimension _: Drop("치수"); break;
                case AttributeDefinition _: Drop("속성 정의"); break; // before DBText: it is one
                case DBText _: Drop("문자"); break;
                case MText _: Drop("여러 줄 문자"); break;
                case DBPoint _: Drop("점"); break;
                case Solid _: Drop("솔리드 (2D)"); break;
                case Viewport _: Drop("뷰포트"); break;
                case Wipeout _: Drop("와이프아웃"); break;
                case RasterImage _: Drop("래스터 이미지"); break;
                case Ole2Frame _: Drop("OLE 개체"); break;
                case UnderlayReference _: Drop("언더레이 (PDF/DWF/DGN)"); break;
                case Solid3d _: case Body _: case ZwSoft.ZwCAD.DatabaseServices.Surface _:
                    if (_depth == 0) _log.Solids[_at]++;
                    Whole(g, e, one => Burst(one, e));
                    break;
                case SubDMesh _: case PolyFaceMesh _: case PolygonMesh _:
                    _log.Solids[_at]++;
                    Whole(g, e, one => Mesh(one, e));
                    break;
                case Mline _: case Region _: Whole(g, e, one => Burst(one, e)); break; // walls drawn as MLINE and REGION outlines: their exploded lines and arcs are plan geometry
                default: Drop("기타: " + e.GetType().Name); break;
            }
        }

        // what an insert draws: null for a layout, and for an xref with nothing to read (counted)
        private int? Target(BlockReference br)
        {
            var b = (BlockTableRecord)_tr.GetObject(br.BlockTableRecord, OpenMode.ForRead); // a dynamic block's current shape is its anonymous record
            if (b.IsLayout) return null;
            if (b.IsFromExternalReference && (!b.IsResolved || b.IsUnloaded)) { LiteReadLog.Count(_log.Removed, "외부참조 (찾을 수 없거나 언로드됨)"); return null; }
            return Def(b);
        }

        // an object or one exploded piece of it goes in whole, or as its box with a count: half a solid would look like a real shape
        private void Whole(BlockDef g, Entity e, Action<BlockDef> read)
        {
            var one = new BlockDef();
            try
            {
                read(one);
                if (!LiteRead.TooBig(one.Geo)) { g.Geo.AddRange(one.Geo); return; }
            }
            catch (System.Exception) { } // the box below: Explode throws on what it cannot take apart
            var x = e.GeometricExtents;
            var box = new[] { new LiteVertex(x.MinPoint.X, x.MinPoint.Y), new LiteVertex(x.MaxPoint.X, x.MinPoint.Y), new LiteVertex(x.MaxPoint.X, x.MaxPoint.Y), new LiteVertex(x.MinPoint.X, x.MaxPoint.Y) };
            g.Geo.Add(new Poly3(e.Layer, box, true, Up, x.MinPoint.Z));
            _log.ExplodeFailed++;
        }

        private void Burst(BlockDef g, Entity e)
        {
            if (_depth >= MaxExplode) throw new InvalidOperationException("explode too deep");
            var parts = new DBObjectCollection();
            _depth++;
            try
            {
                e.Explode(parts);
                if (parts.Count == 0) throw new InvalidOperationException("explode gave nothing");
                foreach (DBObject o in parts)
                    if (o is Entity pe) Add(g, pe);
            }
            finally
            {
                _depth--;
                foreach (DBObject o in parts) o.Dispose(); // they are in no database: nothing else frees them
            }
        }

        private void Mesh(BlockDef g, Entity e)
        {
            var pts = new List<Vec3>();
            List<(int A, int B)> edges;
            switch (e)
            {
                case SubDMesh m:
                {
                    // shortcut: the base cage, not the smoothed surface (Sub Divided arrays grow fast); a smoothed mesh reads slightly larger, upgrade if its outline looks wrong
                    foreach (Point3d p in m.Vertices) pts.Add(V(p));
                    var faces = new List<int>();
                    foreach (int i in m.FaceArray) faces.Add(i);
                    edges = LiteRead.FaceEdges(faces);
                    break;
                }
                case PolyFaceMesh pf:
                {
                    var faces = new List<int[]>();
                    foreach (ObjectId id in pf)
                        switch (_tr.GetObject(id, OpenMode.ForRead))
                        {
                            case PolyFaceMeshVertex v: pts.Add(V(v.Position)); break;
                            case FaceRecord f:
                                faces.Add(Enumerable.Range(0, 4).Select(i => (short)i).Select(i => f.IsEdgeVisibleAt(i) ? Math.Abs((int)f.GetVertexAt(i)) : -Math.Abs((int)f.GetVertexAt(i))).ToArray());
                                break;
                        }
                    edges = LiteRead.RecordEdges(faces);
                    break;
                }
                case PolygonMesh pm:
                {
                    // shortcut: a surface-fit mesh gives its control net, not the fitted surface; upgrade with MSurfaceDensity if one looks wrong
                    foreach (ObjectId id in pm)
                        if (_tr.GetObject(id, OpenMode.ForRead) is PolygonMeshVertex v && v.VertexType != Vertex3dType.FitVertex) pts.Add(V(v.Position));
                    edges = LiteRead.GridEdges(pm.MSize, pm.NSize, pm.IsMClosed, pm.IsNClosed);
                    break;
                }
                default: throw new ArgumentException(e.GetType().Name);
            }
            foreach (var (a, b) in edges) g.Geo.Add(new Line3(e.Layer, pts[a], pts[b]));
        }

        // shortcut: REVCLOUD leaves a plain polyline, so it is told by shape (4+ segments, every one an arc); a real outline made only of arcs is dropped too
        private static bool IsRevCloud(Polyline pl)
        {
            int segs = pl.Closed ? pl.NumberOfVertices : pl.NumberOfVertices - 1;
            return segs >= 4 && Enumerable.Range(0, segs).All(i => pl.GetBulgeAt(i) != 0);
        }

        private static Vec3 V(Point3d p) => new Vec3(p.X, p.Y, p.Z);
        private static Vec3 V(Vector3d v) => new Vec3(v.X, v.Y, v.Z);
    }
}
