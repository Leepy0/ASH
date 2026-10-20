using ZwSoft.ZwCAD.DatabaseServices;

namespace ASH.Plugin.Cad;

/// <summary>Path XData under regapp ASH: (1000) name, (1070) dir 0/1, (1000) ptype "path"|"spur".</summary>
public static class XData
{
    public static (string Name, bool Reversed, bool IsSpur)? ReadPath(Entity e)
    {
        using var rb = e.GetXDataForApplication(Conventions.AppName);
        if (rb == null) return null;
        var v = rb.AsArray();
        if (v.Length < 4) return null;
        return ((string)v[1].Value, System.Convert.ToInt32(v[2].Value) == 1, (string)v[3].Value == "spur");
    }

    public static void WritePath(Entity e, string name, bool reversed, bool isSpur, Transaction tr)
    {
        EnsureRegApp(e.Database, tr);
        e.XData = new ResultBuffer(
            new TypedValue((int)DxfCode.ExtendedDataRegAppName, Conventions.AppName),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, name),
            new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)(reversed ? 1 : 0)),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, isSpur ? "spur" : "path"));
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
