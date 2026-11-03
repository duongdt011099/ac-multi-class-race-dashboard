namespace MulticlassRace.Services.Teams;

public class TeamLogoStorage : ITeamLogoStorage
{
    public const long MaxLogoSizeBytes = 2 * 1024 * 1024;

    private const string FolderName = "team-logos";

    private static readonly string[] AllowedContentTypes =
        { "image/png", "image/jpeg", "image/jpg", "image/gif", "image/webp", "image/svg+xml" };

    private static readonly HashSet<string> AllowedExtensions =
        new() { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg" };

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

        return Versioned(fileName);
    }

    // A team's file keeps the same name when its picture changes, so the address gets a version to make
    // the browser (and Blazor, which skips unchanged attributes) pick up the new picture right away.
    private static string Versioned(string fileName) =>
        $"/uploads/{FolderName}/{fileName}?v={DateTime.UtcNow.Ticks}";

    public Task<IReadOnlyList<string>> ListUploadedAsync()
    {
        var directory = Path.Combine(_uploadsRoot, FolderName);

        if (!Directory.Exists(directory))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        var seenContent = new HashSet<string>();
        var logos = new List<string>();

        foreach (var file in new DirectoryInfo(directory).GetFiles().OrderByDescending(f => f.LastWriteTimeUtc))
        {
            if (!AllowedExtensions.Contains(file.Extension.ToLowerInvariant()))
            {
                continue;
            }

            string hash;

            try
            {
                using var stream = file.OpenRead();
                hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
            }
            catch (IOException)
            {
                continue;
            }

            if (seenContent.Add(hash))
            {
                logos.Add($"/uploads/{FolderName}/{file.Name}");
            }
        }

        return Task.FromResult<IReadOnlyList<string>>(logos);
    }

    public Task<string> UseUploadedAsync(Guid teamId, string logoUrl)
    {
        var directory = Path.Combine(_uploadsRoot, FolderName);

        // Only the file name is honoured, so a crafted URL can never point outside the logo folder.
        var name = Path.GetFileName((logoUrl ?? string.Empty).Split('?')[0]);
        var source = Path.Combine(directory, name);

        if (name.Length == 0 || !AllowedExtensions.Contains(Path.GetExtension(name).ToLowerInvariant()) || !File.Exists(source))
        {
            throw new InvalidLogoException("That logo is no longer available.");
        }

        var target = $"{teamId}{Path.GetExtension(name).ToLowerInvariant()}";

        // Picking the team's own logo changes nothing.
        if (string.Equals(name, target, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Versioned(name));
        }

        foreach (var stale in Directory.GetFiles(directory, $"{teamId}.*"))
        {
            File.Delete(stale);
        }

        var copy = Path.Combine(directory, target);
        File.Copy(source, copy);

        // A copy keeps the original's timestamp, which would let a stale cached picture look current.
        File.SetLastWriteTimeUtc(copy, DateTime.UtcNow);

        return Task.FromResult(Versioned(target));
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
