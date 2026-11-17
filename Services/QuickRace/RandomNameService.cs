using System.Text.Json;

namespace MulticlassRace.Services.QuickRace;

/// <summary>Makes up driver names for custom drivers.</summary>
public interface IRandomNameService
{
    /// <summary>A "First Last" name that is not in <paramref name="taken"/>; the name is not added to the set.</summary>
    string Generate(ISet<string>? taken = null);
}

/// <summary>
/// Combines a common American first name with a common American last name. The names live in
/// <c>wwwroot/data/american-names.json</c> so they can be edited without a rebuild; a short built-in
/// list is used when that file is missing or unreadable, so a custom driver can always be named.
/// </summary>
public sealed class RandomNameService : IRandomNameService
{
    public const string DataFile = "data/american-names.json";

    private static readonly string[] FallbackFirstNames =
    {
        "James", "John", "Robert", "Michael", "William", "David", "Mary", "Patricia", "Jennifer", "Linda"
    };

    private static readonly string[] FallbackLastNames =
    {
        "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis", "Wilson", "Taylor"
    };

    private readonly Lazy<(string[] First, string[] Last)> _names;

    public RandomNameService(IWebHostEnvironment environment, ILogger<RandomNameService> logger)
    {
        _names = new Lazy<(string[], string[])>(() => Load(environment, logger));
    }

    internal RandomNameService(string[] firstNames, string[] lastNames)
    {
        _names = new Lazy<(string[], string[])>(() => (firstNames, lastNames));
    }

    public string Generate(ISet<string>? taken = null)
    {
        var (first, last) = _names.Value;
        var random = Random.Shared;

        // The pool is thousands of combinations, so a clash is rare; after a few tries just accept one.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var name = $"{first[random.Next(first.Length)]} {last[random.Next(last.Length)]}";

            if (taken is null || !taken.Contains(name))
            {
                return name;
            }
        }

        return $"{first[random.Next(first.Length)]} {last[random.Next(last.Length)]}";
    }

    private static (string[] First, string[] Last) Load(IWebHostEnvironment environment, ILogger logger)
    {
        try
        {
            var file = environment.WebRootFileProvider.GetFileInfo(DataFile);

            if (file.Exists)
            {
                using var stream = file.CreateReadStream();
                var data = JsonSerializer.Deserialize<NameData>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var first = Clean(data?.FirstNames);
                var last = Clean(data?.LastNames);

                if (first.Length > 0 && last.Length > 0)
                {
                    return (first, last);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read {File}; using the built-in names.", DataFile);
        }

        return (FallbackFirstNames, FallbackLastNames);
    }

    private static string[] Clean(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed class NameData
    {
        public List<string>? FirstNames { get; set; }

        public List<string>? LastNames { get; set; }
    }
}
