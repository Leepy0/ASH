using ASH.Core;
using ASH.Core.Settings;

namespace ASH.Tests;

public class SeoulTimeTests
{
    [Fact] public void Utc_is_shifted_nine_hours()
        => Assert.Equal("2026-10-21 09:57:15", SeoulTime.Text(new DateTime(2026, 10, 21, 0, 57, 15, DateTimeKind.Utc)));

    [Fact] public void The_date_rolls_over_with_the_shift()
        => Assert.Equal("2026-01-01 08:30:00", SeoulTime.Text(new DateTime(2025, 12, 31, 23, 30, 0, DateTimeKind.Utc)));

    [Theory]
    [InlineData("2026-10-21T00:57:15Z", "2026-10-21 09:57:15")]
    [InlineData("2026-10-21T15:00:00Z", "2026-10-22 00:00:00")]
    [InlineData("2026-10-21T00:00:00+09:00", "2026-10-21 00:00:00")]   // an offset is honoured, not read as UTC
    [InlineData("not a date", "not a date")]
    [InlineData("", "")]
    public void Iso_text_is_converted_and_other_text_is_left_alone(string iso, string expected) => Assert.Equal(expected, SeoulTime.FromIso(iso));

    [Fact] public void Null_comes_back_empty() => Assert.Equal("", SeoulTime.FromIso(null));

    [Fact] public void Now_is_utc_plus_nine_whatever_the_pc_time_zone_is()
    {
        var before = DateTime.UtcNow.AddHours(9);
        var now = SeoulTime.Now;
        var after = DateTime.UtcNow.AddHours(9);
        Assert.InRange(now, before.AddSeconds(-1), after.AddSeconds(1));
        Assert.Equal(DateTimeKind.Unspecified, now.Kind);
    }

    // ---- the per-PC fillet radius lives next to the server address ----

    private static string Tmp() => Path.Combine(Path.GetTempPath(), "ash-user-" + Guid.NewGuid().ToString("N") + ".tsv");

    [Fact] public void Fillet_radius_is_saved_and_read_back()
    {
        var p = Tmp();
        try
        {
            new UserSettings("http://srv:8082", 1.5).Save(p);
            var back = UserSettings.Load(p);
            Assert.Equal(("http://srv:8082", 1.5), (back.ServerUrl, back.FilletRadiusM));
        }
        finally { File.Delete(p); }
    }

    [Fact] public void Without_a_radius_the_file_is_exactly_as_before()
    {
        var p = Tmp();
        try
        {
            new UserSettings("http://srv:8082").Save(p);
            Assert.Equal("ServerUrl\thttp://srv:8082\r\n", File.ReadAllText(p));
            Assert.Equal(0, UserSettings.Load(p).FilletRadiusM);
        }
        finally { File.Delete(p); }
    }

    [Fact] public void Changing_the_server_keeps_the_radius_and_the_other_way_round()
    {
        var p = Tmp();
        try
        {
            new UserSettings("http://a:1", 2.0).Save(p);
            (UserSettings.Load(p) with { ServerUrl = "http://b:2" }).Save(p);
            (UserSettings.Load(p) with { FilletRadiusM = 3.0 }).Save(p);
            var back = UserSettings.Load(p);
            Assert.Equal(("http://b:2", 3.0), (back.ServerUrl, back.FilletRadiusM));
        }
        finally { File.Delete(p); }
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-4")]
    [InlineData("0")]
    public void A_bad_radius_reads_as_not_set(string text)
    {
        var p = Tmp();
        try
        {
            File.WriteAllText(p, "ServerUrl\thttp://a:1\r\nFilletRadiusM\t" + text + "\r\n");
            Assert.Equal(0, UserSettings.Load(p).FilletRadiusM);
        }
        finally { File.Delete(p); }
    }

    [Fact] public void The_machine_file_supplies_only_the_address_not_the_users_radius()
    {
        var u = Tmp(); var m = Tmp();
        try
        {
            new UserSettings("", 1.8).Save(u);
            new UserSettings("http://machine:8082", 9.0).Save(m);
            var s = UserSettings.Load(u, m);
            Assert.Equal(("http://machine:8082", 1.8), (s.ServerUrl, s.FilletRadiusM));
        }
        finally { File.Delete(u); File.Delete(m); }
    }
}
