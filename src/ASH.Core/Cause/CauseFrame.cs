using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Input;
using ASH.Core.Layout;
using ASH.Core.Runner;
using ASH.Core.Settings;

namespace ASH.Core.Cause;

/// <summary>Builds the analysers' CauseContext from what a caller has: the server's model-metre layout and ash.settings.tsv, or the plugin's drawing-unit layout.
/// Metres = (drawing - Origin) x Scale, the frame ExportOptions.ToModel and ArcLayout use.</summary>
public static class CauseFrame
{
    /// <summary>The drawing's layout in model metres (paths, CPs, blocks, stations, 3D assets, conveyors).</summary>
    public static Layout.Layout ToMetres(Layout.Layout l, Point2 origin, double scale)
    {
        Point2 P(Point2 p) => new((p.X - origin.X) * scale, (p.Y - origin.Y) * scale);
        var m = new Layout.Layout { Tolerance = l.Tolerance * scale };
        foreach (var p in l.Paths)
            m.Paths.Add(new LayoutPath(p.Name)
            {
                Id = p.Id, IsSpur = p.IsSpur, IsTwoWay = p.IsTwoWay, IsCrab = p.IsCrab,
                Segments = p.Segments.Select(s => s switch
                {
                    LineSeg g => (Segment)new LineSeg(P(g.Start), P(g.End)),
                    ArcSeg a => new ArcSeg(P(a.Center), P(a.Start), a.SweepDeg),
                    _ => throw new ArgumentException("알 수 없는 선분: " + s.GetType().Name),
                }).ToList(),
            });
        foreach (var c in l.Cps)
            m.Cps.Add(new LayoutCp { Name = c.Name, Type = c.Type, Group = c.Group, Cap = c.Cap, Position = P(c.Position), Id = c.Id });
        foreach (var b in l.Blocks)
            m.Blocks.Add(new LayoutBlock { Name = b.Name, Cap = b.Cap, Rect = new Rect2(P(b.Rect.Min), P(b.Rect.Max)), Id = b.Id, TagId = b.TagId });
        foreach (var s in l.Stations) m.Stations.Add(new LayoutStation { Name = s.Name, Position = P(s.Position) });
        foreach (var a in l.Assets) m.Assets.Add(Cel.AssetShapes.InMetres(a, origin, scale));
        foreach (var c in l.Conveyors)
            m.Conveyors.Add(new LayoutConveyor
            {
                Name = c.Name, GraphicOnly = c.GraphicOnly, WidthM = c.WidthM, Id = c.Id,
                Segments = c.Segments.Select(s => s is ArcSeg a ? new ArcSeg(P(a.Center), P(a.Start), a.SweepDeg) : (Segment)new LineSeg(P(s.Start), P(s.End))).ToList(),
            });
        return m;
    }

    /// <summary>Model metres back to drawing units (what the plugin zooms to).</summary>
    public static Point2 ToDrawing(double xMetres, double yMetres, Point2 origin, double scale) =>
        new(xMetres / scale + origin.X, yMetres / scale + origin.Y);

    /// <summary>Vehicle size and claim/reach in metres: from ash.settings.tsv when there is one (<paramref name="s"/>), else the first AgvType's size and the ProjectSettings
    /// defaults. BlockReach null (automatic) is 0.</summary>
    public static CauseContext Context(Layout.Layout metres, ProjectInput input, ProjectSettings? s, ResultSet? result = null, ReportStats? report = null)
    {
        var def = new ProjectSettings();
        var t = input.AgvTypes.FirstOrDefault(a => a.Length > 0 && a.Width > 0);
        double len = s?.VehicleLength ?? t?.Length ?? def.VehicleLength, wid = s?.VehicleWidth ?? t?.Width ?? def.VehicleWidth;
        var p = s ?? def;
        return new CauseContext(metres, input, result, report, len, wid, p.ClaimDist ?? p.SafetyGap, p.BlockReach ?? 0, p.SafetyGap);
    }

    /// <summary>Layout-only findings of a drawing read in drawing units. DetourCauses reads the Excel Routes, so its findings are left out when the input has none.</summary>
    public static IReadOnlyList<CauseItem> StaticOf(Layout.Layout drawing, ProjectSettings s, ProjectInput? input)
    {
        input ??= new ProjectInput();
        var ctx = Context(ToMetres(drawing, s.Origin, s.Scale), input, s);
        // the Excel's vehicle size beats the settings file's template default
        if (input.AgvTypes.FirstOrDefault(a => a.Length > 0 && a.Width > 0) is { } t) ctx = ctx with { VehicleLength = t.Length, VehicleWidth = t.Width };
        return CauseAnalysis.Static(ctx).Where(i => i.Kind != CauseKinds.Detour || input.Routes.Count > 0).ToList();
    }
}
