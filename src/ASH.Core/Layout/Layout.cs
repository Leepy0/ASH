using System.Collections.Generic;

namespace ASH.Core.Layout;

public sealed class Layout
{
    public List<LayoutPath> Paths { get; set; } = new();
    public List<LayoutCp> Cps { get; set; } = new();
    public List<LayoutBlock> Blocks { get; set; } = new();
    public List<LayoutStation> Stations { get; set; } = new();
    public double Tolerance { get; set; } = 0.001;
}
