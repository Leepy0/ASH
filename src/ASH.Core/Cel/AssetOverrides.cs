using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ASH.Core.Export;
using ASH.Core.Input;

namespace ASH.Core.Cel;

public static class AssetOverrides
{
    /// <summary>For each AgvType with Asset != "": fetch(typeName, assetName) → cel text or null; null/exception → one warning line and no override for that type.
    /// Returns VehSegText overrides keyed by type name (template = agvs_vehseg.txt).</summary>
    public static Dictionary<string, VehicleTemplateOverride> Build(IReadOnlyList<AgvType> types, IReadOnlyList<string> template,
        Func<string, string, string?> fetchCel, ICollection<string> warnings)
    {
        var res = new Dictionary<string, VehicleTemplateOverride>(StringComparer.OrdinalIgnoreCase);
        var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool down = false; int downTypes = 0; // an unreachable server stalls once (3 s), the other types skip the call
        foreach (var t in types)
        {
            if (string.IsNullOrWhiteSpace(t.Asset) || res.ContainsKey(t.Type) || !failed.Add(t.Type)) continue;
            if (down) { downTypes++; continue; }
            string? why = null;
            try
            {
                var cel = fetchCel(t.Type, t.Asset);
                if (cel == null) why = "에셋을 받지 못했습니다";
                else
                {
                    var boxes = CelReader.ReadBoxes(cel);
                    if (boxes.Count == 0) why = "에셋에 상자가 없습니다";
                    else { res[t.Type] = new VehicleTemplateOverride(VehSegText: CelWriter.VehSeg(template, boxes)); failed.Remove(t.Type); }
                }
            }
            catch (Exception e) when (e is HttpRequestException || e is TimeoutException || e is OperationCanceledException) { down = true; downTypes++; }
            catch (Exception e) when (!(e is OutOfMemoryException)) { why = e.Message; }
            if (why != null) warnings.Add($"타입 {t.Type}: 에셋 '{t.Asset}' - {why}. 기본 그림을 씁니다.");
        }
        if (downTypes > 0) warnings.Add($"서버에 닿지 않아 에셋 그림을 쓰지 않습니다 ({downTypes}개 타입). 기본 그림을 씁니다.");
        return res;
    }

    /// <summary>agvs_vehseg.txt, the template CelWriter.VehSeg fills.</summary>
    public static string[] VehSegTemplate()
    {
        using var s = typeof(AssetOverrides).Assembly.GetManifestResourceStream("ASH.Core.Export.Templates.agvs_vehseg.txt")!;
        return new StreamReader(s).ReadToEnd().Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
    }

    /// <summary>The exporter looks overrides up by group (= AGVS vehicle type) name: the same picture for every group of an overridden type.</summary>
    public static Dictionary<string, VehicleTemplateOverride> ForGroups(IReadOnlyDictionary<string, VehicleTemplateOverride> byType, IEnumerable<AgvGroup> groups)
    {
        var res = new Dictionary<string, VehicleTemplateOverride>();
        foreach (var g in groups) if (byType.TryGetValue(g.Type, out var o)) res[g.Group] = o;
        return res;
    }
}

[DataContract] internal sealed class AssetRow
{
    [DataMember(Name = "id")] public string Id { get; set; } = "";
    [DataMember(Name = "name")] public string Name { get; set; } = "";
    [DataMember(Name = "typeName")] public string TypeName { get; set; } = "";
}
[DataContract] internal sealed class AssetList { [DataMember(Name = "assets")] public List<AssetRow> Assets { get; set; } = new List<AssetRow>(); }

/// <summary>GET /api/catalog/assets and /api/catalog/assets/{id}/asset.cel with 3 s per call. Failures are thrown (HttpRequestException, TimeoutException, InvalidDataException).</summary>
public sealed class AssetCatalogClient
{
    private readonly Uri _base;
    private readonly HttpMessageHandler? _handler;
    private readonly TimeSpan _timeout;
    private List<AssetRow>? _rows;

    public AssetCatalogClient(Uri server, HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        _base = new Uri(server.AbsoluteUri.TrimEnd('/') + "/");
        _handler = handler;
        _timeout = timeout ?? TimeSpan.FromSeconds(3);
    }

    string Get(string rel)
    {
        using var cts = new CancellationTokenSource(_timeout);
        // Task.Run: ZWCAD's UI sync context cannot deadlock the wait; the cap is for net48 DNS/WPAD stalls that ignore the token
        var task = Task.Run(async () =>
        {
            using var http = _handler == null ? new HttpClient(Update.UpdateClient.DefaultHandler()) : new HttpClient(_handler, false);
            http.Timeout = Timeout.InfiniteTimeSpan;
            using var r = await http.GetAsync(new Uri(_base, rel), cts.Token).ConfigureAwait(false);
            var body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!r.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)r.StatusCode}");
            return body;
        });
        try
        {
            if (!task.Wait(_timeout + TimeSpan.FromSeconds(1))) throw new TimeoutException();
        }
        catch (AggregateException a) { throw a.InnerException is OperationCanceledException ? new TimeoutException() : a.InnerException ?? a; }
        return task.Result;
    }

    List<AssetRow> Rows()
    {
        if (_rows != null) return _rows;
        try
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(Get("api/catalog/assets")));
            return _rows = (new DataContractJsonSerializer(typeof(AssetList)).ReadObject(ms) as AssetList)?.Assets ?? new List<AssetRow>();
        }
        catch (Exception e) when (e is SerializationException || e is ArgumentException || e is FormatException)
        {
            throw new InvalidDataException("서버 응답을 읽을 수 없습니다: " + e.Message, e);
        }
    }

    /// <summary>All asset names, distinct, without commas (a comma would split the Excel list item).</summary>
    public IReadOnlyList<string> Names() => Rows().Select(r => r.Name).Where(n => n.Length > 0 && n.IndexOf(',') < 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The asset.cel text of the asset called <paramref name="name"/> (the one of <paramref name="typeName"/> first when several types have that name); null when no such asset.</summary>
    public string? FetchCel(string typeName, string name)
    {
        var m = Rows().Where(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        var a = m.FirstOrDefault(r => string.Equals(r.TypeName, typeName, StringComparison.OrdinalIgnoreCase)) ?? m.FirstOrDefault();
        return a == null ? null : Get($"api/catalog/assets/{Uri.EscapeDataString(a.Id)}/asset.cel");
    }
}
