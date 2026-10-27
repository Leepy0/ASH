using ASH.Core.Layout;
using ZwSoft.ZwCAD.DatabaseServices;

namespace ASH.Plugin.Cad;

/// <summary>Path XData under regapp ASH: (1000) name, (1070) dir 0/1, (1000) path type code from PathKinds ("path", "two", "spur", "crab", "spur+crab", ...).</summary>
public static class XData
{
    public static (string Name, bool Reversed, bool IsSpur, bool IsTwoWay, bool IsCrab)? ReadPath(Entity e)
    {
        using var rb = e.GetXDataForApplication(Conventions.AppName);
        if (rb == null) return null;
        var v = rb.AsArray();
        if (v.Length < 4 || !(v[1].Value is string name) || v[2].TypeCode != (short)DxfCode.ExtendedDataInteger16 || !(v[3].Value is string ptype)) return null; // foreign/old layout: treat as untagged
        var k = PathKinds.Decode(ptype); // older drawings hold "path" or "spur"
        return (name, System.Convert.ToInt32(v[2].Value) == 1, k.Spur, k.Two, k.Crab);
    }

    public static void WritePath(Entity e, string name, bool reversed, (bool Spur, bool Two, bool Crab) kind, Transaction tr)
    {
        EnsureRegApp(e.Database, tr);
        using var rb = new ResultBuffer(
            new TypedValue((int)DxfCode.ExtendedDataRegAppName, Conventions.AppName),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, name),
            new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)(reversed ? 1 : 0)),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, PathKinds.Encode(kind.Spur, kind.Two, kind.Crab)));
        e.XData = rb;
    }

    const string ConvTag = "conv";

    /// <summary>Conveyor XData (AGV_CONV polylines, AGVCONV): (1000) name, (1000) "conv", (1000) handle of its label text ("" = none).
    /// The second item is a string, so ReadPath treats it as foreign and the two never mix.</summary>
    public static (string Name, string LabelHandle)? ReadConv(Entity e) => ReadTagged(e, ConvTag);

    public static void WriteConv(Entity e, string name, string labelHandle, Transaction tr) => WriteTagged(e, name, ConvTag, labelHandle, tr);

    const string UseTag = "use";

    /// <summary>Use-point marker XData (the bar on AGV_CONV_USE, AGVCONVUSE): (1000) conveyor name, (1000) "use", (1000) handle of its label text.</summary>
    public static (string Name, string LabelHandle)? ReadUse(Entity e) => ReadTagged(e, UseTag);

    public static void WriteUse(Entity e, string convName, string labelHandle, Transaction tr) => WriteTagged(e, convName, UseTag, labelHandle, tr);

    static (string Name, string LabelHandle)? ReadTagged(Entity e, string convTag)
    {
        using var rb = e.GetXDataForApplication(Conventions.AppName);
        if (rb == null) return null;
        var v = rb.AsArray();
        if (v.Length < 3 || !(v[1].Value is string name) || !(v[2].Value is string tag) || tag != convTag) return null;
        return (name, v.Length > 3 && v[3].Value is string h ? h : "");
    }

    static void WriteTagged(Entity e, string name, string convTag, string labelHandle, Transaction tr)
    {
        EnsureRegApp(e.Database, tr);
        using var rb = new ResultBuffer(
            new TypedValue((int)DxfCode.ExtendedDataRegAppName, Conventions.AppName),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, name),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, convTag),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, labelHandle));
        e.XData = rb;
    }

    public static void EnsureRegApp(Database db, Transaction tr)
    {
        var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
        if (rat.Has(Conventions.AppName)) return;
        rat.UpgradeOpen();
        var r = new RegAppTableRecord { Name = Conventions.AppName };
        rat.Add(r);
        tr.AddNewlyCreatedDBObject(r, true);
    }
}
