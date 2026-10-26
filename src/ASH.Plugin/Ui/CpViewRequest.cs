using System.Collections.Generic;
using ASH.Core.Traffic;

namespace ASH.Plugin.Ui;

/// <summary>What the palette's group view asks AGVCPVIEW to do. Same hand-over as <see cref="Navigator"/>: the palette cannot touch the drawing, so it leaves the request here and runs the command.</summary>
internal static class CpViewRequest
{
    internal sealed class Request
    {
        public Request(CpViewMode mode, ISet<string>? selected) { Mode = mode; Selected = selected; }
        public CpViewMode Mode { get; }
        /// <summary>The groups that stay normal; null = every group (show all).</summary>
        public ISet<string>? Selected { get; }
    }

    /// <summary>Read by AGVCPVIEW.</summary>
    internal static Request? Pending;

    /// <summary>False when the command was not sent (another command is running).</summary>
    public static bool Go(CpViewMode mode, ISet<string>? selected)
    {
        Pending = new Request(mode, selected);
        if (AgvPalette.Send("AGVCPVIEW")) return true;
        Pending = null;
        return false;
    }
}
