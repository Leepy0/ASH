using System;
using System.Globalization;

namespace ASH.Core;

/// <summary>
/// Every time shown to people or put in a file name is Seoul time (UTC+9; Korea has no daylight saving time), whatever time zone the PC is set to.
/// It starts from the PC's clock in UTC, so a PC whose clock itself is wrong still shows a wrong time: fix that clock in Windows.
/// Machine-readable stamps (ASH.json "published") stay UTC with a trailing Z.
/// </summary>
public static class SeoulTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(9);
    public const string Format = "yyyy-MM-dd HH:mm:ss";

    public static DateTime Now => FromUtc(DateTime.UtcNow);

    /// <summary>The Seoul wall-clock time of a UTC instant (Kind Unspecified, it is a plain wall-clock value).</summary>
    public static DateTime FromUtc(DateTime utc) => DateTime.SpecifyKind(utc.ToUniversalTime() + Offset, DateTimeKind.Unspecified);

    public static string Text(DateTime utc) => FromUtc(utc).ToString(Format, CultureInfo.InvariantCulture);

    public static string NowText => Text(DateTime.UtcNow);

    /// <summary>"2026-10-21T00:57:15Z" becomes "2026-10-21 09:57:15"; text that is not a date comes back as it was.</summary>
    public static string FromIso(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return iso ?? "";
        return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? Text(d) : iso!;
    }
}
