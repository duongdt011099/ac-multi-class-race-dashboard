using System.Globalization;
using System.Text.Json;

namespace MulticlassRace.Services;

/// <summary>
/// When the app last decided it had a clean slate of results in front of it.
///
/// A brand new install has an empty database, so every result file the Lua app ever wrote looks
/// brand new and gets offered for import one modal at a time. Stopping at a recorded baseline
/// means a result is only ever offered if it appeared after the dashboard was already in use.
///
/// Deliberately a sibling file rather than a database column: install.json already establishes this
/// pattern, it keeps a schema change off a feature that must not risk one, and the file is removed
/// by the uninstaller so a reinstall starts over.
/// </summary>
public sealed class ResultScanBaseline(ILogger<ResultScanBaseline> logger)
{
    public const string FileName = "result-scan.json";

    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, FileName);

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Results written at or before this moment are never offered. A result written before the
    /// dashboard was ever launched belongs to history, not to a session someone is waiting on.
    /// </summary>
    public async Task<DateTime?> GetCutoffUtcAsync(string resolvedRoot)
    {
        await _gate.WaitAsync();

        try
        {
            var stored = Read();

            if (stored is not null
                && string.Equals(stored.Root, resolvedRoot, StringComparison.OrdinalIgnoreCase)
                && DateTime.TryParse(stored.BaselineUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var parsed))
            {
                return parsed;
            }

            // Either the very first run, or the configured result folder changed. Either way the
            // files already sitting in it are history, so a new baseline is drawn before anything
            // is offered rather than after.
            return await WriteAsync(resolvedRoot);
        }
        finally
        {
            _gate.Release();
        }
    }

    private Baseline? Read()
    {
        try
        {
            if (File.Exists(FilePath) is false)
            {
                return null;
            }

            return JsonSerializer.Deserialize<Baseline>(File.ReadAllText(FilePath), SerializerOptions);
        }
        catch
        {
            // A missing or hand-mangled marker is not worth failing over; it only costs a re-drawn
            // baseline, and the caller falls back to offering everything.
            return null;
        }
    }

    private async Task<DateTime?> WriteAsync(string resolvedRoot)
    {
        // Sampled once and returned as written, so the cutoff this run uses is exactly the one the
        // next run will read back rather than a hair later than it.
        var now = DateTime.UtcNow;

        try
        {
            var json = JsonSerializer.Serialize(
                new Baseline { Root = resolvedRoot, BaselineUtc = now.ToString("O", CultureInfo.InvariantCulture) },
                SerializerOptions);

            await File.WriteAllTextAsync(FilePath, json);
            return now;
        }
        catch (Exception ex)
        {
            // Unwritable folder, antivirus lock, read-only install: fall back to no cutoff so
            // results stay visible. Over-offering is recoverable, hiding results is not.
            logger.LogWarning(ex, "Could not write {FileName}, so old results may be offered again.", FileName);
            return null;
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class Baseline
    {
        public string? Root { get; set; }

        public string? BaselineUtc { get; set; }
    }
}
