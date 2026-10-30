using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;

namespace ASH.Core.Input;

/// <summary>One difference: <paramref name="Kind"/> is a short label for a list column (<see cref="ExcelDrawingDiff.Missing"/> ...).</summary>
public sealed record DiffItem(string Kind, Finding Finding);

/// <summary>What the Excel input and the drawing disagree on, as findings: InputError for a CP the drawing lacks or of the wrong type, InputWarning for a
/// drawing CP no row uses and for a group that differs. A finding about a drawing CP carries its position (zoom-to); one about a row is "Sheet!row".</summary>
public static class ExcelDrawingDiff
{
    public const string Missing = "도면에 없음", WrongType = "타입 불일치", Unused = "미사용 CP", WrongGroup = "그룹 불일치";

    static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static List<DiffItem> Compare(ProjectInput input, ASH.Core.Layout.Layout layout)
    {
        var f = new List<DiffItem>();
        var byName = new Dictionary<string, LayoutCp>();
        foreach (var c in layout.Cps) if (c.Name.Length > 0 && !byName.ContainsKey(c.Name)) byName[c.Name] = c;
        var used = new HashSet<string>();
        string At(string sheet, int row, int i) => $"{sheet}!{(row > 0 ? row : i + 3)}";

        // one reference: the CP must exist, be of the expected type (null = any) and belong to the group (null = any)
        void Ref(string id, string col, string? cp, string? type, string? group, string who)
        {
            if (string.IsNullOrWhiteSpace(cp)) return;
            cp = cp!.Trim();
            used.Add(cp);
            if (!byName.TryGetValue(cp, out var c)) { f.Add(new DiffItem(Missing, new Finding(FindingCode.InputError, $"{col} '{cp}': 도면에 없는 CP", default(Point2), id))); return; }
            if (type != null && !Is(c.Type, type))
                f.Add(new DiffItem(WrongType, new Finding(FindingCode.InputError, $"{col} '{cp}': 도면에서는 {c.Type} CP ({type} 필요)", c.Position, id)));
            else if (group != null && c.Group.Length > 0 && c.Group != group)
                f.Add(new DiffItem(WrongGroup, new Finding(FindingCode.InputWarning, $"{col} '{cp}': CP GROUP {c.Group} ≠ {who} 그룹 {group}", c.Position, id)));
        }

        for (int i = 0; i < input.Routes.Count; i++)
        {
            var r = input.Routes[i];
            var id = At(input.RouteSheet, r.Row, i);
            var group = input.Materials.FirstOrDefault(m => m.Material == r.Material)?.Group;
            string who = $"자재 {r.Material}";
            foreach (var from in r.FromCps) Ref(id, "SupplyFromCP", from, "load", group, who);
            Ref(id, "SupplyToCP", r.SupplyToCP, "unload", group, who);
            Ref(id, "BufferCP", r.BufferCP, "buffer", group, who);
            Ref(id, "ReturnFromCP", r.ReturnFromCP, "rtnload", group, who);
            Ref(id, "ReturnToCP", r.ReturnToCP, "rtnunload", group, who);
        }
        for (int i = 0; i < input.Groups.Count; i++)
        {
            var g = input.Groups[i];
            var id = At("AGV_Groups", g.Row, i);
            foreach (var cp in g.StandbyCPs) Ref(id, "StandbyCPs", cp, "standby", g.Group, "그룹");
            Ref(id, "FinishCP", g.FinishCP, "finish", g.Group, "그룹");
        }
        for (int i = 0; i < input.Stations.Count; i++)
            Ref(At("Stations", input.Stations[i].Row, i), "CPName", input.Stations[i].CPName, null, null, "공정");

        // a traffic CP belongs to no row; every other type is a stop of some route or group
        foreach (var c in layout.Cps)
            if (c.Name.Length > 0 && !Is(c.Type, "traffic") && !used.Contains(c.Name))
                f.Add(new DiffItem(Unused, new Finding(FindingCode.InputWarning, $"CP '{c.Name}' ({c.Type}): Excel에서 쓰지 않음", c.Position, c.Name)));
        return f;
    }
}
