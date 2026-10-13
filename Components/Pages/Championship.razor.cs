using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MulticlassRace.Common;
using MulticlassRace.Services.Abstractions;
using MulticlassRace.ViewModels;

namespace multi_class_race_dashboard.Components.Pages;

public partial class Championship
{
    private enum SortColumn
    {
        Championship,
        Status
    }

    [Inject] private IChampionshipService ChampionshipService { get; set; } = default!;

    [Inject] private ISeasonService SeasonService { get; set; } = default!;

    [Inject] private NavigationManager Nav { get; set; } = default!;

    private readonly SortState<SortColumn> sort = new(SortColumn.Championship);

    private List<ChampionshipModel>? championships;
    private ChampionshipFormModel? editingChampionship;
    private bool isCreating;
    private bool isSaving;
    private ChampionshipModel? confirmDeleteChampionship;
    private bool isDeleting;
    private ChampionshipModel? selectedChampionship;
    private List<SeasonModel>? seasons;
    private string? newSeasonName;
    private bool isAddingSeason;
    private SeasonModel? confirmDeleteSeason;
    private bool isDeletingSeason;
    private SeasonModel? cloningSeason;
    private string? cloneSeasonName;
    private bool isCloningSeason;

    private IEnumerable<ChampionshipModel> DisplayChampionships
    {
        get
        {
            if (championships is null)
            {
                return Array.Empty<ChampionshipModel>();
            }

            return sort.Column switch
            {
                SortColumn.Championship => sort.Apply(championships, c => c.ChampionshipName),
                SortColumn.Status => sort.Apply(championships, c => c.IsActive),
                _ => championships
            };
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadChampionshipsAsync();
    }

    private async Task LoadChampionshipsAsync()
    {
        championships = (await ChampionshipService.GetActiveChampionshipsAsync()).ToList();
    }

    private void OpenCreate()
    {
        isCreating = true;
        editingChampionship = new ChampionshipFormModel
        {
            ChampionshipId = Guid.NewGuid(),
            ChampionshipName = string.Empty,
            IsActive = true
        };
    }

    private void OpenEdit(ChampionshipModel championship)
    {
        isCreating = false;
        editingChampionship = new ChampionshipFormModel
        {
            ChampionshipId = championship.ChampionshipId,
            ChampionshipName = championship.ChampionshipName,
            IsActive = championship.IsActive
        };
    }

    private void CancelEdit()
    {
        if (isSaving)
        {
            return;
        }

        editingChampionship = null;
        isCreating = false;
    }

    private async Task SaveChampionshipAsync()
    {
        if (editingChampionship is null
            || !RequireText(editingChampionship.ChampionshipName, "Championship name is required."))
        {
            return;
        }

        isSaving = true;

        try
        {
            var form = editingChampionship;
            var creating = isCreating;
            var saved = await TryAsync(async () =>
            {
                if (creating)
                {
                    await ChampionshipService.CreateChampionshipAsync(form);
                }
                else
                {
                    await ChampionshipService.UpdateChampionshipAsync(form);
                }

                await LoadChampionshipsAsync();
            }, "Could not save championship");

            if (saved)
            {
                isSaving = false;
                Toast.Show(creating ? "Championship created." : "Championship updated.");
                CancelEdit();
            }
        }
        finally
        {
            isSaving = false;
        }
    }

    private async Task DeleteChampionshipAsync()
    {
        if (confirmDeleteChampionship is null)
        {
            return;
        }

        isDeleting = true;

        try
        {
            var target = confirmDeleteChampionship;
            await TryAsync(async () =>
            {
                await ChampionshipService.DeleteChampionshipAsync(target.ChampionshipId);
                confirmDeleteChampionship = null;
                await LoadChampionshipsAsync();
                Toast.Show($"Championship '{target.ChampionshipName}' deleted.");
            }, "Could not delete championship");
        }
        finally
        {
            isDeleting = false;
        }
    }

    private async Task OpenSeasons(ChampionshipModel championship)
    {
        selectedChampionship = championship;
        newSeasonName = null;
        seasons = null;

        await LoadSeasonsAsync();
    }

    private async Task LoadSeasonsAsync()
    {
        if (selectedChampionship is null)
        {
            return;
        }

        seasons = (await SeasonService.GetSeasonsByChampionshipAsync(selectedChampionship.ChampionshipId)).ToList();
    }

    private void CloseSeasons()
    {
        if (!isAddingSeason && !isDeletingSeason && !isCloningSeason)
        {
            selectedChampionship = null;
            seasons = null;
            newSeasonName = null;
        }
    }

    private void NavigateToRaces(SeasonModel season)
    {
        if (selectedChampionship is null)
        {
            return;
        }

        Nav.NavigateTo($"/races?championshipId={selectedChampionship.ChampionshipId}&seasonId={season.SeasonId}");
    }

    private async Task HandleNewSeasonEnter(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await AddSeasonAsync();
        }
    }

    private async Task AddSeasonAsync()
    {
        if (selectedChampionship is null || !RequireText(newSeasonName, "Season name is required."))
        {
            return;
        }

        isAddingSeason = true;

        try
        {
            var championship = selectedChampionship;
            await TryAsync(async () =>
            {
                await SeasonService.CreateSeasonAsync(championship.ChampionshipId, newSeasonName!);
                newSeasonName = null;
                await LoadSeasonsAsync();
                await LoadChampionshipsAsync();
                Toast.Show($"Season added to '{championship.ChampionshipName}'.");
            }, "Could not add season");
        }
        finally
        {
            isAddingSeason = false;
        }
    }

    private async Task DeleteSeasonAsync()
    {
        if (confirmDeleteSeason is null)
        {
            return;
        }

        isDeletingSeason = true;

        try
        {
            var target = confirmDeleteSeason;
            await TryAsync(async () =>
            {
                await SeasonService.DeleteSeasonAsync(target.SeasonId);
                confirmDeleteSeason = null;
                await LoadSeasonsAsync();
                await LoadChampionshipsAsync();
                Toast.Show($"Season '{target.SeasonName}' deleted.");
            }, "Could not delete season");
        }
        finally
        {
            isDeletingSeason = false;
        }
    }

    private void OpenCloneDialog(SeasonModel season)
    {
        cloningSeason = season;
        cloneSeasonName = $"{season.SeasonName} (copy)";
    }

    private void CloseCloneDialog()
    {
        if (!isCloningSeason)
        {
            cloningSeason = null;
            cloneSeasonName = null;
        }
    }

    private async Task HandleCloneNameEnter(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await CloneSeasonAsync();
        }
    }

    private async Task CloneSeasonAsync()
    {
        if (cloningSeason is null || !RequireText(cloneSeasonName, "Season name is required."))
        {
            return;
        }

        isCloningSeason = true;

        try
        {
            var source = cloningSeason;
            var name = cloneSeasonName!;
            await TryAsync(async () =>
            {
                await SeasonService.CloneSeasonAsync(source.SeasonId, name);
                cloningSeason = null;
                cloneSeasonName = null;
                await LoadSeasonsAsync();
                await LoadChampionshipsAsync();
                Toast.Show($"Season '{name}' cloned.");
            }, "Could not clone season");
        }
        finally
        {
            isCloningSeason = false;
        }
    }
}
