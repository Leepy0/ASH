using System.Collections.Generic;
using System.IO;
using System.Linq;
using ASH.Core.Input;
using ASH.Core.Layout;
using ClosedXML.Excel;
using Xunit;

namespace ASH.Tests.Input;

/// <summary>Named AGV_CONV conveyors of the drawing reach the Conveyors sheet (template and AGVEXCEL update).</summary>
[Collection("ExcelTemplateStatics")]
public class ConveyorSeedTests
{
    static ASH.Core.Layout.Layout WithConveyors(params string[] names)
    {
        var l = new ASH.Core.Layout.Layout();
        foreach (var n in names) l.Conveyors.Add(new LayoutConveyor { Name = n });
        l.Conveyors.Add(new LayoutConveyor { Name = "", GraphicOnly = true }); // picture only: never a row
        return l;
    }

    static List<string> Names(string path)
    {
        using var wb = ExcelReader.Open(path);
        var ws = wb.Worksheet("Conveyors");
        return ws.RowsUsed().Where(r => r.RowNumber() >= 3).Select(r => r.Cell(1).GetString()).Where(s => s.Length > 0).ToList();
    }

    [Fact]
    public void Template_And_Update_Add_A_Row_Per_Named_Conveyor_Once()
    {
        var path = Path.Combine(Path.GetTempPath(), "ash_" + System.Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            ExcelTemplate.Write(path, WithConveyors("CV002", "CV001"));
            Assert.Equal(new[] { "CV001", "CV002" }, Names(path));
            var changes = ExcelTemplate.Update(path, WithConveyors("CV001", "CV002", "CV003"));
            Assert.Contains(changes, c => c.Contains("CV003") && c.Contains("소비 자재"));
            Assert.DoesNotContain(changes, c => c.Contains("CV001"));
            Assert.Equal(new[] { "CV001", "CV002", "CV003" }, Names(path));
            Assert.Empty(ExcelTemplate.Update(path, WithConveyors("CV001")).Where(c => c.StartsWith("Conveyors")));  // nothing new, nothing removed
        }
        finally { File.Delete(path); }
    }
}
