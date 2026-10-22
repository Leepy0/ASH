using System.Text;
using System.Text.Json;
using ASH.Core.Dxf;
using ASH.Core.Geometry;
using ASH.Core.Runner;

namespace ASH.Server;

/// <summary>drawing.json for the cut editor: the user's DXF (segments and labels) plus the AGV layout, all in model metres.</summary>
public static class DrawingJson
{
    public const int MaxSegs = 200_000;
    /// <summary>Bump when the format or the DXF reader changes, so older cached files are rebuilt.</summary>
    public const int Version = 2;
    static string Head => "{\"v\":" + Version + ",";

    /// <summary>Builds the JSON, or reads the cache next to job.json; a result with drawingError is not cached (the cause may be transient).</summary>
    public static string? Cached(string jobDir, string project, string model)
    {
        var cache = Path.Combine(jobDir, "drawing.json");
        try { if (File.Exists(cache) && File.ReadAllText(cache, Encoding.UTF8) is var c && c.StartsWith(Head, StringComparison.Ordinal)) return c; }
        catch (IOException) { } catch (UnauthorizedAccessException) { } // unreadable cache: build it again
        var json = Build(project, model);
        if (json != null && !json.Contains("\"drawingError\""))
            try { var tmp = cache + ".tmp"; File.WriteAllText(tmp, json, new UTF8Encoding(false)); File.Move(tmp, cache, true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        return json;
    }

    /// <summary>Not cached. Null when the model folder (agv.asy) is gone. A missing or unreadable DXF/frame gives empty segs/texts, with "drawingError" when it was unreadable.</summary>
    public static string? Build(string project, string model)
    {
        ASH.Core.Layout.Layout layout;
        try { layout = ArcLayout.Load(Path.Combine(project, model + ".arc")).Layout; }
        catch (Exception e) when (e is IOException or FormatException or InvalidDataException) { return null; }

        DxfDrawing d = new(Array.Empty<DxfSeg>(), Array.Empty<DxfText>(), Array.Empty<string>());
        string? err = null;
        var dxf = Path.Combine(project, "drawing.dxf"); var frame = Path.Combine(project, ModelFrame.FileName);
        if (File.Exists(dxf) && File.Exists(frame))
            try
            {
                using var r = new StreamReader(dxf, Encoding.UTF8);
                d = DxfLite.ToModel(DxfLite.Read(r), ModelFrame.Read(frame));
                static bool Ok(params double[] v) => v.All(x => !double.IsNaN(x) && !double.IsInfinity(x)); // a huge coordinate times the scale can overflow
                d = d with { Segs = d.Segs.Where(s => Ok(s.X1, s.Y1, s.X2, s.Y2)).ToList(), Texts = d.Texts.Where(t => Ok(t.X, t.Y, t.Height)).ToList() };
            }
            catch (Exception e) when (e is IOException or FormatException or InvalidDataException) { err = e.Message == "binary DXF" ? "도면이 바이너리 DXF라서 읽을 수 없습니다. ASCII DXF로 저장해 주세요." : "도면을 읽을 수 없습니다: " + e.Message; }

        var segs = d.Segs; bool truncated = d.Truncated || segs.Count > MaxSegs;
        if (segs.Count > MaxSegs) segs = segs.OrderByDescending(s => (s.X2 - s.X1) * (s.X2 - s.X1) + (s.Y2 - s.Y1) * (s.Y2 - s.Y1)).Take(MaxSegs).ToList();
        var li = d.Layers.Select((n, i) => (n, i)).ToDictionary(p => p.n, p => p.i);
        double minx = double.MaxValue, miny = double.MaxValue, maxx = double.MinValue, maxy = double.MinValue;
        void B(double x, double y) { minx = Math.Min(minx, x); maxx = Math.Max(maxx, x); miny = Math.Min(miny, y); maxy = Math.Max(maxy, y); }

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            void N(double v) => w.WriteNumberValue(Math.Round(v, 3));
            w.WriteStartObject();
            w.WriteNumber("v", Version);
            w.WriteStartArray("segs");
            foreach (var s in segs) { w.WriteStartArray(); N(s.X1); N(s.Y1); N(s.X2); N(s.Y2); w.WriteNumberValue(li[s.Layer]); w.WriteEndArray(); B(s.X1, s.Y1); B(s.X2, s.Y2); }
            w.WriteEndArray();
            w.WriteStartArray("texts");
            foreach (var t in d.Texts) { w.WriteStartArray(); N(t.X); N(t.Y); N(t.Height); w.WriteStringValue(t.Text); w.WriteNumberValue(li[t.Layer]); w.WriteEndArray(); B(t.X, t.Y); }
            w.WriteEndArray();
            w.WriteStartArray("layers"); foreach (var l in d.Layers) w.WriteStringValue(l); w.WriteEndArray();
            w.WriteStartArray("paths");
            foreach (var p in layout.Paths)
            {
                w.WriteStartObject(); w.WriteString("name", p.Name); w.WriteBoolean("twoWay", p.IsTwoWay); w.WriteStartArray("pts");
                void P(Point2 q) { w.WriteStartArray(); N(q.X); N(q.Y); w.WriteEndArray(); B(q.X, q.Y); }
                if (p.Segments.Count > 0) P(p.Segments[0].Start);
                foreach (var g in p.Segments)
                {
                    int k = g is ArcSeg a ? Math.Max(1, (int)Math.Ceiling(Math.Abs(a.SweepDeg) / 10)) : 1; // arcs in 10-degree steps
                    for (int i = 1; i <= k; i++) P(g.PointAt(g.Length * i / k));
                }
                w.WriteEndArray(); w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("cps");
            foreach (var c in layout.Cps) { w.WriteStartObject(); w.WriteString("name", c.Name); w.WriteNumber("x", Math.Round(c.Position.X, 3)); w.WriteNumber("y", Math.Round(c.Position.Y, 3)); w.WriteEndObject(); B(c.Position.X, c.Position.Y); }
            w.WriteEndArray();
            w.WriteStartArray("blocks");
            foreach (var b in layout.Blocks)
            {
                w.WriteStartObject(); w.WriteString("name", b.Name); w.WriteNumber("cap", b.Cap); w.WriteStartArray("rect");
                N(b.Rect.Min.X); N(b.Rect.Min.Y); N(b.Rect.Max.X); N(b.Rect.Max.Y); w.WriteEndArray(); w.WriteEndObject(); B(b.Rect.Min.X, b.Rect.Min.Y); B(b.Rect.Max.X, b.Rect.Max.Y);
            }
            w.WriteEndArray();
            w.WriteStartArray("bounds"); if (minx > maxx) { minx = miny = maxx = maxy = 0; } N(minx); N(miny); N(maxx); N(maxy); w.WriteEndArray();
            w.WriteBoolean("truncated", truncated);
            if (err != null) w.WriteString("drawingError", err);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
