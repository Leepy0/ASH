using System.IO;
using System.Linq;
using ASH.Core.Input;
using ClosedXML.Excel;

namespace ASH.Tests.Input;

/// <summary>Folded advanced columns that the model needs a number for read a blank as the example's value.</summary>
[Collection("ExcelTemplateStatics")]
public class AdvancedBlankTests
{
    [Fact]
    public void Blank_UnloadThreshold_Accel_And_Decel_Read_As_The_Example_Values()
    {
        var path = Path.Combine(Path.GetTempPath(), "ash_" + System.Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            ExcelTemplate.Write(path);
            using (var wb = ExcelReader.Open(path))
            {
                void Clear(string sheet, string col)
                {
                    var ws = wb.Worksheet(sheet);
                    var c = ws.Row(1).CellsUsed().First(x => x.GetString() == col).Address.ColumnNumber;
                    ws.Cell(InputSchema.FirstDataRow, c).Clear(XLClearOptions.Contents);
                }
                Clear("Materials", "UnloadThreshold"); Clear("Materials", "UnloadThresholdQty");
                Clear("AGV_Types", "Accel"); Clear("AGV_Types", "Decel");
                wb.Save();
            }
            var p = ExcelReader.Read(path);
            Assert.Equal(ExcelReader.UnloadThresholdBlank, p.Materials[0].UnloadThreshold);
            Assert.Equal((ExcelReader.AccelBlank, ExcelReader.AccelBlank), (p.AgvTypes[0].Accel, p.AgvTypes[0].Decel));
        }
        finally { File.Delete(path); }
    }
}
