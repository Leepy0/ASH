namespace ASH.Core.Traffic;

/// <summary>BlockReach null = each junction arm reaches only as far as the crossing vehicles need (BlockGenerator.ClearDist).</summary>
public sealed record TrafficParams(double VehicleLength, double VehicleWidth, double SafetyGap = 0.5, double? BlockReach = null,
    double? ClaimDist = null, double CpSpacing = 10, bool SpacingCps = false, bool CurveCps = false)
{
    public double EffectiveClaimDist => ClaimDist ?? SafetyGap;
}
