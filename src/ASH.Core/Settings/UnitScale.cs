namespace ASH.Core.Settings;

public static class UnitScale
{
    /// <summary>Drawing units per meter (Scale converts drawing units to meters): Scale 0.001 (mm drawing) gives 1000.</summary>
    public static double FromSettings(ProjectSettings s) => 1.0 / s.Scale;

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
