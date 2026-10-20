using System;

namespace ASH.Core.Export;

/// <summary>A vehicle group without any standby/buffer CP; Group lets the UI word its own message.</summary>
public sealed class ExportException : InvalidOperationException
{
    public ExportException(string group) : base($"group {group} has no standby/buffer CP") { Group = group; }
    public string Group { get; }
}
