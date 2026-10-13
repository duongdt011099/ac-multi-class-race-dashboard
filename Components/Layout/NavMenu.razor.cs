using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using MulticlassRace.Services;
using MulticlassRace.Services.Abstractions;
using MulticlassRace.ViewModels;

namespace multi_class_race_dashboard.Components.Layout;

public partial class NavMenu : IDisposable
{
    [Inject] private AssettoCorsaPathResolver PathResolver { get; set; } = default!;

    [Inject] private IDriverService DriverService { get; set; } = default!;

    [Inject] private GameConfigChangeNotifier ConfigNotifier { get; set; } = default!;

    [Inject] private DriverDataChangeNotifier DriverNotifier { get; set; } = default!;

    [Inject] private NavigationManager Nav { get; set; } = default!;

    [Inject] private AppVersionProvider VersionProvider { get; set; } = default!;

    [Inject] private UpdateStateService UpdateState { get; set; } = default!;

    [Inject] private UpdateNotificationRequest UpdateRequest { get; set; } = default!;

    private bool isConfigured;
    private bool hasUpdate;
    private DriverModel? humanDriver;

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
    }

    private void OnUpdateStateChanged()
    {
        InvokeAsync(() =>
        {
            hasUpdate = UpdateState.Current is { IsUpdateAvailable: true };
            StateHasChanged();
        });
    }

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
