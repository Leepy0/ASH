using System.Collections.Generic;
using System.IO;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Input;
using ASH.Core.Layout;
using ClosedXML.Excel;
using Xunit;

namespace ASH.Tests.Input;

/// <summary>Excel and drawing tied together: CP drop-downs, rows from AGVCPROUTE sequences, the difference report, validation marks.</summary>
[Xunit.Collection("ExcelTemplateStatics")]
public class ExcelDrawingLinkTests
{
    static LayoutCp Cp(string name, string type, string group, string route = "") => new LayoutCp { Name = name, Type = type, Group = group, RouteId = route, Position = new Point2(name.Length, 1) };

    static ASH.Core.Layout.Layout Drawing()
    {
        var l = new ASH.Core.Layout.Layout();
        l.Cps.Add(Cp("SB1", "standby", "A")); l.Cps.Add(Cp("FN1", "finish", "A"));
        l.Cps.Add(Cp("L001", "load", "A")); l.Cps.Add(Cp("L002", "load", "A"));
        l.Cps.Add(Cp("U001", "unload", "A")); l.Cps.Add(Cp("B001", "buffer", "A"));
        l.Cps.Add(Cp("RL1", "rtnload", "A")); l.Cps.Add(Cp("RU1", "rtnunload", "A"));
        return l;
    }

    static string Tmp() => Path.Combine(Path.GetTempPath(), "link-" + System.Guid.NewGuid().ToString("N") + ".xlsx");

    static List<string> Column(IXLWorksheet ws, int col) =>
        Enumerable.Range(1, 100).Select(r => ws.Cell(r, col).GetString()).TakeWhile(s => s.Length > 0).ToList();

    // ---- E1 -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Cp_Columns_Drop_Down_The_Drawings_Cps_Of_The_Matching_Type()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path, Drawing());
            ExcelTemplate.Update(path, Drawing()); // a second run must not pile validations up
            using var wb = ExcelReader.Open(path);
            var lists = wb.Worksheet("_lists");
            Assert.Equal(XLWorksheetVisibility.Hidden, lists.Visibility);
            Assert.Equal(new[] { "L001", "L002" }, Column(lists, 3));      // load
            Assert.Equal(new[] { "U001" }, Column(lists, 4));              // unload
            Assert.Equal(new[] { "B001" }, Column(lists, 5));              // buffer
            Assert.Equal(new[] { "RL1" }, Column(lists, 6));               // rtnload
            Assert.Equal(new[] { "RU1" }, Column(lists, 7));               // rtnunload
            Assert.Equal(new[] { "SB1" }, Column(lists, 8));               // standby
            Assert.Equal(new[] { "FN1" }, Column(lists, 9));               // finish
            var routes = wb.Worksheet("Routes");
            string Src(IXLWorksheet ws, int col) => ws.Cell(5, col).GetDataValidation().Value;
            Assert.Contains("_lists!$C$1:$C$2", Src(routes, 2));           // SupplyFromCP
            Assert.Contains("_lists!$D$1:$D$1", Src(routes, 3));           // SupplyToCP
            Assert.Contains("_lists!$E$1:$E$1", Src(routes, 4));           // BufferCP
            Assert.Contains("_lists!$F$1:$F$1", Src(routes, 5));
            Assert.Contains("_lists!$G$1:$G$1", Src(routes, 6));
            var groups = wb.Worksheet("AGV_Groups");
            Assert.Contains("_lists!$H$1:$H$1", Src(groups, 5));           // StandbyCPs
            Assert.Contains("_lists!$I$1:$I$1", Src(groups, 7));           // FinishCP
            Assert.Equal(XLErrorStyle.Warning, routes.Cell(5, 2).GetDataValidation().ErrorStyle);
            Assert.Single(routes.DataValidations, v => v.Ranges.Any(r => r.RangeAddress.FirstAddress.ColumnNumber == 2));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Standby_Drop_Down_Is_Skipped_While_A_Row_Holds_A_Comma_List_And_Stale_Names_Are_Cleared()
    {
        var path = Tmp();
        try
        {
            var l = Drawing();
            ExcelTemplate.Write(path, l);
            using (var wb = ExcelReader.Open(path)) Assert.NotEmpty(wb.Worksheet("AGV_Groups").Cell(3, 5).GetDataValidation().Value);
            var log = new List<ExcelEdit>();
            using (var wb = ExcelReader.Open(path))
            {
                wb.Worksheet("AGV_Groups").Cell(3, 5).Value = "SB1,SB2"; // the user lists two standby CPs
                ExcelTemplate.Update(wb, l, log);
                Assert.False(wb.Worksheet("AGV_Groups").Cell(3, 5).HasDataValidation);
                wb.Save();
            }
            Assert.Contains(log, e => e is ListEdit x && x.Sheet == "AGV_Groups" && x.Col == 5 && x.Formula == "");
            // the drawing lost a load CP: its name leaves the list
            var shorter = Drawing(); shorter.Cps.RemoveAll(c => c.Name == "L002");
            var log2 = new List<ExcelEdit>();
            using (var wb = ExcelReader.Open(path)) { ExcelTemplate.Update(wb, shorter, log2); Assert.Equal(new[] { "L001" }, Column(wb.Worksheet("_lists"), 3)); }
            Assert.Contains(log2, e => e is CellEdit c && c.Sheet == "_lists" && c.Row == 2 && c.Col == 3 && c.Value == "");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Cp_Lists_Share_The_Helper_Sheet_With_The_Asset_List_Without_Clearing_It()
    {
        var path = Tmp();
        var old = ExcelTemplate.AssetListFormula;
        try
        {
            ExcelTemplate.AssetListFormula = string.Join(",", Enumerable.Range(1, 40).Select(i => "agv_asset_" + i));
            ExcelTemplate.Write(path, Drawing());
            using var wb = ExcelReader.Open(path);
            Assert.Equal(40, Column(wb.Worksheet("_lists"), 1).Count);
            Assert.Equal(new[] { "L001", "L002" }, Column(wb.Worksheet("_lists"), 3));
        }
        finally { ExcelTemplate.AssetListFormula = old; File.Delete(path); }
    }

    [Fact]
    public void Cp_List_Edits_Are_In_The_Log_For_The_Excel_Fallback()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path);
            var log = new List<ExcelEdit>();
            using var wb = ExcelReader.Open(path);
            ExcelTemplate.Update(wb, Drawing(), log);
            Assert.Contains(log, e => e is AddSheetEdit a && a.Sheet == "_lists" && a.Hidden);
            Assert.Contains(log, e => e is CellEdit c && c.Sheet == "_lists" && c.Col == 3 && c.Row == 2 && c.Value == "L002");
            Assert.Contains(log, e => e is ListEdit x && x.Sheet == "Routes" && x.Formula == "=_lists!$C$1:$C$2" && x.Warn);
        }
        finally { File.Delete(path); }
    }

    // ---- E2 -------------------------------------------------------------------------------------------------------

    static ASH.Core.Layout.Layout Recorded()
    {
        var l = new ASH.Core.Layout.Layout();
        l.Cps.Add(Cp("SB1", "standby", "G1", "r1"));
        l.Cps.Add(Cp("LA", "load", "G1", "r1")); l.Cps.Add(Cp("LB", "load", "G1", "r1"));
        l.Cps.Add(Cp("UA", "unload", "G1", "r1"));
        l.Cps.Add(Cp("RLA", "rtnload", "G1", "r1")); l.Cps.Add(Cp("RUA", "rtnunload", "G1", "r1"));
        l.Cps.Add(Cp("FN", "finish", "G1", "r1"));
        l.Cps.Add(Cp("L9", "load", "G1")); l.Cps.Add(Cp("U9", "unload", "G1")); // placed one by one
        return l;
    }

    [Fact]
    public void Recorded_Sequence_Gives_One_M_Numbered_Material_Per_Load_Cp()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path, Recorded());
            var p = ExcelReader.Read(path);
            Assert.Equal(new[] { "M001", "M002", "M003" }, p.Materials.Select(m => m.Material));  // L9 stays on the one-by-one pairing; every material is M###
            Assert.All(p.Materials, m => Assert.Equal("G1", m.Group));
            var la = p.Routes.Single(r => r.SupplyFromCP == "LA");
            Assert.Equal(("LA", "UA", "RLA", "RUA"), (la.SupplyFromCP, la.SupplyToCP, la.ReturnFromCP, la.ReturnToCP));
            var lb = p.Routes.Single(r => r.SupplyFromCP == "LB");
            Assert.Equal(("UA", "RLA", "RUA"), (lb.SupplyToCP, lb.ReturnFromCP, lb.ReturnToCP));   // two loads, one unload: both go to it
            var one = p.Routes.Single(r => r.Material == "M001");
            Assert.Equal(("L9", "U9"), (one.SupplyFromCP, one.SupplyToCP));
            var g = Assert.Single(p.Groups);
            Assert.Equal("FN", g.FinishCP);
            Assert.Equal(new[] { "SB1" }, g.StandbyCPs);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Update_Adds_Recorded_Sequences_Once_Fills_Blank_Finish_Only_And_Says_What_It_Added()
    {
        var path = Tmp();
        try
        {
            var start = new ASH.Core.Layout.Layout();
            start.Cps.Add(Cp("SB1", "standby", "G1")); start.Cps.Add(Cp("L9", "load", "G1")); start.Cps.Add(Cp("U9", "unload", "G1"));
            ExcelTemplate.Write(path, start);
            var changes = ExcelTemplate.Update(path, Recorded());
            var p = ExcelReader.Read(path);
            Assert.Equal(new[] { "M001", "M002", "M003" }, p.Materials.Select(m => m.Material));
            Assert.Equal("FN", p.Groups[0].FinishCP);
            Assert.Contains(changes, c => c.Contains("자재 M002") && c.Contains("로딩 LA,LB") && c.Contains("완료 FN"));
            Assert.Contains(changes, c => c.Contains("완료 CP FN"));
            Assert.Empty(ExcelTemplate.Update(path, Recorded()));                       // nothing left to add
            using (var wb = ExcelReader.Open(path)) { wb.Worksheet("AGV_Groups").Cell(3, 7).Value = "OWN"; wb.Save(); }
            ExcelTemplate.Update(path, Recorded());
            Assert.Equal("OWN", ExcelReader.Read(path).Groups[0].FinishCP);              // typed value stays
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Recorded_Name_That_Is_Taken_Or_Unusable_Falls_Back_To_The_Next_M_Number()
    {
        var l = new ASH.Core.Layout.Layout();
        l.Cps.Add(Cp("M001", "load", "G1", "r")); l.Cps.Add(Cp("로딩1", "load", "G1", "r")); l.Cps.Add(Cp("U1", "unload", "G1", "r"));
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path, l);
            var names = ExcelReader.Read(path).Materials.Select(m => m.Material).ToList();
            Assert.Equal(2, names.Distinct().Count());
            Assert.Contains("M001", names);
            Assert.All(names, n => Assert.True(InputValidator.IsAmText(n)));
        }
        finally { File.Delete(path); }
    }

    // ---- E3 -------------------------------------------------------------------------------------------------------

    static ProjectInput Input(params Route[] routes)
    {
        var p = new ProjectInput();
        p.Groups.Add(new AgvGroup("A", "T", 1, 2, new List<string> { "SB1" }, "Nearest", 3, "FN1"));
        p.Materials.Add(new MaterialInfo("M1", "", "A", 60, 1, 10, 4, 1, null, null, false, 3));
        p.Routes.AddRange(routes);
        return p;
    }

    [Fact]
    public void Diff_Is_Empty_When_Excel_And_Drawing_Agree()
    {
        var l = Drawing(); l.Cps.RemoveAll(c => c.Name == "L002");
        var p = Input(new Route("M1", "L001", "U001", "B001", "RL1", "RU1", 0, 3));
        Assert.Empty(ExcelDrawingDiff.Compare(p, l));
    }

    [Fact]
    public void Diff_Reports_Missing_Wrong_Type_Unused_And_Group_Mismatch()
    {
        var l = Drawing();                                   // L002 is not used by any row
        l.Cps.Add(Cp("TR1", "traffic", ""));                 // traffic CPs belong to no row
        l.Cps.First(c => c.Name == "RU1").Group = "B";       // group differs from material M1 (A)
        var p = Input(new Route("M1", "L001", "L002", "NOPE", "RL1", "RU1", 0, 3)); // SupplyTo is a load CP, BufferCP is not drawn
        var items = ExcelDrawingDiff.Compare(p, l);
        var f = items.Select(d => d.Finding).ToList();
        Assert.Equal(new[] { ExcelDrawingDiff.Missing, ExcelDrawingDiff.WrongGroup, ExcelDrawingDiff.WrongType }.OrderBy(x => x), items.Where(d => d.Kind != ExcelDrawingDiff.Unused).Select(d => d.Kind).Distinct().OrderBy(x => x));
        Assert.All(items.Where(d => d.Finding.Message.Contains("쓰지 않음")), d => Assert.Equal(ExcelDrawingDiff.Unused, d.Kind));
        var missing = Assert.Single(f, x => x.Message.Contains("도면에 없는"));
        Assert.Equal((FindingCode.InputError, "Routes!3"), (missing.Code, missing.EntityId));
        Assert.Contains("BufferCP 'NOPE'", missing.Message);
        var type = Assert.Single(f, x => x.Message.Contains("load CP"));
        Assert.Equal(FindingCode.InputError, type.Code); Assert.Contains("SupplyToCP 'L002'", type.Message); Assert.Contains("unload 필요", type.Message);
        Assert.Equal(new Point2(4, 1), type.Where);          // zoom-to: the CP's position
        var grp = Assert.Single(f, x => x.Message.Contains("GROUP"));
        Assert.Equal(FindingCode.InputWarning, grp.Code); Assert.Contains("ReturnToCP 'RU1'", grp.Message);
        var unused = f.Where(x => x.Message.Contains("쓰지 않음")).Select(x => x.EntityId).ToList();
        Assert.Contains("U001", unused); Assert.Contains("B001", unused);   // the drawing's own unload / buffer, since the row uses others
        Assert.DoesNotContain("TR1", unused);
        Assert.DoesNotContain("L001", unused);
        Assert.All(f.Where(x => x.Message.Contains("쓰지 않음")), x => Assert.Equal(FindingCode.InputWarning, x.Code));
    }

    [Fact]
    public void Diff_Checks_Group_Standby_And_Finish_Columns()
    {
        var l = Drawing();
        var p = new ProjectInput();
        p.Groups.Add(new AgvGroup("A", "T", 1, 2, new List<string> { "SB1", "L001", "SBX" }, "Nearest", 3, "RL1"));
        var f = ExcelDrawingDiff.Compare(p, l).Select(d => d.Finding).ToList();
        Assert.Contains(f, x => x.EntityId == "AGV_Groups!3" && x.Message.Contains("StandbyCPs 'L001'") && x.Message.Contains("standby 필요"));
        Assert.Contains(f, x => x.Message.Contains("StandbyCPs 'SBX'") && x.Message.Contains("도면에 없는"));
        Assert.Contains(f, x => x.Message.Contains("FinishCP 'RL1'") && x.Message.Contains("finish 필요"));
        Assert.Contains(f, x => x.Message.Contains("CP 'FN1'") && x.Message.Contains("쓰지 않음"));
    }

    // ---- E4 -------------------------------------------------------------------------------------------------------

    static Finding Err(string id, string msg) => new Finding(FindingCode.InputError, msg, default(Point2), id);
    static Finding Warn(string id, string msg) => new Finding(FindingCode.InputWarning, msg, default(Point2), id);

    [Fact]
    public void Marks_Colour_The_Named_Cell_And_Carry_The_Message_As_A_Comment_Then_Clear_On_The_Next_Run()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path, Drawing());
            var f = new[]
            {
                Err("Routes!3", "SupplyToCP 'XX'이(가) 레이아웃에 없습니다."),
                Warn("Materials!3", "자재 'M001': 호출 임계값이 안전재고 이하 (CallThreshold 1 <= SafetyStock 2)."),
                Warn("Materials!3", "자재 'M001': Group 'G'..."),   // same cell family, different column
                Err("Settings", "no row"),                          // no cell reference: left out
                Err("Nope!3", "unknown sheet"),
            };
            Assert.True(ExcelMarks.MarkFile(path, f));
            using (var wb = ExcelReader.Open(path))
            {
                var c = wb.Worksheet("Routes").Cell(3, 3);                                  // SupplyToCP = column 3
                Assert.Equal(XLColor.FromArgb(ExcelMarks.ErrorRgb), c.Style.Fill.BackgroundColor);
                Assert.StartsWith("ASH 검증(오류)", c.GetComment().Text); Assert.Contains("SupplyToCP 'XX'", c.GetComment().Text);
                var m = wb.Worksheet("Materials");
                int callCol = Enumerable.Range(1, 30).First(i => m.Cell(1, i).GetString() == "CallThreshold");
                Assert.Equal(XLColor.FromArgb(ExcelMarks.WarnRgb), m.Cell(3, callCol).Style.Fill.BackgroundColor);
                Assert.Equal(XLColor.FromArgb(ExcelMarks.WarnRgb), m.Cell(3, 3).Style.Fill.BackgroundColor);   // Group = column 3
                Assert.Equal(3, wb.Worksheets.Sum(w => w.CellsUsed(XLCellsUsedOptions.All).Count(x => x.HasComment && x.GetComment().Text.StartsWith(ExcelMarks.Prefix))));
            }
            // the next run: only the new finding stays
            Assert.True(ExcelMarks.MarkFile(path, new[] { Warn("Routes!3", "BufferCP 'B'") }));
            using (var wb = ExcelReader.Open(path))
            {
                Assert.False(wb.Worksheet("Routes").Cell(3, 3).HasComment);
                Assert.Equal(XLFillPatternValues.None, wb.Worksheet("Routes").Cell(3, 3).Style.Fill.PatternType);
                Assert.Contains("BufferCP 'B'", wb.Worksheet("Routes").Cell(3, 4).GetComment().Text);
                Assert.Equal(XLColor.FromArgb(ExcelMarks.WarnRgb), wb.Worksheet("Routes").Cell(3, 4).Style.Fill.BackgroundColor);
            }
            Assert.True(ExcelMarks.MarkFile(path, new Finding[0]));       // fixed: all gone
            Assert.False(ExcelMarks.MarkFile(path, new Finding[0]));      // nothing to clear: no save
            using (var wb = ExcelReader.Open(path))
                Assert.Empty(wb.Worksheets.SelectMany(w => w.CellsUsed(XLCellsUsedOptions.All)).Where(x => x.HasComment));
            Assert.Equal("M001", ExcelReader.Read(path).Materials[0].Material);       // the data is untouched
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Marks_Keep_A_Users_Own_Comment_And_The_Auto_Fill_Of_Formula_Columns()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path, Drawing());
            using (var wb = ExcelReader.Open(path)) { wb.Worksheet("Routes").Cell(3, 3).CreateComment().AddText("내 메모"); wb.Save(); }
            ExcelMarks.MarkFile(path, new[] { Err("Routes!3", "SupplyToCP x") });
            using (var wb = ExcelReader.Open(path))
            {
                var c = wb.Worksheet("Routes").Cell(3, 3);
                Assert.Equal("내 메모", c.GetComment().Text);                      // not replaced
                Assert.Equal(XLColor.FromArgb(ExcelMarks.ErrorRgb), c.Style.Fill.BackgroundColor);
            }
            ExcelMarks.MarkFile(path, new Finding[0]);
            using (var wb = ExcelReader.Open(path))
            {
                Assert.Equal("내 메모", wb.Worksheet("Routes").Cell(3, 3).GetComment().Text);          // a comment without the prefix is never cleared
                var m = wb.Worksheet("Materials");
                int total = Enumerable.Range(1, 30).First(i => m.Cell(1, i).GetString() == "CallThresholdTotal");
                Assert.Equal(XLColor.FromArgb(ExcelTemplate.AutoFillRgb), m.Cell(3, total).Style.Fill.BackgroundColor);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Marks_Log_Always_Clears_Every_Template_Sheet_So_Excel_Can_Do_Its_Own_Search()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path, Drawing());
            var log = new List<ExcelEdit>();
            using var wb = ExcelReader.Open(path);
            ExcelMarks.Apply(wb, new[] { Warn("Routes!3", "SupplyFromCP 'L'") }, log);
            Assert.Equal(ExcelTemplate.Sheets.Length, log.OfType<ClearMarksEdit>().Count());
            Assert.True(log.FindIndex(e => e is ClearMarksEdit) < log.FindIndex(e => e is MarkEdit));
            var mark = log.OfType<MarkEdit>().Single();
            Assert.Equal(("Routes", 3, 2, ExcelMarks.WarnRgb), (mark.Sheet, mark.Row, mark.Col, mark.Rgb));
            Assert.StartsWith("ASH 검증(경고)", mark.Text);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Validator_Findings_Of_A_Real_Input_Land_On_The_Cell_Of_Their_Column()
    {
        var path = Tmp();
        try
        {
            ExcelTemplate.Write(path, Drawing());
            using (var wb = ExcelReader.Open(path)) { wb.Worksheet("Routes").Cell(3, 3).Value = "NOWHERE"; wb.Save(); }
            var input = ExcelReader.Read(path);
            var f = InputValidator.Validate(input, Drawing().Cps.Select(c => c.Name).ToList());
            ExcelMarks.MarkFile(path, f);
            using var wb2 = ExcelReader.Open(path);
            Assert.Contains("NOWHERE", wb2.Worksheet("Routes").Cell(3, 3).GetComment().Text);   // SupplyToCP
        }
        finally { File.Delete(path); }
    }
}
