using System;
using System.Collections.Generic;
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
    public double CloseBlock(int start)
    {
        var b = LiteBox.Empty;
        for (int i = start; i < Items.Count; i++) Items[i].Entity.Extend(ref b);
        double d = b.Diagonal;
        for (int i = start; i < Items.Count; i++)
            if (d < Items[i].Block) Items[i] = Items[i] with { Block = d };
        return d;
    }
}

/// <summary>Deduped, DedupedBytes: placed entities the writer dropped as duplicates, and what they would have added. Level: the level written, -1 for bare thresholds.</summary>
public sealed record LiteResult(double ShiftX, double ShiftY, LiteBox Extents, IReadOnlyDictionary<string, int> Written, int Entities, int Deduped = 0, long DedupedBytes = 0, int Level = -1);

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

    /// <summary>Size of each item for the minimum-size rule: <see cref="SmallFilter.GroupSizes"/> with the 3D footprint boxes kept out of the links.</summary>
    internal static void ChainSizes(IReadOnlyList<LiteItem> items, double snap, double[] size) =>
        SmallFilter.GroupSizes(items.Select(i => i.Entity).ToList(), i => (items[i].Flags & LiteFlags.Footprint) == 0, snap, size);

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
