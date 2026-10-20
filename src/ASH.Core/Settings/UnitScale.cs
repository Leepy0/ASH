namespace ASH.Core.Settings;

public static class UnitScale
{
    /// <summary>Drawing units per meter (Scale converts drawing units to meters): Scale 0.001 (mm drawing) gives 1000.</summary>
    public static double FromSettings(ProjectSettings s) => 1.0 / s.Scale;
}
