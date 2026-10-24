namespace ASH.Server;

internal static partial class Routes
{
    const int FsMax = 2000;

    /// <summary>
    /// GET /admin/fs?path=&amp;files=1 — the server's drives and folders for the settings page's "찾아보기" dialog (a browser cannot open the server's Explorer).
    /// Admin only (same check as /admin/settings; under /admin because the session cookie is Path=/admin). Lists names only, never file contents; an empty path lists the drives.
    /// </summary>
    public static void MapAdminFs(this WebApplication app)
    {
        app.MapGet("/admin/fs", (HttpContext ctx, ReleaseStore rs, AdminSessions sessions, string? path, string? files) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            if (!rs.UploadsEnabled || !(HeaderAdmin(ctx, rs) || CookieAdmin(ctx, sessions))) return Msg(rs.UploadsEnabled ? 401 : 403, "관리자 로그인이 필요합니다.");
            var withFiles = files == "1";
            path = (path ?? "").Trim();
            if (path.Length == 0)
                return Results.Json(new FsListing("", null, DriveInfo.GetDrives().Select(d => new FsEntry(d.Name, d.Name)).ToList(), [], false, null));
            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return Msg(400, "경로가 올바르지 않습니다."); }
            if (!Path.IsPathRooted(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return Msg(400, "드라이브부터 시작하는 전체 경로여야 합니다.");
            if (File.Exists(full)) full = Path.GetDirectoryName(full) ?? full; // a file path (current setting value) opens its folder
            if (!Directory.Exists(full)) return Msg(404, "그런 폴더가 없습니다.");
            var parent = Directory.GetParent(full)?.FullName ?? ""; // "" = the drive list
            var opt = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false };
            try
            {
                var di = new DirectoryInfo(full);
                var dirs = di.EnumerateDirectories("*", opt).Select(d => new FsEntry(d.Name, d.FullName)).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Take(FsMax + 1).ToList();
                var fl = withFiles ? di.EnumerateFiles("*", opt).Select(f => new FsEntry(f.Name, f.FullName)).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Take(FsMax + 1).ToList() : [];
                bool cut = dirs.Count > FsMax || fl.Count > FsMax;
                return Results.Json(new FsListing(full, parent, dirs.Take(FsMax).ToList(), fl.Take(FsMax).ToList(), cut, null));
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                return Results.Json(new FsListing(full, parent, [], [], false, "이 폴더는 열 수 없습니다(권한 없음)."));
            }
        });
    }

    internal sealed record FsEntry(string Name, string Path);
    internal sealed record FsListing(string Path, string? Parent, List<FsEntry> Dirs, List<FsEntry> Files, bool Truncated, string? Error);
}
