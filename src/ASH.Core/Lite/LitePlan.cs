using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ASH.Core.Settings;

namespace ASH.Core.Lite;

/// <summary>Why an extracted item may be left out: it comes from inside an xref, it is the footprint box of a 3D object, or its layer (or a frozen parent insert's) is off/frozen.</summary>
[Flags]
public enum LiteFlags { None = 0, Xref = 1, Footprint = 2, Hidden = 4 }

/// <summary>Type = the source entity's kind (LiteTypes), which the AGVLITE dialog selects by: an ellipse stays "타원" though it is written as a polyline.
/// Block = box diagonal of the smallest block insert it sits in (infinity in model space), so a block smaller than the minimum size goes as a whole.</summary>
public sealed record LiteItem(LiteEntity Entity, LiteFlags Flags, string Type = LiteTypes.Line, double Block = double.PositiveInfinity);

/// <summary>The kinds AGVLITE keeps (everything else is always removed), in dialog order.</summary>
public static class LiteTypes
{
    public const string Line = "선", Polyline = "폴리선", Arc = "호", Circle = "원", Ellipse = "타원", Spline = "스플라인", Footprint = "3D 외곽 상자";
    public static readonly string[] Order = { Line, Polyline, Arc, Circle, Ellipse, Spline, Footprint };
}

public sealed record LiteLayerState(short Color, bool Off, bool Frozen);

/// <summary>Defaults: xref contents out, off/frozen layers out. MinSize (drawing units): an item whose LiteBox.Size is smaller is removed, block contents included.</summary>
public sealed record LiteOptions(bool IncludeXref = false, bool SkipHidden = true, double MinSize = 0)
{
    public bool Allows(LiteFlags f) => (IncludeXref || (f & LiteFlags.Xref) == 0) && (!SkipHidden || (f & LiteFlags.Hidden) == 0);
}

/// <summary>What AGVLITE read from the open drawing: every item it could keep (also those only some options keep), and counts for the report.</summary>
public sealed class LiteSource
{
    public List<LiteItem> Items { get; } = new();
    /// <summary>Every layer of the drawing (xref layers included) with its ACI colour and state.</summary>
    public Dictionary<string, LiteLayerState> Layers { get; } = new(StringComparer.Ordinal);
    /// <summary>Top-level model space entities by type, before lightening.</summary>
    public SortedDictionary<string, int> Before { get; } = new(StringComparer.Ordinal);
    /// <summary>Entities always dropped (text, hatch, dimension, image ...), counted per block instance.</summary>
    public SortedDictionary<string, int> Removed { get; } = new(StringComparer.Ordinal);
    public int XrefRefs, Solids3d, Failures;
    /// <summary>The work budget ran out: the item list is partial.</summary>
    public bool Truncated;
    /// <summary>Chord error used for ellipses and stretched arcs (drawing units).</summary>
    public double Tolerance;
    public int InsUnits;

    public static void Count(IDictionary<string, int> d, string key, int n = 1) => d[key] = (d.TryGetValue(key, out var v) ? v : 0) + n;

    /// <summary>Items from <paramref name="start"/> on are one block insert's contents (nested ones included): stamp them with its box diagonal.</summary>
    public void CloseBlock(int start)
    {
        var b = LiteBox.Empty;
        for (int i = start; i < Items.Count; i++) Items[i].Entity.Extend(ref b);
        double d = b.Diagonal;
        for (int i = start; i < Items.Count; i++)
            if (d < Items[i].Block) Items[i] = Items[i] with { Block = d };
    }
}

public sealed record LiteResult(double ShiftX, double ShiftY, LiteBox Extents, IReadOnlyDictionary<string, int> Written, int Entities);

/// <summary>Size estimate and selection over a LiteSource. Each item's bytes (what LiteDxfWriter writes for it, with the shift of all the geometry) and size are
/// measured once; the dialog then re-sums them per type for each change of types, options or minimum size.</summary>
public sealed class LitePlan
{
    public LiteSource Source { get; }
    /// <summary>Original layer name -> R12 layer name, over every layer of the drawing.</summary>
    public IReadOnlyDictionary<string, string> Names { get; }
    /// <summary>Types that have at least one item, in LiteTypes.Order.</summary>
    public IReadOnlyList<string> Types { get; }
    readonly long[] _bytes;
    readonly double[] _size;

    public LitePlan(LiteSource src)
    {
        Source = src;
        Names = LiteLayers.R12Names(src.Layers.Keys.Concat(src.Items.Select(i => i.Entity.Layer)));
        var box = LiteBox.Of(src.Items.Select(i => i.Entity));
        double sx = box.IsEmpty ? 0 : Shift(box.MinX, src.InsUnits), sy = box.IsEmpty ? 0 : Shift(box.MinY, src.InsUnits);
        _bytes = new long[src.Items.Count];
        _size = new double[src.Items.Count];
        for (int i = 0; i < _bytes.Length; i++)
        {
            var e = src.Items[i].Entity;
            _bytes[i] = LiteDxfWriter.Bytes(e, Names[e.Layer], sx, sy);
        }
        ChainSizes(src.Items, Math.Max(src.Tolerance, 1e-9), _size);
        for (int i = 0; i < _size.Length; i++) _size[i] = Math.Min(_size[i], src.Items[i].Block); // a small block goes whole, whatever its pieces chain to
        Types = src.Items.Select(i => i.Type).Distinct().OrderBy(t => Array.IndexOf(LiteTypes.Order, t) is var k && k < 0 ? int.MaxValue : k).ThenBy(t => t, StringComparer.Ordinal).ToList();
    }

    /// <summary>The size the minimum-size rule judges item <paramref name="i"/> by: its chain (see <see cref="ChainSizes"/>), or its block if that is smaller.</summary>
    public double SizeOf(int i) => _size[i];

    /// <summary>Size of each item for the minimum-size rule: the box diagonal of the chain it belongs to. Lines, arcs and open polylines whose end points
    /// meet (within <paramref name="snap"/>, drawing units) are one chain, so a large outline drawn as many short pieces — common in equipment blocks
    /// from other CAD — is kept as a whole instead of losing every piece; a closed shape or a circle stands alone (a bolt hole stays a small detail).</summary>
    internal static void ChainSizes(IReadOnlyList<LiteItem> items, double snap, double[] size)
    {
        int n = items.Count;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
        // one end point per grid cell is enough to link: every later point in or next to that cell within snap joins its item
        var cells = new Dictionary<(long, long), (double X, double Y, int Item)>();
        void End(int i, double x, double y)
        {
            long cx = (long)Math.Floor(x / snap), cy = (long)Math.Floor(y / snap);
            for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                    if (cells.TryGetValue((cx + dx, cy + dy), out var c) && Math.Abs(c.X - x) <= snap && Math.Abs(c.Y - y) <= snap) Union(i, c.Item);
            if (!cells.ContainsKey((cx, cy))) cells[(cx, cy)] = (x, y, i);
        }
        for (int i = 0; i < n; i++)
        {
            if ((items[i].Flags & LiteFlags.Footprint) != 0) continue;
            switch (items[i].Entity)
            {
                case LiteLine l: End(i, l.X1, l.Y1); End(i, l.X2, l.Y2); break;
                case LiteArc a:
                    End(i, a.Cx + a.R * Math.Cos(a.StartDeg * Math.PI / 180), a.Cy + a.R * Math.Sin(a.StartDeg * Math.PI / 180));
                    End(i, a.Cx + a.R * Math.Cos(a.EndDeg * Math.PI / 180), a.Cy + a.R * Math.Sin(a.EndDeg * Math.PI / 180));
                    break;
                case LitePolyline p when !p.Closed && p.Vertices.Count >= 2:
                    End(i, p.Vertices[0].X, p.Vertices[0].Y); End(i, p.Vertices[p.Vertices.Count - 1].X, p.Vertices[p.Vertices.Count - 1].Y);
                    break;
            }
        }
        var boxes = new Dictionary<int, LiteBox>();
        for (int i = 0; i < n; i++)
        {
            var r = Find(i);
            var b = boxes.TryGetValue(r, out var x) ? x : LiteBox.Empty;
            items[i].Entity.Extend(ref b);
            boxes[r] = b;
        }
        for (int i = 0; i < n; i++)
        {
            size[i] = boxes[Find(i)].Diagonal;
        }
    }

    /// <summary>Default check state of a type: off for the 3D footprint boxes (not a true plan projection) and splines (removed unless asked for:
    /// an imported equipment block may draw its outline with them, so they can be turned back on, as lines).</summary>
    public bool DefaultOn(string type) => type is not (LiteTypes.Footprint or LiteTypes.Spline);

    /// <summary>Millimetres to drawing units, for the dialog's minimum size.</summary>
    public double FromMm(double mm) => mm * 0.001 / UnitScale.FromInsUnits(Source.InsUnits);

    bool Keeps(int i, LiteOptions o) => o.Allows(Source.Items[i].Flags) && _size[i] >= o.MinSize;

    /// <summary>Count and bytes per type of what the options keep (every type of Types present).</summary>
    public Dictionary<string, (int Count, long Bytes)> ByType(LiteOptions o)
    {
        var d = Types.ToDictionary(t => t, _ => (Count: 0, Bytes: 0L), StringComparer.Ordinal);
        for (int i = 0; i < _bytes.Length; i++)
            if (Keeps(i, o)) { var t = Source.Items[i].Type; var v = d[t]; d[t] = (v.Count + 1, v.Bytes + _bytes[i]); }
        return d;
    }

    /// <summary>Items the flags keep but MinSize removes.</summary>
    public int Small(LiteOptions o) => Enumerable.Range(0, _bytes.Length).Count(i => o.Allows(Source.Items[i].Flags) && _size[i] < o.MinSize);

    public long Total(LiteOptions o, ICollection<string> on)
    {
        var set = new HashSet<string>(on, StringComparer.Ordinal);
        var layers = new HashSet<string>(StringComparer.Ordinal);
        long bytes = 0;
        for (int i = 0; i < _bytes.Length; i++)
            if (set.Contains(Source.Items[i].Type) && Keeps(i, o)) { bytes += _bytes[i]; layers.Add(Source.Items[i].Entity.Layer); }
        return LiteDxfWriter.OverheadBytes(layers.Count) + bytes;
    }

    /// <summary>The checked types that weigh most, for the "over 10 MB" hint.</summary>
    public List<(string Type, long Bytes)> Biggest(LiteOptions o, ICollection<string> on, int n = 3)
    {
        var d = ByType(o);
        return on.Where(d.ContainsKey).Select(t => (Type: t, d[t].Bytes)).Where(x => x.Bytes > 0).OrderByDescending(x => x.Bytes).Take(n).ToList();
    }

    public List<LiteEntity> Select(LiteOptions o, ICollection<string> on)
    {
        var set = new HashSet<string>(on, StringComparer.Ordinal);
        return Enumerable.Range(0, _bytes.Length).Where(i => set.Contains(Source.Items[i].Type) && Keeps(i, o)).Select(i => Source.Items[i].Entity).ToList();
    }

    /// <summary>Writes the checked types as R12 DXF (every Z is 0), moved so the lower left of what is written sits just above (0, 0).</summary>
    public LiteResult Write(TextWriter w, LiteOptions o, ICollection<string> on)
    {
        var ents = Select(o, on);
        var box = LiteBox.Of(ents);
        double sx = box.IsEmpty ? 0 : Shift(box.MinX, Source.InsUnits), sy = box.IsEmpty ? 0 : Shift(box.MinY, Source.InsUnits);
        var colors = Source.Layers.ToDictionary(kv => kv.Key, kv => kv.Value.Color, StringComparer.Ordinal);
        LiteDxfWriter.Write(w, ents, Names, colors, sx, sy);
        var written = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in ents) LiteSource.Count(written, e.Kind);
        var ext = box.IsEmpty ? box : new LiteBox { MinX = box.MinX - sx, MinY = box.MinY - sy, MaxX = box.MaxX - sx, MaxY = box.MaxY - sy };
        return new LiteResult(sx, sy, ext, written, ents.Count);
    }

    /// <summary>The lower-left coordinate rounded down to a clean step: the power of ten closest below one metre in drawing units (1000 in a mm drawing, 1 in m, 10 in inches),
    /// so the shift is a round number and every written coordinate is >= 0.</summary>
    public static double Shift(double min, int insUnits)
    {
        double perMeter = 1 / UnitScale.FromInsUnits(insUnits);
        double step = Math.Pow(10, Math.Floor(Math.Log10(perMeter) + 1e-9));
        return Math.Round(Math.Floor(min / step) * step, 9);
    }
}

/// <summary>lite\&lt;name&gt;_lite.report.tsv: before/after counts and sizes, what was dropped and why, the layer table, the origin shift and the units.</summary>
public static class LiteReport
{
    static readonly CultureInfo C = CultureInfo.InvariantCulture;

    public static void Write(TextWriter w, LitePlan plan, LiteOptions o, ICollection<string> on, LiteResult r, string sourcePath, long sourceBytes, string litePath, long liteBytes, DateTime now)
    {
        var s = plan.Source;
        string Mb(long b) => (b / 1048576.0).ToString("0.00", C) + " MB";
        string YesNo(bool b) => b ? "예" : "아니오";
        string N(double v) => v.ToString("0.######", C);
        void Row(params string[] cells) => w.WriteLine(string.Join("\t", cells.Select(c => (c ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '))));
        void Section(string title) { w.WriteLine(); Row("[" + title + "]"); }

        Row("ASH 도면 경량화 보고서 (AGVLITE)");
        Row("항목", "값");
        Row("작성", now.ToString("yyyy-MM-dd HH:mm", C));
        Row("원본 도면", sourcePath);
        Row("원본 크기 (DWG)", sourceBytes > 0 ? $"{Mb(sourceBytes)} ({sourceBytes.ToString("N0", C)} bytes)" : "알 수 없음 (저장 안 된 도면)");
        Row("경량 DXF", litePath);
        Row("경량 크기 (DXF R12)", $"{Mb(liteBytes)} ({liteBytes.ToString("N0", C)} bytes)");
        Row("대화상자의 예상 크기", Mb(plan.Total(o, on)));
        Row("판정", liteBytes > 10L * 1048576 ? "10 MB 초과: AutoMod가 느리거나 멈출 수 있습니다. 최소 크기를 키우거나 큰 종류를 더 끄세요" : liteBytes > 5L * 1048576 ? "5~10 MB: 열 수 있지만 목표(5 MB 이하)보다 큽니다" : "목표(5 MB 이하) 충족");
        Row("단위", Units(s.InsUnits));
        Row("곡선 허용 오차", N(s.Tolerance) + " (도면 단위, 타원·비균등 배율 블록의 원호)");
        Row("옵션", $"외부참조 내용 포함: {YesNo(o.IncludeXref)} / 꺼진·동결 레이어 제외: {YesNo(o.SkipHidden)} / 최소 크기: {N(o.MinSize)} (도면 단위, 외곽 상자 대각선·선 길이)");
        Row("남긴 종류", string.Join(", ", plan.Types.Where(on.Contains)));
        Row("평면화", "모든 좌표의 Z = 0 (3D 폴리선·기울어진 객체는 평면에 투영)");
        Row("작업 한도", s.Truncated ? "도달: 블록 중첩·반복이 너무 많아 일부만 담았습니다" : "정상");
        Row("읽지 못한 엔티티", s.Failures.ToString(C));

        Section("원점 이동");
        Row("이동량 X", N(r.ShiftX));
        Row("이동량 Y", N(r.ShiftY));
        Row("설명", "경량 좌표 = 원본 좌표 - 이동량. 경량 도면의 범위: " + (r.Extents.IsEmpty ? "-" : $"({N(r.Extents.MinX)}, {N(r.Extents.MinY)}) ~ ({N(r.Extents.MaxX)}, {N(r.Extents.MaxY)})"));
        Row("ash.settings.tsv에 넣을 값", "경량 도면에 그린 경로를 원본 도면 좌표 기준 모델로 만들 때(원본에서 Origin 0이었다면):");
        Row("OriginX", N(-r.ShiftX));
        Row("OriginY", N(-r.ShiftY));
        Row("참고", "모델 좌표 = (도면 좌표 - Origin) x Scale. 원본에서 Origin (Ox, Oy)를 쓰던 프로젝트는 OriginX = Ox - 이동량 X, OriginY = Oy - 이동량 Y");

        Section("원본 엔티티 (모델 공간 최상위)");
        Row("종류", "개수");
        foreach (var kv in s.Before) Row(kv.Key, kv.Value.ToString(C));
        Row("합계", s.Before.Values.Sum().ToString(C));

        int xref = 0, hidden = 0, small = 0, unchecked_ = 0, footWritten = 0;
        var set = new HashSet<string>(on, StringComparer.Ordinal);
        for (int ii = 0; ii < s.Items.Count; ii++)
        {
            var it = s.Items[ii]; var f = it.Flags;
            if ((f & LiteFlags.Xref) != 0 && !o.IncludeXref) xref++;
            else if ((f & LiteFlags.Hidden) != 0 && o.SkipHidden) hidden++;
            else if (!set.Contains(it.Type)) unchecked_++;
            else if (plan.SizeOf(ii) < o.MinSize) small++;
            else if ((f & LiteFlags.Footprint) != 0) footWritten++;
        }
        Section("제거한 항목 (블록 안은 삽입 수만큼)");
        Row("종류", "개수");
        foreach (var kv in s.Removed) Row(kv.Key, kv.Value.ToString(C));
        Row("외부참조 삽입", s.XrefRefs + (o.IncludeXref ? " (내용 포함함)" : " (내용 제외)"));
        if (!o.IncludeXref) Row("외부참조 안의 선·곡선", xref.ToString(C));
        Row("3D 솔리드·메시·서피스", s.Solids3d + (footWritten > 0 ? $" (외곽 사각형 {footWritten}개로 바꿈: 실제 투영이 아닌 범위 상자)" : " (제거)"));
        Row("꺼진·동결 레이어의 선·곡선", o.SkipHidden ? hidden.ToString(C) : "0 (포함함)");
        Row("선택 해제한 종류의 선·곡선", unchecked_.ToString(C));
        Row("최소 크기 미만 (블록 안 포함)", small.ToString(C));

        Section("경량 도면 엔티티");
        Row("종류", "개수");
        foreach (var kv in r.Written) Row(kv.Key, kv.Value.ToString(C));
        Row("합계", r.Entities.ToString(C));

        Section("종류");
        Row("종류", "포함", "엔티티 (최소 크기 이상)", "예상 크기 (KB)");
        foreach (var kv in plan.ByType(o))
            Row(kv.Key, YesNo(set.Contains(kv.Key)), kv.Value.Count.ToString(C), (kv.Value.Bytes / 1024.0).ToString("0.0", C));
    }

    /// <summary>INSUNITS as text plus the matching Scale. Geometry is never rescaled: ASH's Scale setting converts units.</summary>
    public static string Units(int insUnits)
    {
        string name = insUnits switch { 0 => "단위 없음(mm로 간주)", 1 => "inch", 2 => "feet", 4 => "mm", 5 => "cm", 6 => "m", 7 => "km", 10 => "yard", _ => "기타" };
        return $"INSUNITS {insUnits} = {name}. 좌표는 배율을 바꾸지 않고 그대로 씁니다. ash.settings.tsv의 Scale = {UnitScale.FromInsUnits(insUnits).ToString("R", C)} (도면 1단위의 m). R12 DXF에는 단위 값이 없으므로 경량 도면 폴더의 ash.settings.tsv Scale도 이 값인지 확인하세요";
    }
}
