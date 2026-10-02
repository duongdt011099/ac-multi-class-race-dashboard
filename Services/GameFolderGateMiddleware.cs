namespace MulticlassRace.Services;

/// <summary>
/// The pages that cannot show anything useful without the game folder, so they are not worth
/// rendering when there is none. Kept as one list next to the gate that redirects away from them,
/// because a page added here without being added to <see cref="GameFolderGateMiddleware"/> would be
/// reachable and empty.
/// </summary>
public static class GameFolderProtectedPaths
{
    private static readonly string[] Paths = ["/", "/teams", "/drivers", "/races", "/pointsettings"];

    public static bool Contains(string? path) => path is not null && Paths.Contains(path, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Sends the user to the one page that can explain a missing game folder instead of rendering tables
/// with no cars, no tracks and no way to start a session. The game config page is deliberately not
/// protected: it is where the answer is explained and corrected.
/// </summary>
public sealed class GameFolderGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AssettoCorsaPathResolver pathResolver)
    {
        if (GameFolderProtectedPaths.Contains(context.Request.Path.Value))
        {
            var location = await pathResolver.GetAsync();

            if (location.IsConfigured is false)
            {
                context.Response.Redirect("/gameconfig");
                return;
            }
        }

        await next(context);
    }
}

public static class GameFolderGateMiddlewareExtensions
{
    public static IApplicationBuilder UseGameFolderGate(this IApplicationBuilder app)
    {
        return app.UseMiddleware<GameFolderGateMiddleware>();
    }
}
