using ASH.Core.Geometry;

namespace ASH.Core.Layout;

public sealed class LayoutCp
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Group { get; set; } = "";
    public string Station { get; set; } = "";
    public int Cap { get; set; }
    public Point2 Position { get; set; }
    public string Id { get; set; } = "";
}
