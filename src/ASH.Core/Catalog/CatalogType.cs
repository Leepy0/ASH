using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;
using ASH.Core.Input;

namespace ASH.Core.Catalog;

// Public get/set properties: the server's System.Text.Json ignores fields; the plugin reads the same JSON with DataContractJsonSerializer.
[DataContract]
public sealed class CatalogDefaults
{
    [DataMember(Name = "speedLoadedStraight")] public double SpeedLoadedStraight { get; set; }
    [DataMember(Name = "speedLoadedCurve")] public double SpeedLoadedCurve { get; set; }
    [DataMember(Name = "speedEmptyStraight")] public double SpeedEmptyStraight { get; set; }
    [DataMember(Name = "speedEmptyCurve")] public double SpeedEmptyCurve { get; set; }
    [DataMember(Name = "accel")] public double Accel { get; set; }
    [DataMember(Name = "decel")] public double Decel { get; set; }
    [DataMember(Name = "capacity")] public int Capacity { get; set; }
    [DataMember(Name = "loadTime")] public double LoadTime { get; set; }
    [DataMember(Name = "unloadTime")] public double UnloadTime { get; set; }
    [DataMember(Name = "mtbf")] public double MTBF { get; set; }
    [DataMember(Name = "mttr")] public double MTTR { get; set; }
    [DataMember(Name = "length")] public double Length { get; set; }
    [DataMember(Name = "width")] public double Width { get; set; }
    [DataMember(Name = "color")] public string Color { get; set; } = "";
}

[DataContract]
public sealed class CatalogType
{
    [DataMember(Name = "name")] public string Name { get; set; } = "";
    [DataMember(Name = "label")] public string Label { get; set; } = "";
    [DataMember(Name = "category")] public string Category { get; set; } = "";
    [DataMember(Name = "kind")] public string Kind { get; set; } = "";
    [DataMember(Name = "defaults")] public CatalogDefaults Defaults { get; set; } = new CatalogDefaults();
    [DataMember(Name = "assets")] public List<string> Assets { get; set; } = new List<string>();
    // server-file only: the API hides hidden types and omits these
    [DataMember(Name = "hidden", EmitDefaultValue = false)] public bool Hidden { get; set; }
    [DataMember(Name = "updatedUtc", EmitDefaultValue = false)] public string UpdatedUtc { get; set; } = "";
    [DataMember(Name = "updatedBy", EmitDefaultValue = false)] public string UpdatedBy { get; set; } = "";

    internal static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static string Curve(double curve, double straight) => curve == 0 || curve == straight ? "" : N(curve);

    /// <summary>Strings in the AGV_Types column order of ExcelTemplate (a curve equal to the straight speed or 0 stays blank; Asset stays blank).</summary>
    public static string[] ToTemplateRow(CatalogType t)
    {
        var d = t.Defaults;
        return new[]
        {
            t.Name, t.Category, N(d.SpeedLoadedStraight), Curve(d.SpeedLoadedCurve, d.SpeedLoadedStraight),
            N(d.SpeedEmptyStraight), Curve(d.SpeedEmptyCurve, d.SpeedEmptyStraight), N(d.Accel), N(d.Decel),
            d.Capacity.ToString(CultureInfo.InvariantCulture), N(d.LoadTime), N(d.UnloadTime), N(d.MTBF), N(d.MTTR),
            N(d.Length), N(d.Width), d.Color, t.Kind, "",
        };
    }
}

[DataContract]
public sealed class CatalogTypes
{
    [DataMember(Name = "types")] public List<CatalogType> Types { get; set; } = new List<CatalogType>();
}
