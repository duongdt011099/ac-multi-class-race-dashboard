using System.Globalization;
using Microsoft.AspNetCore.Components;
using MulticlassRace.BackgroundServices;
using MulticlassRace.Services.Updates;

namespace multi_class_race_dashboard.Components;

public partial class UpdateNotification : IDisposable
{
    private const int MaxNotesLength = 1500;

    [Inject] private UpdateStateService StateService { get; set; } = default!;

    [Inject] private UpdateNotificationRequest UpdateRequest { get; set; } = default!;

    [Inject] private UpdateCheckWorker UpdateWorker { get; set; } = default!;

    [Inject] private IAppUpdatePreferenceService UpdatePreferences { get; set; } = default!;

    private UpdateCheckResult? result;
    private string? truncatedNotes;
    private string lastSeen = string.Empty;
    private bool isOpen;
    private bool isChecking;
    private bool isDismissing;

    private string HeaderTitle => result switch
    {
        { IsUpdateAvailable: true } => "Update Available",
        { HasError: true } => "Update Status",
        _ => "Up To Date"
    };

    protected override void OnInitialized()
    {
        ApplyResult(StateService.Current);
        StateService.AvailabilityChanged += OnAvailabilityChanged;
        UpdateRequest.Requested += OnOpenRequested;
    }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            lastSeen = await UpdatePreferences.GetLastSeenVersionAsync() ?? string.Empty;
        }
        catch
        {
            lastSeen = string.Empty;
        }

        if (ShouldAutoOpen())
        {
            isOpen = true;
        }
    }

    private void OnAvailabilityChanged()
    {
        InvokeAsync(() =>
        {
            ApplyResult(StateService.Current);

            if (ShouldAutoOpen())
            {
                isOpen = true;
            }

            StateHasChanged();
        });
    }

    private void OnOpenRequested()
    {
        InvokeAsync(async () =>
        {
            await EnsureResultAsync();
            ApplyResult(StateService.Current);
            isOpen = true;
            StateHasChanged();
        });
    }

    private async Task EnsureResultAsync()
    {
        if (StateService.Current is not null)
        {
            return;
        }

        try
        {
            await UpdateWorker.CheckNowAsync();
        }
        catch
        {
            // With no result the dialog reports it could not check.
        }
    }

    private async Task CheckForUpdateAsync()
    {
        if (isChecking)
        {
            return;
        }

        isChecking = true;

        try
        {
            await UpdateWorker.CheckNowAsync();
            ApplyResult(StateService.Current);
        }
        catch
        {
            // A failed check leaves the previous result on screen.
        }
        finally
        {
            isChecking = false;
        }
    }

    private void ApplyResult(UpdateCheckResult? value)
    {
        result = value;
        truncatedNotes = Truncate(value?.ReleaseNotes);
        isDismissing = false;
    }

    private bool ShouldAutoOpen()
    {
        if (result is not { IsUpdateAvailable: true } available)
        {
            return false;
        }

        return !string.Equals(available.TagName, lastSeen, StringComparison.OrdinalIgnoreCase);
    }

    private async Task DismissAsync()
    {
        if (isDismissing || result is null)
        {
            return;
        }

        if (result.IsUpdateAvailable)
        {
            isDismissing = true;
        }

        isOpen = false;

        if (result is { IsUpdateAvailable: true, TagName: not null } available)
        {
            try
            {
                await UpdatePreferences.MarkSeenAsync(available.TagName);
                lastSeen = available.TagName;
            }
            catch
            {
                // Not remembering the dismissal only means the dialog may open again next time.
            }
        }

        isDismissing = false;
    }

    internal static string? Truncate(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        var trimmed = notes.Trim();

        return trimmed.Length <= MaxNotesLength
            ? trimmed
            : string.Concat(trimmed.AsSpan(0, MaxNotesLength).TrimEnd(), "...");
    }

    internal static string FormatSize(long bytes)
    {
        string[] units = ["KB", "MB", "GB"];

        double size = bytes;
        var unitIndex = -1;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex < 0
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes} B")
            : string.Create(CultureInfo.InvariantCulture, $"{size:0.0} {units[unitIndex]}");
    }

    public void Dispose()
    {
        StateService.AvailabilityChanged -= OnAvailabilityChanged;
        UpdateRequest.Requested -= OnOpenRequested;
    }
}
