using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MulticlassRace.Models;
using MulticlassRace.ViewModels;
using MulticlassRace.Services.AssettoCorsa;
using MulticlassRace.Services.Notifications;
using MulticlassRace.Services.Races;
using MulticlassRace.Services.Results;
using MulticlassRace.Services.Teams;

namespace multi_class_race_dashboard.Components.Features.Races;

public partial class RaceSessionsModal : IDisposable
{
    private const long MaxResultFileBytes = 2 * 1024 * 1024;

    [Inject] private IRaceService RaceService { get; set; } = default!;

    [Inject] private IRaceSessionLauncher SessionLauncher { get; set; } = default!;

    [Inject] private IRaceResultService ResultService { get; set; } = default!;

    [Inject] private IGridPresetService PresetService { get; set; } = default!;

    [Inject] private ITeamService TeamService { get; set; } = default!;

    [Inject] private ITrackService TrackService { get; set; } = default!;

    [Inject] private ILuaResultImportService LuaResultImportService { get; set; } = default!;

    [Inject] private RaceSessionChangeNotifier RaceSessionChangeNotifier { get; set; } = default!;

    [Inject] private ToastService Toast { get; set; } = default!;

    [Parameter, EditorRequired] public RaceModel Race { get; set; } = default!;

    [Parameter] public IReadOnlyList<ClassSettingModel> ClassSettings { get; set; } = Array.Empty<ClassSettingModel>();

    [Parameter] public ClassColorMap Colors { get; set; } = ClassColorMap.Empty;

    [Parameter] public string? ChampionshipName { get; set; }

    [Parameter] public string? SeasonName { get; set; }

    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>Raised when the race could not be loaded at all and the dialog should go away.</summary>
    [Parameter] public EventCallback OnLoadFailed { get; set; }

    /// <summary>Raised when a race result lands from outside this dialog, so the race list can refresh its status.</summary>
    [Parameter] public EventCallback OnRaceResultChanged { get; set; }

    private readonly SessionConditionSet conditions = new();

    private bool isLoading = true;
    private RacePopupTab selectedTab = RacePopupTab.Race;
    private SessionType selectedSessionType = SessionType.Race;
    private List<SessionModel>? sessionsCache;
    private List<TeamModel> availableTeams = new();
    private HashSet<Guid> selectedTeamIds = new();
    private bool isSavingTeams;
    private string teamClassFilter = "";
    private string standingClassFilter = "";
    private List<RaceResultFileModel>? raceResultFiles;
    private ResultImportAvailabilityModel availability = new();
    private string? selectedRaceResultFile;
    private bool isImportingRaceResult;
    private bool isImportingSessionResult;
    private bool isStarting;
    private int? trackPitCount;
    private int enteredCarCount;

    private RaceSessionContext Context => new(
        selectedTeamIds.Count > 0,
        sessionsCache,
        Race.PracticeSessionMinutes,
        Race.TrackName,
        trackPitCount,
        enteredCarCount,
        selectedSessionType);

    private ClassSettingModel? RaceClassSetting => ClassSettings.FirstOrDefault(s => s.ClassSettingId == Race.ClassSettingId);

    private RaceTeamFilter TeamFilter => new(RaceClassSetting?.ClassSettingId);

    private IReadOnlyList<SessionModel>? DisplaySessions =>
        sessionsCache?.Where(s => s.SessionType == selectedSessionType).ToList();

    private IReadOnlyList<string> StandingClassNames =>
        StandingsFilter.SessionClassNames(DisplaySessions ?? new List<SessionModel>());

    private bool CanImportRaceResult =>
        !isImportingRaceResult && !string.IsNullOrWhiteSpace(selectedRaceResultFile);

    protected override async Task OnInitializedAsync()
    {
        RaceSessionChangeNotifier.Imported += OnLuaResultImported;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        isLoading = true;

        try
        {
            selectedTeamIds = (await RaceService.GetRaceTeamsAsync(Race.RaceId)).Select(t => t.TeamId).ToHashSet();
            availableTeams = (await TeamService.GetActiveTeamsAsync()).ToList();
            sessionsCache = (await RaceService.GetRaceSessionsAsync(Race.RaceId)).ToList();

            await RefreshPitCapacityAsync();

            (selectedTab, selectedSessionType) = Context.InitialTab();

            // Which of these controls can do anything depends on whether Content Manager is installed,
            // so the availability is read once per dialog rather than guessed per click.
            availability = await ResultService.GetResultImportAvailabilityAsync();

            if (availability.CanImportResults)
            {
                await LoadResultFilesAsync();
            }
            else
            {
                raceResultFiles = new List<RaceResultFileModel>();
                selectedRaceResultFile = null;
            }
        }
        catch (Exception ex)
        {
            Toast.Show($"Could not load race: {ex.Message}", isError: true);
            await OnLoadFailed.InvokeAsync();
        }
        finally
        {
            isLoading = false;
        }
    }

    private void OnLuaResultImported(Guid raceId, SessionType sessionType)
    {
        _ = InvokeAsync(async () =>
        {
            if (Race.RaceId != raceId)
            {
                return;
            }

            try
            {
                sessionsCache = (await RaceService.GetRaceSessionsAsync(raceId)).ToList();

                if (sessionType == SessionType.Race)
                {
                    await OnRaceResultChanged.InvokeAsync();
                }

                StateHasChanged();
            }
            catch (Exception ex)
            {
                Toast.Show($"Could not refresh imported session results: {ex.Message}", isError: true);
            }
        });
    }

    private async Task RefreshPitCapacityAsync()
    {
        var race = await RaceService.GetRaceByIdAsync(Race.RaceId);

        if (race is null)
        {
            return;
        }

        Race.RaceName = race.RaceName;
        Race.TrackName = race.TrackName;
        Race.TrackLayout = race.TrackLayout;

        trackPitCount = string.IsNullOrWhiteSpace(race.TrackName)
            ? null
            : await TrackService.GetPitCountAsync(
                race.TrackName,
                string.IsNullOrWhiteSpace(race.TrackLayout) ? null : race.TrackLayout);

        enteredCarCount = await RaceService.GetEnteredCarCountAsync(Race.RaceId);
    }

    private async Task LoadResultFilesAsync()
    {
        raceResultFiles = (await ResultService.GetRaceResultFilesAsync()).ToList();
        selectedRaceResultFile = raceResultFiles.FirstOrDefault()?.FullPath;
    }

    private void SetTeamsTab()
    {
        if (!Context.HasSessionData)
        {
            selectedTab = RacePopupTab.Teams;
        }
    }

    private void SetSessionTab(SessionType type)
    {
        var context = Context;

        if (type == SessionType.Practice && !context.CanOpenPractice
            || type == SessionType.Qualifying && !context.CanOpenQualifying
            || type == SessionType.Race && !context.CanOpenRace)
        {
            return;
        }

        selectedTab = type switch
        {
            SessionType.Practice => RacePopupTab.Practice,
            SessionType.Qualifying => RacePopupTab.Qualifying,
            _ => RacePopupTab.Race
        };
        selectedSessionType = type;
        standingClassFilter = "";
    }

    private async Task ToggleTeamAsync(Guid teamId)
    {
        if (isSavingTeams)
        {
            return;
        }

        if (!selectedTeamIds.Add(teamId))
        {
            selectedTeamIds.Remove(teamId);
        }

        await SaveTeamsAsync();
    }

    private async Task ToggleAllVisibleAsync()
    {
        var visible = TeamFilter.Filter(availableTeams, teamClassFilter);

        if (isSavingTeams || visible.Count == 0)
        {
            return;
        }

        var checkAll = !visible.All(t => selectedTeamIds.Contains(t.TeamId));

        foreach (var team in visible)
        {
            if (checkAll)
            {
                selectedTeamIds.Add(team.TeamId);
            }
            else
            {
                selectedTeamIds.Remove(team.TeamId);
            }
        }

        await SaveTeamsAsync();
    }

    private async Task SaveTeamsAsync()
    {
        isSavingTeams = true;

        try
        {
            await RaceService.SaveRaceTeamsAsync(Race.RaceId, selectedTeamIds);
        }
        catch
        {
            Toast.Show("Failed to update race teams.", isError: true);
            selectedTeamIds = (await RaceService.GetRaceTeamsAsync(Race.RaceId)).Select(t => t.TeamId).ToHashSet();
            await RefreshPitCapacityAsync();
        }
        finally
        {
            isSavingTeams = false;
        }
    }

    private async Task StartSessionAsync()
    {
        if (!Context.CanStartSession)
        {
            return;
        }

        isStarting = true;

        try
        {
            var current = conditions.Get(selectedSessionType);

            await SessionLauncher.LaunchSessionAsync(
                Race.RaceId,
                selectedSessionType,
                current.Weather,
                current.SunAngle,
                current.Penalties);
            Toast.Show($"Started {RaceFormat.SessionTypeLabel(selectedSessionType)} session in Assetto Corsa.");
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

    private async Task ImportRaceResultAsync()
    {
        if (!CanImportRaceResult)
        {
            return;
        }

        var selectedFile = raceResultFiles?.FirstOrDefault(f => f.FullPath == selectedRaceResultFile);

        if (selectedFile is null)
        {
            Toast.Show("That result file is no longer available. Reload the list and try again.", isError: true);
            return;
        }

        isImportingRaceResult = true;

        try
        {
            // Files the Lua app produced live outside the Content Manager results folder,
            // so they are imported by full path instead of by file name.
            var result = selectedFile.IsLuaResult
                ? await ResultService.ImportRaceResultFromPathAsync(Race.RaceId, selectedFile.FullPath, selectedFile.FileName)
                : await ResultService.ImportRaceResultAsync(Race.RaceId, selectedFile.FileName);

            if (selectedFile.IsLuaResult)
            {
                // Record it so the auto-import worker does not offer the same file again.
                await LuaResultImportService.RecordManualImportAsync(selectedFile.FullPath, selectedFile.FileName);
            }

            Toast.Show(
                $"Imported {result.Imported} result(s) (skipped {result.Skipped}) from {selectedFile.FileName}.",
                result.Skipped > 0);

            selectedSessionType = SessionType.Race;
            selectedTab = RacePopupTab.Race;
            sessionsCache = (await RaceService.GetRaceSessionsAsync(Race.RaceId)).ToList();
        }
        catch (Exception ex)
        {
            Toast.Show(ex.Message, isError: true);
        }
        finally
        {
            isImportingRaceResult = false;
        }
    }

    private async Task HandleSessionResultImport(InputFileChangeEventArgs e)
    {
        if (e.File is null)
        {
            return;
        }

        isImportingSessionResult = true;

        try
        {
            await using var stream = e.File.OpenReadStream(MaxResultFileBytes);
            var result = await ResultService.ImportSessionResultAsync(Race.RaceId, selectedSessionType, stream, e.File.Name);

            Toast.Show($"Imported {result.Imported} result(s) (skipped {result.Skipped}).", result.Skipped > 0);

            sessionsCache = (await RaceService.GetRaceSessionsAsync(Race.RaceId)).ToList();
        }
        catch (Exception ex)
        {
            Toast.Show(ex.Message, isError: true);
        }
        finally
        {
            isImportingSessionResult = false;
        }
    }

    private async Task<PresetExportResult> ExportGridPresetAsync(bool humanClassOnly)
    {
        RequireChampionshipAndSeason();

        var sessionType = selectedTab switch
        {
            RacePopupTab.Practice => SessionType.Practice,
            RacePopupTab.Qualifying => SessionType.Qualifying,
            _ => SessionType.Race
        };

        return await PresetService.ExportGridPresetAsync(
            Race.RaceId,
            sessionType,
            humanClassOnly,
            SeasonName!,
            ChampionshipName!);
    }

    private async Task ExportRacePresetAsync()
    {
        RequireChampionshipAndSeason();

        var result = await PresetService.ExportRaceGridPresetAsync(Race.RaceId, SeasonName!, ChampionshipName!);

        Toast.Show($"Preset exported ({result.CarCount} cars): {result.FilePath}");
    }

    private void RequireChampionshipAndSeason()
    {
        if (string.IsNullOrEmpty(ChampionshipName) || string.IsNullOrEmpty(SeasonName))
        {
            throw new InvalidOperationException("No championship or season selected.");
        }
    }

    public void Dispose()
    {
        RaceSessionChangeNotifier.Imported -= OnLuaResultImported;
    }
}
