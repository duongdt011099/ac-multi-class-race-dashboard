using Microsoft.AspNetCore.Components;
using MulticlassRace.Services.Abstractions;
using MulticlassRace.ViewModels;

namespace multi_class_race_dashboard.Components.Pages;

public partial class PointSettings
{
    [Inject] private IPointSettingService PointSettingService { get; set; } = default!;

    private List<PointSettingModel>? settings;
    private PointSettingFormModel? editingSetting;
    private bool isCreating;
    private bool isSaving;
    private PointSettingModel? confirmDelete;
    private bool isDeleting;

    private PointSettingModel? DefaultSetting => settings?.FirstOrDefault(s => s.IsDefault);

    private string? DefaultSettingName => DefaultSetting?.SettingName;

    private bool CanToggleDefault =>
        DefaultSetting is null ||
        (editingSetting is not null && editingSetting.SettingId == DefaultSetting.SettingId);

    protected override async Task OnInitializedAsync()
    {
        await LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        settings = (await PointSettingService.GetAllAsync()).ToList();
    }

    private void OpenCreate()
    {
        isCreating = true;
        editingSetting = PointSettingForms.CreateNew(isDefault: settings is null || !settings.Any(s => s.IsDefault));
    }

    private void OpenEdit(PointSettingModel setting)
    {
        isCreating = false;
        editingSetting = PointSettingForms.From(setting);
    }

    private void CancelEdit()
    {
        if (isSaving)
        {
            return;
        }

        editingSetting = null;
        isCreating = false;
    }

    private void AddEntry() => editingSetting?.AddEntry();

    private void RemoveEntry(int index) => editingSetting?.Entries.RemoveAt(index);

    private async Task SaveSettingAsync()
    {
        if (editingSetting is null || !RequireText(editingSetting.SettingName, "Setting name is required."))
        {
            return;
        }

        isSaving = true;

        try
        {
            var form = editingSetting;
            var saved = await TryAsync(async () =>
            {
                if (isCreating)
                {
                    await PointSettingService.CreateAsync(form);
                }
                else
                {
                    await PointSettingService.UpdateAsync(form);
                }

                await LoadSettingsAsync();
            });

            if (saved)
            {
                Toast.Show(isCreating ? "Point setting created." : "Point setting updated.");
            }
        }
        finally
        {
            isSaving = false;
            CancelEdit();
        }
    }

    private async Task DeleteSettingAsync()
    {
        if (confirmDelete is null)
        {
            return;
        }

        isDeleting = true;

        try
        {
            var target = confirmDelete;
            await TryAsync(async () =>
            {
                await PointSettingService.DeleteAsync(target.SettingId);
                confirmDelete = null;
                await LoadSettingsAsync();
                Toast.Show($"Point setting '{target.SettingName}' deleted.");
            });
        }
        finally
        {
            isDeleting = false;
        }
    }
}
