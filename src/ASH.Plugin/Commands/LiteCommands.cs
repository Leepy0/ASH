using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ASH.Core.Lite;
using ASH.Core.Settings;
using ASH.Plugin.Ui;
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Runtime;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ASH.Plugin.Commands.LiteCommands))]

namespace ASH.Plugin.Commands;

/// <summary>AGVLITE (spec §5): a light copy of the open factory drawing to draw AGV paths on and to use as AutoMod's background.
/// Reads model space (read-only: the open drawing is never changed) with every block expanded, lets the user pick entity types and a minimum size in LiteDialog, and writes lite\&lt;name&gt;_lite.dxf (R12, ASH's own writer)
/// plus lite\&lt;name&gt;_lite.report.tsv next to the drawing.</summary>
public sealed class LiteCommands
{
    /// <summary>Default chord error for ellipses and stretched arcs: 10 mm, well under an AGV's path accuracy and invisible at plan scale.</summary>
    internal const double DefaultToleranceMm = 10;

    /// <summary>Default minimum size: lines shorter (and objects whose box diagonal is smaller) than 100 mm are bolts, symbols and fittings, not plan geometry.</summary>
    internal const double DefaultMinSizeMm = 100;

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
        var ed = doc.Editor;
        void Say(string t) { ed.WriteMessage("\n" + t); PaletteStatus.Set(t); }
        if (Convert.ToInt32(Application.GetSystemVariable("DWGTITLED")) == 0) { Say("AGVLITE: 도면을 먼저 저장하세요. 경량 도면은 도면 폴더의 lite\\ 에 만듭니다."); return; }
        var dir = (string)Application.GetSystemVariable("DWGPREFIX");
        var dwg = Path.Combine(dir, (string)Application.GetSystemVariable("DWGNAME"));
        var name = Path.GetFileNameWithoutExtension(dwg);
        var outDir = Path.Combine(dir, "lite");
        var dxfPath = Path.Combine(outDir, name + "_lite.dxf");
        var reportPath = Path.Combine(outDir, name + "_lite.report.tsv");
        int insUnits = Convert.ToInt32(Application.GetSystemVariable("INSUNITS"));

        double tolMm = DefaultToleranceMm;
        Say("AGVLITE: 도면 읽는 중… (원본은 바꾸지 않습니다)");
        var plan = new LitePlan(Extract(doc, insUnits, tolMm));
        if (plan.Types.Count == 0) { Say("AGVLITE: 모델 공간에 남길 선·곡선이 없습니다."); return; }

        LiteOptions options;
        List<string> types;
        using (var dlg = new LiteDialog(plan, Path.GetFileName(dwg), @"lite\" + Path.GetFileName(dxfPath), tolMm, DefaultMinSizeMm))
        {
            if (Application.ShowModalDialog(dlg) != System.Windows.Forms.DialogResult.OK) { PaletteStatus.Set("AGVLITE 취소"); return; }
            options = dlg.Options;
            types = dlg.Types;
            if (Math.Abs(dlg.ToleranceMm - tolMm) > 1e-9)
            {
                tolMm = dlg.ToleranceMm;
                Say($"AGVLITE: 곡선 허용 오차 {tolMm} mm로 다시 읽는 중…");
                plan = new LitePlan(Extract(doc, insUnits, tolMm));
            }
        }

        Directory.CreateDirectory(outDir);
        LiteResult r;
        try
        {
            using (var w = new StreamWriter(dxfPath, false, Encoding.ASCII)) r = plan.Write(w, options, types);
        }
        catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Say($"AGVLITE: {dxfPath}에 쓰지 못했습니다(다른 프로그램이 열고 있나요?): {ex.Message}");
            return;
        }
        long liteBytes = new FileInfo(dxfPath).Length, srcBytes = File.Exists(dwg) ? new FileInfo(dwg).Length : 0;
        using (var w = new StreamWriter(reportPath, false, new UTF8Encoding(true))) // BOM: Excel opens the Korean text right
            LiteReport.Write(w, plan, options, types, r, dwg, srcBytes, dxfPath, liteBytes, ASH.Core.SeoulTime.Now);

        var src = plan.Source;
        ed.WriteMessage($"\nAGVLITE: 엔티티 {r.Entities:N0}개, {liteBytes / 1048576.0:0.00} MB (원본 DWG {srcBytes / 1048576.0:0.00} MB)");
        ed.WriteMessage($"\n원점 이동 ({r.ShiftX:0.###}, {r.ShiftY:0.###}): 원본 좌표로 되돌리려면 ash.settings.tsv에 OriginX {-r.ShiftX:0.###}, OriginY {-r.ShiftY:0.###} (보고서 참고)");
        ed.WriteMessage($"\nDXF: {dxfPath}\n보고서: {reportPath}");
        if (liteBytes > 10L * 1048576) ed.WriteMessage("\n경고: 10 MB를 넘습니다. AutoMod가 느려지거나 멈출 수 있습니다. 최소 크기를 키우거나 큰 종류를 끄고 다시 실행하세요(보고서의 종류 표 참고).");
        if (src.Truncated) ed.WriteMessage("\n경고: 작업 한도에 도달해 도면 일부만 담았습니다(블록 중첩·반복이 너무 많음).");
        if (src.Failures > 0) ed.WriteMessage($"\n참고: 읽지 못한 엔티티 {src.Failures:N0}개를 건너뛰었습니다.");
        PaletteStatus.Set($"AGVLITE 완료: {liteBytes / 1048576.0:0.00} MB" + (liteBytes > 10L * 1048576 ? " (10 MB 초과)" : src.Truncated ? " (일부 누락)" : ""));

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

    /// <summary>Everything AGVLITE could keep from model space, flagged by what only an option keeps (xref contents, 3D footprints, off/frozen layers).</summary>
    internal static LiteSource Extract(Document doc, int insUnits, double tolMm)
    {
        var src = new LiteSource { InsUnits = insUnits, Tolerance = tolMm * 0.001 / UnitScale.FromInsUnits(insUnits) };
        using (doc.LockDocument())
        using (var tr = doc.Database.TransactionManager.StartTransaction())
        {
            new Extractor(tr, src).Run(doc.Database);
            tr.Commit(); // read-only; commit is just cheaper than abort
        }
        return src;
    }

    /// <summary>Reads each block definition once into block-local neutral geometry, then places it per insert with the composed transform.
    /// The work budget and the nesting cap keep a drawing with huge INSERT fan-out (or a malformed self-referencing block) from hanging ZWCAD.</summary>
    private sealed class Extractor
    {
        private const int MaxDepth = 8;
        /// <summary>Items placed + inserts expanded. 2 million lines are ~200 MB of DXF, far past anything AutoMod opens, so only a runaway drawing reaches it.</summary>
        private const int Budget = 2_000_000;

        private sealed class BlockGeo
        {
            public readonly List<(LiteEntity Geo, LiteFlags Flags, string Type)> Items = new List<(LiteEntity, LiteFlags, string)>();
            public readonly List<(ObjectId Btr, Affine2 Xf, string Layer, bool Xref, bool Resolved)> Inserts = new List<(ObjectId, Affine2, string, bool, bool)>();
            public readonly Dictionary<string, int> Removed = new Dictionary<string, int>();
            public readonly Dictionary<string, int> Kinds = new Dictionary<string, int>();
            public int Solids;
        }

        private readonly Transaction _tr;
        private readonly LiteSource _src;
        private readonly Dictionary<ObjectId, BlockGeo> _cache = new Dictionary<ObjectId, BlockGeo>();
        private int _left = Budget;

        public Extractor(Transaction tr, LiteSource src) { _tr = tr; _src = src; }

        public void Run(Database db)
        {
            var lt = (LayerTable)_tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId id in lt)
                if (_tr.GetObject(id, OpenMode.ForRead) is LayerTableRecord l && !l.IsErased)
                    _src.Layers[l.Name] = new LiteLayerState(l.Color.IsByAci ? l.Color.ColorIndex : (short)7, l.IsOff, l.IsFrozen);
            var bt = (BlockTableRecord)_tr.GetObject(((BlockTable)_tr.GetObject(db.BlockTableId, OpenMode.ForRead))[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            var ms = Read(bt); // not cached: model space is read once
            foreach (var kv in ms.Kinds) LiteSource.Count(_src.Before, kv.Key, kv.Value);
            Walk(ms, Affine2.Identity, "0", LiteFlags.None, 0, false);
        }

        private bool Take() { if (_left > 0) { _left--; return true; } _src.Truncated = true; return false; }

        private bool Hidden(string layer) => _src.Layers.TryGetValue(layer, out var s) && (s.Off || s.Frozen);
        private bool Frozen(string layer) => _src.Layers.TryGetValue(layer, out var s) && s.Frozen;

        private void Walk(BlockGeo g, Affine2 xf, string inherit, LiteFlags flags, int depth, bool parentFrozen)
        {
            foreach (var (geo, f, type) in g.Items)
            {
                if (!Take()) return;
                var layer = depth > 0 && geo.Layer == "0" ? inherit : geo.Layer; // block contents on layer 0 take the insert's layer
                if (LiteLayers.IsAsh(layer)) { LiteSource.Count(_src.Removed, "AGV_ 레이어 (ASH 경로·CP)"); continue; }
                var fl = flags | f | (parentFrozen || Hidden(layer) ? LiteFlags.Hidden : LiteFlags.None);
                try { _src.Items.Add(new LiteItem(CurveApprox.Transform(geo, xf, _src.Tolerance, layer), fl, type)); }
                catch (System.Exception) { _src.Failures++; }
            }
            foreach (var kv in g.Removed) LiteSource.Count(_src.Removed, kv.Key, kv.Value);
            _src.Solids3d += g.Solids;
            foreach (var ins in g.Inserts)
            {
                if (!Take()) return;
                var layer = depth > 0 && ins.Layer == "0" ? inherit : ins.Layer;
                if (ins.Xref)
                {
                    _src.XrefRefs++;
                    if (!ins.Resolved) { LiteSource.Count(_src.Removed, "외부참조 (찾을 수 없거나 언로드됨)"); continue; }
                }
                if (depth + 1 > MaxDepth) { LiteSource.Count(_src.Removed, $"블록 중첩 {MaxDepth}단계 초과"); continue; }
                Walk(Geo(ins.Btr), ins.Xf.Then(xf), layer, flags | (ins.Xref ? LiteFlags.Xref : LiteFlags.None), depth + 1, parentFrozen || Frozen(layer));
                if (_src.Truncated) return;
            }
        }

        private BlockGeo Geo(ObjectId btrId)
        {
            // a block that (wrongly) contains itself is stopped by MaxDepth in Walk: Read never recurses
            if (!_cache.TryGetValue(btrId, out var g)) _cache[btrId] = g = Read((BlockTableRecord)_tr.GetObject(btrId, OpenMode.ForRead));
            return g;
        }

        private BlockGeo Read(BlockTableRecord btr)
        {
            var g = new BlockGeo();
            foreach (ObjectId id in btr)
            {
                if (_left <= 0) { _src.Truncated = true; break; }
                try
                {
                    if (!(_tr.GetObject(id, OpenMode.ForRead, false) is Entity e) || e.IsErased) continue;
                    LiteSource.Count(g.Kinds, e.GetType().Name);
                    if (!e.Visible) { LiteSource.Count(g.Removed, "숨긴 엔티티"); continue; }
                    Add(g, e);
                }
                catch (System.Exception) { _src.Failures++; } // an odd entity must never stop the command
            }
            return g;
        }

        private void Add(BlockGeo g, Entity e)
        {
            string layer = e.Layer;
            double tol = _src.Tolerance;
            void Item(LiteEntity x, string type, LiteFlags f = LiteFlags.None) => g.Items.Add((x, f, type));
            void Drop(string kind) => LiteSource.Count(g.Removed, kind);
            void Poly(List<(double X, double Y)> pts, bool closed, string type)
            {
                if (closed && pts.Count > 2 && pts[0] == pts[pts.Count - 1]) pts.RemoveAt(pts.Count - 1);
                if (pts.Count >= 2) Item(new LitePolyline(layer, pts.Select(p => new LiteVertex(p.X, p.Y)).ToArray(), closed), type);
            }
            void Sample(Curve c, int initial, string type) =>
                Poly(CurveApprox.Adaptive(t => { var p = c.GetPointAtParameter(t); return (p.X, p.Y); }, c.StartParam, c.EndParam, tol, initial), c.Closed, type);
            bool Flat(Vector3d n) => Math.Abs(Math.Abs(n.Z) - 1) < 1e-9;

            switch (e)
            {
                case Table _: Drop("표"); break;
                case MInsertBlock mi:
                {
                    // shortcut: the row/column offsets are rotated by Rotation in the plan only (no OCS tilt); tilted MINSERTs are rare in factory layouts
                    var b = Block(mi, out var xref, out var resolved);
                    if (b == null) break;
                    var xf = ToAffine(mi.BlockTransform);
                    double co = Math.Cos(mi.Rotation), si = Math.Sin(mi.Rotation);
                    for (int r = 0; r < mi.Rows; r++)
                        for (int c = 0; c < mi.Columns; c++)
                        {
                            double dx = c * mi.ColumnSpacing, dy = r * mi.RowSpacing;
                            g.Inserts.Add((b.Value, xf.Then(new Affine2(1, 0, 0, 1, co * dx - si * dy, si * dx + co * dy)), layer, xref, resolved));
                        }
                    break;
                }
                case BlockReference br:
                {
                    var b = Block(br, out var xref, out var resolved);
                    if (b != null) g.Inserts.Add((b.Value, ToAffine(br.BlockTransform), layer, xref, resolved));
                    break;
                }
                case Line l: Item(new LiteLine(layer, l.StartPoint.X, l.StartPoint.Y, l.EndPoint.X, l.EndPoint.Y), LiteTypes.Line); break;
                case Polyline pl:
                {
                    if (IsRevCloud(pl)) { Drop("구름형 리비전"); break; }
                    if (!Flat(pl.Normal) && pl.HasBulges) { Sample(pl, Math.Max(8, pl.NumberOfVertices * 4), LiteTypes.Polyline); break; }
                    double flip = pl.Normal.Z < 0 ? -1 : 1; // the bulge is measured about the normal: CCW about -Z is CW in plan
                    var v = new LiteVertex[pl.NumberOfVertices];
                    for (int i = 0; i < v.Length; i++) { var p = pl.GetPoint3dAt(i); v[i] = new LiteVertex(p.X, p.Y, pl.GetBulgeAt(i) * flip); }
                    if (v.Length >= 2) Item(new LitePolyline(layer, v, pl.Closed), LiteTypes.Polyline);
                    break;
                }
                case Polyline2d p2:
                {
                    if (p2.PolyType != Poly2dType.SimplePoly || !Flat(p2.Normal)) { Sample(p2, 64, LiteTypes.Polyline); break; } // fit / spline-fit curves: sample the curve ZWCAD shows
                    double flip = p2.Normal.Z < 0 ? -1 : 1;
                    var v = new List<LiteVertex>();
                    foreach (ObjectId vid in p2)
                        if (_tr.GetObject(vid, OpenMode.ForRead) is Vertex2d vx) { var p = p2.VertexPosition(vx); v.Add(new LiteVertex(p.X, p.Y, vx.Bulge * flip)); }
                    if (v.Count >= 2) Item(new LitePolyline(layer, v.ToArray(), p2.Closed), LiteTypes.Polyline);
                    break;
                }
                case Polyline3d p3:
                {
                    if (p3.PolyType != Poly3dType.SimplePoly) { Sample(p3, 64, LiteTypes.Polyline); break; }
                    var pts = new List<(double X, double Y)>();
                    foreach (ObjectId vid in p3)
                        if (_tr.GetObject(vid, OpenMode.ForRead) is PolylineVertex3d vx) pts.Add((vx.Position.X, vx.Position.Y)); // flattened: Z dropped
                    Poly(pts, p3.Closed, LiteTypes.Polyline);
                    break;
                }
                case Circle c:
                    if (Flat(c.Normal)) Item(new LiteCircle(layer, c.Center.X, c.Center.Y, c.Radius), LiteTypes.Circle);
                    else Sample(c, 16, LiteTypes.Circle);
                    break;
                case Arc a:
                {
                    if (!Flat(a.Normal)) { Sample(a, 8, LiteTypes.Arc); break; }
                    double s = Deg(a.StartPoint, a.Center), en = Deg(a.EndPoint, a.Center);
                    Item(a.Normal.Z < 0 ? new LiteArc(layer, a.Center.X, a.Center.Y, a.Radius, en, s) : new LiteArc(layer, a.Center.X, a.Center.Y, a.Radius, s, en), LiteTypes.Arc);
                    break;
                }
                case Ellipse el:
                {
                    bool full = Math.Abs(el.EndParam - el.StartParam - 2 * Math.PI) < 1e-9;
                    Poly(CurveApprox.Ellipse(el.Center.X, el.Center.Y, el.MajorAxis.X, el.MajorAxis.Y, el.MinorAxis.X, el.MinorAxis.Y, el.StartParam, el.EndParam, tol), full, LiteTypes.Ellipse);
                    break;
                }
                case Spline sp: Sample(sp, Math.Max(8, sp.NumControlPoints * 4), LiteTypes.Spline); break; // a type of its own, off by default
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
                case Solid3d _: case Body _: case SubDMesh _: case PolyFaceMesh _: case PolygonMesh _: case ZwSoft.ZwCAD.DatabaseServices.Surface _:
                {
                    g.Solids++;
                    var x = e.GeometricExtents; // footprint box only, not a true projection (the dialog says so)
                    Item(new LitePolyline(layer, new[] { new LiteVertex(x.MinPoint.X, x.MinPoint.Y), new LiteVertex(x.MaxPoint.X, x.MinPoint.Y), new LiteVertex(x.MaxPoint.X, x.MaxPoint.Y), new LiteVertex(x.MinPoint.X, x.MaxPoint.Y) }, true), LiteTypes.Footprint, LiteFlags.Footprint);
                    break;
                }
                case Mline _: case Region _:
                {
                    // walls drawn as MLINE and REGION outlines: their exploded lines and arcs are plan geometry
                    var parts = new DBObjectCollection();
                    e.Explode(parts);
                    foreach (DBObject o in parts)
                        using (o)
                            if (o is Entity pe && !(pe is Mline) && !(pe is Region)) Add(g, pe);
                    break;
                }
                default: Drop("기타: " + e.GetType().Name); break;
            }
        }

        // the block an insert shows (a dynamic block's current shape is its anonymous record); null for a layout
        private ObjectId? Block(BlockReference br, out bool xref, out bool resolved)
        {
            var b = (BlockTableRecord)_tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            xref = b.IsFromExternalReference;
            resolved = !xref || (b.IsResolved && !b.IsUnloaded);
            return b.IsLayout ? (ObjectId?)null : b.ObjectId;
        }

        // shortcut: REVCLOUD leaves a plain polyline, so it is told by shape (4+ segments, every one an arc); a real outline made only of arcs is dropped too
        private static bool IsRevCloud(Polyline pl)
        {
            int segs = pl.Closed ? pl.NumberOfVertices : pl.NumberOfVertices - 1;
            return segs >= 4 && Enumerable.Range(0, segs).All(i => pl.GetBulgeAt(i) != 0);
        }

        private static double Deg(Point3d p, Point3d c) => CurveApprox.Norm360(Math.Atan2(p.Y - c.Y, p.X - c.X) * 180 / Math.PI);

        // shortcut: plan rows of the 3D matrix (z terms dropped); exact unless a block is tilted out of plan and holds geometry off z = 0
        private static Affine2 ToAffine(Matrix3d m)
        {
            var a = m.ToArray(); // row-major
            return new Affine2(a[0], a[1], a[4], a[5], a[3], a[7]);
        }
    }
}
