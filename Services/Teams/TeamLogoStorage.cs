namespace MulticlassRace.Services.Teams;

public class TeamLogoStorage : ITeamLogoStorage
{
    public const long MaxLogoSizeBytes = 2 * 1024 * 1024;

    private const string FolderName = "team-logos";

    private static readonly string[] AllowedContentTypes =
        { "image/png", "image/jpeg", "image/jpg", "image/gif", "image/webp", "image/svg+xml" };

    private readonly string _uploadsRoot;

    public TeamLogoStorage(string uploadsRoot)
    {
        _uploadsRoot = uploadsRoot;
    }

    public async Task<string> SaveAsync(
        Guid teamId,
        string originalFileName,
        string contentType,
        long length,
        Func<Stream> openRead)
    {
        if (length > MaxLogoSizeBytes)
        {
            throw new InvalidLogoException("Logo image must be 2MB or smaller.");
        }

        if (!AllowedContentTypes.Contains(contentType))
        {
            throw new InvalidLogoException("Logo must be a PNG, JPG, GIF, WEBP, or SVG image.");
        }

        var directory = Path.Combine(_uploadsRoot, FolderName);
        Directory.CreateDirectory(directory);

        foreach (var stale in Directory.GetFiles(directory, $"{teamId}.*"))
        {
            File.Delete(stale);
        }

        var fileName = $"{teamId}{ResolveExtension(originalFileName, contentType)}";

        await using (var target = File.Create(Path.Combine(directory, fileName)))
        {
            await openRead().CopyToAsync(target);
        }

        return $"/uploads/{FolderName}/{fileName}";
    }

    /// <summary>Keeps only letters and digits of the original extension so a name can never escape the folder.</summary>
    internal static string ResolveExtension(string originalFileName, string contentType)
    {
        var extension = Path.GetExtension(originalFileName);

        if (string.IsNullOrEmpty(extension))
        {
            return contentType switch
            {
                "image/png" => ".png",
                "image/webp" => ".webp",
                "image/gif" => ".gif",
                "image/svg+xml" => ".svg",
                _ => ".jpg"
            };
        }

        return "." + new string(extension.Skip(1).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}
