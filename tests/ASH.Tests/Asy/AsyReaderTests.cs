using ASH.Core.Asy;

namespace ASH.Tests.Asy;

public class AsyReaderTests
{
    static AgvsSystem Demo() => AsyReader.ReadFile(Path.Combine(AppContext.BaseDirectory, "Fixtures/agvdemo/agv.asy"));

    [Fact] public void Reads_Header() { var s = Demo(); Assert.Equal("12.6.1.19", s.Version); Assert.Equal(new[]{"Feet","Seconds"}, s.UnitsTokens); }
    [Fact] public void Reads_21_Paths_11_Spur_5_Arcs() { var s = Demo(); Assert.Equal(21, s.Paths.Count); Assert.Equal(11, s.Paths.Count(p => p.IsSpur && p.IsCrab)); Assert.Equal(5, s.Paths.Count(p => p.Piece is ArcPiece)); }
    [Fact] public void Reads_32_ControlPoints() { var s = Demo(); Assert.Equal(32, s.ControlPoints.Count); var z1 = s.ControlPoints.Single(c => c.Name == "z1"); Assert.Equal("path11_1", z1.PathName); Assert.Equal(50, z1.Dist); }
    [Fact] public void Reads_Workcheck_Align() { var c = Demo().ControlPoints.Single(x => x.Name == "workcheck"); Assert.Equal("leadingpap", c.Align); Assert.Equal(1.10268372703412, c.Dist, 9); }
    [Fact] public void Merges_NameList_Lines() { var s = Demo(); Assert.Equal(new[]{"p_1","p_2","p_3","p_4","p_5"}, s.NameLists["nlwork"]); }
    [Fact] public void Arc_End_Is_Computed_CW_For_Negative_Angle()
    {
        var p16 = Demo().Paths.Single(p => p.Name == "path16").Piece as ArcPiece;
        Assert.Equal(-900, p16!.AngleTenths);
        Assert.Equal(-50, p16.End.X, 6); Assert.Equal(50, p16.End.Y, 6);
    }
    [Fact] public void Keeps_VehicleSegment_And_Vehicle_Raw() { var s = Demo(); Assert.StartsWith("AGVVEHSEG name DefSegment", s.VehicleSegmentLines[0]); Assert.StartsWith("AGVSVEH type DefVehicle numveh 10", s.VehicleLines[0]); Assert.Equal(6, s.ParkListLines.Count); Assert.Single(s.WorkListLines); }
}
