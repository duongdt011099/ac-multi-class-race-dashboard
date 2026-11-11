using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using MulticlassRace.ViewModels;
using MulticlassRace.Services.AssettoCorsa;
using MulticlassRace.Services.Drivers;
using MulticlassRace.Services.Notifications;
using MulticlassRace.Services.Updates;

namespace multi_class_race_dashboard.Components.Layout;

public partial class NavMenu : IDisposable
{
    [Inject] private AssettoCorsaPathResolver PathResolver { get; set; } = default!;

    [Inject] private IAssettoCorsaContentService ContentService { get; set; } = default!;

    [Inject] private IAssettoCorsaGameConfigService GameConfigService { get; set; } = default!;

    [Inject] private SafetyCarPanelRequest PanelRequest { get; set; } = default!;

    [Inject] private IDriverService DriverService { get; set; } = default!;

    [Inject] private GameConfigChangeNotifier ConfigNotifier { get; set; } = default!;

    [Inject] private DriverDataChangeNotifier DriverNotifier { get; set; } = default!;

    [Inject] private NavigationManager Nav { get; set; } = default!;

    [Inject] private AppVersionProvider VersionProvider { get; set; } = default!;

    [Inject] private UpdateStateService UpdateState { get; set; } = default!;

    [Inject] private UpdateNotificationRequest UpdateRequest { get; set; } = default!;

    [Inject] private UserGuideRequest GuideRequest { get; set; } = default!;

    private bool isConfigured;
    private bool hasUpdate;
    private DriverModel? humanDriver;
    private IReadOnlyList<CarOptionModel> carOptions = Array.Empty<CarOptionModel>();
    private string? safetyCar;
    private string SafetyCarName => carOptions.FirstOrDefault(c => string.Equals(c.CarId, safetyCar, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? safetyCar ?? string.Empty;

    private string VersionTitle => hasUpdate
        ? "Update available - click to view"
        : "Click to view version status";

    protected override void OnInitialized()
    {
        Nav.LocationChanged += OnLocationChanged;
        ConfigNotifier.Changed += OnStateChanged;
        DriverNotifier.Changed += OnStateChanged;
        UpdateState.AvailabilityChanged += OnUpdateStateChanged;
        hasUpdate = UpdateState.Current is { IsUpdateAvailable: true };
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadStateAsync();
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => OnStateChanged();

    private async void OnStateChanged()
    {
        try
        {
            await LoadStateAsync();
            await InvokeAsync(StateHasChanged);
        }
        catch
        {
            // The menu keeps showing the last known state when a refresh fails or the circuit is gone.
        }
    }

    private async Task LoadStateAsync()
    {
        var location = await PathResolver.GetAsync();
        isConfigured = location.IsConfigured;

        var drivers = await DriverService.GetActiveDriversAsync();
        humanDriver = drivers.FirstOrDefault(d => d.IsHuman);

        if (isConfigured)
        {
            carOptions = (await ContentService.GetCarOptionsAsync()).OrderBy(c => c.CarId, StringComparer.OrdinalIgnoreCase).ToList();
            var saved = (await GameConfigService.GetAsync())?.SafetyCar;

            // Nothing configured (or the car was removed): the first car found in the game folder.
            safetyCar = carOptions.Any(c => string.Equals(c.CarId, saved, StringComparison.OrdinalIgnoreCase))
                ? carOptions.First(c => string.Equals(c.CarId, saved, StringComparison.OrdinalIgnoreCase)).CarId
                : carOptions.FirstOrDefault()?.CarId;
        }
    }

    private void OnUpdateStateChanged()
    {
        InvokeAsync(() =>
        {
            hasUpdate = UpdateState.Current is { IsUpdateAvailable: true };
            StateHasChanged();
        });
    }

    // The Drivers page picks the id up from the query string, opens that driver's editor and clears it.
    private void OpenHumanDriver()
    {
        if (humanDriver is not null)
        {
            Nav.NavigateTo($"drivers?edit={humanDriver.DriverId}");
        }
    }

    private void OpenGuide() => GuideRequest.Request();

    private void OpenUpdateDialog()
    {
        UpdateRequest.Request();
    }

    public void Dispose()
    {
        Nav.LocationChanged -= OnLocationChanged;
        ConfigNotifier.Changed -= OnStateChanged;
        DriverNotifier.Changed -= OnStateChanged;
        UpdateState.AvailabilityChanged -= OnUpdateStateChanged;
    }
}
