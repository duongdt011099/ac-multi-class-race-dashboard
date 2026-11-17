using Microsoft.AspNetCore.Components;
using MulticlassRace.Models;
using MulticlassRace.Services.AssettoCorsa;
using MulticlassRace.Services.Drivers;
using MulticlassRace.Services.QuickRace;
using MulticlassRace.Services.Races;
using MulticlassRace.Services.Results;
using MulticlassRace.Services.Settings;
using MulticlassRace.ViewModels;

namespace multi_class_race_dashboard.Components.Pages;

public partial class QuickRace : IDisposable
{
    private enum QuickRaceTab
    {
        Settings,
        Drivers,
        Result
    }

    [Inject] private IQuickRaceService QuickRaceService { get; set; } = default!;

    [Inject] private IQuickRaceLauncher Launcher { get; set; } = default!;

    [Inject] private IDriverService DriverService { get; set; } = default!;

    [Inject] private IClassSettingService ClassSettingService { get; set; } = default!;

    [Inject] private ITrackService TrackService { get; set; } = default!;

    [Inject] private IAssettoCorsaContentService ContentService { get; set; } = default!;

    [Inject] private IAssettoCorsaGameConfigService GameConfigService { get; set; } = default!;

    [Inject] private ISteamStatusService SteamStatus { get; set; } = default!;

    [Inject] private RaceSessionChangeNotifier ChangeNotifier { get; set; } = default!;

    [Inject] private LuaResultImportState ImportState { get; set; } = default!;

    private bool isLoading = true;
    private QuickRaceTab tab = QuickRaceTab.Settings;
    private QuickRaceSetupModel setup = new();
    private QuickRaceHuman human = new("Player", string.Empty, string.Empty, string.Empty);
    private DriverModel? humanDriver;
    private List<DriverModel> allDrivers = new();
    private List<TrackOptionModel> tracks = new();
    private IReadOnlyList<CarOptionModel> cars = Array.Empty<CarOptionModel>();
    private List<ClassSettingModel> classSettings = new();
    private List<TrackLayoutOptionModel> layouts = new();
    private string? layoutsFor;
    private IReadOnlyList<QuickRaceHistoryItem> history = Array.Empty<QuickRaceHistoryItem>();
    private QuickRaceResultModel? selectedResult;
    private bool isStarting;
    private bool awaitingResult;
    private ResultCelebration? celebration;
    private ClassColorMap celebrationColors = ClassColorMap.Empty;

    private string? StartBlocker =>
        QuickRaceGrid.ValidateSettings(setup.Settings, layouts) ?? QuickRaceGrid.ValidateGrid(setup, human);

    private bool CanOpenResult => StartBlocker is null;

    // Assetto Corsa needs Steam running in the background, like for a championship session.
    private bool steamRunning = true;
    private System.Threading.Timer? steamTimer;

    /// <summary>With qualifying on, the race is only started once qualifying has set the grid.</summary>
    private SessionType NextSession => setup.Settings.QualifyingEnabled && !setup.QualifyingCompleted ? SessionType.Qualifying : SessionType.Race;

    private string StartLabel => NextSession == SessionType.Qualifying
        ? "Start Qualifying"
        : setup.Settings.QualifyingEnabled ? "Start Race" : "Start Session";

    private async Task RedoQualifyingAsync()
    {
        QuickRaceGrid.ClearQualifying(setup);
        await SaveAsync();
    }

    private bool CanStart => CanOpenResult && steamRunning;

    private string StartHint => !CanOpenResult
        ? StartBlocker!
        : !steamRunning
            ? "Steam is not running. Start Steam in the background to start a session."
            : "Start the quick race in Assetto Corsa";

    protected override async Task OnInitializedAsync()
    {
        ChangeNotifier.QuickRaceImported += OnQuickRaceImported;
        ImportState.OutcomeReported += OnImportOutcome;
        ChangeNotifier.QuickQualifyingImported += OnQuickQualifyingImported;
        steamRunning = SteamStatus.IsRunning();
        steamTimer = new System.Threading.Timer(_ => _ = PollSteamAsync(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));

        try
        {
            tracks = (await TrackService.GetTrackOptionsAsync()).ToList();
            cars = await ContentService.GetCarOptionsAsync();
            classSettings = (await ClassSettingService.GetAllAsync()).ToList();
            allDrivers = (await DriverService.GetActiveDriversAsync()).ToList();
            humanDriver = allDrivers.FirstOrDefault(d => d.IsHuman);

            setup = await QuickRaceService.GetSetupAsync();

            if (setup.Mode == QuickRaceGridMode.ExistingClasses)
            {
                var setting = classSettings.FirstOrDefault(s => s.ClassSettingId == setup.ClassSettingId);

                if (setting is null)
                {
                    // The class setting was deleted since the last visit.
                    setup.Mode = QuickRaceGridMode.Custom;
                    setup.ClassSettingId = null;
                }

                QuickRaceGrid.SyncSystemClasses(setup, setting);
            }

            RememberQualifyingState();
            RefreshHuman();
            await LoadLayoutsAsync();
            await LoadHistoryAsync();

            selectedResult = history.Count > 0 ? await QuickRaceService.GetResultAsync(history[0].ResultId) : null;

            await SaveAsync();
        }
        catch (Exception ex)
        {
            Toast.Show($"Could not load the quick race: {ex.Message}", isError: true);
        }
        finally
        {
            isLoading = false;
        }
    }

    private void RefreshHuman()
    {
        human = QuickRaceGrid.ResolveHuman(setup.Settings, humanDriver, cars);
        QuickRaceGrid.Normalize(setup, human.DriverName);
    }

    private async Task LoadLayoutsAsync()
    {
        var track = setup.Settings.TrackName;
        layoutsFor = track;
        layouts = string.IsNullOrWhiteSpace(track)
            ? new List<TrackLayoutOptionModel>()
            : (await TrackService.GetTrackLayoutOptionsAsync(track)).ToList();
    }

    private async Task LoadHistoryAsync() => history = await QuickRaceService.GetHistoryAsync();

    private async Task SaveAsync()
    {
        RefreshHuman();
        await TryAsync(() => QuickRaceService.SaveSetupAsync(setup), "Could not save the quick race");

        if (tab == QuickRaceTab.Result && !HasResults)
        {
            tab = QuickRaceTab.Settings;
        }
    }

    private async Task HandleTrackChangedAsync()
    {
        await LoadLayoutsAsync();

        // A track with a default layout needs no choice: a blank layout is that default.
        if (RaceFormRules.HasDefaultLayout(layouts))
        {
            setup.Settings.TrackLayout = string.Empty;
        }
    }

    private async Task HandleSettingsChangedAsync()
    {
        if (!string.Equals(layoutsFor, setup.Settings.TrackName, StringComparison.OrdinalIgnoreCase))
        {
            await HandleTrackChangedAsync();
        }

        ForgetQualifyingWhenItNoLongerApplies();
        await SaveAsync();
    }

    private void GoNext() => tab = QuickRaceTab.Drivers;

    private bool HasResults => history.Count > 0;

    private async Task HandleGridChangedAsync()
    {
        // Other cars on the grid mean the qualifying result no longer describes it.
        if (QuickRaceGrid.Signature(setup) != gridSignature && setup.QualifyingCompleted)
        {
            QuickRaceGrid.ClearQualifying(setup);
        }

        await SaveAsync();
        RememberQualifyingState();
    }

    private string? qualifyingKey;
    private string? gridSignature;

    private void RememberQualifyingState()
    {
        qualifyingKey = $"{setup.Settings.TrackName}|{setup.Settings.TrackLayout}|{setup.Settings.QualifyingEnabled}";
        gridSignature = QuickRaceGrid.Signature(setup);
    }

    /// <summary>Qualifying only counts for the track it was run on, and only while it is switched on.</summary>
    private void ForgetQualifyingWhenItNoLongerApplies()
    {
        var key = $"{setup.Settings.TrackName}|{setup.Settings.TrackLayout}|{setup.Settings.QualifyingEnabled}";

        if (key != qualifyingKey && setup.QualifyingCompleted)
        {
            QuickRaceGrid.ClearQualifying(setup);
        }

        qualifyingKey = key;
    }

    private async Task StartAsync()
    {
        if (!CanStart || isStarting)
        {
            return;
        }

        isStarting = true;

        try
        {
            await Launcher.LaunchAsync(NextSession);
            awaitingResult = true;
            Toast.Show(NextSession == SessionType.Qualifying ? "Started qualifying in Assetto Corsa." : "Started the quick race in Assetto Corsa.");
        }
        catch (Exception ex)
        {
            Toast.Show(ex.Message, isError: true);
        }
        finally
        {
            isStarting = false;
        }
    }

    private async Task SelectResultAsync(Guid resultId)
    {
        await TryAsync(async () => selectedResult = await QuickRaceService.GetResultAsync(resultId));
    }

    private async Task DeleteResultAsync(Guid resultId)
    {
        var deleted = await TryAsync(() => QuickRaceService.DeleteResultAsync(resultId));

        if (!deleted)
        {
            return;
        }

        await LoadHistoryAsync();

        if (selectedResult?.ResultId == resultId)
        {
            selectedResult = history.Count > 0 ? await QuickRaceService.GetResultAsync(history[0].ResultId) : null;
        }
    }

    // The game closed and the import has had its say (a result, a failure, or no result at all): nothing is
    // running any more, so the "session is running" note must go, whatever the outcome was.
    private void OnImportOutcome(LuaResultImportOutcome outcome)
    {
        _ = InvokeAsync(() =>
        {
            if (awaitingResult)
            {
                awaitingResult = false;
                StateHasChanged();
            }
        });
    }

    private void OnQuickRaceImported(Guid resultId)
    {
        _ = InvokeAsync(async () =>
        {
            try
            {
                // The race used up the qualifying, so the grid is back to needing a new one.
                setup = await QuickRaceService.GetSetupAsync();

                if (setup.Mode == QuickRaceGridMode.ExistingClasses)
                {
                    QuickRaceGrid.SyncSystemClasses(setup, classSettings.FirstOrDefault(s => s.ClassSettingId == setup.ClassSettingId));
                }

                RefreshHuman();
                RememberQualifyingState();
                await LoadHistoryAsync();
                var result = await QuickRaceService.GetResultAsync(resultId);

                if (result is null)
                {
                    return;
                }

                selectedResult = result;
                awaitingResult = false;
                tab = QuickRaceTab.Result;
                ShowCelebration(result);

                StateHasChanged();
            }
            catch (Exception ex)
            {
                Toast.Show($"Could not show the imported result: {ex.Message}", isError: true);
            }
        });
    }

    private void OnQuickQualifyingImported(QuickRaceQualifyingModel result)
    {
        _ = InvokeAsync(async () =>
        {
            try
            {
                // The import reordered the saved grid; show it.
                setup = await QuickRaceService.GetSetupAsync();

                if (setup.Mode == QuickRaceGridMode.ExistingClasses)
                {
                    QuickRaceGrid.SyncSystemClasses(setup, classSettings.FirstOrDefault(s => s.ClassSettingId == setup.ClassSettingId));
                }

                RefreshHuman();
                RememberQualifyingState();
                awaitingResult = false;
                ShowCelebration(result.Rows, SessionType.Qualifying, result.Date);
                StateHasChanged();
            }
            catch (Exception ex)
            {
                Toast.Show($"Could not show the qualifying result: {ex.Message}", isError: true);
            }
        });
    }

    private void ShowCelebration(QuickRaceResultModel result) => ShowCelebration(result.Rows, SessionType.Race, result.RaceDate);

    /// <summary>The human driver on the podium (or on pole, after qualifying) of their class gets the same celebration as in a championship race.</summary>
    private void ShowCelebration(IReadOnlyList<QuickRaceResultRow> rows, SessionType sessionType, DateTime date)
    {
        var session = new SessionModel
        {
            SessionType = sessionType,
            SessionDate = date,
            DriverStandings = rows.Select(row => new DriverStandingModel
            {
                Position = row.Position,
                ClassPosition = row.ClassPosition,
                DriverName = row.DriverName,
                TeamName = string.IsNullOrWhiteSpace(row.TeamName) ? row.CarDisplayName : row.TeamName,
                TeamClassName = row.ClassName,
                BestLapTimeMs = row.BestLapTimeMs,
                LapCount = row.LapCount,
                IsHuman = row.IsHuman
            }).ToList()
        };

        celebration = ResultCelebration.Evaluate(session);

        celebrationColors = ClassColorMap.From(new[]
        {
            new ClassSettingModel
            {
                ClassSettingId = Guid.Empty,
                ClassSettingName = "Quick Race",
                Items = rows
                    .Where(r => !string.IsNullOrWhiteSpace(r.ClassName))
                    .GroupBy(r => r.ClassName, StringComparer.OrdinalIgnoreCase)
                    .Select((g, index) => new ClassSettingItemModel { Name = g.Key, Order = index, Color = g.First().ClassColor })
                    .ToList()
            }
        });
    }

    private async Task PollSteamAsync()
    {
        try
        {
            var running = SteamStatus.IsRunning();

            if (running != steamRunning)
            {
                await InvokeAsync(() =>
                {
                    steamRunning = running;
                    StateHasChanged();
                });
            }
        }
        catch (ObjectDisposedException)
        {
            // The page closed while the check was running.
        }
    }

    public void Dispose()
    {
        steamTimer?.Dispose();
        ChangeNotifier.QuickRaceImported -= OnQuickRaceImported;
        ImportState.OutcomeReported -= OnImportOutcome;
        ChangeNotifier.QuickQualifyingImported -= OnQuickQualifyingImported;
    }
}
