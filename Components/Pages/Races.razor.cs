using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using MulticlassRace.Common;
using MulticlassRace.Services.Browser;
using MulticlassRace.ViewModels;
using MulticlassRace.Services.AssettoCorsa;
using MulticlassRace.Services.Championships;
using MulticlassRace.Services.Races;
using MulticlassRace.Services.Settings;

namespace multi_class_race_dashboard.Components.Pages;

public partial class Races
{
    private enum SortColumn
    {
        Race,
        Status
    }

    private const string ChampionshipStorageKey = "wec.races.selectedChampionshipId";
    private const string SeasonStorageKey = "wec.races.selectedSeasonId";

    [Inject] private IRaceService RaceService { get; set; } = default!;

    [Inject] private IChampionshipService ChampionshipService { get; set; } = default!;

    [Inject] private ISeasonService SeasonService { get; set; } = default!;

    [Inject] private IPointSettingService PointSettingService { get; set; } = default!;

    [Inject] private IClassSettingService ClassSettingService { get; set; } = default!;

    [Inject] private ITrackService TrackService { get; set; } = default!;

    [Inject] private BrowserStorage Storage { get; set; } = default!;

    [Inject] private NavigationManager Nav { get; set; } = default!;

    private readonly SortState<SortColumn> sort = new(SortColumn.Race);

    private List<ChampionshipModel>? championships;
    private ChampionshipModel? selectedChampionship;
    private List<SeasonModel> seasons = new();
    private SeasonModel? selectedSeason;
    private List<RaceModel>? races;
    private List<PointSettingModel> pointSettings = new();
    private List<ClassSettingModel> classSettings = new();
    private ClassColorMap classColors = ClassColorMap.Empty;
    private List<TrackOptionModel> availableTracks = new();
    private bool preferencesRestored;

    private RaceFormModel? editingRace;
    private List<TrackLayoutOptionModel> editingLayouts = new();
    private int? editingLapTimeMs;
    private bool isCreatingRace;
    private bool isSavingRace;
    private RaceModel? confirmDeleteRace;
    private bool isDeletingRace;
    private RaceModel? viewingSessionsFor;
    private bool isOpeningStandings;
    private List<DriverStandingsModel>? driverStandings;

    private string? SelectedChampionshipId => selectedChampionship?.ChampionshipId.ToString();

    private string? SelectedSeasonId => selectedSeason?.SeasonId.ToString();

    private bool HasSeason => selectedSeason is not null;

    private IEnumerable<SelectOption> ChampionshipOptions =>
        (championships ?? new()).Select(c => new SelectOption(c.ChampionshipId.ToString(), c.ChampionshipName));

    private IEnumerable<SelectOption> SeasonOptions =>
        seasons.Select(s => new SelectOption(s.SeasonId.ToString(), s.SeasonName));

    private IEnumerable<RaceModel> DisplayRaces
    {
        get
        {
            if (races is null)
            {
                return Array.Empty<RaceModel>();
            }

            return sort.Column switch
            {
                SortColumn.Race => sort.Apply(races, r => r.RaceName),
                SortColumn.Status => sort.Apply(races, r => r.Status),
                _ => races
            };
        }
    }

    protected override async Task OnInitializedAsync()
    {
        championships = (await ChampionshipService.GetActiveChampionshipsAsync()).ToList();
        pointSettings = (await PointSettingService.GetAllAsync()).ToList();
        classSettings = (await ClassSettingService.GetAllAsync()).ToList();
        classColors = ClassColorMap.From(classSettings);
        availableTracks = (await TrackService.GetTrackOptionsAsync()).ToList();

        var query = QueryHelpers.ParseQuery(Nav.ToAbsoluteUri(Nav.Uri).Query);

        if (query.TryGetValue("championshipId", out var championshipId) &&
            Guid.TryParse(championshipId.ToString(), out var parsedChampionshipId))
        {
            selectedChampionship = championships.FirstOrDefault(c => c.ChampionshipId == parsedChampionshipId);
            await LoadSeasonsAsync();

            if (query.TryGetValue("seasonId", out var seasonId) &&
                Guid.TryParse(seasonId.ToString(), out var parsedSeasonId))
            {
                selectedSeason = seasons.FirstOrDefault(s => s.SeasonId == parsedSeasonId);
            }

            if (HasSeason)
            {
                await LoadRacesAsync();
            }

            preferencesRestored = true;
            await PersistSelectionAsync();
        }
        else
        {
            await RestoreSelectionFromStorageAsync();
        }
    }

    private async Task RestoreSelectionFromStorageAsync()
    {
        preferencesRestored = true;

        var storedChampionshipId = await Storage.GetAsync(ChampionshipStorageKey);
        var storedSeasonId = await Storage.GetAsync(SeasonStorageKey);

        if (!Guid.TryParse(storedChampionshipId, out var parsedChampionshipId))
        {
            await ClearStoredSelectionAsync();
            return;
        }

        selectedChampionship = championships?.FirstOrDefault(c => c.ChampionshipId == parsedChampionshipId);
        if (selectedChampionship is null)
        {
            await ClearStoredSelectionAsync();
            return;
        }

        await LoadSeasonsAsync();

        if (Guid.TryParse(storedSeasonId, out var parsedSeasonId))
        {
            selectedSeason = seasons.FirstOrDefault(s => s.SeasonId == parsedSeasonId);
        }

        if (selectedSeason is not null)
        {
            await LoadRacesAsync();
        }
        else
        {
            await PersistSelectionAsync();
        }
    }

    private async Task PersistSelectionAsync()
    {
        if (!preferencesRestored)
        {
            return;
        }

        await Storage.SetAsync(ChampionshipStorageKey, SelectedChampionshipId ?? "");
        await Storage.SetAsync(SeasonStorageKey, SelectedSeasonId ?? "");
    }

    private async Task ClearStoredSelectionAsync()
    {
        await Storage.RemoveAsync(ChampionshipStorageKey);
        await Storage.RemoveAsync(SeasonStorageKey);
    }

    private async Task HandleChampionshipChanged(string value)
    {
        selectedSeason = null;
        races = null;
        seasons = new List<SeasonModel>();

        if (Guid.TryParse(value, out var parsedId))
        {
            selectedChampionship = championships?.FirstOrDefault(c => c.ChampionshipId == parsedId);
            await LoadSeasonsAsync();
        }
        else
        {
            selectedChampionship = null;
        }

        await PersistSelectionAsync();
    }

    private async Task HandleSeasonChanged(string value)
    {
        selectedSeason = null;
        races = null;

        if (Guid.TryParse(value, out var parsedId))
        {
            selectedSeason = seasons.FirstOrDefault(s => s.SeasonId == parsedId);
        }

        if (HasSeason)
        {
            await LoadRacesAsync();
        }

        await PersistSelectionAsync();
    }

    private async Task LoadSeasonsAsync()
    {
        seasons = selectedChampionship is null
            ? new List<SeasonModel>()
            : (await SeasonService.GetSeasonsByChampionshipAsync(selectedChampionship.ChampionshipId)).ToList();
    }

    private async Task LoadRacesAsync()
    {
        races = selectedSeason is null
            ? null
            : (await RaceService.GetRacesBySeasonAsync(selectedSeason.SeasonId)).ToList();
    }

    private async Task OpenStandings()
    {
        if (selectedSeason is null)
        {
            return;
        }

        driverStandings = (await SeasonService.GetDriverStandingsAsync(selectedSeason.SeasonId)).ToList();
        isOpeningStandings = true;
    }

    private void OpenRaceCreate()
    {
        if (selectedSeason is null)
        {
            return;
        }

        isCreatingRace = true;
        editingLayouts = new List<TrackLayoutOptionModel>();
        editingLapTimeMs = null;
        editingRace = new RaceFormModel
        {
            RaceId = Guid.NewGuid(),
            RaceName = string.Empty,
            SeasonId = selectedSeason.SeasonId,
            PointSettingId = pointSettings.FirstOrDefault(p => p.IsDefault)?.SettingId ?? Guid.Empty,
            ClassSettingId = classSettings.FirstOrDefault(c => c.IsDefault)?.ClassSettingId ?? Guid.Empty,
            NumberOfLaps = 10,
            PracticeSessionMinutes = 20,
            QualifyingSessionMinutes = 20
        };
    }

    private async Task OpenRaceEdit(RaceModel race)
    {
        if (selectedSeason is null)
        {
            return;
        }

        var form = new RaceFormModel
        {
            RaceId = race.RaceId,
            RaceName = race.RaceName,
            Country = race.Country,
            SeasonId = selectedSeason.SeasonId,
            PointSettingId = race.PointSettingId,
            ClassSettingId = race.ClassSettingId,
            TrackName = race.TrackName,
            TrackLayout = race.TrackLayout,
            NumberOfLaps = race.NumberOfLaps,
            PracticeSessionMinutes = race.PracticeSessionMinutes,
            QualifyingSessionMinutes = race.QualifyingSessionMinutes
        };

        // Loaded before the dialog opens so the saved layout is already in the list when it first renders.
        var hasTrack = !string.IsNullOrWhiteSpace(form.TrackName);
        editingLayouts = hasTrack
            ? (await TrackService.GetTrackLayoutOptionsAsync(form.TrackName!)).ToList()
            : new List<TrackLayoutOptionModel>();
        editingLapTimeMs = hasTrack
            ? await TrackService.GetBestLapTimeAsync(
                form.TrackName!,
                string.IsNullOrWhiteSpace(form.TrackLayout) ? null : form.TrackLayout)
            : null;

        isCreatingRace = false;
        editingRace = form;
    }

    private void CancelRaceEdit()
    {
        if (isSavingRace)
        {
            return;
        }

        editingRace = null;
        isCreatingRace = false;
    }

    private async Task SaveRaceAsync()
    {
        if (editingRace is null)
        {
            return;
        }

        isSavingRace = true;

        try
        {
            var form = editingRace;
            var creating = isCreatingRace;

            var saved = await TryAsync(async () =>
            {
                if (creating)
                {
                    await RaceService.CreateRaceAsync(form);
                }
                else
                {
                    await RaceService.UpdateRaceAsync(form);
                }

                await LoadRacesAsync();
            }, "Could not save race");

            if (saved)
            {
                isSavingRace = false;
                Toast.Show(creating ? "Race created." : "Race updated.");
                CancelRaceEdit();
            }
        }
        finally
        {
            isSavingRace = false;
        }
    }

    private async Task DeleteRaceAsync()
    {
        if (confirmDeleteRace is null)
        {
            return;
        }

        isDeletingRace = true;

        try
        {
            var target = confirmDeleteRace;

            await TryAsync(async () =>
            {
                await RaceService.DeleteRaceAsync(target.RaceId);
                confirmDeleteRace = null;
                await LoadRacesAsync();
                Toast.Show($"Race '{target.RaceName}' deleted.");
            }, "Could not delete race");
        }
        finally
        {
            isDeletingRace = false;
        }
    }

    private async Task CloseSessionsAsync()
    {
        viewingSessionsFor = null;
        await LoadRacesAsync();
    }
}
