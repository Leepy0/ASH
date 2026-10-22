using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;

namespace ASH.Core.Input;

public static class InputValidator
{
    /// <summary>Type and Group must be AM identifiers (the group is the AGVS vehicle type: agv.&lt;Group&gt; procedures, nl_&lt;Group&gt; lists): ASCII letter or _, then letters, digits, _.</summary>
    public static bool IsAmIdentifier(string s) =>
        s.Length > 0 && (IsAsciiLetter(s[0]) || s[0] == '_') && s.All(c => IsAsciiLetter(c) || (c >= '0' && c <= '9') || c == '_');

    /// <summary>Material names go into AM string literals and comments: printable ASCII, no whitespace, quote or backslash.</summary>
    public static bool IsAmText(string s) => LayoutChecker.IsValidName(s) && s.All(c => c > ' ' && c < 127 && c != '"' && c != '\\');

    static bool IsAsciiLetter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

    /// <summary>One warning per material whose shortest loaded supply cycle (SupplyFrom -> [Buffer] -> SupplyTo route length / SpeedLoaded
    /// + LoadTime + UnloadTime + ExtraTime) is longer than the time one container lasts (TaktTime × UnitsPerContainer / UnitsPerCycle):
    /// the logic keeps at most one call per material in flight, so no vehicle count can keep up. supplyLength = route length per material
    /// in model units (see GroupClusters.SupplyLength); materials without a length are skipped.</summary>
    public static List<Finding> SupplyCapacity(ProjectInput input, IReadOnlyDictionary<string, double> supplyLength)
    {
        var f = new List<Finding>();
        for (int i = 0; i < input.Materials.Count; i++)
        {
            var m = input.Materials[i];
            var g = input.Groups.FirstOrDefault(x => x.Group == m.Group);
            var t = g == null ? null : input.AgvTypes.FirstOrDefault(x => x.Type == g.Type);
            var r = input.Routes.FirstOrDefault(x => x.Material == m.Material);
            if (t == null || r == null || !(t.SpeedLoaded > 0) || !(m.UnitsPerCycle > 0) || !supplyLength.TryGetValue(m.Material, out var len)) continue;
            double cycle = len / t.SpeedLoaded + t.LoadTime + t.UnloadTime + r.ExtraTime, limit = m.TaktTime * m.UnitsPerContainer / m.UnitsPerCycle;
            if (cycle > limit)
                f.Add(new Finding(FindingCode.InputWarning, $"자재 {m.Material}: 한 사이클 공급 상한 초과 — 대수와 무관 (최소 공급 사이클 {cycle.ToString("0.#", CultureInfo.InvariantCulture)}초 > 용기 1개 소비 {limit.ToString("0.#", CultureInfo.InvariantCulture)}초)",
                    default(Point2), $"Materials!{(m.Row > 0 ? m.Row : i + 3)}"));
        }
        return f;
    }

    /// <summary>supplyLength (optional) enables the SupplyCapacity warnings.</summary>
    public static List<Finding> Validate(ProjectInput input, IReadOnlyCollection<string>? layoutCpNames, IReadOnlyDictionary<string, double>? supplyLength = null)
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
        if (!(s.DeadlockSec > 0)) Err("Settings", 0, -3, "DeadlockSec은 0보다 커야 합니다.");
        // inputs read but not yet modelled: warn so a changed value is not mistaken for a modelled one
        const string Ignored = "v2 미반영(값을 바꿔도 결과에 영향 없음)";
        void Warn(string sheet, int row, int i, string msg) => Add(FindingCode.InputWarning, sheet, row, i, msg);
        if (string.Equals(s.WorkDefinition, "CallToReturn", StringComparison.OrdinalIgnoreCase)) Warn("Settings", 0, -3, $"WorkDefinition {s.WorkDefinition}: {Ignored}");
        if (string.Equals(s.TrafficMode, "CPOnly", StringComparison.OrdinalIgnoreCase)) Warn("Settings", 0, -3, $"TrafficMode {s.TrafficMode}: {Ignored}");

        var types = new HashSet<string>(input.AgvTypes.Select(t => t.Type));
        var groups = new HashSet<string>(input.Groups.Select(g => g.Group));
        var mats = new HashSet<string>(input.Materials.Select(m => m.Material));
        var cps = layoutCpNames == null ? null : new HashSet<string>(layoutCpNames);

        Dups("AGV_Types", input.AgvTypes, t => t.Type, t => t.Row, "AGV 타입");
        Dups("AGV_Groups", input.Groups, g => g.Group, g => g.Row, "그룹");
        Dups("Materials", input.Materials, m => m.Material, m => m.Row, "자재");
        Dups("Stations", input.Stations, x => x.Station, x => x.Row, "공정");

        for (int i = 0; i < input.AgvTypes.Count; i++)
        {
            var t = input.AgvTypes[i];
            Blank("AGV_Types", t.Row, i, "Type", t.Type);
            if (t.Type.Length > 0 && !IsAmIdentifier(t.Type)) Err("AGV_Types", t.Row, i, $"타입 이름 '{t.Type}'은(는) AutoMod에서 쓸 수 없습니다(영문·숫자·_만, 한글 불가, 영문 또는 _로 시작).");
            if (t.Capacity < 1) Err("AGV_Types", t.Row, i, $"타입 '{t.Type}'의 Capacity(용기 수)는 1 이상이어야 합니다.");
            if (t.SpeedEmpty < 0 || t.MTBF < 0 || t.MTTR < 0) Err("AGV_Types", t.Row, i, $"타입 '{t.Type}'의 SpeedEmpty/MTBF/MTTR은 0 이상이어야 합니다(SpeedEmpty 0 = 적재 속도, MTBF 0 = 고장 없음).");
            if ((t.MTBF > 0) != (t.MTTR > 0)) Warn("AGV_Types", t.Row, i, $"타입 '{t.Type}': MTBF와 MTTR을 둘 다 넣어야 고장이 반영됩니다(지금은 고장 없음).");
            if (t.Accel < 0 || t.Decel < 0) Err("AGV_Types", t.Row, i, $"타입 '{t.Type}'의 Accel/Decel은 0 이상이어야 합니다(0 = AutoMod 기본값).");
            if (VehicleKinds.Find(t.Kind) == null) Err("AGV_Types", t.Row, i, $"타입 '{t.Type}'의 Kind '{t.Kind}'은(는) 알 수 없습니다. ({VehicleKinds.Names})");
        }
        for (int i = 0; i < input.Groups.Count; i++)
        {
            var g = input.Groups[i];
            Blank("AGV_Groups", g.Row, i, "Group", g.Group);
            Blank("AGV_Groups", g.Row, i, "Type", g.Type);
            if (g.Group.Length > 0 && !IsAmIdentifier(g.Group)) Err("AGV_Groups", g.Row, i, $"그룹 이름 '{g.Group}'은(는) AutoMod에서 쓸 수 없습니다(영문·숫자·_만, 한글 불가, 영문 또는 _로 시작).");
            if (string.Equals(g.Dispatch, "FIFO", StringComparison.OrdinalIgnoreCase)) Warn("AGV_Groups", g.Row, i, $"그룹 '{g.Group}' Dispatch {g.Dispatch}: {Ignored}");
            if (cps != null && !string.IsNullOrEmpty(g.FinishCP) && !cps.Contains(g.FinishCP!)) Err("AGV_Groups", g.Row, i, $"그룹 '{g.Group}'의 FinishCP '{g.FinishCP}'이(가) 레이아웃에 없습니다.");
            if (g.Type.Length > 0 && !types.Contains(g.Type)) Err("AGV_Groups", g.Row, i, $"그룹 '{g.Group}'의 Type '{g.Type}'이(가) AGV_Types에 없습니다.");
            if (g.MinCount < 1) Err("AGV_Groups", g.Row, i, $"그룹 '{g.Group}'의 MinCount는 1 이상이어야 합니다.");
            if (g.MaxCount < g.MinCount) Err("AGV_Groups", g.Row, i, $"그룹 '{g.Group}'의 MaxCount가 MinCount보다 작습니다.");
            OneOf("AGV_Groups", g.Row, i, "Dispatch", g.Dispatch, "Nearest", "FIFO");
            if (cps != null)
                foreach (var cp in g.StandbyCPs.Where(c => !cps.Contains(c))) Err("AGV_Groups", g.Row, i, $"StandbyCP '{cp}'이(가) 레이아웃에 없습니다.");
        }
        for (int i = 0; i < input.Materials.Count; i++)
        {
            var m = input.Materials[i];
            Blank("Materials", m.Row, i, "Material", m.Material);
            Blank("Materials", m.Row, i, "Group", m.Group);
            if (m.Material.Length > 0 && !IsAmText(m.Material)) Err("Materials", m.Row, i, $"자재 이름 '{m.Material}'은(는) AutoMod에서 쓸 수 없습니다(영문·숫자·기호만, 한글·공백·따옴표·역슬래시 불가).");
            if (m.Material.Length > 0 && !input.Routes.Any(r => r.Material == m.Material)) Err("Materials", m.Row, i, $"자재 '{m.Material}'의 Routes 행이 없습니다.");
            if (m.Group.Length > 0 && !groups.Contains(m.Group)) Err("Materials", m.Row, i, $"자재 '{m.Material}'의 Group '{m.Group}'이(가) AGV_Groups에 없습니다.");
            foreach (var (col, v) in new[] { ("LoadLength", m.LoadLength), ("LoadWidth", m.LoadWidth), ("LoadHeight", m.LoadHeight) })
                if (v.HasValue && !(v.Value > 0)) Err("Materials", m.Row, i, $"자재 '{m.Material}'의 {col}은(는) 0보다 커야 합니다(비우면 형태별 기본값).");
            var form = MaterialForms.Normalize(m.Form);
            if (form == null) Err("Materials", m.Row, i, $"자재 '{m.Material}'의 Form '{m.Form}'은(는) 알 수 없습니다. ({string.Join(" / ", MaterialForms.All)})");
            else
            {
                // the group's vehicle type has to be able to take this form (a trolley does not go on a belt-conveyor AGV)
                var gr = input.Groups.FirstOrDefault(x => x.Group == m.Group);
                var ty = gr == null ? null : input.AgvTypes.FirstOrDefault(x => x.Type == gr.Type);
                var kind = ty == null ? null : VehicleKinds.Find(ty.Kind);
                if (kind != null && !kind.Carries.Contains(form))
                    Warn("Materials", m.Row, i, $"자재 '{m.Material}'의 Form {form}은(는) 그룹 '{m.Group}'의 AGV 형상 {kind.Name}({kind.Label})이(가) 싣지 못합니다. 싣는 형태: {string.Join(", ", kind.Carries)}.");
            }
            if (!(m.TaktTime > 0)) Err("Materials", m.Row, i, $"자재 '{m.Material}'의 TaktTime은 0보다 커야 합니다.");
            if (!(m.UnitsPerContainer >= 1)) Err("Materials", m.Row, i, $"자재 '{m.Material}'의 UnitsPerContainer는 1 이상이어야 합니다.");
            else if (m.CallThreshold < m.UnitsPerCycle / m.UnitsPerContainer)
                Add(FindingCode.InputWarning, "Materials", m.Row, i, $"자재 '{m.Material}': 호출 임계값이 1회 소비량보다 작습니다(CallThreshold {m.CallThreshold} < {m.UnitsPerCycle / m.UnitsPerContainer} 용기).");
            // a call at or below the safety stock lets min stock dip under it on every cycle (the delivery is still on its way)
            var safety = m.SafetyStock ?? s.SafetyStock;
            if (m.CallThreshold <= safety)
                Warn("Materials", m.Row, i, $"자재 '{m.Material}': 호출 임계값이 안전재고 이하 — 대수와 무관하게 재고 판정 실패(CallThreshold {m.CallThreshold} <= SafetyStock {safety}).");
        }
        for (int i = 0; i < input.Routes.Count; i++)
        {
            var r = input.Routes[i];
            Blank("Routes", r.Row, i, "Material", r.Material);
            Blank("Routes", r.Row, i, "SupplyFromCP", r.SupplyFromCP);
            Blank("Routes", r.Row, i, "SupplyToCP", r.SupplyToCP);
            if (r.Material.Length > 0 && !mats.Contains(r.Material)) Err("Routes", r.Row, i, $"Material '{r.Material}'이(가) Materials에 없습니다.");
            if (r.Material.Length > 0 && input.Routes.Take(i).Any(x => x.Material == r.Material)) Err("Routes", r.Row, i, $"자재 '{r.Material}'의 Routes 행이 중복되었습니다(자재당 1행).");
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
            if (x.BufferCapacity > 0) Warn("Stations", x.Row, i, $"공정 '{x.Station}' BufferCapacity: {Ignored}");
        }
        for (int i = 0; i < input.Ext.Count; i++)
            if (input.Ext[i].Enabled) Warn("Ext", input.Ext[i].Row, i, $"Ext '{input.Ext[i].Name}' Enabled: {Ignored} — 훅은 <Model>.arc/user_ext.m을 직접 고치세요.");
        if (supplyLength != null) f.AddRange(SupplyCapacity(input, supplyLength));
        return f;
    }
}
