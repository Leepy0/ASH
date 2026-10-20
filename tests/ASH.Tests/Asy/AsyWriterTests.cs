using System;
using System.Globalization;
using System.IO;
using System.Linq;
using ASH.Core.Asy;
using Xunit;

namespace ASH.Tests.Asy;

public class AsyWriterTests
{
    [Fact] public void Line_Path_Format() => Assert.Equal("GPATH name path11 type DefaultGuidePath piece begx -50 begy 50 endx 11 endy 50 upz 1",
        AsyWriter.FormatPath(new GuidePath("path11","DefaultGuidePath",false,false,new LinePiece(new(-50,50), new(11,50)))));
    [Fact] public void Arc_Spur_Format() => Assert.Equal("GPATH name path16 type DefaultGuidePath piece cenx -50 ceny 47 begx -53 begy 47 upz 1 angle -900",
        AsyWriter.FormatPath(new GuidePath("path16","DefaultGuidePath",false,false,new ArcPiece(new(-50,47), new(-53,47), -900))));
    [Fact] public void Spur_Crab_Keywords() => Assert.Contains(" spur crab piece ", AsyWriter.FormatPath(new GuidePath("p","T",true,true,new LinePiece(new(0,0),new(1,0)))));
    [Fact] public void Cp_Format_With_Align() => Assert.Equal("CPOINT name workcheck type DefaultControlPoint align leadingpap at path30 1.10268372703412",
        AsyWriter.FormatCp(new ControlPoint("workcheck","DefaultControlPoint","path30",1.10268372703412,"leadingpap")));
    [Fact] public void RoundTrip_Demo_Preserves_All_Entity_Lines()
    {
        var src = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures/agvdemo/agv.asy"));
        var sys = AsyReader.Read(new StringReader(string.Join("\n", src)));
        var sw = new StringWriter(); AsyWriter.Write(sys, sw);
        var outLines = sw.ToString().Split('\n').Select(l => l.TrimEnd('\r'));
        static string Norm(string l) => string.Join(" ", l.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d.ToString("R", CultureInfo.InvariantCulture) : t));
        string[] keys = { "GPATH ", "CPOINT ", "GPATHTYPE ", "CPOINTTYPE ", "PARKLST ", "WORKLST " };
        foreach (var k in keys)
            Assert.Equal(src.Where(l => l.StartsWith(k)).Select(Norm), outLines.Where(l => l.StartsWith(k)).Select(Norm));
        Assert.Equal(src.Count(l => l.StartsWith("AGVVEHSEG")), outLines.Count(l => l.StartsWith("AGVVEHSEG")));
    }
}
