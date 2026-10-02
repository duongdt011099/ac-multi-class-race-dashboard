using Microsoft.Extensions.FileProviders;

namespace MulticlassRace.Services;

/// <summary>
/// User uploads live in their own folder outside wwwroot so they are served as plain files from one
/// place instead of being compiled into the app. Created on demand: an install that has never been
/// asked to take a file would otherwise fail on the first upload rather than when it is set up.
/// </summary>
public static class UploadsEndpointExtensions
{
    private const string RequestPath = "/uploads";

    public static IApplicationBuilder UseUploads(this IApplicationBuilder app, IWebHostEnvironment environment)
    {
        var uploadsRoot = ResolveRoot(environment);
        Directory.CreateDirectory(uploadsRoot);

        return app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(uploadsRoot),
            RequestPath = RequestPath
        });
    }

    private static string ResolveRoot(IWebHostEnvironment environment)
    {
        var webRoot = environment.WebRootPath;

        return string.IsNullOrWhiteSpace(webRoot)
            ? Path.Combine(environment.ContentRootPath, "wwwroot", "uploads")
            : Path.Combine(webRoot, "uploads");
    }
}
