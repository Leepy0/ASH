using System.Collections.Generic;

namespace ASH.Core.Traffic;

/// <summary>Hands out CP names per prefix, continuing after the highest existing number (AutoNamer rules).</summary>
public sealed class CpNaming
{
    private readonly List<string> _names;

    public CpNaming(IEnumerable<string> existingNames) => _names = new List<string>(existingNames);

    public string Next(string prefix)
    {
        var n = AutoNamer.Next(prefix, _names);
        _names.Add(n);
        return n;
    }
}
