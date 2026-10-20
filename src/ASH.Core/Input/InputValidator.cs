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
        // row = source row recorded by the reader; 0 (built in code) falls back to list index + 3.
        void Add(FindingCode code, string sheet, int row, int i, string msg) =>
            f.Add(new Finding(code, msg, default(Point2), sheet == "Settings" ? "Settings" : $"{sheet}!{(row > 0 ? row : i + 3)}"));
        void Err(string sheet, int row, int i, string msg) => Add(FindingCode.InputError, sheet, row, i, msg);
        void Blank(string sheet, int row, int i, string col, string? v) { if (string.IsNullOrWhiteSpace(v)) Err(sheet, row, i, $"{col}이(가) 비어 있습니다."); }
        void OneOf(string sheet, int row, int i, string col, string v, params string[] ok)
        {
            if (!ok.Contains(v, StringComparer.OrdinalIgnoreCase)) Err(sheet, row, i, $"{col} '{v}'은(는) {string.Join(" / ", ok)} 중 하나여야 합니다.");
        }
        void Dups<T>(string sheet, List<T> items, Func<T, string> key, Func<T, int> rowOf, string label)
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < items.Count; i++)
            {
                var k = key(items[i]);
                if (k.Length > 0 && !seen.Add(k)) Err(sheet, rowOf(items[i]), i, $"{label} 이름이 중복되었습니다: {k}");
            }
        }
        var s = input.Settings;
        OneOf("Settings", 0, -2, "InitStockMode", s.InitStockMode, "Fixed", "Uniform", "Staggered");
        OneOf("Settings", 0, -2, "TrafficMode", s.TrafficMode, "Block", "CPOnly");
        OneOf("Settings", 0, -2, "WorkDefinition", s.WorkDefinition, "LoadToUnload", "CallToReturn");
        if (s.Replications < 1) Err("Settings", 0, -3, "Replications는 1 이상이어야 합니다.");
        if (!(s.WarmupSec < s.SimLengthSec)) Err("Settings", 0, -3, "Warmup은 SimLength보다 작아야 합니다.");

        var types = new HashSet<string>(input.AgvTypes.Select(t => t.Type));
        var groups = new HashSet<string>(input.Groups.Select(g => g.Group));
        var mats = new HashSet<string>(input.Materials.Select(m => m.Material));
        var stations = new HashSet<string>(input.Stations.Select(x => x.Station));
        var cps = layoutCpNames == null ? null : new HashSet<string>(layoutCpNames);

        Dups("AGV_Types", input.AgvTypes, t => t.Type, t => t.Row, "AGV 타입");
        Dups("AGV_Groups", input.Groups, g => g.Group, g => g.Row, "그룹");
        Dups("Materials", input.Materials, m => m.Material, m => m.Row, "자재");
        Dups("Stations", input.Stations, x => x.Station, x => x.Row, "공정");

        for (int i = 0; i < input.AgvTypes.Count; i++)
        {
            var t = input.AgvTypes[i];
            Blank("AGV_Types", t.Row, i, "Type", t.Type);
            var g = input.Groups.FirstOrDefault(x => x.Group == t.Group);
            if (g == null || g.Type != t.Type)
                Add(FindingCode.InputWarning, "AGV_Types", t.Row, i, $"타입 '{t.Type}'의 Group '{t.Group}'이(가) 그 타입을 쓰는 AGV_Groups 그룹과 일치하지 않습니다.");
        }
        for (int i = 0; i < input.Groups.Count; i++)
        {
            var g = input.Groups[i];
            Blank("AGV_Groups", g.Row, i, "Group", g.Group);
            Blank("AGV_Groups", g.Row, i, "Type", g.Type);
            if (g.Type.Length > 0 && !types.Contains(g.Type)) Err("AGV_Groups", g.Row, i, $"그룹 '{g.Group}'의 Type '{g.Type}'이(가) AGV_Types에 없습니다.");
            if (g.MinCount < 1) Err("AGV_Groups", g.Row, i, $"그룹 '{g.Group}'의 MinCount는 1 이상이어야 합니다.");
            if (g.MaxCount < g.MinCount) Err("AGV_Groups", g.Row, i, $"그룹 '{g.Group}'의 MaxCount가 MinCount보다 작습니다.");
            OneOf("AGV_Groups", g.Row, i, "Dispatch", g.Dispatch, "Nearest", "FIFO");
            if (cps != null)
                foreach (var cp in g.StandbyCPs.Where(c => !cps.Contains(c))) Err("AGV_Groups", g.Row, i, $"StandbyCP '{cp}'이(가) 레이아웃에 없습니다.");
        }
        // the AM logic defines vehicle procedures and dispatch filters per vehicle type: one type = one group
        var typeOwner = new Dictionary<string, string>();
        for (int i = 0; i < input.Groups.Count; i++)
        {
            var g = input.Groups[i];
            if (g.Type.Length == 0) continue;
            if (typeOwner.TryGetValue(g.Type, out var first)) Err("AGV_Groups", g.Row, i, $"그룹 '{g.Group}'과(와) '{first}'이(가) 같은 Type '{g.Type}'을(를) 씁니다. 그룹마다 다른 Type이 필요합니다.");
            else typeOwner[g.Type] = g.Group;
        }
        for (int i = 0; i < input.Materials.Count; i++)
        {
            var m = input.Materials[i];
            Blank("Materials", m.Row, i, "Material", m.Material);
            Blank("Materials", m.Row, i, "Station", m.Station);
            Blank("Materials", m.Row, i, "Group", m.Group);
            if (m.Group.Length > 0 && !groups.Contains(m.Group)) Err("Materials", m.Row, i, $"자재 '{m.Material}'의 Group '{m.Group}'이(가) AGV_Groups에 없습니다.");
            if (m.Station.Length > 0 && !stations.Contains(m.Station)) Err("Materials", m.Row, i, $"자재 '{m.Material}'의 Station '{m.Station}'이(가) Stations에 없습니다.");
            if (!(m.TaktTime > 0)) Err("Materials", m.Row, i, $"자재 '{m.Material}'의 TaktTime은 0보다 커야 합니다.");
            if (!(m.UnitsPerContainer >= 1)) Err("Materials", m.Row, i, $"자재 '{m.Material}'의 UnitsPerContainer는 1 이상이어야 합니다.");
        }
        for (int i = 0; i < input.Routes.Count; i++)
        {
            var r = input.Routes[i];
            Blank("Routes", r.Row, i, "Material", r.Material);
            Blank("Routes", r.Row, i, "SupplyFromCP", r.SupplyFromCP);
            Blank("Routes", r.Row, i, "SupplyToCP", r.SupplyToCP);
            if (r.Material.Length > 0 && !mats.Contains(r.Material)) Err("Routes", r.Row, i, $"Material '{r.Material}'이(가) Materials에 없습니다.");
            if (cps == null) continue;
            foreach (var (col, cp) in new[] { ("SupplyFromCP", r.SupplyFromCP), ("SupplyToCP", r.SupplyToCP), ("BufferCP", r.BufferCP), ("ReturnFromCP", r.ReturnFromCP), ("ReturnToCP", r.ReturnToCP) })
                if (!string.IsNullOrWhiteSpace(cp) && !cps.Contains(cp!)) Err("Routes", r.Row, i, $"{col} '{cp}'이(가) 레이아웃에 없습니다.");
        }
        for (int i = 0; i < input.Stations.Count; i++)
        {
            var x = input.Stations[i];
            Blank("Stations", x.Row, i, "Station", x.Station);
            Blank("Stations", x.Row, i, "CPName", x.CPName);
            if (cps != null && x.CPName.Length > 0 && !cps.Contains(x.CPName)) Err("Stations", x.Row, i, $"CPName '{x.CPName}'이(가) 레이아웃에 없습니다.");
        }
        return f;
    }
}
