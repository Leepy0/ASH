using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using ASH.Core.Runner;
using ASH.Core.Settings;

namespace ASH.Core.Catalog;

public static class CatalogFetch
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HardCapGrace = TimeSpan.FromSeconds(2);

    /// <summary>The visible catalog types, or null (no address, connection failure, over 3 s, non-200, bad JSON, empty list: all the same fallback).</summary>
    public static IReadOnlyList<CatalogType>? TryGet(string? serverUrl, HttpMessageHandler? handler = null)
    {
        if (!UserSettings.TryNormalizeUrl(serverUrl, out var url)) return null;
        try
        {
            // Task.Run so ZWCAD's UI sync context cannot deadlock the wait; the hard cap is for net48 DNS/WPAD stalls that ignore the token
            using var cts = new System.Threading.CancellationTokenSource(Timeout);
            var task = Task.Run(async () =>
            {
                using var c = new JobServerClient(new Uri(url), handler);
                return await c.GetCatalogTypesAsync(cts.Token).ConfigureAwait(false);
            });
            if (Task.WhenAny(task, Task.Delay(Timeout + HardCapGrace)).GetAwaiter().GetResult() != task)
            {
                task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); // observe the late failure
                return null;
            }
            var list = task.GetAwaiter().GetResult().Types?.Where(t => t?.Name is { Length: > 0 } && t.Defaults != null).ToList();
            return list == null || list.Count == 0 ? null : list;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
