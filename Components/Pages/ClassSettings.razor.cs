using Microsoft.AspNetCore.Components;
using MulticlassRace.ViewModels;
using MulticlassRace.Services.Settings;

namespace multi_class_race_dashboard.Components.Pages;

public partial class ClassSettings
{
    [Inject] private IClassSettingService ClassSettingService { get; set; } = default!;

    private List<ClassSettingModel>? settings;
    private ClassSettingEditor? editing;
    private bool isCreating;
    private bool isSaving;
    private bool editingIsCurrentDefault;
    private ClassSettingModel? confirmDelete;
    private bool isDeleting;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        settings = (await ClassSettingService.GetAllAsync()).ToList();
    }

    private void OpenCreate()
    {
        isCreating = true;
        editingIsCurrentDefault = settings is not null && settings.Count == 0;
        editing = ClassSettingEditor.CreateNew(editingIsCurrentDefault);
    }

    private void OpenEdit(ClassSettingModel setting)
    {
        isCreating = false;
        editingIsCurrentDefault = setting.IsDefault;
        editing = ClassSettingEditor.From(setting);
    }

    private void CancelEdit()
    {
        if (isSaving)
        {
            return;
        }

        editing = null;
        isCreating = false;
        editingIsCurrentDefault = false;
    }

    private async Task SaveAsync()
    {
        if (editing is null)
        {
            return;
        }

        var error = editing.Validate();
        if (error is not null)
        {
            Toast.Show(error, isError: true);
            return;
        }

        isSaving = true;

        try
        {
            var form = editing.ToForm();
            var saved = await TryAsync(async () =>
            {
                if (isCreating)
                {
                    await ClassSettingService.CreateAsync(form);
                }
                else
                {
                    await ClassSettingService.UpdateAsync(form);
                }

                await LoadAsync();
            }, "Could not save class setting");

            if (saved)
            {
                Toast.Show(isCreating ? "Class setting created." : "Class setting updated.");
            }
        }
        finally
        {
            isSaving = false;
            CancelEdit();
        }
    }

    private async Task DeleteAsync()
    {
        if (confirmDelete is null)
        {
            return;
        }

        isDeleting = true;

        try
        {
            var name = confirmDelete.ClassSettingName;
            await TryAsync(async () =>
            {
                await ClassSettingService.DeleteAsync(confirmDelete.ClassSettingId);
                Toast.Show($"Class setting '{name}' deleted.");
                confirmDelete = null;
                await LoadAsync();
            }, "Could not delete class setting");
        }
        finally
        {
            isDeleting = false;
        }
    }
}
