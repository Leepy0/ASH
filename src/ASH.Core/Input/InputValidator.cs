using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;

namespace ASH.Core.Input;

public static class InputValidator
{
    public static List<Finding> Validate(ProjectInput input, IReadOnlyCollection<string>? layoutCpNames)
    {
        var f = new List<Finding>();
        void Err(string sheet, int i, string msg) => f.Add(new Finding(FindingCode.InputError, msg, default(Point2), $"{sheet}!{i + 3}"));
        void Dups<T>(string sheet, List<T> items, Func<T, string> key, string label)
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < items.Count; i++)
                if (!seen.Add(key(items[i]))) Err(sheet, i, $"{label} 이름이 중복되었습니다: {key(items[i])}");
        }
        var types = new HashSet<string>(input.AgvTypes.Select(t => t.Type));
        var groups = new HashSet<string>(input.Groups.Select(g => g.Group));
        var mats = new HashSet<string>(input.Materials.Select(m => m.Material));
        var cps = layoutCpNames == null ? null : new HashSet<string>(layoutCpNames);

        Dups("AGV_Types", input.AgvTypes, t => t.Type, "AGV 타입");
        Dups("AGV_Groups", input.Groups, g => g.Group, "그룹");
        Dups("Materials", input.Materials, m => m.Material, "자재");
        Dups("Stations", input.Stations, s => s.Station, "공정");

        for (int i = 0; i < input.Groups.Count; i++)
        {
            var g = input.Groups[i];
            if (!types.Contains(g.Type)) Err("AGV_Groups", i, $"그룹 '{g.Group}'의 Type '{g.Type}'이(가) AGV_Types에 없습니다.");
            if (g.MinCount < 1) Err("AGV_Groups", i, $"그룹 '{g.Group}'의 MinCount는 1 이상이어야 합니다.");
            if (g.MaxCount < g.MinCount) Err("AGV_Groups", i, $"그룹 '{g.Group}'의 MaxCount가 MinCount보다 작습니다.");
            if (cps != null)
                foreach (var cp in g.StandbyCPs.Where(c => !cps.Contains(c))) Err("AGV_Groups", i, $"StandbyCP '{cp}'이(가) 레이아웃에 없습니다.");
        }
        for (int i = 0; i < input.Materials.Count; i++)
        {
            var m = input.Materials[i];
            if (!groups.Contains(m.Group)) Err("Materials", i, $"자재 '{m.Material}'의 Group '{m.Group}'이(가) AGV_Groups에 없습니다.");
            if (m.TaktTime <= 0) Err("Materials", i, $"자재 '{m.Material}'의 TaktTime은 0보다 커야 합니다.");
            if (m.UnitsPerContainer < 1) Err("Materials", i, $"자재 '{m.Material}'의 UnitsPerContainer는 1 이상이어야 합니다.");
        }
        for (int i = 0; i < input.Routes.Count; i++)
        {
            var r = input.Routes[i];
            if (!mats.Contains(r.Material)) Err("Routes", i, $"Material '{r.Material}'이(가) Materials에 없습니다.");
            if (cps == null) continue;
            foreach (var (col, cp) in new[] { ("SupplyFromCP", r.SupplyFromCP), ("SupplyToCP", r.SupplyToCP), ("BufferCP", r.BufferCP), ("ReturnFromCP", r.ReturnFromCP), ("ReturnToCP", r.ReturnToCP) })
                if (!string.IsNullOrEmpty(cp) && !cps.Contains(cp!)) Err("Routes", i, $"{col} '{cp}'이(가) 레이아웃에 없습니다.");
        }
        if (cps != null)
            for (int i = 0; i < input.Stations.Count; i++)
                if (!cps.Contains(input.Stations[i].CPName)) Err("Stations", i, $"CPName '{input.Stations[i].CPName}'이(가) 레이아웃에 없습니다.");
        return f;
    }
}
