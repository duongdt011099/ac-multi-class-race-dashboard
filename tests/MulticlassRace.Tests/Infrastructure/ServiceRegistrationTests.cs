using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.JSInterop;
using MulticlassRace.Data;
using MulticlassRace.Repositories;
using MulticlassRace.Services;
using MulticlassRace.Services.AssettoCorsa;
using MulticlassRace.Services.Championships;
using MulticlassRace.Services.Drivers;
using MulticlassRace.Services.Notifications;
using MulticlassRace.Services.Races;
using MulticlassRace.Services.Results;
using MulticlassRace.Services.Settings;
using MulticlassRace.Services.Teams;
using MulticlassRace.Services.Updates;

namespace MulticlassRace.Tests.Infrastructure;

public class ServiceRegistrationTests
{
    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "tests";

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "mcr-tests-web");

        public string EnvironmentName { get; set; } = "Development";

        public string ContentRootPath { get; set; } = Path.GetTempPath();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>Blazor supplies the real one per circuit; registrations only need something to inject.</summary>
    private sealed class NullJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => default;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => default;
    }

    private static ServiceProvider Build(bool validate = true)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IWebHostEnvironment>(new TestEnvironment());
        services.AddSingleton<IJSRuntime, NullJsRuntime>();
        services.RegisterDbContext(configuration, Path.GetTempPath());
        services.RegisterRepositories();
        services.RegisterServices(configuration);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = validate,
            ValidateScopes = validate
        });
    }

    [Fact]
    public void Container_BuildsWithScopeAndDependencyValidation()
    {
        using var provider = Build();

        Assert.NotNull(provider);
    }

    [Theory]
    [InlineData(typeof(IChampionshipService))]
    [InlineData(typeof(ISeasonService))]
    [InlineData(typeof(IRaceService))]
    [InlineData(typeof(IRaceSessionLauncher))]
    [InlineData(typeof(IRaceResultService))]
    [InlineData(typeof(IGridPresetService))]
    [InlineData(typeof(IClassSettingService))]
    [InlineData(typeof(IPointSettingService))]
    [InlineData(typeof(ITeamService))]
    [InlineData(typeof(ITeamLogoStorage))]
    [InlineData(typeof(IDriverService))]
    [InlineData(typeof(ITrackService))]
    [InlineData(typeof(IAssettoCorsaContentService))]
    [InlineData(typeof(IAssettoCorsaGameConfigService))]
    [InlineData(typeof(ILuaResultImportService))]
    [InlineData(typeof(IAppUpdatePreferenceService))]
    [InlineData(typeof(ToastService))]
    [InlineData(typeof(DriverDataChangeNotifier))]
    [InlineData(typeof(GameConfigChangeNotifier))]
    [InlineData(typeof(UpdateNotificationRequest))]
    public void EveryScopedServiceResolvesWithinAScope(Type serviceType)
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService(serviceType));
    }

    [Theory]
    [InlineData(typeof(AssettoCorsaPathResolver))]
    [InlineData(typeof(IGameSessionLauncher))]
    [InlineData(typeof(SessionLaunchTracker))]
    [InlineData(typeof(RaceSessionChangeNotifier))]
    [InlineData(typeof(LuaResultImportState))]
    [InlineData(typeof(ResultScanBaseline))]
    [InlineData(typeof(LuaResultSweeper))]
    [InlineData(typeof(AppVersionProvider))]
    [InlineData(typeof(UpdateStateService))]
    public void SharedStateServicesAreSingletons(Type serviceType)
    {
        using var provider = Build();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Assert.Same(
            first.ServiceProvider.GetRequiredService(serviceType),
            second.ServiceProvider.GetRequiredService(serviceType));
    }

    [Fact]
    public void ScopedServicesAreNotSharedBetweenScopes()
    {
        using var provider = Build();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<ToastService>(),
            second.ServiceProvider.GetRequiredService<ToastService>());
    }

    [Fact]
    public void BackgroundWorkersAreRegisteredAsHostedServices()
    {
        using var provider = Build();

        var hosted = provider.GetServices<IHostedService>().Select(s => s.GetType().Name).ToList();

        Assert.Contains("UpdateCheckWorker", hosted);
        Assert.Contains("LuaResultImportWorker", hosted);
        Assert.Contains("LuaResultCleanupService", hosted);
    }

    [Fact]
    public void UpdateCheckWorkerIsTheSameInstanceAsTheHostedOne()
    {
        using var provider = Build();

        var hosted = provider.GetServices<IHostedService>().Single(s => s.GetType().Name == "UpdateCheckWorker");

        Assert.Same(hosted, provider.GetRequiredService<MulticlassRace.BackgroundServices.UpdateCheckWorker>());
    }
}
