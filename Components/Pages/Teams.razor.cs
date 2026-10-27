using Microsoft.AspNetCore.Components;
using MulticlassRace.Common;
using MulticlassRace.ViewModels;
using MulticlassRace.Services.Drivers;
using MulticlassRace.Services.Settings;
using MulticlassRace.Services.Teams;

namespace multi_class_race_dashboard.Components.Pages;

public partial class Teams
{
    private const int PageSize = 10;

    [Inject] private ITeamService TeamService { get; set; } = default!;

    [Inject] private IClassSettingService ClassSettingService { get; set; } = default!;

    [Inject] private IDriverService DriverService { get; set; } = default!;

    [Inject] private DriverDataChangeNotifier DriverNotifier { get; set; } = default!;

    private readonly SortState<TeamSortColumn> sort = new(TeamSortColumn.Team);

    private List<TeamModel>? teams;
    private List<DriverModel> activeDrivers = new();
    private List<ClassItemOption> classItems = new();
    private TeamFormModel? editingTeam;
    private List<DriverModel>? editingDrivers;
    private bool isCreating;
    private bool isSaving;
    private int currentPage = 1;
    private string selectedClassFilter = "";
    private string searchTerm = "";

    private IEnumerable<SelectOption> ClassFilterOptions =>
        TeamListQuery.ClassNames(teams ?? new()).Select(name => new SelectOption(name, name));

    private PagedList<TeamModel> PagedTeams
    {
        get
        {
            var filtered = TeamListQuery.Apply(teams ?? new(), activeDrivers, selectedClassFilter, searchTerm, sort);
            var page = PagedList<TeamModel>.Create(filtered, currentPage, PageSize);
            currentPage = page.Page;
            return page;
        }
    }

    protected override async Task OnInitializedAsync()
    {
        teams = (await TeamService.GetActiveTeamsAsync()).ToList();
        classItems = ClassItemOption.FromSettings(await ClassSettingService.GetAllAsync());
        activeDrivers = (await DriverService.GetActiveDriversAsync()).ToList();
        currentPage = 1;
    }

    private void HandleClassFilterChanged(string value)
    {
        selectedClassFilter = value;
        currentPage = 1;
    }

    private void HandleSearchChanged(string value)
    {
        searchTerm = value;
        currentPage = 1;
    }

    private void OpenCreate()
    {
        isCreating = true;
        editingDrivers = new List<DriverModel>();
        editingTeam = new TeamFormModel
        {
            TeamId = Guid.NewGuid(),
            TeamName = string.Empty,
            IsActive = true,
            ClassSettingItemIds = new List<Guid>()
        };
    }

    private async Task OpenEdit(TeamModel team)
    {
        isCreating = false;
        editingDrivers = null;
        editingTeam = new TeamFormModel
        {
            TeamId = team.TeamId,
            TeamName = team.TeamName,
            TeamLogo = team.TeamLogo,
            IsActive = team.IsActive,
            ClassSettingItemIds = team.ClassTags.Select(t => t.ClassSettingItemId).ToList()
        };

        var detail = await TeamService.GetTeamByIdAsync(team.TeamId);

        // The user may have closed or switched the dialog while the drivers were loading.
        if (editingTeam is null || editingTeam.TeamId != team.TeamId)
        {
            return;
        }

        if (detail is null)
        {
            Toast.Show("Team could not be loaded.", isError: true);
            editingDrivers = new List<DriverModel>();
            return;
        }

        editingDrivers = detail.Drivers;
    }

    private void CancelEdit()
    {
        if (isSaving)
        {
            return;
        }

        editingTeam = null;
        editingDrivers = null;
        isCreating = false;
    }

    private async Task SaveTeamAsync()
    {
        if (editingTeam is null || editingDrivers is null)
        {
            return;
        }

        if (!RequireText(editingTeam.TeamName, "Team name is required."))
        {
            return;
        }

        if (editingTeam.ClassSettingItemIds.Count == 0)
        {
            Toast.Show("Please select at least one team class.", isError: true);
            return;
        }

        isSaving = true;

        try
        {
            var team = editingTeam;
            var creating = isCreating;
            var driverIds = editingDrivers.Select(d => d.DriverId).ToList();

            var saved = await TryAsync(async () =>
            {
                if (creating)
                {
                    await TeamService.CreateTeamAsync(team, driverIds);
                }
                else
                {
                    await TeamService.UpdateTeamAsync(team, driverIds);
                }

                teams = (await TeamService.GetActiveTeamsAsync()).ToList();
                activeDrivers = (await DriverService.GetActiveDriversAsync()).ToList();
            }, "Could not save team");

            if (saved)
            {
                if (creating)
                {
                    currentPage = 1;
                    searchTerm = "";
                }

                isSaving = false;
                DriverNotifier.NotifyChanged();
                Toast.Show(creating ? "Team created." : "Team updated.");
                CancelEdit();
            }
        }
        finally
        {
            isSaving = false;
        }
    }
}
