using System;
using System.Collections.Generic;

namespace ASH.Plugin.Ui;

/// <summary>What the palette's route view asks AGVROUTE to do. Same hand-over as <see cref="Navigator"/>: the palette cannot touch the drawing, so it leaves the request here and runs the command.</summary>
internal static class RouteRequest
{
    internal enum Kind { Show, Clear, List }

    internal sealed class Request
    {
        public Request(Kind kind, string material) { Do = kind; Material = material; }
        public Kind Do { get; }
        public string Material { get; }
    }

    /// <summary>Read by AGVROUTE.</summary>
    internal static Request? Pending;

    /// <summary>Set by the palette: gets the materials (name, group) of the drawing's Excel input whenever AGVROUTE has read it.</summary>
    internal static Action<IReadOnlyList<(string Material, string Group)>>? Materials;

    /// <summary>False when the command was not sent (another command is running).</summary>
    public static bool Go(Kind kind, string material = "")
    {
        Pending = new Request(kind, material);
        if (AgvPalette.Send("AGVROUTE")) return true;
        Pending = null;
        return false;
    }
}
