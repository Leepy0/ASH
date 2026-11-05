using System;
using System.Collections.Generic;

namespace ASH.Core.Lite;

/// <summary>One block definition as read from the drawing (Task 7 fills it from ZWCAD): its own geometry in block-local 3D, and the inserts inside it. Model space is a BlockDef too.
/// No name: nothing downstream may decide by one (names are for the human report only, LiteStat.BlockRow).</summary>
public sealed class BlockDef
{
    /// <summary>In original order; entities on layer "0" take the insert's layer at placement (R8).</summary>
    public List<Geo3> Geo { get; set; } = new();
    /// <summary>In original order.</summary>
    public List<BlockUse> Uses { get; set; } = new();
}

/// <summary>An INSERT inside a definition: which definition, its transform in the parent's local coordinates (BlockReference.BlockTransform), and the insert's own layer
/// ("0" inherits from the parent insert, R8).</summary>
public readonly record struct BlockUse(int Def, Affine3 Xf, string Layer);

/// <summary>The input of LiteBuild: every definition the drawing uses, model space included. Defs are numbered in first-seen order (the writing order's "definition", R8), so the
/// reader adds a definition the first time an insert names it. Layers: every layer of the drawing with its colour and state; a layer missing here counts as on and thawed.</summary>
public sealed class LiteBlocks
{
    public List<BlockDef> Defs { get; } = new();
    /// <summary>Index of model space in Defs.</summary>
    public int Root { get; set; }
    public Dictionary<string, LiteLayerState> Layers { get; } = new(StringComparer.Ordinal);
    public int InsUnits { get; set; } = 4;
}
