using ASH.Core.Geometry;

namespace ASH.Core.Asy;

public abstract record PathPiece;

public sealed record LinePiece(Point2 Beg, Point2 End) : PathPiece;

public sealed record ArcPiece(Point2 Center, Point2 Beg, int AngleTenths) : PathPiece
{
    // Positive angle = CCW, negative = CW.
    public Point2 End
    {
        get
        {
            double dx = Beg.X - Center.X, dy = Beg.Y - Center.Y;
            double r = System.Math.Sqrt(dx * dx + dy * dy);
            double t = System.Math.Atan2(dy, dx) + AngleTenths / 10.0 * System.Math.PI / 180.0;
            return new Point2(Center.X + r * System.Math.Cos(t), Center.Y + r * System.Math.Sin(t));
        }
    }
}

public sealed record GuidePath(string Name, string Type, bool IsSpur, bool IsCrab, PathPiece Piece);
