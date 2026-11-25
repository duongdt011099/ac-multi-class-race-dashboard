using System.Text.Json;

namespace MulticlassRace.Services.Browser;

/// <summary>
/// Remembers small UI preferences (tour answered, last selected championship, ...) in a JSON file
/// under %LocalAppData%. The browser's own local storage cannot be used: Kestrel listens on a port the
/// system picks at every launch, so the page's origin, and with it local storage, changes each time.
/// Every call is best effort: if the file cannot be read or written, a preference is simply not remembered.
/// </summary>
public class BrowserStorage
{
    private readonly string _filePath;
    private readonly object _gate = new();
    private Dictionary<string, string>? _values;

    public BrowserStorage(string filePath)
    {
        _filePath = filePath;
    }

    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EnduranceRaceDashboard",
        "preferences.json");

    public Task<string?> GetAsync(string key)
    {
        lock (_gate)
        {
            return Task.FromResult(Load().TryGetValue(key, out var value) ? value : null);
        }
    }

    public Task SetAsync(string key, string value)
    {
        lock (_gate)
        {
            Load()[key] = value;
            Save();
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        lock (_gate)
        {
            if (Load().Remove(key))
            {
                Save();
            }
        }

        return Task.CompletedTask;
    }

    private Dictionary<string, string> Load()
    {
        if (_values is not null)
        {
            return _values;
        }

        try
        {
            if (File.Exists(_filePath))
            {
                _values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_filePath));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A missing or broken file just means nothing is remembered yet.
        }

        return _values ??= new Dictionary<string, string>();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_values));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
