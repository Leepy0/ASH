using System.Globalization;
using System.Security;
using System.Text;
using ASH.Core.Geometry;
using DocumentFormat.OpenXml.Packaging;
using static ASH.Server.JobPages;

namespace ASH.Server;

/// <summary>
/// The report as a PowerPoint (16:9): 표지, 결론, 안별 결론, 그룹별, 자재별, 병목 지도, 영상 — one slide per section, long tables continue on more slides.
/// Written as DrawingML with DocumentFormat.OpenXml (already here through ClosedXML), no template file. The bottleneck map is drawn with shapes
/// (blocks by heat, paths, numbered top-5 markers), so it stays editable and needs no SVG-to-PNG step on the server.
/// </summary>
internal static class ReportPptx
{
    const long SW = 12192000, SH = 6858000, M = 457200, Top = 1050000, Gap = 228600, BodyW = SW - 2 * M, BodyH = SH - Top - 400000;
    const string Ink = "1D1D1F", Sub = "636366", Line = "E5E5EA", Blue = "007AFF", Orange = "C45500", Red = "D70015", Green = "248A3D";
    const int RowsPerSlide = 14, FilmsPerSlide = 6;
    const string Ns = "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"";

    static string E(string s) => SecurityElement.Escape(s) ?? "";
    static string Color(string c) => c switch { "blue" => Blue, "orange" => Orange, "red" => Red, "green" => Green, _ => Ink };

    sealed record Para(string Text, int Pt, string Rgb = Ink, bool Bold = false, string Align = "l");

    /// <summary>One slide's shapes (ids counted per slide) and the JPEGs it embeds (relationship "rIdImg{n}").</summary>
    sealed class Slide
    {
        readonly StringBuilder sb = new();
        int id = 1;
        public readonly List<byte[]> Images = new();
        int Next() => ++id;

        static string Fill(string? rgb, int alpha = 100) => rgb == null ? "<a:noFill/>" : $"<a:solidFill><a:srgbClr val=\"{rgb}\">{(alpha < 100 ? $"<a:alpha val=\"{alpha * 1000}\"/>" : "")}</a:srgbClr></a:solidFill>";
        static string Ln(string? rgb, long w = 9525) => rgb == null ? "<a:ln><a:noFill/></a:ln>" : $"<a:ln w=\"{w}\" cap=\"rnd\"><a:solidFill><a:srgbClr val=\"{rgb}\"/></a:solidFill></a:ln>";
        static string Xfrm(long x, long y, long w, long h) => $"<a:xfrm><a:off x=\"{x}\" y=\"{y}\"/><a:ext cx=\"{Math.Max(1, w)}\" cy=\"{Math.Max(1, h)}\"/></a:xfrm>";
        static string Run(string text, int pt, string rgb, bool bold) =>
            $"<a:r><a:rPr lang=\"ko-KR\" sz=\"{pt * 100}\"{(bold ? " b=\"1\"" : "")} dirty=\"0\">{Fill(rgb)}<a:latin typeface=\"Malgun Gothic\"/><a:ea typeface=\"Malgun Gothic\"/></a:rPr><a:t>{E(text)}</a:t></a:r>";
        static string P(Para p) => $"<a:p><a:pPr algn=\"{p.Align}\"/>{Run(p.Text, p.Pt, p.Rgb, p.Bold)}</a:p>";

        public Slide Text(long x, long y, long w, long h, IEnumerable<Para> paras, string? fill = null, string? line = null, string geom = "rect", string anchor = "t", bool inset = true)
        {
            var n = Next();
            sb.Append($"<p:sp><p:nvSpPr><p:cNvPr id=\"{n}\" name=\"text {n}\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr>{Xfrm(x, y, w, h)}<a:prstGeom prst=\"{geom}\"><a:avLst/></a:prstGeom>{Fill(fill)}{Ln(line)}</p:spPr>" +
                $"<p:txBody><a:bodyPr wrap=\"square\" anchor=\"{anchor}\"{(inset ? "" : " lIns=\"0\" tIns=\"0\" rIns=\"0\" bIns=\"0\"")}/><a:lstStyle/>{string.Concat(paras.Select(P).DefaultIfEmpty("<a:p/>"))}</p:txBody></p:sp>");
            return this;
        }

        public Slide Title(string t) => Text(M, 300000, BodyW, 600000, new[] { new Para(t, 24, Ink, true) }, anchor: "b");

        /// <summary>A custom-geometry shape whose path coordinates are EMU inside its box (path size = box size).</summary>
        public Slide Geom(long x, long y, long w, long h, string path, string? fill, int alpha, string? line, long lineW = 9525)
        {
            var n = Next();
            sb.Append($"<p:sp><p:nvSpPr><p:cNvPr id=\"{n}\" name=\"map {n}\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr>{Xfrm(x, y, w, h)}" +
                $"<a:custGeom><a:avLst/><a:gdLst/><a:ahLst/><a:cxnLst/><a:rect l=\"0\" t=\"0\" r=\"r\" b=\"b\"/><a:pathLst><a:path w=\"{Math.Max(1, w)}\" h=\"{Math.Max(1, h)}\">{path}</a:path></a:pathLst></a:custGeom>" +
                $"{Fill(fill, alpha)}{Ln(line, lineW)}</p:spPr></p:sp>");
            return this;
        }

        public Slide Picture(byte[] jpg, long x, long y, long w, long h, string descr)
        {
            Images.Add(jpg);
            var n = Next();
            sb.Append($"<p:pic><p:nvPicPr><p:cNvPr id=\"{n}\" name=\"picture {n}\" descr=\"{E(descr)}\"/><p:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>" +
                $"<p:blipFill><a:blip r:embed=\"rIdImg{Images.Count}\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill><p:spPr>{Xfrm(x, y, w, h)}<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:pic>");
            return this;
        }

        /// <summary>A table: header row on light gray, rows coloured by their status (fail red, over orange, under gray, cur blue and bold); column widths follow the text.</summary>
        public Slide Table(string[] head, IReadOnlyList<(string[] Cells, string Cls)> rows, long x, long y, long w)
        {
            const long rowH = 330000;
            static int Wd(string s) => s.Sum(c => c >= 0x1100 ? 2 : 1);
            var weights = head.Select((h, i) => Math.Clamp(rows.Select(r => i < r.Cells.Length ? Wd(r.Cells[i]) : 0).Append(Wd(h)).Max(), 4, 40)).ToList();
            var sum = weights.Sum();
            var cols = weights.Select(k => w * k / sum).ToList();
            string Cell(string text, string bg, string fg, bool bold, int pt, string algn) =>
                $"<a:tc><a:txBody><a:bodyPr/><a:lstStyle/>{P(new Para(text, pt, fg, bold, algn))}</a:txBody><a:tcPr marL=\"45720\" marR=\"45720\" marT=\"22860\" marB=\"22860\" anchor=\"ctr\">" +
                $"<a:lnB w=\"6350\"><a:solidFill><a:srgbClr val=\"{Line}\"/></a:solidFill></a:lnB>{Fill(bg)}</a:tcPr></a:tc>";
            var n = Next();
            sb.Append($"<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id=\"{n}\" name=\"table {n}\"/><p:cNvGraphicFramePr><a:graphicFrameLocks noGrp=\"1\"/></p:cNvGraphicFramePr><p:nvPr/></p:nvGraphicFramePr>" +
                $"<p:xfrm><a:off x=\"{x}\" y=\"{y}\"/><a:ext cx=\"{w}\" cy=\"{rowH * (rows.Count + 1)}\"/></p:xfrm><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/table\"><a:tbl><a:tblPr firstRow=\"1\"/><a:tblGrid>");
            foreach (var c in cols) sb.Append($"<a:gridCol w=\"{c}\"/>");
            sb.Append($"</a:tblGrid><a:tr h=\"{rowH}\">").Append(string.Concat(head.Select((h, i) => Cell(h, "F2F2F4", Sub, true, 10, i == 0 ? "l" : "ctr")))).Append("</a:tr>");
            foreach (var (cells, cls) in rows)
            {
                var (bg, fg) = cls.Contains("fail") ? ("FFE9E7", Red) : cls.Contains("over") ? ("FFF1E0", Orange) : cls.Contains("under") ? ("FFFFFF", Sub) : cls.Contains("cur") ? ("E5F0FF", Ink) : ("FFFFFF", Ink);
                sb.Append($"<a:tr h=\"{rowH}\">").Append(string.Concat(head.Select((_, i) => Cell(i < cells.Length ? cells[i] : "", bg, fg, i == 0 || cls.Contains("cur"), 11, i == 0 ? "l" : "ctr")))).Append("</a:tr>");
            }
            sb.Append("</a:tbl></a:graphicData></a:graphic></p:graphicFrame>");
            return this;
        }

        public string Xml() =>
            $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:sld {Ns}><p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>" +
            $"<p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>{sb}</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>";
    }

    /// <summary>The slides of the report; <paramref name="baseUrl"/> (scheme://host) makes the film links absolute.</summary>
    public static byte[] Build(ReportData r, string baseUrl)
    {
        var a = r.A;
        var slides = new List<Slide>();

        // 표지
        var tags = string.Join(" · ", new[] { a.Corp, a.ProductLine, a.Line }.Where(t => t.Length > 0));
        slides.Add(new Slide().Text(M, 1900000, BodyW, 1100000, new[] { new Para(a.Name, 36, Ink, true) }, anchor: "b")
            .Text(M, 3100000, BodyW, 1800000, new[] { new Para(tags, 16, Sub), new Para($"기준 버전 {r.Version} · 완료 {When(r.Job.FinishedUtc)}", 16), new Para($"작성 {When(DateTime.UtcNow)} · ASH", 14, Sub) }
                .Concat(a.Note.Length > 0 ? new[] { new Para(a.Note, 14, Sub) } : Array.Empty<Para>())));

        // 결론: the number cards
        var s = new Slide().Title("결론");
        long kw = (BodyW - Gap * (r.Kpis.Count - 1)) / r.Kpis.Count;
        for (int i = 0; i < r.Kpis.Count; i++)
        {
            var k = r.Kpis[i];
            s.Text(M + i * (kw + Gap), 1500000, kw, 2300000, new[] { new Para(k.Label, 14, Sub, false, "ctr"), new Para(k.Value + k.Unit, 44, Color(k.Color), true, "ctr"),
                new Para(k.Sub, 12, k.SubColor.Length > 0 ? Color(k.SubColor) : Sub, false, "ctr") }, "FFFFFF", Line, "roundRect", "ctr");
        }
        s.Text(M, 4100000, BodyW, 500000, new[] { new Para($"기준 버전 {r.Version}", 14, Sub, false, "ctr") });
        slides.Add(s);

        void Tables(string title, ReportTable t, string empty)
        {
            if (t.Rows.Count == 0) { slides.Add(new Slide().Title(title).Text(M, Top, BodyW, 600000, new[] { new Para(empty, 16, Sub) })); return; }
            var parts = t.Rows.Chunk(RowsPerSlide).ToList();
            for (int i = 0; i < parts.Count; i++)
                slides.Add(new Slide().Title(parts.Count > 1 ? $"{title} ({i + 1}/{parts.Count})" : title).Table(t.Head, parts[i], M, Top, BodyW));
        }
        Tables("안별 결론", r.Plans, "안이 없습니다.");
        Tables("그룹별 권장 대수", r.Groups, "그룹 결과가 없습니다.");
        Tables("자재별 공급·리드 타임·수량", r.Materials, "자재 결과가 없습니다.");

        // 병목 지도: the map left, the top 5 right
        s = new Slide().Title("병목 지도");
        const long mapW = 7400000;
        Map(s, r, M, Top, mapW, BodyH);
        long lx = M + mapW + Gap, lw = BodyW - mapW - Gap;
        if (r.Top.Count == 0) s.Text(lx, Top, lw, 600000, new[] { new Para("병목 없음", 16, Sub) });
        else s.Table(new[] { "순위", "block", "평균 대기(초)", "점유율(%)" },
            r.Top.Select((b, i) => (new[] { (i + 1).ToString(CultureInfo.InvariantCulture), b.Name, F(b.AvgWaitSec, "0.0"), P(b.Utilization) }, i == 0 ? "fail" : "")).ToList(), lx, Top, lw);
        s.Text(lx, Top + 330000 * 7, lw, 600000, new[] { new Para("block 평균 대기 · 권장 대수 기준 · 색이 진할수록 대기가 깁니다", 11, Sub) });
        slides.Add(s);

        // 영상: thumbnails, 3 x 2 per slide
        if (r.Films.Count == 0) slides.Add(new Slide().Title("영상").Text(M, Top, BodyW, 600000, new[] { new Para("만든 영상이 없습니다.", 16, Sub) }));
        var fparts = r.Films.Chunk(FilmsPerSlide).ToList();
        for (int p = 0; p < fparts.Count; p++)
        {
            s = new Slide().Title(fparts.Count > 1 ? $"영상 ({p + 1}/{fparts.Count})" : "영상");
            long cw = (BodyW - 2 * Gap) / 3, th = cw * 9 / 16, ch = th + 700000;
            for (int i = 0; i < fparts[p].Length; i++)
            {
                var f = fparts[p][i];
                long x = M + i % 3 * (cw + Gap), y = Top + i / 3 * (ch + Gap);
                var jpg = f.Thumb == null ? null : TryRead(f.Thumb);
                if (jpg != null) s.Picture(jpg, x, y, cw, th, $"영상 {When(f.Film.CreatedUtc)}");
                else s.Text(x, y, cw, th, Array.Empty<Para>(), "F2F2F4");
                s.Text(x, y + th, cw, 700000, new[] { new Para($"{When(f.Film.CreatedUtc)} · {f.Main}", 11, Ink, true), new Para($"{baseUrl}/films/{f.Film.Id}", 10, Sub) });
            }
            slides.Add(s);
        }
        return Package(slides);
    }

    static byte[]? TryRead(string path) { try { return File.ReadAllBytes(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; } }

    /// <summary>"rgb(r,g,b)" (JobPages.Heat) as "RRGGBB".</summary>
    internal static string Hex(string rgb) => string.Concat(rgb[4..^1].Split(',').Select(x => int.Parse(x, CultureInfo.InvariantCulture).ToString("X2")));

    /// <summary>The whole layout fitted into the box: blocks filled in 5 heat steps by mean wait, paths in gray, the top 5 as numbered circles (1 red, 2 orange, the rest yellow).</summary>
    static void Map(Slide s, ReportData r, long x, long y, long w, long h)
    {
        var l = r.View.Layout;
        if (l == null || LayoutBounds(l) is not { } bb) { s.Text(x, y, w, 600000, new[] { new Para("레이아웃을 읽지 못했습니다.", 16, Sub) }); return; }
        double pad = Math.Max(1e-6, 0.05 * Math.Max(bb.X1 - bb.X0, bb.Y1 - bb.Y0));
        double x0 = bb.X0 - pad, y1 = bb.Y1 + pad, mw = bb.X1 - bb.X0 + 2 * pad, mh = bb.Y1 - bb.Y0 + 2 * pad;
        double k = Math.Min(w / mw, h / mh); // EMU per model unit
        long cx = (long)(mw * k), cy = (long)(mh * k), ox = x + (w - cx) / 2, oy = y + (h - cy) / 2;
        long PX(double mx) => (long)Math.Round((mx - x0) * k);
        long PY(double my) => (long)Math.Round((y1 - my) * k);
        string Pt(double mx, double my) => $"<a:pt x=\"{Math.Clamp(PX(mx), 0, cx)}\" y=\"{Math.Clamp(PY(my), 0, cy)}\"/>";
        string Rect(Rect2 b) => $"<a:moveTo>{Pt(b.Min.X, b.Min.Y)}</a:moveTo><a:lnTo>{Pt(b.Max.X, b.Min.Y)}</a:lnTo><a:lnTo>{Pt(b.Max.X, b.Max.Y)}</a:lnTo><a:lnTo>{Pt(b.Min.X, b.Max.Y)}</a:lnTo><a:close/>";

        s.Text(ox, oy, cx, cy, Array.Empty<Para>(), "F2F2F4");
        var vals = r.View.Blocks.Where(b => !double.IsNaN(b.AvgWaitSec)).ToDictionary(b => b.Name, b => b.AvgWaitSec, StringComparer.Ordinal);
        double hi = vals.Values.DefaultIfEmpty(0).Max();
        int Step(string name) => vals.TryGetValue(name, out var v) && v > 0 && hi > 0 ? Math.Clamp((int)Math.Ceiling(v / hi * 5), 1, 5) : 0;
        foreach (var g in l.Blocks.GroupBy(b => Step(b.Name)).Where(g => g.Key > 0))
            s.Geom(ox, oy, cx, cy, string.Concat(g.Select(b => Rect(b.Rect))), Hex(Heat(g.Key / 5.0)), 75, null);
        if (l.Blocks.Count > 0) s.Geom(ox, oy, cx, cy, string.Concat(l.Blocks.Select(b => Rect(b.Rect))), null, 100, "C7C7CC");
        var paths = new StringBuilder();
        foreach (var p in l.Paths)
        {
            int n = p.Segments.Any(sg => sg is ArcSeg) ? 16 : 1; double len = p.Length;
            for (int i = 0; i <= n; i++) { var q = p.PointAt(len * i / n); paths.Append(i == 0 ? $"<a:moveTo>{Pt(q.X, q.Y)}</a:moveTo>" : $"<a:lnTo>{Pt(q.X, q.Y)}</a:lnTo>"); }
        }
        if (paths.Length > 0) s.Geom(ox, oy, cx, cy, paths.ToString(), null, 100, "AEAEB2", 28575);

        var rects = l.Blocks.GroupBy(b => b.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Rect, StringComparer.Ordinal);
        string[] col = { Red, "FF9500", "FFCC00", "FFCC00", "FFCC00" };
        const long d = 320000;
        for (int i = r.Top.Count - 1; i >= 0; i--) // the first drawn last, on top
        {
            if (!rects.TryGetValue(r.Top[i].Name, out var rr)) continue;
            long mx = ox + PX((rr.Min.X + rr.Max.X) / 2), my = oy + PY((rr.Min.Y + rr.Max.Y) / 2);
            s.Text(mx - d / 2, my - d / 2, d, d, new[] { new Para((i + 1).ToString(CultureInfo.InvariantCulture), 12, i < 2 ? "FFFFFF" : Ink, true, "ctr") }, col[i], "FFFFFF", "ellipse", "ctr", false);
        }
    }

    static void Feed(OpenXmlPart part, string xml) { using var ms = new MemoryStream(Encoding.UTF8.GetBytes(xml)); part.FeedData(ms); }

    const string Theme = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"ASH\"><a:themeElements>" +
        "<a:clrScheme name=\"ASH\"><a:dk1><a:srgbClr val=\"1D1D1F\"/></a:dk1><a:lt1><a:srgbClr val=\"FFFFFF\"/></a:lt1><a:dk2><a:srgbClr val=\"636366\"/></a:dk2><a:lt2><a:srgbClr val=\"F5F5F7\"/></a:lt2>" +
        "<a:accent1><a:srgbClr val=\"007AFF\"/></a:accent1><a:accent2><a:srgbClr val=\"5856D6\"/></a:accent2><a:accent3><a:srgbClr val=\"C45500\"/></a:accent3><a:accent4><a:srgbClr val=\"248A3D\"/></a:accent4>" +
        "<a:accent5><a:srgbClr val=\"D70015\"/></a:accent5><a:accent6><a:srgbClr val=\"8E8E93\"/></a:accent6><a:hlink><a:srgbClr val=\"0062CC\"/></a:hlink><a:folHlink><a:srgbClr val=\"5856D6\"/></a:folHlink></a:clrScheme>" +
        "<a:fontScheme name=\"ASH\"><a:majorFont><a:latin typeface=\"Malgun Gothic\"/><a:ea typeface=\"Malgun Gothic\"/><a:cs typeface=\"\"/></a:majorFont><a:minorFont><a:latin typeface=\"Malgun Gothic\"/><a:ea typeface=\"Malgun Gothic\"/><a:cs typeface=\"\"/></a:minorFont></a:fontScheme>" +
        "<a:fmtScheme name=\"ASH\"><a:fillStyleLst>" + Phs + Phs + Phs + "</a:fillStyleLst><a:lnStyleLst>" + PhLn + PhLn + PhLn + "</a:lnStyleLst>" +
        "<a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst>" +
        "<a:bgFillStyleLst>" + Phs + Phs + Phs + "</a:bgFillStyleLst></a:fmtScheme></a:themeElements></a:theme>";
    const string Phs = "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>", PhLn = "<a:ln w=\"6350\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln>";
    const string EmptyTree = "<p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/></p:spTree>";

    /// <summary>The package: presentation, one master with one blank layout and the theme, then the slides (with their pictures).</summary>
    static byte[] Package(List<Slide> slides)
    {
        using var ms = new MemoryStream();
        using (var doc = PresentationDocument.Create(ms, DocumentFormat.OpenXml.PresentationDocumentType.Presentation))
        {
            var pp = doc.AddPresentationPart();
            var master = pp.AddNewPart<SlideMasterPart>("rIdM");
            var layout = master.AddNewPart<SlideLayoutPart>("rId1");
            layout.AddPart(master, "rId1");
            var theme = master.AddNewPart<ThemePart>("rId2");
            pp.AddPart(theme, "rIdT");
            Feed(theme, Theme);
            Feed(master, $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:sldMaster {Ns}><p:cSld><p:bg><p:bgPr><a:solidFill><a:srgbClr val=\"FFFFFF\"/></a:solidFill><a:effectLst/></p:bgPr></p:bg>{EmptyTree}</p:cSld>" +
                "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>" +
                "<p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"rId1\"/></p:sldLayoutIdLst><p:txStyles><p:titleStyle/><p:bodyStyle/><p:otherStyle/></p:txStyles></p:sldMaster>");
            Feed(layout, $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:sldLayout {Ns} type=\"blank\" preserve=\"1\"><p:cSld name=\"Blank\">{EmptyTree}</p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>");
            var ids = new StringBuilder();
            for (int i = 0; i < slides.Count; i++)
            {
                var sp = pp.AddNewPart<SlidePart>($"rIdS{i + 1}");
                sp.AddPart(layout, "rId1");
                for (int k = 0; k < slides[i].Images.Count; k++)
                    using (var img = new MemoryStream(slides[i].Images[k])) sp.AddImagePart(ImagePartType.Jpeg, $"rIdImg{k + 1}").FeedData(img);
                Feed(sp, slides[i].Xml());
                ids.Append($"<p:sldId id=\"{256 + i}\" r:id=\"rIdS{i + 1}\"/>");
            }
            Feed(pp, $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:presentation {Ns}><p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rIdM\"/></p:sldMasterIdLst>" +
                $"<p:sldIdLst>{ids}</p:sldIdLst><p:sldSz cx=\"{SW}\" cy=\"{SH}\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/></p:presentation>");
        }
        return ms.ToArray();
    }
}
