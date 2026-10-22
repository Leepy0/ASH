using ASH.Core.Export;
using ASH.Core.Geometry;
using ASH.Core.Input;
using ASH.Core.Model;
using ASH.Tests.Layout;

namespace ASH.Tests.AutoMod;

/// <summary>
/// A model for looking at the vehicle and load pictures in AutoMod: one square loop per vehicle kind, every form the kind carries, each with an empty
/// return, the 5 m loops two by two. Builds into ASH_SHAPES_DIR\&lt;variant&gt; (kept) when that and ASH_AM_HOME are set; open &lt;variant&gt;\shapes.exe to look.
/// </summary>
public class ShapeShowcaseTests
{
    static string? AmHome => Environment.GetEnvironmentVariable("ASH_AM_HOME");
    static string? OutDir => Environment.GetEnvironmentVariable("ASH_SHAPES_DIR");

    /// <summary>drawingPerMeter: drawing units in one metre (1000 = a mm drawing), drawn and exported with Scale = 1 / drawingPerMeter.</summary>
    public static (ProjectInput Input, ASH.Core.Layout.Layout Layout) Showcase(double drawingPerMeter)
    {
        var p = new ProjectInput { Settings = new ASH.Core.Input.Settings { Units = "Meters", SimLengthSec = 3600, WarmupSec = 0, InitStockMode = "Fixed" } };
        var l = new ASH.Core.Layout.Layout { Tolerance = 0.001 * drawingPerMeter };
        double u = drawingPerMeter;
        int m = 0;
        foreach (var (kind, i) in VehicleKinds.All.Select((k, i) => (k, i)))
        {
            string g = "G" + (i + 1), s = "S00" + (i + 1), ld = "L00" + (i + 1), ul = "U00" + (i + 1);
            // one-way square loop, 5 m sides, 2 x 2 loops (AutoMod's first view fits the whole layout: small keeps the vehicles readable)
            double x = i % 2 * 7, y = i / 2 * 7, a = 5;
            l.Paths.Add(L.Line($"p{i}a", x * u, y * u, (x + a) * u, y * u));
            l.Paths.Add(L.Line($"p{i}b", (x + a) * u, y * u, (x + a) * u, (y + a) * u));
            l.Paths.Add(L.Line($"p{i}c", (x + a) * u, (y + a) * u, x * u, (y + a) * u));
            l.Paths.Add(L.Line($"p{i}d", x * u, (y + a) * u, x * u, y * u));
            l.Cps.Add(L.Cp(s, (x + 1.5) * u, y * u, "standby", g));
            l.Cps.Add(L.Cp(ld, (x + a) * u, (y + 2.5) * u, "load"));
            l.Cps.Add(L.Cp(ul, (x + 2.5) * u, (y + a) * u, "unload"));
            p.AgvTypes.Add(new AgvType("T_" + kind.Name, 1.0, 1.5, 0.5, 0.5, 1, 3, 3, 0, 0, kind.Name == "Ant" ? 1.2 : 1.6, kind.Name == "Ant" ? 0.8 : 1.0, "red", Kind: kind.Name));
            p.Groups.Add(new AgvGroup(g, "T_" + kind.Name, 1, 2, new List<string> { s }, "Nearest"));
            p.Stations.Add(new StationInfo("ST" + (i + 1), ul, 0));
            foreach (var form in kind.Carries)
            {
                var mat = "M" + ++m;
                p.Materials.Add(new MaterialInfo(mat, "ST" + (i + 1), g, 40, 1, 1, 2, 1, 1, 1, true, Form: form));
                p.Routes.Add(new Route(mat, ld, ul, null, ul, ld, 0));
            }
        }
        return (p, l);
    }

    [SkippableTheory]
    [InlineData("m", 1.0, "Meters")]
    [InlineData("mm", 1000.0, "Meters")]
    [InlineData("mm_units", 1000.0, "Millimeters")]
    public void Build_Showcase(string variant, double drawingPerMeter, string units)
    {
        Skip.If(AmHome is null || OutDir is null, "ASH_AM_HOME or ASH_SHAPES_DIR not set");
        var (input, l) = Showcase(drawingPerMeter);
        var dir = Path.Combine(OutDir!, variant);
        Directory.CreateDirectory(dir);
        var o = new ExportOptions(Units: units, Scale: 1 / drawingPerMeter, Groups: ModelBuilder.VehicleGroups(input));
        var r = ModelBuilder.Build(new ModelBuildRequest(dir, "shapes", input, l, o), AmHome!, TimeSpan.FromMinutes(5));
        Assert.DoesNotContain(r.Findings, f => f.Code == ASH.Core.Layout.FindingCode.InputError);
        Assert.True(r.Build!.Success, r.Build.MakeErrText + r.Build.Am2ErrText);
    }
}
