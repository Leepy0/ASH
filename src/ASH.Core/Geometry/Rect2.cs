namespace ASH.Core.Geometry;

public readonly record struct Rect2(Point2 Min, Point2 Max)
{
    public bool Contains(Point2 p) => p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y;
    public Rect2 Expand(double d) => new Rect2(new Point2(Min.X - d, Min.Y - d), new Point2(Max.X + d, Max.Y + d));
    public bool Intersects(Rect2 o) => Min.X <= o.Max.X && Max.X >= o.Min.X && Min.Y <= o.Max.Y && Max.Y >= o.Min.Y;
}
