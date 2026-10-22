using System.IO;
using System.Linq;
using ASH.Core.Input;
using ASH.Core.Layout;
using Xunit;

namespace ASH.Tests.Input;

public class KindAndFormTests
{
    static readonly string[] Cps = { "S", "L", "U" };

    static ProjectInput Good(string kind, string form)
    {
        var p = new ProjectInput();
        p.AgvTypes.Add(new AgvType("T1", 1, 1, 0.3, 0.3, 1, 20, 20, 0, 0, 1, 1, "Blue", Kind: kind));
        p.Groups.Add(new AgvGroup("G1", "T1", 1, 3, new System.Collections.Generic.List<string> { "S" }, "Nearest"));
        p.Materials.Add(new MaterialInfo("M1", "", "G1", 60, 1, 10, 4, 1, null, null, true, Form: form));
        p.Routes.Add(new Route("M1", "L", "U", null, null, null, 0));
        return p;
    }

    static System.Collections.Generic.List<Finding> Check(string kind, string form) => InputValidator.Validate(Good(kind, form), Cps);

    [Theory]
    [InlineData("", "")] [InlineData("Standard", "Trolley")] [InlineData("conveyor", "box")] [InlineData("Conveyor", "Pallet")]
    [InlineData("Ant", "Trolley")] [InlineData("Magnet", "trolley")]
    public void Compatible_Pairs_Give_No_Finding(string kind, string form) => Assert.Empty(Check(kind, form));

    [Theory]
    [InlineData("Conveyor", "Trolley")] [InlineData("Ant", "Box")] [InlineData("Magnet", "Pallet")] [InlineData("Ant", "")]
    public void A_Form_The_Shape_Cannot_Carry_Is_A_Warning(string kind, string form)
    {
        var f = Check(kind, form);
        var w = Assert.Single(f);
        Assert.Equal(FindingCode.InputWarning, w.Code);
        Assert.Equal("Materials!3", w.EntityId);
        Assert.Contains("싣지 못합니다", w.Message);
    }

    [Fact]
    public void Unknown_Kind_Or_Form_Is_An_Error_With_The_Choices()
    {
        var k = Assert.Single(Check("Hover", "Box"));
        Assert.Equal(FindingCode.InputError, k.Code); Assert.Contains("Conveyor", k.Message);
        var m = Assert.Single(Check("Standard", "Crate"));
        Assert.Equal(FindingCode.InputError, m.Code); Assert.Contains("Pallet", m.Message);
    }

    [Fact]
    public void Template_Carries_Accel_0_3_Kind_And_Form()
    {
        var path = Path.Combine(Path.GetTempPath(), "kf-" + System.Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            ExcelTemplate.Write(path);
            var p = ExcelReader.Read(path);
            var t = Assert.Single(p.AgvTypes);
            Assert.Equal(0.3, t.Accel); Assert.Equal(0.3, t.Decel); Assert.Equal("Standard", t.Kind);
            Assert.Equal("Box", Assert.Single(p.Materials).Form);
            Assert.DoesNotContain(InputValidator.Validate(p, new[] { "CP_STBY", "CP_LOAD", "CP_UNLOAD" }), f => f.Code == FindingCode.InputError);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void An_Older_Workbook_Without_The_New_Columns_Still_Reads()
    {
        var path = Path.Combine(Path.GetTempPath(), "kf-" + System.Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            ExcelTemplate.Write(path);
            using (var wb = ExcelReader.Open(path))
            {
                wb.Worksheet("AGV_Types").Column(14).Delete();  // Kind
                wb.Worksheet("Materials").Column(13).Delete();  // Form
                wb.Save();
            }
            var p = ExcelReader.Read(path);
            Assert.Equal("", p.AgvTypes[0].Kind); Assert.Equal("", p.Materials[0].Form);
        }
        finally { File.Delete(path); }
    }
}
