using System.Collections.Generic;
using System.IO;
using System.Linq;
using ASH.Core.Input;
using ASH.Core.Layout;
using ASH.Core.Model;
using ASH.Tests.Layout;
using ClosedXML.Excel;
using Xunit;

namespace ASH.Tests.Input;

/// <summary>The route on the Materials row (an older Routes sheet merged in), the advanced columns behind "+", the suggested call threshold and the
/// "입력 필요" line of AGVEXCEL.</summary>
[Collection("ExcelTemplateStatics")]
public class MaterialsRoutesTests
{
    static string Tmp(string ext = ".xlsx") => Path.Combine(Path.GetTempPath(), "mr-" + System.Guid.NewGuid().ToString("N") + ext);

    static int Col(IXLWorksheet ws, string head) => Enumerable.Range(1, ws.LastColumnUsed()!.ColumnNumber()).First(c => ws.Cell(1, c).GetString() == head);

    static ASH.Core.Layout.Layout Drawing()
    {
        // one 100 m path (mm drawing), two loads and unloads of group G1: M001 L1 -> U1 (80 m), M002 L2 -> U2
        var l = L.Of(L.Line("P1", 0, 0, 100000, 0));
        l.Tolerance = 1;
        l.Cps.Add(L.Cp("S1", 0, 0, "standby", "G1"));
        l.Cps.Add(L.Cp("L1", 10000, 0, "load", "G1")); l.Cps.Add(L.Cp("U1", 90000, 0, "unload", "G1"));
        l.Cps.Add(L.Cp("L2", 20000, 0, "load", "G1")); l.Cps.Add(L.Cp("U2", 95000, 0, "unload", "G1"));
        l.Cps.Add(L.Cp("B1", 50000, 0, "buffer", "G1"));
        return l;
    }

    /// <summary>The workbook as an older template had it: the route columns on a Routes sheet of their own, from row 4 (other rows than the materials', so they
    /// are matched by name); <paramref name="reverse"/> lists them in the other order.</summary>
    static void MakeLegacy(string path, System.Action<IXLWorksheet>? routes = null, bool reverse = false)
    {
        using var wb = ExcelReader.Open(path);
        var m = wb.Worksheet("Materials");
        var r = wb.AddWorksheet("Routes");
        var cols = new[] { "Material" }.Concat(ExcelTemplate.RouteCols).ToArray();
        for (int c = 0; c < cols.Length; c++) { r.Cell(1, c + 1).Value = cols[c]; r.Cell(2, c + 1).Value = "설명"; }
        var rows = Enumerable.Range(3, 50).Where(i => m.Cell(i, 1).GetString().Length > 0).ToList();
        if (reverse) rows.Reverse();
        for (int i = 0; i < rows.Count; i++)
            for (int c = 0; c < cols.Length; c++) r.Cell(4 + i, c + 1).Value = m.Cell(rows[i], Col(m, cols[c])).Value;
        foreach (var c in ExcelTemplate.RouteCols.Select(x => Col(m, x)).OrderByDescending(x => x)) m.Column(c).Delete();
        routes?.Invoke(r);
        wb.Save();
    }

    /// <summary>Equal but for the route rows (on another sheet now). Routes follow the Materials order after a merge: with the Routes sheet in another order
    /// (<paramref name="anyOrder"/>) the list order and so the model signature differ.</summary>
    static void AssertSameInput(ProjectInput a, ProjectInput b, bool anyOrder = false)
    {
        if (anyOrder)
        {
            Assert.Equal(a.Routes.Select(x => x with { Row = 0 }).OrderBy(x => x.Material), b.Routes.Select(x => x with { Row = 0 }).OrderBy(x => x.Material));
            a = Sorted(a); b = Sorted(b);
        }
        Assert.Equal(a.Settings, b.Settings);
        Assert.Equal(a.AgvTypes, b.AgvTypes); Assert.Equal(a.Materials, b.Materials);
        Assert.Equal(a.Groups.Select(g => (g with { StandbyCPs = null! }, string.Join(",", g.StandbyCPs))), b.Groups.Select(g => (g with { StandbyCPs = null! }, string.Join(",", g.StandbyCPs))));
        Assert.Equal(a.DisabledMaterials, b.DisabledMaterials); Assert.Equal(a.Conveyors, b.Conveyors);
        Assert.Equal(a.Routes.Select(x => x with { Row = 0 }), b.Routes.Select(x => x with { Row = 0 }));
        Assert.Equal(InputSignature.Compute(a), InputSignature.Compute(b));
    }

    static ProjectInput Sorted(ProjectInput p)
    {
        var s = new ProjectInput { Settings = p.Settings, RouteSheet = p.RouteSheet };
        s.AgvTypes.AddRange(p.AgvTypes); s.Groups.AddRange(p.Groups); s.Materials.AddRange(p.Materials); s.Conveyors.AddRange(p.Conveyors); s.DisabledMaterials.AddRange(p.DisabledMaterials);
        s.Routes.AddRange(p.Routes.OrderBy(r => r.Material));
        return s;
    }

    // ---- E3 ----

    [Fact]
    public void New_Template_Has_No_Routes_Sheet_And_Reads_The_Route_From_The_Material_Row()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path, Drawing());
            using (var wb = ExcelReader.Open(path)) Assert.False(wb.TryGetWorksheet("Routes", out _));
            var p = ExcelReader.Read(path);
            Assert.Equal("Materials", p.RouteSheet);
            Assert.Equal(new[] { ("M001", "L1", "U1", 3), ("M002", "L2", "U2", 4) }, p.Routes.Select(r => (r.Material, r.SupplyFromCP, r.SupplyToCP, r.Row)));
            Assert.Equal(p.Materials.Select(m => m.Row), p.Routes.Select(r => r.Row));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Old_Workbook_Is_Merged_Into_Materials_And_Reads_Back_The_Same()
    {
        var path = Tmp(); var tsv = Tmp(".tsv");
        try
        {
            ExcelTemplate.Write(path, Drawing());
            using (var wb = ExcelReader.Open(path)) // values that must survive: a buffer, extra time, allowance, a switched-off material
            {
                var m = wb.Worksheet("Materials");
                m.Cell(3, Col(m, "BufferCP")).Value = "B1"; m.Cell(3, Col(m, "ExtraTime")).Value = 12.5; m.Cell(3, Col(m, "BufferWaitSec")).Value = 30;
                m.Row(4).CopyTo(m.Row(5)); m.Cell(5, 1).Value = "M003"; m.Cell(5, Col(m, "Use")).Value = "N";
                wb.Save();
            }
            MakeLegacy(path);
            var before = ExcelReader.Read(path);
            Assert.Equal("Routes", before.RouteSheet);
            Assert.Equal("B1", before.Routes.Single(r => r.Material == "M001").BufferCP);
            using (var wb = ExcelReader.Open(path)) CellDump.Write(wb, tsv); // what the server keeps

            var log = new List<ExcelEdit>();
            List<string> changes;
            using (var wb = ExcelReader.Open(path)) { changes = ExcelTemplate.Update(wb, null, log); wb.SaveAs(path); }
            Assert.Contains("Routes 시트를 Materials에 합침 3행", changes);
            Assert.DoesNotContain(changes, c => c.Contains("SupplyFromCP 열 추가")); // the merge says it once
            Assert.Contains(log, e => e is DeleteSheetEdit { Sheet: "Routes" });
            using (var wb = ExcelReader.Open(path)) Assert.False(wb.TryGetWorksheet("Routes", out _));
            var after = ExcelReader.Read(path);
            Assert.Equal("Materials", after.RouteSheet);
            AssertSameInput(before, after);
            Assert.Equal(after.Materials.Select(m => m.Row), after.Routes.Select(r => r.Row)); // Route.Row = the material's row

            // the server rebuilds input.xlsx from the cell snapshot of an older job the same way
            using (var wb = CellDump.Load(tsv)) { ExcelTemplate.Update(wb, null); AssertSameInput(before, ExcelReader.Read(wb, out _)); }
        }
        finally { File.Delete(path); File.Delete(tsv); }
    }

    /// <summary>For a file only Excel can write the plugin replays the log through Excel: the merge (cells, then the sheet deleted) must be in it.</summary>
    [Fact]
    public void The_Merge_Replayed_From_The_Log_Gives_The_Same_Input()
    {
        var path = Tmp(); var copy = Tmp();
        try
        {
            ExcelTemplate.Write(path, Drawing());
            MakeLegacy(path, reverse: true);
            File.Copy(path, copy);
            var before = ExcelReader.Read(path);
            var log = new List<ExcelEdit>();
            using (var wb = ExcelReader.Open(path)) ExcelTemplate.Update(wb, Drawing(), log);
            using (var replay = ExcelReader.Open(copy))
            {
                foreach (var e in log)
                    switch (e)
                    {
                        case AddSheetEdit a: replay.AddWorksheet(a.Sheet); break;
                        case DeleteSheetEdit d: replay.Worksheet(d.Sheet).Delete(); break;
                        case DeleteColumnEdit d: replay.Worksheet(d.Sheet).Column(d.Col).Delete(); break;
                        case CellEdit c:
                            if (double.TryParse(c.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d2)) replay.Worksheet(c.Sheet).Cell(c.Row, c.Col).Value = d2;
                            else replay.Worksheet(c.Sheet).Cell(c.Row, c.Col).Value = c.Value;
                            break;
                    }
                replay.Save();
            }
            AssertSameInput(before, ExcelReader.Read(copy), anyOrder: true);
        }
        finally { File.Delete(path); File.Delete(copy); }
    }

    [Fact]
    public void Old_Workbook_With_Two_Routes_For_One_Material_Stays_As_It_Is_And_Says_Why()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path, Drawing());
            MakeLegacy(path, r => { r.Row(5).CopyTo(r.Row(6)); r.Cell(6, 3).Value = "U9"; }); // M002 twice
            var changes = ExcelTemplate.Update(path, null);
            Assert.Contains(changes, c => c.StartsWith("Routes 시트 유지") && c.Contains("M002"));
            using (var wb = ExcelReader.Open(path))
            {
                Assert.True(wb.TryGetWorksheet("Routes", out _));
                Assert.DoesNotContain("SupplyFromCP", wb.Worksheet("Materials").Row(1).CellsUsed().Select(c => c.GetString())); // no half merge
            }
            var p = ExcelReader.Read(path);
            Assert.Equal("Routes", p.RouteSheet);
            Assert.Equal(3, p.Routes.Count);
            var dup = Assert.Single(InputValidator.Validate(p, null), f => f.Message.Contains("중복"));
            Assert.Equal("Routes!6", dup.EntityId); // the mark lands on the Routes sheet

            // a new load CP still gets its material and route, on both sheets
            var l = Drawing(); l.Cps.Add(L.Cp("L3", 30000, 0, "load", "G1"));
            Assert.Contains(ExcelTemplate.Update(path, l), c => c.StartsWith("Materials·Routes: load CP L3"));
            Assert.Contains(ExcelReader.Read(path).Routes, r => r.SupplyFromCP == "L3" && r.Material == "M003");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Findings_On_A_Merged_Workbook_Mark_The_Material_Row()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path, Drawing());
            var l = Drawing(); l.Cps.First(c => c.Name == "U2").Group = "G2"; // M002's unload is another group's
            var p = ExcelReader.Read(path);
            var f = ModelBuilder.Validate(p, l, 0.001).Concat(ExcelDrawingDiff.Compare(p, l).Select(d => d.Finding)).Where(x => x.Message.Contains("U2")).ToList();
            Assert.NotEmpty(f);
            Assert.All(f, x => Assert.Equal("Materials!4", x.EntityId));
            using var wb = ExcelReader.Open(path);
            Assert.All(ExcelMarks.Plan(wb, f), m => Assert.Equal(("Materials", 4, Col(wb.Worksheet("Materials"), "SupplyToCP")), (m.Sheet, m.Row, m.Col)));
        }
        finally { File.Delete(path); }
    }

    // ---- E1 ----

    [Fact]
    public void Advanced_Columns_Come_Last_And_Are_Grouped_And_Collapsed()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path);
            using var wb = ExcelReader.Open(path);
            foreach (var sheet in new[] { "Materials", "AGV_Types" })
            {
                var ws = wb.Worksheet(sheet);
                var cols = ExcelTemplate.Sheets.First(s => s.Sheet == sheet).Cols;
                var adv = cols.Select((c, i) => (c, i)).Where(x => ExcelTemplate.Advanced.Contains((sheet, x.c))).Select(x => x.i + 1).ToList();
                Assert.Equal(Enumerable.Range(adv[0], cols.Length - adv[0] + 1), adv); // one run at the end
                Assert.All(adv, c => { Assert.Equal(1, ws.Column(c).OutlineLevel); Assert.True(ws.Column(c).IsHidden); });
                Assert.All(Enumerable.Range(1, adv[0] - 1), c => { Assert.Equal(0, ws.Column(c).OutlineLevel); Assert.False(ws.Column(c).IsHidden); });
            }
            Assert.Equal(new[] { "Material", "Group", "TaktTime", "UnitsPerCycle", "UnitsPerContainer", "CallThreshold", "Form", "SupplyFromCP", "SupplyToCP", "ReturnFromCP", "ReturnToCP" },
                InputSchema.Of("Materials").Where(c => !c.Advanced).Select(c => c.Name));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Update_Groups_The_Advanced_Runs_Of_An_Older_Workbook_Once()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path, Drawing());
            MakeLegacy(path); // Materials then ends with basic columns; the merge appends the route columns
            using (var wb = ExcelReader.Open(path)) { foreach (var ws in wb.Worksheets) ws.Columns().Ungroup(true); wb.Save(); }
            var log = new List<ExcelEdit>();
            using (var wb = ExcelReader.Open(path))
            {
                ExcelTemplate.Update(wb, null, log);
                var m = wb.Worksheet("Materials");
                foreach (var c in Enumerable.Range(1, m.LastColumnUsed()!.ColumnNumber()))
                    Assert.Equal(ExcelTemplate.Advanced.Contains(("Materials", m.Cell(1, c).GetString())) ? 1 : 0, m.Column(c).OutlineLevel);
                wb.SaveAs(path);
            }
            var runs = log.OfType<GroupColumnsEdit>().Where(g => g.Sheet == "Materials").ToList();
            Assert.Equal(2, runs.Count); // Station..Use where they were, BufferCP..BufferWaitSec appended by the merge
            var log2 = new List<ExcelEdit>();
            using (var wb = ExcelReader.Open(path))
            {
                ExcelTemplate.Update(wb, null, log2);
                Assert.Equal(1, wb.Worksheet("Materials").Column(Col(wb.Worksheet("Materials"), "Station")).OutlineLevel); // not a second level
            }
            Assert.DoesNotContain(log2, e => e is GroupColumnsEdit);
        }
        finally { File.Delete(path); }
    }

    // ---- E2 ----

    [Theory]
    [InlineData(400, 2, 3)]   // cycle 440 s < one container 600 s: 1 + 1 = 2, but above the safety stock 2
    [InlineData(1500, 2, 4)]  // cycle 1540 s: 3 containers + 1
    [InlineData(400, 0, 2)]   // no safety stock: 1 + 1
    [InlineData(0, 0, 2)]
    public void Suggested_Call_Threshold(double lengthM, double safety, int expected) =>
        Assert.Equal(expected, ExcelTemplate.SuggestCall(lengthM, 1, 20, 20, 0, 60, 1, 10, safety));

    [Fact]
    public void Suggested_Call_Threshold_Covers_One_Takts_Use_And_Needs_Usable_Numbers()
    {
        Assert.Equal(3, ExcelTemplate.SuggestCall(0, 1, 0, 0, 0, 60, 30, 10, 0)); // 3 containers a takt
        Assert.Null(ExcelTemplate.SuggestCall(10, 0, 0, 0, 0, 60, 1, 10, 2));
        Assert.Null(ExcelTemplate.SuggestCall(10, 1, 0, 0, 0, 0, 1, 10, 2));
    }

    [Fact]
    public void Suggestion_Fills_Seeded_And_Blank_Thresholds_Only()
    {
        var path = Tmp();
        try
        {
            // M001: 80 m at 1 m/s + 20 + 20 s = 120 s < 600 s a container -> 2, above SafetyStock 2 -> 3
            var made = ExcelTemplate.Write(path, Drawing(), null, 0.001);
            Assert.Contains("Materials: M001 호출 임계 3 (제안)", made);
            Assert.Equal(3, ExcelReader.Read(path).Materials[0].CallThreshold);
            Assert.DoesNotContain(ExcelTemplate.Write(path, Drawing()), c => c.Contains("제안")); // no scale: the example's value stays
            Assert.Equal(4, ExcelReader.Read(path).Materials[0].CallThreshold);

            using (var wb = ExcelReader.Open(path))
            {
                var m = wb.Worksheet("Materials"); int call = Col(m, "CallThreshold");
                m.Cell(3, call).Value = 7;                                                     // typed: kept
                m.Cell(4, call).Value = "";                                                    // blank: filled
                m.Row(4).CopyTo(m.Row(5)); m.Cell(5, 1).Value = "M009"; m.Cell(5, Col(m, "CallThresholdQty")).Value = 25; // units typed: kept
                wb.Save();
            }
            var changes = ExcelTemplate.Update(path, Drawing(), null, 0.001);
            Assert.Equal(new[] { "Materials: M002 호출 임계 3 (제안)" }, changes.Where(c => c.Contains("제안")));
            var p = ExcelReader.Read(path);
            Assert.Equal(new[] { 7, 3, 2.5 }, p.Materials.Select(x => x.CallThreshold));
        }
        finally { File.Delete(path); }
    }

    // ---- E5 ----

    [Fact]
    public void Summary_Counts_Blank_Required_Cells_Of_Named_Rows()
    {
        var path = Tmp();
        try
        {
            Assert.Equal("입력 필요 없음", ExcelTemplate.Write(path).Last());
            using (var wb = ExcelReader.Open(path))
            {
                var m = wb.Worksheet("Materials");
                m.Cell(3, Col(m, "TaktTime")).Value = "";
                m.Cell(4, 1).Value = "M002";                                   // a name only
                m.Cell(5, 1).Value = "M003"; m.Cell(5, Col(m, "Use")).Value = "N"; // switched off: needs nothing
                var g = wb.Worksheet("AGV_Groups"); g.Cell(3, Col(g, "MaxCount")).Value = "";
                wb.Save();
            }
            Assert.Equal("입력 필요: Materials 운반 그룹 1칸, T/T 2칸, 소요 원수 1칸, 용기당 자재 수량 1칸, 호출 기준 1칸, load CP 1칸, unload CP 1칸 · AGV_Groups 최대 대수 1칸",
                ExcelTemplate.Update(path, null).Last());
        }
        finally { File.Delete(path); }
    }
}
