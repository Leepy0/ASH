using System.Globalization;
using ASH.Core.Cel;
using ASH.Server.Catalog;
using Microsoft.AspNetCore.Http.Features;

namespace ASH.Server;

internal static partial class Routes
{
    internal const double AssetMaxMbDefault = 50;

    /// <summary>Asset API (public, the plugin reads it) and the admin POSTs behind the 에셋 section of /admin/types. CatalogType.Assets holds asset IDs.</summary>
    public static void MapAssets(this WebApplication app)
    {
        app.MapGet("/api/catalog/assets", (HttpContext ctx, AssetStore st) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            var t = ctx.Request.Query["type"].ToString();
            var list = t.Length == 0 ? st.All() : st.ForType(t);
            return Results.Json(new { assets = list.Select(a => new { a.Id, a.TypeName, a.Name, a.Note, a.ConveyorDir, boxes = a.Boxes, a.Source, a.LengthM, a.WidthM, a.HeightM, a.CreatedUtc }) },
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        });

        IResult File_(AssetStore st, string id, string file, string mime)
        {
            if (st.Find(id) == null) return Msg(404, "에셋이 없습니다.");
            var p = st.Path(id, file);
            return File.Exists(p) ? Results.File(p, mime) : Msg(404, "에셋 파일이 없습니다.");
        }
        app.MapGet("/api/catalog/assets/{id}/asset.cel", (string id, AssetStore st) => File_(st, id, "asset.cel", "text/plain; charset=utf-8"));
        app.MapGet("/api/catalog/assets/{id}/preview.svg", (string id, AssetStore st) => File_(st, id, "preview.svg", "image/svg+xml"));

        app.MapPost("/admin/assets", async (HttpContext ctx, ReleaseStore rs, AdminSessions sessions, AssetStore st, CatalogStore types, IConfiguration cfg) =>
        {
            if (DenyPost(ctx, rs, sessions) is { } deny) return deny;
            var limit = (long)(cfg.GetValue("Catalog:AssetMaxMb", AssetMaxMbDefault) * 1024 * 1024);
            if (ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } lim) lim.MaxRequestBodySize = limit + 256 * 1024; // form fields on top of the file
            if (!ctx.Request.HasFormContentType) return Msg(400, "폼이 아닙니다.");
            IFormCollection f;
            try { f = await ctx.Request.ReadFormAsync(); }
            catch (BadHttpRequestException e) { return Msg(e.StatusCode, e.StatusCode == 413 ? $"파일이 너무 큽니다(최대 {limit / 1024 / 1024} MB)." : "폼을 읽을 수 없습니다."); }
            var file = f.Files["file"];
            if (file == null || file.Length == 0) return Msg(400, "STL 또는 OBJ 파일을 고르세요.");
            if (file.Length > limit) return Msg(413, $"파일이 너무 큽니다(최대 {limit / 1024 / 1024} MB).");
            var type = f["type"].ToString().Trim();
            if (types.Get(type) == null) return Msg(400, $"타입 '{type}'이(가) 없습니다.");
            var (name, note, dir, opts, err) = AssetForm(f);
            if (err != null) return Msg(400, err);
            var tmp = System.IO.Path.GetTempFileName();
            try
            {
                await using (var fs = File.Create(tmp)) await file.CopyToAsync(fs);
                var a = st.Create(type, name, note, dir, tmp, file.FileName, opts, "admin");
                if (!types.AddAsset(type, a.Id)) { st.Delete(a.Id); return Msg(500, "타입에 에셋을 연결하지 못해 취소했습니다."); }
                return SeeOther($"/admin/types#asset-{a.Id}");
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or InvalidDataException or FormatException)
            {
                return Msg(400, e.Message);
            }
            finally { try { File.Delete(tmp); } catch (IOException) { } }
        });

        app.MapPost("/admin/assets/{id}/reconvert", async (string id, HttpContext ctx, ReleaseStore rs, AdminSessions sessions, AssetStore st) =>
        {
            if (DenyPost(ctx, rs, sessions) is { } deny) return deny;
            if (st.Find(id) == null) return Msg(404, "에셋이 없습니다.");
            var f = await FormFrom(ctx);
            if (f == null) return Msg(400, "폼이 아닙니다.");
            var (name, note, dir, opts, err) = AssetForm(f);
            if (err != null) return Msg(400, err);
            try { st.Reconvert(id, name, note, dir, opts, "admin"); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or InvalidDataException or FormatException) { return Msg(400, e.Message); }
            return SeeOther($"/admin/types#asset-{id}");
        });

        app.MapPost("/admin/assets/{id}/delete", (string id, HttpContext ctx, ReleaseStore rs, AdminSessions sessions, AssetStore st, CatalogStore types) =>
        {
            if (DenyPost(ctx, rs, sessions) is { } deny) return deny;
            var a = st.Find(id);
            if (a == null) return Msg(404, "에셋이 없습니다.");
            types.RemoveAsset(a.TypeName, id);
            st.Delete(id);
            return SeeOther("/admin/types");
        });
    }

    static IResult SeeOther(string url) { var r = Results.StatusCode(303); return new LocationResult(r, url); }

    sealed class LocationResult(IResult inner, string url) : IResult
    {
        public Task ExecuteAsync(HttpContext ctx) { ctx.Response.Headers.Location = url; return inner.ExecuteAsync(ctx); }
    }

    // name, note, conveyor dir and conversion options from a form; Err is a Korean message when something does not parse
    static (string Name, string Note, int Dir, SimplifyOptions Opts, string? Err) AssetForm(IFormCollection f)
    {
        string name = f["name"].ToString().Trim(), note = f["note"].ToString().Trim();
        if (note.Length > NoteMax) return (name, note, 0, new SimplifyOptions(), $"메모는 {NoteMax}자까지입니다.");
        string? err = null;
        double D(string k, double def) { var s = f[k].ToString().Trim(); if (s.Length == 0) return def; if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v)) return v; err ??= $"'{k}' 값 '{s}'은(는) 숫자가 아닙니다."; return def; }
        int I(string k, int def) { var v = D(k, def); if (v != Math.Floor(v) || Math.Abs(v) > int.MaxValue) { err ??= $"'{k}'은(는) 정수여야 합니다."; return def; } return (int)v; }
        T E<T>(string k, T def) where T : struct, Enum { var s = f[k].ToString().Trim(); if (s.Length == 0) return def; if (Enum.TryParse<T>(s, true, out var v) && Enum.IsDefined(v)) return v; err ??= $"'{k}' 값 '{s}'을(를) 알 수 없습니다."; return def; }
        var unit = f["sourceUnit"].ToString().Trim().ToLowerInvariant() switch { "" or "mm" => 0.001, "m" => 1.0, "in" or "inch" => 0.0254, var s => D("sourceUnit", 0.001) };
        var o = new SimplifyOptions(D("minPartPct", 2), I("maxBoxes", 200), E("heading", Heading.PlusX), E("origin", OriginAt.Center), I("rotateDeg", 0), unit, I("grid", 1));
        if (err == null && (o.MaxBoxes < 1 || o.MinPartPct < 0 || o.Grid < 1 || o.Grid > 50 || o.RotateDeg is not (0 or 90 or 180 or 270) || !(unit > 0))) err = "변환 옵션 범위가 올바르지 않습니다(상자 수 1 이상, 격자 1~50, 회전 0/90/180/270).";
        return (name, note, I("conveyorDir", 0), o, err);
    }
}
