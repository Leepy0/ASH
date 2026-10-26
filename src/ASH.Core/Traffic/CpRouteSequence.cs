using System.Linq;

namespace ASH.Core.Traffic;

/// <summary>F4 AGVCPROUTE step machine: standby (1, advances by itself) → load → unload → rtnload → rtnunload → finish (1, ends).
/// The middle four take any number of CPs; Next or Skip moves on.</summary>
public sealed class CpRouteSequence
{
    public static readonly string[] Types = { "standby", "load", "unload", "rtnload", "rtnunload", "finish" };
    private static readonly string[] Labels = { "대기", "로딩", "언로딩", "복귀 로딩", "복귀 언로딩", "종료" };

    private readonly int[] _counts = new int[Types.Length];

    public int Index { get; private set; }
    public bool Done { get; private set; }
    public string Type => Types[Index];

    /// <summary>standby and finish take exactly one CP.</summary>
    private bool Single => Index == 0 || Index == Types.Length - 1;

    /// <summary>Records a CP placed in the current step. True when that moved the sequence on (standby) or ended it (finish).</summary>
    public bool Placed()
    {
        if (Done) return false;
        _counts[Index]++;
        if (!Single) return false;
        Next();
        return true;
    }

    /// <summary>Enter/Space: leave the current step (with whatever was placed). After the last step the sequence is done.</summary>
    public void Next()
    {
        if (Index == Types.Length - 1) Done = true;
        else if (!Done) Index++;
    }

    /// <summary>A step left empty is skipped (a route without a return trip): same as Next.</summary>
    public void Skip() => Next();

    public int Count(string type) => _counts[System.Array.IndexOf(Types, type)];

    /// <summary>"[2/6] load 로딩 — 클릭: 배치, Enter/Space: 다음, S: 건너뛰기, Esc: 끝"</summary>
    public string Prompt() => $"[{Index + 1}/{Types.Length}] {Type} {Labels[Index]} — 클릭: 배치, Enter/Space: 다음, S: 건너뛰기, Esc: 끝";

    /// <summary>"G1: standby 1, load 2, unload 2, rtnload 0, rtnunload 0, finish 1"</summary>
    public string Summary(string group) => group + ": " + string.Join(", ", Types.Select((t, i) => $"{t} {_counts[i]}"));
}
