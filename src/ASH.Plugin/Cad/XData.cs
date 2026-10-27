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
    public static (string Name, string LabelHandle)? ReadConv(Entity e)
    {
        using var rb = e.GetXDataForApplication(Conventions.AppName);
        if (rb == null) return null;
        var v = rb.AsArray();
        if (v.Length < 3 || !(v[1].Value is string name) || !(v[2].Value is string tag) || tag != ConvTag) return null;
        return (name, v.Length > 3 && v[3].Value is string h ? h : "");
    }

    public static void WriteConv(Entity e, string name, string labelHandle, Transaction tr)
    {
        EnsureRegApp(e.Database, tr);
        using var rb = new ResultBuffer(
            new TypedValue((int)DxfCode.ExtendedDataRegAppName, Conventions.AppName),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, name),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, ConvTag),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, labelHandle));
        e.XData = rb;
    }

    /// <summary>AGVCPROUTE: the id of the sequence a CP block was placed in, under its own regapp so it never mixes with the path / conveyor XData.</summary>
    const string RouteApp = "ASH_ROUTE";

    public static string ReadCpRoute(Entity e)
    {
        using var rb = e.GetXDataForApplication(RouteApp);
        var v = rb?.AsArray();
        return v != null && v.Length > 1 && v[1].Value is string id ? id : "";
    }

    public static void WriteCpRoute(Entity e, string routeId, Transaction tr)
    {
        EnsureRegApp(e.Database, tr, RouteApp);
        using var rb = new ResultBuffer(
            new TypedValue((int)DxfCode.ExtendedDataRegAppName, RouteApp),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, routeId));
        e.XData = rb;
    }

    public static void EnsureRegApp(Database db, Transaction tr, string app = Conventions.AppName)
    {
        var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
        if (rat.Has(app)) return;
        rat.UpgradeOpen();
        var r = new RegAppTableRecord { Name = app };
        rat.Add(r);
        tr.AddNewlyCreatedDBObject(r, true);
    }
}
