using ASH.Core.Geometry;

namespace ASH.Core.Layout;

public sealed class LayoutBlock
{
    public string Name { get; set; } = "";
    public int Cap { get; set; }
    public Rect2 Rect { get; set; }
    public string Id { get; set; } = "";
    /// <summary>Handle of the AGV_BLOCK tag holding NAME/CAP; "" when the rectangle has none (drawn by hand).</summary>
    public string TagId { get; set; } = "";
}

public sealed class LayoutStation
{
    public string Name { get; set; } = "";
    public Point2 Position { get; set; }
}
