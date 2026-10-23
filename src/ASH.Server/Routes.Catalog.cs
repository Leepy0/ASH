using System.Globalization;
using System.Net;
using System.Text;
using ASH.Core.Catalog;

namespace ASH.Server;

internal static partial class Routes
{
    /// <summary>Vehicle type catalog: read API (the AutoMod plugin fetches it, projected so hidden/updated* never leave the server) and the admin page.</summary>
    public static void MapCatalog(this WebApplication app)
    {
        app.MapGet("/api/catalog/types", (HttpContext ctx, CatalogStore store) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Json(new { types = store.Types().Select(t => new { name = t.Name, label = t.Label, category = t.Category, kind = t.Kind, defaults = t.Defaults, assets = t.Assets }) });
        });

        app.MapGet("/admin/types", (HttpContext ctx, ReleaseStore rs, AdminSessions sessions, CatalogStore store) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            if (!rs.UploadsEnabled || !(HeaderAdmin(ctx, rs) || CookieAdmin(ctx, sessions)))
                return Html(ServerPages.Login(rs.UploadsEnabled, null, "/admin/types", "타입·투자비(관리자)"), rs.UploadsEnabled ? 401 : 403);
            CatalogType? editing = null;
            string? msg = store.LoadError is { } le ? Bad($"types.json을 읽을 수 없습니다(저장은 막혀 있습니다): {WebUtility.HtmlEncode(le)}") : null;
            if (ctx.Request.Query["edit"].ToString() is { Length: > 0 } en) editing = store.Get(en);
            else if (ctx.Request.Query["clone"].ToString() is { Length: > 0 } cn && store.Get(cn) is { } src)
                editing = new CatalogType { Label = src.Label, Category = src.Category, Kind = src.Kind, Defaults = Copy(src.Defaults), Hidden = src.Hidden };
            return Html(Render(store, editing, msg));
        });

        app.MapPost("/admin/types", async (HttpContext ctx, ReleaseStore rs, AdminSessions sessions, CatalogStore store) =>
        {
            if (DenyPost(ctx, rs, sessions) is { } deny) return deny;
            var f = await FormFrom(ctx);
            if (f == null) return Msg(400, "폼이 아닙니다.");
            var errs = new List<string>();
            double D(string k)
            {
                var s = f[k].ToString().Trim();
                if (s.Length == 0) return 0;
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && !double.IsInfinity(v)) return v;
                errs.Add($"'{k}' 값 '{s}'은(는) 숫자가 아닙니다.");
                return 0;
            }
            int cap = 1;
            if (!int.TryParse(f["capacity"].ToString().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out cap)) { errs.Add($"'capacity' 값 '{f["capacity"]}'은(는) 정수가 아닙니다."); cap = 1; }
            var t = new CatalogType
            {
                Name = f["name"].ToString().Trim(), Label = f["label"].ToString().Trim(), Category = f["category"].ToString().Trim(), Kind = f["kind"].ToString().Trim(),
                Hidden = f["hidden"].ToString().Length > 0,
                Defaults = new CatalogDefaults
                {
                    SpeedLoadedStraight = D("speedLoadedStraight"), SpeedLoadedCurve = D("speedLoadedCurve"), SpeedEmptyStraight = D("speedEmptyStraight"), SpeedEmptyCurve = D("speedEmptyCurve"),
                    Accel = D("accel"), Decel = D("decel"), Capacity = cap, LoadTime = D("loadTime"), UnloadTime = D("unloadTime"), MTBF = D("mtbf"), MTTR = D("mttr"),
                    Length = D("length"), Width = D("width"), Color = f["color"].ToString().Trim(),
                },
            };
            if (store.Get(t.Name) is { } old) t.Assets = old.Assets; // the form never edits assets
            var priceText = f["unitPrice"].ToString().Trim();
            decimal price = 0;
            var hasPrice = priceText.Length > 0;
            if (hasPrice && !decimal.TryParse(priceText.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out price)) { errs.Add($"'unitPrice' 값 '{priceText}'은(는) 숫자가 아닙니다."); hasPrice = false; }
            if (errs.Count == 0 && store.Save(t, "admin") is { } why) errs.Add(why);
            if (errs.Count > 0) return Html(Render(store, t, Bad(string.Join("<br>", errs.Select(WebUtility.HtmlEncode)))), 400);

            var msg = Ok($"{WebUtility.HtmlEncode(t.Name)} 저장했습니다.");
            if (hasPrice)
            {
                var rows = store.Prices().Values.Where(p => !p.Type.Equals(t.Name, StringComparison.OrdinalIgnoreCase)).Append(new PriceRow(t.Name, price, store.Prices().TryGetValue(t.Name, out var o) ? o.Currency : "KRW", o?.Note ?? ""));
                var res = store.SavePrices(PricesCsv(rows));
                if (res.Errors.Count > 0) return Html(Render(store, t, msg + Bad(PriceErrors(res))), 400);
            }
            var d = t.Defaults;
            if (d.SpeedLoadedCurve > d.SpeedLoadedStraight) msg += Warn("적재 곡선 속도가 직선 속도보다 큽니다.");
            if (d.SpeedEmptyCurve > (d.SpeedEmptyStraight > 0 ? d.SpeedEmptyStraight : d.SpeedLoadedStraight)) msg += Warn("공차 곡선 속도가 직선 속도보다 큽니다.");
            return Html(Render(store, t, msg));
        });

        app.MapPost("/admin/types/prices", async (HttpContext ctx, ReleaseStore rs, AdminSessions sessions, CatalogStore store) =>
        {
            if (DenyPost(ctx, rs, sessions) is { } deny) return deny;
            var csv = (await FormFrom(ctx))?["csv"].ToString();
            if (csv == null) return Msg(400, "폼이 아닙니다.");
            var res = store.SavePrices(csv);
            if (res.Errors.Count > 0) return Html(Render(store, null, Bad(PriceErrors(res)), csv), 400);
            return Html(Render(store, null, Ok($"투자비 {res.Changed}행을 저장했습니다.")));
        });
    }

    static string Ok(string html) => $"<div class=\"msg ok\">{html}</div>";
    static string Bad(string html) => $"<div class=\"msg bad\">{html}</div>";
    static string Warn(string text) => $"<div class=\"msg warn\">{WebUtility.HtmlEncode(text)}</div>";
    static string PriceErrors(PriceSave r) => "투자비를 저장하지 않았습니다.<br>" + string.Join("<br>", r.Errors.Select(e => $"{e.Row}행: {WebUtility.HtmlEncode(e.Why)}"));

    static string Render(CatalogStore store, CatalogType? editing, string? msg, string? csvOverride = null) =>
        CatalogPages.Types(store.Types(includeHidden: true), store.Prices(), csvOverride ?? store.PricesCsv(), editing, msg);

    static CatalogDefaults Copy(CatalogDefaults d) => new()
    {
        SpeedLoadedStraight = d.SpeedLoadedStraight, SpeedLoadedCurve = d.SpeedLoadedCurve, SpeedEmptyStraight = d.SpeedEmptyStraight, SpeedEmptyCurve = d.SpeedEmptyCurve, Accel = d.Accel, Decel = d.Decel,
        Capacity = d.Capacity, LoadTime = d.LoadTime, UnloadTime = d.UnloadTime, MTBF = d.MTBF, MTTR = d.MTTR, Length = d.Length, Width = d.Width, Color = d.Color,
    };

    static string PricesCsv(IEnumerable<PriceRow> rows)
    {
        static string Q(string s) => s.IndexOfAny(new[] { ',', '"' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        var sb = new StringBuilder("Type,UnitPrice,Currency,Note\n");
        foreach (var r in rows) sb.Append(Q(r.Type)).Append(',').Append(r.UnitPrice.ToString(CultureInfo.InvariantCulture)).Append(',').Append(Q(r.Currency)).Append(',').Append(Q(r.Note)).Append('\n');
        return sb.ToString();
    }
}
