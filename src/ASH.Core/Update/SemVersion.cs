using System;

namespace ASH.Core.Update;

/// <summary>SemVer MAJOR.MINOR.PATCH[-pre][+build]. A pre-release is lower than the same number without one; build metadata is ignored.</summary>
public sealed class SemVersion : IComparable<SemVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string Pre { get; }

    private SemVersion(int major, int minor, int patch, string pre) { Major = major; Minor = minor; Patch = patch; Pre = pre; }

    public static bool TryParse(string? s, out SemVersion? v)
    {
        v = null;
        if (s == null || s.Trim().Length == 0) return false;
        s = s.Trim();
        int plus = s.IndexOf('+');
        if (plus >= 0) s = s.Substring(0, plus);
        string pre = "";
        int dash = s.IndexOf('-');
        if (dash >= 0) { pre = s.Substring(dash + 1); s = s.Substring(0, dash); if (pre.Length == 0) return false; }
        var parts = s.Split('.');
        if (parts.Length != 3 || !Num(parts[0], out int a) || !Num(parts[1], out int b) || !Num(parts[2], out int c)) return false;
        v = new SemVersion(a, b, c, pre);
        return true;
    }

    private static bool Num(string t, out int n)
    {
        n = 0;
        foreach (var ch in t) if (ch < '0' || ch > '9') return false;
        return t.Length > 0 && int.TryParse(t, out n);
    }

    public int CompareTo(SemVersion? o)
    {
        if (o is null) return 1;
        int c = Major.CompareTo(o.Major);
        if (c == 0) c = Minor.CompareTo(o.Minor);
        if (c == 0) c = Patch.CompareTo(o.Patch);
        if (c != 0) return c;
        if (Pre.Length == 0 || o.Pre.Length == 0) return Pre.Length == o.Pre.Length ? 0 : Pre.Length == 0 ? 1 : -1;
        var x = Pre.Split('.'); var y = o.Pre.Split('.');
        for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            bool nx = int.TryParse(x[i], out int ix), ny = int.TryParse(y[i], out int iy);
            c = nx && ny ? ix.CompareTo(iy) : nx ? -1 : ny ? 1 : string.CompareOrdinal(x[i], y[i]);
            if (c != 0) return c < 0 ? -1 : 1;
        }
        return x.Length.CompareTo(y.Length);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (Pre.Length > 0 ? "-" + Pre : "");
}
