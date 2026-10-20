namespace ASH.Core.Geometry;

public readonly record struct Point2(double X, double Y)
{
    public double DistanceTo(Point2 other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return System.Math.Sqrt(dx * dx + dy * dy);
    }
}
