using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using ASH.Core.Layout;
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.GraphicsInterface;
using Polyline = ZwSoft.ZwCAD.DatabaseServices.Polyline;

namespace ASH.Plugin.Cad;

/// <summary>AGVFLOW: small arrows run along every path for a few seconds as transient graphics (never in the drawing, nothing to undo), then vanish.</summary>
internal static class FlowPreview
{
    private const double Seconds = 5, MaxArrows = 1500;
    private static Timer? _timer;
    private static List<Polyline>? _arrows;
    private static Document? _doc;

    public static void Start(Document doc, IReadOnlyList<LayoutPath> paths, double size)
    {
        Stop();
        // arrows four lengths apart, fewer on a large layout so a frame stays cheap
        double spacing = Math.Max(4 * size, paths.Sum(p => p.Length) / MaxArrows);
        var tm = TransientManager.CurrentTransientManager;
        _doc = doc;
        _arrows = new List<Polyline>();
        try
        {
            foreach (var a in PathFlow.Frame(paths, spacing, size, 0))
            {
                var pl = new Polyline { ColorIndex = 7 }; // black or white, whatever the background
                pl.AddVertexAt(0, new Point2d(a.Left.X, a.Left.Y), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(a.Tip.X, a.Tip.Y), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(a.Right.X, a.Right.Y), 0, 0, 0);
                _arrows.Add(pl);
                tm.AddTransient(pl, TransientDrawingMode.DirectShortTerm, 128, new IntegerCollection());
            }
        }
        catch (System.Exception ex) { PluginLog.Write("AGVFLOW: transient graphics failed", ex); doc.Editor.WriteMessage("\n흐름 보기를 그리지 못했습니다: " + ex.Message); Stop(); return; }
        var clock = Stopwatch.StartNew();
        var timer = new Timer { Interval = 50 };
        timer.Tick += (o, e) =>
        {
            try
            {
                if (clock.Elapsed.TotalSeconds >= Seconds || _arrows == null) { Stop(); return; }
                var frame = PathFlow.Frame(paths, spacing, size, clock.Elapsed.TotalSeconds); // one gap per second
                for (int i = 0; i < _arrows.Count && i < frame.Count; i++)
                {
                    var pl = _arrows[i];
                    pl.SetPointAt(0, new Point2d(frame[i].Left.X, frame[i].Left.Y));
                    pl.SetPointAt(1, new Point2d(frame[i].Tip.X, frame[i].Tip.Y));
                    pl.SetPointAt(2, new Point2d(frame[i].Right.X, frame[i].Right.Y));
                    tm.UpdateTransient(pl, new IntegerCollection());
                }
                doc.Editor.UpdateScreen();
            }
            catch (System.Exception ex) { PluginLog.Write("AGVFLOW: frame failed", ex); Stop(); }
        };
        _timer = timer;
        timer.Start();
    }

    /// <summary>Stops the animation and removes every arrow. Safe to call any time.</summary>
    public static void Stop()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
        if (_arrows == null) return;
        var tm = TransientManager.CurrentTransientManager;
        foreach (var pl in _arrows)
        {
            try { tm.EraseTransient(pl, new IntegerCollection()); } catch (System.Exception ex) { PluginLog.Write("AGVFLOW: erase failed", ex); }
            pl.Dispose();
        }
        _arrows = null;
        try { _doc?.Editor.UpdateScreen(); } catch (System.Exception) { } // the drawing may be closed by now
        _doc = null;
    }
}
