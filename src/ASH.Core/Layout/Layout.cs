using System.Collections.Generic;

namespace ASH.Core.Layout;

public sealed class Layout
{
    public List<LayoutPath> Paths { get; set; } = new();
    public List<LayoutCp> Cps { get; set; } = new();
    public List<LayoutBlock> Blocks { get; set; } = new();
    public List<LayoutStation> Stations { get; set; } = new();
    /// <summary>3D picture only (ASH_3D_* blocks, AGV_3D_WALL / AGV_3D_FENCE lines): drawn by a static system, no effect on the simulation.</summary>
    public List<LayoutAsset> Assets { get; set; } = new();
    /// <summary>AGV_CONV polylines (named, may carry products) and AGV_3D_CONV polylines (GraphicOnly: sections without stations or logic).</summary>
    public List<LayoutConveyor> Conveyors { get; set; } = new();
    public double Tolerance { get; set; } = 0.001;
}
