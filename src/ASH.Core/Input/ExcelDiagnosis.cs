using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ASH.Core.Input;

/// <summary>
/// Explains why an input.xlsx could not be opened. The libraries only say "file contains corrupted data"; this looks into the file itself and names what is
/// wrong: an empty or half-written file, an old .xls or encrypted file, a zip entry that cannot be read, or the sheet whose XML is broken.
/// </summary>
public static class ExcelDiagnosis
{
    static readonly string[] RequiredSheets = { "Settings", "AGV_Types", "AGV_Groups", "Materials" }; // Routes: only older workbooks have it (now on Materials)

    /// <summary>Lines for the command line: the exception chain, then what the file check found.</summary>
    public static IReadOnlyList<string> Explain(string path, Exception ex)
    {
        var lines = new List<string>();
        for (var e = ex; e != null; e = e.InnerException)
            lines.Add((e == ex ? "원인: " : "  ← ") + e.GetType().Name + ": " + e.Message.Split('\n')[0].Trim());
        lines.AddRange(Inspect(path));
        return lines;
    }

    /// <summary>What is wrong with the file itself; a single "구조는 정상" line when nothing is.</summary>
    public static IReadOnlyList<string> Inspect(string path)
    {
        var res = new List<string>();
        FileInfo fi;
        try { fi = new FileInfo(path); }
        catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException) { return new[] { "파일 경로가 올바르지 않습니다: " + e.Message }; }
        if (!fi.Exists) return new[] { "파일이 없습니다: " + path };
        res.Add($"파일: {fi.FullName} ({fi.Length:N0}바이트, 수정 {ASH.Core.SeoulTime.Text(fi.LastWriteTimeUtc)})");
        if (fi.Length == 0)
        {
            res.Add("파일이 비어 있습니다(0바이트). 저장이 중간에 끊긴 파일입니다. 지우고 AGVEXCEL로 양식을 다시 만드세요.");
            return res;
        }
        byte[] head = new byte[8];
        try
        {
            using var fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            int n = fs.Read(head, 0, head.Length);
            if (n < 4) { res.Add("파일이 너무 짧아 xlsx가 아닙니다."); return res; }
            if (!(head[0] == 'P' && head[1] == 'K'))
            {
                res.Add(Describe(head));
                return res;
            }
            fs.Position = 0;
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: true);
            var problems = Walk(zip);
            if (problems.Count == 0) res.Add("파일 구조(zip·XML·시트)는 정상입니다. 위 원인 메시지를 참고하세요. Excel에서 연 파일을 닫고 다시 시도해 보세요.");
            else res.AddRange(problems);
        }
        catch (IOException e) when (!(e is InvalidDataException)) { res.Add("파일을 읽을 수 없습니다(다른 프로그램이 사용 중일 수 있습니다): " + e.Message); }
        catch (InvalidDataException e) { res.Add("zip 구조가 깨졌습니다(파일 끝이 잘렸거나 저장이 중단됨): " + e.Message); }
        catch (UnauthorizedAccessException e) { res.Add("파일에 접근할 수 없습니다: " + e.Message); }
        return res;
    }

    static string Describe(byte[] head)
    {
        if (head[0] == 0xD0 && head[1] == 0xCF && head[2] == 0x11 && head[3] == 0xE0)
            return "xlsx가 아닙니다. 옛 .xls 형식이거나 암호가 걸린 파일입니다. 암호를 풀고 'Excel 통합 문서(*.xlsx)'로 저장하세요.";
        if (head[0] == '<' || head[0] == 0xEF || head[0] == 0xFF || head[0] == 0xFE || (head[0] >= 0x20 && head[0] < 0x7F))
            return "xlsx가 아닙니다. 텍스트(CSV·HTML 등)로 저장된 파일입니다. 'Excel 통합 문서(*.xlsx)'로 다시 저장하세요.";
        return "xlsx가 아닙니다(알 수 없는 형식). 사내 문서 암호화(DRM, 예: NASCA)로 저장된 파일일 수 있습니다. 이 경우 Excel만 읽을 수 있으며, 플러그인은 Excel로 읽습니다.";
    }

    static List<string> Walk(ZipArchive zip)
    {
        var problems = new List<string>();
        var parts = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            byte[] data;
            try
            {
                using var s = entry.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                data = ms.ToArray();
            }
            catch (Exception e) when (e is InvalidDataException || e is IOException || e is NotSupportedException)
            {
                problems.Add($"zip 항목 '{entry.FullName}'을(를) 읽을 수 없습니다(손상): {e.Message}");
                continue;
            }
            if (!(entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                using var ms = new MemoryStream(data);
                parts[entry.FullName] = XDocument.Load(XmlReader.Create(ms, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }));
            }
            catch (XmlException e)
            {
                problems.Add($"'{entry.FullName}'{SheetOf(zip, parts, entry.FullName)}의 XML이 깨졌습니다 (줄 {e.LineNumber}, 위치 {e.LinePosition}): {e.Message.Split('\n')[0].Trim()}");
            }
        }
        if (!parts.ContainsKey("[Content_Types].xml") && zip.GetEntry("[Content_Types].xml") == null) problems.Add("'[Content_Types].xml'이 없습니다. xlsx 파일이 아닙니다.");
        if (zip.GetEntry("xl/workbook.xml") == null) problems.Add("'xl/workbook.xml'이 없습니다. xlsx 파일이 아닙니다.");
        else if (parts.TryGetValue("xl/workbook.xml", out var wb))
        {
            var names = wb.Descendants().Where(x => x.Name.LocalName == "sheet").Select(x => (string?)x.Attribute("name") ?? "").ToList();
            foreach (var need in RequiredSheets)
                if (!names.Contains(need)) problems.Add($"'{need}' 시트가 없습니다. (있는 시트: {string.Join(", ", names)})");
        }
        return problems;
    }

    // "(시트 'Materials')" for xl/worksheets/sheetN.xml, found through workbook.xml and its rels; empty when it cannot be told
    static string SheetOf(ZipArchive zip, Dictionary<string, XDocument> parts, string part)
    {
        if (!part.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)) return "";
        try
        {
            XDocument Get(string name)
            {
                if (parts.TryGetValue(name, out var d)) return d;
                var e = zip.GetEntry(name); if (e == null) return null!;
                using var s = e.Open(); return XDocument.Load(s);
            }
            var wb = Get("xl/workbook.xml"); var rels = Get("xl/_rels/workbook.xml.rels");
            if (wb == null || rels == null) return "";
            foreach (var sh in wb.Descendants().Where(x => x.Name.LocalName == "sheet"))
            {
                var id = sh.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value;
                var rel = rels.Descendants().FirstOrDefault(r => (string?)r.Attribute("Id") == id);
                var target = (string?)rel?.Attribute("Target");
                if (target != null && part.EndsWith(target.TrimStart('/').Replace("xl/", ""), StringComparison.OrdinalIgnoreCase)) return $" (시트 '{(string?)sh.Attribute("name")}')";
            }
        }
        catch (Exception e) when (e is XmlException || e is InvalidDataException || e is IOException) { }
        return "";
    }
}
