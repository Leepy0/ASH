namespace ASH.Server;

internal static partial class Routes
{
    /// <summary>Vehicle type catalog read API (the AutoMod plugin fetches it). Projected so hidden/updated* never leave the server.</summary>
    public static void MapCatalog(this WebApplication app)
    {
        app.MapGet("/api/catalog/types", (HttpContext ctx, CatalogStore store) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Json(new { types = store.Types().Select(t => new { name = t.Name, label = t.Label, category = t.Category, kind = t.Kind, defaults = t.Defaults, assets = t.Assets }) });
        });
    }
}
