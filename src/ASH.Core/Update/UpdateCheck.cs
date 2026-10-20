using System;
using System.IO;

namespace ASH.Core.Update;

public enum UpdateState { UpToDate, Available, BelowMinimum, ServerOlder }

public sealed record UpdateStatus(UpdateState State, string Message);

public static class UpdateCheck
{
    /// <exception cref="ArgumentException"><paramref name="current"/> is not SemVer.</exception>
    /// <exception cref="InvalidDataException">The manifest's version or minSupported is not SemVer.</exception>
    public static UpdateStatus Evaluate(string current, ReleaseManifest m)
    {
        if (!SemVersion.TryParse(current, out var cur)) throw new ArgumentException("현재 버전 형식 오류: " + current, nameof(current));
        if (!SemVersion.TryParse(m.Version, out var latest)) throw new InvalidDataException("배포 정보의 version 형식 오류: " + m.Version);
        SemVersion? min = null;
        if (m.MinSupported.Length > 0 && !SemVersion.TryParse(m.MinSupported, out min)) throw new InvalidDataException("배포 정보의 minSupported 형식 오류: " + m.MinSupported);

        if (min != null && cur!.CompareTo(min) < 0)
            return new(UpdateState.BelowMinimum, $"현재 버전 {cur}은(는) 지원 최소 버전 {min} 미만입니다. 최신 {latest}로 업데이트하세요.");
        int c = cur!.CompareTo(latest);
        if (c < 0) return new(UpdateState.Available, $"새 버전 {latest}이(가) 있습니다 (현재 {cur}). AGVUPDATE로 받으세요.");
        if (c == 0) return new(UpdateState.UpToDate, $"최신 버전입니다 ({cur}).");
        return new(UpdateState.ServerOlder, $"서버의 배포 버전 {latest}이(가) 현재 {cur}보다 낮습니다.");
    }
}
