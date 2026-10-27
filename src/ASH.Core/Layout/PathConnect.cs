using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Geometry;

namespace ASH.Core.Layout;

/// <summary>Paths after Connect: Removed (original ids) are replaced by Added; Notes says what was connected, one line each.</summary>
public sealed record ConnectResult(List<LayoutPath> Paths, List<string> Removed, List<LayoutPath> Added, List<string> Notes);

/// <summary>A dangling end (Flow = false) or a one-way node vehicles can only enter or only leave (Flow = true).</summary>
public sealed record PathGap(Point2 At, string Path, bool Flow);

/// <summary>Geometry only: ends are snapped by distance, path directions are not looked at (the gap markers show flow dead ends).</summary>
public static class PathConnect
{
    /// <summary>
    /// Every free end (touching no other path end) of a movable path (null = all) moves onto the nearest other path end within gap,
    /// else onto the nearest point of another path within gap, which is then split there (a cut within gap of that path's end snaps to the end instead).
    /// Changed paths get ids "&lt;original id&gt;#k", like PathSplitter.SplitAt pieces.
    /// </summary>
    public static ConnectResult Connect(IReadOnlyList<LayoutPath> paths, ICollection<string>? movable, double gap, double tol)
    {
        var work = paths.Where(p => p.Segments.Count > 0).ToList();
        var notes = new List<string>();
        var taken = new HashSet<string>(paths.Select(p => p.Name));
        bool Movable(LayoutPath p) => movable == null || movable.Contains(Origin(p.Id));
        // shortcut: every free end scans every path (ends x segments); fine for a few thousand paths, add a spatial grid if AGVCONNECT on everything gets slow
        for (int i = 0; gap > tol && i < work.Count; i++)
            foreach (var atStart in new[] { true, false })
            {
                var p = work[i];
                if (!Movable(p)) break;
                var e = atStart ? p.Start : p.End;
                if (Enumerable.Range(0, work.Count).Any(j => j != i && (work[j].Start.DistanceTo(e) <= tol || work[j].End.DistanceTo(e) <= tol))) continue;

                int target = -1;
                bool targetStart = false;
                double best = gap;
                for (int j = 0; j < work.Count; j++)
                {
                    if (j == i) continue;
                    if (work[j].Start.DistanceTo(e) <= best) { best = work[j].Start.DistanceTo(e); target = j; targetStart = true; }
                    if (work[j].End.DistanceTo(e) <= best) { best = work[j].End.DistanceTo(e); target = j; targetStart = false; }
                }
                Point2 to;
                string what;
                if (target >= 0)
                {
                    to = targetStart ? work[target].Start : work[target].End;
                    what = $"{work[target].Name} {(targetStart ? "시작" : "끝")}";
                }
                else
                {
                    var (j, d, off) = Nearest(work, i, e);
                    if (j < 0 || off > gap) continue;
                    var q = work[j];
                    if (d <= gap || d >= q.Length - gap)
                    {
                        bool qs = d <= gap;
                        to = qs ? q.Start : q.End;
                        what = $"{q.Name} {(qs ? "시작" : "끝")}";
                    }
                    else
                    {
                        if (MoveEnd(p, atStart, q.PointAt(d), tol) == null) continue; // never split a path nothing will join
                        var pieces = PathSplitter.SplitAt(q, new[] { d }, tol);
                        if (pieces.Count < 2) continue;
                        int k = 1;
                        foreach (var piece in pieces)
                        {
                            while (!taken.Add(q.Name + "_" + k)) k++; // same suffix skipping as AGVSPLIT
                            piece.Name = q.Name + "_" + k++;
                        }
                        work[j] = pieces[0];
                        work.AddRange(pieces.Skip(1));
                        to = pieces[0].End;
                        what = $"{q.Name} 중간 → {string.Join(", ", pieces.Select(x => x.Name))}";
                    }
                }
                if (e.DistanceTo(to) > tol)
                {
                    var moved = MoveEnd(p, atStart, to, tol);
                    if (moved == null) continue;
                    work[i] = moved;
                }
                notes.Add($"{p.Name} {(atStart ? "시작" : "끝")} → {what} ({e.DistanceTo(to):0.###})");
            }
        var added = work.Where(p => p.Id.IndexOf('#') >= 0).ToList();
        return new ConnectResult(work, added.Select(p => Origin(p.Id)).Distinct().ToList(), added, notes);
    }

    /// <summary>Ends touching no other path (spur ends excepted) and one-way nodes with only incoming or only outgoing paths (none with a spur).</summary>
    public static List<PathGap> Gaps(Layout l)
    {
        var res = new List<PathGap>();
        foreach (var n in PathGraph.Build(l).Nodes)
        {
            if (n.Ends.Any(x => x.Path.IsSpur)) continue; // a spur is a dead end by design
            if (n.Degree == 1)
            {
                var p = n.Ends[0].Path;
                // an end on another path's middle touches it (not split yet: AGVCHECK reports that)
                var (j, _, off) = Nearest(l.Paths, l.Paths.IndexOf(p), n.Position);
                if (j < 0 || off > l.Tolerance) res.Add(new PathGap(n.Position, p.Name, false));
            }
            else if (n.InCount == 0 || n.OutCount == 0) res.Add(new PathGap(n.Position, n.Ends[0].Path.Name, true));
        }
        return res;
    }

    private static string Origin(string id)
    {
        int k = id.IndexOf('#');
        return k >= 0 ? id.Substring(0, k) : id;
    }

    // nearest point of any path but skip: (path index, distance along it, distance from pt); -1 when there is none
    private static (int Path, double Along, double Off) Nearest(IReadOnlyList<LayoutPath> paths, int skip, Point2 pt)
    {
        (int, double, double) best = (-1, 0, double.MaxValue);
        for (int j = 0; j < paths.Count; j++)
        {
            if (j == skip) continue;
            double o = 0;
            foreach (var s in paths[j].Segments)
            {
                double d = s.DistanceAlong(pt), off = s.PointAt(d).DistanceTo(pt);
                if (off < best.Item3) best = (j, o + d, off);
                o += s.Length;
            }
        }
        return best;
    }

    // a line end moves; an arc keeps its shape and gets a short straight bridge. Null when the line would shrink to nothing.
    private static LayoutPath? MoveEnd(LayoutPath p, bool atStart, Point2 to, double tol)
    {
        var segs = p.Segments.ToList();
        int k = atStart ? 0 : segs.Count - 1;
        if (segs[k] is LineSeg l)
        {
            var n = atStart ? new LineSeg(to, l.End) : new LineSeg(l.Start, to);
            if (n.Length <= tol) return null;
            segs[k] = n;
        }
        else if (atStart) segs.Insert(0, new LineSeg(to, segs[0].Start));
        else segs.Add(new LineSeg(segs[k].End, to));
        return new LayoutPath(p.Name) { Id = p.Id.IndexOf('#') >= 0 ? p.Id : p.Id + "#0", IsSpur = p.IsSpur, IsTwoWay = p.IsTwoWay, IsCrab = p.IsCrab, Segments = segs };
    }
}
