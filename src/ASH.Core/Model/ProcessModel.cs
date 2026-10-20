using System.Collections.Generic;
using ASH.Core.Geometry;

namespace ASH.Core.Model;

public sealed record ProcEntity(string Name);
public sealed record QueueEntity(string Name, long Cap = 2147483647, int Count = 0);
public sealed record BlockEntity(string Name, int Cap, Rect2 Rect);
/// <summary>Array when ArraySize &gt; 0; Init only applies to scalars.</summary>
public sealed record VarEntity(string Name, string Type, int ArraySize = 0, string? Init = null);
public sealed record AttEntity(string Name, string Type);
public sealed record RnStreamEntity(string Name, long Seed);

public sealed class ProcessModel
{
    public List<ProcEntity> Procs { get; } = new List<ProcEntity>();
    public List<string> LoadTypes { get; } = new List<string>();
    public List<QueueEntity> Queues { get; } = new List<QueueEntity>();
    public List<string> Labels { get; } = new List<string>();
    public List<BlockEntity> Blocks { get; } = new List<BlockEntity>();
    public List<string> Orders { get; } = new List<string>();
    public List<string> Tables { get; } = new List<string>();
    public List<AttEntity> Atts { get; } = new List<AttEntity>();
    public List<VarEntity> Vars { get; } = new List<VarEntity>();
    public List<RnStreamEntity> RnStreams { get; } = new List<RnStreamEntity>();
    public List<string> Funcs { get; } = new List<string>();
    public string LogicSource { get; set; } = "";
}
