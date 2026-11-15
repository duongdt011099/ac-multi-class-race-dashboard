using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MulticlassRace.BackgroundServices;
using MulticlassRace.Configuration;
using MulticlassRace.Services.Updates;

namespace MulticlassRace.Tests.Services;

public sealed class UpdateInstallerServiceTests : IDisposable
{
    private const string Url = "https://github.com/owner/repo/releases/download/v2.0.0/Setup.exe";

    private static readonly byte[] Payload = [1, 2, 3, 4, 5, 6, 7, 8];

    private readonly string _work = Path.Combine(Path.GetTempPath(), "uis-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeEnvironment _environment = new();
    private readonly FakeLifetime _lifetime = new();
    private readonly UpdateCheckOptions _options = new() { Owner = "owner", Repository = "repo" };

    public void Dispose()
    {
        if (Directory.Exists(_work))
        {
            Directory.Delete(_work, recursive: true);
        }
    }

    [Fact]
    public async Task Download_WithMatchingChecksum_BecomesReady()
    {
        var service = Create(Payload);

        await service.DownloadAsync(Update(Hash(Payload)));

        Assert.Equal(UpdateInstallPhase.Ready, service.Phase);
        Assert.Equal(100, service.ProgressPercent);
        Assert.Null(service.Error);
    }

    [Fact]
    public async Task Download_WithWrongChecksum_FailsAndKeepsNoFile()
    {
        var service = Create(Payload);

        await service.DownloadAsync(Update(Hash([9, 9, 9])));

        Assert.Equal(UpdateInstallPhase.Failed, service.Phase);
        Assert.Contains("checksum", service.Error);
        Assert.Empty(Directory.EnumerateFiles(_work, "*", SearchOption.AllDirectories));
        Assert.False(service.TryInstall(out _));
    }

    [Fact]
    public async Task Download_WithoutPublishedChecksum_RefusesToRunAnything()
    {
        var service = Create(Payload);

        await service.DownloadAsync(Update(null));

        Assert.Equal(UpdateInstallPhase.Failed, service.Phase);
        Assert.Contains("checksum", service.Error);
    }

    [Fact]
    public async Task Download_ServerError_FailsWithoutThrowing()
    {
        var service = Create(Payload, HttpStatusCode.NotFound);

        await service.DownloadAsync(Update(Hash(Payload)));

        Assert.Equal(UpdateInstallPhase.Failed, service.Phase);
    }

    [Theory]
    [InlineData("https://evil.example.com/owner/repo/releases/download/v2/Setup.exe")]
    [InlineData("http://github.com/owner/repo/releases/download/v2/Setup.exe")]
    [InlineData("https://github.com/someone/else/releases/download/v2/Setup.exe")]
    public void ValidateAsset_RejectsForeignUrls(string url)
    {
        var asset = new ReleaseAsset("Setup.exe", url, 8, Hash(Payload));

        Assert.NotNull(UpdateInstallerService.ValidateAsset(asset, _options));
    }

    [Fact]
    public void ValidateAsset_RejectsPathLikeNames()
    {
        var asset = new ReleaseAsset(@"..\Setup.exe", Url, 8, Hash(Payload));

        Assert.NotNull(UpdateInstallerService.ValidateAsset(asset, _options));
    }

    [Fact]
    public async Task TryInstall_WhenReady_LaunchesInstallerAndStopsApp()
    {
        var service = Create(Payload);
        await service.DownloadAsync(Update(Hash(Payload)));

        var started = service.TryInstall(out var error);

        Assert.True(started, error);
        Assert.True(_lifetime.StopRequested);
        Assert.EndsWith("Setup.exe", _environment.LaunchedPath);
        Assert.Contains("/SILENT", _environment.LaunchedArguments);
        Assert.Contains("/RELAUNCH=1", _environment.LaunchedArguments);
    }

    [Fact]
    public async Task TryInstall_WhileGameRunning_DoesNothing()
    {
        var service = Create(Payload);
        await service.DownloadAsync(Update(Hash(Payload)));
        _environment.GameRunning = true;

        var started = service.TryInstall(out var error);

        Assert.False(started);
        Assert.Contains("Assetto Corsa", error);
        Assert.False(_lifetime.StopRequested);
        Assert.Null(_environment.LaunchedPath);
    }

    [Fact]
    public async Task TryInstall_WithLegacyService_DoesNothing()
    {
        var service = Create(Payload);
        await service.DownloadAsync(Update(Hash(Payload)));
        _environment.LegacyService = true;

        Assert.False(service.TryInstall(out var error));
        Assert.Contains("service", error);
        Assert.False(_lifetime.StopRequested);
    }

    [Fact]
    public async Task AutoInstall_InstallsAsSoonAsDownloadIsVerified()
    {
        _options.AutoInstall = true;
        var service = Create(Payload);

        await service.DownloadAsync(Update(Hash(Payload)));

        Assert.True(_lifetime.StopRequested);
    }

    [Fact]
    public void TryInstall_BeforeDownload_IsRefused()
    {
        var service = Create(Payload);

        Assert.False(service.TryInstall(out _));
        Assert.False(_lifetime.StopRequested);
    }

    [Fact]
    public void BuildInstallerArguments_KeepsInstallFolderAndGameFolderQuoted()
    {
        var args = UpdateInstallerService.BuildInstallerArguments(@"C:\Apps\Race Dash\", @"D:\Steam\Assetto Corsa");

        Assert.Contains(@"/DIR=""C:\Apps\Race Dash""", args);
        Assert.Contains(@"/ACFOLDER=""D:\Steam\Assetto Corsa""", args);
    }

    [Fact]
    public void BuildInstallerArguments_OmitsGameFolderWhenUnknown()
    {
        var args = UpdateInstallerService.BuildInstallerArguments(@"C:\Apps\Race", null);

        Assert.DoesNotContain("/ACFOLDER", args);
    }

    [Theory]
    [InlineData("sha256:" + "ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12", "ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12")]
    [InlineData("SHA256:AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12", "ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12")]
    [InlineData("sha1:abc", null)]
    [InlineData("sha256:short", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParseDigest_AcceptsOnlyWellFormedSha256(string? digest, string? expected)
    {
        Assert.Equal(expected, UpdateCheckWorker.ParseDigest(digest));
    }

    private UpdateInstallerService Create(byte[] body, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new UpdateInstallerService(
            new FakeHttpClientFactory(body, status),
            Options.Create(_options),
            new UpdateStateService(),
            new AppVersionProvider(),
            _environment,
            _lifetime,
            NullLogger<UpdateInstallerService>.Instance,
            _work,
            @"C:\Apps\Race");
    }

    private static UpdateCheckResult Update(string? sha256)
    {
        return new UpdateCheckResult(
            "1.0.0",
            "2.0.0",
            "v2.0.0",
            "https://github.com/owner/repo/releases/tag/v2.0.0",
            null,
            null,
            new ReleaseAsset("Setup.exe", Url, Payload.Length, sha256),
            true,
            DateTimeOffset.UtcNow,
            null);
    }

    private static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private sealed class FakeEnvironment : IUpdateInstallerEnvironment
    {
        public bool GameRunning { get; set; }
        public bool LegacyService { get; set; }
        public string? LaunchedPath { get; private set; }
        public string? LaunchedArguments { get; private set; }

        public bool IsGameRunning() => GameRunning;
        public bool HasLegacyService() => LegacyService;

        public void Launch(string installerPath, string arguments)
        {
            LaunchedPath = installerPath;
            LaunchedArguments = arguments;
        }
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        public bool StopRequested { get; private set; }
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() => StopRequested = true;
    }

    private sealed class FakeHttpClientFactory(byte[] body, HttpStatusCode status) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FakeHandler(body, status));
    }

    private sealed class FakeHandler(byte[] body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body), RequestMessage = request });
        }
    }
}
