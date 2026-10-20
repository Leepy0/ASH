using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ASH.Core.Export;

/// <summary>Applies VehicleTemplateOverride texts: verbatim except the lines that keep the model consistent (missing ones are inserted).</summary>
internal static class VehicleOverrideApplier
{
    static List<string> Lines(string text, string keyword)
    {
        var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').ToList();
        if (!Regex.IsMatch(lines[0], @"^\s*" + keyword + @"\b")) throw new ArgumentException($"override text must start with {keyword}");
        return lines;
    }

    public static List<string> VehSeg(string text, string seg)
    {
        var l = Lines(text, "AGVVEHSEG");
        var re = new Regex(@"^(\s*AGVVEHSEG\s+name\s+)\S+");
        if (!re.IsMatch(l[0])) throw new ArgumentException("AGVVEHSEG header must contain 'name <X>'");
        l[0] = re.Replace(l[0], m => m.Groups[1].Value + seg);
        return l;
    }

    static string Force(string header, string key, string value)
    {
        var re = new Regex(@"(\b" + key + @"\s+)\S+");
        return re.IsMatch(header) ? re.Replace(header, m => m.Groups[1].Value + value) : header.TrimEnd() + " " + key + " " + value;
    }

    public static List<string> Vehicle(string text, string type, int numVeh, string seg, string nameList)
    {
        var l = Lines(text, "AGVSVEH");
        l[0] = Force(Force(l[0], "type", type), "numveh", numVeh.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var segRe = new Regex(@"^(\s*vehsegs\s+item\s+)\S+"); var startRe = new Regex(@"^(\s*start\s+)\S+");
        int at = 1;
        if (l.Any(x => segRe.IsMatch(x))) l = l.Select(x => segRe.Replace(x, m => m.Groups[1].Value + seg)).ToList();
        else l.Insert(at++, "\tvehsegs item " + seg);
        if (l.Any(x => startRe.IsMatch(x))) l = l.Select(x => startRe.Replace(x, m => m.Groups[1].Value + nameList)).ToList();
        else l.Insert(at, "\tstart " + nameList);
        return l;
    }
}
