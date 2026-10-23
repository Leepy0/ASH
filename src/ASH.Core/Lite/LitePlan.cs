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

public sealed record LiteItem(LiteEntity Entity, LiteFlags Flags);

public sealed record LiteLayerState(short Color, bool Off, bool Frozen);

/// <summary>Defaults: xref contents out, 3D objects out (no footprint boxes), off/frozen layers out.</summary>
public sealed record LiteOptions(bool IncludeXref = false, bool Footprints = false, bool SkipHidden = true)
{
    public bool Allows(LiteFlags f) => (IncludeXref || (f & LiteFlags.Xref) == 0) && (Footprints || (f & LiteFlags.Footprint) == 0) && (!SkipHidden || (f & LiteFlags.Hidden) == 0);
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
    /// <summary>Chord error used for ellipses, splines and stretched arcs (drawing units).</summary>
    public double Tolerance;
    public int InsUnits;

    public static void Count(IDictionary<string, int> d, string key, int n = 1) => d[key] = (d.TryGetValue(key, out var v) ? v : 0) + n;
}

public sealed record LiteResult(double ShiftX, double ShiftY, LiteBox Extents, IReadOnlyDictionary<string, int> Written, int Entities);

/// <summary>Size estimate and selection over a LiteSource. Each item's size is what LiteDxfWriter writes for it, measured once with the shift of all the geometry;
/// the dialog then sums per layer and flag combination, so toggling layers and options costs nothing.</summary>
public sealed class LitePlan
{
    public LiteSource Source { get; }
    /// <summary>Original layer name -> R12 layer name, over every layer of the drawing.</summary>
    public IReadOnlyDictionary<string, string> Names { get; }
    /// <summary>Layers that have at least one item, in name order.</summary>
    public IReadOnlyList<string> Layers { get; }
    readonly Dictionary<string, (int Count, long Bytes)[]> _buckets = new(StringComparer.Ordinal);

    public LitePlan(LiteSource src)
    {
        Source = src;
        Names = LiteLayers.R12Names(src.Layers.Keys.Concat(src.Items.Select(i => i.Entity.Layer)));
        var box = LiteBox.Of(src.Items.Select(i => i.Entity));
        double sx = box.IsEmpty ? 0 : Shift(box.MinX, src.InsUnits), sy = box.IsEmpty ? 0 : Shift(box.MinY, src.InsUnits);
        foreach (var it in src.Items)
        {
            var l = it.Entity.Layer;
            if (!_buckets.TryGetValue(l, out var b)) _buckets[l] = b = new (int, long)[8];
            var k = (int)it.Flags & 7;
            b[k] = (b[k].Count + 1, b[k].Bytes + LiteDxfWriter.Bytes(it.Entity, Names[l], sx, sy));
        }
        Layers = _buckets.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    /// <summary>Default check state of a layer: name heuristic, and it must have something to draw.</summary>
    public bool DefaultOn(string layer) => LiteLayers.KeepByDefault(layer);

    public (int Count, long Bytes) Layer(string layer, LiteOptions o)
    {
        int n = 0; long bytes = 0;
        if (_buckets.TryGetValue(layer, out var b))
            for (int f = 0; f < 8; f++) if (o.Allows((LiteFlags)f)) { n += b[f].Count; bytes += b[f].Bytes; }
        return (n, bytes);
    }

    public long Total(LiteOptions o, ICollection<string> on) =>
        LiteDxfWriter.OverheadBytes(on.Count) + on.Sum(l => Layer(l, o).Bytes);

    /// <summary>The checked layers that weigh most, for the "over 10 MB" hint.</summary>
    public List<(string Layer, long Bytes)> Biggest(LiteOptions o, ICollection<string> on, int n = 3) =>
        on.Select(l => (Layer: l, Layer(l, o).Bytes)).Where(x => x.Bytes > 0).OrderByDescending(x => x.Bytes).Take(n).ToList();

    public List<LiteEntity> Select(LiteOptions o, ICollection<string> on)
    {
        var set = new HashSet<string>(on, StringComparer.Ordinal);
        return Source.Items.Where(i => o.Allows(i.Flags) && set.Contains(i.Entity.Layer)).Select(i => i.Entity).ToList();
    }

    /// <summary>Writes the checked layers as R12 DXF, moved so the lower left of what is written sits just above (0, 0).</summary>
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
        Row("판정", liteBytes > 10L * 1048576 ? "10 MB 초과: AutoMod가 느리거나 멈출 수 있습니다. 큰 레이어를 더 끄세요" : liteBytes > 5L * 1048576 ? "5~10 MB: 열 수 있지만 목표(5 MB 이하)보다 큽니다" : "목표(5 MB 이하) 충족");
        Row("단위", Units(s.InsUnits));
        Row("곡선 허용 오차", N(s.Tolerance) + " (도면 단위, 타원·스플라인·비균등 배율 블록의 원호)");
        Row("옵션", $"외부참조 내용 포함: {YesNo(o.IncludeXref)} / 3D 외곽 사각형: {YesNo(o.Footprints)} / 꺼진·동결 레이어 제외: {YesNo(o.SkipHidden)}");
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

        int xref = 0, foot = 0, hidden = 0, unchecked_ = 0, footWritten = 0;
        var set = new HashSet<string>(on, StringComparer.Ordinal);
        foreach (var it in s.Items)
        {
            var f = it.Flags;
            if ((f & LiteFlags.Xref) != 0 && !o.IncludeXref) xref++;
            else if ((f & LiteFlags.Footprint) != 0 && !o.Footprints) foot++;
            else if ((f & LiteFlags.Hidden) != 0 && o.SkipHidden) hidden++;
            else if (!set.Contains(it.Entity.Layer)) unchecked_++;
            else if ((f & LiteFlags.Footprint) != 0) footWritten++;
        }
        Section("제거한 항목 (블록 안은 삽입 수만큼)");
        Row("종류", "개수");
        foreach (var kv in s.Removed) Row(kv.Key, kv.Value.ToString(C));
        Row("외부참조 삽입", s.XrefRefs + (o.IncludeXref ? " (내용 포함함)" : " (내용 제외)"));
        if (!o.IncludeXref) Row("외부참조 안의 선·곡선", xref.ToString(C));
        Row("3D 솔리드·메시·서피스", s.Solids3d + (o.Footprints ? $" (외곽 사각형 {footWritten}개로 바꿈: 실제 투영이 아닌 범위 상자)" : " (제거)"));
        Row("꺼진·동결 레이어의 선·곡선", o.SkipHidden ? hidden.ToString(C) : "0 (포함함)");
        Row("선택 해제한 레이어의 선·곡선", unchecked_.ToString(C));

        Section("경량 도면 엔티티");
        Row("종류", "개수");
        foreach (var kv in r.Written) Row(kv.Key, kv.Value.ToString(C));
        Row("합계", r.Entities.ToString(C));

        Section("레이어");
        Row("원본 레이어", "DXF 레이어", "상태", "포함", "엔티티", "예상 크기 (KB)", "기본 규칙");
        foreach (var l in plan.Layers)
        {
            var (n, bytes) = plan.Layer(l, o);
            var st = s.Layers.TryGetValue(l, out var ls) ? (ls.Frozen ? "동결" : ls.Off ? "꺼짐" : "켜짐") : "-";
            Row(l, plan.Names[l], st, YesNo(set.Contains(l)), n.ToString(C), (bytes / 1024.0).ToString("0.0", C), plan.DefaultOn(l) ? "유지" : "제거");
        }
    }

    /// <summary>INSUNITS as text plus the matching Scale. Geometry is never rescaled: ASH's Scale setting converts units.</summary>
    public static string Units(int insUnits)
    {
        string name = insUnits switch { 0 => "단위 없음(mm로 간주)", 1 => "inch", 2 => "feet", 4 => "mm", 5 => "cm", 6 => "m", 7 => "km", 10 => "yard", _ => "기타" };
        return $"INSUNITS {insUnits} = {name}. 좌표는 배율을 바꾸지 않고 그대로 씁니다. ash.settings.tsv의 Scale = {UnitScale.FromInsUnits(insUnits).ToString("R", C)} (도면 1단위의 m). R12 DXF에는 단위 값이 없으므로 경량 도면 폴더의 ash.settings.tsv Scale도 이 값인지 확인하세요";
    }
}
