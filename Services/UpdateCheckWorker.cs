using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using MulticlassRace.Configuration;
using MulticlassRace.Services.Abstractions;

namespace MulticlassRace.Services;

public class UpdateCheckWorker : BackgroundService
{
    public const string HttpClientName = "UpdateCheck";

    private const string ApiBaseUrl = "https://api.github.com";
    private const string ReleasesBaseUrl = "https://github.com";

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<UpdateCheckOptions> _options;
    private readonly UpdateStateService _stateService;
    private readonly AppVersionProvider _versionProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UpdateCheckWorker> _logger;

    public UpdateCheckWorker(
        IHttpClientFactory httpClientFactory,
        IOptions<UpdateCheckOptions> options,
        UpdateStateService stateService,
        AppVersionProvider versionProvider,
        IServiceScopeFactory scopeFactory,
        ILogger<UpdateCheckWorker> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _stateService = stateService;
        _versionProvider = versionProvider;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.Value;

        if (!options.Enabled)
        {
            _logger.LogInformation("Update checking is disabled by configuration.");
            return;
        }

        if (!options.IsConfigured)
        {
            _logger.LogWarning("Update checking is enabled but 'Owner' and 'Repository' are not configured. Skipping.");
            return;
        }

        await CheckAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(options.ResolvedIntervalHours));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CheckAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        var currentVersion = _versionProvider.CurrentVersion;

        try
        {
            var release = await FetchLatestReleaseAsync(cancellationToken);

            if (release is null)
            {
                _stateService.Set(UpdateCheckResult.Failed(
                    currentVersion,
                    checkedAt,
                    "Could not reach GitHub to check for updates.",
                    _stateService.Current));
                return;
            }

            await MarkCheckedAsync();

            var result = BuildResult(release, currentVersion, checkedAt);

            if (result.HasError)
            {
                _logger.LogInformation("Update check did not produce a comparison: {Error}", result.ErrorMessage);
            }

            _stateService.Set(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for updates.");

            _stateService.Set(UpdateCheckResult.Failed(
                currentVersion,
                checkedAt,
                ex.Message,
                _stateService.Current));
        }
    }

    private async Task<GithubRelease?> FetchLatestReleaseAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var url = $"{ApiBaseUrl}/repos/{options.Owner.Trim()}/{options.Repository.Trim()}/releases/latest";

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("EnduranceRaceDashboard", _versionProvider.CurrentVersion));

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("GitHub release check returned {StatusCode}.", (int)response.StatusCode);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<GithubRelease>(_jsonOptions, cancellationToken);
    }

    private UpdateCheckResult BuildResult(GithubRelease release, string currentVersionText, DateTimeOffset checkedAt)
    {
        if (string.IsNullOrWhiteSpace(release.TagName))
        {
            return UpdateCheckResult.Failed(
                currentVersionText,
                checkedAt,
                "The latest GitHub release has no version tag.",
                _stateService.Current);
        }

        var currentVersion = ParseVersion(currentVersionText);
        var latestVersion = ParseVersion(release.TagName);

        if (currentVersion is null || latestVersion is null)
        {
            _logger.LogInformation(
                "Could not compare current '{Current}' against tag '{Tag}'.",
                currentVersionText,
                release.TagName);

            return UpdateCheckResult.Failed(
                currentVersionText,
                checkedAt,
                $"Could not compare version {currentVersionText} against release tag {release.TagName}.",
                _stateService.Current);
        }

        if (latestVersion <= currentVersion)
        {
            return UpdateCheckResult.UpToDate(currentVersionText, latestVersion.ToString(), checkedAt);
        }

        var options = _options.Value;
        var releaseUrl = string.IsNullOrWhiteSpace(release.HtmlUrl)
            ? $"{ReleasesBaseUrl}/{options.Owner.Trim()}/{options.Repository.Trim()}/releases/latest"
            : release.HtmlUrl;

        return new UpdateCheckResult(
            currentVersionText,
            latestVersion.ToString(),
            release.TagName,
            releaseUrl,
            release.Body,
            release.PublishedAt,
            SelectInstaller(release),
            true,
            checkedAt,
            null);
    }

    private ReleaseAsset? SelectInstaller(GithubRelease release)
    {
        var assets = release.Assets;

        if (assets is null || assets.Count == 0)
        {
            return null;
        }

        var installerName = _options.Value.InstallerFileName?.Trim();

        var match = !string.IsNullOrWhiteSpace(installerName)
            ? assets.FirstOrDefault(a => string.Equals(a.Name?.Trim(), installerName, StringComparison.OrdinalIgnoreCase))
            : null;

        match ??= assets.FirstOrDefault(a =>
            a.Name is not null && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        if (match is null || string.IsNullOrWhiteSpace(match.BrowserDownloadUrl))
        {
            return null;
        }

        return new ReleaseAsset(match.Name!.Trim(), match.BrowserDownloadUrl, match.Size);
    }

    private async Task MarkCheckedAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var preferences = scope.ServiceProvider.GetRequiredService<IAppUpdatePreferenceService>();
            await preferences.MarkCheckedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record the update check timestamp.");
        }
    }

    private static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        if (trimmed.Length > 0 && (trimmed[0] == 'v' || trimmed[0] == 'V'))
        {
            trimmed = trimmed[1..];
        }

        var dashIndex = trimmed.IndexOf('-');
        if (dashIndex >= 0)
        {
            trimmed = trimmed[..dashIndex];
        }

        if (!Version.TryParse(trimmed, out var version))
        {
            return null;
        }

        return new Version(
            version.Major,
            version.Minor,
            version.Build < 0 ? 0 : version.Build);
    }

    private sealed class GithubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset PublishedAt { get; set; }

        [JsonPropertyName("assets")]
        public List<GithubReleaseAsset>? Assets { get; set; }
    }

    private sealed class GithubReleaseAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}
