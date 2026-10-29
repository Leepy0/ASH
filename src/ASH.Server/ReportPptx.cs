using System.Globalization;
using System.Security;
using System.Text;
using ASH.Core.Geometry;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using static ASH.Server.JobPages;

namespace ASH.Server;

/// <summary>
/// The report as a PowerPoint (16:9, Samsung tone: Samsung Blue as the point colour, grays for the rest): 표지, 요약, 안별 비교, 그룹별 부하,
/// 시간 내역, 자재 공급, 병목, 영상, then the detail tables as 부록. Every content slide has a section label, an action title worked out from the
/// numbers (the slide's conclusion), its content and a footer. Charts are native PowerPoint charts with an embedded workbook (editable in
/// PowerPoint); the bottleneck map is drawn with shapes. Written as DrawingML with DocumentFormat.OpenXml and ClosedXML (both already here), no template file.
/// </summary>
internal static class ReportPptx
{
    // ---- page grid (EMU; 914400 per inch): 13.333 x 7.5 in
    const long SW = 12192000, SH = 6858000;
    static long In(double inch) => (long)Math.Round(inch * 914400);
    static readonly long M = In(0.5), BodyW = SW - 2 * M, Top = In(1.65), Bottom = In(6.85), BodyH = Bottom - Top, Gap = In(0.3);

    // ---- palette
    const string Blue = "1428A0", Navy = "0B1A5C", Tint = "E8ECF8", Ink = "1D1D1F", Sub = "5F6368", Ter = "9AA0A6", Rule = "E5E7EB", Panel = "F5F6F8",
        Orange = "E37222", Red = "D93025", Green = "1E8E3E", Gray = "C4C8CF", White = "FFFFFF";
    /// <summary>Block wait, short to long: blue, orange, red.</summary>
    static readonly string[] Heat5 = { "B4C3F0", "6F86DB", "F2B27A", Orange, Red };
    const string Num = "Segoe UI Semibold", Light = "Segoe UI Light";

    const int RowsPerSlide = 14;
    const string NsA = "http://schemas.openxmlformats.org/drawingml/2006/main", NsR = "http://schemas.openxmlformats.org/officeDocument/2006/relationships",
        NsC = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    const string Ns = "xmlns:a=\"" + NsA + "\" xmlns:r=\"" + NsR + "\" xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"";

    static string E(string s) => SecurityElement.Escape(s) ?? "";
    static string Inv(double d) => d.ToString("R", CultureInfo.InvariantCulture);
    static string Color(string c) => c switch { "blue" => Blue, "orange" => Orange, "red" => Red, "green" => Green, _ => Ink };
    static string Pc(double u) => double.IsNaN(u) ? "-" : F(u * 100, "0") + "%";
    /// <summary>Over the limit on average, or only in some confirm runs (the mean itself under it).</summary>
    static string OverWhy(GroupView g, double lim) => g.Util > lim ? "초과" : "초과 실행 있음";

    /// <summary>Rough text width (Malgun Gothic for Hangul, Segoe UI for the rest), for column widths and truncation.</summary>
    static long TextW(string s, double pt) => (long)(s.Sum(c => c >= 0x1100 ? 0.98 : c == ' ' ? 0.28 : char.IsUpper(c) ? 0.7 : c is '.' or ',' or ':' or '·' ? 0.3 : 0.56) * pt * 12700);
    static string Fit(string s, double pt, long w)
    {
        if (TextW(s, pt) <= w) return s;
        while (s.Length > 1 && TextW(s + "…", pt) > w) s = s[..^1];
        return s + "…";
    }

    sealed record Run(string Text, double Pt, string Rgb = Ink, bool Bold = false, string? Font = null, int Link = 0);
    sealed record Para(IReadOnlyList<Run> Runs, string Align = "l", int Before = 0);
    static Para T(string text, double pt, string rgb = Ink, bool bold = false, string align = "l", string? font = null, int before = 0) =>
        new(new[] { new Run(text, pt, rgb, bold, font) }, align, before);

    /// <summary>A bar series: values, colour, colours per point (null = the series colour), the data label format (null = none) and position,
    /// points whose label is hidden.</summary>
    sealed record Ser(string Name, double[] Vals, string Rgb, string?[]? Rgbs = null, string? Fmt = null, string Pos = "outEnd", string LabelRgb = Ink, bool LabelBold = false, bool[]? Hide = null);

    /// <summary>A bar chart: horizontal (Bar) or columns, clustered or percentStacked, value axis 0..Max (hidden: the bars carry labels),
    /// the plot area at fixed fractions of the frame so shapes drawn over it (limit lines) land where the values are.</summary>
    sealed record Chart(string[] Cats, Ser[] Series, bool Bar, string Grouping = "clustered", double? Max = null, bool Legend = false, int Gap = 60, int Overlap = 0,
        double PX = 0.02, double PY = 0.02, double PW = 0.96, double PH = 0.88, double CatPt = 12);

    /// <summary>One slide's shapes (ids counted per slide), the JPEGs ("rIdImg{n}"), charts ("rIdC{n}") and link targets ("rIdL{n}") it uses.</summary>
    sealed class Slide
    {
        readonly StringBuilder sb = new();
        int id = 1;
        public readonly List<byte[]> Images = new();
        public readonly List<Chart> Charts = new();
        public readonly List<string> Links = new();
        public bool Cover;
        int Next() => ++id;

        public int Link(string url) { Links.Add(url); return Links.Count; }

        static string Fill(string? rgb, int alpha = 100) => rgb == null ? "<a:noFill/>" : $"<a:solidFill><a:srgbClr val=\"{rgb}\">{(alpha < 100 ? $"<a:alpha val=\"{alpha * 1000}\"/>" : "")}</a:srgbClr></a:solidFill>";
        static string Ln(string? rgb, long w = 9525, bool dash = false) => rgb == null ? "<a:ln><a:noFill/></a:ln>"
            : $"<a:ln w=\"{w}\"><a:solidFill><a:srgbClr val=\"{rgb}\"/></a:solidFill>{(dash ? "<a:prstDash val=\"dash\"/>" : "")}</a:ln>";
        static string Xfrm(long x, long y, long w, long h) => $"<a:xfrm><a:off x=\"{x}\" y=\"{y}\"/><a:ext cx=\"{Math.Max(1, w)}\" cy=\"{Math.Max(1, h)}\"/></a:xfrm>";
        static string Geo(string geom) => geom == "roundRect" ? "<a:prstGeom prst=\"roundRect\"><a:avLst><a:gd name=\"adj\" fmla=\"val 6000\"/></a:avLst></a:prstGeom>" : $"<a:prstGeom prst=\"{geom}\"><a:avLst/></a:prstGeom>";
        internal static string R(Run r) =>
            $"<a:r><a:rPr lang=\"ko-KR\" altLang=\"en-US\" sz=\"{(int)Math.Round(r.Pt * 100)}\"{(r.Bold ? " b=\"1\"" : "")} dirty=\"0\">{Fill(r.Rgb)}<a:latin typeface=\"{r.Font ?? "+mn-lt"}\"/><a:ea typeface=\"+mn-ea\"/>" +
            $"{(r.Link > 0 ? $"<a:hlinkClick r:id=\"rIdL{r.Link}\"/>" : "")}</a:rPr><a:t>{E(r.Text)}</a:t></a:r>";
        internal static string P(Para p) => $"<a:p><a:pPr algn=\"{p.Align}\">{(p.Before > 0 ? $"<a:spcBef><a:spcPts val=\"{p.Before * 100}\"/></a:spcBef>" : "")}</a:pPr>{string.Concat(p.Runs.Select(R))}</a:p>";

        public Slide Text(long x, long y, long w, long h, IEnumerable<Para> paras, string? fill = null, string? line = null, string geom = "rect", string anchor = "t", bool inset = false, int alpha = 100)
        {
            var n = Next();
            sb.Append($"<p:sp><p:nvSpPr><p:cNvPr id=\"{n}\" name=\"shape {n}\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr>{Xfrm(x, y, w, h)}{Geo(geom)}{Fill(fill, alpha)}{Ln(line)}</p:spPr>" +
                $"<p:txBody><a:bodyPr wrap=\"square\" anchor=\"{anchor}\"{(inset ? " lIns=\"137160\" tIns=\"91440\" rIns=\"137160\" bIns=\"91440\"" : " lIns=\"0\" tIns=\"0\" rIns=\"0\" bIns=\"0\"")}/><a:lstStyle/>" +
                $"{string.Concat(paras.Select(P).DefaultIfEmpty("<a:p><a:endParaRPr lang=\"ko-KR\"/></a:p>"))}</p:txBody></p:sp>");
            return this;
        }
        public Slide Text(long x, long y, long w, long h, Para p, string anchor = "t") => Text(x, y, w, h, new[] { p }, anchor: anchor);
        public Slide Box(long x, long y, long w, long h, string? fill, string? line = null, string geom = "rect", int alpha = 100) => Text(x, y, w, h, Array.Empty<Para>(), fill, line, geom, alpha: alpha);

        /// <summary>A horizontal or vertical line.</summary>
        public Slide Line(long x1, long y1, long x2, long y2, string rgb, long w = 12700, bool dash = false)
        {
            var n = Next();
            sb.Append($"<p:cxnSp><p:nvCxnSpPr><p:cNvPr id=\"{n}\" name=\"line {n}\"/><p:cNvCxnSpPr/><p:nvPr/></p:nvCxnSpPr><p:spPr>{Xfrm(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1))}" +
                $"<a:prstGeom prst=\"line\"><a:avLst/></a:prstGeom>{Ln(rgb, w, dash)}</p:spPr></p:cxnSp>");
            return this;
        }

        /// <summary>A custom-geometry shape whose path coordinates are EMU inside its box (path size = box size).</summary>
        public Slide Geom(long x, long y, long w, long h, string path, string? fill, int alpha, string? line, long lineW = 9525)
        {
            var n = Next();
            sb.Append($"<p:sp><p:nvSpPr><p:cNvPr id=\"{n}\" name=\"map {n}\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr>{Xfrm(x, y, w, h)}" +
                $"<a:custGeom><a:avLst/><a:gdLst/><a:ahLst/><a:cxnLst/><a:rect l=\"0\" t=\"0\" r=\"r\" b=\"b\"/><a:pathLst><a:path w=\"{Math.Max(1, w)}\" h=\"{Math.Max(1, h)}\">{path}</a:path></a:pathLst></a:custGeom>" +
                $"{Fill(fill, alpha)}{Ln(line, lineW)}</p:spPr></p:sp>");
            return this;
        }

        public Slide Picture(byte[] jpg, long x, long y, long w, long h, string descr, int link = 0)
        {
            Images.Add(jpg);
            var n = Next();
            sb.Append($"<p:pic><p:nvPicPr><p:cNvPr id=\"{n}\" name=\"picture {n}\" descr=\"{E(descr)}\">{(link > 0 ? $"<a:hlinkClick r:id=\"rIdL{link}\"/>" : "")}</p:cNvPr><p:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>" +
                $"<p:blipFill><a:blip r:embed=\"rIdImg{Images.Count}\"/><a:srcRect/><a:stretch><a:fillRect/></a:stretch></p:blipFill><p:spPr>{Xfrm(x, y, w, h)}{Geo("roundRect")}{Ln(Rule)}</p:spPr></p:pic>");
            return this;
        }

        public Slide AddChart(Chart c, long x, long y, long w, long h)
        {
            Charts.Add(c);
            var n = Next();
            sb.Append($"<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id=\"{n}\" name=\"chart {n}\"/><p:cNvGraphicFramePr/><p:nvPr/></p:nvGraphicFramePr>" +
                $"<p:xfrm><a:off x=\"{x}\" y=\"{y}\"/><a:ext cx=\"{w}\" cy=\"{h}\"/></p:xfrm><a:graphic><a:graphicData uri=\"{NsC}\"><c:chart xmlns:c=\"{NsC}\" r:id=\"rIdC{Charts.Count}\"/></a:graphicData></a:graphic></p:graphicFrame>");
            return this;
        }

        /// <summary>A table: header in Samsung blue on the light tint (unit "(…)" on a second line), zebra rows, no grid; columns as wide as their text,
        /// the widest one shortened when the row does not fit. First column left, the rest centred. Row text: fail red, over orange, under gray, cur bold.
        /// Returns the height.</summary>
        public long Table(string[] head, IReadOnlyList<(string[] Cells, string Cls)> rows, long x, long y, long w, double pt = 11, long rowH = 0, string[]? leftCols = null)
        {
            if (rowH == 0) rowH = In(0.32);
            long headH = head.Any(h => h.Contains('(')) ? In(0.52) : rowH;
            long pad = In(0.2);
            (string Name, string Unit) Split(string h) { var i = h.IndexOf('('); return i > 0 ? (h[..i].Trim(), h[i..]) : (h, ""); }
            var need = head.Select((h, i) => rows.Select(r => i < r.Cells.Length ? TextW(r.Cells[i], pt) : 0).Append(TextW(Split(h).Name, pt)).Append(TextW(Split(h).Unit, pt)).Max() + pad).ToList();
            var cols = need.ToList();
            if (cols.Sum() > w) { var wide = cols.IndexOf(cols.Max()); cols[wide] = Math.Max(In(0.6), w - (cols.Sum() - cols[wide])); }
            else { var extra = (w - cols.Sum()) / cols.Count; for (int i = 0; i < cols.Count; i++) cols[i] += extra; }
            bool Left(int i) => i == 0 || leftCols != null && leftCols.Contains(head[i]);
            const string none = "<a:lnL><a:noFill/></a:lnL><a:lnR><a:noFill/></a:lnR><a:lnT><a:noFill/></a:lnT>";
            string Cell(IEnumerable<Para> ps, string? bg, string lnB) =>
                $"<a:tc><a:txBody><a:bodyPr/><a:lstStyle/>{string.Concat(ps.Select(P))}</a:txBody><a:tcPr marL=\"64008\" marR=\"64008\" marT=\"27432\" marB=\"27432\" anchor=\"ctr\">" +
                $"{none}{lnB}{Fill(bg)}</a:tcPr></a:tc>";
            var n = Next();
            sb.Append($"<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id=\"{n}\" name=\"table {n}\"/><p:cNvGraphicFramePr><a:graphicFrameLocks noGrp=\"1\"/></p:cNvGraphicFramePr><p:nvPr/></p:nvGraphicFramePr>" +
                $"<p:xfrm><a:off x=\"{x}\" y=\"{y}\"/><a:ext cx=\"{cols.Sum()}\" cy=\"{headH + rowH * rows.Count}\"/></p:xfrm><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/table\"><a:tbl><a:tblPr firstRow=\"1\"/><a:tblGrid>");
            foreach (var c in cols) sb.Append($"<a:gridCol w=\"{c}\"/>");
            var blueLine = $"<a:lnB w=\"12700\"><a:solidFill><a:srgbClr val=\"{Blue}\"/></a:solidFill></a:lnB>";
            sb.Append($"</a:tblGrid><a:tr h=\"{headH}\">").Append(string.Concat(head.Select((h, i) =>
            {
                var (name, unit) = Split(h);
                var ps = new List<Para> { T(name, pt, Blue, true, Left(i) ? "l" : "ctr") };
                if (unit.Length > 0) ps.Add(T(unit, pt, Sub, false, Left(i) ? "l" : "ctr"));
                return Cell(ps, Tint, blueLine);
            }))).Append("</a:tr>");
            for (int r = 0; r < rows.Count; r++)
            {
                var (cells, cls) = rows[r];
                var fg = cls.Contains("fail") ? Red : cls.Contains("over") ? Orange : cls.Contains("under") ? Sub : Ink;
                var bold = cls.Contains("cur");
                sb.Append($"<a:tr h=\"{rowH}\">").Append(string.Concat(head.Select((_, i) =>
                    Cell(new[] { T(Fit(i < cells.Length ? cells[i] : "", pt, cols[i] - pad / 2), pt, fg, bold || i == 0, Left(i) ? "l" : "ctr") }, r % 2 == 1 ? Panel : null, "<a:lnB><a:noFill/></a:lnB>")))).Append("</a:tr>");
            }
            sb.Append("</a:tbl></a:graphicData></a:graphic></p:graphicFrame>");
            return headH + rowH * rows.Count;
        }

        /// <summary>Section label, action title (the slide's conclusion) and the rule under it.</summary>
        public Slide Head(string section, string title)
        {
            Text(M, In(0.36), BodyW, In(0.3), T(section, 12, Blue, true));
            Text(M, In(0.66), BodyW, In(0.72), T(title, title.Length > 44 ? 20 : 22, Ink, true), "t");
            Line(M, In(1.42), SW - M, In(1.42), Rule, 9525);
            return this;
        }

        public void Footer(string name, int page, int pages)
        {
            Text(M, In(7.1), In(8), In(0.25), T(name, 10, Ter));
            Text(SW - M - In(3), In(7.1), In(3), In(0.25), T($"{page} / {pages}   ·   ASH", 10, Ter, false, "r"));
            Box(0, SH - 57150, In(1.6), 57150, Blue);
        }

        public string Xml() =>
            $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:sld {Ns}><p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>" +
            $"<p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>{sb}</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>";
    }

    /// <summary>A chart frame plus its plot area (inner, EMU) for drawing over it.</summary>
    static (long X, long Y, long W, long H) Plot(Chart c, long x, long y, long w, long h) => (x + (long)(c.PX * w), y + (long)(c.PY * h), (long)(c.PW * w), (long)(c.PH * h));

    /// <summary>The plot's left fraction that fits the longest category label of a horizontal bar chart.</summary>
    static double LabelRoom(IEnumerable<string> cats, double pt, long w) => Math.Min(0.4, Math.Max(In(0.8), cats.Select(c => TextW(c, pt)).DefaultIfEmpty(0).Max() * 1.15 + In(0.4)) / (double)w);

    /// <summary>The slides of the report; <paramref name="baseUrl"/> (scheme://host) makes the film links absolute.</summary>
    public static byte[] Build(ReportData r, string baseUrl)
    {
        var a = r.A; var v = r.View;
        var lim = v.UtilMax;
        var slides = new List<Slide>();
        int sec = 0;
        Slide New(string section, string title) { var s = new Slide().Head($"{++sec:00} · {section}", title); slides.Add(s); return s; }

        var plan = AnalysisPages.PlanName(a, r.Job);
        var total = r.Job.Decided?.Values.Sum() ?? 0;
        var stop = v.Materials.Select(m => m.StopSec).Where(s => !double.IsNaN(s)).DefaultIfEmpty(double.NaN).Sum();
        var byGroup = v.VehicleGroups.ToDictionary(x => x.Group, StringComparer.Ordinal);
        var loaded = v.Groups.Where(g => g.HasUtil).OrderByDescending(g => g.Util).ToList();
        var top = loaded.FirstOrDefault();
        var fails = v.Groups.Where(g => AnalysisPages.GroupClass(g, lim) == "fail").ToList();
        string Supply(GroupView g) => byGroup.TryGetValue(g.Group, out var vg) ? $"공급 {F(vg.Jobs * vg.Count, "#,0")}회 · {F(vg.Distance * vg.Count / 1000, "0.0")} km" : "";

        // ---- 표지
        Cover(slides, r, v);

        // ---- 요약: the number cards and three findings
        string headline;
        if (stop > 0) headline = $"{plan} {total}대 — 자재 품절 {F(stop, "#,0")}초 발생, 대수 재검토 필요";
        else if (fails.Count > 0) headline = $"{plan} {total}대 — {fails[0].Group} 판정 실패, 대수 재검토 필요";
        else if (top == null) headline = $"{plan} {total}대로 자재 품절 없이 운영 가능";
        else if (Over(top, lim)) headline = $"{plan} {total}대로 자재 품절 없이 운영 가능 — {top.Group} 가동률 {Pc(top.Util)}, 상한({Pc(lim)}) {OverWhy(top, lim)}";
        else if (top.Util >= lim - 0.05) headline = $"{plan} {total}대로 자재 품절 없이 운영 가능 — {top.Group}만 가동률 상한({Pc(lim)}) 근접";
        else headline = $"{plan} {total}대로 자재 품절 없이 운영 가능 — 모든 그룹 가동률 상한({Pc(lim)}) 이하";
        var s = New("요약", headline);
        long cw = (BodyW - Gap * (r.Kpis.Count - 1)) / Math.Max(1, r.Kpis.Count), ch = In(2.05);
        for (int i = 0; i < r.Kpis.Count; i++)
        {
            var k = r.Kpis[i]; long x = M + i * (cw + Gap);
            s.Box(x, Top, cw, ch, White, Rule, "roundRect");
            s.Box(x + In(0.25), Top, In(0.5), 38100, k.Color == "blue" ? Blue : Ter);
            s.Text(x + In(0.25), Top + In(0.22), cw - In(0.5), In(0.3), T(k.Label, 13, Sub));
            s.Text(x + In(0.25), Top + In(0.55), cw - In(0.5), In(0.85), new[] { new Para(new[] { new Run(k.Value, 44, Color(k.Color), false, Num), new Run(" " + k.Unit, 18, Sub) }) }, anchor: "b");
            s.Text(x + In(0.25), Top + In(1.5), cw - In(0.5), In(0.4), T(Fit(k.Sub, 12, cw - In(0.5)), 12, k.SubColor.Length > 0 ? Color(k.SubColor) : Sub));
        }
        s.Text(M, Top + ch + In(0.3), BodyW, In(0.35), T("핵심 발견", 14, Ink, true));
        var finds = Findings(r, v, plan, total, top, fails, Supply);
        for (int i = 0; i < finds.Count; i++)
        {
            long y = Top + ch + In(0.78) + i * In(0.8);
            s.Box(M, y, BodyW, In(0.66), Panel, null, "roundRect");
            s.Box(M + In(0.28), y + In(0.24), In(0.18), In(0.18), finds[i].Rgb, null, "ellipse");
            s.Text(M + In(0.65), y, BodyW - In(0.9), In(0.66), new[] { new Para(new[] { new Run(finds[i].Lead, 15, Ink, true), new Run(finds[i].Rest, 14, Sub) }) }, anchor: "ctr");
        }

        // ---- 안별 비교 (or the plan's versions)
        if (r.Compare.Count >= 2) Compare(New(r.ByPlan ? "안별 비교" : "버전 비교", CompareTitle(r)), r);

        // ---- 그룹별 부하
        if (loaded.Count > 0)
        {
            var over = loaded.Where(g => Over(g, lim)).ToList();
            var title = fails.Count > 0 ? $"{fails.Count}개 그룹 판정 실패 — {fails[0].Group}: {fails[0].Reason}"
                : over.Count == 1 ? $"{over[0].Group} 가동률 {Pc(over[0].Util)}, 상한({Pc(lim)}) {OverWhy(over[0], lim)} — 나머지 {loaded.Count - 1}개 그룹은 상한 이하"
                : over.Count > 0 ? $"{over.Count}개 그룹 가동률 상한({Pc(lim)}) 초과 — 최대 {over[0].Group} {Pc(over[0].Util)}, 나머지 {loaded.Count - over.Count}개는 상한 이하"
                : $"모든 그룹 가동률 상한({Pc(lim)}) 이하 — 최대 {top!.Group} {Pc(top.Util)}";
            Groups(New("그룹별 부하", title), r, loaded, Supply);
        }

        // ---- 시간 내역 (when the model writes where the time went)
        var split = loaded.Where(g => byGroup.TryGetValue(g.Group, out var vg) && vg.Split is { Window: > 0 }).Take(12).ToList();
        if (split.Count > 0) TimeSplitSlide(slides, ref sec, split.Select(g => (g.Group, byGroup[g.Group].Split!)).ToList(), v);

        // ---- 자재 공급
        var mats = v.Materials.Where(m => !double.IsNaN(m.DelivAvg)).ToList();
        if (mats.Count > 0)
        {
            var outs = mats.Where(m => m.StopSec > 0).OrderByDescending(m => m.StopSec).ToList();
            var longest = mats.OrderByDescending(m => m.DelivAvg).First();
            var title = outs.Count > 0 ? $"{outs.Count}개 자재 품절 — {MatLabel(v, outs[0])} {F(outs[0].StopSec, "#,0")}초로 최대"
                : $"자재 품절 없음 — 리드 타임 최장 {MatLabel(v, longest)} 평균 {F(longest.DelivAvg, "0")}초 · 최대 {F(longest.DelivMax, "0")}초";
            Materials(New("자재 공급", title), v, mats, outs);
        }

        // ---- 병목
        {
            var t = r.Top;
            var title = t.Count == 0 ? "병목 없음 — 모든 block 평균 대기 0초"
                : $"{t[0].Name} 평균 대기 {F(t[0].AvgWaitSec, "0.0")}초로 최대 병목{(t.Count > 1 ? $" — 2위 {t[1].Name} {F(t[1].AvgWaitSec, "0.0")}초의 {F(t[0].AvgWaitSec / Math.Max(1e-9, t[1].AvgWaitSec), "0.0")}배" : "")}";
            Bottleneck(New("병목", title), r);
        }

        // ---- 영상
        var fparts = r.Films.Chunk(8).ToList();
        for (int p = 0; p < fparts.Count; p++)
        {
            Films(New("영상", $"영상 {r.Films.Count}건 — 썸네일을 누르면 영상 페이지가 열립니다{(fparts.Count > 1 ? $" ({p + 1}/{fparts.Count})" : "")}"), fparts[p], baseUrl);
        }

        // ---- 부록: the detail tables
        void Appendix(string what, string title, ReportTable t, string[]? left = null)
        {
            var n = (t.Rows.Count + RowsPerSlide - 1) / RowsPerSlide; // slides, rows spread evenly over them
            var parts = t.Rows.Chunk((t.Rows.Count + n - 1) / n).ToList();
            for (int i = 0; i < parts.Count; i++)
            {
                var sl = new Slide().Head("부록 · " + what, parts.Count > 1 ? $"{title} ({i + 1}/{parts.Count})" : title);
                sl.Table(t.Head, parts[i], M, Top, BodyW, 11, 0, left);
                slides.Add(sl);
            }
        }
        string[] gLeft = { "AGV 타입", "판정" }, mLeft = { "그룹" };
        int gn = r.Groups.Rows.Count, mn = r.Materials.Rows.Count;
        if (gn > 0 && mn > 0 && gn + mn <= 10)
        {
            // both short: one slide, groups then materials
            var sl = new Slide().Head("부록 · 상세 표", $"그룹 {gn}개 · 자재 {mn}개 상세 — 문제 먼저");
            sl.Text(M, Top, BodyW, In(0.3), T("그룹", 13, Sub, true));
            var y = Top + In(0.35) + sl.Table(r.Groups.Head, r.Groups.Rows, M, Top + In(0.35), BodyW, 11, 0, gLeft) + In(0.3);
            sl.Text(M, y, BodyW, In(0.3), T("자재", 13, Sub, true));
            sl.Table(r.Materials.Head, r.Materials.Rows, M, y + In(0.35), BodyW, 11, 0, mLeft);
            slides.Add(sl);
        }
        else
        {
            if (gn > 0) Appendix("그룹 상세", $"그룹 {gn}개 상세 — 문제 그룹 먼저", r.Groups, gLeft);
            if (mn > 0) Appendix("자재 상세", $"자재 {mn}개 공급·리드 타임·수량", r.Materials, mLeft);
        }

        for (int i = 0; i < slides.Count; i++) if (!slides[i].Cover) slides[i].Footer(a.Name, i + 1, slides.Count);
        return Package(slides);
    }

    /// <summary>Left 60 % Samsung Blue with the name, tags, version and dates; right the three headline numbers.</summary>
    static void Cover(List<Slide> slides, ReportData r, JobView v)
    {
        var a = r.A;
        var s = new Slide { Cover = true };
        long lw = In(8.0);
        s.Box(0, 0, lw, SH, Blue).Box(lw, 0, SW - lw, SH, Panel);
        s.Text(In(0.8), In(0.75), In(6.4), In(0.35), T("AGV 대수 산정 보고서", 14, Tint, true));
        s.Text(In(0.8), In(1.3), In(6.6), In(2.3), T(a.Name, 36, White, true), "b");
        var tags = string.Join("  ·  ", new[] { a.Corp, a.ProductLine, a.Line }.Where(t => t.Length > 0));
        if (tags.Length > 0) s.Text(In(0.8), In(3.8), In(6.6), In(0.4), T(tags, 16, Tint));
        s.Line(In(0.8), In(4.5), In(2.2), In(4.5), White, 19050);
        var lines = new List<Para> { T("기준 버전  " + r.Version, 16, White, true), T($"완료 {When(r.Job.FinishedUtc)}   ·   작성 {When(DateTime.UtcNow)}", 13, Tint, before: 6) };
        if (a.Note.Length > 0) lines.Add(T(a.Note, 12, Tint, before: 6));
        s.Text(In(0.8), In(4.75), In(6.6), In(1.4), lines);
        s.Text(In(0.8), In(6.75), In(3), In(0.35), T("ASH", 14, White, true));

        // the three numbers: 권장 대수, 최대 가동률, then 투자비 (else 자재 품절)
        var ks = r.Kpis.Take(2).Append(r.Kpis.Count > 3 ? r.Kpis[3] : r.Kpis.ElementAtOrDefault(2)).OfType<ReportKpi>().ToList();
        long x = lw + In(0.7), w = SW - lw - In(1.2), bh = In(2.0);
        for (int i = 0; i < ks.Count; i++)
        {
            var k = ks[i]; long y = In(0.75) + i * bh;
            if (i > 0) s.Line(x, y - In(0.15), x + w, y - In(0.15), Rule, 9525);
            s.Text(x, y, w, In(0.32), T(k.Label, 14, Sub));
            s.Text(x, y + In(0.3), w, In(0.95), new[] { new Para(new[] { new Run(k.Value, 54, Color(k.Color), false, Num), new Run(" " + k.Unit, 20, Sub) }) }, anchor: "b");
            if (k.Sub.Length > 0) s.Text(x, y + In(1.3), w, In(0.3), T(Fit(k.Sub, 12, w), 12, k.SubColor.Length > 0 ? Color(k.SubColor) : Sub));
        }
        slides.Add(s);
    }

    sealed record Finding(string Rgb, string Lead, string Rest);

    /// <summary>요약's three findings: the most loaded (or failed) group, the top bottleneck, the comparison.</summary>
    static List<Finding> Findings(ReportData r, JobView v, string plan, int total, GroupView? top, List<GroupView> fails, Func<GroupView, string> supply)
    {
        var lim = v.UtilMax;
        var l = new List<Finding>();
        if (fails.Count > 0) l.Add(new(Red, $"{fails[0].Group} 판정 실패", $"  —  {fails[0].Reason}{(fails.Count > 1 ? $" (외 {fails.Count - 1}개 그룹)" : "")}"));
        else if (top != null)
        {
            var over = Over(top, lim);
            var sp = supply(top);
            l.Add(new(over ? Orange : Blue, $"가장 바쁜 그룹 {top.Group} 가동률 {Pc(top.Util)}",
                (over ? $"  —  상한 {Pc(lim)} {OverWhy(top, lim)}(경고)" : $"  —  상한 {Pc(lim)}까지 {F((lim - top.Util) * 100, "0")}%p 여유") + (sp.Length > 0 ? $" · {sp}" : "")));
        }
        if (r.Top.Count > 0)
        {
            var b = r.Top[0];
            l.Add(new(b.AvgWaitSec >= 10 ? Red : Orange, $"최대 병목 {b.Name} 평균 대기 {F(b.AvgWaitSec, "0.0")}초", $"  —  점유율 {Pc(b.Utilization)} · 통과 {F(b.Passes, "#,0")}회"));
        }
        else l.Add(new(Green, "병목 없음", "  —  모든 block 평균 대기 0초"));
        if (r.Compare.Count >= 2)
        {
            var (cur, other) = Pair(r);
            var d = (cur.M.Total ?? 0) - (other.M.Total ?? 0);
            var money = cur.M.Investment is { AnyPriced: true } ci && other.M.Investment is { AnyPriced: true } oi && ci.Total != oi.Total
                ? $" · 투자비 {AnalysisPages.Money(Math.Abs(ci.Total - oi.Total), ci.Currency)} {(ci.Total < oi.Total ? "절감" : "증가")}" : "";
            l.Add(new(d <= 0 ? Blue : Orange, $"{cur.Label} {cur.M.Total}대 vs {other.Label} {other.M.Total}대",
                $"  —  {(d == 0 ? "대수 같음" : $"{Math.Abs(d)}대 {(d < 0 ? "적음" : "많음")}")}{money} · 최대 가동률 {Pc(cur.M.Util)} vs {Pc(other.M.Util)}"));
        }
        else l.Add(new(Ter, $"{plan} {total}대", $"  —  비교할 다른 안·버전 없음 · 시험 범위 {v.Groups.Sum(g => g.Min)}~{v.Groups.Sum(g => g.Max)}대"));
        return l;
    }

    /// <summary>The report's own entry and what it is compared with: by plan the alternative with the fewest vehicles, by version the first one.</summary>
    static (ReportPlan Cur, ReportPlan Other) Pair(ReportData r)
    {
        var cur = r.Compare.FirstOrDefault(c => c.Job.Id == r.Job.Id) ?? r.Compare[^1];
        var others = r.Compare.Where(c => c != cur).ToList();
        var other = r.ByPlan ? others.OrderBy(c => c.M.Total ?? int.MaxValue).First() : others[0];
        return (cur, other);
    }

    static string CompareTitle(ReportData r)
    {
        var (cur, other) = Pair(r);
        var d = (cur.M.Total ?? 0) - (other.M.Total ?? 0);
        var money = cur.M.Investment is { AnyPriced: true } ci && other.M.Investment is { AnyPriced: true } oi && ci.Total != oi.Total
            ? $", 투자비 {AnalysisPages.Money(Math.Abs(ci.Total - oi.Total), ci.Currency)} {(ci.Total < oi.Total ? "절감" : "증가")}" : "";
        return d == 0 ? $"{cur.Label}·{other.Label} 모두 {cur.M.Total}대 — 최대 가동률 {Pc(cur.M.Util)} vs {Pc(other.M.Util)}"
            : $"{cur.Label} {cur.M.Total}대 — {other.Label}보다 {Math.Abs(d)}대 {(d < 0 ? "적음" : "많음")}{money}";
    }

    /// <summary>Two column charts (권장 대수, 최대 가동률 with the limit line), the report's entry in blue, then a strip with every number.</summary>
    static void Compare(Slide s, ReportData r)
    {
        var lim = r.View.UtilMax;
        var cmp = r.Compare.TakeLast(8).ToList();
        var cats = cmp.Select(c => c.Label).ToArray();
        var cols = cmp.Select(c => (string?)(c.Job.Id == r.Job.Id ? Blue : Gray)).ToArray();
        var priced = cmp.Any(c => c.M.Investment is { AnyPriced: true });
        var stripRows = cmp.Select(c => (new[] { c.Label, r.ByPlan ? $"v{c.Job.Version}{(c.Job.Selected ? " · 선정" : "")}" : When(c.Job.FinishedUtc), c.M.Total?.ToString(CultureInfo.InvariantCulture) ?? "-", (OverM(c.M) ? "▲ " : "") + Pc(c.M.Util),
                double.IsNaN(c.M.Stop) ? "-" : F(c.M.Stop, "#,0") }.Concat(priced ? new[] { c.M.Investment is { AnyPriced: true } i ? AnalysisPages.Money(i.Total, i.Currency) : "-" } : Array.Empty<string>()).ToArray(),
            string.Join(" ", new[] { c.Job.Id == r.Job.Id ? "cur" : "", c.M.Stop > 0 ? "fail" : OverM(c.M) ? "over" : "" }.Where(x => x.Length > 0)))).ToList();
        var head = new[] { r.ByPlan ? "안" : "버전", r.ByPlan ? "버전" : "완료", "권장(대)", "최대 가동률(%)", "자재 품절(초)" }.Concat(priced ? new[] { "투자비" } : Array.Empty<string>()).ToArray();
        long stripH = In(0.52) + In(0.32) * stripRows.Count, chartH = BodyH - stripH - In(0.75);
        long hw = (BodyW - Gap) / 2;

        s.Text(M, Top, hw, In(0.3), T("권장 대수 (대)", 13, Sub, true));
        var c1 = new Chart(cats, new[] { new Ser("권장 대수", cmp.Select(c => (double)(c.M.Total ?? 0)).ToArray(), Gray, cols, "0\"대\"", LabelBold: true) }, false,
            Max: Math.Max(1, cmp.Max(c => c.M.Total ?? 0)) * 1.2, Gap: cmp.Count <= 3 ? 160 : 80, PY: 0.04, PH: 0.84, CatPt: 13);
        s.AddChart(c1, M, Top + In(0.35), hw, chartH);

        long x2 = M + hw + Gap;
        s.Text(x2, Top, hw, In(0.3), T("최대 가동률 (%)", 13, Sub, true));
        var mx = Math.Max(lim, cmp.Select(c => double.IsNaN(c.M.Util) ? 0 : c.M.Util).Max()) * 1.2;
        var c2 = new Chart(cats, new[] { new Ser("최대 가동률", cmp.Select(c => c.M.Util).ToArray(), Gray, cols, "0%", LabelBold: true) }, false,
            Max: mx, Gap: cmp.Count <= 3 ? 160 : 80, PY: 0.04, PH: 0.84, CatPt: 13);
        var p = Plot(c2, x2, Top + In(0.35), hw, chartH);
        long ly = p.Y + p.H - (long)(lim / mx * p.H);
        s.Line(p.X, ly, p.X + p.W, ly, Orange, 15875, true); // under the chart: bars and labels cover it
        s.AddChart(c2, x2, Top + In(0.35), hw, chartH);
        s.Text(p.X + p.W - In(1.4), ly - In(0.3), In(1.4), In(0.26), T($"상한 {Pc(lim)}", 11, Orange, true, "r"));

        s.Table(head, stripRows, M, Bottom - stripH, BodyW, 12);
    }

    static bool OverM(AnalysisPages.Metrics m) => !double.IsNaN(m.Util) && m.Util > m.View.UtilMax;

    /// <summary>Utilisation per group (top 12), blue, over the limit orange, failed red, with the limit line; the top 3 as cards on the right.</summary>
    static void Groups(Slide s, ReportData r, List<GroupView> loaded, Func<GroupView, string> supply)
    {
        var v = r.View; var lim = v.UtilMax;
        var shown = loaded.Take(12).ToList();
        long cw = In(8.2), cardX = M + cw + Gap, cardW = BodyW - cw - Gap;
        var cats = shown.Select(g => g.Group).ToArray();
        string? Col(GroupView g) => AnalysisPages.GroupClass(g, lim) switch { "fail" => Red, "over" => Orange, _ => Blue };
        var px = LabelRoom(cats, 12, cw);
        long chH = shown.Count < loaded.Count ? BodyH - In(0.4) : BodyH;
        var c = new Chart(cats, new[] { new Ser("가동률", shown.Select(g => g.Util).ToArray(), Blue, shown.Select(Col).ToArray(), "0%", LabelBold: true) }, true,
            Max: 1, Gap: shown.Count <= 5 ? 110 : 55, PX: px, PY: 0.07, PW: 0.9 - px, PH: 0.92);
        var p = Plot(c, M, Top, cw, chH);
        long lx = p.X + (long)(lim * p.W);
        s.Line(lx, p.Y - In(0.05), lx, p.Y + p.H, Sub, 12700, true); // under the chart: bars and labels cover it
        s.AddChart(c, M, Top, cw, chH);
        s.Text(lx - In(0.8), Top, In(1.6), In(0.25), T($"상한 {Pc(lim)}", 11, Sub, true, "ctr"));
        if (shown.Count < loaded.Count) s.Text(M, Bottom - In(0.3), cw, In(0.28), T($"가동률 상위 {shown.Count}개 · 전체 {loaded.Count}개 그룹은 부록 참조", 11, Ter));

        var byGroup = v.VehicleGroups.ToDictionary(x => x.Group, StringComparer.Ordinal);
        long chh = (BodyH - 2 * In(0.2)) / 3;
        for (int i = 0; i < Math.Min(3, shown.Count); i++)
        {
            var g = shown[i]; long y = Top + i * (chh + In(0.2));
            var col = Col(g)!;
            s.Box(cardX, y, cardW, chh, i == 0 ? Tint : Panel, null, "roundRect");
            s.Box(cardX, y + In(0.2), 38100, chh - In(0.4), col);
            s.Text(cardX + In(0.3), y + In(0.16), cardW - In(0.5), In(0.32), new[] { new Para(new[] { new Run($"{i + 1}  ", 13, Ter, true), new Run(g.Group, 15, Ink, true), new Run("   " + g.Type, 12, Sub) }) });
            s.Text(cardX + In(0.3), y + In(0.48), cardW - In(0.5), In(0.62), new[] { new Para(new[] { new Run(F(g.Util * 100, "0"), 32, col == Blue ? Ink : col, false, Num), new Run(" %", 16, Sub),
                new Run($"     권장 {g.Decided?.ToString(CultureInfo.InvariantCulture) ?? "-"}대", 14, Ink, true) }) }, anchor: "b");
            var sp = supply(g);
            s.Text(cardX + In(0.3), y + In(1.18), cardW - In(0.5), In(0.28), T(sp.Length > 0 ? sp : "공급·거리 기록 없음", 12, Sub));
        }
    }

    /// <summary>Where each group's vehicle time went, as 100 % bars: busy parts in blues, buffer over-wait orange, idle gray; overall shares on the right.</summary>
    static void TimeSplitSlide(List<Slide> slides, ref int sec, List<(string Group, ASH.Core.Runner.TimeSplit S)> rows, JobView v)
    {
        static double Z(double d) => double.IsNaN(d) || d < 0 ? 0 : d;
        var parts = new (string Name, string Rgb, Func<ASH.Core.Runner.TimeSplit, double> Of)[]
        {
            ("적재 주행", Blue, x => x.Loaded), ("공차 주행", "5B6FD0", x => x.Empty), ("상하차", "9AAAE6", x => x.Handling), ("버퍼 대기(허용)", "C9D2F3", x => x.BufferBusy),
            ("고장", Navy, x => x.Repair), ("버퍼 대기(초과)", Orange, x => x.BufferIdle), ("유휴", "DADCE0", x => x.Idle),
        };
        var all = JobView.Combine(rows.Select(x => x.S), 1);
        double Share(ASH.Core.Runner.TimeSplit t, Func<ASH.Core.Runner.TimeSplit, double> f) => t.Window > 0 ? Z(f(t)) / t.Window : 0;
        var drive = Share(all, x => x.Loaded) + Share(all, x => x.Empty);
        var idle = Share(all, x => x.Idle);
        var worst = rows.OrderByDescending(x => Share(x.S, y => y.BufferIdle)).First();
        var title = $"차량 시간의 {Pc(drive)}가 주행, 유휴 {Pc(idle)}" + (Share(worst.S, y => y.BufferIdle) >= 0.01 ? $" — 버퍼 초과 대기 최대 {worst.Group} {Pc(Share(worst.S, y => y.BufferIdle))}" : "");
        var s = new Slide().Head($"{++sec:00} · 시간 내역", title);
        slides.Add(s);

        long cw = In(8.6);
        var cats = rows.Select(x => x.Group).ToArray();
        var px = LabelRoom(cats, 12, cw);
        var series = parts.Select(pt => new Ser(pt.Name, rows.Select(x => Share(x.S, pt.Of)).ToArray(), pt.Rgb, null, "0%", "ctr",
            pt.Rgb is Blue or Navy or "5B6FD0" or Orange ? White : Ink, false, rows.Select(x => Share(x.S, pt.Of) < 0.07).ToArray())).ToArray();
        s.AddChart(new Chart(cats, series, true, "percentStacked", Legend: true, Gap: rows.Count <= 5 ? 90 : 45, Overlap: 100, PX: px, PY: 0.11, PW: 0.98 - px, PH: 0.87), M, Top, cw, BodyH);

        long x0 = M + cw + Gap, w = BodyW - cw - Gap;
        s.Box(x0, Top, w, BodyH, Panel, null, "roundRect");
        s.Text(x0 + In(0.3), Top + In(0.25), w - In(0.6), In(0.3), T($"전체 {rows.Count}개 그룹 합계", 13, Sub, true));
        var sum = new (string Name, string Rgb, double V)[]
        {
            ("주행 (적재 + 공차)", Blue, drive), ("상하차", "9AAAE6", Share(all, x => x.Handling)), ("버퍼 대기 (허용)", "C9D2F3", Share(all, x => x.BufferBusy)),
            ("버퍼 대기 (초과)", Orange, Share(all, x => x.BufferIdle)), ("고장", Navy, Share(all, x => x.Repair)), ("유휴", "BDC1C6", idle),
        };
        long rh = (BodyH - In(0.8)) / sum.Length;
        for (int i = 0; i < sum.Length; i++)
        {
            long y = Top + In(0.7) + i * rh;
            s.Box(x0 + In(0.3), y + In(0.12), In(0.16), In(0.16), sum[i].Rgb);
            s.Text(x0 + In(0.6), y, w - In(0.9), In(0.38), T(sum[i].Name, 13, Ink));
            s.Text(x0 + In(0.3), y, w - In(0.6), In(0.38), new[] { new Para(new[] { new Run(F(sum[i].V * 100, "0.0"), 20, sum[i].Rgb == Orange && sum[i].V >= 0.01 ? Orange : Ink, false, Num), new Run(" %", 12, Sub) }, "r") });
        }
    }

    /// <summary>Lead time per material (top 12 by mean): the max as a light bar behind the mean; stockouts in red; cards on the right.</summary>
    static void Materials(Slide s, JobView v, List<MaterialView> mats, List<MaterialView> outs)
    {
        var shown = mats.OrderByDescending(m => m.DelivAvg).Take(12).ToList();
        long cw = In(8.2);
        var cats = shown.Select(m => MatLabel(v, m)).ToArray();
        var px = LabelRoom(cats, 12, cw);
        var mx = shown.Max(m => double.IsNaN(m.DelivMax) ? m.DelivAvg : m.DelivMax) * 1.15;
        var c = new Chart(cats, new[]
        {
            new Ser("최대", shown.Select(m => m.DelivMax).ToArray(), "D5DBF2", null, "0\"초\"", "outEnd", Sub),
            new Ser("평균", shown.Select(m => m.DelivAvg).ToArray(), Blue, shown.Select(m => (string?)(m.StopSec > 0 ? Red : Blue)).ToArray(), "0\"초\"", "inEnd", White, true),
        }, true, Max: mx, Legend: true, Gap: shown.Count <= 5 ? 90 : 45, Overlap: 100, PX: px, PY: 0.1, PW: 0.88 - px, PH: 0.88);
        s.AddChart(c, M, Top, cw, BodyH - In(0.4));
        s.Text(M, Bottom - In(0.3), cw, In(0.28), T($"리드 타임 = 호출부터 하차 완료까지{(shown.Count < mats.Count ? $" · 평균 상위 {shown.Count}개, 전체 {mats.Count}개는 부록 참조" : "")}", 11, Ter));

        long x0 = M + cw + Gap, w = BodyW - cw - Gap, chh = (BodyH - 2 * In(0.2)) / 3;
        var cards = outs.Count > 0 ? outs.Take(3).Select(m => (m, true)).ToList() : shown.Take(2).Select(m => (m, false)).ToList();
        if (outs.Count == 0)
        {
            s.Box(x0, Top, w, chh, "E6F4EA", null, "roundRect");
            s.Text(x0 + In(0.3), Top + In(0.2), w - In(0.6), In(0.35), T("✓ 자재 품절 없음", 18, Green, true));
            s.Text(x0 + In(0.3), Top + In(0.65), w - In(0.6), In(0.7), new[] { T($"자재 {mats.Count}개 모두 품절 0초", 13, Sub), T($"공급 합계 {F(mats.Sum(m => double.IsNaN(m.Trips) ? m.Supplies : m.Trips), "#,0")}회", 13, Sub, before: 4) });
        }
        for (int i = 0; i < cards.Count; i++)
        {
            var (m, bad) = cards[i];
            long y = Top + (outs.Count == 0 ? i + 1 : i) * (chh + In(0.2));
            s.Box(x0, y, w, chh, bad ? "FCE8E6" : Panel, null, "roundRect");
            s.Box(x0, y + In(0.2), 38100, chh - In(0.4), bad ? Red : Blue);
            s.Text(x0 + In(0.3), y + In(0.16), w - In(0.5), In(0.32), new[] { new Para(new[] { new Run(MatLabel(v, m), 15, Ink, true), new Run("   " + m.Group, 12, Sub) }) });
            s.Text(x0 + In(0.3), y + In(0.48), w - In(0.5), In(0.62), new[] { bad
                ? new Para(new[] { new Run(F(m.StopSec, "#,0"), 32, Red, false, Num), new Run(" 초 자재 품절", 14, Sub) })
                : new Para(new[] { new Run(F(m.DelivAvg, "0"), 32, Ink, false, Num), new Run(" 초 평균", 14, Sub), new Run($"   최대 {F(m.DelivMax, "0")}초", 14, Ink, true) }) }, anchor: "b");
            s.Text(x0 + In(0.3), y + In(1.18), w - In(0.5), In(0.28), T($"공급 {F(m.Trips is > 0 ? m.Trips : m.Supplies, "#,0")}회{(SupplyInterval(v, m) is var iv && !double.IsNaN(iv) ? $" · {F(iv, "0")}초마다" : "")}", 12, Sub));
        }
    }

    /// <summary>The map large on the left, the top 5 ranked with inline bars on the right, the colour scale under them.</summary>
    static void Bottleneck(Slide s, ReportData r)
    {
        long mw = In(8.2);
        Map(s, r, M, Top, mw, BodyH);
        long x0 = M + mw + Gap, w = BodyW - mw - Gap;
        s.Text(x0, Top, w, In(0.3), T("평균 대기 상위 5 block", 13, Sub, true));
        if (r.Top.Count == 0) s.Text(x0, Top + In(0.5), w, In(0.4), T("대기가 있는 block이 없습니다.", 14, Sub));
        var hi = r.Top.Select(b => b.AvgWaitSec).DefaultIfEmpty(1).Max();
        string[] mark = { Red, Orange, Blue, Blue, Blue };
        long ly = Bottom - In(0.6), sw = (w - In(0.5)) / 5, rh = (ly - In(0.5) - (Top + In(0.45))) / 5;
        for (int i = 0; i < r.Top.Count; i++)
        {
            var b = r.Top[i]; long y = Top + In(0.45) + i * rh;
            s.Text(x0, y + In(0.04), In(0.34), In(0.34), new[] { T((i + 1).ToString(CultureInfo.InvariantCulture), 12, White, true, "ctr") }, mark[i], null, "ellipse", "ctr");
            s.Text(x0 + In(0.5), y, w - In(0.5), In(0.36), new[] { new Para(new[] { new Run(b.Name, 15, Ink, true), new Run($"   점유율 {Pc(b.Utilization)}", 12, Sub) }) }, anchor: "ctr");
            s.Text(x0 + In(0.5), y, w - In(0.5), In(0.36), new[] { new Para(new[] { new Run(F(b.AvgWaitSec, "0.0"), 16, Ink, false, Num), new Run(" 초", 12, Sub) }, "r") }, anchor: "ctr");
            long bw = w - In(0.5);
            s.Box(x0 + In(0.5), y + In(0.46), bw, In(0.1), Rule);
            s.Box(x0 + In(0.5), y + In(0.46), Math.Max(In(0.05), (long)(bw * b.AvgWaitSec / hi)), In(0.1), Heat5[Step(b.AvgWaitSec, hi) - 1]);
        }
        // the colour scale
        s.Text(x0, ly - In(0.36), w, In(0.3), T("block 평균 대기 · 권장 대수 기준", 11, Sub));
        for (int i = 0; i < 5; i++) s.Box(x0 + i * sw, ly, sw - In(0.04), In(0.16), Heat5[i]);
        s.Text(x0, ly + In(0.22), In(1.5), In(0.25), T("짧음", 11, Sub));
        s.Text(x0 + 5 * sw - In(1.5), ly + In(0.22), In(1.5), In(0.25), T("김", 11, Sub, false, "r"));
    }

    static int Step(double v, double hi) => v > 0 && hi > 0 ? Math.Clamp((int)Math.Ceiling(v / hi * 5), 1, 5) : 0;

    /// <summary>The whole layout fitted into the box: blocks filled in 5 steps (blue to red) by mean wait, paths in gray, the top 5 as numbered markers.</summary>
    static void Map(Slide s, ReportData r, long x, long y, long w, long h)
    {
        var l = r.View.Layout;
        s.Box(x, y, w, h, Panel, null, "roundRect");
        if (l == null || LayoutBounds(l) is not { } bb) { s.Text(x, y + h / 2 - In(0.2), w, In(0.4), T("레이아웃을 읽지 못했습니다.", 16, Sub, false, "ctr")); return; }
        double pad = Math.Max(1e-6, 0.04 * Math.Max(bb.X1 - bb.X0, bb.Y1 - bb.Y0));
        double x0 = bb.X0 - pad, y1 = bb.Y1 + pad, mw = bb.X1 - bb.X0 + 2 * pad, mh = bb.Y1 - bb.Y0 + 2 * pad;
        double k = Math.Min(w / mw, h / mh); // EMU per model unit
        long cx = (long)(mw * k), cy = (long)(mh * k), ox = x + (w - cx) / 2, oy = y + (h - cy) / 2;
        long PX(double mx) => (long)Math.Round((mx - x0) * k);
        long PY(double my) => (long)Math.Round((y1 - my) * k);
        string Pt(double mx, double my) => $"<a:pt x=\"{Math.Clamp(PX(mx), 0, cx)}\" y=\"{Math.Clamp(PY(my), 0, cy)}\"/>";
        string Rect(Rect2 b) => $"<a:moveTo>{Pt(b.Min.X, b.Min.Y)}</a:moveTo><a:lnTo>{Pt(b.Max.X, b.Min.Y)}</a:lnTo><a:lnTo>{Pt(b.Max.X, b.Max.Y)}</a:lnTo><a:lnTo>{Pt(b.Min.X, b.Max.Y)}</a:lnTo><a:close/>";

        var paths = new StringBuilder();
        foreach (var p in l.Paths)
        {
            int n = p.Segments.Any(sg => sg is ArcSeg) ? 16 : 1; double len = p.Length;
            for (int i = 0; i <= n; i++) { var q = p.PointAt(len * i / n); paths.Append(i == 0 ? $"<a:moveTo>{Pt(q.X, q.Y)}</a:moveTo>" : $"<a:lnTo>{Pt(q.X, q.Y)}</a:lnTo>"); }
        }
        if (paths.Length > 0) s.Geom(ox, oy, cx, cy, paths.ToString(), null, 100, "B9BEC6", 28575);
        var vals = r.View.Blocks.Where(b => !double.IsNaN(b.AvgWaitSec)).ToDictionary(b => b.Name, b => b.AvgWaitSec, StringComparer.Ordinal);
        double hi = vals.Values.DefaultIfEmpty(0).Max();
        int StepOf(string name) => vals.TryGetValue(name, out var v) ? Step(v, hi) : 0;
        foreach (var g in l.Blocks.GroupBy(b => StepOf(b.Name)).Where(g => g.Key > 0))
            s.Geom(ox, oy, cx, cy, string.Concat(g.Select(b => Rect(b.Rect))), Heat5[g.Key - 1], 80, null);
        if (l.Blocks.Count > 0) s.Geom(ox, oy, cx, cy, string.Concat(l.Blocks.Select(b => Rect(b.Rect))), null, 100, "9AA0A6");

        var rects = l.Blocks.GroupBy(b => b.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Rect, StringComparer.Ordinal);
        string[] col = { Red, Orange, Blue, Blue, Blue };
        long d = In(0.36);
        for (int i = r.Top.Count - 1; i >= 0; i--) // the first drawn last, on top
        {
            if (!rects.TryGetValue(r.Top[i].Name, out var rr)) continue;
            long mx = ox + PX((rr.Min.X + rr.Max.X) / 2), my = oy + PY((rr.Min.Y + rr.Max.Y) / 2);
            s.Text(mx - d / 2, my - d / 2, d, d, new[] { T((i + 1).ToString(CultureInfo.InvariantCulture), 13, White, true, "ctr") }, col[i], White, "ellipse", "ctr");
        }
    }

    /// <summary>Thumbnails with rounded corners, linked to the film page: one large, two side by side, or a 3 x 2 grid.</summary>
    static void Films(Slide s, ReportFilm[] films, string baseUrl)
    {
        string Len(FilmInfo f) { var sec = f.Cuts.Sum(c => c.VideoSeconds) * Math.Max(1, f.Request.JobIds.Count); return sec >= 60 ? $"{(int)sec / 60}분 {(int)sec % 60}초" : $"{F(sec, "0")}초"; }
        byte[]? Jpg(ReportFilm f) => f.Thumb == null ? null : TryRead(f.Thumb);
        void Thumb(ReportFilm f, long x, long y, long w, long h, int link)
        {
            if (Jpg(f) is { } jpg) s.Picture(jpg, x, y, w, h, $"영상 {When(f.Film.CreatedUtc)}", link);
            else s.Text(x, y, w, h, new[] { T("썸네일 없음", 14, Ter, false, "ctr") }, Panel, Rule, "roundRect", "ctr");
            s.Text(x + w / 2 - In(0.35), y + h / 2 - In(0.35), In(0.7), In(0.7), new[] { T("▶", 20, White, false, "ctr") }, Navy, null, "ellipse", "ctr", alpha: 70);
        }
        if (films.Length == 1)
        {
            var f = films[0]; var url = $"{baseUrl}/films/{f.Film.Id}"; var link = s.Link(url);
            long tw = In(7.9), th = tw * 9 / 16;
            Thumb(f, M, Top, tw, th, link);
            long x0 = M + tw + Gap + In(0.1), w = BodyW - tw - Gap - In(0.1);
            s.Text(x0, Top, w, In(0.4), T(When(f.Film.CreatedUtc), 18, Ink, true));
            s.Text(x0, Top + In(0.45), w, In(0.3), T($"컷 {f.Film.Cuts.Count}개 × 버전 {f.Film.Request.JobIds.Count}개 · 약 {Len(f.Film)}", 13, Sub));
            s.Line(x0, Top + In(0.95), x0 + w, Top + In(0.95), Rule, 9525);
            var ps = new List<Para> { T("컷", 12, Sub, true) };
            ps.AddRange(f.Film.Cuts.Take(8).Select((c, i) => new Para(new[] { new Run($"{i + 1}. {c.Name}", 14, Ink, true), new Run($"   {F(c.VideoSeconds, "0")}초 · {(c.Kind == ASH.Core.Film.CutKind.Move ? "이동" : "고정")}", 12, Sub) }, Before: 6)));
            s.Text(x0, Top + In(1.1), w, In(2.4), ps);
            s.Text(x0, Top + th - In(0.85), w, In(0.85), new[] { new Para(new[] { new Run("영상 페이지 열기 ↗", 14, Blue, true, null, link) }), T(Fit(f.Main, 11, w), 11, Sub, before: 4), T(Fit(url, 11, w), 11, Ter, before: 2) });
            return;
        }
        // one row of 2 or 3, else 4 x 2
        int perRow = films.Length <= 3 ? films.Length : 4;
        long cw = (BodyW - (perRow - 1) * Gap) / perRow, thh = cw * 9 / 16, rowH = thh + In(1.0);
        for (int i = 0; i < films.Length; i++)
        {
            var f = films[i]; var link = s.Link($"{baseUrl}/films/{f.Film.Id}");
            long x = M + i % perRow * (cw + Gap), y = Top + i / perRow * rowH;
            Thumb(f, x, y, cw, thh, link);
            s.Text(x, y + thh + In(0.1), cw, In(0.85), new[] {
                T(When(f.Film.CreatedUtc), 13, Ink, true),
                T(Fit($"컷 {f.Film.Cuts.Count}개 × 버전 {f.Film.Request.JobIds.Count}개 · {Len(f.Film)} · {f.Main}", 12, cw), 12, Sub, before: 2),
                new Para(new[] { new Run("영상 페이지 열기 ↗", 12, Blue, true, null, link) }, Before: 2) });
        }
    }

    static byte[]? TryRead(string path) { try { return File.ReadAllBytes(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; } }

    // ---- charts

    static string TxPr(double pt, string rgb, bool bold = false) =>
        $"<c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz=\"{(int)(pt * 100)}\" b=\"{(bold ? 1 : 0)}\"><a:solidFill><a:srgbClr val=\"{rgb}\"/></a:solidFill><a:latin typeface=\"+mn-lt\"/><a:ea typeface=\"+mn-ea\"/></a:defRPr></a:pPr><a:endParaRPr lang=\"ko-KR\"/></a:p></c:txPr>";
    const string NoLine = "<c:spPr><a:noFill/><a:ln><a:noFill/></a:ln></c:spPr>";

    /// <summary>The chart part's XML; the data sits in Sheet1 of the embedded workbook (A = categories, B.. = series).</summary>
    static string ChartXml(Chart c)
    {
        var sb = new StringBuilder($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><c:chartSpace xmlns:c=\"{NsC}\" xmlns:a=\"{NsA}\" xmlns:r=\"{NsR}\">" +
            "<c:date1904 val=\"0\"/><c:lang val=\"ko-KR\"/><c:roundedCorners val=\"0\"/><c:chart><c:autoTitleDeleted val=\"1\"/><c:plotArea>" +
            $"<c:layout><c:manualLayout><c:layoutTarget val=\"inner\"/><c:xMode val=\"edge\"/><c:yMode val=\"edge\"/><c:x val=\"{Inv(c.PX)}\"/><c:y val=\"{Inv(c.PY)}\"/><c:w val=\"{Inv(c.PW)}\"/><c:h val=\"{Inv(c.PH)}\"/></c:manualLayout></c:layout>" +
            $"<c:barChart><c:barDir val=\"{(c.Bar ? "bar" : "col")}\"/><c:grouping val=\"{c.Grouping}\"/><c:varyColors val=\"0\"/>");
        int n = c.Cats.Length;
        string cats = $"<c:cat><c:strRef><c:f>Sheet1!$A$2:$A${n + 1}</c:f><c:strCache><c:ptCount val=\"{n}\"/>{string.Concat(c.Cats.Select((t, i) => $"<c:pt idx=\"{i}\"><c:v>{E(t)}</c:v></c:pt>"))}</c:strCache></c:strRef></c:cat>";
        string Fill(string rgb) => $"<c:spPr><a:solidFill><a:srgbClr val=\"{rgb}\"/></a:solidFill><a:ln><a:noFill/></a:ln></c:spPr>";
        for (int k = 0; k < c.Series.Length; k++)
        {
            var se = c.Series[k]; var col = (char)('B' + k);
            sb.Append($"<c:ser><c:idx val=\"{k}\"/><c:order val=\"{k}\"/><c:tx><c:strRef><c:f>Sheet1!${col}$1</c:f><c:strCache><c:ptCount val=\"1\"/><c:pt idx=\"0\"><c:v>{E(se.Name)}</c:v></c:pt></c:strCache></c:strRef></c:tx>");
            sb.Append(Fill(se.Rgb)).Append("<c:invertIfNegative val=\"0\"/>");
            if (se.Rgbs != null)
                for (int i = 0; i < n; i++)
                    if (se.Rgbs[i] is { } pc && pc != se.Rgb) sb.Append($"<c:dPt><c:idx val=\"{i}\"/><c:invertIfNegative val=\"0\"/><c:bubble3D val=\"0\"/>{Fill(pc)}</c:dPt>");
            if (se.Fmt != null)
            {
                sb.Append("<c:dLbls>");
                if (se.Hide != null) for (int i = 0; i < n; i++) if (se.Hide[i]) sb.Append($"<c:dLbl><c:idx val=\"{i}\"/><c:delete val=\"1\"/></c:dLbl>");
                sb.Append($"<c:numFmt formatCode=\"{E(se.Fmt)}\" sourceLinked=\"0\"/>{(se.Pos == "outEnd" ? $"<c:spPr><a:solidFill><a:srgbClr val=\"{White}\"/></a:solidFill><a:ln><a:noFill/></a:ln></c:spPr>" : NoLine)}{TxPr(12, se.LabelRgb, se.LabelBold)}<c:dLblPos val=\"{se.Pos}\"/>" +
                    "<c:showLegendKey val=\"0\"/><c:showVal val=\"1\"/><c:showCatName val=\"0\"/><c:showSerName val=\"0\"/><c:showPercent val=\"0\"/><c:showBubbleSize val=\"0\"/></c:dLbls>");
            }
            sb.Append(cats);
            sb.Append($"<c:val><c:numRef><c:f>Sheet1!${col}$2:${col}${n + 1}</c:f><c:numCache><c:formatCode>General</c:formatCode><c:ptCount val=\"{n}\"/>");
            for (int i = 0; i < n; i++) if (!double.IsNaN(se.Vals[i])) sb.Append($"<c:pt idx=\"{i}\"><c:v>{Inv(se.Vals[i])}</c:v></c:pt>");
            sb.Append("</c:numCache></c:numRef></c:val></c:ser>");
        }
        sb.Append($"<c:gapWidth val=\"{c.Gap}\"/>{(c.Overlap != 0 ? $"<c:overlap val=\"{c.Overlap}\"/>" : "")}<c:axId val=\"1001\"/><c:axId val=\"1002\"/></c:barChart>");
        sb.Append($"<c:catAx><c:axId val=\"1001\"/><c:scaling><c:orientation val=\"{(c.Bar ? "maxMin" : "minMax")}\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"{(c.Bar ? "l" : "b")}\"/>" +
            $"<c:numFmt formatCode=\"General\" sourceLinked=\"0\"/><c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"nextTo\"/>" +
            $"<c:spPr><a:ln w=\"9525\"><a:solidFill><a:srgbClr val=\"{(c.Bar ? Rule : Ter)}\"/></a:solidFill></a:ln></c:spPr>{TxPr(c.CatPt, Ink)}" +
            "<c:crossAx val=\"1002\"/><c:crosses val=\"autoZero\"/><c:auto val=\"1\"/><c:lblAlgn val=\"ctr\"/><c:lblOffset val=\"100\"/><c:noMultiLvlLbl val=\"0\"/></c:catAx>");
        sb.Append($"<c:valAx><c:axId val=\"1002\"/><c:scaling><c:orientation val=\"minMax\"/>{(c.Max is { } mx && c.Grouping != "percentStacked" ? $"<c:max val=\"{Inv(mx)}\"/>" : "")}<c:min val=\"0\"/></c:scaling>" +
            $"<c:delete val=\"1\"/><c:axPos val=\"{(c.Bar ? "b" : "l")}\"/><c:numFmt formatCode=\"General\" sourceLinked=\"0\"/><c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"nextTo\"/>" +
            "<c:crossAx val=\"1001\"/><c:crosses val=\"autoZero\"/><c:crossBetween val=\"between\"/></c:valAx>");
        sb.Append($"{NoLine}</c:plotArea>");
        if (c.Legend) sb.Append($"<c:legend><c:legendPos val=\"t\"/><c:overlay val=\"0\"/>{TxPr(12, Sub)}</c:legend>");
        sb.Append($"<c:plotVisOnly val=\"1\"/><c:dispBlanksAs val=\"gap\"/></c:chart>{NoLine}{TxPr(12, Sub)}<c:externalData r:id=\"rId1\"><c:autoUpdate val=\"0\"/></c:externalData></c:chartSpace>");
        return sb.ToString();
    }

    /// <summary>The chart's data as an .xlsx, so "데이터 편집" in PowerPoint opens it.</summary>
    static byte[] ChartBook(Chart c)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");
        for (int k = 0; k < c.Series.Length; k++) ws.Cell(1, k + 2).Value = c.Series[k].Name;
        for (int i = 0; i < c.Cats.Length; i++)
        {
            ws.Cell(i + 2, 1).Value = c.Cats[i];
            for (int k = 0; k < c.Series.Length; k++) if (!double.IsNaN(c.Series[k].Vals[i])) ws.Cell(i + 2, k + 2).Value = c.Series[k].Vals[i];
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ---- package

    static void Feed(OpenXmlPart part, string xml) { using var ms = new MemoryStream(Encoding.UTF8.GetBytes(xml)); part.FeedData(ms); }

    const string Theme = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"ASH\"><a:themeElements>" +
        "<a:clrScheme name=\"ASH\"><a:dk1><a:srgbClr val=\"1D1D1F\"/></a:dk1><a:lt1><a:srgbClr val=\"FFFFFF\"/></a:lt1><a:dk2><a:srgbClr val=\"0B1A5C\"/></a:dk2><a:lt2><a:srgbClr val=\"F5F6F8\"/></a:lt2>" +
        "<a:accent1><a:srgbClr val=\"1428A0\"/></a:accent1><a:accent2><a:srgbClr val=\"5B6FD0\"/></a:accent2><a:accent3><a:srgbClr val=\"9AAAE6\"/></a:accent3><a:accent4><a:srgbClr val=\"E37222\"/></a:accent4>" +
        "<a:accent5><a:srgbClr val=\"D93025\"/></a:accent5><a:accent6><a:srgbClr val=\"9AA0A6\"/></a:accent6><a:hlink><a:srgbClr val=\"1428A0\"/></a:hlink><a:folHlink><a:srgbClr val=\"5B6FD0\"/></a:folHlink></a:clrScheme>" +
        "<a:fontScheme name=\"ASH\"><a:majorFont><a:latin typeface=\"Segoe UI\"/><a:ea typeface=\"맑은 고딕\"/><a:cs typeface=\"\"/><a:font script=\"Hang\" typeface=\"맑은 고딕\"/></a:majorFont>" +
        "<a:minorFont><a:latin typeface=\"Segoe UI\"/><a:ea typeface=\"맑은 고딕\"/><a:cs typeface=\"\"/><a:font script=\"Hang\" typeface=\"맑은 고딕\"/></a:minorFont></a:fontScheme>" +
        "<a:fmtScheme name=\"ASH\"><a:fillStyleLst>" + Phs + Phs + Phs + "</a:fillStyleLst><a:lnStyleLst>" + PhLn + PhLn + PhLn + "</a:lnStyleLst>" +
        "<a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst>" +
        "<a:bgFillStyleLst>" + Phs + Phs + Phs + "</a:bgFillStyleLst></a:fmtScheme></a:themeElements></a:theme>";
    const string Phs = "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>", PhLn = "<a:ln w=\"6350\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln>";
    const string EmptyTree = "<p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/></p:spTree>";
    const string DefText = "<a:defPPr><a:defRPr lang=\"ko-KR\"/></a:defPPr><a:lvl1pPr><a:defRPr sz=\"1400\"><a:solidFill><a:srgbClr val=\"1D1D1F\"/></a:solidFill><a:latin typeface=\"+mn-lt\"/><a:ea typeface=\"+mn-ea\"/><a:cs typeface=\"+mn-cs\"/></a:defRPr></a:lvl1pPr>";

    /// <summary>The package: presentation, one master with one blank layout and the theme, then the slides (with their pictures, charts and links).</summary>
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
                $"<p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"rId1\"/></p:sldLayoutIdLst><p:txStyles><p:titleStyle/><p:bodyStyle/><p:otherStyle>{DefText}</p:otherStyle></p:txStyles></p:sldMaster>");
            Feed(layout, $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:sldLayout {Ns} type=\"blank\" preserve=\"1\"><p:cSld name=\"Blank\">{EmptyTree}</p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>");
            var ids = new StringBuilder();
            for (int i = 0; i < slides.Count; i++)
            {
                var sl = slides[i];
                var sp = pp.AddNewPart<SlidePart>($"rIdS{i + 1}");
                sp.AddPart(layout, "rId1");
                for (int k = 0; k < sl.Images.Count; k++)
                    using (var img = new MemoryStream(sl.Images[k])) sp.AddImagePart(ImagePartType.Jpeg, $"rIdImg{k + 1}").FeedData(img);
                for (int k = 0; k < sl.Charts.Count; k++)
                {
                    var cp = sp.AddNewPart<ChartPart>($"rIdC{k + 1}");
                    Feed(cp, ChartXml(sl.Charts[k]));
                    using var book = new MemoryStream(ChartBook(sl.Charts[k]));
                    cp.AddNewPart<EmbeddedPackagePart>("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "rId1").FeedData(book);
                }
                for (int k = 0; k < sl.Links.Count; k++) sp.AddHyperlinkRelationship(new Uri(sl.Links[k], UriKind.Absolute), true, $"rIdL{k + 1}");
                Feed(sp, sl.Xml());
                ids.Append($"<p:sldId id=\"{256 + i}\" r:id=\"rIdS{i + 1}\"/>");
            }
            Feed(pp, $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:presentation {Ns}><p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rIdM\"/></p:sldMasterIdLst>" +
                $"<p:sldIdLst>{ids}</p:sldIdLst><p:sldSz cx=\"{SW}\" cy=\"{SH}\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/><p:defaultTextStyle>{DefText}</p:defaultTextStyle></p:presentation>");
        }
        return ms.ToArray();
    }
}
