using System;
using System.IO;
using System.Linq;
using ASH.Core.Input;
using ASH.Core.Model;
using ClosedXML.Excel;
using Xunit;

namespace ASH.Tests.Input;

[Xunit.Collection("ExcelTemplateStatics")]
public class CellDumpTests
{
    static string Tmp(string ext) => Path.Combine(Path.GetTempPath(), "dump-" + Guid.NewGuid().ToString("N") + ext);

    [Fact]
    public void Dump_Of_The_Template_Reads_Back_To_The_Same_Input()
    {
        var xlsx = Tmp(".xlsx"); var tsv = Tmp(".tsv");
        try
        {
            ExcelTemplate.Write(xlsx);
            using (var wb = ExcelReader.Open(xlsx)) CellDump.Write(wb, tsv);
            var direct = ExcelReader.Read(xlsx);
            using var back = CellDump.Load(tsv);
            var viaDump = ExcelReader.Read(back, out _);
            Assert.Equal(InputSignature.Compute(direct), InputSignature.Compute(viaDump));
            Assert.Equal(direct.Materials.Select(m => m.TaktTime), viaDump.Materials.Select(m => m.TaktTime));
            Assert.Equal(direct.Routes.Select(r => r.Row), viaDump.Routes.Select(r => r.Row));
        }
        finally { File.Delete(xlsx); File.Delete(tsv); File.Delete(tsv + ".tmp"); }
    }

    [Fact]
    public void Tabs_Newlines_Backslashes_And_Korean_Survive()
    {
        var tsv = Tmp(".tsv");
        var text = "a\tb\nc\r\nd" + (char)92 + "t" + (char)92 + (char)92 + " 공정";
        try
        {
            using (var wb = ExcelReader.NewWorkbook())
            {
                var ws = wb.AddWorksheet("S\tX");
                ws.Cell(3, 2).Value = text; ws.Cell(4, 1).Value = 12.5;
                CellDump.Write(wb, tsv);
            }
            using var back = CellDump.Load(tsv);
            var w2 = back.Worksheet("S\tX");
            Assert.Equal(text, w2.Cell(3, 2).GetString());
            Assert.Equal(12.5, w2.Cell(4, 1).GetDouble());
        }
        finally { File.Delete(tsv); }
    }

    [Fact]
    public void A_Text_File_That_Is_Not_A_Dump_Is_Rejected()
    {
        var tsv = Tmp(".tsv"); File.WriteAllText(tsv, "hello");
        try { Assert.Throws<FormatException>(() => CellDump.Load(tsv)); }
        finally { File.Delete(tsv); }
    }
}
