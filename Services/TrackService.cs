using System.Text.Json;
using System.Text.RegularExpressions;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.Services.Abstractions;

namespace MulticlassRace.Services;

public class TrackService : ITrackService
{
    private readonly IAssettoCorsaGameConfigRepository _configRepository;

    public TrackService(IAssettoCorsaGameConfigRepository configRepository)
    {
        _configRepository = configRepository;
    }

    public async Task<IReadOnlyList<string>> GetTrackNamesAsync()
    {
        var tracksRoot = await GetTracksRootAsync();

        if (tracksRoot is null || !Directory.Exists(tracksRoot))
        {
            return Array.Empty<string>();
        }

        return Directory.GetDirectories(tracksRoot)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetTrackLayoutsAsync(string trackName)
    {
        if (IsValidSegment(trackName) is false)
        {
            return Array.Empty<string>();
        }

        var tracksRoot = await GetTracksRootAsync();

        if (tracksRoot is null)
        {
            return Array.Empty<string>();
        }

        var uiDir = Path.Combine(tracksRoot, trackName, "ui");

        if (!Directory.Exists(uiDir))
        {
            return Array.Empty<string>();
        }

        return Directory.GetDirectories(uiDir)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
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
        var resultsPath = config?.RaceResultsPath?.Trim();

        if (string.IsNullOrWhiteSpace(resultsPath) || !Directory.Exists(resultsPath))
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
        var config = await _configRepository.GetAsync();
        var gamePath = config?.GamePath?.Trim();

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