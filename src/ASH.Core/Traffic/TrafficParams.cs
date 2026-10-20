namespace ASH.Core.Traffic;

public sealed record TrafficParams(double VehicleLength, double VehicleWidth, double SafetyGap = 0.5, double? BlockReach = null,
    double? ClaimDist = null, double CpSpacing = 10, bool SpacingCps = false, bool CurveCps = false)
{
    public double EffectiveBlockReach => BlockReach ?? VehicleLength + SafetyGap;
    public double EffectiveClaimDist => ClaimDist ?? SafetyGap;
}
