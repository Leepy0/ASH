using System;

namespace ASH.Core.Update;

/// <summary>
/// Strict SemVer MAJOR.MINOR.PATCH[-pre][+build]: no leading zeros in numbers, no empty identifiers.
/// A pre-release is lower than the same number without one; build metadata is ignored.
/// </summary>
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
        if (string.IsNullOrEmpty(s)) return false;
        int plus = s!.IndexOf('+');
        if (plus >= 0)
        {
            if (!Identifiers(s.Substring(plus + 1), numericNoLeadingZero: false)) return false;
            s = s.Substring(0, plus);
        }
        string pre = "";
        int dash = s.IndexOf('-');
        if (dash >= 0)
        {
            pre = s.Substring(dash + 1);
            s = s.Substring(0, dash);
            if (!Identifiers(pre, numericNoLeadingZero: true)) return false;
        }
        var parts = s.Split('.');
        if (parts.Length != 3 || !Num(parts[0], out int a) || !Num(parts[1], out int b) || !Num(parts[2], out int c)) return false;
        v = new SemVersion(a, b, c, pre);
        return true;
    }

    private static bool Num(string t, out int n)
    {
        n = 0;
        if (t.Length == 0 || (t.Length > 1 && t[0] == '0')) return false;
        foreach (var ch in t) if (!IsDigit(ch)) return false;
        return int.TryParse(t, out n);
    }

    private static bool IsDigit(char c) => c >= '0' && c <= '9';

    // dot-separated, non-empty, [0-9A-Za-z-]; numeric pre-release identifiers must not have leading zeros
    private static bool Identifiers(string s, bool numericNoLeadingZero)
    {
        if (s.Length == 0) return false;
        foreach (var id in s.Split('.'))
        {
            if (id.Length == 0) return false;
            bool digits = true;
            foreach (var ch in id)
            {
                if (!(IsDigit(ch) || (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || ch == '-')) return false;
                if (!IsDigit(ch)) digits = false;
            }
            if (numericNoLeadingZero && digits && id.Length > 1 && id[0] == '0') return false;
        }
        return true;
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
            c = CompareId(x[i], y[i]);
            if (c != 0) return c;
        }
        return x.Length.CompareTo(y.Length);
    }

    private static int CompareId(string a, string b)
    {
        bool na = AllDigits(a), nb = AllDigits(b);
        int c = na && nb ? (a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b)) // no leading zeros: longer = bigger
              : na ? -1 : nb ? 1 : string.CompareOrdinal(a, b);
        return c < 0 ? -1 : c > 0 ? 1 : 0;
    }

    private static bool AllDigits(string s) { foreach (var ch in s) if (!IsDigit(ch)) return false; return true; }

    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (Pre.Length > 0 ? "-" + Pre : "");
}
