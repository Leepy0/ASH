using System.IO;
using System.Linq;
using ASH.Core.Input;
using ASH.Core.Layout;
using Xunit;

namespace ASH.Tests.Input;

[Xunit.Collection("ExcelTemplateStatics")]
public class BufferWaitColumnTests
{
    static string Temp() => Path.Combine(Path.GetTempPath(), "bw-" + System.Guid.NewGuid().ToString("N") + ".xlsx");

    static int Col(ClosedXML.Excel.IXLWorksheet ws, string head) =>
        Enumerable.Range(1, ws.LastColumnUsed()!.ColumnNumber()).First(c => ws.Cell(1, c).GetString() == head);

    [Fact]
    public void Template_Has_The_Column_With_Its_Note_And_Reads_A_Value()
    {
        var path = Temp();
        try
        {
            ExcelTemplate.Write(path);
            using (var wb = ExcelReader.Open(path))
            {
                var ws = wb.Worksheet("Materials"); int c = Col(ws, "BufferWaitSec");
                Assert.StartsWith("버퍼 대기 허용(초", ws.Cell(2, c).GetString());
                Assert.Equal(0, ws.Cell(3, c).GetDouble());
                ws.Cell(3, c).Value = 90; wb.Save();
            }
            Assert.Equal(90, Assert.Single(ExcelReader.Read(path).Routes).BufferWaitSec);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void An_Older_Workbook_Reads_0_And_Update_Adds_The_Column_Keeping_The_Data()
    {
        var path = Temp();
        try
        {
            ExcelTemplate.Write(path);
            using (var wb = ExcelReader.Open(path))
            {
                var ws = wb.Worksheet("Materials");
                ws.Column(Col(ws, "BufferWaitSec")).Delete();
                ws.Cell(3, Col(ws, "BufferCP")).Value = "CP_BUF"; ws.Cell(3, Col(ws, "ExtraTime")).Value = 7;
                wb.Save();
            }
            var old = Assert.Single(ExcelReader.Read(path).Routes);
            Assert.Equal(0, old.BufferWaitSec);

            var changes = ExcelTemplate.Update(path, null);
            Assert.Contains("Materials: BufferWaitSec 열 추가", changes);
            using (var wb = ExcelReader.Open(path))
            {
                var ws = wb.Worksheet("Materials"); int c = ExcelTemplate.Sheets.First(x => x.Sheet == "Materials").Cols.Length;
                Assert.Equal(c, Col(ws, "BufferWaitSec")); // appended after the last column
                Assert.StartsWith("버퍼 대기 허용(초", ws.Cell(2, c).GetString());
            }
            var r = Assert.Single(ExcelReader.Read(path).Routes);
            Assert.Equal(old with { Row = r.Row }, r); // typed data kept, the new column blank = 0
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_Negative_Allowance_Is_An_Error()
    {
        var p = new ProjectInput();
        p.AgvTypes.Add(new AgvType("T1", 1, 1, 0.3, 0.3, 1, 20, 20, 0, 0, 1, 1, "Blue"));
        p.Groups.Add(new AgvGroup("G1", "T1", 1, 3, new System.Collections.Generic.List<string> { "S" }, "Nearest"));
        p.Materials.Add(new MaterialInfo("M1", "", "G1", 60, 1, 10, 4, 1, null, null, false));
        p.Routes.Add(new Route("M1", "L", "U", "B", null, null, 0, BufferWaitSec: 30));
        Assert.Empty(InputValidator.Validate(p, new[] { "S", "L", "U", "B" }));
        p.Routes[0] = p.Routes[0] with { BufferWaitSec = -1 };
        var e = Assert.Single(InputValidator.Validate(p, new[] { "S", "L", "U", "B" }));
        Assert.Equal(FindingCode.InputError, e.Code); Assert.Equal("Materials!3", e.EntityId); Assert.Contains("BufferWaitSec", e.Message);
    }
}
