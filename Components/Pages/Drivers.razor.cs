using Microsoft.AspNetCore.Components;
using MulticlassRace.Common;
using MulticlassRace.ViewModels;
using multi_class_race_dashboard.Components.Shared;
using MulticlassRace.Services.AssettoCorsa;
using MulticlassRace.Services.Drivers;
using MulticlassRace.Services.Teams;

namespace multi_class_race_dashboard.Components.Pages;

public partial class Drivers : IDisposable
{
    private const int PageSize = 10;

    [Inject] private IDriverService DriverService { get; set; } = default!;

    [Inject] private ITeamService TeamService { get; set; } = default!;

    [Inject] private IAssettoCorsaGameConfigService ConfigService { get; set; } = default!;

    [Inject] private IAssettoCorsaContentService CarContentService { get; set; } = default!;

    [Inject] private DriverDataChangeNotifier DriverNotifier { get; set; } = default!;

    [Inject] private GameConfigChangeNotifier ConfigNotifier { get; set; } = default!;

    private readonly SortState<DriverSortColumn> sort = new(DriverSortColumn.Driver);

    private List<DriverModel>? drivers;
    private List<TeamModel> teamOptions = new();
    private List<CarOptionModel> carOptions = new();
    private DriverFormModel? editingDriver;
    private bool isCreating;
    private bool isSaving;
    private bool isImporting;
    private bool isLoadingCars;
    private DriverModel? confirmDeleteDriver;
    private bool isDeleting;
    private DriverModel? viewingCar;
    private string? viewingCarDisplayName;
    private int currentPage = 1;
    private string selectedClassFilter = "";
    private string searchTerm = "";
    private string presetPath = "";
    private FolderPicker presetPicker = default!;

    private bool isBatchMode;
    private bool showBatchModal;
    private readonly HashSet<Guid> selectedIds = new();

    private DriverModel? HumanDriver => drivers?.FirstOrDefault(d => d.IsHuman);

    private bool CanToggleHuman =>
        HumanDriver is null ||
        (editingDriver is not null && !isCreating && editingDriver.DriverId == HumanDriver.DriverId);

    private IEnumerable<SelectOption> ClassFilterOptions
    {
        get
        {
            var options = DriverListQuery.ClassNames(drivers ?? new()).Select(name => new SelectOption(name, name));

            return drivers is not null && DriverListQuery.HasUncategorized(drivers)
                ? options.Append(new SelectOption(DriverListQuery.UncategorizedKey, "Uncategorized"))
                : options;
        }
    }

    private PagedList<DriverModel> PagedDrivers
    {
        get
        {
            var filtered = DriverListQuery.Apply(drivers ?? new(), selectedClassFilter, searchTerm, sort);
            var page = PagedList<DriverModel>.Create(filtered, currentPage, PageSize);
            currentPage = page.Page;
            return page;
        }
    }

    protected override void OnInitialized()
    {
        ConfigNotifier.Changed += OnConfigChanged;
    }

    protected override async Task OnInitializedAsync()
    {
        var config = await ConfigService.GetAsync();
        presetPath = ContentManagerPaths.ResolvePreset(config?.PresetPath);
        teamOptions = (await TeamService.GetActiveTeamsAsync()).ToList();
        await LoadCarsAsync();
        await LoadDriversAsync();
    }

    private async void OnConfigChanged()
    {
        try
        {
            var config = await ConfigService.GetAsync();
            presetPath = ContentManagerPaths.ResolvePreset(config?.PresetPath);
            await LoadCarsAsync();
            await InvokeAsync(StateHasChanged);
        }
        catch
        {
            // A failed refresh leaves the previous lists in place; the next change tries again.
        }
    }

    private async Task LoadCarsAsync()
    {
        isLoadingCars = true;

        try
        {
            carOptions = (await CarContentService.GetCarOptionsAsync()).ToList();
        }
        catch (Exception ex)
        {
            Toast.Show($"Could not read car list: {ex.Message}", isError: true);
        }
        finally
        {
            isLoadingCars = false;
        }
    }

    private async Task LoadDriversAsync(bool resetView = true)
    {
        drivers = (await DriverService.GetActiveDriversAsync()).ToList();

        if (resetView)
        {
            currentPage = 1;
            searchTerm = "";
        }
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

    private void HandleBatchButton()
    {
        if (!isBatchMode)
        {
            isBatchMode = true;
        }
        else if (selectedIds.Count == 0)
        {
            isBatchMode = false;
        }
        else
        {
            showBatchModal = true;
        }
    }

    private void ToggleSelected(Guid id, bool selected)
    {
        if (selected)
        {
            selectedIds.Add(id);
        }
        else
        {
            selectedIds.Remove(id);
        }
    }

    private bool AllPageSelected(PagedList<DriverModel> paged) =>
        paged.Items.Any() && paged.Items.All(d => selectedIds.Contains(d.DriverId));

    private void ToggleAll(PagedList<DriverModel> paged, bool selected)
    {
        foreach (var driver in paged.Items)
        {
            ToggleSelected(driver.DriverId, selected);
        }
    }

    private async Task SaveBatchAsync((int? Aggression, int? Strength) values)
    {
        if (values.Aggression is null && values.Strength is null)
        {
            Toast.Show("Enter an AI strength or aggression to apply.", isError: true);
            return;
        }

        isSaving = true;

        try
        {
            var ids = selectedIds.ToList();

            var saved = await TryAsync(async () =>
            {
                await DriverService.BatchUpdateAiAsync(ids, values.Aggression, values.Strength);
                await LoadDriversAsync();
            }, "Could not save drivers");

            if (saved)
            {
                DriverNotifier.NotifyChanged();
                Toast.Show($"Updated {ids.Count} driver(s).");
                showBatchModal = false;
                isBatchMode = false;
                selectedIds.Clear();
            }
        }
        finally
        {
            isSaving = false;
        }
    }

    private void OpenCreate()
    {
        isCreating = true;
        editingDriver = new DriverFormModel
        {
            DriverId = Guid.NewGuid(),
            DriverName = string.Empty,
            CarDisplayName = string.Empty,
            Car = string.Empty,
            Skin = string.Empty,
            IsActive = true
        };
    }

    private void OpenEdit(DriverModel driver)
    {
        isCreating = false;
        editingDriver = new DriverFormModel
        {
            DriverId = driver.DriverId,
            DriverName = driver.DriverName,
            CarDisplayName = driver.CarDisplayName,
            Car = driver.Car,
            Skin = driver.Skin,
            Nationality = driver.Nationality,
            Age = driver.Age,
            DriverAgression = driver.DriverAgression,
            DriverStrength = driver.DriverStrength,
            TeamId = driver.TeamId,
            TeamName = driver.TeamName,
            IsActive = driver.IsActive,
            IsHuman = driver.IsHuman
        };
    }

    private async Task OpenCarImageAsync(DriverModel driver)
    {
        viewingCar = driver;
        viewingCarDisplayName = string.IsNullOrWhiteSpace(driver.CarDisplayName)
            ? driver.Car
            : driver.CarDisplayName.Trim();

        try
        {
            var displayName = await CarContentService.GetCarDisplayNameAsync(driver.Car);

            if (viewingCar?.DriverId == driver.DriverId && !string.IsNullOrWhiteSpace(displayName))
            {
                viewingCarDisplayName = displayName;
            }
        }
        catch
        {
            // The folder id and any saved display name are still useful when AC metadata is unreadable.
        }
    }

    private void CloseCarImage()
    {
        viewingCar = null;
        viewingCarDisplayName = null;
    }

    private void RequestDeleteEditing()
    {
        if (editingDriver is null)
        {
            return;
        }

        var match = drivers?.FirstOrDefault(d => d.DriverId == editingDriver.DriverId);

        if (match is null)
        {
            Toast.Show("This driver is not loaded and cannot be deleted from here.", isError: true);
            return;
        }

        editingDriver = null;
        isCreating = false;
        confirmDeleteDriver = match;
    }

    private void CancelEdit()
    {
        if (isSaving)
        {
            return;
        }

        editingDriver = null;
        isCreating = false;
    }

    private async Task SaveDriverAsync()
    {
        if (editingDriver is null
            || !RequireText(editingDriver.DriverName, "Driver name is required.")
            || !RequireText(editingDriver.Car, "Car is required."))
        {
            return;
        }

        isSaving = true;

        try
        {
            var form = editingDriver;
            var creating = isCreating;

            var saved = await TryAsync(async () =>
            {
                if (creating)
                {
                    await DriverService.CreateDriverAsync(form);
                }
                else
                {
                    await DriverService.UpdateDriverAsync(form);
                }

                await LoadDriversAsync(resetView: creating);
            }, "Could not save driver");

            if (saved)
            {
                isSaving = false;
                DriverNotifier.NotifyChanged();
                Toast.Show(creating ? "Driver created." : "Driver updated.");
                CancelEdit();
            }
        }
        finally
        {
            isSaving = false;
        }
    }

    private async Task DeleteDriverAsync()
    {
        if (confirmDeleteDriver is null)
        {
            return;
        }

        isDeleting = true;

        try
        {
            var target = confirmDeleteDriver;

            await TryAsync(async () =>
            {
                await DriverService.DeleteDriverAsync(target.DriverId);
                confirmDeleteDriver = null;
                await LoadDriversAsync();
                DriverNotifier.NotifyChanged();
                Toast.Show($"Driver '{target.DriverName}' deleted.");
            }, "Could not delete driver");
        }
        finally
        {
            isDeleting = false;
        }
    }

    private async Task OpenPresetPicker()
    {
        if (isImporting)
        {
            return;
        }

        var config = await ConfigService.GetAsync();
        var path = ContentManagerPaths.ResolvePreset(config?.PresetPath);

        if (!Directory.Exists(path))
        {
            Toast.Show($"Content Manager's preset folder '{path}' does not exist. Install Content Manager to import a grid.", isError: true);
            return;
        }

        presetPath = path;
        presetPicker.Open(path);
    }

    private async Task ImportPresetFromPath(string? presetFilePath)
    {
        if (string.IsNullOrWhiteSpace(presetFilePath))
        {
            return;
        }

        isImporting = true;

        try
        {
            await TryAsync(async () =>
            {
                var result = await DriverService.ImportAssettoCorsaPresetFileAsync(presetFilePath);

                Toast.Show($"Imported {result.PresetName} — added {result.Added}, skipped {result.Skipped}.");

                await LoadDriversAsync();
                DriverNotifier.NotifyChanged();
            }, "Could not import preset");
        }
        finally
        {
            isImporting = false;
        }
    }

    public void Dispose()
    {
        ConfigNotifier.Changed -= OnConfigChanged;
    }
}
