using System;
using System.Collections.Generic;

namespace ASH.Core.Geometry;

public sealed class SpatialGrid<T>
{
    private readonly double _cell;
    private readonly Dictionary<(long, long), List<(Point2 P, T Item)>> _cells = new();

    public SpatialGrid(double cellSize) { _cell = cellSize; }

    private (long, long) Key(double x, double y) => ((long)Math.Floor(x / _cell), (long)Math.Floor(y / _cell));

    public void Add(Point2 p, T item)
    {
        var k = Key(p.X, p.Y);
        if (!_cells.TryGetValue(k, out var l)) _cells[k] = l = new();
        l.Add((p, item));
    }

    public IEnumerable<T> Near(Point2 p, double radius)
    {
        var (x0, y0) = Key(p.X - radius, p.Y - radius);
        var (x1, y1) = Key(p.X + radius, p.Y + radius);
        for (long x = x0; x <= x1; x++)
            for (long y = y0; y <= y1; y++)
                if (_cells.TryGetValue((x, y), out var l))
                    foreach (var e in l)
                        if (e.P.DistanceTo(p) <= radius) yield return e.Item;
    }
}
