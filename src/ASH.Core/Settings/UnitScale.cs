namespace ASH.Core.Settings;

public static class UnitScale
{
    /// <summary>Drawing units per meter (Scale converts drawing units to meters): Scale 0.001 (mm drawing) gives 1000.</summary>
    public static double FromSettings(ProjectSettings s) => 1.0 / s.Scale;

    /// <summary>
    /// Model units (the AutoMod UNITS of the export, ExportOptions.Units) in one meter, or null for a unit ASH cannot convert.
    /// Everything ASH reads is in meters (drawing x Scale, Excel sizes and speeds) and is written to the model times this.
    /// </summary>
    public static double? ModelUnitsPerMeter(string? units)
    {
        switch ((units ?? "").Trim().ToLowerInvariant())
        {
            case "meters": return 1;
            case "feet": return 3.280839895;
            case "inches": return 39.37007874;
            case "centimeters": return 100;
            case "millimeters": return 1000;
            default: return null;
        }
    }

    /// <summary>
    /// Scale (drawing unit -> meters) for a new settings file, from the drawing's INSUNITS: inches, feet, mm, cm, m, km, yards.
    /// A drawing with no unit (0) or an unusual one gets 0.001: AGV layouts are drawn in millimetres.
    /// </summary>
    public static double FromInsUnits(int insUnits)
    {
        switch (insUnits)
        {
            case 1: return 0.0254;
            case 2: return 0.3048;
            case 4: return 0.001;
            case 5: return 0.01;
            case 6: return 1.0;
            case 7: return 1000.0;
            case 10: return 0.9144;
            default: return 0.001;
        }
    }
}
