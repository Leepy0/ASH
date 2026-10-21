using System.IO;
using System.Linq;
using ASH.Core.Input;
using ASH.Core.Layout;
using Xunit;

namespace ASH.Tests.Input;

public class ExcelTemplateSeedTests
{
    static LayoutCp Cp(string name, string type, string group) => new LayoutCp { Name = name, Type = type, Group = group };

    static ASH.Core.Layout.Layout Drawing()
    {
        var l = new ASH.Core.Layout.Layout();
        l.Cps.Add(Cp("SB1", "standby", "A")); l.Cps.Add(Cp("SB2", "standby", "A")); l.Cps.Add(Cp("SB3", "standby", "B"));
        l.Cps.Add(Cp("L001", "load", "A")); l.Cps.Add(Cp("L002", "load", "B"));
        l.Cps.Add(Cp("U001", "unload", "A")); l.Cps.Add(Cp("U002", "unload", "B"));
        l.Cps.Add(Cp("T001", "traffic", ""));
        return l;
    }

    [Fact]
    public void Template_From_Drawing_Carries_Groups_Standby_Cps_And_Routes()
    {
        var path = Path.Combine(Path.GetTempPath(), "seed-" + System.Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            ExcelTemplate.Write(path, Drawing());
            var p = ExcelReader.Read(path);
            Assert.Equal(new[] { "A", "B" }, p.Groups.Select(g => g.Group));
            Assert.Equal(new[] { "SB1", "SB2" }, p.Groups[0].StandbyCPs);
            Assert.Equal(new[] { "T_A", "T_B" }, p.AgvTypes.Select(t => t.Type));
            Assert.Equal(new[] { "L001", "L002" }, p.Routes.Select(r => r.SupplyFromCP));
            Assert.Equal(new[] { "U001", "U002" }, p.Routes.Select(r => r.SupplyToCP));
            Assert.Equal(new[] { "A", "B" }, p.Materials.Select(m => m.Group));
            Assert.Empty(InputValidator.Validate(p, new[] { "SB1", "SB2", "SB3", "L001", "L002", "U001", "U002", "T001" }).Where(f => f.Code == FindingCode.InputError));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Template_From_Empty_Drawing_Keeps_The_Example_Rows()
    {
        var path = Path.Combine(Path.GetTempPath(), "seed-" + System.Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            ExcelTemplate.Write(path, new ASH.Core.Layout.Layout());
            var p = ExcelReader.Read(path);
            Assert.Equal("G_EX", Assert.Single(p.Groups).Group);
            Assert.Equal("M001", Assert.Single(p.Routes).Material);
        }
        finally { File.Delete(path); }
    }
}
