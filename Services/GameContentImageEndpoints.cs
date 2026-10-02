namespace MulticlassRace.Services;

/// <summary>
/// Serves car liveries and track images straight out of the game folder.
///
/// Both routes take folder and file names from the query string, so they are written the same way on
/// purpose: reject anything containing a path separator, resolve to a full path, and then confirm the
/// result is still inside the expected root before the file is handed over. Without that last check
/// ".." would walk out of the game folder and serve any file the process can read.
/// </summary>
public static class GameContentImageEndpoints
{
    public static IEndpointRouteBuilder MapGameContentImageEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/car-image", async (
            string? car,
            string? skin,
            string? type,
            AssettoCorsaPathResolver pathResolver) =>
        {
            var fileName = type switch
            {
                "livery" => "livery.png",
                "car" => "preview.jpg",
                _ => null
            };

            if (fileName is null || !IsSafeFolderName(car) || !IsSafeFolderName(skin))
            {
                return Results.NotFound();
            }

            var carsRoot = await ResolveGameFolderAsync(pathResolver, "cars");

            if (carsRoot is null)
            {
                return Results.NotFound();
            }

            if (!TryResolveInside(carsRoot, [car!.Trim(), "skins", skin!.Trim(), fileName], out var fullPath))
            {
                return Results.NotFound();
            }

            return Results.File(fullPath, type == "livery" ? "image/png" : "image/jpeg");
        });

        app.MapGet("/track-image", async (
            string? track,
            string? layout,
            string? type,
            AssettoCorsaPathResolver pathResolver) =>
        {
            var fileName = type switch
            {
                "outline" => "outline.png",
                "preview" => "preview.png",
                _ => null
            };

            if (fileName is null || !IsSafeFolderName(track) || ContainsPathSeparator(layout))
            {
                return Results.NotFound();
            }

            var tracksRoot = await ResolveGameFolderAsync(pathResolver, "tracks");

            if (tracksRoot is null)
            {
                return Results.NotFound();
            }

            string[] segments = string.IsNullOrWhiteSpace(layout)
                ? [track!.Trim(), "ui", fileName]
                : [track!.Trim(), "ui", layout!.Trim(), fileName];

            if (!TryResolveInside(tracksRoot, segments, out var fullPath))
            {
                return Results.NotFound();
            }

            return Results.File(fullPath, "image/png");
        });

        return app;
    }

    /// <summary>
    /// The game folder's content subfolder, or null when there is no usable game folder. A missing
    /// game is a 404 rather than an exception: these are images on an otherwise working page.
    /// </summary>
    private static async Task<string?> ResolveGameFolderAsync(AssettoCorsaPathResolver pathResolver, string contentFolder)
    {
        var gamePath = (await pathResolver.GetAsync()).Path;

        if (string.IsNullOrWhiteSpace(gamePath))
        {
            return null;
        }

        return Path.GetFullPath(Path.Combine(gamePath, "content", contentFolder));
    }

    private static bool TryResolveInside(string root, IEnumerable<string> segments, out string fullPath)
    {
        fullPath = Path.GetFullPath(Path.Combine(new[] { root }.Concat(segments).ToArray()));

        var insideRoot = fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        return insideRoot && File.Exists(fullPath);
    }

    /// <summary>A folder name that is present and cannot climb out of its parent.</summary>
    private static bool IsSafeFolderName(string? value)
    {
        return string.IsNullOrWhiteSpace(value) is false && ContainsPathSeparator(value) is false;
    }

    private static bool ContainsPathSeparator(string? value)
    {
        return value?.Contains('\\') == true || value?.Contains('/') == true;
    }
}
