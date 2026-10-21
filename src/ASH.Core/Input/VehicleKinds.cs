using System;
using System.Collections.Generic;
using System.Linq;

namespace ASH.Core.Input;

/// <summary>What a material is shipped as. Empty in the workbook means Box.</summary>
public static class MaterialForms
{
    public const string Box = "Box", Pallet = "Pallet", Trolley = "Trolley";
    public static readonly string[] All = { Box, Pallet, Trolley };

    /// <summary>The canonical name for a cell value (case-insensitive; empty = Box), or null when it is not a form.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Box;
        return All.FirstOrDefault(f => string.Equals(f, value!.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// AGV shapes. A type's Kind says how it takes a load, and with that what it can carry. To add a shape, add a line to <see cref="All"/>: name, label and the forms it
/// carries; AGV_Types.Kind then accepts it and the check against the materials' Form follows. Nothing in the AutoMod model depends on the Kind yet (the vehicle graphic
/// and its load/unload time come from the AGV_Types row).
/// </summary>
public static class VehicleKinds
{
    public sealed record Kind(string Name, string Label, string[] Carries);

    public static readonly Kind[] All =
    {
        new Kind("Standard", "표준", new[] { MaterialForms.Box, MaterialForms.Pallet, MaterialForms.Trolley }),
        new Kind("Conveyor", "벨트 C/V 상차형", new[] { MaterialForms.Box, MaterialForms.Pallet }),
        new Kind("Ant", "대차 리프팅(ANT)", new[] { MaterialForms.Trolley }),
        new Kind("Magnet", "전자석 견인(Magnet)", new[] { MaterialForms.Trolley }),
    };

    /// <summary>The kind for a cell value (case-insensitive; empty = Standard), or null when it is not one.</summary>
    public static Kind? Find(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return All[0];
        return All.FirstOrDefault(k => string.Equals(k.Name, value!.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static string Names => string.Join(" / ", All.Select(k => k.Name));
}
