namespace ASH.Server;

internal static partial class Routes
{
    /// <summary>The films list (/films) and its poll (/api/films). One film, its files and cancel stay with the analysis routes.</summary>
    public static void MapFilms(this WebApplication app)
    {
        app.MapGet("/films", (HttpContext ctx, string? me, JobStore js, FilmStore fs) =>
        {
            var films = FilmPages.Filter(fs.All(), js.All(), ClientIp(ctx), Me(me), out var onlyMine);
            var live = FilmPages.LiveOf(films);
            return Html(FilmPages.List(films, onlyMine, (id, n) => File.Exists(Path.Combine(fs.FilmDir(id), n)),
                live.Active ? JobPages.PollScript("/api/films?me=" + (onlyMine ? 1 : 0), live.Watch) : null));
        });

        app.MapGet("/api/films", (HttpContext ctx, string? me, JobStore js, FilmStore fs) =>
        {
            var live = FilmPages.LiveOf(FilmPages.Filter(fs.All(), js.All(), ClientIp(ctx), Me(me), out _));
            return Results.Json(new { active = live.Active, fields = live.Fields, watch = live.Watch });
        });

        static bool? Me(string? me) => me switch { "1" => true, "0" => false, _ => null };
    }
}
