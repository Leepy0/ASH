using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ASH.Core.Input;
using Xunit;

namespace ASH.Tests.Input;

public class ExcelDiagnosisTests
{
    static string Tmp() => Path.Combine(Path.GetTempPath(), "diag-" + Guid.NewGuid().ToString("N") + ".xlsx");

    static string Joined(string path) => string.Join("\n", ExcelDiagnosis.Inspect(path));

    [Fact]
    public void Empty_File_Is_Named()
    {
        var p = Tmp(); File.WriteAllBytes(p, new byte[0]);
        try { Assert.Contains("0바이트", Joined(p)); }
        finally { File.Delete(p); }
    }

    [Fact]
    public void Old_Xls_Or_Text_Is_Not_Xlsx()
    {
        var p = Tmp(); File.WriteAllBytes(p, new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 });
        var q = Tmp(); File.WriteAllText(q, "a,b,c\n1,2,3\n");
        try { Assert.Contains(".xls", Joined(p)); Assert.Contains("텍스트", Joined(q)); }
        finally { File.Delete(p); File.Delete(q); }
    }

    [Fact]
    public void Good_Template_Has_No_Problem()
    {
        var p = Tmp(); ExcelTemplate.Write(p);
        try { Assert.Contains("정상", Joined(p)); }
        finally { File.Delete(p); }
    }

    [Fact]
    public void Truncated_Zip_Is_Reported()
    {
        var p = Tmp(); ExcelTemplate.Write(p);
        var bytes = File.ReadAllBytes(p); File.WriteAllBytes(p, bytes.Take(bytes.Length / 2).ToArray());
        try { Assert.DoesNotContain("정상", Joined(p)); }
        finally { File.Delete(p); }
    }

    [Fact]
    public void Broken_Sheet_Xml_Names_The_Sheet()
    {
        var p = Tmp(); ExcelTemplate.Write(p);
        try
        {
            using (var z = ZipFile.Open(p, ZipArchiveMode.Update))
            {
                var wbXml = new StreamReader(z.GetEntry("xl/workbook.xml")!.Open()).ReadToEnd();
                // Materials is the 4th sheet of the template: sheet4.xml
                z.GetEntry("xl/worksheets/sheet4.xml")!.Delete();
                using var w = new StreamWriter(z.CreateEntry("xl/worksheets/sheet4.xml").Open(), new UTF8Encoding(false));
                w.Write("<worksheet><sheetData><row></sheetData></worksheet>");
                Assert.Contains("Materials", wbXml);
            }
            var text = Joined(p);
            Assert.Contains("sheet4.xml", text);
            Assert.Contains("Materials", text);
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void Template_Write_Leaves_No_Temp_File()
    {
        var p = Tmp(); ExcelTemplate.Write(p);
        try { Assert.False(File.Exists(p + ".tmp")); Assert.True(File.Exists(p)); }
        finally { File.Delete(p); }
    }
}
