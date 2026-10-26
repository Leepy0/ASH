using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Input;

namespace ASH.Core.Runner;

/// <summary>생산량 한계: the decided counts run again with production raised step by step (every material's takt divided by 1 + pct/100),
/// each step judged like the search's confirm round. params.txt carries the takt, so the built model runs every step without a rebuild.</summary>
public static class ProductionLimit
{
    /// <summary>+0 % .. +50 % in 10 % steps.</summary>
    public static readonly IReadOnlyList<int> Pcts = new[] { 0, 10, 20, 30, 40, 50 };

    /// <summary>Steps stop after this many failures (the one after the first failure tells a seed's bad luck from the real limit).</summary>
    public const int MaxFails = 2;

    /// <summary>Seeds per step: the settings' replications, one with a fixed initial stock (as <see cref="SearchStrategy.Confirm"/> runs).</summary>
    public static int Replications(Input.Settings s) => SearchStrategy.FixedStock(s) ? 1 : Math.Max(1, s.Replications);

    /// <summary>A copy of the input with production raised by <paramref name="pct"/> %. A conveyor-fed material carries its conveyor's product takt
    /// in TaktTime, so its line speeds up too (the conveyor clock reads the takt from params.txt).</summary>
    public static ProjectInput Scale(ProjectInput input, int pct)
    {
        double f = 1 + pct / 100.0;
        // shortcut: the lists are copied one by one; a new list on ProjectInput must be added here
        var p = new ProjectInput { Settings = input.Settings };
        p.AgvTypes.AddRange(input.AgvTypes);
        p.Groups.AddRange(input.Groups);
        p.Materials.AddRange(input.Materials.Select(m => m with { TaktTime = m.TaktTime / f }));
        p.Routes.AddRange(input.Routes);
        p.Stations.AddRange(input.Stations);
        p.Ext.AddRange(input.Ext);
        p.Conveyors.AddRange(input.Conveyors.Select(c => c with { ProductTakt = c.ProductTakt / f }));
        p.DisabledMaterials.AddRange(input.DisabledMaterials);
        return p;
    }
}
