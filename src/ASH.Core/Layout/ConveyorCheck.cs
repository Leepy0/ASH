using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Input;
using ASH.Core.Model;

namespace ASH.Core.Layout;

/// <summary>
/// Where the Conveyors sheet meets the drawing: every row needs its named AGV_CONV polyline (the logic moves products into its stations),
/// and the use point must lie on it behind the entry station. A named polyline without a row, or an unnamed one, is only drawn (warning).
/// </summary>
public static class ConveyorCheck
{
    /// <summary><paramref name="scale"/> = metres per layout unit. Messages in Korean, InputError stops the model build.</summary>
    public static List<Finding> Check(ProjectInput input, Layout l, double scale)
    {
        var res = new List<Finding>();
        void Err(string m, ConveyorInfo r) => res.Add(new Finding(FindingCode.InputError, $"Conveyors {r.Row}행: " + m, default, null));
        void Warn(string m, LayoutConveyor c) => res.Add(new Finding(FindingCode.InputWarning, m, c.Segments.FirstOrDefault()?.Start ?? default, c.Id));
        string F(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
        var named = l.Conveyors.Where(c => !c.GraphicOnly && c.Name.Length > 0).ToList();
        foreach (var g in named.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            res.Add(new Finding(FindingCode.InputError, $"도면에 컨베이어 이름 '{g.Key}'이(가) {g.Count()}개 있습니다. AGVCONV로 다시 이름을 붙이세요.", g.First().Segments.FirstOrDefault()?.Start ?? default, g.First().Id));
        foreach (var r in input.Conveyors)
        {
            if (input.Conveyors.Count(x => string.Equals(x.Conveyor, r.Conveyor, StringComparison.OrdinalIgnoreCase)) > 1) { Err($"컨베이어 '{r.Conveyor}'이(가) 여러 행에 있습니다.", r); continue; }
            if (!input.Materials.Any(m => m.Material == r.Material)) Err($"자재 '{r.Material}'이(가) Materials에 없습니다.", r);
            if (!(r.ProductTakt > 0)) Err("ProductTakt는 0보다 커야 합니다.", r);
            var c = named.FirstOrDefault(x => string.Equals(x.Name, r.Conveyor, StringComparison.OrdinalIgnoreCase));
            if (c == null) { Err($"컨베이어 '{r.Conveyor}'이(가) 도면에 없습니다. AGV_CONV 레이어에 폴리선을 그리고 AGVCONV로 이름을 붙이세요.", r); continue; }
            if (!LayoutChecker.IsValidName(c.Name)) { Err($"컨베이어 이름 '{c.Name}'은(는) 모델에 쓸 수 없습니다. AGVCONV로 다시 이름을 붙이세요.", r); continue; }
            double len = ConveyorAsyWriter.LengthM(c, scale), entry = ConveyorAsyWriter.ProductLengthM;
            if (len <= entry) Err($"컨베이어 '{r.Conveyor}'의 길이 {F(len)} m가 제품 길이 {F(entry)} m보다 짧습니다.", r);
            else if (r.ConsumeAtM is double at && (at <= entry || at > len + 1e-6))
                Err($"ConsumeAt {F(at)} m가 컨베이어 '{r.Conveyor}' 위에 있지 않습니다. 시작에서 {F(entry)} m 초과, 길이 {F(len)} m 이하로 넣으세요(빈칸 = 끝).", r);
        }
        foreach (var c in named.Where(c => !input.Conveyors.Any(r => string.Equals(r.Conveyor, c.Name, StringComparison.OrdinalIgnoreCase))))
            Warn($"도면의 컨베이어 '{c.Name}'은(는) Conveyors 시트에 행이 없어 모양만 그립니다(제품 흐름 없음).", c);
        int unnamed = l.Conveyors.Count(c => !c.GraphicOnly && c.Name.Length == 0);
        if (unnamed > 0) Warn($"이름 없는 AGV_CONV 폴리선 {unnamed}개는 모양만 그립니다. 제품을 흘리려면 AGVCONV로 이름을 붙이세요.", l.Conveyors.First(c => !c.GraphicOnly && c.Name.Length == 0));
        return res;
    }
}
