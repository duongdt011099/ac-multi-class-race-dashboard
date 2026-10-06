using System.Text.Json;
using System.Text.RegularExpressions;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.Services.Abstractions;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services;

public class TrackService : ITrackService
{
    private readonly IAssettoCorsaGameConfigRepository _configRepository;
    private readonly AssettoCorsaPathResolver _pathResolver;

    public TrackService(IAssettoCorsaGameConfigRepository configRepository, AssettoCorsaPathResolver pathResolver)
    {
        _configRepository = configRepository;
        _pathResolver = pathResolver;
    }

    public async Task<IReadOnlyList<string>> GetTrackNamesAsync()
    {
        var tracks = await GetTrackOptionsAsync();

        return tracks.Select(t => t.TrackId).ToList();
    }

    public async Task<IReadOnlyList<TrackOptionModel>> GetTrackOptionsAsync()
    {
        var tracksRoot = await GetTracksRootAsync();

        if (tracksRoot is null || !Directory.Exists(tracksRoot))
        {
            return Array.Empty<TrackOptionModel>();
        }

        var options = new List<TrackOptionModel>();

        foreach (var directory in Directory.GetDirectories(tracksRoot))
        {
            var trackId = Path.GetFileName(directory);

            if (string.IsNullOrWhiteSpace(trackId))
            {
                continue;
            }

            options.Add(new TrackOptionModel(trackId, await ResolveDisplayNameAsync(directory, trackId)));
        }

        return Disambiguate(options)
            .OrderBy(o => o.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.TrackId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Two content packs can name different folders the same thing (rt_misano and "Misano 2025"
    /// are both "Misano World Circuit"), and a dropdown that shows the same label twice is
    /// worse than one that shows the folder id, so duplicates fall back to their ids.
    /// </summary>
    private static List<TrackOptionModel> Disambiguate(List<TrackOptionModel> options)
    {
        return options
            .Select(o => options.Count(other => string.Equals(
                    other.DisplayName,
                    o.DisplayName,
                    StringComparison.OrdinalIgnoreCase)) > 1
                ? o with { DisplayName = o.TrackId }
                : o)
            .ToList();
    }

    public async Task<string> GetTrackDisplayNameAsync(string? trackName)
    {
        if (string.IsNullOrWhiteSpace(trackName))
        {
            return trackName ?? string.Empty;
        }

        var requested = trackName.Trim();
        var tracksRoot = await GetTracksRootAsync();

        if (tracksRoot is null || !Directory.Exists(tracksRoot))
        {
            return requested;
        }

        var trackDirectories = Directory.GetDirectories(tracksRoot);
        var directory = trackDirectories.FirstOrDefault(d =>
            string.Equals(Path.GetFileName(d), requested, StringComparison.OrdinalIgnoreCase));

        if (directory is null)
        {
            // The caller already has AC's own name for the track, so show that as-is.
            return requested;
        }

        var trackId = Path.GetFileName(directory);

        if (string.Equals(trackId, requested, StringComparison.Ordinal))
        {
            // Came from the dropdown, so it can be resolved to the shared label.
            var options = await GetTrackOptionsAsync();
            var match = options.FirstOrDefault(o => string.Equals(o.TrackId, requested, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                return match.DisplayName;
            }
        }

        return await ResolveDisplayNameAsync(directory, trackId);
    }

    /// <summary>
    /// Works out what to call a track. Most tracks name themselves in ui/ui_track.json, but
    /// plenty (rt_sebring, fn_spa, ks_monza66) only name their layouts, in which case the
    /// shared prefix of those names is the track: "Sebring International Raceway (Raceday)"
    /// and "Sebring International Raceway (Trackday)" both start with "Sebring International
    /// Raceway". Tracks whose layout names share nothing fall back to the shortest one.
    /// </summary>
    private static async Task<string> ResolveDisplayNameAsync(string directory, string fallback)
    {
        var uiDirectory = Path.Combine(directory, "ui");
        var rootName = await ReadNameAsync(uiDirectory);

        if (rootName is not null)
        {
            return rootName;
        }

        var layoutNames = new List<string>();

        if (Directory.Exists(uiDirectory))
        {
            foreach (var layoutDirectory in Directory.GetDirectories(uiDirectory))
            {
                var name = await ReadNameAsync(layoutDirectory);

                if (name is not null)
                {
                    layoutNames.Add(name);
                }
            }
        }

        if (layoutNames.Count == 0)
        {
            return fallback;
        }

        var prefix = layoutNames[0];

        foreach (var name in layoutNames.Skip(1))
        {
            var shared = 0;
            var limit = Math.Min(prefix.Length, name.Length);

            while (shared < limit && char.ToUpperInvariant(prefix[shared]) == char.ToUpperInvariant(name[shared]))
            {
                shared++;
            }

            prefix = prefix[..shared];
        }

        var trimmed = prefix.TrimEnd(' ', '-', ':', ',', '(', '/');

        if (trimmed.Length >= 3)
        {
            return trimmed;
        }

        return layoutNames
            .OrderBy(n => n.Length)
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    /// <summary>Reads the display name from a track or layout UI folder, if it declares one.</summary>
    private static async Task<string?> ReadNameAsync(string uiDirectory)
    {
        foreach (var fileName in new[] { "ui_track.json", "dlc_ui_track.json" })
        {
            var path = Path.Combine(uiDirectory, fileName);

            if (File.Exists(path) is false)
            {
                continue;
            }

            try
            {
                await using var stream = File.OpenRead(path);
                using var document = await JsonDocument.ParseAsync(stream);

                if (document.RootElement.TryGetProperty("name", out var displayName))
                {
                    var name = displayName.GetString();

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name.Trim();
                    }
                }
            }
            catch
            {
                // An invalid optional UI metadata file should not hide the track.
            }
        }

        return null;
    }

    public async Task<string?> ResolveTrackNameAsync(string? trackName)
    {
        if (string.IsNullOrWhiteSpace(trackName))
        {
            return null;
        }

        var requested = trackName.Trim();
        var tracksRoot = await GetTracksRootAsync();
        if (tracksRoot is null || !Directory.Exists(tracksRoot))
        {
            return requested;
        }

        var trackDirectories = Directory.GetDirectories(tracksRoot);
        var directMatch = trackDirectories.FirstOrDefault(directory =>
            string.Equals(Path.GetFileName(directory), requested, StringComparison.OrdinalIgnoreCase));
        if (directMatch is not null)
        {
            return Path.GetFileName(directMatch);
        }

        foreach (var directory in trackDirectories)
        {
            var uiDirectory = Path.Combine(directory, "ui");

            // Tracks that only name their layouts (rt_sebring has no ui/ui_track.json) report
            // AC's layout name in results, so those have to be searched too.
            if (Directory.Exists(uiDirectory) is false)
            {
                continue;
            }

            var uiDirectories = new List<string> { uiDirectory };
            uiDirectories.AddRange(Directory.GetDirectories(uiDirectory));

            foreach (var candidate in uiDirectories)
            {
                var name = await ReadNameAsync(candidate);

                if (name is not null && string.Equals(name, requested, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFileName(directory);
                }
            }
        }

        return requested;
    }

    public async Task<IReadOnlyList<string>> GetTrackLayoutsAsync(string trackName)
    {
        return (await GetTrackLayoutOptionsAsync(trackName)).Select(o => o.LayoutId).ToList();
    }

    public async Task<IReadOnlyList<TrackLayoutOptionModel>> GetTrackLayoutOptionsAsync(string trackName)
    {
        if (IsValidSegment(trackName) is false)
        {
            return Array.Empty<TrackLayoutOptionModel>();
        }

        var tracksRoot = await GetTracksRootAsync();

        if (tracksRoot is null)
        {
            return Array.Empty<TrackLayoutOptionModel>();
        }

        var uiDir = Path.Combine(tracksRoot, trackName, "ui");

        if (!Directory.Exists(uiDir))
        {
            return Array.Empty<TrackLayoutOptionModel>();
        }

        var options = new List<TrackLayoutOptionModel>();

        var layoutDirectories = Directory.GetDirectories(uiDir);
        var defaultLayoutName = layoutDirectories.Length > 0
            ? await ReadNameAsync(uiDir)
            : null;

        // AC stores a track's default configuration in ui/ui_track.json and extra layouts in
        // ui/<layout>/ui_track.json. When both exist, the default is a real selectable layout too;
        // otherwise tracks such as lilski_road_america expose only their Moto variant.
        if (!string.IsNullOrWhiteSpace(defaultLayoutName))
        {
            options.Add(new TrackLayoutOptionModel(string.Empty, $"{defaultLayoutName} (Default)"));
        }

        foreach (var directory in layoutDirectories)
        {
            var layoutId = Path.GetFileName(directory);

            if (string.IsNullOrWhiteSpace(layoutId))
            {
                continue;
            }

            var layoutUiDir = Path.Combine(uiDir, layoutId);
            var layoutName = await ReadNameAsync(layoutUiDir) ?? layoutId;

            options.Add(new TrackLayoutOptionModel(layoutId, layoutName));
        }

        return options
            .OrderBy(o => o.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.LayoutId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<int?> GetPitCountAsync(string trackName, string? layout)
    {
        if (IsValidSegment(trackName) is false ||
            (layout is not null && IsValidSegment(layout) is false))
        {
            return null;
        }

        var tracksRoot = await GetTracksRootAsync();

        if (tracksRoot is null)
        {
            return null;
        }

        var uiDir = Path.Combine(
            tracksRoot,
            trackName,
            "ui",
            string.IsNullOrWhiteSpace(layout) ? string.Empty : layout);

        foreach (var fileName in new[] { "ui_track.json", "dlc_ui_track.json" })
        {
            var jsonPath = Path.Combine(uiDir, fileName);

            if (File.Exists(jsonPath) is false)
            {
                continue;
            }

            JsonDocument document;

            try
            {
                await using var stream = File.OpenRead(jsonPath);
                document = await JsonDocument.ParseAsync(stream);
            }
            catch
            {
                continue;
            }

            using (document)
            {
                if (document.RootElement.TryGetProperty("pitboxes", out var pitboxes) &&
                    int.TryParse(pitboxes.ToString(), out var count) &&
                    count > 0)
                {
                    return count;
                }
            }
        }

        return null;
    }

    public async Task<int?> GetBestLapTimeAsync(string trackName, string? layout)
    {
        if (IsValidSegment(trackName) is false || (layout is not null && IsValidSegment(layout) is false))
        {
            return null;
        }

        var config = await _configRepository.GetAsync();
        var resultsPath = ContentManagerPaths.ResolveResults(config?.RaceResultsPath);

        if (!Directory.Exists(resultsPath))
        {
            return null;
        }

        long? best = null;

        foreach (var file in Directory.GetFiles(resultsPath, "*.json"))
        {
            JsonDocument document;

            try
            {
                await using var stream = File.OpenRead(file);
                document = await JsonDocument.ParseAsync(stream);
            }
            catch
            {
                continue;
            }

            using (document)
            {
                var root = document.RootElement;

                if (!root.TryGetProperty("track", out var track) ||
                    !string.Equals(track.GetString(), trackName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(layout))
                {
                    var fileLayout = GetConfigTrack(root);

                    if (fileLayout is not null &&
                        !string.Equals(fileLayout, layout, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                var fileBest = GetBestTime(root);

                if (fileBest > 0)
                {
                    best = Math.Min(best ?? long.MaxValue, fileBest);
                }
            }
        }

        return best is > 0 ? (int?)best : null;
    }

    private async Task<string?> GetTracksRootAsync()
    {
        var gamePath = (await _pathResolver.GetAsync()).Path;

        if (string.IsNullOrWhiteSpace(gamePath))
        {
            return null;
        }

        return Path.Combine(gamePath, "content", "tracks");
    }

    private static string? GetConfigTrack(JsonElement root)
    {
        if (root.TryGetProperty("__raceIni", out var raceIni) &&
            raceIni.ValueKind == JsonValueKind.String)
        {
            var match = Regex.Match(
                raceIni.GetString() ?? string.Empty,
                @"(?im)^CONFIG_TRACK=(.*)$");

            if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                return match.Groups[1].Value.Trim().TrimEnd('\r').Trim();
            }

            return string.Empty;
        }

        return null;
    }

    private static long GetBestTime(JsonElement root)
    {
        long? best = null;

        if (root.TryGetProperty("extras", out var extras) && extras.ValueKind == JsonValueKind.Array)
        {
            foreach (var extra in extras.EnumerateArray())
            {
                if (extra.TryGetProperty("name", out var name) &&
                    string.Equals(name.GetString(), "bestlap", StringComparison.OrdinalIgnoreCase) &&
                    extra.TryGetProperty("time", out var time) && time.TryGetInt64(out var value) && value > 0)
                {
                    best = Math.Min(best ?? long.MaxValue, value);
                }
            }
        }

        if (!root.TryGetProperty("sessions", out var sessions) || sessions.ValueKind != JsonValueKind.Array)
        {
            return best ?? 0;
        }

        foreach (var session in sessions.EnumerateArray())
        {
            if (session.TryGetProperty("bestLaps", out var bestLaps) && bestLaps.ValueKind == JsonValueKind.Array)
            {
                foreach (var lap in bestLaps.EnumerateArray())
                {
                    if (lap.TryGetProperty("time", out var time) && time.TryGetInt64(out var value) && value > 0)
                    {
                        best = Math.Min(best ?? long.MaxValue, value);
                    }
                }
            }

            if (session.TryGetProperty("laps", out var laps) && laps.ValueKind == JsonValueKind.Array)
            {
                foreach (var lap in laps.EnumerateArray())
                {
                    if (lap.TryGetProperty("time", out var time) && time.TryGetInt64(out var value) && value > 0)
                    {
                        best = Math.Min(best ?? long.MaxValue, value);
                    }
                }
            }
        }

        return best ?? 0;
    }

    private static bool IsValidSegment(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               !value.Contains('\\') &&
               !value.Contains('/');
    }
}
