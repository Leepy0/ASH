using System.Globalization;
using System.Net;
using System.Text;
using ASH.Core.Catalog;
using ASH.Server.Catalog;

namespace ASH.Server;

internal static partial class Routes
{
    /// <summary>Vehicle type catalog: read API (the AutoMod plugin fetches it, projected so hidden/updated* never leave the server) and the admin page.</summary>
    public static void MapCatalog(this WebApplication app)
    {
        app.MapGet("/api/catalog/types", (HttpContext ctx, CatalogStore store, AssetStore assets) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Json(new { types = store.Types().Select(t => new { name = t.Name, label = t.Label, category = t.Category, kind = t.Kind, defaults = t.Defaults, assets = t.Assets }) });
        });

        app.MapGet("/admin/types", (HttpContext ctx, ReleaseStore rs, AdminSessions sessions, CatalogStore store, AssetStore assets) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            if (!rs.UploadsEnabled || !(HeaderAdmin(ctx, rs) || CookieAdmin(ctx, sessions)))
                return Html(ServerPages.Login(rs.UploadsEnabled, null, "/admin/types", "타입·투자비(관리자)"), rs.UploadsEnabled ? 401 : 403);
            CatalogType? editing = null;
            string? msg = store.LoadError is { } le ? Bad($"types.json을 읽을 수 없습니다(저장은 막혀 있습니다): {WebUtility.HtmlEncode(le)}") : null;
            if (ctx.Request.Query["new"].ToString().Length > 0) editing = new CatalogType { Kind = "Standard", Defaults = { Capacity = 1 } }; // the detail pane shows an empty form
            else if (ctx.Request.Query["edit"].ToString() is { Length: > 0 } en) editing = store.Get(en);
            else if (ctx.Request.Query["clone"].ToString() is { Length: > 0 } cn && store.Get(cn) is { } src)
                editing = new CatalogType { Label = src.Label, Category = src.Category, Kind = src.Kind, Defaults = Copy(src.Defaults), Hidden = src.Hidden };
            return Html(Render(store, assets, editing, msg));
        });

        app.MapPost("/admin/types", async (HttpContext ctx, ReleaseStore rs, AdminSessions sessions, CatalogStore store, AssetStore assets) =>
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
            if (errs.Count > 0) return Html(Render(store, assets, t, Bad(string.Join("<br>", errs.Select(WebUtility.HtmlEncode)))), 400);

            var msg = Ok($"{WebUtility.HtmlEncode(t.Name)} 저장했습니다.");
            if (hasPrice)
            {
                var rows = store.Prices().Values.Where(p => !p.Type.Equals(t.Name, StringComparison.OrdinalIgnoreCase)).Append(new PriceRow(t.Name, price, store.Prices().TryGetValue(t.Name, out var o) ? o.Currency : "KRW", o?.Note ?? ""));
                var res = store.SavePrices(PricesCsv(rows));
                if (res.Errors.Count > 0) return Html(Render(store, assets, t, msg + Bad(PriceErrors(res))), 400);
            }
            var d = t.Defaults;
            if (d.SpeedLoadedCurve > d.SpeedLoadedStraight) msg += Warn("적재 곡선 속도가 직선 속도보다 큽니다.");
            if (d.SpeedEmptyCurve > (d.SpeedEmptyStraight > 0 ? d.SpeedEmptyStraight : d.SpeedLoadedStraight)) msg += Warn("공차 곡선 속도가 직선 속도보다 큽니다.");
            return Html(Render(store, assets, t, msg));
        });

        app.MapGet("/admin/types/template.xlsx", (HttpContext ctx, ReleaseStore rs, AdminSessions sessions, CatalogStore store) =>
        {
            if (!rs.UploadsEnabled || !(HeaderAdmin(ctx, rs) || CookieAdmin(ctx, sessions))) return Msg(rs.UploadsEnabled ? 401 : 403, "관리자 로그인이 필요합니다.");
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.File(CatalogSheet.Template(store.Types(includeHidden: true), store.Prices()),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ASH-types.xlsx");
        });

        // rows copied out of the template: all checked first, nothing saved when any row is wrong
        app.MapPost("/admin/types/paste", async (HttpContext ctx, ReleaseStore rs, AdminSessions sessions, CatalogStore store, AssetStore assets) =>
        {
            if (DenyPost(ctx, rs, sessions) is { } deny) return deny;
            var text = (await FormFrom(ctx))?["rows"].ToString();
            if (text == null) return Msg(400, "폼이 아닙니다.");
            if (store.LoadError != null) return Html(Render(store, assets, null, Bad("types.json을 읽을 수 없어 저장하지 않았습니다."), paste: text), 400);
            var (rows, errs) = CatalogSheet.Parse(text, store.Get);
            if (ctx.Request.Query["preview"] == "1") return Html(Render(store, assets, null, null, paste: text, preview: Preview(store, rows, errs))); // checked like a real paste, nothing saved
            if (errs.Count > 0) return Html(Render(store, assets, null, Bad("저장하지 않았습니다.<br>" + string.Join("<br>", errs.Select(WebUtility.HtmlEncode))), paste: text), 400);
            foreach (var r in rows)
                if (store.Save(r.Type, "admin") is { } why) return Html(Render(store, assets, null, Bad($"{WebUtility.HtmlEncode(r.Type.Name)}에서 멈췄습니다(앞 줄은 저장됨): {WebUtility.HtmlEncode(why)}"), paste: text), 500);
            var priced = rows.Where(r => r.Price != null).ToList();
            if (priced.Count > 0)
            {
                var old = store.Prices();
                var keep = old.Values.Where(p => !priced.Any(r => r.Type.Name.Equals(p.Type, StringComparison.OrdinalIgnoreCase)));
                var res = store.SavePrices(PricesCsv(keep.Concat(priced.Select(r => new PriceRow(r.Type.Name, r.Price!.Value, old.TryGetValue(r.Type.Name, out var o) ? o.Currency : "KRW", o?.Note ?? "")))));
                if (res.Errors.Count > 0) return Html(Render(store, assets, null, Bad(PriceErrors(res))), 400);
            }
            return Html(Render(store, assets, null, Ok($"타입 {rows.Count}개를 저장했습니다{(priced.Count > 0 ? $"(단가 {priced.Count}개 포함)" : "")}.")));
        });

        app.MapPost("/admin/types/prices", async (HttpContext ctx, ReleaseStore rs, AdminSessions sessions, CatalogStore store, AssetStore assets) =>
        {
            if (DenyPost(ctx, rs, sessions) is { } deny) return deny;
            var csv = (await FormFrom(ctx))?["csv"].ToString();
            if (csv == null) return Msg(400, "폼이 아닙니다.");
            var res = store.SavePrices(csv);
            if (res.Errors.Count > 0) return Html(Render(store, assets, null, Bad(PriceErrors(res)), csv), 400);
            return Html(Render(store, assets, null, Ok($"투자비 {res.Changed}행을 저장했습니다.")));
        });
    }

    static string Ok(string html) => $"<div class=\"msg ok\">{html}</div>";
    static string Bad(string html) => $"<div class=\"msg bad\">{html}</div>";
    static string Warn(string text) => $"<div class=\"msg warn\">{WebUtility.HtmlEncode(text)}</div>";
    static string PriceErrors(PriceSave r) => "투자비를 저장하지 않았습니다.<br>" + string.Join("<br>", r.Errors.Select(e => $"{e.Row}행: {WebUtility.HtmlEncode(e.Why)}"));

    static string Render(CatalogStore store, AssetStore assets, CatalogType? editing, string? msg, string? csvOverride = null, string? paste = null, CatalogPages.PastePreview? preview = null) =>
        CatalogPages.Types(store.Types(includeHidden: true), store.Prices(), csvOverride ?? store.PricesCsv(), editing, msg, assets, paste, preview);

    // 새로 = no such type yet; 동일 = every field and the price would stay; else 변경
    internal static CatalogPages.PastePreview Preview(CatalogStore store, IReadOnlyList<CatalogSheet.Row> rows, IReadOnlyList<string> errs)
    {
        static string Sig(CatalogType t, decimal? price) => string.Join("|", t.Label, t.Category, t.Kind, t.Hidden, price,
            string.Join(",", new[] { t.Defaults.SpeedLoadedStraight, t.Defaults.SpeedLoadedCurve, t.Defaults.SpeedEmptyStraight, t.Defaults.SpeedEmptyCurve, t.Defaults.Accel, t.Defaults.Decel,
                t.Defaults.Capacity, t.Defaults.LoadTime, t.Defaults.UnloadTime, t.Defaults.MTBF, t.Defaults.MTTR, t.Defaults.Length, t.Defaults.Width }.Select(v => v.ToString("R", CultureInfo.InvariantCulture))), t.Defaults.Color);
        var prices = store.Prices();
        var list = rows.Select(r =>
        {
            var old = store.Get(r.Type.Name);
            decimal? oldPrice = prices.TryGetValue(r.Type.Name, out var op) ? op.UnitPrice : null;
            var state = old == null ? "새로" : Sig(old, oldPrice) == Sig(r.Type, r.Price ?? oldPrice) ? "동일" : "변경";
            return new CatalogPages.PreviewRow(state, r.Type, r.Price, old, oldPrice);
        }).ToList();
        return new CatalogPages.PastePreview(list, errs);
    }

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
